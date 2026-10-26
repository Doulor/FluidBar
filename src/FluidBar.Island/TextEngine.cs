using HarfBuzzSharp;
using SkiaSharp;

namespace FluidBar.Island;

/// <summary>Font role resolved by the text engine (variable weights via DirectWrite).</summary>
public enum FontRole
{
    /// <summary>Segoe UI Variable Text / Microsoft YaHei UI / Segoe UI, regular.</summary>
    Ui,

    /// <summary>UI medium (500) — prototype2's agent task/action text weight.</summary>
    UiMedium,

    /// <summary>UI semibold (600).</summary>
    UiSemiBold,

    /// <summary>Display semibold (600) for page titles.</summary>
    DisplaySemiBold,

    /// <summary>Display bold (700) for the dashboard clock.</summary>
    DisplayBold,

    /// <summary>Cascadia Mono / Consolas, regular.</summary>
    Mono,

    /// <summary>Monospace semibold for clocks and HUD values.</summary>
    MonoSemiBold,
}

/// <summary>Text alignment inside a drawing rectangle.</summary>
public enum TextAlign
{
    /// <summary>Left aligned.</summary>
    Left,

    /// <summary>Centred.</summary>
    Center,

    /// <summary>Right aligned.</summary>
    Right,
}

/// <summary>A font role + size pair (size in DIP; fractional sizes are supported).</summary>
public readonly record struct TextStyle(FontRole Role, float Size);

/// <summary>One measured/laid-out string: shaped runs, cached text blobs and line metrics.</summary>
public sealed class TextLayout : IDisposable
{
    /// <summary>Final (possibly ellipsis-trimmed) text.</summary>
    public required string Text { get; init; }

    /// <summary>One blob per fallback run.</summary>
    public required SKTextBlob[] Blobs { get; init; }

    /// <summary>Per-run x offsets in DIP.</summary>
    public required float[] Offsets { get; init; }

    /// <summary>Measured width in DIP.</summary>
    public required float Width { get; init; }

    /// <summary>Line height (primary font spacing) in DIP.</summary>
    public required float Height { get; init; }

    /// <summary>Primary font ascent (negative) in DIP.</summary>
    public required float Ascent { get; init; }

    /// <summary>Glyph count across runs.</summary>
    public required int GlyphCount { get; init; }

    /// <summary>Whether the text was ellipsis-trimmed.</summary>
    public required bool Trimmed { get; init; }

    /// <summary>Number of fallback runs.</summary>
    public required int RunCount { get; init; }

    /// <summary>Disposes the cached text blobs.</summary>
    public void Dispose()
    {
        foreach (var blob in Blobs)
        {
            blob.Dispose();
        }
    }
}

/// <summary>
/// Text stack for the island: explicit font fallback (Segoe UI Variable → Microsoft YaHei UI →
/// Segoe UI), HarfBuzz shaping for emoji clusters, a bounded LRU layout cache, character-ellipsis
/// trimming and CJK-aware line metrics. Warm layout hits are allocation-free.
/// </summary>
public sealed class TextEngine : IDisposable
{
    /// <summary>Default bounded layout cache capacity (entries).</summary>
    public const int DefaultCacheCapacity = 200;

    /// <summary>Maximum cached per-codepoint fallback typefaces.</summary>
    public const int FallbackCacheCapacity = 512;

    private static readonly (int Low, int High)[] EmojiRanges =
    [
        (0x1F000, 0x1FAFF),
        (0x2600, 0x27BF),
        (0x2B00, 0x2BFF),
        (0xFE00, 0xFE0F),
        (0x200D, 0x200D),
        (0x1F1E6, 0x1F1FF),
    ];

    private static readonly (int Low, int High)[] CjkRanges =
    [
        (0x2E80, 0x2FFF),
        (0x3000, 0x30FF),
        (0x31F0, 0x31FF),
        (0x3400, 0x4DBF),
        (0x4E00, 0x9FFF),
        (0xF900, 0xFAFF),
        (0xFE30, 0xFE4F),
        (0xFF00, 0xFFEF),
        (0x20000, 0x2FA1F),
    ];

    private readonly Dictionary<(FontRole Role, float Size), SKFont> _fonts = [];
    private readonly Dictionary<(SKTypeface Typeface, float Size), SKFont> _runFonts = [];
    private readonly Dictionary<int, SKTypeface?> _fallbacks = [];
    private readonly Dictionary<TextKey, CacheEntry> _cache = [];
    private readonly LinkedList<TextKey> _lru = [];
    private readonly List<SKTypeface> _ownedTypefaces = [];
    private readonly int _capacity;
    private readonly SKPaint _paint = new() { IsAntialias = true };

