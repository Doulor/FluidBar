using FluidBar.App.Hosting.Pages;
using FluidBar.App.Hosting.Skia;
using FluidBar.App.Settings;
using FluidBar.Island;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Pages;

namespace FluidBar.Ui.Tests;

public sealed class FileShelfOutboundDragTests
{
    [Fact]
    public void File_row_defers_copy_until_release()
    {
        using var fixture = Create();
        var row = Row(fixture);
        var calls = new List<PluginPageCommandRequest>();
        fixture.Controller.PluginPageCommandRequested += calls.Add;
        fixture.Controller.HandlePointerPressed(row.MidX, row.MidY);
        Assert.Empty(calls);
        fixture.Controller.HandlePointerReleased(row.MidX, row.MidY);
        Assert.Equal("fluidbar-file-shelf.copy", Assert.Single(calls).CommandId);
    }

    [Fact]
    public void File_row_drag_is_distinct_from_paging_and_never_copies_on_press()
    {
        using var fixture = Create();
        var row = Row(fixture);
        var calls = new List<PluginPageCommandRequest>();
        fixture.Controller.PluginPageCommandRequested += calls.Add;
        fixture.Controller.HandlePointerPressed(row.MidX, row.MidY);
        Assert.Equal("FileCandidate", fixture.Controller.HandleDragStarted(row.MidX, row.MidY, 0).ToString());
        fixture.Controller.HandlePointerReleased(row.MidX + 20, row.MidY);
        Assert.Empty(calls);
        Assert.False(fixture.Controller.Pager.IsDragging);
    }

    [Fact]
    public void Registry_snapshots_drag_paths_so_later_mutation_cannot_change_the_drag()
    {
        var files = new[] { Path.Combine(Path.GetTempPath(), "original.txt") };
        var registry = Registry(files);
        files[0] = Path.Combine(Path.GetTempPath(), "different.txt");
        Assert.EndsWith("original.txt", Assert.Single(registry.BuildPluginPages().Single().Rows.Single().DragFiles!));
    }

    [Fact]
    public void Native_drag_keeps_the_card_open_until_source_completion()
    {
        using var fixture = ControllerFixture.Create(settings: AppSettings.Default with
        { AutoCollapseMs = 300, AutoHideMs = 1000 }, pageRegistry: Registry([Path.Combine(Path.GetTempPath(), "keep.txt")]));
        fixture.ExpandAndSettle();
        fixture.GoToPage(6);
        var row = Row(fixture);
        fixture.Controller.HandlePointerPressed(row.MidX, row.MidY);
        Assert.NotNull(fixture.Controller.TakeFileDrag());
        fixture.Controller.HandlePointerLeft();
        fixture.Time.Advance(TimeSpan.FromSeconds(5));
        fixture.Controller.PumpTimers();
        fixture.RunToRest();
        Assert.Equal(FluidBar.Core.Activities.IslandStateKind.Expanded, fixture.Controller.State.Kind);
        fixture.Controller.CompleteFileDrag();
        fixture.Controller.HandlePointerReleased(row.MidX, row.MidY);
        Assert.Equal("file-stable", fixture.Controller.RenderState.Pages.PluginPageAt(6)!.Rows.Single().RowId);
    }

    internal static ControllerFixture Create()
    {
        var fixture = ControllerFixture.Create(pageRegistry: Registry([Path.Combine(Path.GetTempPath(), "drag-out.txt")]));
        fixture.ExpandAndSettle();
        fixture.GoToPage(6);
        return fixture;
    }

    internal static SkiaSharp.SKRect Row(ControllerFixture fixture)
    {
        var state = fixture.Controller.RenderState;
        return PageMetricsLayout.NotificationRow(IslandMetrics.PageRect(state, 6), 0, 0,
            FontScaleMetrics.From(state.FontScale));
    }

    internal static PageRegistry Registry(string[] files)
    {
        var registry = new PageRegistry();
        Assert.True(registry.RegisterPage("fluidbar-file-shelf", new PageDescriptor
        { PageId = "fluidbar-file-shelf.page", TemplateId = PageTemplates.List, Order = 110,
            Title = new LocalizedString("Files"), IconToken = "file" }, out _));
        Assert.True(registry.PublishPage("fluidbar-file-shelf", new PageData
        {
            PageId = "fluidbar-file-shelf.page", TemplateId = PageTemplates.List,
            List = new PageListData { Rows = [new PageListRow
            { RowId = "file-stable", Title = "drag-out.txt", DragFiles = files,
                RowCommandId = "fluidbar-file-shelf.copy", TrailingCommandId = "fluidbar-file-shelf.remove", TrailingLabel = "Remove" }] },
        }, out var error), error);
        return registry;
    }
}
