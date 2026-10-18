using FluidBar.Core.Activities;
using FluidBar.Core.Presentation;

namespace FluidBar.Core.Tests;

public class PresentationTests
{
    private static ActivityRecord Record(ActivityKind kind, ActivityPayload payload, int rank = 0)
        => ActivityRecord.Create(new ActivityKey(kind, "test"), payload, 1, TestSupport.TestData.Now);

    [Fact]
    public void IconFor_MapsEveryActivityKind()
    {
        Assert.Equal(ActivityIconKind.Clock, ActivityPresenter.IconFor(ActivityKind.Idle));
        Assert.Equal(ActivityIconKind.Media, ActivityPresenter.IconFor(ActivityKind.Media));
        Assert.Equal(ActivityIconKind.Clipboard, ActivityPresenter.IconFor(ActivityKind.Clipboard));
        Assert.Equal(ActivityIconKind.Notification, ActivityPresenter.IconFor(ActivityKind.Notification));
        Assert.Equal(ActivityIconKind.Volume, ActivityPresenter.IconFor(ActivityKind.Volume));
        Assert.Equal(ActivityIconKind.Brightness, ActivityPresenter.IconFor(ActivityKind.Brightness));
        Assert.Equal(ActivityIconKind.Battery, ActivityPresenter.IconFor(ActivityKind.BatteryLow));
        Assert.Equal(ActivityIconKind.Timer, ActivityPresenter.IconFor(ActivityKind.Timer));
        Assert.Equal(ActivityIconKind.LockKeys, ActivityPresenter.IconFor(ActivityKind.LockKeys));
        Assert.Equal(ActivityIconKind.Device, ActivityPresenter.IconFor(ActivityKind.Device));
        Assert.Equal(ActivityIconKind.Bluetooth, ActivityPresenter.IconFor(ActivityKind.Bluetooth));
        Assert.Equal(ActivityIconKind.Focus, ActivityPresenter.IconFor(ActivityKind.Focus));
        Assert.Equal(ActivityIconKind.Progress, ActivityPresenter.IconFor(ActivityKind.Progress));
        Assert.Equal(ActivityIconKind.Search, ActivityPresenter.IconFor(ActivityKind.Lookup));
        Assert.Equal(ActivityIconKind.Agent, ActivityPresenter.IconFor(ActivityKind.Agent));
    }

    [Fact]
    public void AccentFor_UsesStatusRules()
    {
        Assert.Equal(AccentRule.Artwork, ActivityPresenter.AccentFor(ActivityKind.Media));
        Assert.Equal(AccentRule.Danger, ActivityPresenter.AccentFor(ActivityKind.BatteryLow));
        Assert.Equal(AccentRule.Danger, ActivityPresenter.AccentFor(ActivityKind.Agent, new ActivityPayload { IsActive = false }));
        Assert.Equal(AccentRule.Info, ActivityPresenter.AccentFor(ActivityKind.Agent, new ActivityPayload { IsActive = true }));
        Assert.Equal(AccentRule.Info, ActivityPresenter.AccentFor(ActivityKind.Notification));
        Assert.Equal(AccentRule.SystemAccent, ActivityPresenter.AccentFor(ActivityKind.Progress));
        Assert.Equal(
            AccentRule.Artwork,
            ActivityPresenter.AccentFor(ActivityKind.Media, new ActivityPayload { AccentColorHex = "#FF0000" }));
    }

    [Fact]
    public void Present_MapsTextSlotsProgressAndWave()
    {
        var payload = new ActivityPayload
        {
            Title = "Track",
            Subtitle = "Artist",
            Body = "Lyric",
            Badge = "+2",
            ProgressPercent = 42,
            IsActive = true,
            ShowsAudioWave = true,
            AlbumArtPath = "art.png",
        };
        var view = ActivityPresenter.Present(Record(ActivityKind.Media, payload));

        Assert.Equal("Track", view.Title);
        Assert.Equal("Artist", view.Subtitle);
        Assert.Equal("Lyric", view.Body);
        Assert.Equal("+2", view.Badge);
        Assert.Equal(42, view.ProgressPercent);
        Assert.True(view.ShowsWave);
        Assert.True(view.IsActive);
        Assert.Equal(IslandPage.Media, view.Page);
        Assert.Equal(ActivityIconKind.Media, view.Icon);
        Assert.Equal("Activity.Media", view.KindLabelKey);
        Assert.False(view.IsGhost);
        Assert.Equal(0, view.StackIndex);
    }

    [Fact]
    public void Present_DerivesProgressFromPlaybackTicks()
    {
        var payload = new ActivityPayload { Title = "T", PositionTicks = 50, EndTicks = 200, IsActive = true };
        var view = ActivityPresenter.Present(Record(ActivityKind.Media, payload));
        Assert.Equal(25, view.ProgressPercent);
    }