    private readonly SKTypeface _ui;
    private readonly SKTypeface _uiMedium;
    private readonly SKTypeface _uiSemibold;
    private readonly SKTypeface _displaySemibold;
    private readonly SKTypeface _displayBold;
    private readonly SKTypeface _mono;
    private readonly SKTypeface _monoSemibold;
    private readonly SKTypeface _cjkRegular;
    private readonly SKTypeface _cjkSemibold;
    private readonly SKTypeface _emoji;

    private Blob? _emojiBlob;
    private Face? _emojiFace;
    private bool _emojiLoaded;
    private readonly Dictionary<float, HarfBuzzSharp.Font> _emojiFonts = [];

    private int _hits;
    private int _misses;
    private int _evictions;
    private int _measureCalls;
    private int _trimSteps;
    private long _glyphs;

    /// <summary>Creates the text engine with a bounded layout cache.</summary>
    /// <param name="cacheCapacity">Layout cache capacity (entries; default 200).</param>
    public TextEngine(int cacheCapacity = DefaultCacheCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cacheCapacity, 1);
        _capacity = cacheCapacity;
        _ui = Resolve(["Segoe UI Variable Text", "Microsoft YaHei UI", "Segoe UI"], SKFontStyleWeight.Normal);
        _uiMedium = Resolve(["Segoe UI Variable Text", "Microsoft YaHei UI", "Segoe UI"], SKFontStyleWeight.Medium);
        _uiSemibold = Resolve(["Segoe UI Variable Text", "Microsoft YaHei UI", "Segoe UI Semibold", "Segoe UI"], SKFontStyleWeight.SemiBold);
        _displaySemibold = Resolve(["Segoe UI Variable Display", "Microsoft YaHei UI", "Segoe UI Semibold", "Segoe UI"], SKFontStyleWeight.SemiBold);
        _displayBold = Resolve(["Segoe UI Variable Display", "Microsoft YaHei UI", "Segoe UI Bold", "Segoe UI"], SKFontStyleWeight.Bold);
        _mono = Resolve(["Cascadia Mono", "Consolas", "Microsoft YaHei UI"], SKFontStyleWeight.Normal);
        _monoSemibold = Resolve(["Cascadia Mono", "Consolas", "Microsoft YaHei UI"], SKFontStyleWeight.SemiBold);
        _cjkRegular = Resolve(["Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI"], SKFontStyleWeight.Normal);
        _cjkSemibold = Resolve(["Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI"], SKFontStyleWeight.SemiBold);
        _emoji = Resolve(["Segoe UI Emoji", "Segoe UI Symbol"], SKFontStyleWeight.Normal);
    }

    /// <summary>
    /// Loads the colour-emoji HarfBuzz face on first emoji text. Previously the 9 MB
    /// `seguiemj.ttf` blob/face was created in the constructor even when no emoji was ever painted,
    /// which cost working set (file mapping + face) for the whole session.
    /// </summary>
    private void EnsureEmojiFace()
    {
        if (_emojiLoaded)
        {
            return;
        }

        _emojiLoaded = true;
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "seguiemj.ttf");
            if (File.Exists(path))
            {
                _emojiBlob = Blob.FromFile(path);
                _emojiFace = new Face(_emojiBlob, 0);
            }
        }
        catch
        {
            _emojiBlob?.Dispose();
            _emojiBlob = null;
            _emojiFace = null;
        }
    }

    /// <summary>Entries currently held by the layout cache.</summary>
    public int CacheCount => _cache.Count;

    /// <summary>Whether the colour-emoji HarfBuzz face has been loaded (lazy since the perf audit).</summary>
    public bool EmojiFaceLoaded => _emojiFace is not null;

    /// <summary>Bounded layout cache capacity.</summary>
    public int CacheCapacity => _capacity;

    /// <summary>Warm layout hits.</summary>
    public int Hits => _hits;

    /// <summary>Cold layout misses.</summary>
    public int Misses => _misses;

    /// <summary>LRU evictions.</summary>
    public int Evictions => _evictions;

    /// <summary>Text width measurements performed (cold paths).</summary>
    public int MeasureCalls => _measureCalls;

    /// <summary>Ellipsis binary-search measurement steps.</summary>
    public int TrimSteps => _trimSteps;

    /// <summary>Glyphs shaped since the last reset.</summary>
    public long LaidOutGlyphs => _glyphs;

    /// <summary>Approximate retained bytes of the layout cache.</summary>
    public long ApproxCacheBytes
    {
        get
        {
            long total = 0;
            foreach (var entry in _cache.Values)
            {
                total += 160 + (entry.Layout.Text.Length * 2L) + (entry.Layout.Blobs.Length * 96L);
            }

            return total;
        }
    }

    /// <summary>Line height of a style in DIP.</summary>
    public float LineHeight(TextStyle style) => GetFont(style).Spacing;

    /// <summary>Measures a single line (ellipsis-trimmed to <paramref name="maxWidth"/> when set).</summary>
    public float Measure(string text, TextStyle style, float maxWidth = float.PositiveInfinity)
        => GetLayout(text, style, maxWidth).Width;

    /// <summary>Gets a cached layout (LRU). Warm hits are allocation-free.</summary>
    public TextLayout GetLayout(string text, TextStyle style, float maxWidth)
    {
        var bucket = maxWidth >= float.PositiveInfinity ? int.MaxValue : (int)MathF.Round(maxWidth * 2);
        var key = new TextKey(text, style.Role, style.Size, bucket);
        if (_cache.TryGetValue(key, out var entry))
        {
            _hits++;
            _lru.Remove(entry.Node);
            _lru.AddFirst(entry.Node);
            return entry.Layout;
        }

        _misses++;
        var layout = Build(text, style, maxWidth);
        var node = _lru.AddFirst(key);
        _cache[key] = new CacheEntry(layout, node);
        if (_cache.Count > _capacity)
        {
            var last = _lru.Last!;
            if (_cache.Remove(last.Value, out var evicted))
            {
                evicted.Layout.Dispose();
                _evictions++;
            }

            _lru.RemoveLast();
        }

        return layout;
    }

    /// <summary>Draws one line inside <paramref name="area"/> with character ellipsis.</summary>
    public void Draw(
        SKCanvas canvas,
        string? text,
        TextStyle style,
        SKRect area,
        SKColor color,
        TextAlign align = TextAlign.Left,
        float opacity = 1f,
        SKMaskFilter? maskFilter = null)
    {
        if (string.IsNullOrEmpty(text) || area.Width <= 1 || area.Height <= 1 || opacity <= 0.01f)
        {
            return;
        }

        var layout = GetLayout(text, style, area.Width);
        DrawLayout(canvas, layout, area, color, align, opacity, maskFilter);
    }

    /// <summary>Draws a previously measured layout inside <paramref name="area"/>.</summary>
    public void DrawLayout(
        SKCanvas canvas,
        TextLayout layout,
        SKRect area,
        SKColor color,
        TextAlign align,
        float opacity = 1f,
        SKMaskFilter? maskFilter = null)
    {
        if (opacity <= 0.01f)
        {
            return;
        }

        var x = align switch
        {
            TextAlign.Center => area.Left + ((area.Width - layout.Width) / 2),
            TextAlign.Right => area.Right - layout.Width,
            _ => area.Left,
        };
        var baseline = area.Top + ((area.Height - layout.Height) / 2) - layout.Ascent;
        var resolved = opacity >= 0.999f
            ? color
            : color.WithAlpha((byte)Math.Clamp(color.Alpha * opacity, 0, 255));

        // Light text gets a soft dark shadow so white labels stay readable on a white/photo backdrop
        // (reported 2026-10-23). Cheap: one extra text pass, no blur filter, no allocation.
        var luminance = ((0.2126 * resolved.Red) + (0.7152 * resolved.Green) + (0.0722 * resolved.Blue)) / 255.0;
        if (luminance > 0.55)
        {
            _paint.Color = new SKColor(0, 0, 0, (byte)Math.Clamp(resolved.Alpha * 0.45f, 0, 255));
            _paint.MaskFilter = maskFilter;
            for (var i = 0; i < layout.Blobs.Length; i++)
            {
                canvas.DrawText(layout.Blobs[i], x + layout.Offsets[i], baseline + 0.9f, _paint);
            }
        }

        _paint.Color = resolved;
        _paint.MaskFilter = maskFilter;
        for (var i = 0; i < layout.Blobs.Length; i++)
        {
            canvas.DrawText(layout.Blobs[i], x + layout.Offsets[i], baseline, _paint);
        }

        // The paint is shared: a blur left on it would smear every later draw of this frame.
        _paint.MaskFilter = null;
    }

    /// <summary>Drops every cached layout (locale/theme/DPI change) while keeping fonts and typefaces.</summary>
    public void ClearCache()
    {
        foreach (var entry in _cache.Values)
        {
            entry.Layout.Dispose();
        }

        _cache.Clear();
        _lru.Clear();
        _fallbacks.Clear();
        ResetStats();
    }

    /// <summary>Resets the counters.</summary>
    public void ResetStats()
    {
        _hits = 0;
        _misses = 0;
        _evictions = 0;
        _measureCalls = 0;
        _trimSteps = 0;
        _glyphs = 0;
    }

    private TextLayout Build(string text, TextStyle style, float maxWidth)
    {
        text = SanitizeSurrogates(text);
        var primaryFont = GetFont(style);
        var trimmed = text;
        var width = MeasureTextWidth(text, style, primaryFont);
        var trimmedFlag = false;
        if (width > maxWidth && maxWidth > 1)
        {
            trimmed = Trim(text, style, primaryFont, maxWidth, out width);
            trimmedFlag = true;
        }

        var runs = Segment(trimmed, style, primaryFont);
        var blobs = new SKTextBlob[runs.Count];
        var offsets = new float[runs.Count];
        var x = 0f;
        var glyphs = 0;
        for (var i = 0; i < runs.Count; i++)
        {
            var run = runs[i];
            blobs[i] = CreateBlob(run, style, out var runWidth, out var runGlyphs);
            offsets[i] = x;
            x += runWidth;
            glyphs += runGlyphs;
        }

        _glyphs += glyphs;
        return new TextLayout
        {
            Text = trimmed,
            Blobs = blobs,
            Offsets = offsets,
            Width = x,
            Height = primaryFont.Spacing,
            Ascent = primaryFont.Metrics.Ascent,
            GlyphCount = glyphs,
            Trimmed = trimmedFlag,
            RunCount = runs.Count,
        };
    }

    /// <summary>
    /// Longest prefix (+ ellipsis) that fits <paramref name="maxWidth"/>, found by binary search.
    /// </summary>
    /// <param name="text">Source text (already surrogate-sanitised).</param>
    /// <param name="style">Text style.</param>
    /// <param name="primaryFont">Font used for measurement.</param>
    /// <param name="maxWidth">Width budget in pixels.</param>
    /// <param name="width">Measured width of the returned string.</param>
    private string Trim(string text, TextStyle style, SKFont primaryFont, float maxWidth, out float width)
    {
        const string ellipsis = "…";
        var low = 0;
        var high = text.Length;
        var best = string.Empty;
        width = 0;
        var steps = 0;
        while (low <= high)
        {
            var mid = (low + high) / 2;

            // Never split a surrogate pair: cut after the whole pair. `cut` is the probe length, `mid` stays the
            // binary-search position — using the adjusted value for the bound update made the interval stop
            // shrinking (low == high, surrogate at the split point, candidate still too wide) and the render
            // thread spun there forever, freezing the island on the notifications page (2026-10-26 dump:
            // TextEngine.Trim on the frame clock with 5h41m of CPU).
            var cut = mid;
            if (cut > 0 && cut < text.Length && char.IsHighSurrogate(text[cut - 1]))
            {
                cut++;
            }

            var length = Math.Min(cut, text.Length);
            var candidate = text[..length] + ellipsis;
            var candidateWidth = MeasureTextWidth(candidate, style, primaryFont);
            _trimSteps++;
            if (candidateWidth <= maxWidth)
            {
                best = candidate;
                width = candidateWidth;
                low = Math.Max(mid + 1, length + 1);
            }
            else
            {
                high = Math.Min(mid - 1, length - 1);
            }

            // Belt and braces: no future edit may turn this loop into a hang again. One probe per character is a
            // generous budget (the search is O(log n)) and stays unreachable while the bounds shrink.
            if (++steps > text.Length + TrimSafetySteps)
            {
                break;
            }
        }

        if (best.Length == 0)
        {
            best = ellipsis;
            width = MeasureTextWidth(best, style, primaryFont);
        }

        return best;
    }

    /// <summary>Extra probe budget above the text length before the trim loop bails out (see <see cref="Trim"/>).</summary>
    public const int TrimSafetySteps = 64;

    private float MeasureTextWidth(string text, TextStyle style, SKFont primaryFont)
    {
        _measureCalls++;
        var runs = Segment(text, style, primaryFont);
        var width = 0f;
        for (var i = 0; i < runs.Count; i++)
        {
            width += MeasureRun(runs[i], style);
        }

        return width;
    }

    private float MeasureRun(Run run, TextStyle style)
        => run.Shaped ? ShapeEmoji(run.Text, style).Width : run.Font.MeasureText(run.Text);

    private SKTextBlob CreateBlob(Run run, TextStyle style, out float width, out int glyphs)
    {
        if (run.Shaped)
        {
            var shaped = ShapeEmoji(run.Text, style);
            width = shaped.Width;
            glyphs = shaped.GlyphCount;
            return shaped.Blob;
        }

        var blob = SKTextBlob.Create(run.Text, run.Font);
        width = run.Font.MeasureText(run.Text);
        glyphs = run.Text.Length;
        if (blob is null)
        {
            throw new InvalidOperationException($"Skia could not create a text blob for '{run.Text}'.");
        }

        return blob;
    }

    /// <summary>Replaces unpaired UTF-16 surrogates with U+FFFD; Skia/char.ConvertToUtf32 requires well-formed pairs.</summary>
    private static string SanitizeSurrogates(string text)
    {
        var needsRepair = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                {
                    needsRepair = true;
                    break;
                }

                i++;
            }
            else if (char.IsLowSurrogate(text[i]))
            {
                needsRepair = true;
                break;
            }
        }

        if (!needsRepair)
        {
            return text;
        }

        var builder = new System.Text.StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                builder.Append(text[i]).Append(text[i + 1]);
                i++;
            }
            else if (char.IsSurrogate(text[i]))
            {
                builder.Append('\uFFFD');
            }
            else
            {
                builder.Append(text[i]);
            }
        }

        return builder.ToString();
    }

    private List<Run> Segment(string text, TextStyle style, SKFont primaryFont)
    {
        var runs = new List<Run>(2);
        var start = 0;
        SKTypeface? current = null;
        var shaped = false;
        for (var i = 0; i < text.Length;)
        {
            var cp = char.ConvertToUtf32(text, i);
            var length = char.IsHighSurrogate(text[i]) ? 2 : 1;
            var (typeface, isShaped) = ResolveTypeface(cp, style, primaryFont);
            if (current is null || !ReferenceEquals(typeface, current) || isShaped != shaped)
            {
                if (current is not null)
                {
                    runs.Add(new Run(text[start..i], GetRunFont(current, style.Size), shaped));
                }

                current = typeface;
                shaped = isShaped;
                start = i;
            }

            i += length;
        }

        if (current is not null)
        {
            runs.Add(new Run(text[start..], GetRunFont(current, style.Size), shaped));
        }

        return runs;
    }

    private (SKTypeface Typeface, bool Shaped) ResolveTypeface(int cp, TextStyle style, SKFont primaryFont)
    {
        if (IsEmoji(cp))
        {
            return (_emoji, true);
        }

        if (IsCjk(cp))
        {
            return (style.Role is FontRole.UiSemiBold or FontRole.DisplaySemiBold or FontRole.DisplayBold
                ? _cjkSemibold
                : _cjkRegular, false);
        }

        if (primaryFont.ContainsGlyph(cp))
        {
            return (GetPrimaryTypeface(style), false);
        }

        if (_fallbacks.Count >= FallbackCacheCapacity)
        {
            _fallbacks.Clear();
        }

        if (!_fallbacks.TryGetValue(cp, out var fallback))
        {
            fallback = SKFontManager.Default.MatchCharacter(cp);
            _fallbacks[cp] = fallback;
        }

        return (fallback ?? GetPrimaryTypeface(style), false);
    }

    private static bool IsEmoji(int cp)
    {
        foreach (var range in EmojiRanges)
        {
            if (cp >= range.Low && cp <= range.High)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCjk(int cp)
    {
        foreach (var range in CjkRanges)
        {
            if (cp >= range.Low && cp <= range.High)
            {
                return true;
            }
        }

        return false;
    }

    private SKTypeface GetPrimaryTypeface(TextStyle style) => style.Role switch
    {
        FontRole.Ui => _ui,
        FontRole.UiMedium => _uiMedium,
        FontRole.UiSemiBold => _uiSemibold,
        FontRole.DisplaySemiBold => _displaySemibold,
        FontRole.DisplayBold => _displayBold,
        FontRole.Mono => _mono,
        FontRole.MonoSemiBold => _monoSemibold,
        _ => _ui,
    };

    private SKFont GetFont(TextStyle style)
    {
        if (_fonts.TryGetValue((style.Role, style.Size), out var font))
        {
            return font;
        }

        font = new SKFont(GetPrimaryTypeface(style), style.Size)
        {
            Edging = SKFontEdging.Antialias,
            Subpixel = false,
            Hinting = SKFontHinting.Normal,
        };
        _fonts[(style.Role, style.Size)] = font;
        return font;
    }

    private SKFont GetRunFont(SKTypeface typeface, float size)
    {
        if (_runFonts.TryGetValue((typeface, size), out var font))
        {
            return font;
        }

        font = new SKFont(typeface, size)
        {
            Edging = SKFontEdging.Antialias,
            Subpixel = false,
            Hinting = SKFontHinting.Normal,
        };
        _runFonts[(typeface, size)] = font;
        return font;
    }

    private (SKTextBlob Blob, float Width, int GlyphCount) ShapeEmoji(string text, TextStyle style)
    {
        EnsureEmojiFace();
        if (_emojiFace is null)
        {
            var font = GetRunFont(_emoji, style.Size);
            var fallback = SKTextBlob.Create(text, font)
                ?? throw new InvalidOperationException("Emoji shaping unavailable.");
            return (fallback, font.MeasureText(text), text.Length);
        }

        if (!_emojiFonts.TryGetValue(style.Size, out var hbFont))
        {
            hbFont = new HarfBuzzSharp.Font(_emojiFace);
            hbFont.SetScale((int)(style.Size * 64), (int)(style.Size * 64));
            _emojiFonts[style.Size] = hbFont;
        }

        using var buffer = new HarfBuzzSharp.Buffer();
        buffer.AddUtf8(text);
        buffer.GuessSegmentProperties();
        hbFont.Shape(buffer);

        var infos = buffer.GetGlyphInfoSpan();
        var positions = buffer.GetGlyphPositionSpan();
        var builder = new SKTextBlobBuilder();
        var run = builder.AllocatePositionedRun(GetRunFont(_emoji, style.Size), infos.Length);
        var glyphSpan = run.GetGlyphSpan();
        var positionSpan = run.GetPositionSpan();
        var x = 0f;
        for (var i = 0; i < infos.Length; i++)
        {
            glyphSpan[i] = (ushort)infos[i].Codepoint;
            positionSpan[i] = new SKPoint(x + (positions[i].XOffset / 64f), positions[i].YOffset / 64f);
            x += positions[i].XAdvance / 64f;
        }

        var blob = builder.Build();
        if (blob is null)
        {
            throw new InvalidOperationException("Skia could not build a shaped emoji blob.");
        }

        return (blob, x, infos.Length);
    }

    private SKTypeface Resolve(string[] families, SKFontStyleWeight weight)
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
                Own(typeface);
                return typeface;
            }

            typeface.Dispose();
        }

        return SKTypeface.Default;
    }

    private void Own(SKTypeface typeface)
    {
        foreach (var owned in _ownedTypefaces)
        {
            if (ReferenceEquals(owned, typeface))
            {
                return;
            }
        }

        _ownedTypefaces.Add(typeface);
    }

    /// <summary>Disposes every cached layout, font and typeface.</summary>
    public void Dispose()
    {
        foreach (var entry in _cache.Values)
        {
            entry.Layout.Dispose();
        }

        _cache.Clear();
        _lru.Clear();

        foreach (var font in _fonts.Values)
        {
            font.Dispose();
        }

        foreach (var font in _runFonts.Values)
        {
            font.Dispose();
        }

        foreach (var font in _emojiFonts.Values)
        {
            font.Dispose();
        }

        _emojiFace?.Dispose();
        _emojiBlob?.Dispose();
        _paint.Dispose();

        foreach (var typeface in _ownedTypefaces)
        {
            typeface.Dispose();
        }

        _ownedTypefaces.Clear();
    }

    private readonly record struct TextKey(string Text, FontRole Role, float Size, int MaxWidthHalf);

    private sealed record CacheEntry(TextLayout Layout, LinkedListNode<TextKey> Node);

    private readonly record struct Run(string Text, SKFont Font, bool Shaped);
}
