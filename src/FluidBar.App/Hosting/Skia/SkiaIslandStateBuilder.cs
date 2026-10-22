using System.Globalization;
using System.Text;
using FluidBar.App.Island;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.State;
using FluidBar.Core.Activities;
using FluidBar.Core.Presentation;
using FluidBar.Island;
using AppIcon = FluidBar.App.Controls.IconKind;
using AppTone = FluidBar.App.Island.StatusTone;
using IslandIcon = FluidBar.Island.IconKind;

namespace FluidBar.App.Hosting.Skia;

/// <summary>
/// Maps Core state + scheduler snapshots + App content onto the renderer's immutable models.
/// Pure and UI-framework-free: no Avalonia, no HWND, no clock. The controller caches the results and
/// only rebuilds them when the structural/dynamic signature changes, so the per-frame path never
/// re-creates strings or page models.
/// </summary>
public static class SkiaIslandStateBuilder
{
    /// <summary>Builds the compact activity (idle clock pill when no activity is live).</summary>
    /// <param name="island">Activity view model from the Core presenter.</param>
    /// <param name="content">Non-activity content snapshot.</param>
    /// <param name="localizer">Localizer for labels.</param>
    /// <param name="peeked">Whether the pill is in the hover-peek form.</param>
    /// <param name="island">Activity view model from the Core presenter.</param>
    /// <param name="content">Non-activity content snapshot.</param>
    /// <param name="localizer">Localizer for labels.</param>
    /// <param name="peeked">Whether the pill is in the hover-peek form.</param>
    /// <param name="compactLyrics">
    /// Media setting "lyrics in the pill": when true and a lyric line is known, the compact pill swaps the
    /// title/artist for the live lyric (cover + lyric + wave). A missing lyric keeps the title form.
    /// </param>
    public static CompactActivity BuildCompact(
        IslandViewModel island,
        IslandContent content,
        ILocalizer localizer,
        bool peeked,
        bool compactLyrics = false)
    {
        ArgumentNullException.ThrowIfNull(island);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(localizer);

        var kind = island.Primary?.Kind ?? ActivityKind.Idle;

        var model = IslandPresenter.ToCompact(island, content, localizer, peeked);
        // The activity payload carries no artwork and a possibly stale progress; the media snapshot
        // has both, so the compact pill mirrors what the media page shows.
        var media = kind == ActivityKind.Media ? IslandPresenter.ResolveMedia(content, island) : null;
        var progress = kind == ActivityKind.Media && media is not null
            ? (media.HasTimeline ? media.ProgressPercent : -1)
            : model.ProgressPercent ?? -1;
        var unread = kind == ActivityKind.Notification ? Math.Max(0, content.UnreadCount) : 0;
        var showsAudioWave = kind == ActivityKind.Notification
            && (content.Media?.IsPlaying == true
                || island.Ghosts.Any(ghost => ghost.Kind == ActivityKind.Media && ghost.IsActive));
        var isNotification = kind == ActivityKind.Notification;
        var notification = isNotification
            ? FindNotification(content, model.SourceName, model.Title)
                ?? (content.Notifications.IsDefaultOrEmpty ? null : content.Notifications[0])
            : null;
        var notificationTime = isNotification
            ? notification?.TimeText ?? localizer.Get(LocKeys.Notifications.JustNow)
            : null;
        // Lyric mode only replaces the two text rows; artwork/progress/wave stay exactly the same, and an
        // empty lyric falls back to the title form (never a blank line).
        var lyricLine = compactLyrics && kind == ActivityKind.Media && media is not null
            ? NullIfEmpty(media.LyricLine)
            : null;
        return new CompactActivity(kind, new CompactModel
        {
            ShowLyrics = lyricLine is not null,
            LyricLine = lyricLine,
            Title = NullIfEmpty(model.Title),
            Subtitle = NullIfEmpty(model.Subtitle),
            Body = NullIfEmpty(model.Body),
            Badge = NullIfEmpty(model.Badge),
            AppName = NullIfEmpty(model.SourceName),
            ClockText = model.ClockText,
            DateText = model.DateText,
            StatusText = NullIfEmpty(model.StatusText),
            DurationText = NullIfEmpty(model.RemainingText),
            TimerText = NullIfEmpty(model.TimerText),
            VerificationCode = NullIfEmpty(model.VerificationCode),
            Level = model.Level,
            BatteryPercent = model.BatteryPercent,
            UnreadCount = unread,
            ShowsAudioWave = showsAudioWave,
            IsActive = model.IsActive,
            IsMuted = model.IsMuted,
            IsCharging = model.IsCharging,
            ProgressPercent = progress,
            // The cover's own accent drives the compact wave/highlight too; without a cover the accent
            // rule from the source/theme stays in charge.
            AccentHex = ArtworkAccent.For(media?.ArtworkKey, media?.Artwork) ?? model.AccentHex ?? media?.AccentHex,
            LyricHighlight = media?.LyricHighlight ?? -1,
            Artwork = media?.Artwork,
            ArtworkKey = media?.ArtworkKey,
            AppIconPng = notification?.AppIconPng,
            AppIconKey = notification?.AppIconKey,
            Icon = MapIcon(model.Icon),
            Status = MapTone(model.Tone),
            TimeText = notificationTime,
        });
    }

