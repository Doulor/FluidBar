namespace FluidBar.Core.Motion;

/// <summary>
/// Independent settling-time reference used by the ±20 % acceptance tests. It integrates the
/// normalized spring ODE with a high-resolution RK4 solver (not the runtime semi-implicit Euler
/// integrator), so the test compares two different numerical methods rather than validating the
/// integrator against itself.
/// </summary>
public static class SettleTimePredictor
{
    private const double ReferenceStepSeconds = 1e-4;
    private const double EnvelopeFraction = 0.02;

    /// <summary>
    /// Returns the time after which a spring released from rest at 0 towards target 1 never
    /// exceeds the 2 % error band again (the spec's <c>t₂%</c> acceptance quantity).
    /// </summary>
    public static double PredictedSettleSeconds(SpringParameters parameters)
    {
        var stiffness = parameters.Stiffness;
        var damping = parameters.Damping;
        const double target = 1.0;
        var x = 0.0;
        var v = 0.0;
        var elapsed = 0.0;
        var lastOutside = 0.0;
        var maxSeconds = Math.Max(1.0, 10 * parameters.PeriodSeconds);

        while (elapsed < maxSeconds)
        {
            if (Math.Abs(x - target) >= EnvelopeFraction)
            {
                lastOutside = elapsed + ReferenceStepSeconds;
            }

            // RK4 for: x' = v, v' = -k(x - target) - c v
            var h = ReferenceStepSeconds;
            var k1x = v;
            var k1v = (-stiffness * (x - target)) - (damping * v);

            var x2 = x + (0.5 * h * k1x);
            var v2 = v + (0.5 * h * k1v);
            var k2x = v2;
            var k2v = (-stiffness * (x2 - target)) - (damping * v2);

            var x3 = x + (0.5 * h * k2x);
            var v3 = v + (0.5 * h * k2v);
            var k3x = v3;
            var k3v = (-stiffness * (x3 - target)) - (damping * v3);

            var x4 = x + (h * k3x);
            var v4 = v + (h * k3v);
            var k4x = v4;
            var k4v = (-stiffness * (x4 - target)) - (damping * v4);

            x += h / 6 * (k1x + (2 * k2x) + (2 * k3x) + k4x);
            v += h / 6 * (k1v + (2 * k2v) + (2 * k3v) + k4v);
            elapsed += h;

            if (Math.Abs(x - target) < EnvelopeFraction && Math.Abs(v) < EnvelopeFraction * parameters.AngularFrequency * 0.1)
            {
                break;
            }
        }

        return lastOutside;
    }
}
