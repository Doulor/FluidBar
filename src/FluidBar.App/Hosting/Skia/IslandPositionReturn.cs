using FluidBar.Core.Layout;
using FluidBar.Core.Motion;

namespace FluidBar.App.Hosting.Skia;

/// <summary>A temporary position pauses after release, then returns with a damped spring.</summary>
internal sealed class IslandPositionReturn
{
    internal const double HoldSeconds = 1.2;
    private static readonly SpringParameters Elastic = new(0.55, 0.72);
    private static readonly SpringParameters Reduced = new(0.24, 1);
    private readonly Spring _x = new(Elastic);
    private readonly Spring _y = new(Elastic);
    private double _hold;
    private bool _reducedMotion;

    public bool IsActive { get; private set; }
    public bool IsSuspended { get; private set; }
    public bool NeedsFrames => IsActive && !IsSuspended;
    public VisualPoint Offset => new(_x.Value, _y.Value);

    public void Start(VisualPoint offset, bool reducedMotion)
    {
        _reducedMotion = reducedMotion;
        _x.SetParameters(reducedMotion ? Reduced : Elastic);
        _y.SetParameters(reducedMotion ? Reduced : Elastic);
        _x.Reset(offset.X);
        _y.Reset(offset.Y);
        _x.Retarget(0, 1.0 / 60);
        _y.Retarget(0, 1.0 / 60);
        _hold = HoldSeconds;
        IsSuspended = false;
        IsActive = !_x.IsAtRest || !_y.IsAtRest;
    }

    public void Suspend() => IsSuspended = IsActive;

    public void Resume()
    {
        if (!IsSuspended) return;
        IsSuspended = false;
        _hold = HoldSeconds;
    }

    public void Step(TimeSpan delta, bool reducedMotion)
    {
        if (!NeedsFrames || delta <= TimeSpan.Zero) return;
        var seconds = delta.TotalSeconds;
        var held = Math.Min(seconds, _hold);
        _hold -= held;
        seconds -= held;
        if (seconds <= 0) return;
        if (_reducedMotion != reducedMotion)
        {
            _reducedMotion = reducedMotion;
            _x.SetParameters(reducedMotion ? Reduced : Elastic);
            _y.SetParameters(reducedMotion ? Reduced : Elastic);
        }
        _x.Step(seconds);
        _y.Step(seconds);
        IsActive = !_x.IsAtRest || !_y.IsAtRest;
    }

    public void Cancel()
    {
        _x.Reset();
        _y.Reset();
        IsActive = false;
        IsSuspended = false;
        _hold = 0;
    }
}
