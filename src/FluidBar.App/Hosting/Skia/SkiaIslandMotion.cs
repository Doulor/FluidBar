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
/// Avalonia-free shell motion driver for the Skia island host.
/// <para>
/// The expanded shell is a <b>CSS transition</b>, not a spring: prototype2 declares
/// <c>transition: width var(--dur-shell) var(--ease-shell), height var(--dur-shell) var(--ease-shell),
/// border-radius 260ms var(--ease-out)</c> with <c>--dur-shell: 380ms</c> and
/// <c>--ease-shell: cubic-bezier(0.3, 0.8, 0.25, 1)</c>. The same CSS file fades the three layers with
/// their own timings (compact 90 ms ease-in on expand / 140 ms ease-out + 40 ms delay on collapse, the
/// expanded layer 220 ms ease-out + 90 ms delay in and 140 ms ease-out out, the chrome 160 ms ease-out
/// with the same 90 ms expand delay). <see cref="CssTransition"/> reproduces those semantics exactly, so
/// the per-frame trajectory matches the browser samples recorded by Playwright (see the Ui tests).
/// </para>
/// <para>
/// Springs remain only where the prototype has no CSS transition to copy: the hover/peek growth, press
/// scale, the new-activity nudge and the hide fade. Nothing here owns a timer or touches a window; the
/// host steps it.
/// </para>
/// </summary>
public sealed class SkiaIslandMotion : IDisposable
{
    private const double RetargetDeltaSeconds = 1.0 / 120.0;

    private readonly SpringSet _springs = new();
    private readonly Spring _hover;
    private readonly Spring _press;
    private readonly Spring _nudge;
    private readonly Spring _hide;

    // Prototype2 shell geometry is time-based.
    private readonly CssTransition _width;
    private readonly CssTransition _height;
    private readonly CssTransition _radiusBlend;
    private readonly CssTransition _compactOpacity;
    private readonly CssTransition _expandedOpacity;
    private readonly CssTransition _chromeOpacity;

    private MotionProfile _profile;
    private SkiaMotionTarget _target;
    private bool _hasAppeared;
    private bool _wasExpanded;

    /// <summary>Creates the driver with a motion profile (reduced motion / strength).</summary>
    /// <param name="profile">Motion profile; defaults to <see cref="MotionProfile.Default"/>.</param>
    public SkiaIslandMotion(MotionProfile? profile = null)
    {
        _profile = profile ?? MotionProfile.Default;
        _hover = _springs.Add(new Spring(_profile.ResolveParameters(MotionTokens.Hover), 0));
        _press = _springs.Add(new Spring(_profile.ResolveParameters(MotionTokens.Press), 0));
        _nudge = _springs.Add(new Spring(_profile.ResolveParameters(MotionTokens.Nudge), 0));
        _hide = _springs.Add(new Spring(_profile.ResolveParameters(MotionTokens.Hide), 1));

        var compact = LayoutTokens.CompactSize(null);
        _width = new CssTransition(compact.Width);
        _height = new CssTransition(compact.Height);
        _radiusBlend = new CssTransition(0);
        _compactOpacity = new CssTransition(1);
        _expandedOpacity = new CssTransition(0);
        _chromeOpacity = new CssTransition(0);

        _target = SkiaMotionTarget.Hidden(IslandMode.Float, IslandAnchor.TopCenter);
    }

    /// <summary>Current logical target.</summary>
    public SkiaMotionTarget Target => _target;

