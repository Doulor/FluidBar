using FluidBar.App.Settings;
using FluidBar.Core.Activities;

namespace FluidBar.Ui.Tests;

/// <summary>Changing behavior settings at runtime must replace or clear the old deadlines.</summary>
public sealed class SettingsDeadlineTests
{
    [Fact]
    public void AutoCollapseOff_ClearsTheExpandedDeadlineImmediately()
    {
        using var fixture = ControllerFixture.Create(AppSettings.Default with { AutoCollapseMs = 2600, AutoHideMs = 0 });
        fixture.ExpandAndSettle();
        fixture.Controller.ApplyInput(IslandInput.PointerLeft());
        Assert.Equal(IslandStateKind.Expanded, fixture.Controller.State.Kind);
        Assert.NotNull(fixture.Controller.State.AutoCollapseDeadline);

        fixture.Settings.Update(settings => settings with { AutoCollapseMs = 0 });

        Assert.Null(fixture.Controller.State.AutoCollapseDeadline);
        fixture.Time.Advance(TimeSpan.FromSeconds(5));
        fixture.Controller.Advance(TimeSpan.Zero);
        Assert.Equal(IslandStateKind.Expanded, fixture.Controller.State.Kind);
    }
}
