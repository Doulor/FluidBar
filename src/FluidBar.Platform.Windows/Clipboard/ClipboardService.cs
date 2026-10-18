using System.Collections.Immutable;
using System.Runtime.InteropServices;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Interop;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Windows.Clipboard;

/// <summary>
/// Clipboard listener service: <c>AddClipboardFormatListener</c> on a platform message window, reads
/// the clipboard off the UI thread with open-retry handling, suppresses duplicates by sequence number
/// and payload, keeps a bounded history and supports writing text back.
/// </summary>
/// <remarks>
/// v1 attached the listener to the main WPF window (<c>AttachWindow</c> hack); v2 owns its own
/// message-only window so no plugin/window coupling exists. No timer polls the clipboard.
/// </remarks>
public sealed class ClipboardService : PlatformMonitor<ClipboardSnapshot>, IClipboardSource
{
    private const uint WM_CLIPBOARDUPDATE = NativeMethods.WM_CLIPBOARDUPDATE;
    private const uint DragQueryFileCount = 0xFFFFFFFF; // DragQueryFile: request the file count

    private readonly PlatformMessageHost _host;
    private readonly ClipboardServiceOptions _options;
    private readonly IPlatformLogger _logger;
    private readonly PlatformCounters _counters;
    private readonly TimeProvider _timeProvider;
    private readonly object _debounceGate = new();
    private PlatformWindow? _window;
    private ClipboardHistory? _history;
    private ITimer? _debounceTimer;
    private uint _lastSequence;
    private int _readInFlight;

    public ClipboardService(PlatformMessageHost host, IPlatformLogger logger, PlatformCounters counters,
        ClipboardServiceOptions? options = null, TimeProvider? timeProvider = null)
        : base("clipboard", logger, counters, timeProvider ?? TimeProvider.System)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _options = options ?? new ClipboardServiceOptions();
        _logger = logger ?? NullPlatformLogger.Instance;
        _counters = counters ?? new PlatformCounters();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Bounded history (newest first) from the latest snapshot.</summary>
    public IReadOnlyList<ClipboardEntry> GetHistory() => Current?.History ?? ImmutableArray<ClipboardEntry>.Empty;

    protected override async ValueTask OnStartAsync(CancellationToken cancellationToken)
    {
        _history = new ClipboardHistory(_options.MaxHistoryEntries);
        _lastSequence = 0;
        _window = await _host.CreateWindowAsync("clipboard", PlatformWindowKind.MessageOnly,
            OnWindowMessage, cancellationToken).ConfigureAwait(false);
        var handle = _window.Handle;
        var registered = await _host.InvokeAsync(() => NativeMethods.User32.AddClipboardFormatListener(handle),
            cancellationToken).ConfigureAwait(false);
        if (!registered)
        {
            _counters.IncrementWin32Failures();
            throw new InvalidOperationException(
                "AddClipboardFormatListener failed. " + Win32Error.Format(Win32Error.LastError));
        }

        await ReadClipboardSafeAsync(isInitial: true, cancellationToken).ConfigureAwait(false);
        _logger.LogInfo(PlatformLogCategory.Clipboard, "clipboard.started", "Clipboard listener started.");
    }

