using SkiaSharp;

namespace FluidBar.Island;

/// <summary>
/// Skia paint factory for the L0–L4 material stack. The renderer owns its own <see cref="SKPaint"/> instances;
/// this factory builds them from the tokens so the layer values cannot drift from <see cref="MaterialTokens"/>.
/// </summary>
public static class IslandMaterialBrushes
{
    /// <summary>L0 container fill (opaque, no shader).</summary>
    public static SKPaint CreateContainerFill(MaterialTokens tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        return new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = tokens.ContainerColor, Shader = null };
    }

    /// <summary>L1 card fill (translucent #141418).</summary>
    public static SKPaint CreateCardFill(MaterialTokens tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        return new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = tokens.L1.Color };
    }

    /// <summary>L2 separator stroke (white 9 %, 1 DIP).</summary>
    public static SKPaint CreateSeparatorStroke(MaterialTokens tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        return new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)tokens.L2.StrokeWidthDip,
            Color = tokens.L2.Color,
        };
    }

    /// <summary>
    /// L3 top highlight paint: vertical gradient from white 5 % to transparent over the highlight height,
    /// anchored at the card's top edge (<paramref name="rect"/>), not at the canvas origin.
    /// </summary>
    public static SKPaint CreateTopHighlight(MaterialTokens tokens, SKRect rect)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        var token = tokens.L3;
        var height = (float)Math.Max(0.5, token.HeightDip);
        var shader = SKShader.CreateLinearGradient(
            new SKPoint(rect.Left, rect.Top),
            new SKPoint(rect.Left, rect.Top + height),
            [token.Color, token.GradientEndColor],
            null,
            SKShaderTileMode.Clamp);
        return new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Shader = shader,
        };
    }

    /// <summary>L4 pressed inner shadow paint: black 18 % with a Gaussian blur (sigma = inset / 2).</summary>
    public static SKPaint CreatePressedInset(MaterialTokens tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        var token = tokens.L4;
        var sigma = (float)Math.Max(0.5, token.BlurSigmaDip);
        return new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = token.Color,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma),
        };
    }

    /// <summary>Rounded-rect path for L1 cards (radius 12 from the token).</summary>
    public static SKRoundRect CreateCardPath(MaterialTokens tokens, SKRect rect)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        var radius = (float)tokens.L1.CornerRadiusDip;
        return new SKRoundRect(rect, radius, radius);
    }
}

/// <summary>
/// A reusable, disposable set of material paints plus the five draw helpers. Draw the stack in order:
/// container → card → separator → top highlight → pressed inset. Every helper clips to the card shape so a
/// highlight or inset never bleeds into a neighbouring row.
/// </summary>
public sealed class IslandMaterialPaintSet : IDisposable
{
    private readonly SKPaint _container;
    private readonly SKPaint _card;
    private readonly SKPaint _separator;
    private readonly SKPaint _pressedInset;
    private bool _disposed;

    /// <summary>Creates paints for one token stack.</summary>
    public IslandMaterialPaintSet(MaterialTokens tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        Tokens = tokens;
        _container = IslandMaterialBrushes.CreateContainerFill(tokens);
        _card = IslandMaterialBrushes.CreateCardFill(tokens);
        _separator = IslandMaterialBrushes.CreateSeparatorStroke(tokens);
        _pressedInset = IslandMaterialBrushes.CreatePressedInset(tokens);
    }

    /// <summary>The token stack these paints were built from.</summary>
    public MaterialTokens Tokens { get; }

    /// <summary>L0 fill paint.</summary>
    public SKPaint Container => _container;

    /// <summary>L1 fill paint.</summary>
    public SKPaint Card => _card;

    /// <summary>L2 stroke paint.</summary>
    public SKPaint Separator => _separator;

    /// <summary>L4 blurred fill paint.</summary>
    public SKPaint PressedInset => _pressedInset;

    /// <summary>Draws the L0 container.</summary>
    public void DrawContainer(SKCanvas canvas, SKRect rect)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        canvas.DrawRect(rect, _container);
    }

    /// <summary>Draws the L1 card (rounded 12 DIP).</summary>
    public void DrawCard(SKCanvas canvas, SKRect rect)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var radius = (float)Tokens.L1.CornerRadiusDip;
        canvas.DrawRoundRect(rect, radius, radius, _card);
    }

    /// <summary>Draws the L2 separator between <paramref name="left"/> and <paramref name="right"/> at <paramref name="y"/>.</summary>
    public void DrawSeparator(SKCanvas canvas, float left, float right, float y)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        canvas.DrawLine(left, y, right, y, _separator);
    }

    /// <summary>Draws the L3 top highlight over the card rect (clipped to the card shape).</summary>
    public void DrawTopHighlight(SKCanvas canvas, SKRect rect)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var radius = (float)Tokens.L1.CornerRadiusDip;
        var height = (float)Tokens.L3.HeightDip;
        using var clip = new SKPath();
        clip.AddRoundRect(new SKRoundRect(rect, radius, radius));
        canvas.Save();
        try
        {
            canvas.ClipPath(clip, SKClipOperation.Intersect, antialias: true);
            using var paint = IslandMaterialBrushes.CreateTopHighlight(Tokens, rect);
            canvas.DrawRect(SKRect.Create(rect.Left, rect.Top, rect.Width, height), paint);
        }
        finally
        {
            canvas.Restore();
        }
    }

    /// <summary>Draws the L4 pressed inner shadow: a blurred black band bleeding down from the card's top edge.</summary>
    public void DrawPressedInset(SKCanvas canvas, SKRect rect)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var radius = (float)Tokens.L1.CornerRadiusDip;
        var inset = (float)Tokens.L4.HeightDip;
        using var clip = new SKPath();
        clip.AddRoundRect(new SKRoundRect(rect, radius, radius));
        canvas.Save();
        try
        {
            canvas.ClipPath(clip, SKClipOperation.Intersect, antialias: true);

            // The shadow source sits just above the card; the blur bleeds into the top inset band, exactly how
            // an inner "pressed" inset reads (no bottom/left/right shadow contribution).
            var band = SKRect.Create(rect.Left - radius, rect.Top - (inset * 2), rect.Width + (radius * 2), inset * 2);
            canvas.DrawRect(band, _pressedInset);
        }
        finally
        {
            canvas.Restore();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _container.Dispose();
        _card.Dispose();
        _separator.Dispose();
        _pressedInset.Dispose();
    }
}
