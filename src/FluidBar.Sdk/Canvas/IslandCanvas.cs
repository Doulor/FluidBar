using System;
using System.Collections.Generic;
using System.Globalization;

namespace FluidBar.Sdk.Canvas;

public enum CanvasCommandKind { FillRect = 0, FillRoundRect = 1, StrokeRoundRect = 2, DrawText = 3, DrawIcon = 4, DrawProgress = 5, PushClip = 6, PopClip = 7, PushOpacity = 8, PopOpacity = 9 }
public enum CanvasTextStyle { Title = 0, Body = 1, Caption = 2, Mono = 3 }
public enum CanvasTextAlign { Left = 0, Center = 1, Right = 2 }

public sealed record CanvasCommand
{
    public required CanvasCommandKind Kind { get; init; }
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
    public CanvasTextStyle TextStyle { get; init; } = CanvasTextStyle.Body;
    public CanvasTextAlign TextAlign { get; init; } = CanvasTextAlign.Left;
}

public interface IIslandCanvas
{
    void FillRect(double x, double y, double width, double height, string color, double opacity = 1.0);
    void FillRoundRect(double x, double y, double width, double height, double radius, string color, double opacity = 1.0);
    void StrokeRoundRect(double x, double y, double width, double height, double radius, string color, double strokeWidth = 1.0, double opacity = 1.0);
    void DrawText(string text, double x, double y, double width, double height, string color, CanvasTextStyle style = CanvasTextStyle.Body, CanvasTextAlign align = CanvasTextAlign.Left, double opacity = 1.0);
    void DrawIcon(string iconToken, double x, double y, double size, string color, double opacity = 1.0);
    void DrawProgress(double x, double y, double width, double height, double progress, string color, double opacity = 1.0);
    void PushClip(double x, double y, double width, double height);
    void PopClip();
    void PushOpacity(double opacity);
    void PopOpacity();
}

public sealed class CanvasRecorder : IIslandCanvas
{
    private readonly List<CanvasCommand> _commands = [];
    private int _clipDepth;
    private int _opacityDepth;
    public IReadOnlyList<CanvasCommand> Commands => _commands;
    public IReadOnlyList<CanvasCommand> Build() => _commands.ToArray();
    public void FillRect(double x, double y, double width, double height, string color, double opacity = 1.0) => Add(new CanvasCommand { Kind = CanvasCommandKind.FillRect, X = x, Y = y, Width = width, Height = height, Color = color, Opacity = opacity });
    public void FillRoundRect(double x, double y, double width, double height, double radius, string color, double opacity = 1.0) => Add(new CanvasCommand { Kind = CanvasCommandKind.FillRoundRect, X = x, Y = y, Width = width, Height = height, CornerRadius = radius, Color = color, Opacity = opacity });
    public void StrokeRoundRect(double x, double y, double width, double height, double radius, string color, double strokeWidth = 1.0, double opacity = 1.0) => Add(new CanvasCommand { Kind = CanvasCommandKind.StrokeRoundRect, X = x, Y = y, Width = width, Height = height, CornerRadius = radius, Color = color, StrokeWidth = strokeWidth, Opacity = opacity });
    public void DrawText(string text, double x, double y, double width, double height, string color, CanvasTextStyle style = CanvasTextStyle.Body, CanvasTextAlign align = CanvasTextAlign.Left, double opacity = 1.0) => Add(new CanvasCommand { Kind = CanvasCommandKind.DrawText, Text = text, X = x, Y = y, Width = width, Height = height, Color = color, TextStyle = style, TextAlign = align, Opacity = opacity });
    public void DrawIcon(string iconToken, double x, double y, double size, string color, double opacity = 1.0) => Add(new CanvasCommand { Kind = CanvasCommandKind.DrawIcon, IconToken = iconToken, X = x, Y = y, Width = size, Height = size, Color = color, Opacity = opacity });
    public void DrawProgress(double x, double y, double width, double height, double progress, string color, double opacity = 1.0) => Add(new CanvasCommand { Kind = CanvasCommandKind.DrawProgress, X = x, Y = y, Width = width, Height = height, Progress = progress, Color = color, Opacity = opacity });
    public void PushClip(double x, double y, double width, double height) { Add(new CanvasCommand { Kind = CanvasCommandKind.PushClip, X = x, Y = y, Width = width, Height = height }); _clipDepth++; }
    public void PopClip() { if (_clipDepth <= 0) throw new InvalidOperationException("PopClip without PushClip."); Add(new CanvasCommand { Kind = CanvasCommandKind.PopClip }); _clipDepth--; }
    public void PushOpacity(double opacity) { Add(new CanvasCommand { Kind = CanvasCommandKind.PushOpacity, Opacity = opacity }); _opacityDepth++; }
    public void PopOpacity() { if (_opacityDepth <= 0) throw new InvalidOperationException("PopOpacity without PushOpacity."); Add(new CanvasCommand { Kind = CanvasCommandKind.PopOpacity }); _opacityDepth--; }
    private void Add(CanvasCommand command)
    {
        if (_commands.Count >= CanvasLimits.MaxCommands) throw new InvalidOperationException($"Canvas command limit ({CanvasLimits.MaxCommands}) exceeded.");
        if (command.Text is { Length: > CanvasLimits.MaxTextCharacters }) throw new ArgumentException("Canvas text too long.", nameof(command));
        _commands.Add(command);
    }
}

