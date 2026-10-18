using Avalonia;
using FluidBar.App.Island;
using FluidBar.App.Theme;
using FluidBar.Core.Activities;
using FluidBar.Core.Layout;
using FluidBar.Core.Motion;

namespace FluidBar.App.Rendering;

/// <summary>
/// Everything the shell needs to compute its motion targets: the logical form, the compact size token,
/// mode/anchor (to decide growth direction) and appearance. Derived from Core layout input by the host.
/// </summary>
/// <param name="Form">Logical shell form.</param>
/// <param name="Mode">Presentation mode.</param>
/// <param name="Anchor">Anchor position (decides vertical origin for bottom anchors).</param>
/// <param name="CompactSize">Compact size token for the active activity.</param>
/// <param name="GhostCount">Live ghost layer count.</param>
/// <param name="Palette">Island palette.</param>
/// <param name="FillOpacity">Shell fill opacity.</param>
/// <param name="IsAcrylic">Whether acrylic material is active.</param>
/// <param name="HideTarget">Whether the shell should fade out (hidden form).</param>
public readonly record struct ShellMotionTarget(
    ShellForm Form,
    IslandMode Mode,
    IslandAnchor Anchor,
    VisualSize CompactSize,
    int GhostCount,
    IslandColors Palette,
    double FillOpacity,
    bool IsAcrylic,
    bool HideTarget,
    double WindowHeightDip);

/// <summary>
/// Drives the two independent spring axes (width <c>motion.expand</c>, height
/// <c>motion.expand.height</c>, collapse <c>motion.collapse</c>), plus hover, press, nudge and hide
/// springs (spec 4.1/4.2/4.3). Only geometry inside the fixed window animates; this class never
/// touches a window handle. All springs share one fixed-step loop that stops when everything is at
/// rest ("settle → 0 frame callbacks").
/// </summary>
public sealed class ShellMotionDriver : IDisposable
{
    private readonly IFrameClock _clock;
    private readonly MotionProfile _profile;
    private readonly SpringSet _springs = new();
    private readonly Spring _width;
    private readonly Spring _height;
    private readonly Spring _hover;
    private readonly Spring _press;
    private readonly Spring _nudge;
    private readonly Spring _hide;

    private ShellMotionTarget _target = new(
        ShellForm.Hidden,
        IslandMode.Float,
        IslandAnchor.TopCenter,
        LayoutTokens.CompactSize(null),
        0,
        IslandColors.Obsidian,
        LayoutTokens.FillOpacity,
        false,
        true,
        LayoutTokens.WindowHeight);

    private ShellFrame _frame;
    private double _targetWidth;
    private double _targetHeight;
    private bool _settled = true;
    private bool _disposed;

    /// <summary>Creates a motion driver bound to a frame clock.</summary>
    /// <param name="clock">Compositor frame source (Avalonia in production, manual in tests).</param>
    /// <param name="profile">Reduced-motion/motion-strength profile.</param>
    public ShellMotionDriver(IFrameClock clock, MotionProfile? profile = null)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _profile = profile ?? MotionProfile.Default;

        _width = _springs.Add(new Spring(_profile.ResolveParameters(MotionTokens.Expand), LayoutTokens.CompactSize(null).Width));
        _height = _springs.Add(new Spring(_profile.ResolveParameters(MotionTokens.ExpandHeight), LayoutTokens.CompactSize(null).Height));
        _hover = _springs.Add(new Spring(_profile.ResolveParameters(MotionTokens.Hover), 0));
        _press = _springs.Add(new Spring(_profile.ResolveParameters(MotionTokens.Press), 0));
        _nudge = _springs.Add(new Spring(_profile.ResolveParameters(MotionTokens.Nudge), 0));
        _hide = _springs.Add(new Spring(_profile.ResolveParameters(MotionTokens.Hide), 1));

