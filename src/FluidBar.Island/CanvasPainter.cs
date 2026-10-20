using System;
using System.Globalization;
using FluidBar.Core.Presentation;
using SkiaSharp;

namespace FluidBar.Island;

/// <summary>Host-owned painter for plugin command canvases. No plugin code runs here.</summary>
internal static class CanvasPainter
{
    public static void DrawPage(SKCanvas canvas, IslandCanvasView view, SKRect bounds, IslandTheme theme,
        ScaledText text, float opacity)
        => DrawCommands(canvas, view.Commands ?? [], bounds, theme, text, opacity, compact: false);

    public static void DrawCompact(SKCanvas canvas, IslandCompactCanvas view, SKRect bounds, IslandTheme theme,
        ScaledText text, float opacity)
    {
        if (!string.IsNullOrWhiteSpace(view.BackgroundColor))
        {
            using var background = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            background.Color = ResolveColor(view.BackgroundColor, theme, opacity);
            canvas.DrawRoundRect(bounds, bounds.Height / 2, bounds.Height / 2, background);
        }

        DrawCommands(canvas, view.Commands ?? [], bounds, theme, text, opacity, compact: true);
    }

    private static void DrawCommands(SKCanvas canvas, IReadOnlyList<IslandCanvasCommand> commands, SKRect bounds,
        IslandTheme theme, ScaledText text, float opacity, bool compact)
    {
        if (commands.Count == 0)
        {
            return;
        }

        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };
        using var iconPaint = new SKPaint { IsAntialias = true };
        canvas.Save();
        canvas.ClipRect(bounds);
        var saveDepth = 0;
        foreach (var command in commands)
        {
            var x = bounds.Left + (float)command.X;
            var y = bounds.Top + (float)command.Y;
            var w = (float)command.Width;
            var h = (float)command.Height;
            var alpha = (float)(command.Opacity * opacity);
            var color = ResolveColor(command.Color, theme, alpha);
            switch (command.Kind)
            {
                case IslandCanvasCommandKind.FillRect:
                    fill.Style = SKPaintStyle.Fill;
                    fill.Color = color;
                    canvas.DrawRect(new SKRect(x, y, x + w, y + h), fill);
                    break;
                case IslandCanvasCommandKind.FillRoundRect:
                    fill.Style = SKPaintStyle.Fill;
                    fill.Color = color;
                    canvas.DrawRoundRect(new SKRect(x, y, x + w, y + h), (float)command.CornerRadius,
                        (float)command.CornerRadius, fill);
                    break;
                case IslandCanvasCommandKind.StrokeRoundRect:
                    stroke.Style = SKPaintStyle.Stroke;
                    stroke.StrokeWidth = Math.Max(0.5f, (float)command.StrokeWidth);
                    stroke.Color = color;
                    canvas.DrawRoundRect(new SKRect(x, y, x + w, y + h), (float)command.CornerRadius,
                        (float)command.CornerRadius, stroke);
                    break;
                case IslandCanvasCommandKind.DrawText:
                    if (!string.IsNullOrEmpty(command.Text))
                    {
                        text.Draw(canvas, command.Text, TypeFor(command.TextStyle, compact),
                            new SKRect(x, y, x + w, y + h), color,
                            command.TextAlign switch
                            {
                                IslandCanvasTextAlign.Center => TextAlign.Center,
                                IslandCanvasTextAlign.Right => TextAlign.Right,
                                _ => TextAlign.Left,
                            }, alpha);
                    }

                    break;
                case IslandCanvasCommandKind.DrawIcon:
                    IconSet.Draw(canvas, IconFor(command.IconToken), new SKRect(x, y, x + w, y + h), color, iconPaint);
                    break;
                case IslandCanvasCommandKind.DrawProgress:
                {
                    var progress = (float)Math.Clamp(command.Progress ?? 0, 0, 1);
                    fill.Style = SKPaintStyle.Fill;
                    fill.Color = ResolveColor("track", theme, alpha * 0.55f);
                    canvas.DrawRoundRect(new SKRect(x, y, x + w, y + h), h / 2, h / 2, fill);
                    if (progress > 0)
                    {
                        fill.Color = color;
                        canvas.DrawRoundRect(new SKRect(x, y, x + (w * progress), y + h), h / 2, h / 2, fill);
                    }

                    break;
                }
                case IslandCanvasCommandKind.PushClip:
                    canvas.Save();
                    canvas.ClipRect(new SKRect(x, y, x + w, y + h));
                    saveDepth++;
                    break;
                case IslandCanvasCommandKind.PopClip:
                    if (saveDepth > 0)
                    {
                        canvas.Restore();
                        saveDepth--;
                    }

                    break;
                case IslandCanvasCommandKind.PushOpacity:
                {
                    canvas.SaveLayer(new SKPaint
                    {
                        Color = new SKColor(255, 255, 255, (byte)Math.Clamp(command.Opacity * opacity * 255, 0, 255)),
                    });
                    saveDepth++;
                    break;
                }
                case IslandCanvasCommandKind.PopOpacity:
                    if (saveDepth > 0)
                    {
                        canvas.Restore();
                        saveDepth--;
                    }

                    break;
            }
        }

