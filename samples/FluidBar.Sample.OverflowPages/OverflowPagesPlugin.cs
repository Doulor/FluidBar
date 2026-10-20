using FluidBar.Sdk;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Pages;

namespace FluidBar.Sample.OverflowPages;

/// <summary>Second local fixture plugin: adds the final two pages so the rail has 12 pages and overflow.</summary>
public sealed class OverflowPagesPlugin : IFluidBarPlugin
{
    private static readonly string[] PageIds =
    [
        "fluidbar-sample-overflow-pages.one",
        "fluidbar-sample-overflow-pages.two",
    ];

    private IPluginContext? _context;

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        _context = context;
        return ValueTask.CompletedTask;
    }

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        var context = _context ?? throw new InvalidOperationException("InitializeAsync was not called.");
        for (var i = 0; i < PageIds.Length; i++)
        {
            context.Islands.RegisterPage(new PageDescriptor
            {
                PageId = PageIds[i],
                Order = 60 + i,
                Title = new LocalizedString($"Overflow page {i + 1}"),
                ShortTitle = new LocalizedString($"More{i + 1}"),
                IconToken = "list",
                TemplateId = PageTemplates.List,
            });
            context.Islands.PublishPage(new PageData
            {
                PageId = PageIds[i],
                Revision = 1,
                TemplateId = PageTemplates.List,
                List = new PageListData
                {
                    Rows =
                    [
                        new PageListRow
                        {
                            RowId = "only",
                            Title = $"Overflow page {i + 1}",
                            Subtitle = "Twelve-page rail overflow fixture",
                        },
                    ],
                },
            });
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        foreach (var pageId in PageIds)
        {
            _context?.Islands.UnregisterPage(pageId);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => StopAsync(CancellationToken.None);
}
