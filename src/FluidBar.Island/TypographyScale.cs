using FluidBar.Core.Layout;

namespace FluidBar.Island;

/// <summary>One resolved typographic style: font role, size, line box and tracking.</summary>
public readonly record struct TypographyToken(
    string Name,
    FontRole Role,
    float Size,
    float LineHeight,
    float Tracking = 0f,
    bool Tabular = false);

/// <summary>
/// iOS typography scale plus the island-specific compact scale. Every size, line height and tracking
/// value is defined here (or in <see cref="IosTextScale"/>); renderers keep their existing
/// <see cref="TextStyle"/> vocabulary and <see cref="TextEngine"/> resolves it through
/// <see cref="Apply"/>. This keeps the current call sites stable while moving the actual painting to
/// a single, testable scale.
/// </summary>
public static class TypographyScale
{
    /// <summary>Canonical iOS SF text styles (size / line height in DIP).</summary>
    public static TypographyToken LargeTitle { get; } = new(
        "ios.large-title", FontRole.DisplayBold,
        IosTextScale.LargeTitleSize, IosTextScale.LargeTitleLine);

    /// <summary>iOS Title1.</summary>
    public static TypographyToken Title1 { get; } = new(
        "ios.title1", FontRole.DisplaySemiBold,
        IosTextScale.Title1Size, IosTextScale.Title1Line);

    /// <summary>iOS Title2.</summary>
    public static TypographyToken Title2 { get; } = new(
        "ios.title2", FontRole.DisplaySemiBold,
        IosTextScale.Title2Size, IosTextScale.Title2Line);

    /// <summary>iOS Title3.</summary>
    public static TypographyToken Title3 { get; } = new(
        "ios.title3", FontRole.DisplaySemiBold,
        IosTextScale.Title3Size, IosTextScale.Title3Line);

    /// <summary>iOS Headline (semibold 17/22).</summary>
    public static TypographyToken Headline { get; } = new(
        "ios.headline", FontRole.UiSemiBold,
        IosTextScale.HeadlineSize, IosTextScale.HeadlineLine);

    /// <summary>iOS Body (17/22).</summary>
    public static TypographyToken Body { get; } = new(
        "ios.body", FontRole.Ui,
        IosTextScale.BodySize, IosTextScale.BodyLine);

    /// <summary>iOS Callout (16/21).</summary>
    public static TypographyToken Callout { get; } = new(
        "ios.callout", FontRole.Ui,
        IosTextScale.CalloutSize, IosTextScale.CalloutLine);

    /// <summary>iOS Subheadline (15/20).</summary>
    public static TypographyToken Subheadline { get; } = new(
        "ios.subheadline", FontRole.Ui,
        IosTextScale.SubheadlineSize, IosTextScale.SubheadlineLine);

    /// <summary>iOS Footnote (13/18).</summary>
    public static TypographyToken Footnote { get; } = new(
        "ios.footnote", FontRole.Ui,
        IosTextScale.FootnoteSize, IosTextScale.FootnoteLine);

    /// <summary>iOS Caption1 (12/16).</summary>
    public static TypographyToken Caption1 { get; } = new(
        "ios.caption1", FontRole.Ui,
        IosTextScale.Caption1Size, IosTextScale.Caption1Line,
        IosTextScale.SmallTracking);

    /// <summary>iOS Caption2 (11/13).</summary>
    public static TypographyToken Caption2 { get; } = new(
        "ios.caption2", FontRole.Ui,
        IosTextScale.Caption2Size, IosTextScale.Caption2Line,
        IosTextScale.LabelTracking);

    // ---- Compact island scale (designed for the 36-40 DIP compact shell) ----------------

    /// <summary>Compact primary: 15/20 semibold (main activity title).</summary>
    public static TypographyToken CompactPrimary { get; } = new(
        "island.compact.primary", FontRole.UiSemiBold,
        14f, IosTextScale.SubheadlineLine);

    /// <summary>Compact secondary: 12/16 regular (subtitle / artist).</summary>
    public static TypographyToken CompactSecondary { get; } = new(
        "island.compact.secondary", FontRole.Ui,
        11f, IosTextScale.Caption1Line,
        IosTextScale.SmallTracking);

    /// <summary>Compact time/value: 13/16, tabular monospace so seconds never move the layout.</summary>
    public static TypographyToken CompactTime { get; } = new(
        "island.compact.time", FontRole.MonoSemiBold,
        IosTextScale.FootnoteSize, IosTextScale.Caption1Line,
        IosTextScale.SmallTracking,
        Tabular: true);

    /// <summary>Rail label: 11/14 regular, the small preview-strip captions.</summary>
    public static TypographyToken RailLabel { get; } = new(
        "island.rail.label", FontRole.Ui,
        IosTextScale.Caption2Size, 14f,
        IosTextScale.SmallTracking);