        while (saveDepth-- > 0)
        {
            canvas.Restore();
        }

        canvas.Restore();
    }

    private static TextStyle TypeFor(IslandCanvasTextStyle style, bool compact)
        => style switch
        {
            IslandCanvasTextStyle.Title => compact ? IslandType.CompactTitle : IslandType.RowTitle,
            IslandCanvasTextStyle.Caption => compact ? IslandType.CompactCaption : IslandType.RowMeta,
            IslandCanvasTextStyle.Mono => compact ? IslandType.CompactCaption : IslandType.RowMono,
            _ => compact ? IslandType.CompactCaption : IslandType.RowBody,
        };

    private static SKColor ResolveColor(string? value, IslandTheme theme, float opacity)
    {
        var color = value switch
        {
            "accent" => theme.Accent,
            "foreground" => theme.Foreground,
            "foregroundSecondary" => theme.ForegroundSecondary,
            "foregroundTertiary" => theme.ForegroundTertiary,
            "success" => theme.Success,
            "warning" => theme.Warning,
            "danger" => theme.Danger,
            "info" => theme.Info,
            "track" => theme.LineStrong,
            "transparent" => SKColors.Transparent,
            _ => ParseHex(value) ?? theme.Foreground,
        };
        return color.WithAlpha((byte)Math.Clamp(color.Alpha * opacity, 0, 255));
    }

    private static SKColor? ParseHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value[0] != '#' || value.Length is not (7 or 9)
            || !uint.TryParse(value[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var raw))
        {
            return null;
        }

        return value.Length == 7
            ? new SKColor((byte)(raw >> 16), (byte)(raw >> 8), (byte)raw)
            : new SKColor((byte)(raw >> 16), (byte)(raw >> 8), (byte)raw, (byte)(raw >> 24));
    }

    private static IconKind IconFor(string? token) => token?.ToLowerInvariant() switch
    {
        "agent" => IconKind.Agent,
        "check" or "success" => IconKind.Check,
        "alert" or "danger" or "warning" => IconKind.Alert,
        "pause" => IconKind.Pause,
        "bell" => IconKind.Bell,
        "media" => IconKind.Media,
        "clipboard" => IconKind.Clipboard,
        "clock" => IconKind.Clock,
        "download" or "progress" => IconKind.Download,
        "search" => IconKind.Search,
        "lock" => IconKind.Lock,
        "usb" => IconKind.Usb,
        "bluetooth" => IconKind.Bluetooth,
        "home" => IconKind.Home,
        "volume" => IconKind.Volume,
        "brightness" => IconKind.Brightness,
        "battery" => IconKind.Battery,
        "timer" => IconKind.Timer,
        "calendar" => IconKind.Calendar,
        "grid" => IconKind.Grid,
        "settings" => IconKind.Settings,
        "close" => IconKind.Close,
        "play" => IconKind.Play,
        "next" => IconKind.Next,
        "previous" => IconKind.Previous,
        _ => IconKind.Check,
    };
}

