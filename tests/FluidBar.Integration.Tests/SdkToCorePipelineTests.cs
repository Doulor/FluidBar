using FluidBar.Core.Activities;
using FluidBar.Core.Presentation;
using FluidBar.Integration.Tests.TestSupport;

namespace FluidBar.Integration.Tests;

/// <summary>
/// Seam 2: captured SDK events → test App adapter → Core EventHub → ActivityScheduler →
/// IslandStateMachine → ActivityPresenter. Asserts priority/replacement/coalescing/stack/expiry and
/// the final view models the UI renders.
/// </summary>
public class SdkToCorePipelineTests
{
    [Fact]
    public void Media_event_produces_media_view_model_with_progress_and_wave()
    {
        using var pipeline = new CorePipelineHarness();
        pipeline.Publish(SdkEvents.Media(progress: 0.125));

        var view = pipeline.View;
        Assert.Equal(IslandStateKind.Compact, view.StateKind);
        Assert.NotNull(view.Primary);
        Assert.Equal(ActivityKind.Media, view.Primary!.Kind);
        Assert.Equal("Midnight City", view.Primary.Title);
        Assert.Equal("M83", view.Primary.Subtitle);
        Assert.Equal(13, view.Primary.ProgressPercent); // 12.5 % rounds away from zero
        Assert.True(view.Primary.ShowsWave);
        Assert.Equal(PageRef.Media, view.Primary.Page);
        Assert.Equal(ActivityIconKind.Media, view.Primary.Icon);
        Assert.Empty(view.Ghosts);
    }

    [Fact]
    public void Notification_outranks_media_and_media_becomes_ghost()
    {
        using var pipeline = new CorePipelineHarness();
        pipeline.PublishBatch([SdkEvents.Media(), SdkEvents.Notification(code: "482913")]);

        var view = pipeline.View;
        Assert.Equal(ActivityKind.Notification, view.Primary!.Kind);
        Assert.Equal("Build finished", view.Primary.Title);
        Assert.Equal("482913", view.Primary.VerificationCode);
        Assert.Equal(2, view.ActivityCount);
        Assert.Single(view.Ghosts);
        Assert.Equal(ActivityKind.Media, view.Ghosts[0].Kind);
        Assert.True(view.Ghosts[0].IsGhost);
    }

    [Fact]
    public void Notification_outranks_clipboard_on_the_primary_slot()
    {
        // Policy 2026-10-23 (commit a352e7a): notifications own the primary island, so a clipboard event
        // arriving afterwards becomes a ghost instead of pre-empting the notification.
        using var pipeline = new CorePipelineHarness();
        pipeline.Publish(SdkEvents.Notification());
        Assert.Equal(ActivityKind.Notification, pipeline.View.Primary!.Kind);

        pipeline.Publish(SdkEvents.Clipboard());

        Assert.Equal(ActivityKind.Notification, pipeline.View.Primary!.Kind);
        Assert.Equal(2, pipeline.SchedulerState.ActivityCount);
        Assert.Contains(pipeline.View.Ghosts, ghost => ghost.Kind == ActivityKind.Clipboard);
    }

    [Fact]
    public void Same_key_events_replace_in_place_without_new_layer()
    {
        using var pipeline = new CorePipelineHarness();
        pipeline.Publish(SdkEvents.Notification(title: "First"));
        pipeline.Publish(SdkEvents.Notification(title: "Second"));

        Assert.Equal("Second", pipeline.View.Primary!.Title);
        Assert.Equal(1, pipeline.SchedulerState.ActivityCount);
        Assert.Equal(ActivityUpdateKind.ReplacedInPlace, pipeline.LastUpdate.Kind);
    }

    [Fact]
    public void Incremental_media_batch_is_coalesced_by_the_hub()
    {
        using var pipeline = new CorePipelineHarness();
        var events = new[]
        {
            SdkEvents.Media(progress: 0.10),
            SdkEvents.Media(progress: 0.20),
            SdkEvents.Media(progress: 0.30),
            SdkEvents.Media(progress: 0.40),
            SdkEvents.Media(progress: 0.50),
        };

        pipeline.PublishBatch(events);

        Assert.True(pipeline.Metrics.Coalesced >= 3, $"coalesced={pipeline.Metrics.Coalesced}");
        Assert.Equal(1, pipeline.SchedulerState.ActivityCount);
        Assert.Equal(50, pipeline.View.Primary!.ProgressPercent);
        Assert.Equal(ActivityUpdateKind.ContentUpdated, pipeline.LastUpdate.Kind);
    }

    [Fact]
    public void Transient_activity_expires_and_media_remains_primary()
    {
        using var pipeline = new CorePipelineHarness();
        pipeline.PublishBatch([SdkEvents.Media(), SdkEvents.Notification()]);
        Assert.Equal(ActivityKind.Notification, pipeline.View.Primary!.Kind);

        pipeline.Advance(TimeSpan.FromSeconds(6));

        Assert.Equal(ActivityKind.Media, pipeline.View.Primary!.Kind);
        Assert.Equal(1, pipeline.SchedulerState.ActivityCount);
        Assert.Empty(pipeline.View.Ghosts);
    }