public static class CanvasLimits
{
    public const int MaxCommands = 256;
    public const int MaxTextCharacters = 512;
    public const int MaxHitAreas = 32;
    public const double MinCompactWidth = 132;
    public const double MaxCompactWidth = 430;
    public const double MinCompactHeight = 36;
    public const double MaxCompactHeight = 44;
    public const double MinContrastRatio = 4.5;
}

public enum PageHitAreaKind { Button = 0, Row = 1, Slider = 2, Scroll = 3 }

public sealed record PageHitArea
{
    public required string Id { get; init; }
    public required PageHitAreaKind Kind { get; init; }
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

public sealed record PageCanvasData
{
    public required IReadOnlyList<CanvasCommand> Commands { get; init; }
    public IReadOnlyList<PageHitArea> HitAreas { get; init; } = [];
    public string? AccessibleName { get; init; }
}

public sealed record CompactViewDescriptor
{
    public required IReadOnlyList<CanvasCommand> Commands { get; init; }
    public double Width { get; init; } = 236;
    public double Height { get; init; } = 36;
    public string BackgroundColor { get; init; } = "#0B0B10";
    public string ForegroundColor { get; init; } = "#FFFFFF";
    public string? AccessibleName { get; init; }
}

public sealed record PreviewCanvasData
{
    public required IReadOnlyList<CanvasCommand> Commands { get; init; }
    public double Width { get; init; } = 400;
    public double Height { get; init; } = 160;
    public string? AccessibleName { get; init; }
}

public static class IslandCanvasValidator
{
    private static readonly HashSet<string> Tokens = new(StringComparer.OrdinalIgnoreCase)
    { "agent", "check", "alert", "pause", "bell", "media", "clipboard", "clock", "download", "search", "lock", "usb", "bluetooth", "home", "volume", "brightness", "battery", "timer", "calendar", "grid", "settings", "warning", "close", "play", "next", "previous" };
    private static readonly HashSet<string> Colors = new(StringComparer.OrdinalIgnoreCase)
    { "accent", "foreground", "foregroundSecondary", "foregroundTertiary", "success", "warning", "danger", "info", "track", "transparent" };

    public static bool Validate(IReadOnlyList<CanvasCommand> commands, double width, double height, out string? error)
    {
        if (commands is null)
        { error = "Canvas commands are missing."; return false; }
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
        { error = "Canvas bounds must be finite and positive."; return false; }
        if (commands.Count > CanvasLimits.MaxCommands)
        { error = "Canvas command limit exceeded."; return false; }
        var clip = 0; var op = 0;
        foreach (var c in commands)
        {
            if (c is null) { error = "Canvas command is null."; return false; }
            if (!double.IsFinite(c.X) || !double.IsFinite(c.Y) || !double.IsFinite(c.Width) || !double.IsFinite(c.Height)
                || c.Width < 0 || c.Height < 0 || c.CornerRadius < 0 || c.StrokeWidth < 0
                || c.X < -0.01 || c.Y < -0.01 || c.X + c.Width > width + 0.01 || c.Y + c.Height > height + 0.01)
            { error = "Canvas command geometry is outside the canvas."; return false; }
            if (!double.IsFinite(c.Opacity) || c.Opacity is < 0 or > 1)
            { error = "Canvas opacity must be in 0..1."; return false; }
            if ((c.Kind is CanvasCommandKind.FillRect or CanvasCommandKind.FillRoundRect
                or CanvasCommandKind.StrokeRoundRect or CanvasCommandKind.DrawText
                or CanvasCommandKind.DrawIcon or CanvasCommandKind.DrawProgress)
                && !ValidColor(c.Color))
            { error = "Canvas color is not a host token or hex."; return false; }
            switch (c.Kind)
            {
                case CanvasCommandKind.PushClip: if (++clip > 8) { error = "Canvas clip depth exceeds 8."; return false; } break;
                case CanvasCommandKind.PopClip: if (--clip < 0) { error = "PopClip without PushClip."; return false; } break;
                case CanvasCommandKind.PushOpacity: if (++op > 8 || c.Opacity is < 0 or > 1) { error = "Canvas opacity depth invalid."; return false; } break;
                case CanvasCommandKind.PopOpacity: if (--op < 0) { error = "PopOpacity without PushOpacity."; return false; } break;
                case CanvasCommandKind.DrawText:
                    if (string.IsNullOrWhiteSpace(c.Text) || c.Text.Length > CanvasLimits.MaxTextCharacters) { error = "Canvas text is invalid."; return false; }
                    if (!Enum.IsDefined(c.TextStyle) || !Enum.IsDefined(c.TextAlign)) { error = "Canvas text style is invalid."; return false; }
                    break;
                case CanvasCommandKind.DrawIcon:
                    if (string.IsNullOrWhiteSpace(c.IconToken) || !Tokens.Contains(c.IconToken)) { error = "Canvas icon token is not allowed."; return false; }
                    break;
                case CanvasCommandKind.DrawProgress:
                    if (c.Progress is not { } p || !double.IsFinite(p) || p < 0 || p > 1) { error = "Canvas progress must be in 0..1."; return false; }
                    break;
            }
        }
        if (clip != 0 || op != 0) { error = "Canvas has an unmatched clip/opacity scope."; return false; }
        error = null; return true;
    }

