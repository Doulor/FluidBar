using System.Collections.Concurrent;
using FluidBar.Core.Interaction;
using FluidBar.Core.Layout;
using SkiaSharp;

namespace FluidBar.Island;

/// <summary>
/// One measured prototype2 <c>.mini-rail</c> frame in host-local DIP. The rail is centred on the
/// expanded shell, sits <see cref="IslandMetrics.StripGap"/> below it and is
/// <see cref="IslandMetrics.StripHeight"/> tall; the island row is the 26 / dynamic-capsule / 26
/// trio with up to three far-page dots on either side.
/// </summary>
/// <param name="Page">Committed page index (nearest page to the pager position).</param>
/// <param name="CapsuleWidth">Current capsule width after the label growth and the page morph.</param>
/// <param name="Strip">Rail envelope (islands + dot clusters).</param>
/// <param name="Previous">Previous island (26×26; the slot stays reserved on page 0).</param>
/// <param name="Current">Current capsule (dynamic width × 28).</param>
/// <param name="Next">Next island (26×26; the slot stays reserved on the last page).</param>
/// <param name="LeftDots">Far-page dots on the left (0..3, nearest first).</param>
/// <param name="RightDots">Far-page dots on the right (0..3, nearest first).</param>
public readonly record struct MiniRailFrame(
    int Page,
    float CapsuleWidth,
    SKRect Strip,
    SKRect Previous,
    SKRect Current,
    SKRect Next,
    int LeftDots,
    int RightDots);

/// <summary>
/// Pure geometry + text metrics for the prototype2 transient preview rail.
/// <para>
/// Drawing (<see cref="TransientStripRenderer"/>) and hit-testing
/// (<see cref="IslandStripLayout.HitTest"/>, FluidBar.App <c>SkiaIslandHitTest</c>) call the same
/// <see cref="Measure"/> overload, so a painted pill and its click target can never drift apart.
/// </para>
/// <para>
/// Source of truth: <c>docs/rewrite/prototype2/{styles.css,app.js}</c> — measured live in a
/// 1440×900 Edge viewport (shell 440×284 at (500,12), rail y = 304, height 32).
/// </para>
/// </summary>
public static class MiniRailLayout
{
    /// <summary>Rail height in DIP (32; `.mini-rail` height).</summary>
    public const float RailHeight = (float)IslandMetrics.StripHeight;

    /// <summary>Rail top below the expanded shell bottom in DIP (8; `top: calc(100% + 8px)`).</summary>
    public const float RailGap = (float)IslandMetrics.StripGap;

    /// <summary>Height of the dot hit target (15 DIP; `.far-dot::after` insets −6 px).</summary>
    public const float DotHitHeight = (float)IslandMetrics.DotHitHeight;

    /// <summary>Width of the dot hit target (7 DIP; `.far-dot::after` insets −2 px).</summary>
    public const float DotHitWidth = (float)IslandMetrics.DotHitWidth;

    /// <summary>Nearest page index for a continuous pager position (prototype `Math.round`).</summary>
    /// <param name="pagePosition">Continuous pager position (0..5).</param>
    public static int PageAt(double pagePosition)
        => PagePagerPolicy.OffsetToPage(
            Position(pagePosition) * PagerConstants.PagePitchDip,
            PagerConstants.PagePitchDip,
            PagerConstants.PageCount);

    /// <summary>
    /// Locale label width of a page in DIP, measured with the same 9 DIP semibold face the rail paints
    /// (`Segoe UI Variable Text` semibold with the Microsoft YaHei UI CJK fallback).
    /// </summary>
    /// <param name="label">Short page label.</param>
    public static float LabelWidth(string? label) => MiniRailText.LabelWidth(label);

    /// <summary>Locale label width at a font scale (same faces the rail paints, scaled).</summary>
    /// <param name="label">Short page label.</param>
    /// <param name="fontScale">Island font scale (0.8–1.6).</param>
    public static float LabelWidth(string? label, double fontScale) => MiniRailText.LabelWidth(label, fontScale);

