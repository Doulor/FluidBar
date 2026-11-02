using FluidBar.Core.Diagnostics;

namespace FluidBar.App.Hosting.Skia;

public sealed partial class SkiaIslandHost
{
    private Action<IReadOnlyList<string>>? _fileDropHandlers;
    private bool _nativeFileDragActive;

    private void StartNativeFileDrag(WindowEntry entry, bool fromHook)
    {
        var request = _controller.TakeFileDrag();
        if (request is null) return;
        FluidBar.Platform.Windows.Hosting.IslandMouseHook.NativeDragScope? input = null;
        _hookPressed = _hookDragActive = false;
        _hookDragKind = SkiaDragKind.None;
        _hookDragWindow = null;
        entry.DragKind = SkiaDragKind.None;
        entry.DragActive = false;
        try
        {
            if (_mouseHook is { } hook && (fromHook || hook.IsInstalled))
            {
                input = fromHook ? hook.TrySuspendForNativeDrag() : hook.SuspendForNativeDrag();
                if (input is null) return; // The physical release beat the queued move.
            }
            _nativeFileDragActive = true;
            foreach (var window in _windows) window.Host.SetFileDropEnabled(false);
            if (entry.Host.Handle != IntPtr.Zero) RenderNow(forceFull: true);
            Func<bool>? released = input is null ? null : () => input.LeftButtonReleased;
            if (_options.FileDrag is { } drag) drag(request.Paths, released);
            else
            {
                var path = request.Paths[0];
                var title = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
                var hint = _options.Localizer.Format(FluidBar.App.Localization.LocKeys.Island.FileDrag.CopyHint, ("count", request.Paths.Count));
                using var bitmap = FluidBar.Island.FileDragPreviewRenderer.Render(_controller.RenderState.Theme,
                    string.IsNullOrEmpty(title) ? path : title, hint, (float)SafeScale(entry.Host.Scale));
                var image = new FluidBar.Platform.Windows.Hosting.FileDragImage(bitmap.Width, bitmap.Height,
                    bitmap.Bytes, -16, -16);
                FluidBar.Platform.Windows.Hosting.OleFileDrag.DragFiles(request.Paths, released, image);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(FluidBar.Core.Diagnostics.LogCategories.Render, "island.file-drag.failed",
                "The native file copy drag could not be started.", ex);
        }
        finally
        {
            input?.Dispose();
            _nativeFileDragActive = false;
            foreach (var window in _windows) window.Host.SetFileDropEnabled(_fileDropHandlers is not null);
            _controller.CompleteFileDrag();
            _controller.HandlePointerReleased(_hookPressX, _hookPressY);
            _controller.HandlePointerLeft();
            EnsureDeadlineTicker();
            EnsureFrameLoop();
            if (entry.Host.Handle != IntPtr.Zero) RenderNow(forceFull: true);
        }
    }

    public IDisposable RegisterFileDropHandler(Action<IReadOnlyList<string>> handler)
    {
        var registration = new FileDropRegistration(this, handler);
        _dispatcher.Post(() =>
        {
            if (_disposed || registration.IsDisposed) return;
            _fileDropHandlers += handler;
            foreach (var entry in _windows) entry.Host.SetFileDropEnabled(true);
        });
        return registration;
    }

    private sealed class FileDropRegistration(SkiaIslandHost host, Action<IReadOnlyList<string>> handler) : IDisposable
    {
        private int _disposed;
        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            host._dispatcher.Post(() =>
            {
                host._fileDropHandlers -= handler;
                foreach (var entry in host._windows) entry.Host.SetFileDropEnabled(host._fileDropHandlers is not null);
            });
        }
    }
}
