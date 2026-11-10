using Avalonia.Controls;
using FluidBar.App;

namespace FluidBar.Ui.Tests;

public sealed class AppBrandingTests
{
    [Fact]
    public async Task Brand_icon_can_be_initialized_and_is_shared_by_base_and_derived_windows()
    {
        await AvaloniaTest.Run(() =>
        {
            AppBranding.ConfigureWindowIcons();
            var window = new Window();
            var derived = new BrandWindow();
            try
            {
                window.Show();
                derived.Show();
                Assert.NotNull(window.Icon);
                Assert.Same(window.Icon, derived.Icon);
            }
            finally { derived.Close(); window.Close(); }
        });
    }

    private sealed class BrandWindow : Window;
}
