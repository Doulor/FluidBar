using FluidBar.App.Hosting.Pages;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Pages;

namespace FluidBar.Ui.Tests;

/// <summary>M0 page registry contract: owner-scoped PageIds, host list data, removal and the 12-page cap.</summary>
public sealed class PageRegistryTests
{
    private const string Owner = "sample-page";

    [Fact]
    public void RegisterPage_AppendsOnePageAfterBuiltins()
    {
        var registry = new PageRegistry();
        var changed = 0;
        registry.Changed += (_, _) => changed++;

        var ok = registry.RegisterPage(Owner, Descriptor("sample-page.metrics"), out var error);

        Assert.True(ok, error);
        Assert.Equal(PageRegistry.BuiltinDescriptors.Count + 1, registry.Count);
        var page = Assert.Single(registry.BuildPluginPages());
        Assert.Equal("sample-page.metrics", page.PageId);
        Assert.Equal(Owner, page.OwnerPluginId);
        Assert.Equal("Sample page", page.Title);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void RegisterPage_RejectsForeignPageId()
    {
        var registry = new PageRegistry();

        Assert.False(registry.RegisterPage(Owner, Descriptor("other-plugin.metrics"), out var error));
        Assert.Contains("prefixed", error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PageRegistry.BuiltinDescriptors.Count, registry.Count);
    }

    [Fact]
    public void RegisterPage_AllowsBuiltinNamespaceOnlyForTrustedBuiltinOwner()
    {
        var registry = new PageRegistry();

        Assert.True(registry.RegisterPage("builtin", Descriptor("builtin.agent"), "builtin", out var builtinError),
            builtinError);
        Assert.Contains(registry.BuildPluginPages(), page => page.PageId == "builtin.agent");

        Assert.False(registry.RegisterPage(Owner, Descriptor("builtin.evil"), "builtin", out var foreignError));
        Assert.Contains("reserved", foreignError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RegisterPage_RejectsUnknownTemplateAndDuplicate()
    {
        var registry = new PageRegistry();
        Assert.True(registry.RegisterPage(Owner, Descriptor("sample-page.metrics"), out _));

        Assert.False(registry.RegisterPage(Owner, Descriptor("sample-page.metrics"), out var duplicate));
        Assert.Contains("already", duplicate, StringComparison.OrdinalIgnoreCase);
        Assert.False(registry.RegisterPage(Owner, Descriptor("sample-page.other") with
        {
            TemplateId = "custom",
        }, out var template));
        Assert.Contains("template", template, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PublishPage_UpdatesTheHostModel()
    {
        var registry = new PageRegistry();
        Assert.True(registry.RegisterPage(Owner, Descriptor("sample-page.metrics"), out _));

        Assert.True(registry.PublishPage(Owner, new PageData
        {
            PageId = "sample-page.metrics",
            TemplateId = PageTemplates.List,
            List = new PageListData
            {
                Rows =
                [
                    new PageListRow
                    {
                        RowId = "row",
                        Title = "Count #1",
                        Subtitle = "after click",
                        RowCommandId = "sample-page.refresh",
                        TrailingCommandId = "sample-page.refresh",
                        TrailingLabel = "Refresh",
                    },
                ],
            },
        }, out var error), error);

        var page = Assert.Single(registry.BuildPluginPages());
        var row = Assert.Single(page.Rows);
        Assert.Equal("Count #1", row.Title);
        Assert.Equal("sample-page.refresh", row.TrailingCommandId);
        Assert.Equal("Refresh", row.TrailingLabel);
    }

    [Fact]
    public void UnregisterOwner_RemovesPagesAndRaisesChanged()
    {
        var registry = new PageRegistry();
        Assert.True(registry.RegisterPage(Owner, Descriptor("sample-page.metrics"), out _));
        var changed = 0;
        registry.Changed += (_, _) => changed++;

        Assert.True(registry.UnregisterOwner(Owner));

        Assert.Equal(PageRegistry.BuiltinDescriptors.Count, registry.Count);
        Assert.Empty(registry.BuildPluginPages());
        Assert.Equal(1, changed);
    }

    [Fact]
    public void RegisterPage_StopsAtTwelvePages()
    {
        var registry = new PageRegistry();
        for (var i = 0; i < PageRegistry.MaxPages - PageRegistry.BuiltinDescriptors.Count; i++)
        {
            var owner = $"sample-page-{i}";
            Assert.True(registry.RegisterPage(owner, Descriptor($"{owner}.page"), out var error), error);
        }

        Assert.False(registry.RegisterPage("overflow", Descriptor("overflow.page"), out var limit));
        Assert.Contains("limit", limit, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RegisterPage_AllowsAtMostFourPagesPerPlugin()
    {
        var registry = new PageRegistry();
        for (var i = 0; i < 4; i++)
        {
            Assert.True(registry.RegisterPage(Owner, Descriptor($"sample-page.page{i}"), out var registerError), registerError);
        }

        Assert.False(registry.RegisterPage(Owner, Descriptor("sample-page.fifth"), out var pageError));
        Assert.Contains("4 pages", pageError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuiltinsUseTemplatesAndUserOrderIsApplied()
    {
        var registry = new PageRegistry();
        Assert.Equal(5, PageRegistry.BuiltinDescriptors.Count);
        Assert.Equal("builtin.home", PageRegistry.BuiltinDescriptors[0].TemplateId);
        Assert.Equal("calendar", PageRegistry.BuiltinDescriptors[4].TemplateId);
        Assert.DoesNotContain(PageRegistry.BuiltinDescriptors, descriptor => descriptor.PageId == "builtin.agent");

        Assert.True(registry.RegisterPage(Owner, Descriptor("sample-page.metrics"), out _));
        registry.SetOrder(["sample-page.metrics", "builtin.media", "builtin.home"]);

        var ordered = registry.BuildPageDescriptors();
        Assert.Equal("sample-page.metrics", ordered[0].PageId);
        Assert.Equal("builtin.media", ordered[1].PageId);
        Assert.Equal("builtin.home", ordered[2].PageId);
    }

    private static PageDescriptor Descriptor(string pageId) => new()
    {
        PageId = pageId,
        Order = 55,
        Title = new LocalizedString("Sample page"),
        ShortTitle = new LocalizedString("Sample"),
        IconToken = "list",
        TemplateId = PageTemplates.List,
    };
}

