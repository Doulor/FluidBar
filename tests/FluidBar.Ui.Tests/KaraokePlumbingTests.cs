using FluidBar.App.Hosting.Skia;
using FluidBar.App.Localization;
using FluidBar.App.State;
using FluidBar.Core.Activities;
using FluidBar.Core.Presentation;

namespace FluidBar.Ui.Tests;

/// <summary>Karaoke sweep plumbing: platform snapshot → presenter → island models for pill and page.</summary>
public sealed class KaraokePlumbingTests
{
    private static IslandViewModel MediaIsland() => new()
    {
        StateKind = IslandStateKind.Compact,
        Page = PageRef.Media,
        Primary = new ActivityViewModel
        {
            Key = new ActivityKey(ActivityKind.Media, "test:media"),
            Kind = ActivityKind.Media,
            Icon = ActivityIconKind.Media,
            Accent = AccentRule.Artwork,
            KindLabelKey = "activity.media",
            Page = PageRef.Media,
            PriorityRank = 1,
            Title = "寡",
            Subtitle = "一颗狼星",
            IsActive = true,
        },
    };

    private static IslandContent Content(double highlight) => new()
    {
        Media = new MediaSessionData
        {
            Title = "寡",
            Artist = "一颗狼星",
            LyricLine = "山后南北走",
            NextLyricLine = "你丢的我煨熟",
            LyricHighlight = highlight,
            IsPlaying = true,
            ProgressPercent = 50,
        },
    };

    [Fact]
    public void Karaoke_sweep_survives_the_activity_projection()
    {
        var media = IslandPresenter.ResolveMedia(Content(0.42), MediaIsland());

        Assert.NotNull(media);
        Assert.Equal(0.42, media!.LyricHighlight, 3);
    }

    [Fact]
    public Task Karaoke_sweep_reaches_the_pill_and_the_page_models()
    {
        return AvaloniaTest.Run(() =>
        {
        var localizer = TestFixtures.CreateLocalizer("en-US");
        var content = Content(0.42);
        var island = MediaIsland();

        var resolved = IslandPresenter.ResolveMedia(content, island);
        var pages = SkiaIslandStateBuilder.BuildPages(content, island, localizer);
        Assert.Equal("山后南北走", pages.Media.LyricLine);
        Assert.Equal(0.42, pages.Media.LyricHighlight, 3);

        var compact = SkiaIslandStateBuilder.BuildCompact(island, content, localizer, peeked: false, compactLyrics: true);
        Assert.Equal(0.42, compact.Model.LyricHighlight, 3);
        Assert.Equal("山后南北走", compact.Model.LyricLine);
        });
    }
}

