namespace FluidBar.Core.Layout;

/// <summary>
/// Shared text-colour tokens for every FluidBar surface. The island, settings window and marketplace
/// must reference these constants instead of hard-coding their own tertiary greys: the island used
/// #80808A while settings used #86868F, which read as two different "hint" intensities on the same
/// desktop (task-36).
/// </summary>
public static class TextTokens
{
    /// <summary>Tertiary/hint text on dark surfaces (#86868F). Island shell 5.55:1, raised cards 5.09:1, settings window 5.41:1.</summary>
    public const string TertiaryDark = "#86868F";

    /// <summary>Tertiary/hint text on light surfaces (#666D78). Island light 4.83:1, settings light 5.0:1.</summary>
    public const string TertiaryLight = "#666D78";
}

/// <summary>
/// Apple SF text-style scale in points/DIP. These are the canonical iOS values from the
/// Human Interface Guidelines; island-specific compact/expanded tokens live in
/// <c>FluidBar.Island.TypographyScale</c> and must reference these constants rather than
/// repeating sizes. Line heights are the SF default line boxes.
/// </summary>
public static class IosTextScale
{
    /// <summary>LargeTitle 34 pt / 41 line.</summary>
    public const float LargeTitleSize = 34f;
    /// <summary>LargeTitle line height.</summary>
    public const float LargeTitleLine = 41f;

    /// <summary>Title1 28 pt / 34 line.</summary>
    public const float Title1Size = 28f;
    /// <summary>Title1 line height.</summary>
    public const float Title1Line = 34f;

    /// <summary>Title2 22 pt / 28 line.</summary>
    public const float Title2Size = 22f;
    /// <summary>Title2 line height.</summary>
    public const float Title2Line = 28f;

    /// <summary>Title3 20 pt / 25 line.</summary>
    public const float Title3Size = 20f;
    /// <summary>Title3 line height.</summary>
    public const float Title3Line = 25f;

    /// <summary>Headline 17 pt / 22 line, semibold.</summary>
    public const float HeadlineSize = 17f;
    /// <summary>Headline line height.</summary>
    public const float HeadlineLine = 22f;

    /// <summary>Body 17 pt / 22 line.</summary>
    public const float BodySize = 17f;
    /// <summary>Body line height.</summary>
    public const float BodyLine = 22f;

    /// <summary>Callout 16 pt / 21 line.</summary>
    public const float CalloutSize = 16f;
    /// <summary>Callout line height.</summary>
    public const float CalloutLine = 21f;

    /// <summary>Subheadline 15 pt / 20 line.</summary>
    public const float SubheadlineSize = 15f;
    /// <summary>Subheadline line height.</summary>
    public const float SubheadlineLine = 20f;

    /// <summary>Footnote 13 pt / 18 line.</summary>
    public const float FootnoteSize = 13f;
    /// <summary>Footnote line height.</summary>
    public const float FootnoteLine = 18f;

    /// <summary>Caption1 12 pt / 16 line.</summary>
    public const float Caption1Size = 12f;
    /// <summary>Caption1 line height.</summary>
    public const float Caption1Line = 16f;

    /// <summary>Caption2 11 pt / 13 line.</summary>
    public const float Caption2Size = 11f;
    /// <summary>Caption2 line height.</summary>
    public const float Caption2Line = 13f;

    /// <summary>Body tracking: SF at 17 pt has effectively zero tracking.</summary>
    public const float BodyTracking = 0f;

    /// <summary>Small-text tracking: +0.1 DIP at 12-13 pt improves legibility on dark glass.</summary>
    public const float SmallTracking = 0.1f;

    /// <summary>Micro/uppercase label tracking: +0.2 DIP at 9-11 pt.</summary>
    public const float LabelTracking = 0.2f;
}