    /// <summary>Builds the expanded page models from the App content snapshot.</summary>
    /// <param name="content">Non-activity content snapshot.</param>
    /// <param name="island">Activity view model from the Core presenter.</param>
    /// <param name="localizer">Localizer for page titles and labels.</param>
    /// <param name="settings">Live settings (drives the theme quick toggle state); optional for callers without settings.</param>
    public static ExpandedPageContent BuildPages(
        IslandContent content,
        IslandViewModel island,
        ILocalizer localizer,
        AppSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(island);
        ArgumentNullException.ThrowIfNull(localizer);

        var media = IslandPresenter.ResolveMedia(content, island);
        var culture = localizer.CurrentCulture;
        var now = content.Now;
        var artistAlbum = media is null
            ? null
            : string.IsNullOrEmpty(media.Album) ? media.Artist : $"{media.Artist} · {media.Album}";

        var home = new HomePageModel
        {
            ClockText = now.ToString("HH:mm", culture),
            DateText = culture.TwoLetterISOLanguageName == "zh"
                ? now.ToString("M月d日 ddd", culture)
                : now.ToString("ddd, MMM d", culture),
            // The widgets page already receives real weather; the dashboard chip is the same data,
            // not a second source.
            ShowWeather = !string.IsNullOrWhiteSpace(content.WeatherTemp),
            WeatherTemp = content.WeatherTemp,
            WeatherCondition = content.WeatherConditionText,
            Toggles =
            [
                new HomeToggleModel(IslandIcon.Wifi, content.NetworkName ?? "Wi-Fi",
                    content.IsWifiEnabled ?? content.IsOnline),
                new HomeToggleModel(IslandIcon.Bluetooth, localizer.Get(LocKeys.Activity.Bluetooth),
                    content.IsBluetoothEnabled ?? false),
                new HomeToggleModel(
                    IslandIcon.Focus,
                    localizer.Get(LocKeys.Page.Home.FocusTime),
                    content.FocusOn,
                    content.FocusAvailable,
                    content.FocusAvailable ? null : localizer.Get(LocKeys.Common.Disabled)),
                new HomeToggleModel(
                    settings?.Theme == AppTheme.Light ? IslandIcon.Sun : IslandIcon.Moon,
                    localizer.Get(settings is not null && settings.Theme == AppTheme.Light
                        ? LocKeys.Settings.Appearance.Theme.Light
                        : LocKeys.Settings.Appearance.Theme.Dark),
                    settings is null || settings.Theme != AppTheme.Light),
            ],
            NowPlaying = new MediaSummaryModel
            {
                // An empty card looks broken; show the honest empty state instead.
                Title = media?.Title ?? localizer.Get(LocKeys.Media.NoSession),
                Subtitle = artistAlbum,
                ProgressPercent = media?.ProgressPercent ?? 0,
                IsPlaying = media?.IsPlaying ?? false,
                HasTimeline = media?.HasTimeline ?? false,
                AccentHex = media?.AccentHex,
                Artwork = media?.Artwork,
                ArtworkKey = media?.ArtworkKey,
            },
            // Real platform levels; unknown renders an em dash instead of a demo number.
            Volume = content.VolumePercent ?? 0,
            HasVolume = content.VolumePercent is not null,
            Brightness = content.BrightnessPercent ?? 0,
            HasBrightness = content.BrightnessPercent is not null,
            IsMuted = content.IsMuted,
            BatteryPercent = content.BatteryPercent ?? 0,
            IsCharging = content.IsCharging,
            BatteryStatus = content.IsCharging ? localizer.Get(LocKeys.Common.Charging) : null,
        };

        var mediaPage = new MediaPageModel
        {
            HasSession = media is not null,
            Eyebrow = localizer.Get(LocKeys.Media.NowPlaying),
            Title = media?.Title,
            Artist = media?.Artist,
            Album = media?.Album,
            LyricLine = media?.LyricLine,
            NextLyricLine = media?.NextLyricLine,
            LyricHighlight = media?.LyricHighlight ?? -1,
            HasKaraokeSweep = media?.Karaoke is not null,
            VolumePercent = content.VolumePercent,
            HasVolume = content.VolumePercent is not null,
            IsMuted = content.IsMuted,
            NextLyricLabel = localizer.Get(LocKeys.Common.Next),
            PositionText = media?.PositionText,
            DurationText = media?.DurationText,
            ProgressPercent = media?.ProgressPercent ?? 0,
            IsPlaying = media?.IsPlaying ?? false,
            HasTimeline = media?.HasTimeline ?? false,
                CanSeek = media?.CanSeek ?? false,
            AccentHex = ArtworkAccent.For(media?.ArtworkKey, media?.Artwork) ?? media?.AccentHex,
            Artwork = media?.Artwork,
            ArtworkKey = media?.ArtworkKey,
            // The source rarely exposes an endpoint/queue, so the slots carry light localized placeholders
            // instead of staying blank (a blank transport slot read as a broken render).
            QueueCount = media?.TrackCount ?? 0,
            QueueLabel = localizer.Get(LocKeys.Page.Media.QueueLabel),
            OutputPlaceholder = localizer.Get(LocKeys.Page.Media.OutputNone),
            QueuePlaceholder = localizer.Get(LocKeys.Page.Media.QueueNone),
            // Capability bits: Windows has no per-app output-device API and neither GSMTC nor KuGou's
            // accessibility tree exposes a queue, so both slots are inert and say so (greyed by the
            // renderer, refused by the hit test, disabled in the automation tree).
            OutputAvailable = false,
            QueueAvailable = false,
            NoLyricsText = localizer.Get(LocKeys.Page.Media.NoLyrics),
            EmptyTitle = localizer.Get(LocKeys.Media.NoSession),
            EmptyHint = localizer.Get(LocKeys.Media.NoSessionHint),
        };

        var notificationsPage = new NotificationsPageModel
        {
            Items = BuildNotifications(content.Notifications, localizer),
            EmptyTitle = localizer.Get(LocKeys.Notifications.Empty),
            EmptyHint = localizer.Get(LocKeys.Notifications.EmptyHint),
            DismissLabel = localizer.Get(LocKeys.Common.Close),
            CopyCodeLabel = localizer.Get(LocKeys.Notifications.CopyCode),
        };

        var clipboardPage = new ClipboardPageModel
        {
            Items = BuildClipboard(content.Clipboard, localizer),
            PinnedLabel = localizer.Get(LocKeys.Clipboard.Pinned),
            EmptyTitle = localizer.Get(LocKeys.Clipboard.Empty),
            EmptyHint = localizer.Get(LocKeys.Clipboard.EmptyHint),
        };

        var agentPage = new AgentPageModel
        {
            Items = BuildAgents(content.Agents),
            EmptyTitle = localizer.Get(LocKeys.Agent.Empty),
            EmptyHint = localizer.Get(LocKeys.Agent.EmptyHint),
        };

        var widgetsPage = BuildWidgets(content, localizer);

        string[] titles =
        [
            localizer.Get(LocKeys.Page.Home.Title),
            localizer.Get(LocKeys.Page.Media.Title),
            localizer.Get(LocKeys.Page.Notifications.Title),
            localizer.Get(LocKeys.Page.Clipboard.Title),
            localizer.Get(LocKeys.Page.Agent.Title),
            localizer.Get(LocKeys.Page.Widgets.Title),
        ];

        // The transient preview rail paints the short page names (prototype2 .mini-label:
        // 主页/媒体/通知/剪贴板/Agent/小组件), not the long expanded-page titles.
        string[] shortTitles =
        [
            localizer.Get(LocKeys.Rail.Home),
            localizer.Get(LocKeys.Rail.Media),
            localizer.Get(LocKeys.Rail.Notifications),
            localizer.Get(LocKeys.Rail.Clipboard),
            localizer.Get(LocKeys.Rail.Agent),
            localizer.Get(LocKeys.Rail.Widgets),
        ];

        return new ExpandedPageContent
        {
            Home = home,
            Media = mediaPage,
            Notifications = notificationsPage,
            Clipboard = clipboardPage,
            Agent = agentPage,
            Widgets = widgetsPage,
            PageTitles = titles,
            ShortTitles = shortTitles,
        };
    }

