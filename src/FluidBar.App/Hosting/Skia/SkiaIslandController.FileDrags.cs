using FluidBar.Island;

namespace FluidBar.App.Hosting.Skia;

public sealed partial class SkiaIslandController
{
    private PluginFileDragRequest? _pendingFileDrag;
    private double _filePressX;
    private double _filePressY;
    private bool _nativeFileDragActive;

    public bool HasPendingFileDrag => _pendingFileDrag is not null;

    private bool DeferFileRow(in IslandHitTarget hit, double x, double y)
    {
        var page = _renderState.Pages.PluginPageAt(hit.PageIndex);
        if (hit.Control != IslandPageControl.PluginRow || page is null
            || hit.Index < 0 || hit.Index >= page.Rows.Count || page.Rows[hit.Index].DragFiles is not { Count: > 0 } files)
            return false;
        _pendingFileDrag = new PluginFileDragRequest(hit.PageIndex, page.PageId, page.Rows[hit.Index].RowId, files.ToArray());
        _filePressX = x;
        _filePressY = y;
        return true;
    }

    private int CurrentFileRow(PluginFileDragRequest request)
    {
        var page = _renderState.Pages.PluginPageAt(request.PageIndex);
        if (page?.PageId != request.PageId) return -1;
        for (var i = 0; i < page.Rows.Count; i++)
            if (page.Rows[i].RowId == request.RowId && page.Rows[i].DragFiles is { } paths
                && paths.SequenceEqual(request.Paths, StringComparer.OrdinalIgnoreCase)) return i;
        return -1;
    }

    /// <summary>Transfers ownership to the native drag loop. The host calls this only after the drag threshold.</summary>
    public PluginFileDragRequest? TakeFileDrag()
    {
        var request = _pendingFileDrag;
        _pendingFileDrag = null;
        if (request is null || CurrentFileRow(request) < 0) return null;
        _nativeFileDragActive = true;
        _pressedControl = IslandPageControl.None;
        _pressedPage = _pressedIndex = -1;
        _motion.SetPressed(false);
        InvalidateFull();
        return request;
    }

    public void CompleteFileDrag()
    {
        _nativeFileDragActive = false;
        _pendingFileDrag = null;
    }

    private void CommitFileClick(double x, double y)
    {
        var request = _pendingFileDrag;
        _pendingFileDrag = null;
        if (request is null || SkiaDragThreshold.Exceeds(_filePressX, _filePressY, x, y)) return;
        var row = CurrentFileRow(request);
        var hit = SkiaIslandHitTest.Hit(_renderState, x, y);
        if (row >= 0 && hit.Control == IslandPageControl.PluginRow && hit.PageIndex == request.PageIndex && hit.Index == row)
            RaisePluginPageCommand(request.PageIndex, row, trailing: false);
    }
}
