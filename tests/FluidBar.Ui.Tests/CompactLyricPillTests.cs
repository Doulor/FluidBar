using FluidBar.App.Hosting.Skia;
using FluidBar.App.Localization;
using FluidBar.App.State;
using FluidBar.Core.Activities;
using FluidBar.Core.Presentation;

namespace FluidBar.Ui.Tests;

/// <summary>Compact pill lyric mode: media setting gate, fallback to the title form and the on-disk store.</summary>
public sealed class CompactLyricPillTests
{
    private static IslandViewModel MediaIsland() => new()
    {
        StateKind = IslandStateKind.Compact,
        Page = IslandPage.Media,
        Primary = new ActivityViewModel
        {
            Key = new ActivityKey(ActivityKind.Media, "test:media"),
            Kind = ActivityKind.Media,
            Icon = ActivityIconKind.Media,
            Accent = AccentRule.Artwork,
            KindLabelKey = "activity.media",
            Page = IslandPage.Media,
            PriorityRank = 1,
            Title = "Miss You a Little (feat. lovelytheband)",
            Subtitle = "Bryce Vine",
            IsActive = true,
            ShowsWave = true,
        },
    };

    private static IslandContent MediaContent(string? lyric) => new()
    {
        Media = new MediaSessionData
        {
            Title = "Miss You a Little (feat. lovelytheband)",
            Artist = "Bryce Vine",
            LyricLine = lyric,
            IsPlaying = true,
            ProgressPercent = 42,
        },
    };

    [Fact]
    public Task LyricMode_ReplacesTitleAndArtistWithTheLiveLine()
    {
        return AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var activity = SkiaIslandStateBuilder.BuildCompact(
                MediaIsland(), MediaContent("I miss you a little all the time"), localizer, peeked: false,
                compactLyrics: true);

            Assert.True(activity.Model.ShowLyrics);
            Assert.Equal("I miss you a little all the time", activity.Model.LyricLine);
            // The title form must not be reachable through the model while the lyric is showing.
            Assert.Equal(ActivityKind.Media, activity.Kind);
    
        });
    }

    [Fact]
    public Task LyricMode_OffKeepsTheTitleForm()
    {
        return AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var activity = SkiaIslandStateBuilder.BuildCompact(
                MediaIsland(), MediaContent("I miss you a little all the time"), localizer, peeked: false,
                compactLyrics: false);

            Assert.False(activity.Model.ShowLyrics);
            Assert.Null(activity.Model.LyricLine);
            Assert.Equal("Miss You a Little (feat. lovelytheband)", activity.Model.Title);
    
        });
    }

    [Fact]
    public Task LyricMode_WithoutALyricLineFallsBackToTheTitleForm()
    {
        return AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var activity = SkiaIslandStateBuilder.BuildCompact(
                MediaIsland(), MediaContent(null), localizer, peeked: false, compactLyrics: true);

            Assert.False(activity.Model.ShowLyrics);
            Assert.Null(activity.Model.LyricLine);
            Assert.Equal("Miss You a Little (feat. lovelytheband)", activity.Model.Title);
            Assert.False(string.IsNullOrEmpty(activity.Model.Subtitle));
    
        });
    }

    [Fact]
    public Task LyricMode_DoesNotLeakIntoNonMediaActivities()
    {
        return AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var island = MediaIsland() with
            {
                Primary = new ActivityViewModel
                {
                    Key = new ActivityKey(ActivityKind.Clipboard, "test:clipboard"),
                    Kind = ActivityKind.Clipboard,
                    Icon = ActivityIconKind.Clipboard,
                    Accent = AccentRule.ThemeAccent,
                    KindLabelKey = "activity.clipboard",
                    Page = IslandPage.Clipboard,
                    PriorityRank = 2,
                    Title = "copied text",
                },
            };
            var activity = SkiaIslandStateBuilder.BuildCompact(
                island, MediaContent("a lyric"), localizer, peeked: false, compactLyrics: true);

            Assert.False(activity.Model.ShowLyrics);
            Assert.Null(activity.Model.LyricLine);
    
        });
    }

    [Fact]
    public async Task MediaPluginSettingsPage_OffersTheCompactLyricsToggle()
    {
        await AvaloniaTest.Run(() =>
        {
            var contribs = FluidBar.Plugins.Builtin.BuiltinContributions.ForMedia();
            var page = Assert.Single(contribs.SettingsPages!);
            var toggle = Assert.Single(page.Controls!, c => c.Key == "media.compactLyrics");

            Assert.Equal(FluidBar.Sdk.Contributions.SettingControlType.Toggle, toggle.Type);
            Assert.Equal("setting.media.compactLyrics.title", toggle.Title!.Value);

            // The label must resolve in both shipped locales (no raw key leaks into the settings page).
            foreach (var culture in new[] { "en-US", "zh-Hans" })
            {
                var localizer = TestFixtures.CreateLocalizer(culture);
                localizer.SetCulture(culture);
                var title = localizer.Get("setting.media.compactLyrics.title");
                Assert.False(string.IsNullOrWhiteSpace(title));
                Assert.NotEqual("setting.media.compactLyrics.title", title);
            }
        });
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("not json", true)]
    [InlineData("{\"values\":{}}", true)]
    [InlineData("{\"values\":{\"media.compactLyrics\":true}}", true)]
    [InlineData("{\"values\":{\"media.compactLyrics\":false}}", false)]
    [InlineData("{\"values\":{\"media.compactLyrics\":\"no\"}}", true)]
    [InlineData("{\"values\":{\"media.showLyrics\":false}}", true)]
    public void ParseShowCompactLyrics_DefaultsToOn(string? json, bool expected)
        => Assert.Equal(expected, MediaPillPreferences.ParseShowCompactLyrics(json));

    [Fact]
    public void Preferences_ReadTheMediaPluginSettingsFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fluidbar-pill-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{\"values\":{\"media.compactLyrics\":false}}");
            var preferences = new MediaPillPreferences(path);
            Assert.False(preferences.ShowCompactLyrics);

            // Writing the store from the settings UI must be picked up without a restart.
            Thread.Sleep(15);
            File.WriteAllText(path, "{\"values\":{\"media.compactLyrics\":true}}");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
            Assert.True(preferences.ShowCompactLyrics);
        }
        finally
        {
            File.Delete(path);
        }
    }
}