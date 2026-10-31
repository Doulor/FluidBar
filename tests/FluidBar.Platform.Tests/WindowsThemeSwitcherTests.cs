using FluidBar.Platform.Windows.Shell;
using Microsoft.Win32;

namespace FluidBar.Platform.Tests;

public sealed class WindowsThemeSwitcherTests
{
    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(1, 1, 0)]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 1)]
    [InlineData(null, 0, 1)]
    [InlineData(null, null, 0)]
    public async Task Toggle_writes_both_modes_before_notifying(int? apps, int? system, int expected)
    {
        using var fixture = new ThemeRegistryFixture(apps, system);
        var notifications = 0;
        var switcher = new WindowsThemeSwitcher(fixture.Open, () =>
        {
            fixture.AssertModes(expected);
            notifications++;
        });

        await switcher.ToggleAsync();

        fixture.AssertModes(expected);
        Assert.Equal(1, notifications);
    }

    [Fact]
    public async Task Concurrent_clicks_each_invert_the_latest_registry_value()
    {
        using var fixture = new ThemeRegistryFixture(0, 0);
        var observed = new List<int>();
        var switcher = new WindowsThemeSwitcher(fixture.Open, () =>
        {
            using var key = fixture.Open();
            observed.Add((int)key.GetValue("AppsUseLightTheme")!);
        });

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => switcher.ToggleAsync()));

        Assert.Equal(new[] { 1, 0, 1, 0, 1, 0, 1, 0 }, observed);
        fixture.AssertModes(0);
    }

    [Fact]
    public async Task Cancelled_toggle_does_not_write_or_notify()
    {
        using var fixture = new ThemeRegistryFixture(0, 0);
        var notified = false;
        var switcher = new WindowsThemeSwitcher(fixture.Open, () => notified = true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => switcher.ToggleAsync(cancellation.Token));

        fixture.AssertModes(0);
        Assert.False(notified);
    }

    [Fact]
    public async Task Read_only_preferences_report_failure_without_notifying()
    {
        using var fixture = new ThemeRegistryFixture(0, 0);
        var notified = false;
        var switcher = new WindowsThemeSwitcher(fixture.OpenReadOnly, () => notified = true);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => switcher.ToggleAsync());

        fixture.AssertModes(0);
        Assert.False(notified);
    }

    private sealed class ThemeRegistryFixture : IDisposable
    {
        private readonly string _path = @"Software\FluidBar.Tests\Theme-" + Guid.NewGuid().ToString("N");

        public ThemeRegistryFixture(int? apps, int? system)
        {
            using var key = Registry.CurrentUser.CreateSubKey(_path);
            if (apps.HasValue)
            {
                key.SetValue("AppsUseLightTheme", apps.Value, RegistryValueKind.DWord);
            }

            if (system.HasValue)
            {
                key.SetValue("SystemUsesLightTheme", system.Value, RegistryValueKind.DWord);
            }
        }

        public RegistryKey Open() => Registry.CurrentUser.OpenSubKey(_path, writable: true)!;

        public RegistryKey OpenReadOnly() => Registry.CurrentUser.OpenSubKey(_path)!;

        public void AssertModes(int expected)
        {
            using var key = Open();
            Assert.Equal(expected, key.GetValue("AppsUseLightTheme"));
            Assert.Equal(expected, key.GetValue("SystemUsesLightTheme"));
            Assert.Equal(RegistryValueKind.DWord, key.GetValueKind("AppsUseLightTheme"));
            Assert.Equal(RegistryValueKind.DWord, key.GetValueKind("SystemUsesLightTheme"));
        }

        public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);
    }
}
