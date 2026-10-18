using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FluidBar.App.Rendering;
using FluidBar.App.Theme;
using FluidBar.Core.Layout;

namespace FluidBar.App.Island;

/// <summary>
/// One immutable visual frame of the island shell: the animating geometry plus the derived content
/// opacities. The motion driver produces these at compositor frame rate; the shell consumes them with
/// a single repaint (no layout, no window resize).
/// </summary>
/// <param name="Width">Shell width in DIP.</param>
/// <param name="Height">Shell height in DIP.</param>
/// <param name="Radii">Per-corner radii in DIP.</param>
/// <param name="OriginX">Shell left edge inside the host window (DIP).</param>
/// <param name="OriginY">Shell top edge inside the host window (DIP).</param>
/// <param name="CompactOpacity">Opacity of the compact content layer.</param>
/// <param name="ExpandedOpacity">Opacity of the expanded content layer.</param>
/// <param name="ShellOpacity">Whole-shell opacity (hide fade).</param>
/// <param name="NudgeOffsetY">Transient vertical nudge offset in DIP.</param>
/// <param name="PressScale">Press scale (1 = not pressed).</param>
/// <param name="GhostCount">Number of ghost pills behind the main pill.</param>
/// <param name="Palette">Island palette.</param>
/// <param name="FillOpacity">Shell fill opacity (0.85–1.0).</param>
/// <param name="IsAcrylic">Whether the acrylic material is active (lower fill alpha).</param>
/// <param name="IsHidden">No shell is drawn (fully hidden/click-through).</param>
public readonly record struct ShellFrame(
    double Width,
    double Height,
    CornerRadii Radii,
    double OriginX,
    double OriginY,
    double CompactOpacity,
    double ExpandedOpacity,
    double ShellOpacity,
    double NudgeOffsetY,
    double PressScale,
    int GhostCount,
    IslandColors Palette,
    double FillOpacity,
    bool IsAcrylic,
    bool IsHidden);

/// <summary>
/// The single island visual (spec 5.6/4.4): a panel that owns the custom-drawn shell background plus
/// the compact and expanded content layers. Content layers are laid out once at their design sizes;
/// only render transforms / opacity / clip change per frame. The squircle, ghost stack and shadow are
/// painted by a dedicated child control because Avalonia 12 seals <see cref="Panel.Render"/>.
/// </summary>
public sealed class IslandShell : Panel
{
    private readonly ShellBackgroundVisual _background;
    private readonly RectangleGeometry _expandedClip = new();
    private Control? _compactLayer;
    private Control? _expandedLayer;
    private ShellFrame _frame = new(
        LayoutTokens.CompactSize(null).Width,
        LayoutTokens.CompactSize(null).Height,
        CornerRadii.Uniform(18),
        UiMetrics.WindowPadding,
        UiMetrics.WindowPadding,
        1,
        0,
        1,
        0,
        1,
        0,
        IslandColors.Obsidian,
        LayoutTokens.FillOpacity,
        false,
        true);
    private Size _compactLayerSize;

    /// <summary>Creates the island shell.</summary>
    public IslandShell()
    {
        _background = new ShellBackgroundVisual(this);
        Children.Add(_background);
    }

    /// <summary>Current visual frame (read by hit-testing/diagnostics/tests).</summary>
    public ShellFrame Frame => _frame;

    /// <summary>Design size of the expanded layer (uniform 440×284).</summary>
    public static Size ExpandedLayerSize => new(LayoutTokens.ExpandedWidth, LayoutTokens.ExpandedHeight);

    /// <summary>Design origin of the shell inside the fixed host window (DIP).</summary>
    public static Point DefaultShellOrigin => new(UiMetrics.WindowPadding, UiMetrics.WindowPadding);

    /// <summary>Whether the shell currently receives pointer input (hidden = false).</summary>
    public bool IsInteractive => !_frame.IsHidden && _frame.ShellOpacity > 0.01;

    /// <summary>Sets the compact content layer (created by the host on activity kind changes).</summary>
    public void SetCompactLayer(Control? layer, Size designSize)
    {
        if (ReferenceEquals(_compactLayer, layer))
        {
            _compactLayerSize = designSize;
            return;
        }

        if (_compactLayer is not null)
        {
            Children.Remove(_compactLayer);
        }

        _compactLayer = layer;
        _compactLayerSize = designSize;
        if (layer is not null)
        {
            Children.Add(layer);
            InvalidateArrange();
        }
    }

    /// <summary>Sets the expanded content layer (rail + pages).</summary>
    public void SetExpandedLayer(Control? layer)
    {
        if (ReferenceEquals(_expandedLayer, layer))
        {
            return;
        }

        if (_expandedLayer is not null)
        {
            Children.Remove(_expandedLayer);
        }

        _expandedLayer = layer;
        if (layer is not null)
        {
            Children.Add(layer);
            InvalidateArrange();
        }
    }