    private IntPtr? OnWindowMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message != WM_CLIPBOARDUPDATE)
        {
            return null;
        }

        _counters.IncrementClipboardEvents();
        ScheduleRead();
        return IntPtr.Zero;
    }

    private void ScheduleRead()
    {
        lock (_debounceGate)
        {
            _debounceTimer?.Dispose();
            // Small debounce: clipboard owners often set several formats in quick succession.
            _debounceTimer = _timeProvider.CreateTimer(static state =>
            {
                var self = (ClipboardService)state!;
                _ = self.ReadClipboardSafeAsync(isInitial: false, CancellationToken.None);
            }, this, TimeSpan.FromMilliseconds(30), Timeout.InfiniteTimeSpan);
        }
    }

    private async Task ReadClipboardSafeAsync(bool isInitial, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _readInFlight, 1) != 0)
        {
            return;
        }

        try
        {
            var result = await ReadClipboardWithRetryAsync(isInitial, cancellationToken).ConfigureAwait(false);
            if (result is not null)
            {
                Publish(result);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _counters.IncrementClipboardReadFailures();
            _logger.LogError(PlatformLogCategory.Clipboard, "clipboard.read-failed",
                "Clipboard read failed.", ex);
        }
        finally
        {
            Volatile.Write(ref _readInFlight, 0);
        }
    }

    private async Task<ClipboardSnapshot?> ReadClipboardWithRetryAsync(bool isInitial, CancellationToken cancellationToken)
    {
        var attempts = Math.Clamp(_options.OpenClipboardAttempts, 1, 100);
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var (opened, snapshot) = await Task.Run(() => TryReadOnce(isInitial), cancellationToken)
                .ConfigureAwait(false);
            if (opened)
            {
                return snapshot;
            }

            if (attempt < attempts - 1)
            {
                await Task.Delay(_options.OpenClipboardRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        _counters.IncrementClipboardReadFailures();
        _logger.LogWarning(PlatformLogCategory.Clipboard, "clipboard.open-failed",
            "Could not open the clipboard after retries; another process is holding it.");
        return null;
    }

    /// <summary>Single open/read/close cycle on one thread; returns whether the clipboard could be opened.</summary>
    private (bool Opened, ClipboardSnapshot? Snapshot) TryReadOnce(bool isInitial)
    {
        var sequence = NativeMethods.User32.GetClipboardSequenceNumber();
        if (sequence == 0)
        {
            return (true, isInitial ? ClipboardSnapshot.Empty : null);
        }

        if (!isInitial && sequence == _lastSequence)
        {
            return (true, null);
        }

        if (!NativeMethods.User32.OpenClipboard(IntPtr.Zero))
        {
            return (false, null);
        }

        try
        {
            var entry = CaptureEntry(sequence);
            _lastSequence = sequence;
            if (entry is null)
            {
                return (true, isInitial ? ClipboardSnapshot.Empty : null);
            }

            var history = _history;
            if (history is null)
            {
                return (true, null);
            }

            return history.TryAdd(entry, out var snapshot)
                ? (true, snapshot)
                : (true, DedupeAndReturnNull());
        }
        finally
        {
            NativeMethods.User32.CloseClipboard();
        }
    }

    private ClipboardSnapshot? DedupeAndReturnNull()
    {
        _counters.IncrementClipboardDeduped();
        return null;
    }

    private ClipboardEntry? CaptureEntry(uint sequence)
    {
        var timestamp = _timeProvider.GetUtcNow();
        var maxText = Math.Max(1, _options.MaxTextCharacters);
        if (NativeMethods.User32.IsClipboardFormatAvailable(NativeMethods.CF_UNICODETEXT))
        {
            var text = ReadUnicodeText();
            if (text is not null)
            {
                var originalLength = text.Length;
                var stored = originalLength > maxText ? text[..maxText] : text;
                return new ClipboardEntry(sequence, timestamp, ClipboardContentKind.Text, stored, originalLength,
                    ImmutableArray<string>.Empty);
            }
        }

        if (NativeMethods.User32.IsClipboardFormatAvailable(NativeMethods.CF_HDROP))
        {
            var files = ReadFileDrop();
            if (files.Length > 0)
            {
                return new ClipboardEntry(sequence, timestamp, ClipboardContentKind.Files, null, 0, files);
            }
        }

        if (NativeMethods.User32.IsClipboardFormatAvailable(NativeMethods.CF_BITMAP)
            || NativeMethods.User32.IsClipboardFormatAvailable(NativeMethods.CF_DIB))
        {
            return new ClipboardEntry(sequence, timestamp, ClipboardContentKind.Image, null, 0,
                ImmutableArray<string>.Empty);
        }

        return new ClipboardEntry(sequence, timestamp, ClipboardContentKind.Unknown, null, 0,
            ImmutableArray<string>.Empty);
    }

    private static string? ReadUnicodeText()
    {
        var handle = NativeMethods.User32.GetClipboardData(NativeMethods.CF_UNICODETEXT);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var pointer = NativeMethods.Kernel32.GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(pointer);
        }
        finally
        {
            NativeMethods.Kernel32.GlobalUnlock(pointer);
        }
    }

    private ImmutableArray<string> ReadFileDrop()
    {
        var handle = NativeMethods.User32.GetClipboardData(NativeMethods.CF_HDROP);
        if (handle == IntPtr.Zero)
        {
            return ImmutableArray<string>.Empty;
        }

        var count = NativeMethods.Shell32.DragQueryFileW(handle, DragQueryFileCount, null, 0);
        if (count == 0)
        {
            return ImmutableArray<string>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<string>((int)Math.Min(count, (uint)_options.MaxFilePaths));
        for (uint index = 0; index < count && builder.Count < _options.MaxFilePaths; index++)
        {
            var length = NativeMethods.Shell32.DragQueryFileW(handle, index, null, 0);
            if (length == 0 || length > 4096)
            {
                continue;
            }

            var buffer = new char[length + 1];
            var written = NativeMethods.Shell32.DragQueryFileW(handle, index, buffer, (uint)buffer.Length);
            if (written > 0)
            {
                builder.Add(new string(buffer, 0, (int)written));
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>Forces a clipboard read (e.g. after the App regains focus). Change-only.</summary>
    public Task TriggerReadAsync(CancellationToken cancellationToken = default)
        => ReadClipboardSafeAsync(isInitial: false, cancellationToken);

    /// <summary>Copies text back to the clipboard (used by the island's "copy verification code").</summary>
    public async Task<bool> SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        var attempts = Math.Clamp(_options.OpenClipboardAttempts, 1, 100);
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var (opened, written) = await Task.Run(() => TryWriteOnce(text), cancellationToken)
                .ConfigureAwait(false);
            if (opened)
            {
                return written;
            }

            if (attempt < attempts - 1)
            {
                await Task.Delay(_options.OpenClipboardRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        _counters.IncrementClipboardReadFailures();
        _logger.LogWarning(PlatformLogCategory.Clipboard, "clipboard.write-open-failed",
            "Could not open the clipboard for writing.");
        return false;
    }

    private static (bool Opened, bool Written) TryWriteOnce(string text)
    {
        if (!NativeMethods.User32.OpenClipboard(IntPtr.Zero))
        {
            return (false, false);
        }

        try
        {
            if (!NativeMethods.User32.EmptyClipboard())
            {
                return (true, false);
            }

            var bytes = System.Text.Encoding.Unicode.GetBytes(text + "\0");
            var nativeMemory = NativeMethods.Kernel32.GlobalAlloc(NativeMethods.GMEM_MOVEABLE,
                (UIntPtr)bytes.Length);
            if (nativeMemory == IntPtr.Zero)
            {
                return (true, false);
            }

            var pointer = NativeMethods.Kernel32.GlobalLock(nativeMemory);
            if (pointer == IntPtr.Zero)
            {
                NativeMethods.Kernel32.GlobalFree(nativeMemory);
                return (true, false);
            }

            try
            {
                Marshal.Copy(bytes, 0, pointer, bytes.Length);
            }
            finally
            {
                NativeMethods.Kernel32.GlobalUnlock(nativeMemory);
            }

            if (NativeMethods.User32.SetClipboardData(NativeMethods.CF_UNICODETEXT, nativeMemory) == IntPtr.Zero)
            {
                NativeMethods.Kernel32.GlobalFree(nativeMemory);
                return (true, false);
            }

            return (true, true);
        }
        finally
        {
            NativeMethods.User32.CloseClipboard();
        }
    }

    /// <summary>Clears the bounded history (does not touch the system clipboard).</summary>
    public void ClearHistory()
    {
        _history?.Clear();
        Publish(ClipboardSnapshot.Empty);
    }

    protected override async ValueTask OnStopAsync(CancellationToken cancellationToken)
    {
        lock (_debounceGate)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }

        var window = _window;
        _window = null;
        if (window is not null)
        {
            try
            {
                await _host.InvokeAsync(() => NativeMethods.User32.RemoveClipboardFormatListener(window.Handle),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(PlatformLogCategory.Clipboard, "clipboard.unregister-failed",
                    "Failed to remove the clipboard format listener.", ex);
            }

            await window.DisposeAsync().ConfigureAwait(false);
        }

        _history = null;
        _logger.LogInfo(PlatformLogCategory.Clipboard, "clipboard.stopped", "Clipboard listener stopped.");
    }
}