    /// <summary>
    /// Prototype capsule width for one label: `round(64 + max(0, ceil(labelW) − 20) × 1.1)`,
    /// clamped to 64..104 DIP. Measured sizes: 主页/媒体/通知 64, 剪贴板/小组件 73, Agent 70.
    /// </summary>
    /// <param name="label">Short page label.</param>
    public static float CapsuleWidth(string? label) => MiniRailText.CapsuleWidth(label);

    /// <summary>
    /// The same capsule contract with the label measured at <paramref name="fontScale"/>:
    /// <c>round(64 + max(0, ceil(labelW × scale) − 20) × 1.1)</c> clamped to 64..104 DIP. Because the
    /// width and the painted label share one measurement, text can never leave the capsule.
    /// </summary>
    /// <param name="label">Short page label.</param>
    /// <param name="fontScale">Island font scale (0.8–1.6).</param>
    public static float CapsuleWidth(string? label, double fontScale) => MiniRailText.CapsuleWidth(label, fontScale);

    /// <summary>
    /// Current capsule width at a continuous pager position: the prototype morphs the capsule between
    /// the two neighbouring pages' label widths while the page springs, so the width never jumps
    /// (measured range 64 → 73 DIP, max frame-to-frame delta 1.68 DIP at 60 Hz).
    /// </summary>
    /// <param name="pagePosition">Continuous pager position (0..5).</param>
    /// <param name="pages">Expanded page content that owns the localized short labels (may be null).</param>
    public static float CapsuleWidthAt(double pagePosition, ExpandedPageContent? pages)
        => CapsuleWidthAt(pagePosition, pages, 1.0);

    /// <summary>Capsule width at a continuous pager position and font scale.</summary>
    /// <param name="pagePosition">Continuous pager position (0..5).</param>
    /// <param name="pages">Expanded page content that owns the localized short labels (may be null).</param>
    /// <param name="fontScale">Island font scale (0.8–1.6).</param>
    public static float CapsuleWidthAt(double pagePosition, ExpandedPageContent? pages, double fontScale)
    {
        const int lastPage = PagerConstants.PageCount - 1;
        var position = Position(pagePosition);
        var lower = (int)Math.Floor(position);
        var lowerWidth = CapsuleWidth(Label(pages, lower), fontScale);
        if (lower >= lastPage)
        {
            return lowerWidth;
        }

        var fraction = position - lower;
        if (fraction <= 0.0001)
        {
            return lowerWidth;
        }

        var upperWidth = CapsuleWidth(Label(pages, lower + 1), fontScale);
        if (Math.Abs(upperWidth - lowerWidth) < 0.0001f)
        {
            return lowerWidth;
        }

        // Smoothstep easing reproduces the prototype's under-damped 0.32 s / ζ 0.85 spring: the
        // capsule eases out of the old width and into the new one instead of snapping at the commit.
        var eased = fraction * fraction * (3 - (2 * fraction));
        return (float)(lowerWidth + ((upperWidth - lowerWidth) * eased));
    }

