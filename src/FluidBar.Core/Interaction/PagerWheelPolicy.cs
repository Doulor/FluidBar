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

    /// <summary>Whether the event was dropped because the wheel cooldown is active.</summary>
    public bool CooldownBlocked { get; init; }

    /// <summary>Accumulated (sub-threshold) wheel DIP after this event.</summary>
    public double AccumulatedDip { get; init; }
}

/// <summary>
/// Pure wheel/trackpad pager policy: accumulate deltas until one 100 DIP notch is reached, then step
/// exactly one page per notch/cooldown window. A 120 ms cooldown drops residual inertia so a single
/// gesture cannot flip several pages. While a control inside the page (slider, scrollable, text
/// field) owns the gesture the policy ignores the wheel entirely.
/// </summary>
public sealed class PagerWheelPolicy
{
    private DateTimeOffset? _lastPageChangeAt;

    /// <summary>Creates a wheel policy.</summary>
    /// <param name="pageCount">Number of pages (&gt;= 1); used by <see cref="ClampCurrent"/>.</param>
    /// <param name="notchDip">DIP accumulated per page step (100 in production).</param>
    /// <param name="cooldownMs">Minimum milliseconds between two page steps (120 in production).</param>
    public PagerWheelPolicy(
        int pageCount = PagerConstants.PageCount,
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

        if (IsInCooldown(now))
        {
            // Drop residual inertia inside the cooldown window: at most one page per window.
            return new PagerWheelResult
            {
                PageDelta = 0,
                Consumed = false,
                CooldownBlocked = true,
                AccumulatedDip = AccumulatedDip,
            };
        }

        if (!double.IsFinite(deltaDip))
        {
            deltaDip = 0;
        }

        AccumulatedDip += deltaDip;
        if (AccumulatedDip >= NotchDip)
        {
            AccumulatedDip = 0;
            _lastPageChangeAt = now;
            return new PagerWheelResult
            {
                PageDelta = 1,
                Consumed = true,
                AccumulatedDip = AccumulatedDip,
            };
        }

        if (AccumulatedDip <= -NotchDip)
        {
            AccumulatedDip = 0;
            _lastPageChangeAt = now;
            return new PagerWheelResult
            {
                PageDelta = -1,
                Consumed = true,
                AccumulatedDip = AccumulatedDip,
            };
        }

        return new PagerWheelResult
        {
            PageDelta = 0,
            Consumed = true,
            AccumulatedDip = AccumulatedDip,
        };
    }

    /// <summary>Applies accumulated wheel deltas to a page index and clamps it.</summary>
    public static int ApplyPageDelta(int currentPage, int pageDelta, int pageCount = PagerConstants.PageCount)
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
