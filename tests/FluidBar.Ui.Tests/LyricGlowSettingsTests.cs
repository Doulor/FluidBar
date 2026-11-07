using System.Text.Json;
using FluidBar.App.Hosting.Skia;
using FluidBar.App.State;
using FluidBar.Core.Activities;

namespace FluidBar.Ui.Tests;

public sealed class LyricGlowSettingsTests
{
    [Theory]
    [InlineData(null, 0.7f)]
    [InlineData("null", 0.7f)]
    [InlineData("true", 0.7f)]
    [InlineData("{}", 0.7f)]
    [InlineData("\"oops\"", 0.7f)]
    [InlineData("\"NaN\"", 0.7f)]
    [InlineData("\"Infinity\"", 0.7f)]
    [InlineData("1e400", 0.7f)]
    [InlineData("\"35.5\"", 0.355f)]
    [InlineData("35", 0.35f)]
    [InlineData("-15", 0)]
    [InlineData("130", 1)]
    public void Parse_normalizes_plugin_setting(string? json, float expected)
    {
        var value = json is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal(expected, MediaPillPreferences.ParseLyricGlowStrength(value));
    }

    [Fact]
    public async Task Stored_value_loads_without_a_running_builtin_session()
    {
        var root = Path.Combine(Path.GetTempPath(), "FluidBarTests", "lyric-glow-read", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "settings.json");
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(path, "{\"values\":{\"media.lyricGlowStrength\":35}}");
            var preferences = new MediaPillPreferences(path);
            var value = await preferences.ReadStoredLyricGlowSettingAsync();
            Assert.Equal(0.35f, MediaPillPreferences.ParseLyricGlowStrength(value));
            preferences.SetLyricGlowStrength(value);
            await File.WriteAllTextAsync(path, "{\"values\":{\"media.lyricGlowStrength\":0}}");
            Assert.Equal(0.35f, preferences.LyricGlowStrength); // Getter must not poll the file.
            Assert.Equal(0, MediaPillPreferences.ParseLyricGlowStrength(
                await new MediaPillPreferences(path).ReadStoredLyricGlowSettingAsync()));
            await File.WriteAllTextAsync(path, "not JSON");
            Assert.Null(await preferences.ReadStoredLyricGlowSettingAsync());
            File.Delete(path);
            Assert.Null(await preferences.ReadStoredLyricGlowSettingAsync());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Cached_preference_is_isolated_and_does_not_need_a_file()
    {
        var first = MediaPillPreferences.Default;
        var second = MediaPillPreferences.Default;
        Assert.True(first.SetLyricGlowStrength(JsonSerializer.SerializeToElement(35)));
        Assert.Equal(0.35f, first.LyricGlowStrength);
        Assert.Equal(0.7f, second.LyricGlowStrength);
        Assert.False(first.SetLyricGlowStrength(JsonSerializer.SerializeToElement(35)));
    }

    [Fact]
    public void Paused_media_refreshes_both_models_without_a_tick_or_content_change()
    {
        using var fixture = ControllerFixture.Create();
        fixture.Controller.PublishActivity(ActivityKind.Media,
            new ActivityPayload { Title = "Paused track", LyricLine = "AAAA BBBB", IsActive = false }, 1);
        fixture.Data.Update(content => content with
        {
            Media = new MediaSessionData { Title = "Paused track", LyricLine = "AAAA BBBB", IsPlaying = false },
        });
        fixture.Settle();
        while (fixture.Controller.TakeInvalidation(out _)) { }
        var frames = 0;
        fixture.Controller.FrameRequested += () => frames++;
        fixture.Controller.SetLyricGlowStrength(JsonSerializer.SerializeToElement(35));
        Assert.Equal(0.35f, fixture.Controller.RenderState.Compact!.Value.Model.LyricGlowStrength);
        Assert.Equal(0.35f, fixture.Controller.RenderState.Pages.Media.LyricGlowStrength);
        Assert.True(fixture.Controller.TakeInvalidation(out var invalidation));
        Assert.True(invalidation.FullFrame);
        Assert.True(frames > 0);
        var previousFrames = frames;
        fixture.Controller.SetLyricGlowStrength(JsonSerializer.SerializeToElement(35));
        Assert.Equal(previousFrames, frames);
        fixture.Controller.SetLyricGlowStrength(JsonSerializer.SerializeToElement(0));
        fixture.Data.Update(content => content with { Media = content.Media! with { Title = "Changed track" } });
        Assert.Equal(0, fixture.Controller.RenderState.Compact!.Value.Model.LyricGlowStrength);
        Assert.Equal(0, fixture.Controller.RenderState.Pages.Media.LyricGlowStrength);
    }
}