    /// <summary>
    /// Measures one rail frame (host-local DIP) and writes the far-page dot rectangles into the
    /// caller's buffers. No allocations: drawing and hit-testing reuse their own fixed arrays.
    /// </summary>
    /// <param name="state">Current render state.</param>
    /// <param name="shell">Expanded shell rectangle in host-local DIP.</param>
    /// <param name="dotsLeft">Destination for the left cluster, nearest first (≥ 3 longs).</param>
    /// <param name="dotsRight">Destination for the right cluster, nearest first (≥ 3 longs).</param>
    public static MiniRailFrame Measure(
        in IslandRenderState state,
        SKRect shell,
        Span<SKRect> dotsLeft,
        Span<SKRect> dotsRight)
    {
        var arch = state.Archipelago;
        var position = Position(double.IsFinite(arch.PagePosition) ? arch.PagePosition : state.Pager.PagePosition);
        var page = PageAt(position);
        var capsule = CapsuleWidthAt(position, state.Pages, state.FontScale);
        var leftDots = arch.ResolvedFarLeftDots;
        var rightDots = arch.ResolvedFarRightDots;
        var leftWide = arch.ResolvedFarLeftOverflow;
        var rightWide = arch.ResolvedFarRightOverflow;

        const float neighbour = (float)IslandMetrics.MiniNeighbourWidth;
        const float capsuleHeight = (float)IslandMetrics.MiniCurrentHeight;
        const float islandGap = (float)IslandMetrics.MiniGap;
        const float dotIslandGap = (float)IslandMetrics.DotIslandGap;
        const float dotPitch = (float)(IslandMetrics.DotSize + IslandMetrics.DotGap);
        const float dotSize = (float)IslandMetrics.DotSize;
        const float wideDot = (float)IslandMetrics.DotWideWidth;

        var host = HostSurface.ForMode(state.Mode);
        var railTop = host.HasDockStrip
            ? (float)host.ArchipelagoTopLocal
            : shell.Bottom + RailGap;
        var midY = railTop + (RailHeight / 2f);
        var centreX = shell.MidX;

        var leftWidth = ClusterWidth(leftDots, leftWide);
        var rightWidth = ClusterWidth(rightDots, rightWide);
        var half = capsule / 2f;
        var prevCentre = centreX - half - islandGap - (neighbour / 2f);
        var nextCentre = centreX + half + islandGap + (neighbour / 2f);

        var previous = new SKRect(prevCentre - (neighbour / 2f), midY - (neighbour / 2f),
            prevCentre + (neighbour / 2f), midY + (neighbour / 2f));
        var current = new SKRect(centreX - half, midY - (capsuleHeight / 2f),
            centreX + half, midY + (capsuleHeight / 2f));
        var next = new SKRect(nextCentre - (neighbour / 2f), midY - (neighbour / 2f),
            nextCentre + (neighbour / 2f), midY + (neighbour / 2f));

        var leftClusterCentre = prevCentre - (neighbour / 2f) - dotIslandGap - (leftWidth / 2f);
        var rightClusterCentre = nextCentre + (neighbour / 2f) + dotIslandGap + (rightWidth / 2f);
        var dotTop = midY - (dotSize / 2f);

        var cursor = leftClusterCentre - (leftWidth / 2f);
        for (var i = 0; i < leftDots && i < dotsLeft.Length; i++)
        {
            var width = leftWide && i == leftDots - 1 ? wideDot : dotSize;
            dotsLeft[i] = new SKRect(cursor, dotTop, cursor + width, dotTop + dotSize);
            cursor += width + (dotPitch - dotSize);
        }

        cursor = rightClusterCentre - (rightWidth / 2f);
        for (var i = 0; i < rightDots && i < dotsRight.Length; i++)
        {
            var width = rightWide && i == rightDots - 1 ? wideDot : dotSize;
            dotsRight[i] = new SKRect(cursor, dotTop, cursor + width, dotTop + dotSize);
            cursor += width + (dotPitch - dotSize);
        }

        // The prototype rail is a zero-width anchor centred on the shell, so its envelope is symmetric
        // even when only one side carries dots.
        var halfWidth = Math.Max(
            Math.Max(previous.Left, next.Right) - centreX,
            centreX - Math.Min(previous.Left, next.Right));
        if (leftDots > 0)
        {
            halfWidth = Math.Max(halfWidth, centreX - (leftClusterCentre - (leftWidth / 2f)));
        }

        if (rightDots > 0)
        {
            halfWidth = Math.Max(halfWidth, (rightClusterCentre + (rightWidth / 2f)) - centreX);
        }

        var strip = new SKRect(centreX - halfWidth, railTop, centreX + halfWidth, railTop + RailHeight);
        return new MiniRailFrame(page, capsule, strip, previous, current, next, leftDots, rightDots);
    }

