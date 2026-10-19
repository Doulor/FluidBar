using FluidBar.Core.Activities;
using FluidBar.Core.Layout;
using FluidBar.Core.Motion;

namespace FluidBar.App.Hosting.Skia;

/// <summary>Everything the shell springs need to compute their targets.</summary>
/// <param name="Form">Target shell form.</param>
/// <param name="Mode">Presentation mode (selects dock geometry).</param>
/// <param name="Anchor">Anchor within the work area.</param>
/// <param name="CompactSize">Core compact size token for the active activity.</param>
/// <param name="GhostCount">Live ghost layer count (stacking strategy already applied).</param>
/// <param name="HideTarget">Whether the shell fades out.</param>
public readonly record struct SkiaMotionTarget(
    ShellForm Form,
    IslandMode Mode,
    IslandAnchor Anchor,
    VisualSize CompactSize,
    int GhostCount,
    bool HideTarget)
{
    /// <summary>Hidden target for a mode/anchor.</summary>
    /// <param name="mode">Presentation mode.</param>
    /// <param name="anchor">Anchor within the work area.</param>
    public static SkiaMotionTarget Hidden(IslandMode mode, IslandAnchor anchor) => new(
        ShellForm.Hidden,
        mode,
        anchor,
        LayoutTokens.CompactSize(null),
        0,
        true);
}

/// <summary>
/// Avalonia-free shell spring driver for the Skia island host. It mirrors the existing
/// <c>ShellMotionDriver</c> math (width/height/hover/press/nudge/hide, radii blend, content opacity,
/// dock growth) but depends only on Core types, so the island path carries no UI framework.
/// The host steps it; nothing here owns a timer or touches a window.
/// </summary>
public sealed class SkiaIslandMotion : IDisposable
{
    private const double RetargetDeltaSeconds = 1.0 / 120.0;

    private readonly SpringSet _springs = new();
    private readonly Spring _width;
    private readonly Spring _height;
    private readonly Spring _hover;
    private readonly Spring _press;
    private readonly Spring _nudge;
    private readonly Spring _hide;

    private MotionProfile _profile;
    private SkiaMotionTarget _target;
    private double _targetWidth;
    private double _targetHeight;

    /// <summary>Creates the driver with a motion profile (reduced motion / strength).</summary>
    /// <param name="profile">Motion profile; defaults to <see cref="MotionProfile.Default"/>.</param>
    public SkiaIslandMotion(MotionProfile? profile = null)
    {
        _profile = profile ?? MotionProfile.Default;
        _width = _springs.Add(new Spring(_profile.ResolveParameters(MotionTokens.Expand), LayoutTokens.CompactSize(null).Width));
        _height = _springs.Add(new Spring(_profile.ResolveParameters(MotionTokens.ExpandHeight), LayoutTokens.CompactSize(null).Height));
        _hover = _springs.Add(new Spring(_profile.ResolveParameters(MotionTokens.Hover), 0));
        _press = _springs.Add(new Spring(_profile.ResolveParameters(MotionTokens.Press), 0));
        _nudge = _springs.Add(new Spring(_profile.ResolveParameters(MotionTokens.Nudge), 0));
        _hide = _springs.Add(new Spring(_profile.ResolveParameters(MotionTokens.Hide), 1));
        _target = SkiaMotionTarget.Hidden(IslandMode.Float, IslandAnchor.TopCenter);
        _width.SnapTo(LayoutTokens.CompactSize(null).Width);
        _height.SnapTo(LayoutTokens.CompactSize(null).Height);
        _targetWidth = _width.Value;
        _targetHeight = _height.Value;
    }

    /// <summary>Current logical target.</summary>
    public SkiaMotionTarget Target => _target;

    /// <summary>Whether every spring is at rest (the frame loop can stop).</summary>
    public bool IsAtRest => _springs.AllAtRest;

    /// <summary>Reduced-motion profile flag (renderer waveform/parallax).</summary>
    public bool ReducedMotion => _profile.ReducedMotion;

    /// <summary>Current shell width in DIP (includes the peek delta).</summary>
    public double Width => Math.Max(0, _width.Value + (_hover.Value * LayoutTokens.PeekDeltaWidth));

    /// <summary>Current shell height in DIP (includes the peek delta).</summary>
    public double Height => Math.Max(0, _height.Value + (_hover.Value * LayoutTokens.PeekDeltaHeight));

    /// <summary>Whole-shell opacity (hide spring).</summary>
    public double Opacity => Math.Clamp(_hide.Value, 0, 1);

