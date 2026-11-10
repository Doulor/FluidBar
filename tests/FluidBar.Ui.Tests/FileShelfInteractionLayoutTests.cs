using FluidBar.App.Hosting.Pages;
using FluidBar.App.Hosting.Skia;
using FluidBar.App.Settings;
using FluidBar.Island;
using FluidBar.Sdk.Pages;
using SkiaSharp;

namespace FluidBar.Ui.Tests;

public sealed class FileShelfInteractionLayoutTests
{
    [Theory]
    [InlineData(0.8)]
    [InlineData(1.0)]
    [InlineData(1.6)]
    public void Visible_cards_and_fixed_toolbar_share_renderer_pointer_and_accessibility_geometry(double scale)
    {
        var registry = FileShelfOutboundDragTests.Registry([Path.Combine(Path.GetTempPath(), "layout.pdf")]);
        Assert.True(registry.PublishPage("fluidbar-file-shelf", new PageData
        {
            PageId = "fluidbar-file-shelf.page", TemplateId = PageTemplates.List,
            List = new PageListData { Rows = new[] { new PageListRow
            {
                RowId = "actions", Title = "Copy all", RowCommandId = "fluidbar-file-shelf.copy-all",
                TrailingLabel = "Clear", TrailingCommandId = "fluidbar-file-shelf.clear",
            } }.Concat(Enumerable.Range(1, 7).Select(i => new PageListRow
            {
                RowId = "file-" + i, Title = "layout-" + i + ".pdf", Subtitle = Path.GetTempPath(),
                DragFiles = [Path.Combine(Path.GetTempPath(), "layout-" + i + ".pdf")],
                RowCommandId = "fluidbar-file-shelf.copy", TrailingCommandId = "fluidbar-file-shelf.remove", TrailingLabel = "Remove",
            })).ToArray() },
        }, out _));
        using var fixture = ControllerFixture.Create(pageRegistry: registry, settings: AppSettings.Default with { FontScale = scale });
        using var source = new SkiaIslandUiaSource(fixture.Controller, new FakeLocalizer(), new SkiaHostDispatcher());
        using var renderer = new IslandRenderer();
        fixture.ExpandAndSettle();
        fixture.GoToPage(6);
        Verify();
        var page = IslandMetrics.PageRect(fixture.Controller.RenderState, 6);
        fixture.Controller.HandlePointerMoved(page.MidX, page.MidY);
        Assert.True(fixture.Controller.HandleWheel(80, false, false).ContentScroll);
        fixture.RunToRest();
        Verify();
        Assert.True(fixture.Controller.HandleWheel(10000, false, false).ContentScroll);
        fixture.RunToRest();
        Verify();
        var bottom = fixture.Controller.RenderState.Pages.PluginPageAt(6)!.ScrollOffset;
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.False(fixture.Controller.HandleWheel(120, false, false).Paged);
        Assert.Equal(6, fixture.Controller.Pager.SelectedIndex);
        Assert.Equal(bottom, fixture.Controller.RenderState.Pages.PluginPageAt(6)!.ScrollOffset);
        Assert.True(fixture.Controller.HandleWheel(-60, false, false).ContentScroll);
        Assert.True(fixture.Controller.RenderState.Pages.PluginPageAt(6)!.ScrollOffset < bottom);

        void Verify()
        {
            var state = fixture.Controller.RenderState;
            var pageRect = IslandMetrics.PageRect(state, 6);
            var model = state.Pages.PluginPageAt(6)!;
            var metrics = FontScaleMetrics.From(scale);
            var snapshot = source.BuildSnapshot();
            for (var i = 0; i < model.Rows.Count; i++)
            {
                var row = PluginListLayout.Row(pageRect, model, i, metrics);
                var visible = SKRect.Intersect(row, PluginListLayout.Clip(pageRect, model, i, metrics));
                var node = snapshot.Find($"island.page.6.row.{i}");
                if (visible.IsEmpty) { Assert.Null(node); continue; }
                Assert.NotNull(node);
                Assert.Equal(visible.Left, node!.BoundsDip.X);
                Assert.Equal(visible.Top, node.BoundsDip.Y);
                Assert.Equal(visible.Height, node.BoundsDip.Height);
                var hit = SkiaIslandHitTest.Hit(state, visible.MidX, visible.MidY);
                Assert.Equal(IslandPageControl.PluginRow, hit.Control);
                Assert.Equal(i, hit.Index);
                Assert.Equal(hit, renderer.HitTest(state, visible.MidX, visible.MidY));
                var button = SKRect.Intersect(PluginListLayout.TrailingButton(row, model.Rows[i]), visible);
                var buttonNode = snapshot.Find($"island.page.6.row.{i}.button");
                if (button.IsEmpty) { Assert.Null(buttonNode); continue; }
                Assert.NotNull(buttonNode);
                Assert.Equal(IslandPageControl.PluginRowButton, SkiaIslandHitTest.Hit(state, button.MidX, button.MidY).Control);
            }
            var toolbar = PluginListLayout.Row(pageRect, model, 0, metrics);
            Assert.Equal(pageRect.Top + PageMetricsLayout.BodyTop, toolbar.Top);
        }
    }
}
