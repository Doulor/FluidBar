using FluidBar.Island;
using FluidBar.Core.Layout;

namespace FluidBar.App.Hosting.Skia;

public sealed partial class SkiaIslandController
{
    private int _contentScrollPage = -1;
    private double _contentScrollTarget;
    private int _contentScrollDirection;
    private (string? Page, uint? Detail, int Filter, bool Files, double Font) _contentScrollContext;
    private int _contentScrollDragPage = -1;
    private float _contentScrollGrab;

    public bool HasContentScrollDrag => _contentScrollDragPage >= 0;
    public bool IsContentScrollAnimating => _contentScrollPage >= 0 && _contentScrollPage == _pager.SelectedIndex
        && _renderState.Form == ShellForm.Expanded && Math.Abs(_contentScrollTarget - _pageScroll[_contentScrollPage]) > 0.1;

    private (string?, uint?, int, bool, double) ContentScrollContext()
        => (_pages.PageSlotAt(_pager.SelectedIndex)?.PageId, _pages.Notifications.Detail?.Id,
            _pages.Notifications.Filter, _pages.Clipboard.FilesOnly, _settings.Current.FontScale);

    private ContentScrollGeometry ScrollGeometry(int page)
        => ContentScrollLayout.Measure(_pages, page, IslandMetrics.PageRect(_renderState, page),
            FontScaleMetrics.From(_settings.Current.FontScale));

    private void QueueContentScroll(int page, double delta, double maximum)
    {
        var context = ContentScrollContext();
        var direction = Math.Sign(delta);
        if (_contentScrollPage != page || _contentScrollContext != context || direction != _contentScrollDirection)
            _contentScrollTarget = _pageScroll[page];
        _contentScrollPage = page;
        _contentScrollContext = context;
        _contentScrollDirection = direction;
        _contentScrollTarget = Math.Clamp(_contentScrollTarget + delta, 0, maximum);
        // Precision touchpads already supply a continuous stream; do not add a second inertia layer.
        var blend = _renderState.ReducedMotion || Math.Abs(delta) <= 12 ? 1 : 0.25;
        _pageScroll[page] += (_contentScrollTarget - _pageScroll[page]) * blend;
    }

    private bool StepContentScroll(double seconds)
    {
        if (_contentScrollPage < 0) return false;
        if (_contentScrollPage != _pager.SelectedIndex || _contentScrollContext != ContentScrollContext()
            || _renderState.Form != ShellForm.Expanded)
        {
            _contentScrollPage = -1;
            return false;
        }
        var page = _contentScrollPage;
        _contentScrollTarget = Math.Clamp(_contentScrollTarget, 0, MaxScrollForPage(page));
        var distance = _contentScrollTarget - _pageScroll[page];
        if (Math.Abs(distance) <= 0.1) return false;
        var next = _pageScroll[page] + distance * (1 - Math.Exp(-Math.Min(seconds, 0.1) / 0.045));
        _pageScroll[page] = Math.Abs(_contentScrollTarget - next) < 0.1 ? _contentScrollTarget : next;
        _pages = ApplyPageScroll(_pages);
        return true;
    }

    private void BeginContentScrollDrag(int page, float pointerY)
    {
        var geometry = ScrollGeometry(page);
        _contentScrollPage = -1;
        _contentScrollDragPage = page;
        _contentScrollGrab = geometry.Thumb.Top <= pointerY && pointerY <= geometry.Thumb.Bottom
            ? pointerY - geometry.Thumb.Top : geometry.Thumb.Height / 2;
        _pressedControl = IslandPageControl.Scrollbar;
        _pressedPage = page;
        MoveContentScrollDrag(pointerY);
    }

    private void MoveContentScrollDrag(float pointerY)
    {
        var page = _contentScrollDragPage;
        if (page < 0 || page != _pager.SelectedIndex) { _contentScrollDragPage = -1; return; }
        var next = ScrollGeometry(page).OffsetAt(pointerY, _contentScrollGrab);
        if (Math.Abs(next - _pageScroll[page]) < 0.001) return;
        _pageScroll[page] = next;
        _pages = ApplyPageScroll(_pages);
        BuildRenderState();
        InvalidateDirty(ContentDamage());
    }
}