    public static bool ValidateHitAreas(IReadOnlyList<PageHitArea>? areas, double width, double height, out string? error)
    {
        if (areas is null) { error = null; return true; }
        if (areas.Count > CanvasLimits.MaxHitAreas) { error = "Canvas hit-area limit exceeded."; return false; }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in areas)
        {
            if (a is null || string.IsNullOrWhiteSpace(a.Id) || !ids.Add(a.Id)
                || a.X < 0 || a.Y < 0 || a.X + a.Width > width + 0.01 || a.Y + a.Height > height + 0.01
                || a.AccessibleName is { Length: > CanvasLimits.MaxTextCharacters })
            { error = "Canvas hit area is invalid or outside the canvas."; return false; }
            if (a.Kind == PageHitAreaKind.Slider && (!double.IsFinite(a.Min) || !double.IsFinite(a.Max) || a.Max <= a.Min))
            { error = "Canvas slider range is invalid."; return false; }
        }
        error = null; return true;
    }

    public static bool ValidateCompact(CompactViewDescriptor descriptor, out string? error)
    {
        if (descriptor is null || descriptor.Commands is null)
        { error = "Compact view is missing commands."; return false; }
        if (descriptor.AccessibleName is { Length: > CanvasLimits.MaxTextCharacters })
        { error = "Compact view accessible name is too long."; return false; }
        if (!double.IsFinite(descriptor.Width) || !double.IsFinite(descriptor.Height)
            || descriptor.Width < CanvasLimits.MinCompactWidth || descriptor.Width > CanvasLimits.MaxCompactWidth
            || descriptor.Height < CanvasLimits.MinCompactHeight || descriptor.Height > CanvasLimits.MaxCompactHeight)
        { error = "Compact view size is outside host bounds."; return false; }
        if (!Validate(descriptor.Commands, descriptor.Width, descriptor.Height, out error)) return false;
        if (!TryHex(descriptor.BackgroundColor, out var bg) || !TryHex(descriptor.ForegroundColor, out var fg)
            || Contrast(bg, fg) < CanvasLimits.MinContrastRatio)
        { error = "Compact view contrast is too low."; return false; }
        error = null; return true;
    }

    private static bool ValidColor(string? value)
        => !string.IsNullOrWhiteSpace(value) && (Colors.Contains(value) || TryHex(value, out _));

    private static bool TryHex(string? value, out RgbaColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value) || value[0] != '#' || value.Length is not (7 or 9)
            || !uint.TryParse(value[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var raw)) return false;
        color = value.Length == 7 ? new RgbaColor(255, (byte)(raw >> 16), (byte)(raw >> 8), (byte)raw)
            : new RgbaColor((byte)(raw >> 24), (byte)(raw >> 16), (byte)(raw >> 8), (byte)raw);
        return true;
    }

    private static double Contrast(RgbaColor a, RgbaColor b)
    {
        var l1 = Luma(a); var l2 = Luma(b);
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    private static double Luma(RgbaColor c)
    {
        static double Ch(byte v) { var n = v / 255.0; return n <= 0.03928 ? n / 12.92 : Math.Pow((n + 0.055) / 1.055, 2.4); }
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
    }
}

public readonly record struct RgbaColor(byte A, byte R, byte G, byte B);

