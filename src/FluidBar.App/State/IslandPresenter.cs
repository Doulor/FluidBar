using System.Collections.Immutable;
using FluidBar.App.Controls;
using FluidBar.App.Island;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.Theme;
using FluidBar.Core.Activities;
using FluidBar.Core.Presentation;

namespace FluidBar.App.State;

/// <summary>
/// Everything an expanded page draws with: the non-activity content snapshot, the activity view model,
/// live settings and the active localizer. Pure data; no Avalonia types (pages draw it themselves).
/// </summary>
public sealed record ExpandedPageContext
{
    /// <summary>Non-activity content (clock, battery, lists).</summary>
    public required IslandContent Content { get; init; }

    /// <summary>Activity view model from the Core scheduler.</summary>
    public required IslandViewModel Island { get; init; }

    /// <summary>Current settings snapshot.</summary>
    public required AppSettings Settings { get; init; }

    /// <summary>Current localizer.</summary>
    public required ILocalizer Localizer { get; init; }

    /// <summary>Selected rail page.</summary>
    public required PageRef SelectedPage { get; init; }
}

/// <summary>
/// Maps Core state/scheduler snapshots into the UI's compact content models. Business decisions
/// (priority, expiry, which activity is primary) stay in Core; this is presentation only.
/// </summary>
public static class IslandPresenter
{
    /// <summary>Builds the compact surface model from the island view model and content snapshot.</summary>
    /// <param name="island">Activity view model snapshot.</param>
    /// <param name="content">Non-activity content.</param>
    /// <param name="localizer">Localizer for labels.</param>
    /// <param name="isPeeked">Whether the compact pill is currently in the hover peek form.</param>
    public static CompactContentModel ToCompact(
        IslandViewModel island,
        IslandContent content,
        ILocalizer localizer,
        bool isPeeked)
    {
        ArgumentNullException.ThrowIfNull(island);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(localizer);

        var primary = island.Primary;
        if (primary is null)
        {
            return new CompactContentModel
            {
                Style = CompactStyle.Idle,
                ClockText = content.Now.ToString("HH:mm", localizer.CurrentCulture),
                // "M月d日" overflowed the idle pill and ellipsised to "10月..."; keep the compact form.
                DateText = content.Now.ToString("MMM d", localizer.CurrentCulture),
                BatteryPercent = content.BatteryPercent,
                IsCharging = content.IsCharging,
                Icon = IconKind.Clock,
                KindLabelKey = LocKeys.Activity.Idle,
                IsPeeked = isPeeked,
            };
        }

        var style = StyleFor(primary.Kind);
        var model = new CompactContentModel
        {
            Style = style,
            Kind = primary.Kind,
            Title = primary.Title,
            Subtitle = primary.Subtitle,
            Body = primary.Body,
            Badge = primary.Badge,
            SourceName = primary.SourceName,
            Icon = MapIcon(primary.Icon),
            ProgressPercent = primary.ProgressPercent,
            ProgressIndeterminate = primary.ProgressIndeterminate,
            IsActive = primary.IsActive,
            ShowsWave = primary.ShowsWave,
            VerificationCode = primary.VerificationCode,
            AccentHex = ResolveAccent(primary),
            TimerText = primary.Title,
            IsPeeked = isPeeked,
            KindLabelKey = primary.KindLabelKey,
        };

        return primary.Kind switch
        {
            ActivityKind.Media => model with
            {
                Title = string.IsNullOrEmpty(primary.Title) ? localizer.Get(LocKeys.Media.UnknownTitle) : primary.Title,
                Subtitle = string.IsNullOrEmpty(primary.Subtitle) ? localizer.Get(LocKeys.Media.UnknownArtist) : primary.Subtitle,
                Badge = primary.Subtitle,
            },
            ActivityKind.Volume or ActivityKind.Brightness => model with
            {
                Level = island.Hud?.Value ?? primary.ProgressPercent,
                IsMuted = island.Hud?.IsMuted ?? false,
                Title = localizer.Get(primary.Kind == ActivityKind.Volume ? LocKeys.Hud.Volume : LocKeys.Hud.Brightness),
            },
            ActivityKind.Agent => model with
            {
                // The publishing plugin already resolved the priority chain (action → session name →
                // status description), so Title is that text. An empty title means "status only": show the
                // localized status in the main slot instead of falling back to the brand/source name.
                Title = string.IsNullOrEmpty(primary.Title)
                    ? localizer.Get(primary.IsActive ? LocKeys.Agent.Running : LocKeys.Agent.Completed)
                    : primary.Title,
                StatusText = localizer.Get(primary.IsActive ? LocKeys.Agent.Running : LocKeys.Agent.Completed),
                Tone = primary.IsActive ? StatusTone.Info : StatusTone.Success,
            },
            ActivityKind.BatteryLow => model with
            {
                Level = content.BatteryPercent ?? 0,
                IsCharging = content.IsCharging,
                StatusText = localizer.Get(content.IsCharging ? LocKeys.Common.Charging : LocKeys.Battery.Low),
                Tone = content.IsCharging ? StatusTone.Success : StatusTone.Danger,
            },
            ActivityKind.Clipboard => model with
            {
                Body = string.IsNullOrEmpty(primary.Body) ? primary.Title : primary.Body,
                Badge = localizer.Get(LocKeys.Common.Copied),
            },
            ActivityKind.Timer => model with
            {
                StatusText = localizer.Get(LocKeys.Timer.Pomodoro),
                Tone = StatusTone.Info,
            },
            ActivityKind.Focus => model with
            {
                StatusText = localizer.Get(LocKeys.Page.Home.FocusTime),
                Tone = StatusTone.Info,
            },
            ActivityKind.Progress => model with
            {
                StatusText = primary.ProgressPercent is { } progress ? $"{progress}%" : null,
                Tone = StatusTone.Info,
            },
            ActivityKind.LockKeys or ActivityKind.Device or ActivityKind.Bluetooth or ActivityKind.Lookup => model with
            {
                Subtitle = primary.Subtitle,
                StatusText = primary.Badge,
                Tone = StatusTone.Neutral,
            },
            _ => model,
        };
    }

