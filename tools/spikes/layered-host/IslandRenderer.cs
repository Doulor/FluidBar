using FluidBar.Core.Layout;
using FluidBar.Platform.Windows.Hosting;
using SkiaSharp;

namespace FluidBar.Spikes.LayeredHost;

/// <summary>
/// Draws the spike island straight into the host's persistent DIB: a 440x284 squircle panel with a
/// soft shadow, title text, a media row and six detached mini-islands in the lane below the panel.
/// The SkiaSharp surface wraps <see cref="LayeredFrame.Bits"/>, so there is no copy and no readback.
/// </summary>
internal sealed class IslandRenderer : IDisposable
{
    // Panel geometry, client DIP.
    private const float PanelX = 24;
    private const float PanelY = 24;
    private const float PanelWidth = 440;
    private const float PanelHeight = 284;

    // Detached mini-island lane (float: below the panel; dock: above it).
    private const float PillWidth = 140;
    private const float PillHeight = 26;
    private const float PillGapX = 10;
    private const float PillGapY = 8;
    private const float DockPillWidth = 68;
    private const float DockPillGap = 6;
    private const float DockLaneY = 24;
    private const float DockPanelY = 76;
    private const float DockStripY = 368;
    private const float DockStripHeight = 52;

    private static readonly string[] PillLabels =
    [
        "VOL 42%", "BRI 80%", "CLIP 3", "MEDIA", "TOAST", "CLOCK",
    ];

    private static readonly SKColor[] PillAccents =
    [
        new(96, 165, 250), new(251, 191, 36), new(52, 211, 153),
        new(244, 114, 182), new(167, 139, 250), new(148, 163, 184),
    ];

    private readonly SKTypeface _regular;
    private readonly SKTypeface _bold;
    private readonly SKFont _titleFont;
    private readonly SKFont _subtitleFont;
    private readonly SKFont _cardTitleFont;
    private readonly SKFont _cardArtistFont;
    private readonly SKFont _chipLabelFont;
    private readonly SKFont _chipValueFont;
    private readonly SKFont _pillFont;
    private readonly SKFont _badgeFont;
    private readonly SKFont _footerFont;
    private readonly SKPaint _fill = new() { IsAntialias = true };
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
    private readonly SKPaint _textPaint = new() { IsAntialias = true };
    private readonly SKTextBlob _titleBlob;
    private readonly SKTextBlob _subtitleBlob;
    private readonly SKTextBlob _badgeBlob;
    private readonly SKTextBlob _cardTitleBlob;
    private readonly SKTextBlob _cardArtistBlob;
    private readonly SKTextBlob _footerBlob;
    private readonly SKTextBlob _logoBlob;
    private readonly SKTextBlob _clockBlob;
    private readonly SKTextBlob[] _pillBlobs;
    private readonly SKTextBlob[] _chipLabelBlobs;
    private readonly SKTextBlob[] _chipValueBlobs;
    private readonly SKShader _panelShader;
    private readonly SKShader _artworkShader;
    private readonly SKShader _logoShader;
    private readonly SKPath _floatPanelPath;
    private readonly SKPath _dockPanelPath;
    private readonly SKShader _stripShader;
    private readonly SKImageFilter _shadowFilter;
    private readonly VisualRect[] _floatPills;
    private readonly VisualRect[] _dockPills;
    private readonly VisualRect[] _floatInteractive;
    private readonly VisualRect[] _dockInteractive;
    private SKSurface? _surface;
    private int _widthPx;
    private int _heightPx;
    private int _disposed;