    /// <summary>Compact label: 11/14 semibold, slight label tracking.</summary>
    public static TypographyToken CompactLabel { get; } = new(
        "island.compact.label", FontRole.UiSemiBold,
        IosTextScale.Caption2Size, IosTextScale.Caption2Line,
        IosTextScale.LabelTracking);

    /// <summary>Compact micro: 10/12 semibold (badges, tiny counts).</summary>
    public static TypographyToken CompactMicro { get; } = new(
        "island.compact.micro", FontRole.UiSemiBold,
        IosTextScale.Caption2Size - 1f, IosTextScale.Caption1Line - 4f,
        IosTextScale.LabelTracking);

    /// <summary>Compact value: 13/16 tabular mono (HUD/percent/elapsed).</summary>
    public static TypographyToken CompactValue { get; } = new(
        "island.compact.value", FontRole.MonoSemiBold,
        IosTextScale.FootnoteSize, IosTextScale.Caption1Line,
        IosTextScale.SmallTracking,
        Tabular: true);

    // ---- Expanded island scale ----------------------------------------------------------

    /// <summary>Expanded clock: 32/38 (slightly tighter than iOS LargeTitle for a desktop panel).</summary>
    public static TypographyToken ExpandedClock { get; } = new(
        "island.expanded.clock", FontRole.DisplayBold, 32f, 38f);

    /// <summary>Expanded hero: 22/28 (metric highlight).</summary>
    public static TypographyToken ExpandedHero { get; } = new(
        "island.expanded.hero", FontRole.DisplaySemiBold,
        IosTextScale.Title2Size, IosTextScale.Title2Line);

    /// <summary>
    /// Expanded media title: 20/21. prototype2 draws <c>.media-info .media-title</c> at 20 px with
    /// <c>line-height: 1.05</c>; the legacy bridge used to snap its 20 DIP style to <see cref="ExpandedHero"/>
    /// and silently painted 22 DIP (+10 %), which the audit measured on the real media page.
    /// </summary>
    public static TypographyToken ExpandedMediaTitle { get; } = new(
        "island.expanded.media-title", FontRole.DisplaySemiBold,
        20f, 21f);

    /// <summary>Expanded title: 15/20 semibold (page / card section title).</summary>
    public static TypographyToken ExpandedTitle { get; } = new(
        "island.expanded.title", FontRole.UiSemiBold,
        IosTextScale.SubheadlineSize, IosTextScale.SubheadlineLine);

    /// <summary>Expanded row title: 13/16 semibold.</summary>
    public static TypographyToken ExpandedRowTitle { get; } = new(
        "island.expanded.row-title", FontRole.UiSemiBold,
        IosTextScale.FootnoteSize, IosTextScale.Caption1Line);

    /// <summary>Expanded body: 12/16 regular.</summary>
    public static TypographyToken ExpandedBody { get; } = new(
        "island.expanded.body", FontRole.Ui,
        11.5f, IosTextScale.Caption1Line,
        IosTextScale.SmallTracking);

    /// <summary>Expanded body medium: 13/18 for lyric-style content.</summary>
    public static TypographyToken ExpandedLyric { get; } = new(
        "island.expanded.lyric", FontRole.UiMedium,
        IosTextScale.FootnoteSize, IosTextScale.FootnoteLine,
        IosTextScale.SmallTracking);

    /// <summary>Expanded metadata: 10/13 regular.</summary>
    public static TypographyToken ExpandedMeta { get; } = new(
        "island.expanded.meta", FontRole.Ui,
        10f, IosTextScale.Caption2Line,
        IosTextScale.SmallTracking);

    /// <summary>Expanded metadata strong: 10/13 semibold.</summary>
    public static TypographyToken ExpandedMetaStrong { get; } = new(
        "island.expanded.meta-strong", FontRole.UiSemiBold,
        10f, IosTextScale.Caption2Line,
        IosTextScale.LabelTracking);

    /// <summary>Expanded monospace: 11/14 tabular.</summary>
    public static TypographyToken ExpandedMono { get; } = new(
        "island.expanded.mono", FontRole.Mono,
        IosTextScale.Caption2Size, 14f,
        IosTextScale.SmallTracking,
        Tabular: true);

    /// <summary>Expanded section eyebrow: 10/13 semibold, uppercase tracking.</summary>
    public static TypographyToken ExpandedSection { get; } = new(
        "island.expanded.section", FontRole.UiSemiBold,
        10f, IosTextScale.Caption2Line,
        IosTextScale.LabelTracking);

    /// <summary>Expanded widget title: 20/25.</summary>
    public static TypographyToken ExpandedWidgetTitle { get; } = new(
        "island.expanded.widget-title", FontRole.DisplaySemiBold,
        IosTextScale.Title3Size, IosTextScale.Title3Line);

