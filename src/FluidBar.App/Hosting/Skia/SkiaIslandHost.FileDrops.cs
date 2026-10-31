namespace FluidBar.App.Hosting.Skia;

public sealed partial class SkiaIslandHost
{
    private Action<IReadOnlyList<string>>? _fileDropHandlers;

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
