namespace FluidBar.Core.Layout;

/// <summary>Island presentation mode (spec 2.2).</summary>
public enum IslandMode
{
    /// <summary>Floating pill near the work-area anchor (default).</summary>
    Float,

    /// <summary>Top-flush bar: top edge at y=0, bottom corners 22.</summary>
    Flush,

    /// <summary>Taskbar-replacement dock strip; the panel grows upwards.</summary>
    Dock,
}

/// <summary>The six anchor positions (v1 parity: Top/Bottom + left/center/right).</summary>
public enum IslandAnchor
{
    /// <summary>Top-left corner of the work area.</summary>
    TopLeft,

    /// <summary>Top centre (default for <see cref="IslandMode.Float"/>).</summary>
    TopCenter,

    /// <summary>Top-right corner.</summary>
    TopRight,

    /// <summary>Bottom-left (above the taskbar).</summary>
    BottomLeft,

    /// <summary>Bottom centre.</summary>
    BottomCenter,

    /// <summary>Bottom-right.</summary>
    BottomRight,
}

/// <summary>The visual form the layout must measure.</summary>
public enum ShellForm
{
    /// <summary>No visible shell; only the 10 DIP hover strip may exist.</summary>
    Hidden,

    /// <summary>Compact pill (idle or one active surface).</summary>
    Compact,

    /// <summary>Hover peek: compact + 10x6 DIP.</summary>
    Peek,

    /// <summary>Expanded 440x284 control centre.</summary>
    Expanded,

    /// <summary>Collapse target while the springs run (measured as compact).</summary>
    Closing,
}

/// <summary>Direction the expanded panel grows from its anchor.</summary>
public enum ExpandDirection
{
    /// <summary>Down (float/flush).</summary>
    Down,

    /// <summary>Up (dock).</summary>
    Up,
}

/// <summary>Corner radii in DIP, one per corner.</summary>
/// <param name="TopLeft">Top-left radius.</param>
/// <param name="TopRight">Top-right radius.</param>
/// <param name="BottomRight">Bottom-right radius.</param>
/// <param name="BottomLeft">Bottom-left radius.</param>
public readonly record struct CornerRadii(double TopLeft, double TopRight, double BottomRight, double BottomLeft)
{
    /// <summary>No rounding.</summary>
    public static CornerRadii Zero { get; } = new(0, 0, 0, 0);

    /// <summary>Uniform radius on all four corners.</summary>
    public static CornerRadii Uniform(double radius) => new(radius, radius, radius, radius);

    /// <summary>Largest radius of the four corners.</summary>
    public double Max => Math.Max(Math.Max(TopLeft, TopRight), Math.Max(BottomRight, BottomLeft));

    /// <summary>Whether all four corners are zero.</summary>
    public bool IsZero => Max <= 0;
}
