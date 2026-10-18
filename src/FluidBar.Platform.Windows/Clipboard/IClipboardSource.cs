using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Windows.Clipboard;

/// <summary>Clipboard contract consumed by first-party plugins. Implemented by <see cref="ClipboardService"/>.</summary>
public interface IClipboardSource
{
    ClipboardSnapshot? Current { get; }

    event EventHandler<SnapshotEventArgs<ClipboardSnapshot>>? SnapshotChanged;

    IReadOnlyList<ClipboardEntry> GetHistory();

    Task<bool> SetTextAsync(string text, CancellationToken cancellationToken = default);

    void ClearHistory();
}