    /// <summary>Derives the expanded page from the state.</summary>
    public static PageRef PageFor(IslandViewModel island)
    {
        ArgumentNullException.ThrowIfNull(island);
        return island.Page;
    }

    /// <summary>Maps an activity kind to the compact surface style.</summary>
    public static CompactStyle StyleFor(ActivityKind kind) => kind switch
    {
        ActivityKind.Media => CompactStyle.Media,
        ActivityKind.Volume or ActivityKind.Brightness => CompactStyle.Hud,
        ActivityKind.Notification => CompactStyle.Notification,
        ActivityKind.Clipboard => CompactStyle.Clipboard,
        ActivityKind.Agent => CompactStyle.Agent,
        ActivityKind.Timer => CompactStyle.Timer,
        ActivityKind.BatteryLow => CompactStyle.Battery,
        ActivityKind.LockKeys => CompactStyle.LockKeys,
        ActivityKind.Device => CompactStyle.Device,
        ActivityKind.Bluetooth => CompactStyle.Bluetooth,
        ActivityKind.Focus => CompactStyle.Focus,
        ActivityKind.Progress => CompactStyle.Progress,
        ActivityKind.Lookup => CompactStyle.Lookup,
        _ => CompactStyle.Idle,
    };

    /// <summary>Resolves the accent hex for a compact surface.</summary>
    public static string? ResolveAccent(ActivityViewModel primary) => primary.Accent switch
    {
        AccentRule.Artwork when primary.AccentColorHex is { Length: > 0 } => primary.AccentColorHex,
        AccentRule.Danger => "#FF453A",
        AccentRule.Info => "#0A84FF",
        AccentRule.Success => "#34C759",
        AccentRule.Warning => "#FF9500",
        _ => primary.AccentColorHex,
    };

