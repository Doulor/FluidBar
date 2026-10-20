using FluidBar.Sdk;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Pages;

namespace FluidBar.Sample.LocalPages;

/// <summary>
/// M0 local-channel sample: registers one host-rendered list page after the six built-in pages and
/// exposes a manifest-declared command from its row's trailing button.
/// </summary>
public sealed class SamplePagesPlugin : IFluidBarPlugin
{
    private const string PageId = "fluidbar-sample-local-pages.metrics";
    private const string RefreshCommand = "fluidbar-sample-local-pages.refresh";

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

        var registered = context.Islands.RegisterPage(new PageDescriptor
        {
            PageId = PageId,
            Order = 55,
            Title = new LocalizedString("Sample pages"),
            ShortTitle = new LocalizedString("Sample"),
            IconToken = "list",
            TemplateId = PageTemplates.List,
        });

        if (!registered)
        {
            context.Log.Warning("SamplePages page registration was rejected.");
        }

        Publish(context);
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        _command?.Dispose();
        _command = null;
        _context?.Islands.UnregisterPage(PageId);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => StopAsync(CancellationToken.None);

    private void Publish(IPluginContext context)
    {
        var data = new PageData
        {
            PageId = PageId,
            Revision = _counter + 1,
            TemplateId = PageTemplates.List,
            BadgeCount = _counter > 0 ? _counter : null,
            List = new PageListData
            {
                EmptyTitle = "No sample rows",
                EmptyHint = "The local sample page has no data",
                Rows =
                [
                    new PageListRow
                    {
                        RowId = "counter",
                        Title = $"Local plugin page #{_counter}",
                        Subtitle = $"Registered through the same SDK page API · refresh #{_counter}",
                        Value = $"refresh #{_counter}",
                        RowCommandId = RefreshCommand,
                        TrailingCommandId = RefreshCommand,
                        TrailingLabel = "Refresh",
                    },
                    new PageListRow
                    {
                        RowId = "static",
                        Title = "Island body / rail / UIA",
                        Subtitle = "Seven pages: six built-ins plus this local plugin page",
                    },
                ],
            },
        };

        if (!context.Islands.PublishPage(data))
        {
            context.Log.Warning("SamplePages page data was rejected.");
        }
    }
}
