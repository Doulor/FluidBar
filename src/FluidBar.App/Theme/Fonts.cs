using Avalonia.Media;

namespace FluidBar.App.Theme;

/// <summary>
/// Explicit font stacks (spec 3.5). CJK glyphs come from the system fonts listed after the Latin
/// faces; Avalonia does not package a CJK font, so the fallback order must be explicit.
/// </summary>
public static class Fonts
{
    /// <summary>UI text stack (Segoe UI Variable Text → Segoe UI → Microsoft YaHei UI → ...).</summary>
    public static FontFamily Ui { get; } = FontFamily.Parse(
        "Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI, Microsoft YaHei, Yu Gothic UI, Malgun Gothic, Arial");

    /// <summary>Display text stack (Segoe UI Variable Display → Segoe UI Semibold → ...).</summary>
    public static FontFamily Display { get; } = FontFamily.Parse(
        "Segoe UI Variable Display, Segoe UI, Microsoft YaHei UI, Yu Gothic UI, Arial");

    /// <summary>Monospaced stack (Cascadia Mono → Consolas → ...).</summary>
    public static FontFamily Mono { get; } = FontFamily.Parse(
        "Cascadia Mono, Cascadia Code, Consolas, Microsoft YaHei UI, Courier New");

    /// <summary>Icon stack (spec 3.6 uses a 24×24 vector set instead; kept for fallback text icons).</summary>
    public static FontFamily Icons { get; } = FontFamily.Parse(
        "Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol");

    /// <summary>Regular UI typeface.</summary>
    public static Typeface Regular { get; } = new(Ui, FontStyle.Normal, FontWeight.Normal);

    /// <summary>SemiBold UI typeface (titles).</summary>
    public static Typeface SemiBold { get; } = new(Ui, FontStyle.Normal, FontWeight.SemiBold);

    /// <summary>Bold display typeface (dashboard clock).</summary>
    public static Typeface DisplayBold { get; } = new(Display, FontStyle.Normal, FontWeight.Bold);

    /// <summary>SemiBold display typeface (page titles).</summary>
    public static Typeface DisplaySemiBold { get; } = new(Display, FontStyle.Normal, FontWeight.SemiBold);

    /// <summary>Monospaced typeface (clock, tabular values).</summary>
    public static Typeface MonoRegular { get; } = new(Mono, FontStyle.Normal, FontWeight.Normal);

    /// <summary>Monospaced SemiBold typeface (HUD values / compact clock).</summary>
    public static Typeface MonoSemiBold { get; } = new(Mono, FontStyle.Normal, FontWeight.SemiBold);
}
