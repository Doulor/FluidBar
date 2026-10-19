using FluidBar.App.Settings;

namespace FluidBar.Ui.Tests;

/// <summary>Hotkey bindings are user-editable settings; they must round-trip through the JSON store.</summary>
public sealed class HotkeySettingsTests
{
    [Fact]
    public void Hotkeys_RoundTripThroughSettingsJson()
    {
        var settings = AppSettings.Default with
        {
            HotkeyToggle = "Ctrl+Shift+F12",
            HotkeyHide = "Ctrl+Alt+Shift+H",
            HotkeySettings = "Ctrl+Alt+Comma",
        };

        var parsed = AppSettings.TryParse(settings.ToJson(), out var error);

        Assert.Null(error);
        Assert.NotNull(parsed);
        Assert.Equal("Ctrl+Shift+F12", parsed!.HotkeyToggle);
        Assert.Equal("Ctrl+Alt+Shift+H", parsed.HotkeyHide);
        Assert.Equal("Ctrl+Alt+Comma", parsed.HotkeySettings);
    }

    [Fact]
    public void EmptyHotkey_FallsBackToTheDefault()
    {
        var parsed = (AppSettings.Default with { HotkeyToggle = "  " }).Normalize();
        Assert.Equal("Ctrl+Alt+D", parsed.HotkeyToggle);
    }
}
