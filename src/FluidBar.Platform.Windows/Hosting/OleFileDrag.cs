using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using FluidBar.Platform.Windows.Interop;

namespace FluidBar.Platform.Windows.Hosting;

/// <summary>Native Copy-only source for explicit file references. Never opens, moves or deletes source files.</summary>
public static class OleFileDrag
{
    internal const uint Copy = 1;
    internal const int Drop = 0x00040100;
    internal const int Cancel = 0x00040101;
    private const int DefaultCursors = 0x00040102;
    private const int FormatUnavailable = unchecked((int)0x80040064);
    private const int AdviseUnsupported = unchecked((int)0x80040003);

    /// <summary>Runs on the UI STA. A hook-originated press supplies its physical release signal.</summary>
    public static bool DragFiles(IReadOnlyList<string> paths, Func<bool>? released = null)
        => DragFiles(paths, released, null);

    /// <summary>Uses Shell-managed drag imagery so feedback remains visible outside the source window.</summary>
    public static bool DragFiles(IReadOnlyList<string> paths, Func<bool>? released, FileDragImage? image)
    {
        Marshal.ThrowExceptionForHR(NativeMethods.Ole32.OleInitialize(IntPtr.Zero));
        try
        {
            using var data = ShellFileDragData.Create(paths, image);
            using var feedback = image is not null ? new ShellDragImageFeedback(data.Data) : null;
            var source = new FileDropSource(released, feedback is null ? null : feedback.Update);
            var dataPointer = Marshal.GetComInterfaceForObject(data.Data, typeof(IDataObject));
            try
            {
                var sourcePointer = Marshal.GetComInterfaceForObject(source, typeof(IDropSource));
                try
                {
                    NativeMethods.User32.ReleaseCapture();
                    var hr = NativeMethods.Shell32.SHDoDragDrop(IntPtr.Zero, dataPointer, sourcePointer, Copy, out var effect);
                    Marshal.ThrowExceptionForHR(hr);
                    return hr == Drop && (effect & Copy) != 0;
                }
                finally { Marshal.Release(sourcePointer); }
            }
            finally { Marshal.Release(dataPointer); }
        }
        finally { NativeMethods.Ole32.OleUninitialize(); }
    }

