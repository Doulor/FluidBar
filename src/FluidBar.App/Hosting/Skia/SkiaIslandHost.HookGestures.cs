using FluidBar.Core.Layout;
using FluidBar.Platform.Windows.Hosting;

namespace FluidBar.App.Hosting.Skia;

public sealed partial class SkiaIslandHost
{
    private bool _hookPressed;
    private SkiaDragKind _hookDragKind;
    private float _hookPressX;
    private float _hookPressY;

    private void OnHookPointerPressed(float screenX, float screenY)
        => OnHookPointerPressedWithModifiers(screenX, screenY, LayeredKeyModifiers.None);

    private void OnHookPointerPressedWithModifiers(float screenX, float screenY, LayeredKeyModifiers modifiers)
    {
        _dispatcher.Post(() =>
        {
            if (_disposed) return;
            var entry = FindHookWindow(screenX, screenY);
            if (entry is null) return;
            MapHookPoint(entry, screenX, screenY, out var x, out var y);
            _hookPressed = true;
            _hookDragWindow = entry;
            _hookPressX = _hookDragX = x;
            _hookPressY = _hookDragY = y;
            entry.PositionReturn.Suspend();
            entry.PersistDrag = (modifiers & LayeredKeyModifiers.Control) != 0;
            entry.DragActive = false;
            entry.DragStartScreenXDip = screenX / SafeScale(entry.Host.Scale);
            entry.DragStartScreenYDip = screenY / SafeScale(entry.Host.Scale);
            _controller.HandlePointerPressed(x, y, entry.PersistDrag);
            // Classify at the press origin, not at the first moved point.
            _hookDragKind = _controller.HasPendingFileDrag ? SkiaDragKind.FileCandidate : !entry.PersistDrag && _controller.RenderState.Form == ShellForm.Expanded
                ? SkiaDragKind.None : _controller.HandleDragStarted(x, y, NowMs());
            _hookDragActive = false;
            EnsureDeadlineTicker();
            EnsureFrameLoop();
        });
    }

    private void OnHookPointerReleased(float screenX, float screenY)
    {
        _dispatcher.Post(() =>
        {
            if (_disposed) return;
            var entry = _hookDragWindow ?? FindHookWindow(screenX, screenY);
            if (entry is not null)
            {
                MapHookPoint(entry, screenX, screenY, out var x, out var y);
                _controller.HandlePointerReleased(x, y);
                if (_hookDragKind == SkiaDragKind.NotificationCandidate)
                {
                    if (_hookDragActive)
                        _controller.HandleNotificationDragEnded(y, NowMs(), screenY <= entry.Monitor.Bounds.Y + 2);
                    else
                        _controller.CancelNotificationDragCandidate();
                }
                else if (_hookDragKind == SkiaDragKind.WindowCandidate && entry.DragActive)
                {
                    EndWindowDrag(entry, cancelled: false);
                    entry.DragActive = false;
                }
                else if (_hookDragActive)
                    _controller.HandleDragEnded(x, y, NowMs());
                if (!entry.DragActive) entry.PositionReturn.Resume();
            }
            _hookPressed = false;
            _hookDragActive = false;
            _hookDragKind = SkiaDragKind.None;
            _hookDragWindow = null;
            EnsureDeadlineTicker();
            EnsureFrameLoop();
        });
    }

    private void OnHookPointerMoved(float screenX, float screenY)
        => PostHookPointer(screenX, screenY, static (controller, x, y) => controller.HandlePointerMoved(x, y));

    private void OnHookPointerDragged(float screenX, float screenY)
    {
        _dispatcher.Post(() =>
        {
            if (_disposed || !_hookPressed || _hookDragWindow is not { } entry) return;
            MapHookPoint(entry, screenX, screenY, out var x, out var y);
            _hookDragX = x;
            _hookDragY = y;
            if (_hookDragKind == SkiaDragKind.FileCandidate
                && SkiaDragThreshold.Exceeds(_hookPressX, _hookPressY, x, y))
            {
                StartNativeFileDrag(entry, fromHook: true);
                return;
            }
            if (_hookDragKind == SkiaDragKind.None
                && SkiaDragThreshold.Exceeds(_hookPressX, _hookPressY, x, y))
            {
                _hookDragKind = _controller.HandleDragStarted(_hookPressX, _hookPressY, NowMs());
                _hookDragActive = _hookDragKind == SkiaDragKind.Pager;
            }
            if (_hookDragKind == SkiaDragKind.NotificationCandidate)
            {
                if (!_hookDragActive && SkiaDragThreshold.Exceeds(_hookPressX, _hookPressY, x, y))
                    _hookDragActive = _controller.BeginNotificationDrag();
                if (_hookDragActive)
                    _controller.HandleNotificationDragMoved(y, NowMs());
            }
            else if (_hookDragKind == SkiaDragKind.WindowCandidate)
            {
                var scale = SafeScale(entry.Host.Scale);
                var pointer = new LayeredPointerEvent((int)(x * scale), (int)(y * scale), x, y,
                    (int)screenX, (int)screenY, LayeredPointerButtons.Left,
                    entry.PersistDrag ? LayeredKeyModifiers.Control : LayeredKeyModifiers.None, NowMs());
                var drag = new LayeredDragEvent(LayeredDragPhase.Moved, pointer,
                    (int)(screenX - entry.DragStartScreenXDip * scale),
                    (int)(screenY - entry.DragStartScreenYDip * scale), 0, 0);
                MoveWindowForDrag(entry, drag);
                _hookDragActive = entry.DragActive;
                UpdateHitRegions(force: true);
            }
            else if (_hookDragKind == SkiaDragKind.Pager && _hookDragActive)
                _controller.HandleDragMoved(x, y, NowMs());
            EnsureFrameLoop();
        });
    }
}