    /// <summary>Whether every spring and transition is at rest (the frame loop can stop).</summary>
    public bool IsAtRest => _springs.AllAtRest && AllTransitionsAtRest;

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
            var compact = CompactTargetHeight;
            var range = Math.Max(1, LayoutTokens.ExpandedHeight - compact);
            return Math.Clamp((Height - compact) / range, 0, 1);
        }
    }

    /// <summary>
    /// Compact layer opacity (prototype2 <c>.compact-layer</c>): 1 at rest, fades to 0 over 90 ms ease-in
    /// when the panel opens, and back over 140 ms ease-out with a 40 ms delay when it collapses.
    /// </summary>
    public double CompactContentOpacity => Math.Clamp(_compactOpacity.Value, 0, 1);

    /// <summary>
    /// Expanded layer opacity (prototype2 <c>.expanded-layer</c>): 220 ms ease-out with a 90 ms delay on
    /// expand, 140 ms ease-out with no delay on collapse.
    /// </summary>
    public double ExpandedContentOpacity => Math.Clamp(_expandedOpacity.Value, 0, 1);

    /// <summary>
    /// Chrome (page title + collapse control) opacity (prototype2 <c>.pager-title</c> /
    /// <c>.pager-collapse</c>): 160 ms ease-out, delayed 90 ms on expand only.
    /// </summary>
    public double ChromeOpacity => Math.Clamp(_chromeOpacity.Value, 0, 1);

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
    public double PanelRadius { get; set; } = 30;

    /// <summary>Per-corner radii blended between the compact and expanded tokens by the 260 ms radius tween.</summary>
    public CornerRadii ShellRadii
    {
        get
        {
            var mode = _target.Mode;
            var compactRadii = LayoutTokens.RadiiFor(ShellForm.Compact, mode, CompactTargetHeight);
            var expandedRadii = LayoutTokens.RadiiFor(
                ShellForm.Expanded, mode, LayoutTokens.ExpandedHeight, PanelRadius);
            var t = Math.Clamp(_radiusBlend.Value, 0, 1);
            return new CornerRadii(
                Lerp(compactRadii.TopLeft, expandedRadii.TopLeft, t),
                Lerp(compactRadii.TopRight, expandedRadii.TopRight, t),
                Lerp(compactRadii.BottomRight, expandedRadii.BottomRight, t),
                Lerp(compactRadii.BottomLeft, expandedRadii.BottomLeft, t));
        }
    }

    /// <summary>Applies a new logical target and retargets the springs/transitions.</summary>
    /// <param name="target">New target.</param>
    public void SetTarget(in SkiaMotionTarget target)
    {
        var first = !_hasAppeared && target.Form != ShellForm.Hidden;
        _hasAppeared |= target.Form != ShellForm.Hidden;
        _target = target;

        var compactW = Math.Clamp(target.CompactSize.Width, 60, LayoutTokens.ExpandedWidth);
        var compactH = Math.Clamp(target.CompactSize.Height, 24, LayoutTokens.ExpandedHeight);
        var wantsExpanded = target.Form == ShellForm.Expanded;
        var baseWidth = wantsExpanded ? LayoutTokens.ExpandedWidth : compactW;
        var baseHeight = wantsExpanded ? LayoutTokens.ExpandedHeight : compactH;

        if (first)
        {
            // Hidden -> visible is instantaneous (spec T1: no expand animation on reveal).
            SnapGeometry(baseWidth, baseHeight, wantsExpanded);
            _hide.SnapTo(1);
            _hover.SnapTo(0);
        }
        else if (_profile.ReducedMotion)
        {
            SnapGeometry(baseWidth, baseHeight, wantsExpanded);
        }
        else
        {
            _width.Retarget(baseWidth, ShellTransition.ShellDurationSeconds, ShellTransition.ShellEasing);
            _height.Retarget(baseHeight, ShellTransition.ShellDurationSeconds, ShellTransition.ShellEasing);

            if (wantsExpanded != _wasExpanded)
            {
                if (wantsExpanded)
                {
                    _compactOpacity.Retarget(
                        0, ShellTransition.CompactFadeOutSeconds, ShellTransition.EaseIn);
                    _expandedOpacity.Retarget(
                        1, ShellTransition.ExpandedFadeInSeconds, ShellTransition.EaseOut,
                        ShellTransition.ExpandedFadeInDelaySeconds);
                    _chromeOpacity.Retarget(
                        1, ShellTransition.ChromeFadeSeconds, ShellTransition.EaseOut,
                        ShellTransition.ChromeFadeInDelaySeconds);
                }
                else
                {
                    _compactOpacity.Retarget(
                        1, ShellTransition.CompactFadeInSeconds, ShellTransition.EaseOut,
                        ShellTransition.CompactFadeInDelaySeconds);
                    _expandedOpacity.Retarget(
                        0, ShellTransition.ExpandedFadeOutSeconds, ShellTransition.EaseOut);
                    _chromeOpacity.Retarget(0, ShellTransition.ChromeFadeSeconds, ShellTransition.EaseOut);
                }

                _radiusBlend.Retarget(
                    wantsExpanded ? 1 : 0, ShellTransition.RadiusDurationSeconds, ShellTransition.EaseOut);
            }
        }

        _wasExpanded = wantsExpanded;
        _hover.Retarget(target.Form == ShellForm.Peek ? 1 : 0, RetargetDeltaSeconds);
        _hide.Retarget(target.HideTarget ? 0 : 1, RetargetDeltaSeconds);
    }

    /// <summary>Snaps every spring and transition to its target (mode/DPI change, restore, reduced motion).</summary>
    public void SnapToTarget()
    {
        var wantsExpanded = _target.Form == ShellForm.Expanded;
        var compactW = Math.Clamp(_target.CompactSize.Width, 60, LayoutTokens.ExpandedWidth);
        var compactH = Math.Clamp(_target.CompactSize.Height, 24, LayoutTokens.ExpandedHeight);
        SnapGeometry(wantsExpanded ? LayoutTokens.ExpandedWidth : compactW,
            wantsExpanded ? LayoutTokens.ExpandedHeight : compactH, wantsExpanded);
        _hover.SnapTo(_target.Form == ShellForm.Peek ? 1 : 0);
        _press.SnapTo(0);
        _nudge.SnapTo(0);
        _hide.SnapTo(_target.HideTarget ? 0 : 1);
        _wasExpanded = wantsExpanded;
    }

    /// <summary>Applies a new motion profile (reduced motion / strength changed).</summary>
    /// <param name="profile">New profile.</param>
    public void UpdateProfile(MotionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var wasReduced = _profile.ReducedMotion;
        _profile = profile;
        _hover.SetParameters(profile.ResolveParameters(MotionTokens.Hover));
        _press.SetParameters(profile.ResolveParameters(MotionTokens.Press));
        _nudge.SetParameters(profile.ResolveParameters(MotionTokens.Nudge));
        _hide.SetParameters(profile.ResolveParameters(MotionTokens.Hide));
        if (!wasReduced && profile.ReducedMotion)
        {
            SnapToTarget();
        }
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

    /// <summary>Steps every spring and transition by a frame delta; returns whether motion continues.</summary>
    /// <param name="deltaSeconds">Frame delta in seconds.</param>
    public bool Step(double deltaSeconds)
    {
        if (deltaSeconds <= 0)
        {
            return !IsAtRest;
        }

        _springs.Step(deltaSeconds);
        _width.Step(deltaSeconds);
        _height.Step(deltaSeconds);
        _radiusBlend.Step(deltaSeconds);
        _compactOpacity.Step(deltaSeconds);
        _expandedOpacity.Step(deltaSeconds);
        _chromeOpacity.Step(deltaSeconds);
        return !IsAtRest;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _springs.Clear();
    }

    private bool AllTransitionsAtRest => _width.IsAtRest && _height.IsAtRest && _radiusBlend.IsAtRest
        && _compactOpacity.IsAtRest && _expandedOpacity.IsAtRest && _chromeOpacity.IsAtRest;

    private double CompactTargetHeight => Math.Clamp(_target.CompactSize.Height, 24, LayoutTokens.ExpandedHeight);

    private void SnapGeometry(double width, double height, bool expanded)
    {
        _width.SnapTo(width);
        _height.SnapTo(height);
        _radiusBlend.SnapTo(expanded ? 1 : 0);
        _compactOpacity.SnapTo(expanded ? 0 : 1);
        _expandedOpacity.SnapTo(expanded ? 1 : 0);
        _chromeOpacity.SnapTo(expanded ? 1 : 0);
    }

    private static double Lerp(double from, double to, double t) => from + ((to - from) * t);
}