    /// <summary>Applies one motion frame: transforms + opacity + clip, then one repaint.</summary>
    public void ApplyFrame(in ShellFrame frame)
    {
        _frame = frame;
        UpdateLayerTransforms(frame);

        // Hit-testing must not reach invisible content layers.
        if (_compactLayer is not null)
        {
            _compactLayer.IsHitTestVisible = frame.CompactOpacity > 0.5 && !frame.IsHidden;
        }

        if (_expandedLayer is not null)
        {
            _expandedLayer.IsHitTestVisible = frame.ExpandedOpacity > 0.5 && !frame.IsHidden;
        }

        _background.InvalidateVisual();
    }

    /// <summary>Repaints the background without changing the frame (theme/palette only).</summary>
    public void InvalidateShell() => _background.InvalidateVisual();

    /// <summary>Maps a host-window point (DIP) to the compact layer's local coordinates.</summary>
    public bool TryCompactLocalPoint(Point windowPoint, out Point localPoint)
    {
        localPoint = default;
        if (_compactLayer is null || _compactLayerSize.Width <= 0)
        {
            return false;
        }

        var press = Math.Max(0.5, _frame.PressScale);
        var shellCenter = new Point(_frame.OriginX + (_frame.Width / 2), _frame.OriginY + (_frame.Height / 2) + _frame.NudgeOffsetY);
        var tx = shellCenter.X - ((_compactLayerSize.Width / 2) * press);
        var ty = shellCenter.Y - ((_compactLayerSize.Height / 2) * press);
        localPoint = new Point((windowPoint.X - tx) / press, (windowPoint.Y - ty) / press);
        return localPoint.X >= 0 && localPoint.Y >= 0
            && localPoint.X <= _compactLayerSize.Width && localPoint.Y <= _compactLayerSize.Height;
    }

    /// <summary>Whether a host-window point (DIP) is inside the drawn squircle.</summary>
    public bool ContainsPoint(Point windowPoint)
    {
        if (_frame.IsHidden || _frame.ShellOpacity <= 0.01)
        {
            return false;
        }

        return CreateShellGeometry().Contains(windowPoint.X, windowPoint.Y);
    }

    /// <summary>Current shell geometry for the settle-time window region update.</summary>
    public ShellGeometry CreateShellGeometry()
    {
        var bounds = new VisualRect(
            _frame.OriginX,
            _frame.OriginY + _frame.NudgeOffsetY,
            _frame.Width,
            _frame.Height);
        return ShellGeometry.Create(bounds, _frame.Radii);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var windowSize = new Size(UiMetrics.WindowWidth, UiMetrics.WindowHeight);
        _background.Measure(windowSize);
        _compactLayer?.Measure(new Size(_compactLayerSize.Width, _compactLayerSize.Height));
        _expandedLayer?.Measure(ExpandedLayerSize);
        return windowSize;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        _background.Arrange(new Rect(0, 0, UiMetrics.WindowWidth, UiMetrics.WindowHeight));
        _compactLayer?.Arrange(new Rect(0, 0, _compactLayerSize.Width, _compactLayerSize.Height));
        _expandedLayer?.Arrange(new Rect(0, 0, ExpandedLayerSize.Width, ExpandedLayerSize.Height));
        UpdateLayerTransforms(_frame);
        return finalSize;
    }

    private void UpdateLayerTransforms(in ShellFrame frame)
    {
        var press = Math.Max(0.5, frame.PressScale);
        var shellCenter = new Point(
            frame.OriginX + (frame.Width / 2),
            frame.OriginY + (frame.Height / 2) + frame.NudgeOffsetY);

        if (_compactLayer is not null && _compactLayerSize.Width > 0)
        {
            var tx = shellCenter.X - ((_compactLayerSize.Width / 2) * press);
            var ty = shellCenter.Y - ((_compactLayerSize.Height / 2) * press);
            _compactLayer.RenderTransform = CreateLayerTransform(tx, ty, press, shellCenter);
            _compactLayer.Opacity = Math.Clamp(frame.CompactOpacity * frame.ShellOpacity, 0, 1);
        }

        if (_expandedLayer is not null)
        {
            var tx = shellCenter.X - ((ExpandedLayerSize.Width / 2) * press);
            var ty = shellCenter.Y - ((ExpandedLayerSize.Height / 2) * press);
            _expandedLayer.RenderTransform = CreateLayerTransform(tx, ty, press, shellCenter);
            _expandedLayer.Opacity = Math.Clamp(frame.ExpandedOpacity * frame.ShellOpacity, 0, 1);

            // Reveal the expanded layer through the animating shell rect (render-only clip update).
            var clipWidth = frame.Width / press;
            var clipHeight = frame.Height / press;
            _expandedClip.Rect = new Rect(
                (ExpandedLayerSize.Width / 2) - (clipWidth / 2),
                (ExpandedLayerSize.Height / 2) - (clipHeight / 2),
                Math.Max(0, clipWidth),
                Math.Max(0, clipHeight));
            _expandedLayer.Clip = _expandedClip;
        }
    }