    /// <summary>The canonical iOS scale in descending order (for docs/tests).</summary>
    public static IReadOnlyList<TypographyToken> Ios { get; } =
    [
        LargeTitle, Title1, Title2, Title3, Headline, Body, Callout, Subheadline, Footnote, Caption1, Caption2,
    ];

    // Legacy styles currently authored in IslandMetrics. They are intentionally kept here as a
    // compatibility bridge until the visual line replaces every call site with a semantic token.
    // Resolution also accepts a font-scale multiplier: a 1.2x 13.5 DIP legacy title resolves to
    // 1.2x 15 DIP, preserving the user's text-size setting.
    private static readonly (FontRole Role, float LegacySize, TypographyToken Token)[] Legacy =
    [
        (FontRole.Ui, 7f, ExpandedMeta),
        (FontRole.Ui, 7.5f, ExpandedMeta),
        (FontRole.Ui, 8f, ExpandedMeta),
        (FontRole.Ui, 8.5f, ExpandedMeta),
        (FontRole.Ui, 9f, RailLabel),
        (FontRole.Ui, 9.5f, ExpandedBody),
        (FontRole.Ui, 10f, CompactSecondary),
        (FontRole.Ui, 10.5f, ExpandedBody),
        (FontRole.Ui, 11f, ExpandedBody),
        (FontRole.Ui, 11.5f, ExpandedBody),
        (FontRole.UiMedium, 10.5f, ExpandedBody),
        (FontRole.UiMedium, 11.5f, ExpandedBody),
        (FontRole.UiMedium, 13f, ExpandedLyric),
        (FontRole.UiSemiBold, 7.5f, ExpandedMetaStrong),
        (FontRole.UiSemiBold, 8f, ExpandedMetaStrong),
        (FontRole.UiSemiBold, 8.5f, ExpandedMetaStrong),
        (FontRole.UiSemiBold, 9f, CompactMicro),
        (FontRole.UiSemiBold, 10f, ExpandedRowTitle),
        (FontRole.UiSemiBold, 10.5f, ExpandedRowTitle),
        (FontRole.UiSemiBold, 11f, CompactLabel),
        (FontRole.UiSemiBold, 11.5f, ExpandedRowTitle),
        (FontRole.UiSemiBold, 13f, CompactPrimary),
        (FontRole.UiSemiBold, 13.5f, CompactPrimary),
        (FontRole.UiSemiBold, 14f, CompactPrimary),
        (FontRole.DisplaySemiBold, 13.5f, CompactTime),
        (FontRole.DisplaySemiBold, 17f, ExpandedWidgetTitle),
        (FontRole.DisplaySemiBold, 20f, ExpandedMediaTitle),
        (FontRole.DisplayBold, 30f, ExpandedClock),
        (FontRole.Mono, 8f, ExpandedMono),
        (FontRole.Mono, 8.5f, ExpandedMono),
        (FontRole.Mono, 9.5f, ExpandedMono),
        (FontRole.Mono, 12.5f, CompactValue),
        (FontRole.MonoSemiBold, 12.5f, CompactValue),
    ];

    /// <summary>
    /// Resolves a legacy <see cref="TextStyle"/> to the iOS scale. Explicit token styles pass through;
    /// unknown styles keep their original size so new features never silently change.
    /// </summary>
    public static TextStyle Apply(TextStyle style)
    {
        if (!string.IsNullOrEmpty(style.Token) || style.LineHeight.HasValue)
        {
            return style;
        }

        var best = default((FontRole Role, float LegacySize, TypographyToken Token));
        var bestScore = float.PositiveInfinity;
        foreach (var binding in Legacy)
        {
            if (binding.Role != style.Role)
            {
                continue;
            }

            var ratio = style.Size / binding.LegacySize;
            if (ratio < 0.79f || ratio > 1.61f)
            {
                continue;
            }

            // FontScaleMetrics.Style re-creates a two-argument style, so the token identity is
            // lost before Apply. Prefer a binding whose ratio lands on a readable scale step
            // (1.0, 1.2, 1.6, ...): this keeps 10 -> 16.0 resolving from the 10 DIP base rather
            // than from a nearby 11.5 DIP base whose ratio is not a real scale.
            var rounded = MathF.Round(ratio * 20f) / 20f;
            var score = (MathF.Abs(ratio - rounded) * 1000f) + MathF.Abs(ratio - 1f);
            if (score < bestScore)
            {
                bestScore = score;
                best = binding;
            }
        }

        if (best.Token.Name is null)
        {
            return style;
        }

        var scale = style.Size / best.LegacySize;
        return new TextStyle(
            best.Token.Role,
            best.Token.Size * scale,
            best.Token.LineHeight * scale,
            best.Token.Tracking * scale,
            best.Token.Tabular,
            best.Token.Name);
    }
}
