using System.Collections.Generic;

namespace FluidBar.Core.Presentation;

/// <summary>Host-neutral command kinds produced by plugin canvas recording and rendered by the host.</summary>
public enum IslandCanvasCommandKind
{
    FillRect = 0,
    FillRoundRect = 1,
    StrokeRoundRect = 2,
    DrawText = 3,
    DrawIcon = 4,
    DrawProgress = 5,
    PushClip = 6,
    PopClip = 7,
    PushOpacity = 8,
    PopOpacity = 9,
}

/// <summary>Host-owned text roles; plugins never supply fonts or point sizes.</summary>
public enum IslandCanvasTextStyle
{
    Title = 0,
    Body = 1,
    Caption = 2,
    Mono = 3,
}

/// <summary>Text alignment inside a command rectangle.</summary>
public enum IslandCanvasTextAlign
{
    Left = 0,
    Center = 1,
    Right = 2,
}

/// <summary>One recorded canvas command. Coordinates are page- or compact-local DIP.</summary>
public sealed record IslandCanvasCommand
{
    public required IslandCanvasCommandKind Kind { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public double CornerRadius { get; init; }
    public double StrokeWidth { get; init; } = 1;
    public double Opacity { get; init; } = 1;
    public string? Text { get; init; }
    public string? IconToken { get; init; }
    public string? Color { get; init; }
    public double? Progress { get; init; }
    public IslandCanvasTextStyle TextStyle { get; init; } = IslandCanvasTextStyle.Body;
    public IslandCanvasTextAlign TextAlign { get; init; } = IslandCanvasTextAlign.Left;
}

/// <summary>Host model for a custom page/canvas and its semantic hit regions.</summary>
public sealed record IslandCanvasView
{
    public IReadOnlyList<IslandCanvasCommand> Commands { get; init; } = [];
    public IReadOnlyList<IslandHitArea> HitAreas { get; init; } = [];
    public string? AccessibleName { get; init; }
}

/// <summary>Semantic hit region kinds the host maps to pointer/UIA behavior.</summary>
public enum IslandHitAreaKind
{
    Button = 0,
    Row = 1,
    Slider = 2,
    Scroll = 3,
}

/// <summary>One semantic hit region declared by a plugin canvas.</summary>
public sealed record IslandHitArea
{
    public required string Id { get; init; }
    public required IslandHitAreaKind Kind { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public string? CommandId { get; init; }
    public string? AccessibleName { get; init; }
    public bool LongPress { get; init; }
    public bool DoubleClick { get; init; }
    public double Min { get; init; }
    public double Max { get; init; } = 100;
    public double Value { get; init; }
}

/// <summary>Host model for a plugin-declared compact canvas.</summary>
public sealed record IslandCompactCanvas
{
    public double Width { get; init; }
    public double Height { get; init; }
    public IReadOnlyList<IslandCanvasCommand> Commands { get; init; } = [];
    public string? AccessibleName { get; init; }
    public string? BackgroundColor { get; init; }
    public string? ForegroundColor { get; init; }
}

