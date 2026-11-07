using System.Text.Json;
using FluidBar.App.Hosting.Skia;
using FluidBar.App.Plugins;
using FluidBar.Core.Configuration;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Media;
using FluidBar.Plugins.Builtin;
using FluidBar.Sdk.Versioning;

namespace FluidBar.Ui.Tests;

public sealed class LyricGlowRuntimeTests
{
    private sealed class CaptureLogger : FluidBar.PluginHost.Logging.IPluginHostLogger
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Messages { get; } = new();
        public bool IsEnabled(FluidBar.PluginHost.Logging.PluginHostLogLevel level) => true;
        public void Log(FluidBar.PluginHost.Logging.PluginHostLogLevel level, string pluginId, string message,
            Exception? exception = null, IReadOnlyDictionary<string, object?>? properties = null)
            => Messages.Enqueue(message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Successful_setting_notifies_and_survives_runtime_restart(bool throwingSubscriber)
    {
        var root = Path.Combine(Path.GetTempPath(), "FluidBarTests", "lyric-glow", Guid.NewGuid().ToString("N"));
        var calls = 0;
        JsonElement observed = default;
        try
        {
            await using var media = new GsmtcMediaSessionService(NullPlatformLogger.Instance, new PlatformCounters());
            var logger = new CaptureLogger();
            var options = new PluginRuntimeOptions
            {
                PluginRootDirectory = root,
                AppVersion = new SemanticVersion(4, 1, 0),
                Localizer = new FakeLocalizer(),
                Logger = logger,
                LoadCommunity = false,
                BuiltinServices = new BuiltinPlatformServices { Media = media },
                SettingChanged = (plugin, key, value) =>
                {
                    Assert.Equal(BuiltinIds.MediaPlugin, plugin);
                    Assert.Equal(LyricGlowSettings.Key, key);
                    calls++;
                    observed = value;
                    if (throwingSubscriber) throw new InvalidOperationException("Subscriber failure");
                },
            };
            await using (var runtime = await PluginRuntime.CreateAsync(options))
            {
                Assert.NotNull(runtime.FindBuiltinSession(BuiltinIds.MediaPlugin));
                Assert.Null(runtime.GetSettingValue(BuiltinIds.MediaPlugin, LyricGlowSettings.Key));
                await runtime.SaveSettingsAsync(BuiltinIds.MediaPlugin, new CancellationToken(canceled: true));
                Assert.DoesNotContain(logger.Messages, message => message.Contains("could not be saved", StringComparison.Ordinal));
                using (var json = JsonDocument.Parse("35"))
                    Assert.True(runtime.SetSettingValue(BuiltinIds.MediaPlugin, LyricGlowSettings.Key, json.RootElement));
                Assert.Equal(1, calls);
                Assert.Equal(35, observed.GetDouble());
                Assert.False(runtime.SetSettingValue("missing-plugin", LyricGlowSettings.Key, JsonSerializer.SerializeToElement(80)));
                Assert.Equal(1, calls);
                // Rapid slider motion plus explicit flush must not race on the store's .tmp path.
                for (var percent = 0; percent <= 35; percent += 5)
                    Assert.True(runtime.SetSettingValue(BuiltinIds.MediaPlugin, LyricGlowSettings.Key,
                        JsonSerializer.SerializeToElement(percent)));
                await runtime.SaveSettingsAsync(BuiltinIds.MediaPlugin);
                // Shutdown must also drain a final fire-and-forget save.
                Assert.True(runtime.SetSettingValue(BuiltinIds.MediaPlugin, LyricGlowSettings.Key,
                    JsonSerializer.SerializeToElement(35)));
            }
            await using var restarted = await PluginRuntime.CreateAsync(options);
            var saved = restarted.GetSettingValue(BuiltinIds.MediaPlugin, LyricGlowSettings.Key);
            Assert.Equal(0.35f, MediaPillPreferences.ParseLyricGlowStrength(saved));
            await restarted.DisposeAsync();
            Assert.False(restarted.SetSettingValue(BuiltinIds.MediaPlugin, LyricGlowSettings.Key,
                JsonSerializer.SerializeToElement(100)));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
