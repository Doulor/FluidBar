using SkiaSharp;

namespace FluidBar.SkiaIslandSpike;

/// <summary>
/// Builds retained scenes for the five spike surfaces. All geometry is DIP; the canvas is scaled by
/// the renderer, so the same numbers work at 100/125/150/175/200 % DPI.
/// </summary>
internal sealed class IslandScenes : IDisposable
{
    private readonly TextEngine _text;
    private readonly ShadowCache _shadows;
    private readonly StaticLayerCache _static;
    private readonly SKPaint _paint = new() { IsAntialias = true };
    private readonly SKShader _artSmall;
    private readonly SKShader _artLarge;

    public IslandScenes(TextEngine text, ShadowCache shadows, StaticLayerCache staticLayers)
    {
        _text = text;
        _shadows = shadows;
        _static = staticLayers;
        _artSmall = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint((float)Tokens.MediaArtSize, (float)Tokens.MediaArtSize),
            [Tokens.TrackAccent, Tokens.ArtEnd], [0f, 1f], SKShaderTileMode.Clamp);
        _artLarge = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(64, 64),
            [Tokens.TrackAccent, Tokens.ArtEndLarge], [0f, 1f], SKShaderTileMode.Clamp);
    }

    private static readonly TextStyle TitleStyle = new(FontRole.UiSemiBold, Tokens.TitleSize);
    private static readonly TextStyle CaptionStyle = new(FontRole.Ui, Tokens.CaptionSize);
    private static readonly TextStyle CaptionSemiBold = new(FontRole.UiSemiBold, Tokens.CaptionSize);
    private static readonly TextStyle PageTitleStyle = new(FontRole.DisplaySemiBold, Tokens.PageTitleSize);
    private static readonly TextStyle ClockStyle = new(FontRole.DisplayBold, Tokens.DashboardClockSize);
    private static readonly TextStyle DateStyle = new(FontRole.DisplaySemiBold, Tokens.DashboardDateSize);
    private static readonly TextStyle MonoSmall = new(FontRole.Mono, 11f);
    private static readonly TextStyle MonoValue = new(FontRole.MonoSemiBold, Tokens.HudValueSize);
    private static readonly TextStyle MiniLabel = new(FontRole.UiSemiBold, 10.2f);
    private static readonly TextStyle MiniClock = new(FontRole.MonoSemiBold, 9.5f);
    private static readonly TextStyle BodyStyle = new(FontRole.Ui, 13f);
    private static readonly TextStyle SmallMono = new(FontRole.Mono, 10f);

    private static readonly IconKind[] QuickIcons = [IconKind.Settings, IconKind.Store, IconKind.Close];
    private readonly float[] _stripWidths = new float[6];
    private static readonly double[] WaveHeights = [0.45, 0.9, 0.62, 0.78];
    private static readonly double[] StripWaveHeights = [0.45, 0.6, 0.4];

    // ---------------------------------------------------------------- compact

    public IslandScene CompactIdle(Copy copy, float scale, bool useStatic, bool standalone = false)
    {
        const double w = Tokens.CompactIdleWidth;
        const double h = Tokens.CompactIdleHeight;
        var windowWidth = standalone ? w + 80 : Tokens.WindowWidth;
        var windowHeight = standalone ? h + 80 : Tokens.WindowHeight;
        var x = (windowWidth - w) / 2;
        var y = standalone ? 40 : Tokens.WindowPadding;
        var scene = NewScene("compact-idle", scale, useStatic, x, y, w, h, Tokens.CompactIdleHeight / 2, windowWidth, windowHeight);

        scene.Add("clock", "compact.content", 20,
            new SKRect((float)x + 12, (float)y, (float)x + 70, (float)y + (float)h),
            canvas => _text.Draw(canvas, copy.IdleClock, new TextStyle(FontRole.MonoSemiBold, Tokens.CompactClockSize),
                new SKRect((float)x + 12, (float)y, (float)x + 70, (float)y + (float)h), Tokens.Foreground));

        scene.Add("battery", "compact.content", 20,
            new SKRect((float)x + 74, (float)y, (float)x + 120, (float)y + (float)h),
            canvas =>
            {
                _text.Draw(canvas, copy.Battery, MonoSmall,
                    new SKRect((float)x + 74, (float)y, (float)x + 100, (float)y + (float)h),
                    Tokens.ForegroundSecondary, TextAlign.Right);
                Icons.Draw(canvas, IconKind.Battery,
                    new SKRect((float)x + 104, (float)y + 10, (float)x + 120, (float)y + 26),
                    Tokens.ForegroundSecondary, _paint);
            });

        return scene;
    }

    public IslandScene CompactMedia(Copy copy, float scale, bool useStatic, double wavePhase = 0.35, bool standalone = false)
    {
        const double w = Tokens.CompactMediaWidth;
        const double h = Tokens.CompactMediaHeight;
        var windowWidth = standalone ? w + 80 : Tokens.WindowWidth;
        var windowHeight = standalone ? h + 80 : Tokens.WindowHeight;
        var x = (windowWidth - w) / 2;
        var y = standalone ? 40 : Tokens.WindowPadding;
        var scene = NewScene("compact-media", scale, useStatic, x, y, w, h, h / 2, windowWidth, windowHeight);

        var art = new SKRect((float)x + 10, (float)y + 6, (float)x + 38, (float)y + 34);
        scene.Add("artwork", "compact.content", 20, art, canvas => DrawArt(canvas, art, 28, Tokens.ArtSmallRadius, _artSmall, 15.68f));

        scene.Add("meta", "compact.content", 20,
            new SKRect((float)x + 46, (float)y + 4, (float)x + 168, (float)y + 38),
            canvas =>
            {
                _text.Draw(canvas, copy.MediaTitle, TitleStyle,
                    new SKRect((float)x + 46, (float)y + 6, (float)x + 166, (float)y + 22), Tokens.Foreground);
                _text.Draw(canvas, copy.MediaAlbum, CaptionStyle,
                    new SKRect((float)x + 46, (float)y + 22, (float)x + 166, (float)y + 36), Tokens.ForegroundSecondary);
            });

        var wave = new SKRect((float)x + 168, (float)y + 13, (float)x + 186, (float)y + 27);
        scene.Add("waveform", "compact.content", 20, wave,
            canvas => DrawWave(canvas, wave, Tokens.TrackAccent, wavePhase));

        var play = new SKRect((float)x + 192, (float)y + 8, (float)x + 216, (float)y + 32);
        scene.Add("transport", "compact.content", 20, play,
            canvas => Icons.Draw(canvas, IconKind.Pause, play.Deflate(4), Tokens.Foreground, _paint));

        return scene;
    }

    // ---------------------------------------------------------------- expanded

    public IslandScene ExpandedHome(Copy copy, float scale, bool useStatic)
    {
        var scene = NewExpandedScene("expanded-home", copy, 0, scale, useStatic);
        var px = (float)(Tokens.WindowPadding + Tokens.ContentInsetX);
        var py = (float)(Tokens.WindowPadding + Tokens.ContentInsetY);

        scene.Add("clock", "page.home.clock", 20,
            new SKRect(px + 16, py + 20, px + 188, py + 68),
            canvas => _text.Draw(canvas, copy.IdleClock, ClockStyle,
                new SKRect(px + 16, py + 22, px + 188, py + 68), Tokens.Foreground));

        scene.Add("date", "page.home.date", 20,
            new SKRect(px + 16, py + 66, px + 192, py + 88),
            canvas =>
            {
                _text.Draw(canvas, copy.HomeDate, DateStyle,
                    new SKRect(px + 16, py + 68, px + 192, py + 88), Tokens.ForegroundSecondary);
                _text.Draw(canvas, copy.Greeting, CaptionStyle,
                    new SKRect(px + 16, py + 88, px + 192, py + 106), Tokens.ForegroundTertiary);
            });

        var cards = new (IconKind Icon, string Value, string Caption, SKColor Accent)[]
        {
            (IconKind.Battery, copy.Battery, copy.BatteryLabel, Tokens.Accent),
            (IconKind.Wifi, copy.Network, copy.SystemLabel, Tokens.Success),
            (IconKind.Focus, copy.FocusOff, copy.FocusLabel, Tokens.Warning),
        };
        var quickLabels = new[] { copy.QuickSettings, copy.QuickMarket, copy.QuickHide };

        scene.Add("cards", "page.home.cards", 20,
            new SKRect(px + 200, py + 26, px + 384, py + 138),
            canvas => DrawHomeCards(canvas, cards, px, py));

        scene.Add("quick", "page.home.quick", 20,
            new SKRect(px + 16, py + 140, px + 384, py + 222),
            canvas => DrawQuickActions(canvas, quickLabels, px, py));

        return scene;
    }

    public IslandScene ExpandedMedia(Copy copy, float scale, bool useStatic, double progress = 0.461, string? lyric = null)
    {
        var scene = NewExpandedScene("expanded-media", copy, 1, scale, useStatic);
        var px = (float)(Tokens.WindowPadding + Tokens.ContentInsetX);
        var py = (float)(Tokens.WindowPadding + Tokens.ContentInsetY);

        var art = new SKRect(px + 16, py + 24, px + 80, py + 88);
        scene.Add("artwork", "page.media.art", 20, art,
            canvas => DrawArt(canvas, art, 64, Tokens.ArtLargeRadius, _artLarge, 28.16f));

        scene.Add("meta", "page.media.meta", 20,
            new SKRect(px + 96, py + 20, px + 384, py + 88),
            canvas =>
            {
                _text.Draw(canvas, copy.MediaTitle, PageTitleStyle,
                    new SKRect(px + 96, py + 22, px + 376, py + 42), Tokens.Foreground);
                _text.Draw(canvas, copy.MediaArtist, BodyStyle,
                    new SKRect(px + 96, py + 44, px + 376, py + 62), Tokens.ForegroundSecondary);
                _text.Draw(canvas, copy.MediaAlbum, CaptionStyle,
                    new SKRect(px + 96, py + 64, px + 376, py + 80), Tokens.ForegroundTertiary);
            });

        scene.Add("progress", "page.media.progress", 20,
            new SKRect(px + 16, py + 96, px + 384, py + 118),
            canvas =>
            {
                var track = new SKRect(px + 16, py + 96, px + 384, py + 100);
                DrawTrack(canvas, track, progress, Tokens.TrackAccent);
                _text.Draw(canvas, copy.Position, SmallMono,
                    new SKRect(px + 16, py + 102, px + 96, py + 118), Tokens.ForegroundSecondary);
                _text.Draw(canvas, copy.Duration, SmallMono,
                    new SKRect(px + 304, py + 102, px + 384, py + 118), Tokens.ForegroundSecondary, TextAlign.Right);
            });

        scene.Add("transport", "page.media.transport", 20,
            new SKRect(px + 136, py + 108, px + 256, py + 148),
            canvas => DrawTransport(canvas, px, py));

        scene.Add("lyrics", "page.media.lyrics", 20,
            new SKRect(px + 16, py + 164, px + 384, py + 202),
            canvas =>
            {
                _text.Draw(canvas, lyric ?? copy.Lyric, BodyStyle,
                    new SKRect(px + 16, py + 164, px + 384, py + 184), Tokens.ForegroundSecondary, TextAlign.Center);
                _text.Draw(canvas, copy.NextLyric, CaptionStyle,
                    new SKRect(px + 16, py + 184, px + 384, py + 202), Tokens.ForegroundTertiary, TextAlign.Center);
            });

        return scene;
    }

    /// <summary>Diagnostic page: EN + zh-Hans + kana/Hangul + emoji + accents through the fallback stack.</summary>
    public IslandScene GlyphCoverage(Copy copy, float scale, bool useStatic)
    {
        var scene = NewExpandedScene("glyph-coverage", copy, 0, scale, useStatic);
        var px = (float)(Tokens.WindowPadding + Tokens.ContentInsetX);
        var py = (float)(Tokens.WindowPadding + Tokens.ContentInsetY);
        scene.Add("coverage", "page.coverage", 20,
            new SKRect(px + 16, py + 20, px + 384, py + 246),
            canvas =>
            {
                _text.Draw(canvas, "FluidBar 灵动岛 v2", new TextStyle(FontRole.DisplaySemiBold, 20f),
                    new SKRect(px + 16, py + 22, px + 384, py + 52), Tokens.Foreground);
                _text.Draw(canvas, "正在播放：午夜城市", new TextStyle(FontRole.UiSemiBold, 15f),
                    new SKRect(px + 16, py + 56, px + 384, py + 80), Tokens.Foreground);
                _text.Draw(canvas, "🎵  🎧  🌙  🚀", new TextStyle(FontRole.Ui, 22f),
                    new SKRect(px + 16, py + 86, px + 384, py + 120), Tokens.Foreground);
                _text.Draw(canvas, "こんにちは · 안녕하세요", new TextStyle(FontRole.Ui, 15f),
                    new SKRect(px + 16, py + 124, px + 384, py + 146), Tokens.ForegroundSecondary);
                _text.Draw(canvas, "Café — naïve — ½ — 100 %", new TextStyle(FontRole.Ui, 13f),
                    new SKRect(px + 16, py + 152, px + 384, py + 172), Tokens.Foreground);
                _text.Draw(canvas, "Segoe UI Variable Text · Microsoft YaHei UI · Segoe UI Emoji",
                    new TextStyle(FontRole.Ui, 11f),
                    new SKRect(px + 16, py + 180, px + 384, py + 198), Tokens.ForegroundSecondary);
                _text.Draw(canvas, "HarfBuzz clusters: e + ◌́ → é · emoji ZWJ → 👩‍💻",
                    new TextStyle(FontRole.Ui, 11f),
                    new SKRect(px + 16, py + 204, px + 384, py + 222), Tokens.ForegroundTertiary);
            });
        return scene;
    }

    public IslandScene ExpandedWithStrip(Copy copy, float scale, bool useStatic, double activePosition = 1.0, double previewPhase = 0.6)
    {
        var scene = ExpandedHome(copy, scale, useStatic);
        scene = Rename(scene, "expanded-archipelago", Tokens.WindowHeightWithStrip);

        var stripX = (float)(Tokens.WindowPadding + ((Tokens.ExpandedWidth - StripWidth) / 2));
        var stripY = (float)(Tokens.WindowPadding + Tokens.ExpandedHeight + Tokens.ArchipelagoGap);
        var bounds = new SKRect(stripX - 8, stripY - 8, stripX + (float)StripWidth + 8, stripY + (float)Tokens.StripHeight + 8);
        scene.Add("archipelago", "archipelago", 40, bounds,
            canvas => DrawStrip(canvas, copy, stripX, stripY, activePosition, previewPhase));

        return scene;
    }

    public static double StripWidth => (6 * Tokens.MiniWidth) + (5 * Tokens.MiniGap) + (Tokens.ActiveMiniWidth - Tokens.MiniWidth);

    // ---------------------------------------------------------------- drawing

    private void DrawHomeCards(
        SKCanvas canvas,
        (IconKind Icon, string Value, string Caption, SKColor Accent)[] cards,
        float px,
        float py)
    {
        for (var i = 0; i < cards.Length; i++)
        {
            var rect = new SKRect(px + 200, py + 26 + (i * 40), px + 384, py + 58 + (i * 40));
            DrawCard(canvas, rect);
            Icons.Draw(canvas, cards[i].Icon,
                new SKRect(rect.Left + 8, rect.Top + 8, rect.Left + 24, rect.Top + 24), cards[i].Accent, _paint);
            _text.Draw(canvas, cards[i].Value, CaptionSemiBold,
                new SKRect(rect.Left + 32, rect.Top, rect.Left + 136, rect.Bottom), Tokens.Foreground);
            _text.Draw(canvas, cards[i].Caption, new TextStyle(FontRole.Ui, 10f),
                new SKRect(rect.Right - 56, rect.Top, rect.Right - 8, rect.Bottom),
                Tokens.ForegroundSecondary, TextAlign.Right);
        }
    }

    private void DrawQuickActions(SKCanvas canvas, string[] labels, float px, float py)
    {
        var width = (float)((Tokens.UsableWidth - 16) / 3);
        for (var i = 0; i < 3; i++)
        {
            var left = px + 16 + (i * (width + 8));
            var rect = new SKRect(left, py + 140, left + width, py + 222);
            DrawCard(canvas, rect);
            Icons.Draw(canvas, QuickIcons[i],
                new SKRect(rect.MidX - 10, rect.Top + 12, rect.MidX + 10, rect.Top + 32), Tokens.Foreground, _paint);
            _text.Draw(canvas, labels[i], CaptionSemiBold,
                new SKRect(rect.Left + 4, rect.Top + 42, rect.Right - 4, rect.Top + 64),
                Tokens.ForegroundSecondary, TextAlign.Center);
        }
    }

    private void DrawTransport(SKCanvas canvas, float px, float py)
    {
        var previous = new SKRect(px + 136, py + 112, px + 172, py + 148);
        var play = new SKRect(px + 176, py + 108, px + 216, py + 148);
        var next = new SKRect(px + 220, py + 112, px + 256, py + 148);

        Icons.Draw(canvas, IconKind.Previous, previous.Deflate(10.18f), Tokens.Foreground, _paint);
        _paint.Style = SKPaintStyle.Fill;
        _paint.Color = Tokens.LineStrong;
        canvas.DrawCircle(play.MidX, play.MidY, 20, _paint);
        Icons.Draw(canvas, IconKind.Pause, play.Deflate(10.8f), Tokens.Foreground, _paint);
        Icons.Draw(canvas, IconKind.Next, next.Deflate(10.18f), Tokens.Foreground, _paint);
    }

    private void DrawStrip(SKCanvas canvas, Copy copy, float x, float y, double activePosition, double phase)
    {
        var active = Math.Clamp((int)Math.Round(activePosition, MidpointRounding.AwayFromZero), 0, 5);

        // Flow layout: the active island's 24 DIP growth is taken out of the two 8 DIP gaps and the
        // neighbour slots shift right, so TotalWidth stays 352 and nothing overlaps (the App code
        // left-anchors the growth and draws neighbours over it; see RESULTS.md).
        var widths = _stripWidths;
        for (var i = 0; i < 6; i++)
        {
            var influence = Math.Clamp(1 - Math.Abs(i - activePosition), 0, 1);
            widths[i] = (float)(Tokens.MiniWidth + ((Tokens.ActiveMiniWidth - Tokens.MiniWidth) * influence));
        }

        var cursor = x;
        for (var i = 0; i < 6; i++)
        {
            var width = widths[i];
            var height = i == active ? Tokens.ActiveMiniHeight : Tokens.MiniHeight;
            var rect = new SKRect(
                cursor,
                y + (float)((Tokens.StripHeight - height) / 2),
                cursor + width,
                y + (float)((Tokens.StripHeight + height) / 2));
            if (i != active)
            {
                DrawMiniIsland(canvas, copy, rect, false, i, phase);
            }

            cursor += width + (float)Tokens.MiniGap;
        }

        // Active island is drawn last (topmost) so its label is never covered.
        cursor = x;
        for (var i = 0; i < 6; i++)
        {
            if (i == active)
            {
                var rect = new SKRect(
                    cursor,
                    y + (float)((Tokens.StripHeight - Tokens.ActiveMiniHeight) / 2),
                    cursor + widths[i],
                    y + (float)((Tokens.StripHeight + Tokens.ActiveMiniHeight) / 2));
                DrawMiniIsland(canvas, copy, rect, true, i, phase);
            }

            cursor += widths[i] + (float)Tokens.MiniGap;
        }
    }

    private void DrawMiniIsland(SKCanvas canvas, Copy copy, SKRect rect, bool active, int index, double phase)
    {
        var radius = (float)Tokens.MiniRadius;
        var shadowAlpha = active ? 0.32 : 0.22;
        for (var layer = 2; layer >= 0; layer--)
        {
            var inflate = layer * 2f;
            var shadowRect = new SKRect(
                rect.Left - inflate,
                rect.Top - inflate + 3,
                rect.Right + inflate,
                rect.Bottom + inflate + 3);
            _paint.Style = SKPaintStyle.Fill;
            _paint.Color = new SKColor(0, 0, 0, (byte)Math.Clamp((shadowAlpha / (layer + 1.5)) * 255, 0, 255));
            canvas.DrawRoundRect(shadowRect, radius + inflate, radius + inflate, _paint);
        }

        _paint.Style = SKPaintStyle.Fill;
        _paint.Color = Tokens.Background.WithAlpha(active ? (byte)247 : (byte)240);
        canvas.DrawRoundRect(rect, active ? radius + 1 : radius, active ? radius + 1 : radius, _paint);

        _paint.Style = SKPaintStyle.Stroke;
        _paint.StrokeWidth = active ? 1.2f : 1f;
        _paint.Color = active ? Tokens.Accent.WithAlpha(166) : Tokens.LineStrong.WithAlpha(230);
        canvas.DrawRoundRect(rect, active ? radius + 1 : radius, active ? radius + 1 : radius, _paint);

        if (active)
        {
            var title = copy.PageTitles[index];
            _text.Draw(canvas, title, MiniLabel,
                new SKRect(rect.Left + 6, rect.Top, rect.Right - 6, rect.Bottom),
                Tokens.Accent, TextAlign.Center);
            return;
        }

        var area = new SKRect(rect.Left + 5, rect.Top + 3, rect.Right - 5, rect.Bottom - 3);
        switch (index)
        {
            case 0:
                _text.Draw(canvas, copy.IdleClock, MiniClock, area, Tokens.Foreground, TextAlign.Center);
                break;
            case 1:
            {
                var art = new SKRect(area.Left, area.Top + 2, area.Left + 16, area.Top + 18);
                _paint.Style = SKPaintStyle.Fill;
                _paint.Color = Tokens.TrackAccent;
                canvas.DrawRoundRect(art, 4, 4, _paint);
                var height0 = StripWaveHeights[0] + (0.25 * Math.Abs(Math.Sin(phase * 2.1)));
                var height1 = StripWaveHeights[1] + (0.3 * Math.Abs(Math.Sin((phase * 2.6) + 1)));
                var height2 = StripWaveHeights[2] + (0.2 * Math.Abs(Math.Sin((phase * 1.7) + 2)));
                for (var bar = 0; bar < 3; bar++)
                {
                    var barHeight = bar switch { 0 => height0, 1 => height1, _ => height2 };
                    var height = (area.Height - 6) * (float)barHeight;
                    var barRect = new SKRect(
                        art.Right + 4 + (bar * 4),
                        area.MidY - (height / 2),
                        art.Right + 6 + (bar * 4),
                        area.MidY + (height / 2));
                    canvas.DrawRoundRect(barRect, 1, 1, _paint);
                }

                break;
            }

            case 2:
                _paint.Color = Tokens.Danger;
                canvas.DrawCircle(area.Left + 4, area.Top + 4, 2.5f, _paint);
                _paint.Color = Tokens.ForegroundSecondary;
                canvas.DrawRoundRect(new SKRect(area.Left, area.Top + 9, area.Right, area.Top + 11), 1, 1, _paint);
                canvas.DrawRoundRect(new SKRect(area.Left, area.Top + 14, area.Left + (area.Width * 0.7f), area.Top + 16), 1, 1, _paint);
                break;
            case 3:
                _paint.Color = Tokens.ForegroundSecondary;
                canvas.DrawRoundRect(new SKRect(area.Left, area.Top + 2, area.Right, area.Top + 4), 1, 1, _paint);
                canvas.DrawRoundRect(new SKRect(area.Left, area.Top + 8, area.Left + (area.Width * 0.8f), area.Top + 10), 1, 1, _paint);
                canvas.DrawRoundRect(new SKRect(area.Left, area.Top + 14, area.Left + (area.Width * 0.6f), area.Top + 16), 1, 1, _paint);
                break;
            case 4:
            {
                var pulse = 0.55 + (0.45 * Math.Sin(phase * 3.2));
                _paint.Color = Tokens.Info.WithAlpha((byte)Math.Clamp((0.25 + (0.2 * pulse)) * 255, 0, 255));
                canvas.DrawCircle(area.MidX, area.MidY, 6, _paint);
                _paint.Color = Tokens.Info.WithAlpha((byte)Math.Clamp((0.6 + (0.4 * pulse)) * 255, 0, 255));
                canvas.DrawCircle(area.MidX, area.MidY, 2.6f, _paint);
                break;
            }

            case 5:
            {
                const float cell = 5;
                for (var row = 0; row < 3; row++)
                {
                    for (var column = 0; column < 4; column++)
                    {
                        var selected = row == 1 && column == 2;
                        _paint.Color = selected ? Tokens.Accent.WithAlpha(230) : Tokens.ForegroundTertiary.WithAlpha(102);
                        var cellRect = new SKRect(
                            area.Left + 2 + (column * (cell + 2)),
                            area.Top + 4 + (row * (cell + 1)),
                            area.Left + 2 + (column * (cell + 2)) + cell,
                            area.Top + 4 + (row * (cell + 1)) + cell);
                        canvas.DrawRoundRect(cellRect, 1.2f, 1.2f, _paint);
                    }
                }

                break;
            }
        }
    }

    private void DrawArt(SKCanvas canvas, SKRect rect, float size, double radius, SKShader shader, float iconSize)
    {
        canvas.Save();
        canvas.Translate(rect.Left, rect.Top);
        _paint.Style = SKPaintStyle.Fill;
        _paint.Shader = shader;
        _paint.Color = SKColors.White;
        canvas.DrawRoundRect(new SKRect(0, 0, size, size), (float)radius, (float)radius, _paint);
        _paint.Shader = null;
        Icons.Draw(canvas, IconKind.Media,
            new SKRect((size - iconSize) / 2, (size - iconSize) / 2, (size + iconSize) / 2, (size + iconSize) / 2),
            SKColors.White.WithAlpha(217), _paint);
        canvas.Restore();
    }

    private void DrawCard(SKCanvas canvas, SKRect rect)
    {
        _paint.Style = SKPaintStyle.Fill;
        _paint.Color = Tokens.BackgroundRaised;
        canvas.DrawRoundRect(rect, (float)Tokens.CardRadius, (float)Tokens.CardRadius, _paint);
    }

    private void DrawTrack(SKCanvas canvas, SKRect track, double fraction, SKColor accent)
    {
        _paint.Style = SKPaintStyle.Fill;
        _paint.Color = Tokens.BackgroundInset;
        canvas.DrawRoundRect(track, 2, 2, _paint);
        var clamped = Math.Clamp(fraction, 0, 1);
        if (clamped > 0)
        {
            var width = Math.Max(4, (float)(track.Width * clamped));
            _paint.Color = accent;
            canvas.DrawRoundRect(new SKRect(track.Left, track.Top, track.Left + width, track.Bottom), 2, 2, _paint);
        }
    }

    private void DrawWave(SKCanvas canvas, SKRect area, SKColor accent, double phase)
    {
        const float gap = 2f;
        var barWidth = Math.Max(1.5f, (area.Width - (gap * 3)) / 4);
        _paint.Style = SKPaintStyle.Fill;
        _paint.Color = accent;
        for (var i = 0; i < WaveHeights.Length; i++)
        {
            var wobble = 0.85 + (0.15 * Math.Sin(phase * 3 + i));
            var height = area.Height * (float)WaveHeights[i] * (float)wobble;
            var x = area.Left + (i * (barWidth + gap));
            var y = area.MidY - (height / 2);
            canvas.DrawRoundRect(new SKRect(x, y, x + barWidth, y + height), barWidth / 2, barWidth / 2, _paint);
        }
    }

    // ---------------------------------------------------------------- scene scaffolding

    private IslandScene NewScene(
        string name, float scale, bool useStatic, double x, double y, double w, double h, double radius,
        double windowWidth = Tokens.WindowWidth, double windowHeight = Tokens.WindowHeight)
    {
        var scene = new IslandScene(name, windowWidth, windowHeight);
        var live = (SKCanvas canvas) => _static.DrawBackground(
            canvas, x, y, w, h, radius, true, false, Tokens.Background, Tokens.LineStrong, _shadows, scale);
        var image = _static.Get(
            windowWidth, windowHeight, x, y, w, h, radius, scale,
            fill: true, acrylic: false, Tokens.Background, Tokens.LineStrong, _shadows);
        scene.Background = new BackgroundLayer
        {
            Image = image,
            Dest = new SKRect(0, 0, (float)windowWidth, (float)windowHeight),
            DrawLive = live,
            UseStatic = useStatic,
        };
        return scene;
    }

    private IslandScene NewExpandedScene(string name, Copy copy, int page, float scale, bool useStatic)
    {
        var scene = NewScene(name, scale, useStatic,
            Tokens.WindowPadding, Tokens.WindowPadding, Tokens.ExpandedWidth, Tokens.ExpandedHeight, Tokens.ExpandedRadius);

        var px = (float)(Tokens.WindowPadding + Tokens.ContentInsetX);
        var py = (float)(Tokens.WindowPadding + Tokens.ContentInsetY);
        scene.Add("page-title", "overlay", 30,
            new SKRect(px, py - 2, px + 340, py + 18),
            canvas => _text.Draw(canvas, copy.PageTitles[page], PageTitleStyle,
                new SKRect(px, py - 2, px + 340, py + 18), Tokens.Foreground));

        scene.Add("collapse", "overlay", 30,
            new SKRect(px + (float)Tokens.PageWidth - 20, py - 2, px + (float)Tokens.PageWidth, py + 18),
            canvas => Icons.Draw(canvas, IconKind.ChevronDown,
                new SKRect(px + (float)Tokens.PageWidth - 16, py + 2, px + (float)Tokens.PageWidth - 4, py + 14),
                Tokens.ForegroundSecondary, _paint));

        return scene;
    }

    private static IslandScene Rename(IslandScene source, string name, double height)
    {
        var scene = new IslandScene(name, source.Width, height)
        {
            Background = source.Background,
        };
        foreach (var node in source.Nodes)
        {
            scene.Add(node.Id, node.Region, node.Z, node.Bounds, node.Draw);
        }

        return scene;
    }

    public void Dispose()
    {
        _artSmall.Dispose();
        _artLarge.Dispose();
        _paint.Dispose();
    }
}