    private static NotificationItemModel[] BuildNotifications(
        IReadOnlyList<NotificationItemData> items,
        ILocalizer localizer)
    {
        var models = new NotificationItemModel[items.Count];
        for (var i = 0; i < models.Length; i++)
        {
            var item = items[i];
            var code = NullIfEmpty(item.VerificationCode);
            models[i] = new NotificationItemModel
            {
                AppName = NullIfEmpty(item.Source),
                Title = item.Title,
                Body = item.Body,
                TimeText = item.TimeText,
                VerificationCode = code,
                CodeChipLabel = code is null
                    ? null
                    : localizer.Format(LocKeys.Notifications.VerificationCode, ("code", code)),
            };
        }

        return models;
    }

    private static ClipboardItemModel[] BuildClipboard(IReadOnlyList<ClipboardItemData> items, ILocalizer localizer)
    {
        var models = new ClipboardItemModel[items.Count];
        for (var i = 0; i < models.Length; i++)
        {
            var item = items[i];
            var kind = item.IsImage ? ClipboardContentKind.Image : ClipboardContentKind.Text;
            models[i] = new ClipboardItemModel
            {
                Preview = item.Preview,
                TimeText = item.Meta,
                IsPinned = item.Pinned,
                Kind = kind,
                // The renderer used to fall back to hard-coded English "Text"/"Image" labels, which
                // mixed languages on the zh UI; supply the localized label from the catalog instead.
                TypeLabel = localizer.Get(kind == ClipboardContentKind.Image
                    ? LocKeys.Clipboard.Image
                    : LocKeys.Clipboard.Text),
            };
        }

        return models;
    }

