using FluidBar.Sdk;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Pages;

namespace FluidBar.Sample.LocalPages;

/// <summary>
/// M1 local-channel sample: registers four host-rendered list pages (the per-plugin cap) after the six
/// built-in pages, so the rail exercises overflow and the page chooser.
/// </summary>
public sealed class SamplePagesPlugin : IFluidBarPlugin
{
    private const string RefreshCommand = "fluidbar-sample-local-pages.refresh";
    private static readonly string[] PageIds =
    [
        "fluidbar-sample-local-pages.metrics",
        "fluidbar-sample-local-pages.second",
        "fluidbar-sample-local-pages.third",
        "fluidbar-sample-local-pages.fourth",
    ];

    private IPluginContext? _context;
    private IDisposable? _command;
    private int _counter;

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        _context = context;
        return ValueTask.CompletedTask;
    }

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        var context = _context ?? throw new InvalidOperationException("InitializeAsync was not called.");
        _command = context.Commands.Register(RefreshCommand, (_, _) =>
        {
            _counter++;
            Publish(context);
            context.Log.Info($"SamplePages refresh #{_counter}");
            return ValueTask.CompletedTask;
        });

        for (var i = 0; i < PageIds.Length; i++)
        {
            var registered = context.Islands.RegisterPage(new PageDescriptor
            {
                PageId = PageIds[i],
                Order = 55 + i,
                Title = new LocalizedString(i == 0 ? "Sample pages" : $"Sample page {i + 1}"),
                ShortTitle = new LocalizedString(i == 0 ? "Sample" : $"Sample{i + 1}"),
                IconToken = "list",
                TemplateId = PageTemplates.List,
            });

            if (!registered)
            {
                context.Log.Warning($"SamplePages page {PageIds[i]} registration was rejected.");
            }
        }

        Publish(context);
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        _command?.Dispose();
        _command = null;
        foreach (var pageId in PageIds)
        {
            _context?.Islands.UnregisterPage(pageId);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => StopAsync(CancellationToken.None);

    private void Publish(IPluginContext context)
    {
        for (var i = 0; i < PageIds.Length; i++)
        {
            var data = new PageData
            {
                PageId = PageIds[i],
                Revision = _counter + 1,
                TemplateId = PageTemplates.List,
                BadgeCount = i == 0 && _counter > 0 ? _counter : null,
                List = new PageListData
                {
                    EmptyTitle = "No sample rows",
                    EmptyHint = "The local sample page has no data",
                    Rows =
                    [
                        new PageListRow
                        {
                            RowId = "counter",
                            Title = i == 0 ? $"Local plugin page #{_counter}" : $"Sample page #{i + 1}",
                            Subtitle = $"Registered through the same SDK page API · refresh #{_counter}",
                            RowCommandId = RefreshCommand,
                            TrailingCommandId = RefreshCommand,
                            TrailingLabel = "Refresh",
                        },
                        new PageListRow
                        {
                            RowId = "static",
                            Title = "Island body / rail / UIA",
                            Subtitle = "Twelve pages: six built-ins plus six local plugin pages",
                        },
                    ],
                },
            };

            if (!context.Islands.PublishPage(data))
            {
                context.Log.Warning($"SamplePages page data {PageIds[i]} was rejected.");
            }
        }
    }
}