    /// <summary>Creates the renderer and pre-builds every Skia object (no per-frame allocation).</summary>
    public IslandRenderer()
    {
        _regular = SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default;
        _bold = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? _regular;

        _titleFont = new SKFont(_bold, 18);
        _subtitleFont = new SKFont(_regular, 11);
        _cardTitleFont = new SKFont(_bold, 14);
        _cardArtistFont = new SKFont(_regular, 11);
        _chipLabelFont = new SKFont(_regular, 9);
        _chipValueFont = new SKFont(_bold, 15);
        _pillFont = new SKFont(_regular, 9.5f);
        _badgeFont = new SKFont(_bold, 9);
        _footerFont = new SKFont(_regular, 9);

        _titleBlob = SKTextBlob.Create("FluidBar", _titleFont)!;
        _subtitleBlob = SKTextBlob.Create("v2 layered host \u00b7 150% DPI", _subtitleFont)!;
        _badgeBlob = SKTextBlob.Create("LIVE", _badgeFont)!;
        _cardTitleBlob = SKTextBlob.Create("Midnight City", _cardTitleFont)!;
        _cardArtistBlob = SKTextBlob.Create("M83 \u00b7 Hurry Up, We're Dreaming", _cardArtistFont)!;
        _footerBlob = SKTextBlob.Create("6 detached islands \u00b7 click-through between pills", _footerFont)!;
        _logoBlob = SKTextBlob.Create("F", _titleFont)!;
        _clockBlob = SKTextBlob.Create("20:41", _cardTitleFont)!;
        _pillBlobs = PillLabels.Select(label => SKTextBlob.Create(label, _pillFont)!).ToArray();
        _chipLabelBlobs =
        [
            SKTextBlob.Create("FRAME", _chipLabelFont)!,
            SKTextBlob.Create("UPLOAD", _chipLabelFont)!,
            SKTextBlob.Create("ALPHA", _chipLabelFont)!,
        ];
        _chipValueBlobs =
        [
            SKTextBlob.Create("6.1 ms", _chipValueFont)!,
            SKTextBlob.Create("0.4 ms", _chipValueFont)!,
            SKTextBlob.Create("per-px", _chipValueFont)!,
        ];

        _panelShader = SKShader.CreateLinearGradient(
            new SKPoint(0, PanelY),
            new SKPoint(0, PanelY + PanelHeight),
            [new SKColor(18, 21, 30, 246), new SKColor(10, 12, 18, 246)],
            [0f, 1f],
            SKShaderTileMode.Clamp);
        _artworkShader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(68, 68),
            [new SKColor(129, 140, 248), new SKColor(236, 72, 153)],
            [0f, 1f],
            SKShaderTileMode.Clamp);
        _logoShader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(28, 28),
            [new SKColor(56, 189, 248), new SKColor(129, 140, 248)],
            [0f, 1f],
            SKShaderTileMode.Clamp);
        _stripShader = SKShader.CreateLinearGradient(
            new SKPoint(0, DockStripY),
            new SKPoint(0, DockStripY + DockStripHeight),
            [new SKColor(20, 24, 34, 248), new SKColor(12, 14, 20, 248)],
            [0f, 1f],
            SKShaderTileMode.Clamp);
        _shadowFilter = SKImageFilter.CreateDropShadowOnly(0, 8, 12, 12, new SKColor(0, 0, 0, 150), (SKImageFilter?)null);

        _floatPanelPath = BuildSquirclePath(PanelX, PanelY, PanelWidth, PanelHeight, 28);
        _dockPanelPath = BuildSquirclePath(PanelX, DockPanelY, PanelWidth, PanelHeight, 16);
        _floatPills = BuildPills(PanelX, PanelY + PanelHeight + 8, PillWidth, PillHeight, PillGapX, PillGapY, 3);
        _dockPills = BuildPills(25, DockLaneY, DockPillWidth, PillHeight, DockPillGap, 0, 6);
        _floatInteractive = [new VisualRect(PanelX, PanelY, PanelWidth, PanelHeight), .. _floatPills];
        _dockInteractive =
        [
            new VisualRect(PanelX, DockPanelY, PanelWidth, PanelHeight),
            new VisualRect(PanelX, DockStripY, PanelWidth, DockStripHeight),
            .. _dockPills,
        ];
    }

    /// <summary>Panel (or dock strip) rectangle in client DIP: the host's squircle hit region.</summary>
    public static VisualRect ShellRect(LayeredHostMode mode) => mode == LayeredHostMode.Dock
        ? new VisualRect(PanelX, DockStripY, PanelWidth, DockStripHeight)
        : new VisualRect(PanelX, PanelY, PanelWidth, PanelHeight);

    /// <summary>Detached mini-island rectangles in client DIP.</summary>
    public VisualRect[] MiniIslands(LayeredHostMode mode)
        => mode == LayeredHostMode.Dock ? _dockPills : _floatPills;

    /// <summary>Every interactive rectangle (shell plus detached islands) in client DIP.</summary>
    public VisualRect[] InteractiveRects(LayeredHostMode mode)
        => mode == LayeredHostMode.Dock ? _dockInteractive : _floatInteractive;

    /// <summary>Whether the soft drop shadow is drawn (measurement switch: the shadow dominates full-frame cost).</summary>
    public bool ShadowEnabled { get; set; } = true;

    /// <summary>Corner radii of the shell for the mode (DIP).</summary>
    public static CornerRadii ShellRadii(LayeredHostMode mode) => mode == LayeredHostMode.Dock
        ? CornerRadii.Uniform(12)
        : CornerRadii.Uniform(28);

    /// <summary>Draws the whole frame from scratch.</summary>
    public void RenderFull(LayeredFrame frame, LayeredHostMode mode, double seconds)
    {
        EnsureSurface(frame);
        var canvas = _surface!.Canvas;
        var scale = (float)frame.Scale;
        canvas.Clear(SKColors.Transparent);
        canvas.Save();
        canvas.Scale(scale);

        var progress = ProgressAt(seconds);
        var pulse = PulseAt(seconds);

        if (mode == LayeredHostMode.Dock)
        {
            DrawPanel(canvas, _dockPanelPath, progress);
            DrawDockStrip(canvas, pulse);
            for (var i = 0; i < _dockPills.Length; i++)
            {
                DrawMiniPill(canvas, _dockPills[i], i, pulse);
            }
        }
        else
        {
            DrawPanel(canvas, _floatPanelPath, progress);
            for (var i = 0; i < _floatPills.Length; i++)
            {
                DrawMiniPill(canvas, _floatPills[i], i, pulse);
            }
        }

        canvas.Restore();
    }

    /// <summary>
    /// Draws only the animated elements and returns the dirty rectangles (surface pixels) they
    /// touched. Everything else on the surface keeps its previous content.
    /// </summary>
    public int RenderDynamic(LayeredFrame frame, LayeredHostMode mode, double seconds,
        Span<LayeredRect> dirtyRects)
    {
        EnsureSurface(frame);
        var canvas = _surface!.Canvas;
        var scale = (float)frame.Scale;
        canvas.Save();
        canvas.Scale(scale);

        var progress = ProgressAt(seconds);
        var pulse = PulseAt(seconds);
        var fill = ProgressFill(mode, progress);
        var track = ProgressTrack(mode);

        // The progress bar: repaint the track and the fill inside the track's dirty band.
        var barBand = ToDip(track).Inflate(2);
        canvas.Save();
        canvas.ClipRect(new SKRect((float)barBand.X, (float)barBand.Y, (float)barBand.Right, (float)barBand.Bottom));
        DrawProgress(canvas, track, fill);
        canvas.Restore();

        // The pulsing mini-island: repaint that pill only.
        var pill = MiniIslands(mode)[0];
        var pillBand = pill.Inflate(1);
        canvas.Save();
        canvas.ClipRect(new SKRect((float)pillBand.X, (float)pillBand.Y, (float)pillBand.Right, (float)pillBand.Bottom));
        DrawMiniPill(canvas, pill, 0, pulse);
        canvas.Restore();

        canvas.Restore();

        dirtyRects[0] = LayeredRect.FromDip(barBand, frame.Scale);
        dirtyRects[1] = LayeredRect.FromDip(pillBand, frame.Scale);
        return 2;
    }

    private void EnsureSurface(LayeredFrame frame)
    {
        if (_surface is not null && _widthPx == frame.WidthPx && _heightPx == frame.HeightPx)
        {
            return;
        }

        _surface?.Dispose();
        var info = new SKImageInfo(frame.WidthPx, frame.HeightPx, SKColorType.Bgra8888, SKAlphaType.Premul);
        _surface = SKSurface.Create(info, frame.Bits, frame.Stride)
            ?? throw new InvalidOperationException($"SKSurface.Create failed for {info.Width}x{info.Height}.");
        _widthPx = frame.WidthPx;
        _heightPx = frame.HeightPx;
    }

    private void DrawPanel(SKCanvas canvas, SKPath path, double progress)
    {
        var x = path.Bounds.Left;
        var y = path.Bounds.Top;

        if (ShadowEnabled)
        {
            // Soft shadow: the squircle filled with a drop-shadow-only image filter. This allocates an
            // offscreen layer and blurs it every frame; production should bake it once.
            _fill.Shader = null;
            _fill.Color = SKColors.Black;
            _fill.ImageFilter = _shadowFilter;
            canvas.DrawPath(path, _fill);
            _fill.ImageFilter = null;
        }

        // Panel body.
        _fill.Shader = _panelShader;
        canvas.DrawPath(path, _fill);
        _fill.Shader = null;

        // Hairline highlight: 1 DIP inside the shell, top half only.
        _stroke.Color = new SKColor(255, 255, 255, 28);
        _stroke.StrokeWidth = 1;
        canvas.DrawPath(path, _stroke);

        var cx = x + 20;
        var cy = y + 20;

        // Logo tile.
        _fill.Shader = _logoShader;
        canvas.DrawRoundRect(new SKRect(cx, cy + 2, cx + 28, cy + 30), 9, 9, _fill);
        _fill.Shader = null;
        _textPaint.Color = new SKColor(9, 12, 20);
        _textPaint.Style = SKPaintStyle.Fill;
        canvas.DrawText(_logoBlob, cx + 9, cy + 23, _textPaint);

        // Title + subtitle.
        _textPaint.Color = new SKColor(241, 245, 249);
        canvas.DrawText(_titleBlob, cx + 38, cy + 17, _textPaint);
        _textPaint.Color = new SKColor(148, 163, 184);
        canvas.DrawText(_subtitleBlob, cx + 38, cy + 32, _textPaint);

        // LIVE badge.
        var badge = new SKRect(x + PanelWidth - 72, cy + 4, x + PanelWidth - 20, cy + 26);
        _stroke.Color = new SKColor(56, 189, 248, 180);
        _stroke.StrokeWidth = 1;
        canvas.DrawRoundRect(badge, 11, 11, _stroke);
        _fill.Color = new SKColor(56, 189, 248, 30);
        canvas.DrawRoundRect(badge, 11, 11, _fill);
        _textPaint.Color = new SKColor(125, 211, 252);
        canvas.DrawText(_badgeBlob, badge.Left + 13, badge.Top + 15, _textPaint);

        // Media card.
        var card = new SKRect(x + 20, y + 96, x + PanelWidth - 20, y + 188);
        _fill.Color = new SKColor(255, 255, 255, 13);
        canvas.DrawRoundRect(card, 12, 12, _fill);
        _fill.Color = SKColors.White;
        _fill.Shader = _artworkShader;
        canvas.DrawRoundRect(new SKRect(card.Left + 12, card.Top + 12, card.Left + 80, card.Top + 80), 12, 12, _fill);
        _fill.Shader = null;

        _textPaint.Color = new SKColor(241, 245, 249);
        canvas.DrawText(_cardTitleBlob, card.Left + 92, card.Top + 34, _textPaint);
        _textPaint.Color = new SKColor(148, 163, 184);
        canvas.DrawText(_cardArtistBlob, card.Left + 92, card.Top + 52, _textPaint);

        // Waveform (static, antialiased) on the right of the card.
        _fill.Color = new SKColor(96, 165, 250, 190);
        for (var i = 0; i < 12; i++)
        {
            var height = 6 + ((i * 37) % 22);
            var left = card.Right - 12 - ((12 - i) * 5);
            canvas.DrawRoundRect(new SKRect(left, card.Bottom - 16 - height, left + 3, card.Bottom - 16), 1.5f, 1.5f, _fill);
        }

        DrawProgress(canvas, ProgressTrack(x, y), ProgressFillFor(x, y, progress));

        // Chips.
        for (var i = 0; i < 3; i++)
        {
            var chip = new SKRect(x + 20 + (i * 137), y + 200, x + 20 + (i * 137) + 126, y + 256);
            _fill.Color = new SKColor(255, 255, 255, 10);
            canvas.DrawRoundRect(chip, 12, 12, _fill);
            _textPaint.Color = new SKColor(148, 163, 184);
            canvas.DrawText(_chipLabelBlobs[i], chip.Left + 12, chip.Top + 20, _textPaint);
            _textPaint.Color = new SKColor(226, 232, 240);
            canvas.DrawText(_chipValueBlobs[i], chip.Left + 12, chip.Top + 42, _textPaint);
        }

        _textPaint.Color = new SKColor(100, 116, 139);
        canvas.DrawText(_footerBlob, x + 20, y + PanelHeight - 14, _textPaint);
    }

    private void DrawDockStrip(SKCanvas canvas, double pulse)
    {
        var strip = new SKRect(PanelX, DockStripY, PanelX + PanelWidth, DockStripY + DockStripHeight);
        _fill.Color = SKColors.White;
        _fill.Shader = _stripShader;
        canvas.DrawRoundRect(strip, 12, 12, _fill);
        _fill.Shader = null;
        _stroke.Color = new SKColor(255, 255, 255, 24);
        _stroke.StrokeWidth = 1;
        canvas.DrawRoundRect(strip, 12, 12, _stroke);

        for (var i = 0; i < 5; i++)
        {
            var cx = strip.Left + 24 + (i * 34);
            _fill.Color = PillAccents[i].WithAlpha((byte)(150 + (int)(60 * pulse)));
            canvas.DrawCircle(cx, strip.MidY, 7, _fill);
        }

        _textPaint.Color = new SKColor(226, 232, 240);
        canvas.DrawText(_clockBlob, strip.Right - 72, strip.MidY + 5, _textPaint);
    }

    private void DrawMiniPill(SKCanvas canvas, in VisualRect pill, int index, double pulse)
    {
        var rect = new SKRect((float)pill.X, (float)pill.Y, (float)pill.Right, (float)pill.Bottom);
        _fill.Color = new SKColor(16, 19, 27, 236);
        canvas.DrawRoundRect(rect, rect.Height / 2, rect.Height / 2, _fill);
        _stroke.Color = new SKColor(255, 255, 255, 22);
        _stroke.StrokeWidth = 1;
        canvas.DrawRoundRect(rect, rect.Height / 2, rect.Height / 2, _stroke);

        var alpha = index == 0 ? (byte)(160 + (int)(95 * pulse)) : (byte)220;
        _fill.Color = PillAccents[index].WithAlpha(alpha);
        canvas.DrawCircle(rect.Left + 13, rect.MidY, index == 0 ? 3.5f + (float)(0.8 * pulse) : 3.5f, _fill);

        _textPaint.Color = new SKColor(203, 213, 225);
        canvas.DrawText(_pillBlobs[index], rect.Left + 24, rect.MidY + 3.5f, _textPaint);
    }

    private void DrawProgress(SKCanvas canvas, SKRect track, SKRect fill)
    {
        _fill.Color = new SKColor(255, 255, 255, 26);
        canvas.DrawRoundRect(track, track.Height / 2, track.Height / 2, _fill);
        _fill.Color = new SKColor(96, 165, 250, 235);
        canvas.DrawRoundRect(fill, fill.Height / 2, fill.Height / 2, _fill);
    }

    private static SKRect ProgressTrack(float x, float y)
        => new(x + 136, y + 160, x + 136 + 240, y + 166);

    private static SKRect ProgressTrack(LayeredHostMode mode)
    {
        var (x, y) = mode == LayeredHostMode.Dock
            ? (PanelX, DockPanelY)
            : (PanelX, PanelY);
        return ProgressTrack(x, y);
    }

    private static SKRect ProgressFillFor(float x, float y, double progress)
    {
        var track = ProgressTrack(x, y);
        return new SKRect(track.Left, track.Top, track.Left + (float)(240 * progress), track.Bottom);
    }

    private static SKRect ProgressFill(LayeredHostMode mode, double progress)
    {
        var (x, y) = mode == LayeredHostMode.Dock
            ? (PanelX, DockPanelY)
            : (PanelX, PanelY);
        return ProgressFillFor(x, y, progress);
    }

    private static VisualRect ToDip(SKRect rect)
        => new(rect.Left, rect.Top, rect.Width, rect.Height);

    private static double ProgressAt(double seconds) => 0.25 + (0.2 * Math.Sin(seconds * 2.2));

    private static double PulseAt(double seconds) => 0.5 + (0.5 * Math.Sin(seconds * 3.0));

    /// <summary>
    /// Superellipse path sampled from Core's <see cref="ShellGeometry"/>: the exact same outline the
    /// product draws and hit-tests, proving the layered host can reuse the Core geometry.
    /// </summary>
    /// <remarks>
    /// <c>SampleOutline</c> returns the outline clockwise starting on the left edge at the top-left
    /// corner, and consecutive samples (including the closing segment) are directly connectable, so the
    /// path is built in order. (Before 2026-10-18 the sampler emitted the quadrants in a non-connectable
    /// order; the spike reversed 0/1/3 at the time. The Core contract is now "connect in order".)
    /// </remarks>
    private static SKPath BuildSquirclePath(float x, float y, float width, float height, float radius)
    {
        var geometry = FluidBar.Core.Layout.ShellGeometry.Create(new VisualRect(x, y, width, height),
            CornerRadii.Uniform(radius));
        var points = geometry.SampleOutline(24);
        var path = new SKPath();
        for (var i = 0; i < points.Length; i++)
        {
            var point = points[i];
            if (i == 0)
            {
                path.MoveTo((float)point.X, (float)point.Y);
            }
            else
            {
                path.LineTo((float)point.X, (float)point.Y);
            }
        }

        path.Close();
        return path;
    }

    private static VisualRect[] BuildPills(float x, float y, float width, float height, float gapX, float gapY,
        int columns)
    {
        var pills = new VisualRect[6];
        for (var i = 0; i < pills.Length; i++)
        {
            var column = i % columns;
            var row = i / columns;
            pills[i] = new VisualRect(
                x + (column * (width + gapX)),
                y + (row * (height + gapY)),
                width,
                height);
        }

        return pills;
    }

    /// <summary>Releases every Skia object.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _surface?.Dispose();
        _fill.Dispose();
        _stroke.Dispose();
        _textPaint.Dispose();
        _floatPanelPath.Dispose();
        _dockPanelPath.Dispose();
        _panelShader.Dispose();
        _artworkShader.Dispose();
        _logoShader.Dispose();
        _stripShader.Dispose();
        _shadowFilter.Dispose();
        _titleFont.Dispose();
        _subtitleFont.Dispose();
        _cardTitleFont.Dispose();
        _cardArtistFont.Dispose();
        _chipLabelFont.Dispose();
        _chipValueFont.Dispose();
        _pillFont.Dispose();
        _badgeFont.Dispose();
        _footerFont.Dispose();
        _titleBlob.Dispose();
        _subtitleBlob.Dispose();
        _badgeBlob.Dispose();
        _cardTitleBlob.Dispose();
        _cardArtistBlob.Dispose();
        _footerBlob.Dispose();
        _logoBlob.Dispose();
        _clockBlob.Dispose();
        foreach (var blob in _pillBlobs.Concat(_chipLabelBlobs).Concat(_chipValueBlobs))
        {
            blob.Dispose();
        }

        _regular.Dispose();
        _bold.Dispose();
    }
}
