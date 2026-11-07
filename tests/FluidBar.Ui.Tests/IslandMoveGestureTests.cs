using Avalonia.Controls;
using Avalonia.VisualTree;
using FluidBar.App.Hosting.Skia;
using FluidBar.App.Settings;
using FluidBar.App.Views.Settings;
using FluidBar.Core.Activities;
using FluidBar.Core.Layout;
using FluidBar.Island;

namespace FluidBar.Ui.Tests;

public sealed class IslandMoveGestureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ControlClickRestoresDefaultPositionWithoutOpeningOrActivating(bool expanded)
    {
        var initial = AppSettings.Default.WithMonitorPlacement("display-1", IslandAnchor.BottomRight, 120, 130)
            with { Anchor = "bottom-left", Palette = "nord" };
        var actions = new RecordingIslandPlatformActions();
        using var fixture = ControllerFixture.Create(initial, actions);
        if (expanded) fixture.ExpandAndSettle();
        var shell = IslandMetrics.ShellRect(fixture.Controller.RenderState);
        var before = fixture.Controller.State.Kind;
        fixture.Controller.HandlePointerPressed(shell.MidX, shell.MidY, control: true);
        fixture.Controller.HandlePointerReleased(shell.MidX, shell.MidY);
        Assert.Equal(AppSettings.Default.Anchor, fixture.Settings.Current.Anchor);
        Assert.Empty(fixture.Settings.Current.Placement);
        Assert.Equal("nord", fixture.Settings.Current.Palette);
        Assert.Equal(before, fixture.Controller.State.Kind);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public void ControlDragTakesPriorityOverNotificationDismissAndNeverResetsPosition()
    {
        var initial = AppSettings.Default.WithMonitorPlacement("display-1", IslandAnchor.BottomRight, 120, 130);
        using var fixture = ControllerFixture.Create(initial);
        fixture.Controller.PublishActivity(ActivityKind.Notification, new ActivityPayload
            { Title = "Notice", SourceName = "Test", Ttl = TimeSpan.FromMinutes(5) }, 1, "move-notification");
        fixture.Settle();
        var shell = IslandMetrics.ShellRect(fixture.Controller.RenderState);
        fixture.Controller.HandlePointerPressed(shell.MidX, shell.MidY, control: true);
        Assert.Equal(SkiaDragKind.WindowCandidate, fixture.Controller.HandleDragStarted(shell.MidX, shell.MidY, 0));
        Assert.True(fixture.Controller.BeginWindowDrag());
        // Client coordinates can be unchanged after the host follows the pointer.
        fixture.Controller.HandlePointerReleased(shell.MidX, shell.MidY);
        fixture.Controller.EndWindowDrag();
        Assert.Equal(initial.ResolveMonitorPlacement("display-1"), fixture.Settings.Current.ResolveMonitorPlacement("display-1"));
        Assert.Equal(ActivityKind.Notification, fixture.Controller.RenderState.Compact?.Kind);
    }

    [Fact]
    public void CompactPressCanBecomeAMoveWithoutOpeningThePanel()
    {
        using var fixture = ControllerFixture.Create();
        var shell = IslandMetrics.ShellRect(fixture.Controller.RenderState);
        fixture.Controller.HandlePointerPressed(shell.MidX, shell.MidY);
        Assert.Equal(IslandStateKind.Compact, fixture.Controller.State.Kind);
        Assert.Equal(SkiaDragKind.WindowCandidate, fixture.Controller.HandleDragStarted(shell.MidX, shell.MidY, 0));
        Assert.True(fixture.Controller.BeginWindowDrag());
        fixture.Controller.HandlePointerReleased(shell.MidX + 40, shell.MidY);
        Assert.True(fixture.Controller.EndWindowDrag());
        Assert.Equal(IslandStateKind.Compact, fixture.Controller.State.Kind);
    }

    [Fact]
    public void CompactClickOpensOnRelease()
    {
        using var fixture = ControllerFixture.Create();
        var shell = IslandMetrics.ShellRect(fixture.Controller.RenderState);
        fixture.Controller.HandlePointerPressed(shell.MidX, shell.MidY);
        Assert.Equal(IslandStateKind.Compact, fixture.Controller.State.Kind);
        fixture.Controller.HandlePointerReleased(shell.MidX, shell.MidY);
        Assert.Equal(IslandStateKind.Expanded, fixture.Controller.State.Kind);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("zh-Hans")]
    public async Task GeneralSettingsExplainMouseGestures(string culture)
    {
        await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer(culture);
            var (settings, _) = TestFixtures.CreateSettings(AppSettings.Default);
            var window = new SettingsWindow(settings, localizer, SettingsWindowSection.General);
            window.Show();
            try
            {
                var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
                foreach (var key in new[] { "Title", "TemporaryMove", "SavePosition", "ResetPosition", "TemporaryHide", "Hint" })
                {
                    var message = localizer.Get("Settings.General.Gestures." + key);
                    Assert.DoesNotContain("Settings.General.Gestures.", message);
                    if (key == "Title") message = message.ToUpperInvariant();
                    Assert.Contains(message, texts);
                }
            }
            finally { window.Close(); }
        });
    }
}