    private static ITransform CreateLayerTransform(double tx, double ty, double scale, Point shellCenter)
    {
        var matrix = Matrix.CreateTranslation(tx - shellCenter.X, ty - shellCenter.Y)
            * Matrix.CreateScale(scale, scale)
            * Matrix.CreateTranslation(shellCenter.X, shellCenter.Y);
        return new MatrixTransform(matrix);
    }

    /// <summary>
    /// Custom-drawn shell background: shadow, ghost pills, squircle fill and hairline. This is the only
    /// per-frame drawing work; content layers composite on top.
    /// </summary>
    private sealed class ShellBackgroundVisual : Control
    {
        private static readonly double[] ShadowScales = [1.10, 1.065, 1.035, 1.012];
        private static readonly double[] ShadowAlphas = [0.10, 0.085, 0.06, 0.035];

        private readonly IslandShell _owner;

        public ShellBackgroundVisual(IslandShell owner)
        {
            _owner = owner;
            IsHitTestVisible = false;
        }

        public override void Render(DrawingContext context)
        {
            var frame = _owner._frame;
            if (frame.IsHidden || frame.ShellOpacity <= 0.01 || frame.Width <= 0.5 || frame.Height <= 0.5)
            {
                return;
            }

            using var shellOpacity = context.PushOpacity(Math.Clamp(frame.ShellOpacity, 0, 1));
            var localRect = new Rect(0, 0, frame.Width, frame.Height);
            var geometry = SquircleGeometryFactory.GetOrCreate(frame.Width, frame.Height, frame.Radii);
            var center = localRect.Center;

            using (context.PushTransform(Matrix.CreateTranslation(frame.OriginX, frame.OriginY + frame.NudgeOffsetY)))
            {
                DrawGhosts(context, frame, localRect);
                DrawShadow(context, frame, geometry, center);
                DrawBody(context, frame, geometry);
            }
        }

        private static void DrawBody(DrawingContext context, in ShellFrame frame, Geometry geometry)
        {
            var fillAlpha = frame.IsAcrylic ? 0.72 : Math.Clamp(frame.FillOpacity, LayoutTokens.MinFillOpacity, 1.0);
            var fill = new SolidColorBrush(frame.Palette.Background, fillAlpha);
            if (frame.IsAcrylic)
            {
                var gradient = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF), 0),
                        new GradientStop(Colors.Transparent, 0.35),
                    },
                };
                context.DrawGeometry(fill, null, geometry);
                context.DrawGeometry(gradient, null, geometry);
            }
            else
            {
                context.DrawGeometry(fill, null, geometry);
            }

            var stroke = new Pen(new SolidColorBrush(frame.Palette.LineStrong), LayoutTokens.HairlineWidth);
            context.DrawGeometry(null, stroke, geometry);
        }

        private static void DrawGhosts(DrawingContext context, in ShellFrame frame, Rect rect)
        {
            if (frame.GhostCount <= 0 || frame.CompactOpacity <= 0.5)
            {
                return;
            }

            var offsets = new[] { LayoutTokens.GhostOffsetY1, LayoutTokens.GhostOffsetY2 };
            var scales = new[] { LayoutTokens.GhostScale1, LayoutTokens.GhostScale2 };
            var opacities = new[] { LayoutTokens.GhostOpacity1, LayoutTokens.GhostOpacity2 };
            var count = Math.Min(frame.GhostCount, LayoutTokens.MaxGhosts);
            for (var i = 0; i < count; i++)
            {
                var scale = scales[i];
                var ghostWidth = rect.Width * scale;
                var ghostHeight = rect.Height * scale;
                var ghostRect = new Rect(
                    (rect.Width - ghostWidth) / 2,
                    offsets[i],
                    ghostWidth,
                    ghostHeight);
                var ghostRadii = CornerRadii.Uniform(Math.Min(frame.Radii.Max, ghostRect.Height / 2));
                var ghostGeometry = SquircleGeometryFactory.GetOrCreate(ghostRect.Width, ghostRect.Height, ghostRadii);
                var alpha = opacities[i] * frame.ShellOpacity;
                var fill = new SolidColorBrush(frame.Palette.Background, alpha);
                var stroke = new Pen(new SolidColorBrush(frame.Palette.LineStrong, alpha), LayoutTokens.HairlineWidth);
                using var state = context.PushTransform(Matrix.CreateTranslation(ghostRect.X, ghostRect.Y));
                context.DrawGeometry(fill, stroke, ghostGeometry);
            }
        }

        private static void DrawShadow(DrawingContext context, in ShellFrame frame, Geometry geometry, Point center)
        {
            for (var i = 0; i < ShadowScales.Length; i++)
            {
                var matrix = Matrix.CreateTranslation(-center.X, -center.Y)
                    * Matrix.CreateScale(ShadowScales[i], ShadowScales[i])
                    * Matrix.CreateTranslation(center.X, center.Y + LayoutTokens.ShadowOffsetY);
                using var transform = context.PushTransform(matrix);
                var brush = new SolidColorBrush(Colors.Black, ShadowAlphas[i] * Math.Clamp(frame.ShellOpacity, 0, 1));
                context.DrawGeometry(brush, null, geometry);
            }
        }
    }
}
