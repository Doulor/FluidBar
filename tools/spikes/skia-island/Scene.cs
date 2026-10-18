using SkiaSharp;

namespace FluidBar.SkiaIslandSpike;

/// <summary>One retained draw node: stable id, region, z-order, DIP bounds and a cached draw action.</summary>
internal sealed class SceneNode
{
    public required string Id { get; init; }
    public required string Region { get; init; }
    public required int Z { get; init; }
    public required SKRect Bounds { get; init; }
    public required Action<SKCanvas> Draw { get; init; }
    public bool Visible { get; set; } = true;
}

/// <summary>Baked static shell (shadow + fill + hairline) used as the scene background.</summary>
internal sealed class BackgroundLayer
{
    public required SKImage Image { get; init; }
    public required SKRect Dest { get; init; }
    public required Action<SKCanvas> DrawLive { get; init; }
    public bool UseStatic { get; set; } = true;
}

/// <summary>
/// Retained scene graph for the island: background layer + content nodes ordered by z. Rendering a
/// dirty rect clears just that rect, blits the background clipped to it, then draws intersecting nodes.
/// </summary>
internal sealed class IslandScene
{
    private readonly List<SceneNode> _nodes = [];
    private readonly SKPaint _clearPaint = new() { BlendMode = SKBlendMode.Src };

    public IslandScene(string name, double width, double height)
    {
        Name = name;
        Width = width;
        Height = height;
    }

    public string Name { get; }
    public double Width { get; }
    public double Height { get; }
    public BackgroundLayer? Background { get; set; }
    public IReadOnlyList<SceneNode> Nodes => _nodes;
    public int LastContentNodesDrawn { get; private set; }
    public int LastContentNodesTotal => _nodes.Count;

    public SceneNode Add(string id, string region, int z, SKRect bounds, Action<SKCanvas> draw)
    {
        var node = new SceneNode
        {
            Id = id,
            Region = region,
            Z = z,
            Bounds = bounds,
            Draw = draw,
        };
        _nodes.Add(node);
        _nodes.Sort(static (a, b) => a.Z.CompareTo(b.Z));
        return node;
    }

    public void Render(SKCanvas canvas, SKRect? dirtyDip, bool useStaticBackground)
    {
        var full = new SKRect(0, 0, (float)Width, (float)Height);
        var dirty = dirtyDip ?? full;
        var clipped = dirtyDip.HasValue;

        canvas.Save();
        if (clipped)
        {
            canvas.ClipRect(dirty);
        }

        _clearPaint.Color = SKColors.Transparent;
        canvas.DrawRect(dirty, _clearPaint);

        if (Background is not null)
        {
            if (useStaticBackground && Background.UseStatic)
            {
                canvas.DrawImage(Background.Image, Background.Dest);
            }
            else
            {
                Background.DrawLive(canvas);
            }
        }

        var drawn = 0;
        foreach (var node in _nodes)
        {
            if (!node.Visible)
            {
                continue;
            }

            if (clipped && !node.Bounds.Intersects(dirty))
            {
                continue;
            }

            node.Draw(canvas);
            drawn++;
        }

        LastContentNodesDrawn = drawn;
        canvas.Restore();
    }
}