        _width.SnapTo(LayoutTokens.CompactSize(null).Width);
        _height.SnapTo(LayoutTokens.CompactSize(null).Height);
        _targetWidth = _width.Value;
        _targetHeight = _height.Value;
        _frame = BuildFrame();
    }

    /// <summary>Raised for every stepped frame with the new shell frame.</summary>
    public event Action<ShellFrame>? FrameUpdated;

    /// <summary>Raised once when every spring reached rest (window region/close-settled hook).</summary>
    public event EventHandler? Settled;

    /// <summary>Current visual frame.</summary>
    public ShellFrame Frame => _frame;

    /// <summary>Whether the frame loop is requesting frames.</summary>
    public bool IsRunning => _clock.IsRunning;

    /// <summary>Current logical target.</summary>
    public ShellMotionTarget Target => _target;

    /// <summary>Whether all springs are at rest.</summary>
    public bool IsAtRest => _springs.AllAtRest;

    /// <summary>Applies a new logical target (form change, activity change, settings, display change).</summary>
    public void SetTarget(in ShellMotionTarget target)
    {
        var first = _target.Form == ShellForm.Hidden && target.Form != ShellForm.Hidden;
        _target = target;

        var compactW = Math.Clamp(target.CompactSize.Width, 60, LayoutTokens.ExpandedWidth);
        var compactH = Math.Clamp(target.CompactSize.Height, 24, LayoutTokens.ExpandedHeight);
        var peekW = compactW + LayoutTokens.PeekDeltaWidth;
        var peekH = compactH + LayoutTokens.PeekDeltaHeight;

        var wantsExpanded = target.Form is ShellForm.Expanded;
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
            // Hidden → visible is instantaneous (spec T1: no expand animation on reveal).
            _width.SnapTo(baseWidth);
            _height.SnapTo(baseHeight);
            _hide.SnapTo(1);
            _hover.SnapTo(0);
        }

        _targetWidth = baseWidth;
        _targetHeight = baseHeight;
        _width.Retarget(baseWidth, 1.0 / 120.0);
        _height.Retarget(baseHeight, 1.0 / 120.0);
        _hover.Retarget(target.Form == ShellForm.Peek ? 1 : 0, 1.0 / 120.0);
        _hide.Retarget(target.HideTarget ? 0 : 1, 1.0 / 120.0);
        _settled = false;
        StartLoop();
        EmitFrame();
    }

    /// <summary>Snaps every spring to its target and zeroes velocity (T10 / restore / mode change).</summary>
    public void SnapToTarget()
    {
        var compactW = Math.Clamp(_target.CompactSize.Width, 60, LayoutTokens.ExpandedWidth);
        var compactH = Math.Clamp(_target.CompactSize.Height, 24, LayoutTokens.ExpandedHeight);
        var wantsExpanded = _target.Form is ShellForm.Expanded;
        _width.SnapTo(wantsExpanded ? LayoutTokens.ExpandedWidth : compactW);
        _height.SnapTo(wantsExpanded ? LayoutTokens.ExpandedHeight : compactH);
        _hover.SnapTo(_target.Form == ShellForm.Peek ? 1 : 0);
        _press.SnapTo(0);
        _nudge.SnapTo(0);
        _hide.SnapTo(_target.HideTarget ? 0 : 1);
        _targetWidth = _width.Value;
        _targetHeight = _height.Value;
        _settled = true;
        StopLoop();
        EmitFrame();
    }

    /// <summary>
    /// Applies a new motion profile (reduced-motion / strength settings changed) to every spring
    /// without recreating the loop or losing the current geometry.
    /// </summary>
    public void UpdateProfile(MotionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _width.SetParameters(profile.ResolveParameters(MotionTokens.Expand));
        _height.SetParameters(profile.ResolveParameters(MotionTokens.ExpandHeight));
        _hover.SetParameters(profile.ResolveParameters(MotionTokens.Hover));
        _press.SetParameters(profile.ResolveParameters(MotionTokens.Press));
        _nudge.SetParameters(profile.ResolveParameters(MotionTokens.Nudge));
        _hide.SetParameters(profile.ResolveParameters(MotionTokens.Hide));
    }

    /// <summary>Sets the press spring target (press scale 0.97).</summary>
    public void SetPressed(bool pressed)
    {
        if (!_profile.AllowsPressScale && pressed)
        {
            return;
        }

        _press.Retarget(pressed ? 1 : 0, 1.0 / 120.0);
        if (pressed)
        {
            _settled = false;
            StartLoop();
        }
    }

    /// <summary>Plays the one-shot −6 DIP vertical nudge (spec 4.7; amplitude 0 under reduced motion).</summary>
    public void PlayNudge()
    {
        var amplitude = _profile.NudgeAmplitude(6.0);
        if (amplitude <= 0)
        {
            return;
        }

        _nudge.SnapTo(-amplitude);
        _nudge.Retarget(0, 1.0 / 120.0);
        _settled = false;
        StartLoop();
    }

    /// <summary>Starts the frame loop (idempotent).</summary>
    public void StartLoop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_clock.IsRunning)
        {
            return;
        }

        _clock.Start(OnFrame);
    }

    /// <summary>Stops the frame loop (settle/hide/dispose).</summary>
    public void StopLoop()
    {
        if (!_disposed)
        {
            _clock.Stop();
        }
    }

    /// <summary>Steps the springs one frame; public for the manual-frame-clock tests.</summary>
    public void Step(TimeSpan delta)
    {
        OnFrame(delta);
    }

    /// <summary>Current animating width in DIP (diagnostics/tests).</summary>
    public double Width => _frame.Width;

    /// <summary>Current animating height in DIP (diagnostics/tests).</summary>
    public double Height => _frame.Height;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _clock.Stop();
        FrameUpdated = null;
        Settled = null;
    }

    private void OnFrame(TimeSpan delta)
    {
        _springs.Step(delta.TotalSeconds);
        EmitFrame();
        if (_springs.AllAtRest)
        {
            StopLoop();
            if (!_settled)
            {
                _settled = true;
                Settled?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void EmitFrame()
    {
        _frame = BuildFrame();
        FrameUpdated?.Invoke(_frame);
    }

    private ShellFrame BuildFrame()
    {
        var hover = _hover.Value;
        var width = Math.Max(0, _width.Value + (hover * LayoutTokens.PeekDeltaWidth));
        var height = Math.Max(0, _height.Value + (hover * LayoutTokens.PeekDeltaHeight));
        if (_target.Form is ShellForm.Expanded or ShellForm.Closing or ShellForm.Compact or ShellForm.Peek)
        {
            width = Math.Max(0, width);
        }

        var pressScale = 1.0 - (Math.Clamp(_press.Value, 0, 1) * 0.03);
        var nudge = _nudge.Value;
        var shellOpacity = Math.Clamp(_hide.Value, 0, 1);
        var hidden = shellOpacity <= 0.001 && _target.HideTarget;

        var expandedAlpha = ContentOpacityPolicy.ForExpandedContent(height, LayoutTokens.ExpandedHeight);
        var compactAlpha = ContentOpacityPolicy.ForCompactContent(height, LayoutTokens.ExpandedHeight);
        var ghostCount = expandedAlpha < 0.05 ? Math.Clamp(_target.GhostCount, 0, LayoutTokens.MaxGhosts) : 0;

        var (origin, t) = ComputeOrigin(width, height);
        var radii = ComputeRadii(height, t);
        return new ShellFrame(
            width,
            height,
            radii,
            origin.X,
            origin.Y,
            compactAlpha,
            expandedAlpha,
            shellOpacity,
            nudge,
            pressScale,
            ghostCount,
            _target.Palette,
            _target.FillOpacity,
            _target.IsAcrylic,
            hidden);
    }

    /// <summary>Shell origin inside the fixed host window plus the expanded interpolation factor.</summary>
    public (Point Origin, double ExpandedFactor) ComputeOrigin(double width, double height)
    {
        var mode = _target.Mode;
        var expanded = height > 0
            ? Math.Clamp(
                (height - _target.CompactSize.Height) / Math.Max(1, LayoutTokens.ExpandedHeight - _target.CompactSize.Height),
                0,
                1)
            : 0;

        var x = _target.Anchor switch
        {
            IslandAnchor.TopLeft or IslandAnchor.BottomLeft when mode != IslandMode.Dock => UiMetrics.WindowPadding,
            IslandAnchor.TopRight or IslandAnchor.BottomRight when mode != IslandMode.Dock
                => UiMetrics.WindowWidth - UiMetrics.WindowPadding - width,
            _ => UiMetrics.WindowPadding + ((LayoutTokens.ExpandedWidth - width) / 2),
        };

        if (mode == IslandMode.Dock)
        {
            // Dock window is 488x((52 strip + 8 gap + 284 panel) + 2*24) DIP; the strip sits above the
            // work-area bottom and the uniform panel grows upward from it. Both are fixed per mode;
            // nothing here touches the HWND during animation.
            var expandedT = Math.Clamp(
                (height - _target.CompactSize.Height) / Math.Max(1, LayoutTokens.ExpandedHeight - _target.CompactSize.Height),
                0,
                1);
            var stripTop = Theme.UiMetrics.WindowPadding + LayoutTokens.ExpandedHeight + LayoutTokens.DockExpandedGap;
            var panelTop = Theme.UiMetrics.WindowPadding;
            var dockOriginY = stripTop + ((panelTop - stripTop) * expandedT);
            return (new Point(x, dockOriginY), expanded);
        }

        var bottomAnchored = _target.Anchor is IslandAnchor.BottomLeft or IslandAnchor.BottomCenter or IslandAnchor.BottomRight;
        var y = bottomAnchored
            ? UiMetrics.WindowPadding + LayoutTokens.ExpandedHeight - height
            : UiMetrics.WindowPadding;
        return (new Point(x, y), expanded);
    }

    /// <summary>Blends compact corner radii into the expanded radii by shell height (spec 3.1/2.2).</summary>
    public CornerRadii ComputeRadii(double height, double expandedFactor)
    {
        var mode = _target.Mode;
        CornerRadii expandedRadii = mode switch
        {
            IslandMode.Flush => new CornerRadii(0, 0, LayoutTokens.FlushBottomRadius, LayoutTokens.FlushBottomRadius),
            IslandMode.Dock => CornerRadii.Uniform(LayoutTokens.DockExpandedRadius),
            _ => CornerRadii.Uniform(LayoutTokens.ExpandedRadius),
        };

        CornerRadii compactRadii = mode switch
        {
            IslandMode.Flush => new CornerRadii(0, 0, LayoutTokens.FlushBottomRadius, LayoutTokens.FlushBottomRadius),
            IslandMode.Dock => CornerRadii.Uniform(LayoutTokens.DockCompactRadius),
            _ => CornerRadii.Uniform(Math.Min(height / 2, LayoutTokens.CompactRadiusCap)),
        };

        var t = Math.Clamp(expandedFactor, 0, 1);
        double Lerp(double a, double b) => a + ((b - a) * t);
        return new CornerRadii(
            Lerp(compactRadii.TopLeft, expandedRadii.TopLeft),
            Lerp(compactRadii.TopRight, expandedRadii.TopRight),
            Lerp(compactRadii.BottomRight, expandedRadii.BottomRight),
            Lerp(compactRadii.BottomLeft, expandedRadii.BottomLeft));
    }
}
