using FluidBar.Sdk;
using FluidBar.Sdk.Canvas;
using FluidBar.Sdk.Contributions;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Pages;

namespace FluidBar.Plugins.SampleCanvas;

/// <summary>Reference plugin for the M3 command-canvas API: a ring progress + text + semantic hit areas.</summary>
public sealed class CanvasPlugin : IFluidBarPlugin
{
    private const string PageId = "sample-canvas.ring";
    private const string HitCommand = "sample-canvas.hit";
    private IPluginContext? _context;
    private IDisposable? _hitRegistration;
    private int _hits;
    private double _progress = 0.72;

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        _context = context;
        return ValueTask.CompletedTask;
    }

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        var context = _context ?? throw new InvalidOperationException("InitializeAsync was not called.");
        _hitRegistration = context.Commands.Register(HitCommand, (_, _) =>
        {
            _hits++;
            _progress = _progress >= 0.92 ? 0.72 : Math.Min(1, _progress + 0.10);
            context.Log.Info($"SampleCanvas hit: {_hits}, progress {_progress:0.00}");
            PublishPage();
            PublishCompact();
            return ValueTask.CompletedTask;
        });

        if (!context.Islands.RegisterPage(new PageDescriptor
        {
            PageId = PageId,
            Order = 45,
            Title = new LocalizedString("Canvas ring"),
            ShortTitle = new LocalizedString("Canvas"),
            IconToken = "agent",
            TemplateId = PageTemplates.Custom,
        }))
        {
            context.Log.Warning("SampleCanvas could not register its custom page.");
        }

        PublishPage();
        PublishCompact();
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        _hitRegistration?.Dispose();
        _hitRegistration = null;
        _context?.Islands.UnregisterPage(PageId);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => StopAsync(CancellationToken.None);

    private void PublishPage()
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        var canvas = new CanvasRecorder();
        canvas.FillRoundRect(12, 12, 376, 208, 18, "#14141B");
        canvas.FillRoundRect(36, 36, 200, 160, 14, "#101019");
        DrawRing(canvas, 136, 116, 72);
        canvas.DrawText("RING", 262, 62, 100, 24, "foregroundTertiary", CanvasTextStyle.Caption);
        canvas.DrawText($"{(int)Math.Round(_progress * 100)}%", 250, 88, 120, 44, "accent", CanvasTextStyle.Title);
        canvas.DrawText("command canvas", 250, 136, 130, 20, "foregroundSecondary", CanvasTextStyle.Caption);
        canvas.DrawText($"Hits: {_hits}", 250, 162, 120, 20, "success", CanvasTextStyle.Mono);

        context.Islands.PublishPage(new PageData
        {
            PageId = PageId,
            Revision = _hits + 1,
            TemplateId = PageTemplates.Custom,
            Canvas = new PageCanvasData
            {
                Commands = canvas.Build(),
                AccessibleName = "Canvas ring progress",
                HitAreas =
                [
                    new PageHitArea
                    {
                        Id = "ring-button",
                        Kind = PageHitAreaKind.Button,
                        X = 60, Y = 40, Width = 152, Height = 152,
                        CommandId = HitCommand,
                        AccessibleName = $"Increase ring progress (hits {_hits})",
                        LongPress = true,
                        DoubleClick = true,
                    },
                    new PageHitArea
                    {
                        Id = "ring-slider",
                        Kind = PageHitAreaKind.Slider,
                        X = 248, Y = 188, Width = 116, Height = 16,
                        CommandId = HitCommand,
                        AccessibleName = "Ring progress value",
                        Min = 0, Max = 100, Value = _progress * 100,
                    },
                ],
            },
        });
    }

    private void PublishCompact()
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        var canvas = new CanvasRecorder();
        canvas.FillRoundRect(0, 0, 236, 36, 18, "#14141B");
        DrawCompactRing(canvas, 18, 18, 12);
        canvas.DrawText("RING", 38, 6, 90, 22, "foreground", CanvasTextStyle.Title);
        canvas.DrawProgress(38, 27, 100, 3, _progress, "accent");
        canvas.DrawText($"{_progress * 100:0}%", 150, 6, 74, 22, "accent", CanvasTextStyle.Title, CanvasTextAlign.Right);
        context.Islands.PublishActivity(new IslandActivity
        {
            Source = "sample-canvas",
            Title = "Canvas ring",
            Kind = IslandActivityKind.Status,
            Subtitle = "command canvas",
            IconToken = "agent",
            CompactView = new CompactViewDescriptor
            {
                Width = 236,
                Height = 36,
                BackgroundColor = "#14141B",
                ForegroundColor = "#FFFFFF",
                AccessibleName = "Canvas ring compact",
                Commands = canvas.Build(),
            },
        });
    }

    private void DrawCompactRing(IIslandCanvas canvas, double cx, double cy, double radius)
    {
        const int segments = 20;
        for (var i = 0; i < segments; i++)
        {
            var angle = (Math.PI * 2 * i / segments) - (Math.PI / 2);
            var x = cx + (Math.Cos(angle) * radius);
            var y = cy + (Math.Sin(angle) * radius);
            canvas.FillRoundRect(x - 2, y - 2, 4, 4, 2, (double)i / segments <= _progress ? "accent" : "track");
        }
    }

    private void DrawRing(IIslandCanvas canvas, double cx, double cy, double radius)
    {
        const int segments = 40;
        for (var i = 0; i < segments; i++)
        {
            var angle = (Math.PI * 2 * i / segments) - (Math.PI / 2);
            var x = cx + (Math.Cos(angle) * radius);
            var y = cy + (Math.Sin(angle) * radius);
            var active = (double)i / segments <= _progress;
            var color = active ? "accent" : "track";
            canvas.FillRoundRect(x - 3, y - 3, 6, 6, 3, color);
        }

        canvas.FillRoundRect(cx - 42, cy - 42, 84, 84, 42, "#1E1E29");
        canvas.DrawText($"{_progress * 100:0}%", cx - 40, cy - 16, 80, 32, "foreground",
            CanvasTextStyle.Title, CanvasTextAlign.Center);
    }

}