    /// <summary>Centre X of the rail: the expanded shell midpoint.</summary>
    public static float CentreX(SKRect shell) => shell.MidX;

    /// <summary>Prototype cluster width: n dots, 4 DIP gaps and +4 DIP when the last dot is wide.</summary>
    internal static float ClusterWidth(int count, bool wide)
        => count <= 0
            ? 0
            : (count * (float)IslandMetrics.DotSize)
              + ((count - 1) * (float)IslandMetrics.DotGap)
              + (wide ? (float)(IslandMetrics.DotWideWidth - IslandMetrics.DotSize) : 0);

    private static string Label(ExpandedPageContent? pages, int page)
        => pages?.ShortTitle(page)
           ?? ExpandedPageContent.DefaultPageTitles[Math.Clamp(page, 0, PagerConstants.PageCount - 1)];

    private static double Position(double pagePosition)
        => double.IsFinite(pagePosition)
            ? Math.Clamp(pagePosition, 0, PagerConstants.PageCount - 1)
            : 0;
}

/// <summary>
/// Label metrics for the rail: one shared 9 DIP semibold face (the same `Segoe UI Variable Text`
/// semibold + `Microsoft YaHei UI` fallback the text engine shapes), unhinted fractional advances so
/// the measurement matches the browser's `getBoundingClientRect()` and widths stay stable per label.
/// </summary>
public static class MiniRailText
{
    private static readonly ConcurrentDictionary<(string Label, float Scale), float> WidthCache = new();
    private static readonly ConcurrentDictionary<float, Fonts> ScaledFonts = new();
    private static readonly Lazy<Fonts> SharedFonts = new(CreateFonts, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Measured label width in DIP (0 for an empty label).</summary>
    /// <param name="label">Short page label.</param>
    public static float LabelWidth(string? label) => LabelWidth(label, 1.0);

    /// <summary>Measured label width in DIP at a font scale (clamped to 0.8–1.6).</summary>
    /// <param name="label">Short page label.</param>
    /// <param name="fontScale">Island font scale.</param>
    public static float LabelWidth(string? label, double fontScale)
    {
        if (string.IsNullOrEmpty(label))
        {
            return 0;
        }

        var scale = (float)Math.Clamp(double.IsFinite(fontScale) ? fontScale : 1.0, 0.8, 1.6);
        scale = MathF.Round(scale, 3);
        return WidthCache.GetOrAdd((label, scale), static key =>
        {
            var (text, s) = key;
            var fonts = Math.Abs(s - 1f) < 1e-6f ? SharedFonts.Value : ScaledFonts.GetOrAdd(s, CreateScaledFonts);
            var width = 0f;
            var runStart = 0;
            var runIsCjk = IsCjk(char.ConvertToUtf32(text, 0));
            for (var i = 0; i < text.Length;)
            {
                var runLength = char.IsHighSurrogate(text[i]) ? 2 : 1;
                var cjk = IsCjk(char.ConvertToUtf32(text, i));
                if (cjk != runIsCjk)
                {
                    width += MeasureRun(text[runStart..i], runIsCjk, fonts);
                    runStart = i;
                    runIsCjk = cjk;
                }

                i += runLength;
            }

            var runWidth = width + MeasureRun(text[runStart..], runIsCjk, fonts);

            // The prototype label is `letter-spacing: 0.01em`: the browser reports the trailing
            // advance too, so the measured width carries one spacing step (scaled) per character.
            var spacing = (float)(IslandMetrics.MiniLabelSize * s * IslandMetrics.MiniLabelLetterSpacingEm);
            return runWidth + (text.Length * spacing);
        });
    }

    /// <summary>
    /// Prototype capsule width: `round(64 + max(0, ceil(labelW) − 20) × 1.1)` clamped to 64..104 DIP.
    /// </summary>
    /// <param name="label">Short page label.</param>
    public static float CapsuleWidth(string? label) => CapsuleWidth(label, 1.0);

    /// <summary>
    /// The same capsule contract with the label measured at a font scale, so the capsule always grows
    /// with the painted label and the text can never leave it.
    /// </summary>
    /// <param name="label">Short page label.</param>
    /// <param name="fontScale">Island font scale.</param>
    public static float CapsuleWidth(string? label, double fontScale)
    {
        var width = LabelWidth(label, fontScale);
        var grown = IslandMetrics.MiniCurrentBaseWidth
            + (Math.Max(0f, MathF.Ceiling(width) - 20f) * 1.1f);
        return Math.Clamp(
            MathF.Round((float)grown, MidpointRounding.AwayFromZero),
            (float)IslandMetrics.MiniCurrentMinWidth,
            (float)IslandMetrics.MiniCurrentMaxWidth);
    }

    private static float MeasureRun(string text, bool cjk, Fonts fonts)
        => text.Length == 0 ? 0 : (cjk ? fonts.CjkFont : fonts.UiFont).MeasureText(text);

    private static Fonts CreateFonts()
    {
        // The prototype label inherits the 400 body weight: regular faces, not semibold.
        var ui = Resolve(
            ["Segoe UI Variable Text", "Segoe UI Variable Display", "Segoe UI", "Microsoft YaHei UI"],
            SKFontStyleWeight.Normal);
        var cjk = Resolve(
            ["Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI"],
            SKFontStyleWeight.Normal);
        return new Fonts(
            ui,
            cjk,
            new SKFont(ui, (float)IslandMetrics.MiniLabelSize)
            {
                Edging = SKFontEdging.Antialias,
                Subpixel = true,
                Hinting = SKFontHinting.None,
            },
            new SKFont(cjk, (float)IslandMetrics.MiniLabelSize)
            {
                Edging = SKFontEdging.Antialias,
                Subpixel = true,
                Hinting = SKFontHinting.None,
            });
    }

    private static Fonts CreateScaledFonts(float scale)
    {
        var baseFonts = SharedFonts.Value;
        var size = (float)IslandMetrics.MiniLabelSize * scale;
        return new Fonts(
            baseFonts.Ui,
            baseFonts.Cjk,
            new SKFont(baseFonts.Ui, size)
            {
                Edging = SKFontEdging.Antialias,
                Subpixel = true,
                Hinting = SKFontHinting.None,
            },
            new SKFont(baseFonts.Cjk, size)
            {
                Edging = SKFontEdging.Antialias,
                Subpixel = true,
                Hinting = SKFontHinting.None,
            });
    }

    private static SKTypeface Resolve(string[] families, SKFontStyleWeight weight)
    {
        foreach (var family in families)
        {
            var typeface = SKTypeface.FromFamilyName(family, weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
            if (typeface is null)
            {
                continue;
            }

            if (string.Equals(typeface.FamilyName, family, StringComparison.OrdinalIgnoreCase))
            {
                return typeface;
            }

            typeface.Dispose();
        }

        return SKTypeface.Default;
    }

    private static bool IsCjk(int codePoint)
        => codePoint is >= 0x2E80 and <= 0x2FFF
            or >= 0x3000 and <= 0x30FF
            or >= 0x31F0 and <= 0x31FF
            or >= 0x3400 and <= 0x4DBF
            or >= 0x4E00 and <= 0x9FFF
            or >= 0xF900 and <= 0xFAFF
            or >= 0xFE30 and <= 0xFE4F
            or >= 0xFF00 and <= 0xFFEF
            or >= 0x20000 and <= 0x2FA1F;

    private sealed class Fonts(SKTypeface ui, SKTypeface cjk, SKFont uiFont, SKFont cjkFont)
    {
        public SKTypeface Ui { get; } = ui;

        public SKTypeface Cjk { get; } = cjk;

        public SKFont UiFont { get; } = uiFont;

        public SKFont CjkFont { get; } = cjkFont;
    }
}