    private static AgentItemModel[] BuildAgents(IReadOnlyList<AgentItemData> items)
    {
        var models = new AgentItemModel[items.Count];
        for (var i = 0; i < models.Length; i++)
        {
            var item = items[i];
            models[i] = new AgentItemModel
            {
                Tool = item.Name,
                Project = item.Project,
                Task = string.IsNullOrWhiteSpace(item.PrimaryText) ? item.Detail : item.PrimaryText,
                StatusText = item.StatusText,
                DurationText = item.TimeText,
                Tone = MapTone(item.Tone),
            };
        }

        return models;
    }

    private static WidgetsPageModel BuildWidgets(IslandContent content, ILocalizer localizer)
    {
        var days = new CalendarDayModel[content.CalendarDays.Length];
        for (var i = 0; i < days.Length; i++)
        {
            var day = content.CalendarDays[i];
            days[i] = new CalendarDayModel(day.Day, day.InMonth, day.IsToday, day.HasEvents);
        }

        var tasks = new WidgetTaskModel[content.Tasks.Length];
        var done = 0;
        for (var i = 0; i < tasks.Length; i++)
        {
            var task = content.Tasks[i];
            tasks[i] = new WidgetTaskModel(task.Title, task.Done);
            if (task.Done)
            {
                done++;
            }
        }

        var stats = new WidgetStatModel[content.Stats.Length];
        for (var i = 0; i < stats.Length; i++)
        {
            var stat = content.Stats[i];
            stats[i] = new WidgetStatModel(stat.Label, stat.Value, (WidgetStatTone)stat.Kind);
        }

        // Weather and tasks have no platform source yet: instead of painting blank cards, the renderer
        // is handed a localized "unavailable" caption for each empty widget.
        var unavailable = localizer.Get(LocKeys.Settings.Plugins.StateUnavailable);
        return new WidgetsPageModel
        {
            CalendarMonthTitle = content.CalendarMonthTitle,
            WeekdayHeaders = content.WeekdayHeaders.IsDefaultOrEmpty
                ? WidgetsPageModel.DefaultWeekdayHeaders
                : content.WeekdayHeaders,
            CalendarDays = days,
            CalendarEventText = localizer.Get(LocKeys.Page.Widgets.NoEvents),
            TasksTitle = localizer.Get(LocKeys.Page.Widgets.Tasks),
            TasksProgressText = content.TasksProgressText
                ?? (tasks.Length == 0 ? null : $"{done}/{tasks.Length}"),
            TasksOverflowText = content.TasksMoreText,
            TodayLabel = localizer.Get(LocKeys.Page.Widgets.Today),
            Tasks = tasks,
            Stats = stats,
            // Real weather from the Open-Meteo monitor; the glyph is the WMO code mapped onto the three
            // weather icons the island has. Without a reading the card keeps the explicit unavailable state.
            WeatherTemp = content.WeatherTemp,
            WeatherCondition = content.WeatherConditionText,
            WeatherHighLow = content.WeatherHighLow,
            WeatherWind = content.WeatherWind,
            WeatherIcon = content.WeatherTemp is null
                ? null
                : content.WeatherGlyph switch
                {
                    WidgetWeatherGlyph.Sun => IconKind.Sun,
                    WidgetWeatherGlyph.Moon => IconKind.Moon,
                    _ => IconKind.CloudSun,
                },
            WeatherUnavailableText = content.WeatherTemp is null ? unavailable : null,
            StatsEmptyText = unavailable,
            TasksEmptyText = tasks.Length == 0 ? content.TasksEmptyHint ?? unavailable : null,
        };
    }