    /// <summary>Press scale about the shell centre (1 = not pressed).</summary>
    public double PressScale => 1.0 - (Math.Clamp(_press.Value, 0, 1) * 0.03);

    /// <summary>Transient vertical nudge in DIP.</summary>
    public double NudgeOffsetY => _nudge.Value;

    /// <summary>0..1 interpolation from the compact token height to the expanded height.</summary>
    public double ExpandedFactor
    {
        get
        {
            var compact = Math.Clamp(_target.CompactSize.Height, 24, LayoutTokens.ExpandedHeight);
            var range = Math.Max(1, LayoutTokens.ExpandedHeight - compact);
            return Math.Clamp((Height - compact) / range, 0, 1);
        }
    }

    /// <summary>Compact content opacity derived from the shell height.</summary>
    public double CompactContentOpacity => ContentOpacityPolicy.ForCompactContent(Height, LayoutTokens.ExpandedHeight);

    /// <summary>Expanded content opacity derived from the shell height.</summary>
    public double ExpandedContentOpacity => ContentOpacityPolicy.ForExpandedContent(Height, LayoutTokens.ExpandedHeight);

    /// <summary>Whether the current visual height is closer to the expanded panel.</summary>
    public bool LooksExpanded => _target.Form == ShellForm.Expanded
        || (_target.Form == ShellForm.Closing && ExpandedContentOpacity >= 0.5);

    /// <summary>Shell top-left inside the host surface, in DIP.</summary>
    public VisualPoint ShellOrigin
    {
        get
        {
            var mode = _target.Mode;
            var host = HostSurface.ForMode(mode);
            var width = Width;
            var height = Height;
            var x = _target.Anchor switch
            {
                IslandAnchor.TopLeft or IslandAnchor.BottomLeft when mode != IslandMode.Dock => host.SidePadding,
                IslandAnchor.TopRight or IslandAnchor.BottomRight when mode != IslandMode.Dock
                    => host.Width - host.SidePadding - width,
                _ => host.SidePadding + ((LayoutTokens.ExpandedWidth - width) / 2),
            };

            if (mode == IslandMode.Dock)
            {
                // The dock strip is the compact shell; the expanded panel grows upwards from it.
                var stripTop = host.DockStripTopLocal;
                var panelTop = host.TopPadding;
                var y = stripTop + ((panelTop - stripTop) * ExpandedFactor);
                return new VisualPoint(x, y);
            }

            var bottomAnchored = _target.Anchor is IslandAnchor.BottomLeft or IslandAnchor.BottomCenter
                or IslandAnchor.BottomRight;
            var originY = bottomAnchored
                ? host.TopPadding + LayoutTokens.ExpandedHeight - height
                : host.TopPadding;
            return new VisualPoint(x, originY);
        }
    }

    /// <summary>Expanded panel radius from settings (DIP); compact pills keep their stadium radius.</summary>
    public double PanelRadius { get; set; } = LayoutTokens.ExpandedRadius;

    /// <summary>Per-corner radii blended between the compact and expanded tokens by height.</summary>
    public CornerRadii ShellRadii
    {
        get
        {
            var mode = _target.Mode;
            var compactRadii = LayoutTokens.RadiiFor(ShellForm.Compact, mode, Height);
            var expandedRadii = LayoutTokens.RadiiFor(ShellForm.Expanded, mode, LayoutTokens.ExpandedHeight, PanelRadius);
            var t = Math.Clamp(ExpandedFactor, 0, 1);
            return new CornerRadii(
                Lerp(compactRadii.TopLeft, expandedRadii.TopLeft, t),
                Lerp(compactRadii.TopRight, expandedRadii.TopRight, t),
                Lerp(compactRadii.BottomRight, expandedRadii.BottomRight, t),
                Lerp(compactRadii.BottomLeft, expandedRadii.BottomLeft, t));
        }
    }

