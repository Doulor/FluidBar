using FluidBar.Sdk.Canvas;

namespace FluidBar.Sdk.Tests;

/// <summary>M3 command-canvas hard limits and semantic hit validation.</summary>
public sealed class CanvasTests
{
    [Fact]
    public void Recorder_ProducesCommandsAndValidates()
    {
        var canvas = new CanvasRecorder();
        canvas.FillRect(0, 0, 100, 40, "#101018");
        canvas.DrawText("ring", 10, 8, 80, 20, "foreground", CanvasTextStyle.Title, CanvasTextAlign.Center);
        canvas.DrawIcon("agent", 2, 2, 14, "accent");
        canvas.DrawProgress(10, 30, 80, 4, 0.5, "accent");
        canvas.PushClip(0, 0, 100, 40);
        canvas.PopClip();
        canvas.PushOpacity(0.5);
        canvas.PopOpacity();

        Assert.True(IslandCanvasValidator.Validate(canvas.Build(), 100, 40, out var error), error);
    }

    [Fact]
    public void Validator_RejectsOverLimitTextAndOutOfBounds()
    {
        var outOfBounds = new CanvasRecorder();
        outOfBounds.FillRect(90, 0, 20, 20, "#FFFFFF");
        Assert.False(IslandCanvasValidator.Validate(outOfBounds.Build(), 100, 40, out var boundsError));
        Assert.Contains("outside", boundsError, StringComparison.OrdinalIgnoreCase);

        var tooLong = new CanvasCommand
        {
            Kind = CanvasCommandKind.DrawText,
            Text = new string('x', CanvasLimits.MaxTextCharacters + 1),
            X = 0, Y = 0, Width = 100, Height = 20, Color = "foreground",
        };
        Assert.False(IslandCanvasValidator.Validate([tooLong], 100, 40, out var textError));

        Assert.Throws<InvalidOperationException>(() =>
        {
            var recorder = new CanvasRecorder();
            for (var i = 0; i < CanvasLimits.MaxCommands + 1; i++)
            {
                recorder.FillRect(0, 0, 1, 1, "#FFFFFF");
            }
        });
    }

    [Fact]
    public void Validator_RejectsUnknownIconAndUnbalancedClip()
    {
        var icon = new CanvasRecorder();
        icon.DrawIcon("../../image.png", 0, 0, 10, "foreground");
        Assert.False(IslandCanvasValidator.Validate(icon.Build(), 100, 40, out var iconError));
        Assert.Contains("icon", iconError, StringComparison.OrdinalIgnoreCase);

        var clip = new CanvasRecorder();
        clip.PushClip(0, 0, 10, 10);
        Assert.False(IslandCanvasValidator.Validate(clip.Build(), 100, 40, out var clipError));
        Assert.Contains("clip", clipError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compact_RejectsOutOfBoundsLowContrastAndAcceptsValid()
    {
        var valid = new CompactViewDescriptor
        {
            Width = 236,
            Height = 36,
            BackgroundColor = "#101018",
            ForegroundColor = "#FFFFFF",
            Commands = new CanvasRecorder().Build(),
        };
        Assert.True(IslandCanvasValidator.ValidateCompact(valid, out var validError), validError);

        var tooWide = valid with { Width = 500 };
        Assert.False(IslandCanvasValidator.ValidateCompact(tooWide, out var sizeError));
        Assert.Contains("bounds", sizeError, StringComparison.OrdinalIgnoreCase);

        var lowContrast = valid with { BackgroundColor = "#111111", ForegroundColor = "#222222" };
        Assert.False(IslandCanvasValidator.ValidateCompact(lowContrast, out var contrastError));
        Assert.Contains("contrast", contrastError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HitAreas_RequireSemanticBoundsAndSliderRange()
    {
        var valid = new[]
        {
            new PageHitArea
            {
                Id = "button", Kind = PageHitAreaKind.Button, X = 0, Y = 0, Width = 40, Height = 20,
                CommandId = "sample.hit", AccessibleName = "Hit it",
            },
            new PageHitArea
            {
                Id = "slider", Kind = PageHitAreaKind.Slider, X = 0, Y = 30, Width = 100, Height = 10,
                CommandId = "sample.slide", AccessibleName = "Value", Min = 0, Max = 100,
            },
        };
        Assert.True(IslandCanvasValidator.ValidateHitAreas(valid, 400, 232, out var error), error);
        Assert.True(IslandCanvasValidator.ValidateHitAreas([], 400, 232, out _));
    }
}

