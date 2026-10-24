namespace FluidBar.Core.Interaction;

/// <summary>
/// Manual "soften" control for the island (design change 2026-10-23): there is no predictive engine any
/// more — a right-click softens the island, another right-click restores it, and a safety timer brings it
/// back after <c>restoreAfterMs</c> so it can never be left invisible by accident.
/// </summary>
public sealed class ManualYieldController
{
    private readonly long _restoreAfterMs;

    /// <summary>Creates the controller.</summary>
    /// <param name="restoreAfterMs">Automatic restore delay in milliseconds (0 disables it).</param>
    public ManualYieldController(long restoreAfterMs = 3000) => _restoreAfterMs = restoreAfterMs;

    /// <summary>True while the island is softened (not consuming pointer input).</summary>
    public bool IsSoftened { get; private set; }

    /// <summary>Timestamp of the last soften, in the caller's monotonic clock.</summary>
    public long SoftenedAtMs { get; private set; }

    /// <summary>Softens the island (idempotent).</summary>
    /// <param name="nowMs">Monotonic "now".</param>
    /// <returns>True when the state changed.</returns>
    public bool Soften(long nowMs)
    {
        if (IsSoftened)
        {
            return false;
        }

        IsSoftened = true;
        SoftenedAtMs = nowMs;
        return true;
    }

    /// <summary>Restores the island (idempotent).</summary>
    /// <returns>True when the state changed.</returns>
    public bool Restore()
    {
        if (!IsSoftened)
        {
            return false;
        }

        IsSoftened = false;
        return true;
    }

    /// <summary>
    /// Advances the safety timer. Returns true when the island was restored by the timeout.
    /// </summary>
    /// <param name="nowMs">Monotonic "now".</param>
    public bool Tick(long nowMs)
    {
        if (!IsSoftened || _restoreAfterMs <= 0)
        {
            return false;
        }

        if (nowMs - SoftenedAtMs < _restoreAfterMs)
        {
            return false;
        }

        IsSoftened = false;
        return true;
    }
}
