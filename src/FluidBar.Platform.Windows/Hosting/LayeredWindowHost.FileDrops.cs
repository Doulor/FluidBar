using FluidBar.Platform.Windows.Interop;

namespace FluidBar.Platform.Windows.Hosting;

public sealed partial class LayeredWindowHost
{
    public event Action<IReadOnlyList<string>>? FilesDropped;

    /// <summary>Raised when a file drag enters this host, before the drop.</summary>
    public event Action? FileDragEntered;

    /// <summary>Raised when a file drag leaves/cancels this host.</summary>
    public event Action? FileDragLeft;

    /// <summary>Enables legacy shell file drops only while an optional consumer is registered.</summary>
    public void SetFileDropEnabled(bool enabled)
    {
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.Shell32.DragAcceptFiles(_hwnd, enabled);
            EnableOleFileDrop(enabled);
        }
    }

    private IntPtr ConsumeFileDrop(IntPtr drop)
    {
        try
        {
            var files = ReadDropFiles(drop);
            if (files.Count > 0) FilesDropped?.Invoke(files);
        }
        finally { NativeMethods.Shell32.DragFinish(drop); }
        return IntPtr.Zero;
    }

    private static IReadOnlyList<string> ReadDropFiles(IntPtr drop)
    {
        var count = Math.Min(NativeMethods.Shell32.DragQueryFileW(drop, uint.MaxValue, null, 0), 32u);
        List<string> files = [];
        for (uint index = 0; index < count; index++)
        {
            var length = NativeMethods.Shell32.DragQueryFileW(drop, index, null, 0);
            if (length is 0 or > 2048) continue;
            var buffer = new char[length + 1];
            var read = NativeMethods.Shell32.DragQueryFileW(drop, index, buffer, (uint)buffer.Length);
            if (read > 0) files.Add(new string(buffer, 0, (int)read));
        }
        return files;
    }
}
