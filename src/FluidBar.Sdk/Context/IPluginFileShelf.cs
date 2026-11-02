namespace FluidBar.Sdk;

/// <summary>Host-owned file-reference UI; plugins supply data, never their own UI objects.</summary>
public interface IPluginFileShelf
{
    /// <summary>
    /// Opens a non-modal shelf. The host accepts explicit file drops/picks and emits reference-only
    /// changes. Disposing the session or cancelling its lifetime closes the UI and releases callbacks.
    /// Source files are never moved, renamed or deleted; dragging out offers copy semantics only.
    /// </summary>
    IDisposable Show(IReadOnlyList<string> paths, Action<IReadOnlyList<string>> changed, CancellationToken lifetime);

    /// <summary>Opts into file drops on the island; disposing disables the registration.</summary>
    IDisposable? RegisterIslandDropTarget(Action<IReadOnlyList<string>> dropped, CancellationToken lifetime) => null;

    /// <summary>Copies explicit file references to the system clipboard for pasting into another app.
    /// Returns false if any reference is unavailable or the clipboard cannot be written. Never moves files.</summary>
    ValueTask<bool> CopyFilesAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken) => ValueTask.FromResult(false);
}
