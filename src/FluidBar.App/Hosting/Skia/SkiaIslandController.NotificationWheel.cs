using FluidBar.Core.Activities;
using FluidBar.Core.Interaction;
using FluidBar.Core.Layout;
using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.App.Hosting.Skia;

public sealed partial class SkiaIslandController
{
    private ActivityRecord? _presentedNotification;
    private ActivityRecord? _notificationCandidateActivity;
    private bool _notificationCandidatePending;
    private ActivityRecord? _notificationGestureActivity;
    private bool _notificationWheelActive;
    private double _notificationWheelNotches;
    private long _notificationWheelAtMs;
    private const int NotificationWheelPauseMs = 650;

    // Keep the original card envelope while it moves: the stationary cursor must receive notch two.
    internal SKRect NotificationInputEnvelope
    {
        get
        {
            var state = _renderState;
            var shell = IslandMetrics.ShellRect(state);
            if (state.Compact?.Kind != ActivityKind.Notification || _notificationDragKey is null)
                return shell;
            var original = IslandMetrics.ShellRect(state with { NotificationDragOffsetY = 0 });
            return SKRect.Union(shell, original);
        }
    }

    private void ObservePresentedNotification(ActivityRecord? record)
    {
        _presentedNotification = record;
        if (_notificationCandidatePending && !ReferenceEquals(record, _notificationCandidateActivity))
            CancelPendingClick();
        if (_notificationGestureActivity is not null && !ReferenceEquals(record, _notificationGestureActivity))
            ResetNotificationDrag();
    }

    private bool TryHandleNotificationWheel(double deltaDip, bool horizontal, bool shift)
    {
        if (horizontal || shift || !double.IsFinite(deltaDip) || Math.Abs(deltaDip) < 0.0001
            || State.Kind is not (IslandStateKind.Compact or IslandStateKind.Peek)
            || _renderState.Form is not (ShellForm.Compact or ShellForm.Peek)
            || _compact.Kind != ActivityKind.Notification || !_pointerInside
            || !NotificationInputEnvelope.Contains((float)_pointerX, (float)_pointerY))
            return false;

        if (_notificationDismissing || _notificationDragActive) return true;
        StepNotificationWheelTimeout();
        if (!_notificationWheelActive)
        {
            if (deltaDip >= 0) return true;
            // Wheel starts a fresh gesture, independent of a completed mouse candidate.
            _notificationCandidatePending = false;
            if (!BeginNotificationDrag()) return true;
            _notificationDragActive = false;
            _notificationWheelActive = true;
            _notificationWheelNotches = 0;
        }

        CancelPendingClick();
        _notificationWheelAtMs = NowMs();
        _notificationWheelNotches = Math.Clamp(_notificationWheelNotches
            - deltaDip / PagerConstants.WheelDipPerNotch, 0, 2);
        _notificationDismissing = _notificationWheelNotches >= 2 - 0.000001;
        _notificationDismissSpring.SetParameters(NotificationSpringParameters(_notificationDismissing));
        var target = _notificationDismissing ? NotificationDismissGesture.DismissTargetDip
            : NotificationDismissGesture.OffsetYDip(_notificationWheelNotches * 36);
        _notificationDismissSpring.Retarget(target, 0.016);
        if (_notificationDismissing || _notificationWheelNotches <= 0)
            _notificationWheelActive = false;
        InvalidateFull();
        FrameRequested?.Invoke();
        return true;
    }

    private void StepNotificationWheelTimeout()
    {
        if (!_notificationWheelActive || NowMs() - _notificationWheelAtMs < NotificationWheelPauseMs) return;
        _notificationWheelActive = false;
        _notificationWheelNotches = 0;
        _notificationDismissSpring.SetParameters(NotificationSpringParameters(dismissing: false));
        _notificationDismissSpring.Retarget(0, 0.016);
        InvalidateFull();
        FrameRequested?.Invoke();
    }
}
