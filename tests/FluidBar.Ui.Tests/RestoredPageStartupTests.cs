using FluidBar.App.Settings;
using FluidBar.Core.Activities;

namespace FluidBar.Ui.Tests;

/// <summary>Restoring a non-home page must initialize the rail before the pager commits.</summary>
public sealed class RestoredPageStartupTests
{
    [Fact]
    public void RestoredPage_OnConstruction_DoesNotThrowAndSelectsIt()
    {
        using var fixture = ControllerFixture.Create(
            AppSettings.Default with { AutoCollapseMs = 0, AutoHideMs = 0 },
            restoredPage: 3);

        // The page may be applied on the first frame once the pager knows the full page count; the
        // regression is that construction must not touch the rail before it is initialized.
        Assert.Equal(IslandStateKind.Compact, fixture.Controller.State.Kind);
    }
}