    internal static IDataObject CreateDataObject(IReadOnlyList<string> paths)
    {
        var snapshot = paths.ToArray();
        if (snapshot.Length is 0 or > 32 || snapshot.Any(path => string.IsNullOrWhiteSpace(path)
                || path.Length > 2048 || path.Contains('\0') || !Path.IsPathFullyQualified(path)))
            throw new ArgumentException("File drag requires 1–32 bounded absolute file references.", nameof(paths));
        return new FileDataObject(snapshot.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    internal sealed class FileDropSource(Func<bool>? released, Action<uint>? feedback = null) : IDropSource
    {
        public int QueryContinueDrag(bool escape, uint keys)
            => escape ? Cancel : (released?.Invoke() ?? (keys & 1) == 0) ? Drop : 0;
        public int GiveFeedback(uint effect)
        {
            feedback?.Invoke(effect);
            return DefaultCursors;
        }
    }

    [ComImport, ComVisible(true)]
    [Guid("00000121-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDropSource
    {
        [PreserveSig] int QueryContinueDrag([MarshalAs(UnmanagedType.Bool)] bool escape, uint keys);
        [PreserveSig] int GiveFeedback(uint effect);
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class FileDataObject : IDataObject
    {
        private readonly byte[] _files;
        private readonly FORMATETC[] _formats;

        public FileDataObject(string[] paths)
        {
            var encoded = Encoding.Unicode.GetBytes(string.Join('\0', paths) + "\0\0");
            _files = new byte[20 + encoded.Length];
            BinaryPrimitives.WriteInt32LittleEndian(_files, 20);
            BinaryPrimitives.WriteInt32LittleEndian(_files.AsSpan(16), 1);
            encoded.CopyTo(_files, 20);
            var preferred = NativeMethods.User32.RegisterClipboardFormatW("Preferred DropEffect");
            if (preferred == 0) throw new COMException("Could not register Preferred DropEffect.");
            _formats = [Format(15), Format(unchecked((short)preferred))];
        }

        private static FORMATETC Format(short id) => new()
        { cfFormat = id, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_HGLOBAL };

        public int QueryGetData(ref FORMATETC format)
        {
            var id = format.cfFormat;
            return _formats.Any(item => item.cfFormat == id)
                && format.dwAspect == DVASPECT.DVASPECT_CONTENT && format.lindex == -1
                && (format.tymed & TYMED.TYMED_HGLOBAL) != 0 ? 0 : FormatUnavailable;
        }

        public void GetData(ref FORMATETC format, out STGMEDIUM medium)
        {
            var hr = QueryGetData(ref format);
            if (hr != 0) throw new COMException("Unsupported file drag format.", hr);
            var bytes = format.cfFormat == 15 ? _files : new byte[] { 1, 0, 0, 0 };
            var memory = NativeMethods.Kernel32.GlobalAlloc(0x0042, (UIntPtr)bytes.Length);
            if (memory == IntPtr.Zero) throw new OutOfMemoryException();
            try
            {
                var pointer = NativeMethods.Kernel32.GlobalLock(memory);
                if (pointer == IntPtr.Zero) throw new OutOfMemoryException();
                try { Marshal.Copy(bytes, 0, pointer, bytes.Length); }
                finally { NativeMethods.Kernel32.GlobalUnlock(memory); }
            }
            catch { NativeMethods.Kernel32.GlobalFree(memory); throw; }
            medium = new STGMEDIUM { tymed = TYMED.TYMED_HGLOBAL, unionmember = memory };
        }

        public void GetDataHere(ref FORMATETC format, ref STGMEDIUM medium)
            => throw new COMException("Caller-provided media are unsupported.", FormatUnavailable);
        public int GetCanonicalFormatEtc(ref FORMATETC input, out FORMATETC output)
        { output = input; output.ptd = IntPtr.Zero; return 0x00040130; }
        public void SetData(ref FORMATETC format, ref STGMEDIUM medium, bool release)
        {
            // A target may report a performed effect. Source references are never removed, even for Move.
            if (release) NativeMethods.Ole32.ReleaseStgMedium(ref medium);
        }
        public IEnumFORMATETC EnumFormatEtc(DATADIR direction)
            => new FormatEnumerator(direction == DATADIR.DATADIR_GET ? _formats : []);
        public int DAdvise(ref FORMATETC format, ADVF flags, IAdviseSink sink, out int connection)
        { connection = 0; return AdviseUnsupported; }
        public void DUnadvise(int connection) => throw new COMException("Advises are unsupported.", AdviseUnsupported);
        public int EnumDAdvise(out IEnumSTATDATA? enumerator)
        { enumerator = null; return AdviseUnsupported; }
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class FormatEnumerator(FORMATETC[] formats) : IEnumFORMATETC
    {
        private int _index;
        public int Next(int count, FORMATETC[] destination, int[]? fetched)
        {
            if (count < 0 || count > destination.Length) throw new ArgumentOutOfRangeException(nameof(count));
            var read = Math.Min(count, formats.Length - _index);
            Array.Copy(formats, _index, destination, 0, read);
            _index += read;
            if (fetched is { Length: > 0 }) fetched[0] = read;
            return read == count ? 0 : 1;
        }
        public int Skip(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            var skipped = Math.Min(count, formats.Length - _index);
            _index += skipped;
            return skipped == count ? 0 : 1;
        }
        public int Reset() { _index = 0; return 0; }
        public void Clone(out IEnumFORMATETC clone) => clone = new FormatEnumerator(formats) { _index = _index };
    }
}
