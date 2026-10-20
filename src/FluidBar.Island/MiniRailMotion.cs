using FluidBar.Core.Interaction;
using SkiaSharp;

namespace FluidBar.Island;

/// <summary>
/// Spring targets for one prototype2 preview-rail frame. All offsets are relative to the rail
/// centre (the expanded shell midpoint), matching the prototype's translate3d values.
/// </summary>
/// <param name="Page">Rail page (the page the pills/dots represent).</param>
/// <param name="CapsuleWidth">Current capsule width target in DIP.</param>
/// <param name="PrevX">Previous island centre offset in DIP.</param>
/// <param name="NextX">Next island centre offset in DIP.</param>
/// <param name="PrevAlpha">Previous island opacity target.</param>
/// <param name="NextAlpha">Next island opacity target.</param>
/// <param name="LeftX">Left dot cluster centre offset in DIP.</param>
/// <param name="RightX">Right dot cluster centre offset in DIP.</param>
/// <param name="LeftAlpha">Left dot cluster opacity target.</param>
/// <param name="RightAlpha">Right dot cluster opacity target.</param>
public readonly record struct MiniRailTargets(
    int Page,
    float CapsuleWidth,
    float PrevX,
    float NextX,
    float PrevAlpha,
    float NextAlpha,
    float LeftX,
    float RightX,
    float LeftAlpha,
    float RightAlpha);

/// <summary>
/// One animated preview-rail frame: spring values plus the 150 ms label cross-fade and the three
/// content pulses, exactly the quantities the prototype writes through <c>applyRailFrame</c>.
/// <see cref="Active"/> is false for hosts/tests that do not drive the springs (settled layout).
/// </summary>
/// <param name="Active">Whether the host supplied live spring values.</param>
/// <param name="Page">Rail page the pills/dots represent.</param>
/// <param name="CapsuleWidth">Current capsule width in DIP (64..104).</param>
/// <param name="PrevX">Previous island centre offset in DIP.</param>
/// <param name="NextX">Next island centre offset in DIP.</param>
/// <param name="PrevAlpha">Previous island opacity (0..1).</param>
/// <param name="NextAlpha">Next island opacity (0..1).</param>
/// <param name="LeftX">Left dot cluster centre offset in DIP.</param>
/// <param name="RightX">Right dot cluster centre offset in DIP.</param>
/// <param name="LeftAlpha">Left dot cluster opacity (0..1).</param>
/// <param name="RightAlpha">Right dot cluster opacity (0..1).</param>
/// <param name="LabelProgress">Label cross-fade progress (0 = old label, 1 = new label).</param>
/// <param name="PulsePrev">Previous content pulse progress (1 = no pulse).</param>
/// <param name="PulseCurrent">Current content pulse progress (1 = no pulse).</param>
/// <param name="PulseNext">Next content pulse progress (1 = no pulse).</param>
public readonly record struct MiniRailMotionValues(
    bool Active,
    int Page,
    float CapsuleWidth,
    float PrevX,
    float NextX,
    float PrevAlpha,
    float NextAlpha,
    float LeftX,
    float RightX,
    float LeftAlpha,
    float RightAlpha,
    float LabelProgress,
    float PulsePrev,
    float PulseCurrent,
    float PulseNext)
{
    /// <summary>Content opacity of one slot from its pulse progress (prototype: 0.35 → 1).</summary>
    /// <param name="progress">Pulse progress (0..1; 1 = settled).</param>
    public static float PulseOpacity(float progress) => 0.35f + (0.65f * progress);
}

/// <summary>
/// Frame-rate independent spring driver for the preview rail, copied from the prototype2
/// <c>RailSpring</c>/<c>RAIL_SPRING</c> contract: period <b>T = 0.32 s</b>, damping ratio
/// <b>ζ = 0.85</b>, integrated with ≤ 4 ms substeps, snapping when |v − target| &lt; 0.02 and
/// |velocity| &lt; 0.05. One instance per island host; the controller steps it every frame and
/// projects <see cref="Values"/> into the render state, so drawing and hit-testing read the exact
/// same animated numbers.
/// </summary>
public sealed class MiniRailMotion
{
    /// <summary>Spring period in seconds (prototype <c>RAIL_SPRING.T</c>).</summary>
    public const double PeriodSeconds = 0.32;

    /// <summary>Damping ratio (prototype <c>RAIL_SPRING.Z</c>).</summary>
    public const double DampingRatio = 0.85;

    /// <summary>Integration substep cap in seconds (prototype 4 ms).</summary>
    public const double MaxSubstepSeconds = 0.004;

    /// <summary>Distance at which a spring snaps to its target (prototype 0.02).</summary>
    public const double SettleDistance = 0.02;

    /// <summary>Velocity at which a spring snaps to its target (prototype 0.05).</summary>
    public const double SettleVelocity = 0.05;

