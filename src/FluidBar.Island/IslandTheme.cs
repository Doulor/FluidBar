using SkiaSharp;

namespace FluidBar.Island;

/// <summary>Island surface theme. The approved design keeps the island "Obsidian" in every desktop
/// theme (spec 3.3); <see cref="Light"/> exists for a light island variant / high-contrast previews
/// and is fully supported by the renderer.</summary>
public enum IslandThemeMode
{
    /// <summary>Obsidian dark surface (default).</summary>
    Dark,

    /// <summary>Porcelain light surface.</summary>
    Light,
}

/// <summary>Shell material tier (spec 3.4).</summary>
public enum IslandMaterial
{
    /// <summary>Tier 0: opaque obsidian fill + hairline (default).</summary>
    Obsidian,

    /// <summary>Tier 1: simulated glass (lighter fill + one top highlight; no blur).</summary>
    SimulatedGlass,
}

/// <summary>
/// Immutable palette + material for the Skia island. Values are DIP/colour tokens ported from the
/// approved prototype and the App <c>DesignTokens</c>; the renderer never resolves theme resources.
/// </summary>
public sealed record IslandTheme
{
    /// <summary>Theme identity.</summary>
    public IslandThemeMode Mode { get; init; } = IslandThemeMode.Dark;

    /// <summary>Material tier.</summary>
    public IslandMaterial Material { get; init; } = IslandMaterial.Obsidian;

    /// <summary>Shell body colour.</summary>
    public SKColor Background { get; init; } = new(0x08, 0x08, 0x0A);

    /// <summary>Raised card colour.</summary>
    public SKColor BackgroundRaised { get; init; } = new(0x14, 0x14, 0x18);

    /// <summary>Inset track/well colour.</summary>
    public SKColor BackgroundInset { get; init; } = new(0x0E, 0x0E, 0x11);

    /// <summary>Hairline stroke colour (white @8 %).</summary>
    public SKColor Line { get; init; } = new(0xFF, 0xFF, 0xFF, 0x14);

    /// <summary>Strong hairline/border colour (white @11 %).</summary>
    public SKColor LineStrong { get; init; } = new(0xFF, 0xFF, 0xFF, 0x1C);

    /// <summary>Primary text/icon colour.</summary>
    public SKColor Foreground { get; init; } = SKColors.White;

    /// <summary>Secondary text colour.</summary>
    public SKColor ForegroundSecondary { get; init; } = new(0x9B, 0x9B, 0xA5);

    /// <summary>Tertiary text colour.</summary>
    public SKColor ForegroundTertiary { get; init; } = new(0x6E, 0x6E, 0x78);

    /// <summary>User accent (default cyan).</summary>
    public SKColor Accent { get; init; } = new(0x4C, 0xC9, 0xF0);

    /// <summary>Media/track accent (prototype pink).</summary>
    public SKColor TrackAccent { get; init; } = new(0xF0, 0x6A, 0xB0);

    /// <summary>Success colour.</summary>
    public SKColor Success { get; init; } = new(0x34, 0xC7, 0x59);

    /// <summary>Warning colour.</summary>
    public SKColor Warning { get; init; } = new(0xFF, 0x95, 0x00);

    /// <summary>Danger colour.</summary>
    public SKColor Danger { get; init; } = new(0xFF, 0x45, 0x3A);

    /// <summary>Informational blue.</summary>
    public SKColor Info { get; init; } = new(0x0A, 0x84, 0xFF);

    /// <summary>Artwork gradient end colour.</summary>
    public SKColor ArtEnd { get; init; } = new(0x1A, 0x1A, 0x22);

    /// <summary>HUD track colour.</summary>
    public SKColor HudTrack { get; init; } = new(0x23, 0x23, 0x2A);

    /// <summary>Shell fill opacity (prototype2 default <c>--island-fill: 0.97</c>).</summary>
    public double FillOpacity { get; init; } = 0.97;

    /// <summary>Shadow colour.</summary>
    public SKColor ShadowColor { get; init; } = SKColors.Black;

    // Prototype2 paints the shell with two box-shadow layers (styles.css .island):
    //   0 18px 44px -16px rgba(0,0,0,.78), 0 3px 10px -4px rgba(0,0,0,.42)
    // CSS blur radius maps to a Gaussian sigma of blur / 2.

    /// <summary>Outer shadow opacity (prototype2 0.78).</summary>
    public double ShadowOpacity { get; init; } = 0.78;

