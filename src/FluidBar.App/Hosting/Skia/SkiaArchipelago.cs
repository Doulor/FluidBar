using FluidBar.Core.Interaction;
using FluidBar.Core.Layout;
using FluidBar.Core.Motion;
using FluidBar.Island;

namespace FluidBar.App.Hosting.Skia;

/// <summary>
/// Reveal/hide state and springs for the detached Page Archipelago strip. The strip is transient:
/// it reveals on pager drag/wheel or after a hot-zone dwell, and auto-hides after 1000 ms without
/// activity (inside the 900–1200 ms design band; <see cref="ArchipelagoRevealPolicy.HideDelayMs"/> is
/// the 1200 ms ceiling used by the Core policy). Opacity/translate/scale come from Core springs so
/// the reveal is frame-rate independent.
/// </summary>
public sealed class SkiaArchipelago : IDisposable
{
    /// <summary>Hot-zone dwell before reveal (Core 300 ms).</summary>
    public const int RevealDelayMs = ArchipelagoRevealPolicy.RevealDelayMs;

    /// <summary>Idle time before auto-hide (1000 ms, inside the 900–1200 ms design band).</summary>
    public const int HideDelayMs = 1000;

    /// <summary>Translate distance in DIP at the hidden end of the reveal spring.</summary>
    public const double HiddenTranslateYDip = 8;

    /// <summary>Scale at the hidden end of the reveal spring.</summary>
    public const double HiddenScale = 0.96;

    private readonly Spring _reveal;
    private readonly Spring _translate;
    private readonly Spring _scale;

    private bool _visible;
    private DateTimeOffset _lastActivityAt;
    private bool _hasActivity;

    /// <summary>Creates the strip state (hidden).</summary>
    /// <param name="profile">Motion profile (reduced motion resolves to a faster, critically damped spring).</param>
    public SkiaArchipelago(MotionProfile? profile = null)
    {
        var resolved = profile ?? MotionProfile.Default;
        var parameters = resolved.ResolveParameters(MotionTokens.Hover);
        _reveal = new Spring(parameters, 0);
        _translate = new Spring(parameters, HiddenTranslateYDip);
        _scale = new Spring(parameters, HiddenScale);
    }

    /// <summary>Whether the strip is logically revealed.</summary>
    public bool IsVisible => _visible;

    /// <summary>Whether the reveal/hide springs are still moving.</summary>
    public bool IsAnimating => !_reveal.IsAtRest || !_translate.IsAtRest || !_scale.IsAtRest;

    /// <summary>Reveal opacity 0..1.</summary>
    public double Opacity => Math.Clamp(_reveal.Value, 0, 1);

    /// <summary>Reveal translate in DIP (8 = hidden, 0 = settled).</summary>
    public double TranslateYDip => _translate.Value;

    /// <summary>Reveal scale (0.96 = hidden, 1 = settled).</summary>
    public double Scale => _scale.Value;

    /// <summary>Time of the last drag/wheel/hot-zone activity.</summary>
    public DateTimeOffset LastActivityAt => _lastActivityAt;

    /// <summary>Reveals the strip immediately (drag/wheel start) and restarts the hide timer.</summary>
    /// <param name="now">Current time.</param>
    public void Reveal(DateTimeOffset now)
    {
        _lastActivityAt = now;
        _hasActivity = true;
        if (_visible)
        {
            return;
        }

        _visible = true;
        _reveal.Retarget(1, PagerConstants.RetargetFrameSeconds);
        _translate.Retarget(0, PagerConstants.RetargetFrameSeconds);
        _scale.Retarget(1, PagerConstants.RetargetFrameSeconds);
    }

    /// <summary>Hides the strip (collapse, timeout).</summary>
    public void Hide()
    {
        if (!_visible)
        {
            return;
        }

        _visible = false;
        _reveal.Retarget(0, PagerConstants.RetargetFrameSeconds);
        _translate.Retarget(HiddenTranslateYDip, PagerConstants.RetargetFrameSeconds);
        _scale.Retarget(HiddenScale, PagerConstants.RetargetFrameSeconds);
    }

    /// <summary>Records strip activity (drag/wheel) without forcing a reveal.</summary>
    /// <param name="now">Current time.</param>
    public void NoteActivity(DateTimeOffset now)
    {
        _lastActivityAt = now;
        _hasActivity = true;
    }

    /// <summary>
    /// Applies the reveal/hide thresholds: hot-zone dwell reveals, idle non-dragging hides after
    /// <see cref="HideDelayMs"/>, and any non-expanded state hides immediately.
    /// </summary>
    /// <param name="now">Current time.</param>
    /// <param name="expanded">Whether the island is expanded.</param>
    /// <param name="pointerInHotZone">Whether the pointer is in the bottom hot zone.</param>
    /// <param name="pointerOnStrip">Whether the pointer is over the revealed strip (keeps it alive).</param>
    /// <param name="hotZoneDwell">How long the pointer has dwelt in the hot zone.</param>
    /// <param name="isDragging">Whether a pager drag is in flight (blocks auto-hide).</param>
    public void Update(
        DateTimeOffset now,
        bool expanded,
        bool pointerInHotZone,
        bool pointerOnStrip,
        TimeSpan hotZoneDwell,
        bool isDragging)
    {
        if (!expanded)
        {
            Hide();
            return;
        }

        if (!_visible && pointerInHotZone && hotZoneDwell >= TimeSpan.FromMilliseconds(RevealDelayMs))
        {
            Reveal(now);
            return;
        }

        if (!_visible || isDragging || pointerInHotZone || pointerOnStrip)
        {
            return;
        }

        var sinceLast = _hasActivity ? now - _lastActivityAt : TimeSpan.MaxValue;
        if (sinceLast >= TimeSpan.FromMilliseconds(HideDelayMs))
        {
            Hide();
        }
    }

    /// <summary>Steps the reveal springs.</summary>
    /// <param name="deltaSeconds">Frame delta in seconds.</param>
    public bool Step(double deltaSeconds)
    {
        if (deltaSeconds <= 0)
        {
            return IsAnimating;
        }

        _reveal.Step(deltaSeconds);
        _translate.Step(deltaSeconds);
        _scale.Step(deltaSeconds);
        return IsAnimating;
    }

    /// <summary>Projects the current springs onto the renderer's strip state.</summary>
    /// <param name="pagePosition">Continuous pager position.</param>
    /// <param name="pageCount">Number of pages.</param>
    public ArchipelagoState ToState(double pagePosition, int pageCount = PagerConstants.PageCount)
    {
        var position = double.IsFinite(pagePosition)
            ? Math.Clamp(pagePosition, 0, PagerConstants.PageCount - 1)
            : 0;
        var page = PagePagerPolicy.OffsetToPage(position * PagerConstants.PagePitchDip, PagerConstants.PagePitchDip, pageCount);
        var left = Math.Clamp(page - 1, 0, ArchipelagoState.MaxDotsPerSide);
        var right = Math.Clamp((pageCount - 1) - (page + 1), 0, ArchipelagoState.MaxDotsPerSide);
        return new ArchipelagoState
        {
            Visible = _visible,
            Opacity = Opacity,
            ShowIdlePreview = true,
            TranslateYDip = TranslateYDip,
            Scale = Scale,
            PagePosition = position,
            FarLeftDotCount = left,
            FarRightDotCount = right,
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Springs hold no unmanaged state; the method exists so the host can dispose uniformly.
    }
}
