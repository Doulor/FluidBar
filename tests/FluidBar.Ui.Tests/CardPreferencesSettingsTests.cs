using FluidBar.App.Settings;
using FluidBar.Core.Settings;

namespace FluidBar.Ui.Tests;

public sealed class CardPreferencesSettingsTests
{
    [Fact]
    public void Privacy_and_quick_actions_survive_the_UI_JSON_roundtrip()
    {
        const string json = """
            {"schemaVersion":2,"cards":{"clipboardPaused":true,"clipboardHidePreviews":true,
            "quickActions":[{"id":"theme","name":"切换主题","kind":"theme"}]}}
            """;

        var settings = AppSettings.TryParse(json, out var error);

        Assert.NotNull(settings);
        Assert.Null(error);
        Assert.Contains("\"clipboardPaused\":true", settings!.ToJson());
        Assert.Contains("\"clipboardHidePreviews\":true", settings.ToJson());
        Assert.Contains("\"kind\":\"theme\"", settings.ToJson());
    }

    [Fact]
    public void Privacy_and_quick_actions_survive_the_production_Core_projection()
    {
        const string json = """
            {"schemaVersion":3,"cards":{"clipboardPaused":true,"clipboardHidePreviews":true,
            "quickActions":[{"id":"mute","name":"静音","kind":"mute"}]}}
            """;
        Assert.True(FluidBar.Core.Settings.SettingsJson.TryDeserialize(json, out var core, out _));

        var app = CoreSettingsStoreAdapter.ToAppSettings(core!);
        var saved = CoreSettingsStoreAdapter.ApplyToCore(core!, app);
        var output = FluidBar.Core.Settings.SettingsJson.Serialize(saved);

        Assert.Contains("\"clipboardPaused\":true", output);
        Assert.Contains("\"clipboardHidePreviews\":true", output);
        Assert.Contains("\"kind\":\"mute\"", output);
    }
}