    /// <summary>Label cross-fade duration in seconds (prototype 150 ms).</summary>
    public const double LabelCrossfadeSeconds = 0.15;

    /// <summary>Content pulse duration in seconds (prototype 150 ms).</summary>
    public const double PulseSeconds = 0.15;

    // Mutable structs: each spring owns its value/target/velocity and steps in place.
    private RailSpring _width = new(IslandMetrics.MiniCurrentBaseWidth);
    private RailSpring _prevX;
    private RailSpring _nextX;
    private RailSpring _prevAlpha;
    private RailSpring _nextAlpha;
    private RailSpring _leftX;
    private RailSpring _rightX;
    private RailSpring _leftAlpha;
    private RailSpring _rightAlpha;

    private int _page = -1;
    private double _label;
    private double _pulsePrev = 1;
    private double _pulseCurrent = 1;
    private double _pulseNext = 1;
    private bool _snapped;

    /// <summary>Current page the rail represents (-1 before the first target).</summary>
    public int Page => _page;

    /// <summary>Whether any spring, the label cross-fade or a content pulse is still running.</summary>
    public bool IsAnimating => !_snapped || _label < 1 || _pulsePrev < 1 || _pulseCurrent < 1 || _pulseNext < 1;

    /// <summary>Current animated values, ready to be copied into the render state.</summary>
    public MiniRailMotionValues Values => new(
        _page >= 0,
        _page,
        (float)_width.Value,
        (float)_prevX.Value,
        (float)_nextX.Value,
        (float)Math.Clamp(_prevAlpha.Value, 0, 1),
        (float)Math.Clamp(_nextAlpha.Value, 0, 1),
        (float)_leftX.Value,
        (float)_rightX.Value,
        (float)Math.Clamp(_leftAlpha.Value, 0, 1),
        (float)Math.Clamp(_rightAlpha.Value, 0, 1),
        (float)Math.Clamp(_label, 0, 1),
        (float)Math.Clamp(_pulsePrev, 0, 1),
        (float)Math.Clamp(_pulseCurrent, 0, 1),
        (float)Math.Clamp(_pulseNext, 0, 1));

    /// <summary>
    /// Snaps every value to its target (prototype <c>snapRailSprings</c>): used when the rail is
    /// revealed, when motion is reduced, and when the host jumps pages without animating.
    /// </summary>
    /// <param name="targets">Target frame.</param>
    public void Snap(in MiniRailTargets targets)
    {
        _page = targets.Page;
        _width.Snap(targets.CapsuleWidth);
        _prevX.Snap(targets.PrevX);
        _nextX.Snap(targets.NextX);
        _prevAlpha.Snap(targets.PrevAlpha);
        _nextAlpha.Snap(targets.NextAlpha);
        _leftX.Snap(targets.LeftX);
        _rightX.Snap(targets.RightX);
        _leftAlpha.Snap(targets.LeftAlpha);
        _rightAlpha.Snap(targets.RightAlpha);
        _label = 1;
        _pulsePrev = 1;
        _pulseCurrent = 1;
        _pulseNext = 1;
        _snapped = true;
    }

    /// <summary>
    /// Retargets the springs. A page change also restarts the 150 ms label cross-fade and the three
    /// content pulses, exactly like the prototype's <c>railSetPage</c>.
    /// </summary>
    /// <param name="targets">Target frame.</param>
    public void Retarget(in MiniRailTargets targets)
    {
        if (targets.Page != _page)
        {
            if (_page >= 0)
            {
                _label = 0;
                _pulsePrev = 0;
                _pulseCurrent = 0;
                _pulseNext = 0;
            }

            _page = targets.Page;
        }

        _width.Target = targets.CapsuleWidth;
        _prevX.Target = targets.PrevX;
        _nextX.Target = targets.NextX;
        _prevAlpha.Target = targets.PrevAlpha;
        _nextAlpha.Target = targets.NextAlpha;
        _leftX.Target = targets.LeftX;
        _rightX.Target = targets.RightX;
        _leftAlpha.Target = targets.LeftAlpha;
        _rightAlpha.Target = targets.RightAlpha;
        _snapped = false;
    }

    /// <summary>Steps every spring and timer by one frame.</summary>
    /// <param name="deltaSeconds">Frame delta in seconds (clamped like the prototype's 1–50 ms).</param>
    /// <returns>Whether the rail is still animating.</returns>
    public bool Step(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0)
        {
            return IsAnimating;
        }

        var dt = Math.Clamp(deltaSeconds, 0.001, 0.05);
        var moving = false;
        moving |= _width.Step(dt);
        moving |= _prevX.Step(dt);
        moving |= _nextX.Step(dt);
        moving |= _prevAlpha.Step(dt);
        moving |= _nextAlpha.Step(dt);
        moving |= _leftX.Step(dt);
        moving |= _rightX.Step(dt);
        moving |= _leftAlpha.Step(dt);
        moving |= _rightAlpha.Step(dt);
        _snapped = !moving;