    /// <summary>Snapshot of the media page data from activities/content (activity wins).</summary>
    public static MediaSessionData? ResolveMedia(IslandContent content, IslandViewModel island)
    {
        var primary = island.Primary;
        var fallback = content.Media;
        if (primary is null || primary.Kind != ActivityKind.Media)
        {
            return fallback;
        }

        var progress = primary.ProgressPercent ?? fallback?.ProgressPercent ?? 0;
        var durationTicks = primary.EndTicks;
        var positionTicks = primary.PositionTicks;
        var duration = durationTicks > 0 ? TimeSpan.FromTicks(durationTicks) : TimeSpan.Zero;
        var position = positionTicks > 0 ? TimeSpan.FromTicks(positionTicks) : TimeSpan.Zero;
        return new MediaSessionData
        {
            Title = string.IsNullOrEmpty(primary.Title) ? fallback?.Title ?? string.Empty : primary.Title,
            Artist = string.IsNullOrEmpty(primary.Subtitle) ? fallback?.Artist ?? string.Empty : primary.Subtitle,
            Album = fallback?.Album ?? string.Empty,
            Source = primary.SourceName ?? fallback?.Source ?? string.Empty,
            ProgressPercent = progress,
            PositionText = fallback?.PositionText ?? FormatTime(position),
            DurationText = fallback?.DurationText ?? FormatTime(duration),
            HasTimeline = fallback?.HasTimeline ?? (duration > TimeSpan.Zero),
            CanSeek = fallback?.CanSeek ?? (duration > TimeSpan.Zero),
            // The platform snapshot wins over the activity flag: it is the only side that carries the
            // optimistic flip of a just-tapped play/pause button (the activity only says "still on").
            IsPlaying = fallback?.IsPlaying ?? primary.IsActive,
            AccentHex = primary.AccentColorHex ?? fallback?.AccentHex,
            Artwork = fallback?.Artwork,
            ArtworkKey = fallback?.ArtworkKey,
            LyricLine = primary.LyricLine ?? fallback?.LyricLine,
            NextLyricLine = primary.SecondaryLyricLine ?? fallback?.NextLyricLine,
            // The karaoke sweep only exists in the platform snapshot (the activity carries no word timings).
            LyricHighlight = fallback?.LyricHighlight ?? -1,
            Karaoke = fallback?.Karaoke,
            PositionMs = fallback?.PositionMs ?? 0,
            DurationMs = fallback?.DurationMs is > 0 ? fallback.DurationMs : duration.TotalMilliseconds,
            TrackIndex = fallback?.TrackIndex ?? 0,
            TrackCount = fallback?.TrackCount ?? 0,
        };
    }

    private static string FormatTime(TimeSpan time) => $"{(int)time.TotalMinutes}:{time.Seconds:00}";

    /// <summary>Immutable page list snapshot for the expanded panel.</summary>
    public static ImmutableArray<NotificationItemData> Notifications(IslandContent content) => content.Notifications;
    /// <summary>Maps Core activity icon identities to the App vector icon set.</summary>
    public static IconKind MapIcon(ActivityIconKind icon) => icon switch
    {
        ActivityIconKind.Clock => IconKind.Clock,
        ActivityIconKind.Media => IconKind.Media,
        ActivityIconKind.Clipboard => IconKind.Clipboard,
        ActivityIconKind.Notification => IconKind.Bell,
        ActivityIconKind.Volume => IconKind.VolumeMedium,
        ActivityIconKind.Brightness => IconKind.BrightnessHigh,
        ActivityIconKind.Battery => IconKind.Battery,
        ActivityIconKind.Timer => IconKind.Clock,
        ActivityIconKind.LockKeys => IconKind.Lock,
        ActivityIconKind.Device => IconKind.Usb,
        ActivityIconKind.Bluetooth => IconKind.Bluetooth,
        ActivityIconKind.Focus => IconKind.Focus,
        ActivityIconKind.Progress => IconKind.Download,
        ActivityIconKind.Search => IconKind.Search,
        ActivityIconKind.Agent => IconKind.Agent,
        _ => IconKind.Clock,
    };
}
