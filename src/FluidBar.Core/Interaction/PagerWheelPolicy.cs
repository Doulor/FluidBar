namespace FluidBar.Core.Interaction;

/// <summary>Result of feeding one wheel/trackpad event into <see cref="PagerWheelPolicy"/>.</summary>
public readonly record struct PagerWheelResult
{
    /// <summary>Signed page delta (0 unless a page step fired).</summary>
    public int PageDelta { get; init; }

    /// <summary>Whether the event was consumed by the pager.</summary>
    public bool Consumed { get; init; }

    /// <summary>Whether the event was dropped because a slider/scrollable owns the gesture.</summary>
    public bool OwnerSuppressed { get; init; }

    /// <summary>Whether a page step is being held back by the wheel cooldown (the delta is kept).</summary>
    public bool CooldownBlocked { get; init; }

    /// <summary>Accumulated (sub-threshold) wheel DIP after this event.</summary>
    public double AccumulatedDip { get; init; }
}

/// <summary>
/// Pure wheel/trackpad pager policy mirroring prototype2's <c>onWheel</c>: accumulate deltas until one
/// <see cref="NotchDip"/> notch (140 DIP) is reached, then step one page; a
/// <see cref="CooldownMs"/> window (180 ms) paces the steps so one gesture cannot flip several pages
/// at once. Deltas arriving inside the window are still accumulated (a direction reversal drops the
/// stale residual) and the committed notch is subtracted, so continuous scrolling keeps queueing
/// pages instead of losing notches. While a control inside the page (slider, scrollable, text field)
/// owns the gesture the policy ignores the wheel entirely.
/// </summary>
public sealed class PagerWheelPolicy
{
    private DateTimeOffset? _lastPageChangeAt;
    private DateTimeOffset? _lastEventAt;
    private DateTimeOffset? _gestureLockUntil;

    /// <summary>Creates a wheel policy.</summary>
    /// <param name="pageCount">Number of pages (&gt;= 1); used by <see cref="ClampCurrent"/>.</param>
    /// <param name="notchDip">DIP accumulated per page step (100 in production).</param>
    /// <param name="cooldownMs">Minimum milliseconds between two page steps (120 in production).</param>
    public PagerWheelPolicy(
        int pageCount,
        double notchDip = PagerConstants.WheelNotchDip,
        int cooldownMs = PagerConstants.WheelCooldownMs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(notchDip, 0);
        ArgumentOutOfRangeException.ThrowIfNegative(cooldownMs);
        PageCount = pageCount;
        NotchDip = notchDip;
        CooldownMs = cooldownMs;
    }

    /// <summary>Number of pages.</summary>
    public int PageCount { get; }

    /// <summary>DIP accumulated per page step.</summary>
    public double NotchDip { get; }

    /// <summary>Cooldown between page steps in milliseconds.</summary>
    public int CooldownMs { get; }

    /// <summary>Currently accumulated sub-threshold wheel DIP.</summary>
    public double AccumulatedDip { get; private set; }

    /// <summary>Timestamp of the last emitted page step, when any.</summary>
    public DateTimeOffset? LastPageChangeAt => _lastPageChangeAt;

    /// <summary>Whether a page step happened less than <see cref="CooldownMs"/> ago.</summary>
    public bool IsInCooldown(DateTimeOffset now)
        => _lastPageChangeAt is { } last && now - last < TimeSpan.FromMilliseconds(CooldownMs);

    /// <summary>Clears accumulated sub-threshold deltas (page step / gesture end).</summary>
    public void ResetAccumulation() => AccumulatedDip = 0;

    /// <summary>
    /// Feeds one wheel event. Positive <paramref name="deltaDip"/> means "towards the next page".
    /// </summary>
    /// <param name="deltaDip">Wheel delta in DIP for this event.</param>
    /// <param name="now">Injected current time (no ambient clock).</param>
    /// <param name="controlOwnsGesture">
    /// True when a slider/scrollable/text control inside the page should consume the wheel.
    /// </param>
    /// <remarks>
    /// Mirrors prototype2's <c>onWheel</c>: the accumulator always takes the delta (a reversal drops
    /// the stale residual) and only the <i>commit</i> is paced by the cooldown window, which then
    /// subtracts one notch and keeps the rest. Dropping the event instead — the old behaviour — made a
    /// continuous wheel gesture flip a single page and then swallow everything until the snap ended.
    /// </remarks>
    public PagerWheelResult Accumulate(double deltaDip, DateTimeOffset now, bool controlOwnsGesture = false)
    {
        if (controlOwnsGesture)
        {
            AccumulatedDip = 0;
            return new PagerWheelResult
            {
                PageDelta = 0,
                Consumed = false,
                OwnerSuppressed = true,
                AccumulatedDip = AccumulatedDip,
            };
        }

        if (!double.IsFinite(deltaDip))
        {
            deltaDip = 0;
        }

        // A direction reversal throws away the stale residual before adding the new delta.
        if (deltaDip != 0 && AccumulatedDip != 0 && Math.Sign(deltaDip) != Math.Sign(AccumulatedDip))
        {
            AccumulatedDip = 0;
        }

        // Gesture lock: notches that belong to the same gesture as a page step (gap < idle 120 ms)
        // are dropped unqueued inside the 260 ms transition lock, so one flick can never chain pages.
        var sameGesture = _lastEventAt is { } lastEvent
            && now - lastEvent < TimeSpan.FromMilliseconds(PagerConstants.WheelGestureIdleMs);
        _lastEventAt = now;
        if (sameGesture && _gestureLockUntil is { } until && now < until)
        {
            // Still the same physical gesture: refresh the lock so a long continuous spin stays capped
            // at one page until the user actually stops (>= WheelGestureIdleMs without a notch).
            _gestureLockUntil = now.AddMilliseconds(PagerConstants.WheelGestureIdleMs);
            AccumulatedDip = 0;
            return new PagerWheelResult
            {
                PageDelta = 0,
                Consumed = true,
                CooldownBlocked = true,
                AccumulatedDip = 0,
            };
        }

        AccumulatedDip += deltaDip;
        var inCooldown = IsInCooldown(now);
        if (Math.Abs(AccumulatedDip) >= NotchDip && !inCooldown)
        {
            var sign = AccumulatedDip > 0 ? 1 : -1;
            AccumulatedDip -= sign * NotchDip;
            _lastPageChangeAt = now;
            _gestureLockUntil = now.AddMilliseconds(PagerConstants.WheelTransitionLockMs);
            return new PagerWheelResult
            {
                PageDelta = sign,
                Consumed = true,
                AccumulatedDip = AccumulatedDip,
            };
        }

        return new PagerWheelResult
        {
            PageDelta = 0,
            Consumed = true,
            CooldownBlocked = inCooldown,
            AccumulatedDip = AccumulatedDip,
        };
    }

    /// <summary>Applies accumulated wheel deltas to a page index and clamps it.</summary>
    public static int ApplyPageDelta(int currentPage, int pageDelta, int pageCount)
        => PagePagerPolicy.ClampPage(currentPage + pageDelta, pageCount);

    /// <summary>Clamps a page index for this policy's <see cref="PageCount"/>.</summary>
    public int ClampCurrent(int page) => PagePagerPolicy.ClampPage(page, PageCount);

    /// <summary>Resets accumulation and cooldown (mode switch / panel close).</summary>
    public void Reset()
    {
        AccumulatedDip = 0;
        _lastPageChangeAt = null;
    }
}