        if (_label < 1)
        {
            _label = Math.Min(1, _label + (dt / LabelCrossfadeSeconds));
        }

        if (_pulsePrev < 1)
        {
            _pulsePrev = Math.Min(1, _pulsePrev + (dt / PulseSeconds));
        }

        if (_pulseCurrent < 1)
        {
            _pulseCurrent = Math.Min(1, _pulseCurrent + (dt / PulseSeconds));
        }

        if (_pulseNext < 1)
        {
            _pulseNext = Math.Min(1, _pulseNext + (dt / PulseSeconds));
        }

        return IsAnimating;
    }

    /// <summary>One prototype <c>RailSpring</c>.</summary>
    private struct RailSpring
    {
        public double Value;
        public double Target;
        public double Velocity;

        public RailSpring(double value)
        {
            Value = value;
            Target = value;
            Velocity = 0;
        }

        public void Snap(double value)
        {
            Value = value;
            Target = value;
            Velocity = 0;
        }

        /// <summary>Integrates one frame; returns whether the spring is still moving.</summary>
        public bool Step(double dt)
        {
            var steps = Math.Max(1, (int)Math.Ceiling(dt / MaxSubstepSeconds));
            var h = dt / steps;
            var w = 2 * Math.PI / PeriodSeconds;
            for (var i = 0; i < steps; i++)
            {
                var a = (-w * w * (Value - Target)) - (2 * DampingRatio * w * Velocity);
                Velocity += a * h;
                Value += Velocity * h;
            }

            if (Math.Abs(Value - Target) < SettleDistance && Math.Abs(Velocity) < SettleVelocity)
            {
                Value = Target;
                Velocity = 0;
                return false;
            }

            return true;
        }
    }
}

/// <summary>
/// Targets + geometry helpers for the animated rail. The spring targets and the settled geometry
/// come from the same constants, so a snap lands exactly on the prototype's measured rectangles.
/// </summary>
public static class MiniRailTargetsFactory
{
    /// <summary>Dot counts for a page: capped at three, with an overflow (wide) last dot.</summary>
    /// <param name="page">Page index.</param>
    /// <param name="left">Left dot count (0..3).</param>
    /// <param name="leftWide">Whether the last left dot is the 7 DIP overflow pill.</param>
    /// <param name="right">Right dot count (0..3).</param>
    /// <param name="rightWide">Whether the last right dot is the 7 DIP overflow pill.</param>
    public static void DotCounts(int page, out int left, out bool leftWide, out int right, out bool rightWide, int pageCount)
    {
        var count = Math.Max(1, pageCount);
        var index = Math.Clamp(page, 0, count - 1);
        var leftTotal = Math.Max(0, index - 1);
        var rightTotal = Math.Max(0, (count - 1) - (index + 1));
        left = Math.Min(IslandMetrics.MaxDotsPerSide, leftTotal);
        right = Math.Min(IslandMetrics.MaxDotsPerSide, rightTotal);
        leftWide = leftTotal > left;
        rightWide = rightTotal > right;
    }

    /// <summary>Spring targets for a rail page.</summary>
    /// <param name="page">Rail page index.</param>
    /// <param name="pages">Expanded page content that owns the localized short labels.</param>
    /// <param name="fontScale">Island font scale.</param>
    public static MiniRailTargets Create(int page, ExpandedPageContent? pages, double fontScale)
    {
        var count = Math.Max(1, pages?.PageCount ?? 1);
        var index = Math.Clamp(page, 0, count - 1);
        var label = pages?.ShortTitle(index)
            ?? ExpandedPageContent.DefaultPageTitles[Math.Min(index, ExpandedPageContent.DefaultPageTitles.Length - 1)];
        var width = MiniRailLayout.CapsuleWidth(label, fontScale);
        const float neighbour = (float)IslandMetrics.MiniNeighbourWidth;
        const float islandGap = (float)IslandMetrics.MiniGap;
        const float dotIslandGap = (float)IslandMetrics.DotIslandGap;
        const float half = neighbour / 2f;

        var prevX = -(width / 2f) - islandGap - half;
        var nextX = (width / 2f) + islandGap + half;
        DotCounts(index, out var left, out var leftWide, out var right, out var rightWide, count);
        var leftWidth = MiniRailLayout.ClusterWidth(left, leftWide);
        var rightWidth = MiniRailLayout.ClusterWidth(right, rightWide);

        return new MiniRailTargets(
            index,
            width,
            prevX,
            nextX,
            index > 0 ? 1f : 0f,
            index < count - 1 ? 1f : 0f,
            prevX - half - dotIslandGap - (leftWidth / 2f),
            nextX + half + dotIslandGap + (rightWidth / 2f),
            left > 0 ? 1f : 0f,
            right > 0 ? 1f : 0f);
    }
}