    [Fact]
    public void Present_ClampsOutOfRangeProgress()
    {
        var payload = new ActivityPayload { Title = "T", ProgressPercent = 150 };
        Assert.Equal(100, ActivityPresenter.Present(Record(ActivityKind.Progress, payload)).ProgressPercent);
        Assert.Equal(0, ActivityPresenter.Present(Record(ActivityKind.Progress, payload with { ProgressPercent = -20 })).ProgressPercent);
    }

    [Fact]
    public void Present_WaveOnlyForActiveMedia()
    {
        Assert.True(ActivityPresenter.ShowsWave(ActivityKind.Media, new ActivityPayload { IsActive = true }));
        Assert.False(ActivityPresenter.ShowsWave(ActivityKind.Media, new ActivityPayload { IsActive = false }));
        Assert.False(ActivityPresenter.ShowsWave(ActivityKind.Notification, new ActivityPayload { IsActive = true }));
    }

    [Fact]
    public void PresentHud_LevelsAndLabels()
    {
        var loud = ActivityPresenter.PresentHud(Record(ActivityKind.Volume, new ActivityPayload { Level = 80 }));
        Assert.NotNull(loud);
        Assert.Equal("Hud.Volume", loud!.LabelKey);
        Assert.Equal(3, loud.IconLevel);
        Assert.Equal(80, loud.Value);
        Assert.Equal("80", loud.ValueText);

        var medium = ActivityPresenter.PresentHud(Record(ActivityKind.Volume, new ActivityPayload { Level = 62 }));
        Assert.Equal(2, medium!.IconLevel);

        var muted = ActivityPresenter.PresentHud(Record(ActivityKind.Volume, new ActivityPayload { Level = 40, IsActive = false }));
        Assert.True(muted!.IsMuted);
        Assert.Equal(0, muted.IconLevel);

        var brightness = ActivityPresenter.PresentHud(Record(ActivityKind.Brightness, new ActivityPayload { Level = 10 }));
        Assert.Equal("Hud.Brightness", brightness!.LabelKey);
        Assert.Equal(1, brightness.IconLevel);

        Assert.Null(ActivityPresenter.PresentHud(Record(ActivityKind.Media, new ActivityPayload())));
    }

    [Fact]
    public void PresentIsland_BuildsPrimaryGhostsAndOverflow()
    {
        var scheduler = new ActivityScheduler();
        var state = ActivitySchedulerState.Empty;
        state = scheduler.Update(state, Events.IslandEvent.New(
            new ActivityKey(ActivityKind.Notification, "notif"), new ActivityPayload { Title = "n" }, 1), TestSupport.TestData.Now).State;
        state = scheduler.Update(state, Events.IslandEvent.New(
            new ActivityKey(ActivityKind.Media, "media"), new ActivityPayload { Title = "m" }, 1), TestSupport.TestData.Now).State;
        var island = IslandState.Hidden with { Kind = IslandStateKind.Compact, Page = IslandPage.Media };

        var view = ActivityPresenter.PresentIsland(state, island);

        Assert.NotNull(view.Primary);
        Assert.Equal(ActivityKind.Notification, view.Primary!.Kind);
        Assert.Single(view.Ghosts);
        Assert.True(view.Ghosts[0].IsGhost);
        Assert.Equal(1, view.Ghosts[0].StackIndex);
        Assert.Equal(0, view.OverflowCount);
        Assert.Equal(2, view.ActivityCount);
        Assert.Equal(IslandStateKind.Compact, view.StateKind);
        Assert.Equal(IslandPage.Media, view.Page);
    }

    [Fact]
    public void PresentIsland_IdleWhenNoActivities()
    {
        var view = ActivityPresenter.PresentIsland(ActivitySchedulerState.Empty, IslandState.Hidden with { Kind = IslandStateKind.Compact });
        Assert.True(view.IsIdle);
        Assert.Null(view.Primary);
        Assert.Null(view.Hud);
        Assert.Empty(view.Ghosts);
    }

    [Fact]
    public void PresentIsland_ExposesHudForVolumePrimary()
    {
        var scheduler = new ActivityScheduler();
        var payload = new ActivityPayload { Title = "Volume", Level = 33 };
        var state = scheduler.Update(
            ActivitySchedulerState.Empty,
            Events.IslandEvent.New(new ActivityKey(ActivityKind.Volume, "system:volume"), payload, 1),
            TestSupport.TestData.Now).State;

        var view = ActivityPresenter.PresentIsland(state, IslandState.Hidden with { Kind = IslandStateKind.Compact });

        Assert.NotNull(view.Hud);
        Assert.Equal(33, view.Hud!.Value);
        Assert.True(view.Primary!.IsHud);
    }

    [Fact]
    public void KindLabelKeys_AreStable()
    {
        Assert.Equal("Activity.Notification", ActivityPresenter.KindLabelKeyFor(ActivityKind.Notification));
        Assert.Equal("Activity.BatteryLow", ActivityPresenter.KindLabelKeyFor(ActivityKind.BatteryLow));
        Assert.Equal("Hud.Volume", ActivityPresenter.PresentHud(
            Record(ActivityKind.Volume, new ActivityPayload()))!.LabelKey);
    }
}