    /// <summary>
    /// Structural signature: changes require a full frame (page strip, chrome, page content).
    /// Deliberately excludes progress/lyrics/position so those can take the dirty-rect path.
    /// </summary>
    /// <param name="content">Non-activity content snapshot.</param>
    /// <param name="island">Activity view model.</param>
    /// <param name="localizer">Localizer (culture identity).</param>
    /// <param name="settings">Live settings (mode/anchor/palette signature).</param>
    public static string StructuralSignature(
        IslandContent content,
        IslandViewModel island,
        ILocalizer localizer,
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(island);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(settings);

        var primary = island.Primary;
        var media = content.Media;
        var builder = new StringBuilder(320);
        builder.Append(localizer.Culture).Append('|')
            .Append(settings.Mode).Append('|').Append(settings.Anchor).Append('|')
            .Append(settings.Theme).Append('|').Append(settings.Material).Append('|')
            .Append(settings.Palette).Append('|').Append(settings.AccentMode).Append('|')
            .Append(settings.AccentFallback).Append('|').Append(settings.FillOpacity.ToString("0.###", CultureInfo.InvariantCulture)).Append('|')
            .Append(settings.FontScale.ToString("0.###", CultureInfo.InvariantCulture)).Append('|')
            .Append(settings.Stacking).Append('|')
            .Append(island.StateKind).Append('|').Append(island.Page).Append('|')
            .Append(primary?.Kind.ToString() ?? "idle").Append('|')
            .Append(primary?.Title).Append('|').Append(primary?.Subtitle).Append('|')
            .Append(primary?.Badge).Append('|').Append(primary?.SourceName).Append('|')
            .Append(primary?.VerificationCode).Append('|').Append(primary?.AccentColorHex).Append('|')
            .Append(island.OverflowCount).Append('|')
            .Append(media?.Title).Append('|').Append(media?.Artist).Append('|').Append(media?.Album).Append('|')
            // Cover art arrives after the metadata (asynchronous fetch); without it here the page models
            // keep the placeholder for the whole track because nothing else changes.
            .Append(media?.ArtworkKey).Append('|').Append(media?.AccentHex).Append('|')
            .Append(media?.HasTimeline).Append('|')
            .Append(content.BatteryPercent).Append('|').Append(content.IsCharging).Append('|')
            // Audio/brightness values are painted by the home sliders; missing them let a no-op content
            // heartbeat skip the only rebuild that updated them.
            .Append(content.VolumePercent).Append('|').Append(content.IsMuted).Append('|')
            .Append(content.BrightnessPercent).Append('|')
            // Home clock/date derive from Now; minute ticks are real content changes.
            .Append(content.Now.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture)).Append('|')
            .Append(content.NetworkName).Append('|').Append(content.IsOnline).Append('|').Append(content.FocusOn).Append('|')
            .Append(content.FocusAvailable).Append('|')
            .Append(settings?.Theme).Append('|')
            .Append(content.UnreadCount).Append('|')
            .Append(content.CalendarMonthTitle).Append('|').Append(content.CalendarDays.Length).Append('|')
            // Widgets weather/task text: a new temperature or a task hint must repaint the page.
            .Append(content.WeatherTemp).Append('|').Append(content.WeatherConditionText).Append('|')
            .Append(content.WeatherHighLow).Append('|').Append(content.WeatherWind).Append('|')
            .Append((int)content.WeatherGlyph).Append('|').Append(content.TasksEmptyHint).Append('|')
            .Append(content.TasksMoreText).Append('|').Append(content.TasksProgressText).Append('|');

