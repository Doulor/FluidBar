using FluidBar.Core.Presentation;
using SdkCanvas = FluidBar.Sdk.Canvas;

namespace FluidBar.App.Hosting.Pages;

/// <summary>Converts the SDK's command records into the host's rendering model. This is the only
/// boundary where plugin canvas data crosses into Core/Island types.</summary>
public static class IslandCanvasAdapter
{
    public static IslandCanvasView ToHost(SdkCanvas.PageCanvasData data)
        => new()
        {
            AccessibleName = data.AccessibleName,
            Commands = (data.Commands ?? []).Select(ToHost).ToArray(),
            HitAreas = (data.HitAreas ?? []).Select(ToHost).ToArray(),
        };

    public static IslandCompactCanvas ToHost(SdkCanvas.CompactViewDescriptor view)
        => new()
        {
            Width = view.Width,
            Height = view.Height,
            AccessibleName = view.AccessibleName,
            BackgroundColor = view.BackgroundColor,
            ForegroundColor = view.ForegroundColor,
            Commands = (view.Commands ?? []).Select(ToHost).ToArray(),
        };

    public static IslandCanvasCommand ToHost(SdkCanvas.CanvasCommand command)
        => new()
        {
            Kind = command.Kind switch
            {
                SdkCanvas.CanvasCommandKind.FillRect => IslandCanvasCommandKind.FillRect,
                SdkCanvas.CanvasCommandKind.FillRoundRect => IslandCanvasCommandKind.FillRoundRect,
                SdkCanvas.CanvasCommandKind.StrokeRoundRect => IslandCanvasCommandKind.StrokeRoundRect,
                SdkCanvas.CanvasCommandKind.DrawText => IslandCanvasCommandKind.DrawText,
                SdkCanvas.CanvasCommandKind.DrawIcon => IslandCanvasCommandKind.DrawIcon,
                SdkCanvas.CanvasCommandKind.DrawProgress => IslandCanvasCommandKind.DrawProgress,
                SdkCanvas.CanvasCommandKind.PushClip => IslandCanvasCommandKind.PushClip,
                SdkCanvas.CanvasCommandKind.PopClip => IslandCanvasCommandKind.PopClip,
                SdkCanvas.CanvasCommandKind.PushOpacity => IslandCanvasCommandKind.PushOpacity,
                SdkCanvas.CanvasCommandKind.PopOpacity => IslandCanvasCommandKind.PopOpacity,
                _ => throw new ArgumentOutOfRangeException(nameof(command)),
            },
            X = command.X, Y = command.Y, Width = command.Width, Height = command.Height,
            CornerRadius = command.CornerRadius, StrokeWidth = command.StrokeWidth, Opacity = command.Opacity,
            Text = command.Text, IconToken = command.IconToken, Color = command.Color, Progress = command.Progress,
            TextStyle = command.TextStyle switch
            {
                SdkCanvas.CanvasTextStyle.Title => IslandCanvasTextStyle.Title,
                SdkCanvas.CanvasTextStyle.Body => IslandCanvasTextStyle.Body,
                SdkCanvas.CanvasTextStyle.Caption => IslandCanvasTextStyle.Caption,
                SdkCanvas.CanvasTextStyle.Mono => IslandCanvasTextStyle.Mono,
                _ => IslandCanvasTextStyle.Body,
            },
            TextAlign = command.TextAlign switch
            {
                SdkCanvas.CanvasTextAlign.Left => IslandCanvasTextAlign.Left,
                SdkCanvas.CanvasTextAlign.Center => IslandCanvasTextAlign.Center,
                SdkCanvas.CanvasTextAlign.Right => IslandCanvasTextAlign.Right,
                _ => IslandCanvasTextAlign.Left,
            },
        };

    public static IslandHitArea ToHost(SdkCanvas.PageHitArea area)
        => new()
        {
            Id = area.Id,
            Kind = area.Kind switch
            {
                SdkCanvas.PageHitAreaKind.Button => IslandHitAreaKind.Button,
                SdkCanvas.PageHitAreaKind.Row => IslandHitAreaKind.Row,
                SdkCanvas.PageHitAreaKind.Slider => IslandHitAreaKind.Slider,
                SdkCanvas.PageHitAreaKind.Scroll => IslandHitAreaKind.Scroll,
                _ => IslandHitAreaKind.Button,
            },
            X = area.X, Y = area.Y, Width = area.Width, Height = area.Height,
            CommandId = area.CommandId, AccessibleName = area.AccessibleName,
            LongPress = area.LongPress, DoubleClick = area.DoubleClick,
            Min = area.Min, Max = area.Max, Value = area.Value,
        };
}

