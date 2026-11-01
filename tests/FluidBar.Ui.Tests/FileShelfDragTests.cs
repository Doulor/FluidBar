using FluidBar.App.Hosting.Pages;
using FluidBar.App.Settings;
using FluidBar.Sdk.Pages;
using FluidBar.Sdk.Manifest;

namespace FluidBar.Ui.Tests;

public sealed class FileShelfDragTests
{
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

    [Fact]
    public void DropCompleted_SelectsTheFileShelfPage()
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
        fixture.ExpandAndSettle();
        fixture.Controller.HandleFileDropCompleted();
        fixture.RunToRest();

        Assert.Equal("fluidbar-file-shelf.page",
            fixture.Controller.RenderState.Pages.PageSlotAt(fixture.Controller.RenderState.PageIndex)?.PageId);
    }
}