        AppendNotifications(builder, content.Notifications);
        AppendClipboard(builder, content.Clipboard);
        AppendAgents(builder, content.Agents);
        AppendStats(builder, content.Stats);
        AppendTasks(builder, content.Tasks);
        return builder.ToString();
    }

    private static void AppendNotifications(StringBuilder builder, IReadOnlyList<NotificationItemData> items)
    {
        builder.Append(items.Count).Append('|');
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            builder.Append(item.Title).Append('|').Append(item.Body).Append('|')
                .Append(item.Source).Append('|').Append(item.TimeText).Append('|')
                .Append(item.VerificationCode).Append('|');
        }
    }

    private static void AppendClipboard(StringBuilder builder, IReadOnlyList<ClipboardItemData> items)
    {
        builder.Append(items.Count).Append('|');
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            builder.Append(item.Preview).Append('|').Append(item.Meta).Append('|')
                .Append(item.Pinned ? '1' : '0').Append(item.IsImage ? '1' : '0').Append('|');
        }
    }

    private static void AppendAgents(StringBuilder builder, IReadOnlyList<AgentItemData> items)
    {
        builder.Append(items.Count).Append('|');
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            builder.Append(item.Name).Append('|').Append(item.StatusText).Append('|')
                .Append(item.Detail).Append('|')
                .Append(item.PrimaryText).Append('|').Append(item.Project).Append('|')
                .Append((int)item.Tone).Append('|');
        }
    }

    /// <summary>
    /// Elapsed-time-only signature. Agent durations tick once per second but the page skeleton,
    /// labels and shell size do not change; keeping them out of <see cref="StructuralSignature"/>
    /// lets the controller repaint just the time strip instead of rebuilding the whole layout.
    /// </summary>
    /// <param name="content">Non-activity content snapshot.</param>
    /// <param name="island">Activity view model (compact agent duration lives on the primary activity).</param>
    public static string AgentTimingSignature(IslandContent content, IslandViewModel island)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(island);
        var builder = new StringBuilder(96);
        var items = content.Agents;
        builder.Append(items.Length).Append('|');
        for (var i = 0; i < items.Length; i++)
        {
            builder.Append(items[i].TimeText).Append('|');
        }

        return builder.ToString();
    }

    private static void AppendStats(StringBuilder builder, IReadOnlyList<WidgetStatData> items)
    {
        builder.Append(items.Count).Append('|');
        for (var i = 0; i < items.Count; i++)
        {
            builder.Append(items[i].Label).Append(':').Append(items[i].Value).Append('|');
        }
    }

    private static void AppendTasks(StringBuilder builder, IReadOnlyList<WidgetTaskData> items)
    {
        builder.Append(items.Count).Append('|');
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            builder.Append(item.Title).Append('|').Append(item.Done ? '1' : '0').Append('|');
        }
    }

    /// <summary>Dynamic signature: progress/position/lyrics only (dirty-rect candidates).</summary>
    /// <param name="content">Non-activity content snapshot.</param>
    /// <param name="island">Activity view model.</param>
    public static string DynamicSignature(IslandContent content, IslandViewModel island)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(island);
        var primary = island.Primary;
        var media = content.Media;
        var builder = new StringBuilder(80);
        builder.Append(primary?.ProgressPercent).Append('|')
            .Append(primary?.PositionTicks).Append('|').Append(primary?.EndTicks).Append('|')
            .Append(primary?.LyricLine).Append('|').Append(primary?.SecondaryLyricLine).Append('|')
            .Append(primary?.IsActive).Append('|')
            .Append(media?.ProgressPercent).Append('|').Append(media?.PositionText).Append('|')
            .Append(media?.DurationText).Append('|').Append(media?.LyricLine).Append('|')
            .Append(media?.NextLyricLine).Append('|').Append(media?.IsPlaying).Append('|')
            // Karaoke sweep in 0.2 % steps: the controller repaints per frame, and this signature must not
            // quantise the sweep into visible jumps when content is re-synced.
            .Append(Math.Round(Math.Max(-1, media?.LyricHighlight ?? -1) * 500));
        return builder.ToString();
    }

    /// <summary>
    /// Finds the toast record that produced the current notification activity, so the compact card can
    /// reuse its relative time and real app logo bytes. Matching is deliberately source-first; the title
    /// is only a tie-breaker because plugins may prefix/rewrite it.
    /// </summary>
    private static NotificationItemData? FindNotification(
        IslandContent content,
        string? source,
        string? title)
    {
        foreach (var item in content.Notifications)
        {
            if (!string.IsNullOrWhiteSpace(source)
                && !string.Equals(item.Source, source, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(item.Title)
                && !title.Contains(item.Title, StringComparison.Ordinal)
                && !item.Title.Contains(title, StringComparison.Ordinal))
            {
                continue;
            }

            return item;
        }

        return null;
    }

    /// <summary>Maps the App vector icon identity onto the renderer's path icon set.</summary>
    /// <param name="icon">App icon kind.</param>
    public static IslandIcon MapIcon(AppIcon icon) => icon switch
    {
        AppIcon.Clock => IslandIcon.Clock,
        AppIcon.Play => IslandIcon.Play,
        AppIcon.Pause => IslandIcon.Pause,
        AppIcon.Previous => IslandIcon.Previous,
        AppIcon.Next => IslandIcon.Next,
        AppIcon.VolumeMute => IslandIcon.VolumeMute,
        AppIcon.VolumeLow => IslandIcon.VolumeLow,
        AppIcon.VolumeMedium => IslandIcon.VolumeMedium,
        AppIcon.VolumeHigh => IslandIcon.VolumeHigh,
        AppIcon.BrightnessLow => IslandIcon.BrightnessLow,
        AppIcon.BrightnessHigh => IslandIcon.BrightnessHigh,
        AppIcon.Bell => IslandIcon.Bell,
        AppIcon.Clipboard => IslandIcon.Clipboard,
        AppIcon.Agent => IslandIcon.Agent,
        AppIcon.Battery => IslandIcon.Battery,
        AppIcon.Bolt => IslandIcon.Bolt,
        AppIcon.Lock => IslandIcon.Lock,
        AppIcon.Usb => IslandIcon.Usb,
        AppIcon.Bluetooth => IslandIcon.Bluetooth,
        AppIcon.Focus => IslandIcon.Focus,
        AppIcon.Download => IslandIcon.Download,
        AppIcon.Search => IslandIcon.Search,
        AppIcon.ChevronDown => IslandIcon.ChevronDown,
        AppIcon.ChevronRight => IslandIcon.ChevronRight,
        AppIcon.Settings => IslandIcon.Settings,
        AppIcon.Store => IslandIcon.Store,
        AppIcon.Close => IslandIcon.Close,
        AppIcon.Check => IslandIcon.Check,
        AppIcon.Pin => IslandIcon.Pin,
        AppIcon.Home => IslandIcon.Home,
        AppIcon.Media => IslandIcon.Media,
        AppIcon.Grid => IslandIcon.Grid,
        AppIcon.Wifi => IslandIcon.Wifi,
        AppIcon.Monitor => IslandIcon.Grid,
        AppIcon.Calendar => IslandIcon.Grid,
        AppIcon.Task => IslandIcon.Check,
        AppIcon.Sun => IslandIcon.Sun,
        AppIcon.More => IslandIcon.Grid,
        AppIcon.Rollback => IslandIcon.Previous,
        AppIcon.Warning => IslandIcon.Alert,
        AppIcon.ExternalLink => IslandIcon.ChevronRight,
        AppIcon.Folder => IslandIcon.Document,
        AppIcon.Document => IslandIcon.Document,
        _ => IslandIcon.Clock,
    };

    /// <summary>Maps the App status tone onto the renderer's compact status tone.</summary>
    /// <param name="tone">App tone.</param>
    public static CompactStatus MapTone(AppTone tone) => tone switch
    {
        AppTone.Info => CompactStatus.Info,
        AppTone.Success => CompactStatus.Success,
        AppTone.Warning => CompactStatus.Warning,
        AppTone.Danger => CompactStatus.Danger,
        _ => CompactStatus.Neutral,
    };

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}