    /// <summary>Outer shadow offset Y in DIP (prototype2 18).</summary>
    public double ShadowOffsetY { get; init; } = 18;

    /// <summary>Outer shadow blur radius in DIP (prototype2 44; sigma = blur / 2).</summary>
    public double ShadowBlur { get; init; } = 44;

    /// <summary>Outer shadow spread in DIP (prototype2 −16).</summary>
    public double ShadowSpread { get; init; } = -16;

    /// <summary>Close shadow opacity (prototype2 0.42).</summary>
    public double ShadowOpacity2 { get; init; } = 0.42;

    /// <summary>Close shadow offset Y in DIP (prototype2 3).</summary>
    public double ShadowOffsetY2 { get; init; } = 3;

    /// <summary>Close shadow blur radius in DIP (prototype2 10; sigma = blur / 2).</summary>
    public double ShadowBlur2 { get; init; } = 10;

    /// <summary>Close shadow spread in DIP (prototype2 −4).</summary>
    public double ShadowSpread2 { get; init; } = -4;

    /// <summary>Whether the surface is light (text/hairline contrast helpers).</summary>
    public bool IsLight => Mode == IslandThemeMode.Light;

    /// <summary>Approved Obsidian theme (default).</summary>
    public static IslandTheme Dark { get; } = new();

    /// <summary>Porcelain light island variant (opt-in; the approved default stays dark).</summary>
    public static IslandTheme Light { get; } = new()
    {
        Mode = IslandThemeMode.Light,
        Background = new SKColor(0xF5, 0xF6, 0xF8),
        BackgroundRaised = new SKColor(0xFF, 0xFF, 0xFF),
        BackgroundInset = new SKColor(0xE9, 0xEB, 0xEF),
        Line = new SKColor(0x11, 0x18, 0x27, 0x14),
        LineStrong = new SKColor(0x11, 0x18, 0x27, 0x22),
        Foreground = new SKColor(0x14, 0x16, 0x1A),
        ForegroundSecondary = new SKColor(0x5B, 0x62, 0x70),
        ForegroundTertiary = new SKColor(0x89, 0x91, 0x9C),
        Accent = new SKColor(0x0A, 0x84, 0xFF),
        TrackAccent = new SKColor(0xE0, 0x55, 0x9B),
        ArtEnd = new SKColor(0xDD, 0xE3, 0xEA),
        HudTrack = new SKColor(0xE2, 0xE5, 0xEA),
        FillOpacity = 0.97,
        ShadowColor = new SKColor(0x18, 0x28, 0x3A),
        // Prototype2 light shell: 0 18px 44px -18px rgba(24,40,56,.52), 0 3px 10px -5px rgba(24,40,56,.22).
        ShadowOpacity = 0.52,
        ShadowSpread = -18,
        ShadowOpacity2 = 0.22,
        ShadowSpread2 = -5,
    };

    /// <summary>Returns the standard theme for a mode.</summary>
    public static IslandTheme ForMode(IslandThemeMode mode) => mode == IslandThemeMode.Light ? Light : Dark;

    /// <summary>Returns a copy of this theme with a new accent colour.</summary>
    public IslandTheme WithAccent(SKColor accent) => this with { Accent = accent };

    /// <summary>Returns a copy of this theme with a new material tier.</summary>
    public IslandTheme WithMaterial(IslandMaterial material) => this with { Material = material };

    /// <summary>
    /// Parses a <c>#RRGGBB</c>/<c>#AARRGGBB</c> accent string, falling back to
    /// <paramref name="fallback"/> when the value is null, empty or malformed. Never throws.
    /// </summary>
    public static SKColor ParseColor(string? hex, SKColor fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return fallback;
        }

        var text = hex.AsSpan().Trim();
        if (text.Length > 0 && text[0] == '#')
        {
            text = text[1..];
        }

        if (text.Length != 6 && text.Length != 8)
        {
            return fallback;
        }

        if (!uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var value))
        {
            return fallback;
        }

        // 6 digits = RRGGBB (opaque); 8 digits = AARRGGBB (the App stores accent fallbacks that way).
        // The old 8-digit branch read it as RRGGBBAA, which shifted every translucent-normalised accent
        // one byte (e.g. "#FF4CC9F0" rendered as RGB(FF,4C,C9) instead of RGB(4C,C9,F0)).
        return text.Length == 6
            ? new SKColor((byte)(value >> 16), (byte)(value >> 8), (byte)value)
            : new SKColor((byte)(value >> 16), (byte)(value >> 8), (byte)value, (byte)(value >> 24));
    }
}
