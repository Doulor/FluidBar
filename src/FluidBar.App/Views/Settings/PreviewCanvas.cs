using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FluidBar.App.Controls;
using FluidBar.Sdk.Canvas;

namespace FluidBar.App.Views.Settings;

/// <summary>Settings preview surface that renders the same validated command records as plugin pages.</summary>
public sealed class PreviewCanvas : Control
{
    private PreviewCanvasData? _data;
    private readonly Stack<IDisposable> _scopes = new();

    public PreviewCanvas()
    {
        IsHitTestVisible = false;
    }

    public void SetData(PreviewCanvasData? data)
    {
        _data = data;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = _data?.Width ?? 400;
        var height = _data?.Height ?? 160;
        return new Size(Math.Min(width, availableSize.Width), height);
    }

    public override void Render(DrawingContext context)
    {
        if (_data is null)
        {
            return;
        }

        var width = _data.Width;
        var height = _data.Height;
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#0B0B10")), null, new Rect(0, 0, width, height), 12, 12);
        foreach (var command in _data.Commands)
        {
            var rect = new Rect(command.X, command.Y, command.Width, command.Height);
            var brush = new SolidColorBrush(ParseColor(command.Color));
            switch (command.Kind)
            {
                case CanvasCommandKind.FillRect:
                    context.DrawRectangle(brush, null, rect);
                    break;
                case CanvasCommandKind.FillRoundRect:
                    context.DrawRectangle(brush, null, rect, command.CornerRadius, command.CornerRadius);
                    break;
                case CanvasCommandKind.StrokeRoundRect:
                    context.DrawRectangle(null, new Pen(brush, command.StrokeWidth), rect,
                        command.CornerRadius, command.CornerRadius);
                    break;
                case CanvasCommandKind.DrawText when !string.IsNullOrEmpty(command.Text):
                {
                    var text = new FormattedText(command.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                        Typeface.Default, command.TextStyle == CanvasTextStyle.Title ? 16 : 11, brush)
                    {
                        TextAlignment = command.TextAlign switch
                        {
                            CanvasTextAlign.Center => TextAlignment.Center,
                            CanvasTextAlign.Right => TextAlignment.Right,
                            _ => TextAlignment.Left,
                        },
                        MaxTextWidth = Math.Max(0, command.Width),
                        MaxTextHeight = Math.Max(0, command.Height),
                    };
                    context.DrawText(text, new Point(command.X, command.Y));
                    break;
                }
                case CanvasCommandKind.DrawIcon:
                    Icons.Draw(context, IconFor(command.IconToken), rect, brush);
                    break;
                case CanvasCommandKind.DrawProgress:
                {
                    var progress = Math.Clamp(command.Progress ?? 0, 0, 1);
                    context.DrawRectangle(new SolidColorBrush(Color.Parse("#33333D")), null, rect,
                        command.Height / 2, command.Height / 2);
                    if (progress > 0)
                    {
                        context.DrawRectangle(brush, null,
                            new Rect(command.X, command.Y, command.Width * progress, command.Height),
                            command.Height / 2, command.Height / 2);
                    }

                    break;
                }
                case CanvasCommandKind.PushClip:
                    _scopes.Push(context.PushClip(rect));
                    break;
                case CanvasCommandKind.PushOpacity:
                    _scopes.Push(context.PushOpacity(Math.Clamp(command.Opacity, 0, 1)));
                    break;
                case CanvasCommandKind.PopClip:
                case CanvasCommandKind.PopOpacity:
                    if (_scopes.Count > 0)
                    {
                        _scopes.Pop().Dispose();
                    }

                    break;
            }
        }

        while (_scopes.Count > 0)
        {
            _scopes.Pop().Dispose();
        }
    }

    private static IconKind IconFor(string? token) => token?.ToLowerInvariant() switch
    {
        "agent" => IconKind.Agent,
        "check" or "success" => IconKind.Check,
        "bell" => IconKind.Bell,
        "media" => IconKind.Media,
        "clipboard" => IconKind.Clipboard,
        "clock" => IconKind.Clock,
        "download" => IconKind.Download,
        "search" => IconKind.Search,
        "lock" => IconKind.Lock,
        "usb" => IconKind.Usb,
        "bluetooth" => IconKind.Bluetooth,
        "home" => IconKind.Home,
        "volume" => IconKind.VolumeHigh,
        "brightness" => IconKind.BrightnessHigh,
        "battery" => IconKind.Battery,
        "timer" => IconKind.Clock,
        "calendar" => IconKind.Calendar,
        "grid" => IconKind.Grid,
        "settings" => IconKind.Settings,
        "play" => IconKind.Play,
        "pause" => IconKind.Pause,
        "next" => IconKind.Next,
        "previous" => IconKind.Previous,
        _ => IconKind.Bell,
    };

    private static Color ParseColor(string? value)
        => value is not null && value.StartsWith('#') && Color.TryParse(value, out var parsed)
            ? parsed
            : Colors.White;
}

