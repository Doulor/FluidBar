using FluidBar.App.Hosting.Pages;
using FluidBar.App.Settings;
using FluidBar.Sdk.Pages;
using FluidBar.Sdk.Manifest;

namespace FluidBar.Ui.Tests;

public sealed class FileShelfDragTests
{
    [Fact]
    public void System_feedback_temporarily_covers_the_shelf_then_file_pill_returns()
    {
        using var fixture = ControllerFixture.Create();
        fixture.Controller.PublishActivity(FluidBar.Core.Activities.ActivityKind.FileShelf,
            new FluidBar.Core.Activities.ActivityPayload
            { Title = "file.txt", Body = "file.txt", Badge = "2", IsActive = true }, 1, "shelf");
        fixture.Settle();
        Assert.Equal(FluidBar.Island.IconKind.Document, fixture.Controller.RenderState.Compact?.Model.Icon);
        Assert.Equal("2", fixture.Controller.RenderState.Compact?.Model.Badge);
        fixture.Controller.PublishActivity(FluidBar.Core.Activities.ActivityKind.Volume,
            new FluidBar.Core.Activities.ActivityPayload
            { Title = "Volume", ProgressPercent = 50, Ttl = TimeSpan.FromSeconds(1) }, 1, "volume");
        fixture.Settle();
        Assert.Equal(FluidBar.Core.Activities.ActivityKind.Volume, fixture.Controller.RenderState.Compact?.Kind);
        fixture.Time.Advance(TimeSpan.FromSeconds(2));
        fixture.Controller.PumpTimers();
        fixture.Settle();
        Assert.Equal(FluidBar.Core.Activities.ActivityKind.FileShelf, fixture.Controller.RenderState.Compact?.Kind);
    }

    [Fact]
    public void Plugin_file_list_scrolls_before_the_pager_changes_pages()
    {
        var registry = new PageRegistry();
        Assert.True(registry.RegisterPage("fluidbar-file-shelf", new PageDescriptor
        {
            PageId = "fluidbar-file-shelf.page", TemplateId = PageTemplates.List,
            Title = new LocalizedString("Files"), IconToken = "file", Order = 100,
        }, out _));
        Assert.True(registry.PublishPage("fluidbar-file-shelf", new PageData
        {
            PageId = "fluidbar-file-shelf.page", TemplateId = PageTemplates.List,
            List = new PageListData { Rows = Enumerable.Range(0, 18).Select(i => new PageListRow
            { RowId = "file-" + i, Title = "File " + i, RowCommandId = "fluidbar-file-shelf.copy" }).ToArray() },
        }, out _));
        using var fixture = ControllerFixture.Create(pageRegistry: registry);
        fixture.ExpandAndSettle();
        fixture.GoToPage(6);
        var state = fixture.Controller.RenderState;
        var body = FluidBar.Island.IslandMetrics.ContentRect(FluidBar.Island.IslandMetrics.ShellRect(state), state.RailSide, state.TabBarStyle);
        fixture.Controller.HandlePointerMoved(body.MidX, body.MidY);
        var outcome = fixture.Controller.HandleWheel(10000, false, false);
        Assert.True(outcome.ContentScroll);
        Assert.False(outcome.Paged);
        Assert.Equal(6, fixture.Controller.Pager.SelectedIndex);
        state = fixture.Controller.RenderState;
        var plugin = state.Pages.PluginPageAt(6)!;
        Assert.True(plugin.ScrollOffset > 0);
        var row = FluidBar.Island.PluginListLayout.Row(body, plugin, 17,
            FluidBar.Island.FontScaleMetrics.From(state.FontScale));
        FluidBar.App.Hosting.Skia.PluginPageCommandRequest? invoked = null;
        fixture.Controller.PluginPageCommandRequested += request => invoked = request;
        fixture.Controller.HandlePointerPressed(row.MidX, row.MidY);
        fixture.Controller.HandlePointerReleased(row.MidX, row.MidY);
        Assert.Equal("file-17", invoked?.RowId);
    }

    [Fact]
    public void Shelf_activity_is_persistent_opens_its_own_page_and_retracts_the_same_key()
    {
        var adapter = new FluidBar.App.Plugins.PluginIslandEventAdapter();
        var activity = adapter.Adapt(new FluidBar.Sdk.IslandEvent
        {
            Source = "fluidbar-file-shelf", Title = "example.txt", IconKind = "file",
            Payload = new FluidBar.Sdk.IslandEventPayload { IsActive = true, Badge = "1" },
        });
        Assert.Equal("fileshelf", FluidBar.Core.Activities.ActivityKindExtensions.ToToken(activity.Key.Kind));
        Assert.True(FluidBar.Core.Activities.ActivityPriorityPolicy.IsPersistent(activity.Key.Kind, activity.Payload));
        Assert.Null(FluidBar.Core.Activities.ActivityPriorityPolicy.DefaultTtl(activity.Key.Kind));
        Assert.Equal("fluidbar-file-shelf.page", activity.Payload.PreferredPage?.PageId);
        Assert.Equal(activity.Key, adapter.Retract("fluidbar-file-shelf").Key);
    }

    [Fact]
    public void DragEnter_ExpandsAndSelectsTheFileShelfPage()
    {
        var registry = new PageRegistry();
        var registered = registry.RegisterPage("fluidbar-file-shelf", new PageDescriptor
        {
            PageId = "fluidbar-file-shelf.page",
            TemplateId = PageTemplates.List,
            Order = 100,
            Title = new LocalizedString("File shelf"),
            ShortTitle = new LocalizedString("Files"),
            IconToken = "file",
        }, out var error);
        Assert.True(registered, error);

        using var fixture = ControllerFixture.Create(pageRegistry: registry);
        fixture.Settle();
        Assert.Equal(7, fixture.Controller.RenderState.Pages.PageCount);
        Assert.Equal("fluidbar-file-shelf.page", fixture.Controller.RenderState.Pages.PageSlotAt(6)?.PageId);
        fixture.Controller.HandleFileDragEnter();
        fixture.RunToRest();

        Assert.Equal(FluidBar.Core.Activities.IslandStateKind.Expanded, fixture.Controller.State.Kind);
        Assert.Equal(6, fixture.Controller.Pager.SelectedIndex);
        Assert.Equal("fluidbar-file-shelf.page", fixture.Controller.State.Page.PageId);
        Assert.Equal("fluidbar-file-shelf.page",
            fixture.Controller.RenderState.Pages.PageSlotAt(fixture.Controller.RenderState.PageIndex)?.PageId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DropCompleted_ExpandsAndSelectsTheFileShelfPage(bool alreadyExpanded)
    {
        var registry = new PageRegistry();
        var registered = registry.RegisterPage("fluidbar-file-shelf", new PageDescriptor
        {
            PageId = "fluidbar-file-shelf.page",
            TemplateId = PageTemplates.List,
            Order = 100,
            Title = new LocalizedString("File shelf"),
            ShortTitle = new LocalizedString("Files"),
            IconToken = "file",
        }, out var error);
        Assert.True(registered, error);

        using var fixture = ControllerFixture.Create(pageRegistry: registry);
        if (alreadyExpanded) fixture.ExpandAndSettle();
        else fixture.Settle();
        fixture.Controller.HandleFileDropCompleted();
        fixture.RunToRest();

        Assert.Equal(FluidBar.Core.Activities.IslandStateKind.Expanded, fixture.Controller.State.Kind);
        Assert.Equal("fluidbar-file-shelf.page",
            fixture.Controller.RenderState.Pages.PageSlotAt(fixture.Controller.RenderState.PageIndex)?.PageId);
    }
}