    /// <summary>Applies a new logical target and retargets the springs.</summary>
    /// <param name="target">New target.</param>
    public void SetTarget(in SkiaMotionTarget target)
    {
        var first = _target.Form == ShellForm.Hidden && target.Form != ShellForm.Hidden;
        _target = target;

        var compactW = Math.Clamp(target.CompactSize.Width, 60, LayoutTokens.ExpandedWidth);
        var compactH = Math.Clamp(target.CompactSize.Height, 24, LayoutTokens.ExpandedHeight);
        var wantsExpanded = target.Form == ShellForm.Expanded;
        var baseWidth = wantsExpanded ? LayoutTokens.ExpandedWidth : compactW;
        var baseHeight = wantsExpanded ? LayoutTokens.ExpandedHeight : compactH;

        var grew = baseWidth > _targetWidth + 0.5 || baseHeight > _targetHeight + 0.5;
        var shrank = baseWidth < _targetWidth - 0.5 || baseHeight < _targetHeight - 0.5;
        if (grew)
        {
            _width.SetParameters(_profile.ResolveParameters(MotionTokens.Expand));
            _height.SetParameters(_profile.ResolveParameters(MotionTokens.ExpandHeight));
        }
        else if (shrank)
        {
            var collapse = _profile.ResolveParameters(MotionTokens.Collapse);
            _width.SetParameters(collapse);
            _height.SetParameters(collapse);
        }

        if (first)
        {
            // Hidden -> visible is instantaneous (spec T1: no expand animation on reveal).
            _width.SnapTo(baseWidth);
            _height.SnapTo(baseHeight);
            _hide.SnapTo(1);
            _hover.SnapTo(0);
        }

        _targetWidth = baseWidth;
        _targetHeight = baseHeight;
        _width.Retarget(baseWidth, RetargetDeltaSeconds);
        _height.Retarget(baseHeight, RetargetDeltaSeconds);
        _hover.Retarget(target.Form == ShellForm.Peek ? 1 : 0, RetargetDeltaSeconds);
        _hide.Retarget(target.HideTarget ? 0 : 1, RetargetDeltaSeconds);
    }

    /// <summary>Snaps every spring to its target (mode/DPI change, restore, reduced motion).</summary>
    public void SnapToTarget()
    {
        var compactW = Math.Clamp(_target.CompactSize.Width, 60, LayoutTokens.ExpandedWidth);
        var compactH = Math.Clamp(_target.CompactSize.Height, 24, LayoutTokens.ExpandedHeight);
        var wantsExpanded = _target.Form == ShellForm.Expanded;
        _width.SnapTo(wantsExpanded ? LayoutTokens.ExpandedWidth : compactW);
        _height.SnapTo(wantsExpanded ? LayoutTokens.ExpandedHeight : compactH);
        _hover.SnapTo(_target.Form == ShellForm.Peek ? 1 : 0);
        _press.SnapTo(0);
        _nudge.SnapTo(0);
        _hide.SnapTo(_target.HideTarget ? 0 : 1);
        _targetWidth = _width.Value;
        _targetHeight = _height.Value;
    }

    /// <summary>Applies a new motion profile (reduced motion / strength changed).</summary>
    /// <param name="profile">New profile.</param>
    public void UpdateProfile(MotionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _profile = profile;
        _width.SetParameters(profile.ResolveParameters(MotionTokens.Expand));
        _height.SetParameters(profile.ResolveParameters(MotionTokens.ExpandHeight));
        _hover.SetParameters(profile.ResolveParameters(MotionTokens.Hover));
        _press.SetParameters(profile.ResolveParameters(MotionTokens.Press));
        _nudge.SetParameters(profile.ResolveParameters(MotionTokens.Nudge));
        _hide.SetParameters(profile.ResolveParameters(MotionTokens.Hide));
    }

    /// <summary>Sets the press feedback target (0.97 scale).</summary>
    /// <param name="pressed">Whether the pointer is down.</param>
    public void SetPressed(bool pressed)
    {
        if (!_profile.AllowsPressScale && pressed)
        {
            return;
        }

        _press.Retarget(pressed ? 1 : 0, RetargetDeltaSeconds);
    }

    /// <summary>Plays the one-shot −6 DIP vertical nudge (0 under reduced motion).</summary>
    public void PlayNudge()
    {
        var amplitude = _profile.NudgeAmplitude(6.0);
        if (amplitude <= 0)
        {
            return;
        }

        _nudge.SnapTo(-amplitude);
        _nudge.Retarget(0, RetargetDeltaSeconds);
    }

    /// <summary>Steps every spring by a frame delta; returns whether motion continues.</summary>
    /// <param name="deltaSeconds">Frame delta in seconds.</param>
    public bool Step(double deltaSeconds)
    {
        if (deltaSeconds <= 0)
        {
            return !_springs.AllAtRest;
        }

        _springs.Step(deltaSeconds);
        return !_springs.AllAtRest;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _springs.Clear();
    }

    private static double Lerp(double from, double to, double t) => from + ((to - from) * t);
}