    [Fact]
    public void Expiry_of_all_transient_activities_returns_to_idle()
    {
        using var pipeline = new CorePipelineHarness();
        pipeline.Publish(SdkEvents.Notification());

        pipeline.Advance(TimeSpan.FromSeconds(6));

        Assert.Null(pipeline.View.Primary);
        Assert.True(pipeline.View.IsIdle);
        Assert.Equal(0, pipeline.SchedulerState.ActivityCount);
    }

    [Fact]
    public void Agent_event_maps_to_agent_view_model()
    {
        using var pipeline = new CorePipelineHarness();
        pipeline.Publish(SdkEvents.Agent());

        var primary = pipeline.View.Primary!;
        Assert.Equal(ActivityKind.Agent, primary.Kind);
        Assert.Equal(ActivityIconKind.Agent, primary.Icon);
        Assert.Equal("claude", primary.Subtitle);
        Assert.Equal("completed", primary.Badge);
        Assert.Equal(PageRef.Agent, primary.Page);
    }

    [Fact]
    public void Adapter_maps_builtin_plugin_ids_to_stable_core_source_tokens()
    {
        Assert.Equal(
            (ActivityKind.Media, "media:gsmtc"),
            SdkCoreEventAdapter.MapActivity(SdkEvents.Media()));
        Assert.Equal(
            (ActivityKind.Clipboard, "clipboard:host"),
            SdkCoreEventAdapter.MapActivity(SdkEvents.Clipboard()));
        Assert.Equal(
            (ActivityKind.Notification, "notif:toast"),
            SdkCoreEventAdapter.MapActivity(SdkEvents.Notification()));
        Assert.Equal(
            (ActivityKind.Agent, "agent:claude"),
            SdkCoreEventAdapter.MapActivity(SdkEvents.Agent()));
    }

    [Fact]
    public void Adapter_maps_device_source_fields_to_usb_and_bluetooth_kinds()
    {
        var usb = new FluidBar.Sdk.IslandEvent
        {
            Source = FluidBar.Plugins.Builtin.BuiltinIds.DevicesPlugin,
            Title = "USB 046D:C52B",
            Content = "system:usb:connected",
            IconKind = "usb",
            Kind = FluidBar.Sdk.IslandEventKind.Status,
            Payload = new FluidBar.Sdk.IslandEventPayload
            {
                Fields = new Dictionary<string, string?>(StringComparer.Ordinal) { ["source"] = "system:usb" },
            },
        };
        var bluetooth = new FluidBar.Sdk.IslandEvent
        {
            Source = FluidBar.Plugins.Builtin.BuiltinIds.DevicesPlugin,
            Title = "Headphones",
            Content = "system:bluetooth:connected",
            IconKind = "bluetooth",
            Kind = FluidBar.Sdk.IslandEventKind.Status,
            Payload = new FluidBar.Sdk.IslandEventPayload
            {
                Fields = new Dictionary<string, string?>(StringComparer.Ordinal) { ["source"] = "system:bluetooth" },
            },
        };

        Assert.Equal((ActivityKind.Device, "system:usb"), SdkCoreEventAdapter.MapActivity(usb));
        Assert.Equal((ActivityKind.Bluetooth, "system:bluetooth"), SdkCoreEventAdapter.MapActivity(bluetooth));

        using var pipeline = new CorePipelineHarness();
        pipeline.Publish(usb);
        Assert.Equal(ActivityKind.Device, pipeline.View.Primary!.Kind);
    }

    [Fact]
    public void Generic_plugin_events_are_classified_by_icon_token_with_plugin_scoped_source()
    {
        var generic = new FluidBar.Sdk.IslandEvent
        {
            Source = "community-plugin",
            Title = "Build artifacts ready",
            Content = "3 files",
            IconKind = "progress",
            Kind = FluidBar.Sdk.IslandEventKind.Progress,
        };

        var (kind, source) = SdkCoreEventAdapter.MapActivity(generic);
        Assert.Equal(ActivityKind.Progress, kind);
        Assert.Equal("plugin:community-plugin", source);
    }

    [Fact]
    public void Full_pipeline_state_flow_matches_expected_view_model_sequence()
    {
        using var pipeline = new CorePipelineHarness();
        var observed = new List<string>();

        pipeline.Publish(SdkEvents.Media());
        observed.Add(pipeline.View.Primary?.Kind.ToString() ?? "idle");

        pipeline.Publish(SdkEvents.Notification(title: "Build finished", code: "482913"));
        observed.Add(pipeline.View.Primary?.Kind.ToString() ?? "idle");

        pipeline.Publish(SdkEvents.Clipboard());
        observed.Add(pipeline.View.Primary?.Kind.ToString() ?? "idle");

        pipeline.Advance(TimeSpan.FromSeconds(6));
        observed.Add(pipeline.View.Primary?.Kind.ToString() ?? "idle");

        Assert.Equal(["Media", "Notification", "Notification", "Media"], observed);
        Assert.True(pipeline.Updates.Count >= 4);
    }
}

