using FluidBar.App.Plugins;
using FluidBar.Sdk;
using FluidBar.Sdk.Canvas;

namespace FluidBar.Ui.Tests;

/// <summary>M3 compact-canvas validation/fallback at the plugin boundary.</summary>
public sealed class CompactCanvasTests
{
    [Fact]
    public void ValidCompactView_IsMappedToHostCanvas()
    {
        var recorder = new CanvasRecorder();
        recorder.FillRoundRect(0, 0, 236, 36, 18, "#101018");
        recorder.DrawText("72%", 160, 6, 64, 24, "accent", CanvasTextStyle.Title, CanvasTextAlign.Right);
        var payload = PluginIslandEventAdapter.MapPayload(new IslandEvent
        {
            Source = "sample-canvas",
            Title = "Canvas ring",
            Kind = IslandEventKind.Status,
            Payload = new IslandEventPayload
            {
                CompactView = new CompactViewDescriptor
                {
                    Width = 236,
                    Height = 36,
                    BackgroundColor = "#101018",
                    ForegroundColor = "#FFFFFF",
                    Commands = recorder.Build(),
                },
            },
        }, FluidBar.Core.Activities.ActivityKind.Status);

        Assert.NotNull(payload.CompactView);
        Assert.Equal(236, payload.CompactView!.Width);
        Assert.Equal(36, payload.CompactView.Height);
        Assert.Equal(2, payload.CompactView.Commands.Count);
    }

    [Fact]
    public void InvalidCompactView_FallsBackToGenericTemplate()
    {
        var recorder = new CanvasRecorder();
        recorder.FillRect(230, 0, 40, 20, "#FFFFFF");
        var payload = PluginIslandEventAdapter.MapPayload(new IslandEvent
        {
            Source = "sample-canvas",
            Title = "Canvas ring",
            Kind = IslandEventKind.Status,
            Payload = new IslandEventPayload
            {
                CompactView = new CompactViewDescriptor
                {
                    Width = 236,
                    Height = 36,
                    BackgroundColor = "#101018",
                    ForegroundColor = "#FFFFFF",
                    Commands = recorder.Build(),
                },
            },
        }, FluidBar.Core.Activities.ActivityKind.Status);

        Assert.Null(payload.CompactView);
    }

    [Fact]
    public void CompactView_RejectsLowContrastAndOversize()
    {
        var descriptor = new CompactViewDescriptor
        {
            Width = 236,
            Height = 36,
            BackgroundColor = "#111111",
            ForegroundColor = "#222222",
            Commands = new CanvasRecorder().Build(),
        };
        Assert.False(IslandCanvasValidator.ValidateCompact(descriptor, out var contrastError));
        Assert.Contains("contrast", contrastError, StringComparison.OrdinalIgnoreCase);
        Assert.False(IslandCanvasValidator.ValidateCompact(descriptor with { Width = 431 }, out var sizeError));
        Assert.Contains("bounds", sizeError, StringComparison.OrdinalIgnoreCase);
    }
}

