using System.Globalization;
using FluidBar.App.Localization;
using FluidBar.Core.Activities;
using FluidBar.Core.Interaction;
using FluidBar.Core.Layout;
using FluidBar.Island;
using FluidBar.Platform.Windows.Accessibility;
using SkiaSharp;

namespace FluidBar.App.Hosting.Skia;

/// <summary>Stable element ids exposed by <see cref="SkiaIslandUiaSource"/>.</summary>
public static class SkiaIslandUiaElementIds
{
    /// <summary>The island root ("FluidBar").</summary>
    public const string Root = "island.root";

    /// <summary>Compact activity summary (informational group).</summary>
    public const string Compact = "island.compact";

    /// <summary>Compact "+N" overflow chip (button; opens the oldest background activity's page).</summary>
    public const string Overflow = "island.compact.overflow";

    /// <summary>Secondary notification pill below the compact pill (button; opens the notifications page).</summary>
    public const string SecondaryNotification = "island.compact.notification";

    /// <summary>Split-island preview button id ("island.compact.split.N").</summary>
    public static string SplitIslet(int index) => $"island.compact.split.{index.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Expanded page tab list.</summary>
    public const string Pages = "island.pages";

    /// <summary>Collapse chevron.</summary>
    public const string Collapse = "island.collapse";

    /// <summary>Settings gear in the expanded header (opens the settings window).</summary>
    public const string Settings = "island.settings";

    /// <summary>Transient page-strip group (mini-islands and dots).</summary>
    public const string Strip = "island.strip";

    /// <summary>Page tab id ("island.page.N").</summary>
    public static string Page(int page) => $"island.page.{page.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Page body group id ("island.page.N.body").</summary>
    public static string PageBody(int page) => $"{Page(page)}.body";
}

/// <summary>
/// Builds the island's UI Automation element snapshot from the current render state and executes the
/// provider's actions through the existing controller/platform seam. The source is UI-thread affine
/// (like the controller); the provider calls it through the host's UI dispatcher.
/// <para>
/// The snapshot is rebuilt on state/strip/focus changes, never per frame. Bounds are host-local DIP;
/// the Platform provider translates them with the live window geometry, so moving or DPI-scaling the
/// island does not rebuild the tree.
/// </para>
/// </summary>
public sealed class SkiaIslandUiaSource : IUiaElementSource, IUiaKeyboardSink, IDisposable
{
    private const string PagePrefix = "island.page.";
    private const string StripPrefix = "island.strip.";
    private const int MaxListRows = 32;

    private readonly SkiaIslandController _controller;
    private readonly ILocalizer _localizer;
    private readonly SkiaHostDispatcher _dispatcher;
    private string? _focusedId;
    private bool _windowFocused;
    private bool _disposed;

    /// <summary>Creates the source for one controller.</summary>
    /// <param name="controller">Island pipeline that owns the render state.</param>
    /// <param name="localizer">Localizer for structural labels.</param>
    /// <param name="dispatcher">UI dispatcher (off-thread UIA actions are posted here).</param>
    public SkiaIslandUiaSource(SkiaIslandController controller, ILocalizer localizer, SkiaHostDispatcher dispatcher)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    /// <inheritdoc />
    public event Action? Changed;

    /// <summary>Current logical focus element id, or null when nothing is focused.</summary>
    public string? FocusedElementId => _focusedId;

    /// <summary>Whether the island HWND currently owns keyboard focus.</summary>
    public bool HasWindowFocus => _windowFocused;

    /// <inheritdoc />
    public UiaElementSnapshot BuildSnapshot()
    {
        var state = _controller.HasRenderState
            ? _controller.RenderState
            : new IslandRenderState { Mode = IslandMode.Float, Form = ShellForm.Hidden, Opacity = 0 };

        var root = BuildRoot(state);
        var focused = _focusedId is null ? null : root.Find(_focusedId);
        if (_focusedId is not null && focused is null)
        {
            _focusedId = null;
        }

        var ring = _windowFocused && focused is { State.IsOffscreen: false }
            ? focused.BoundsDip
            : (UiaRect?)null;
        _controller.SetAccessibilityFocus(
            ring is { } bounds ? new VisualRect(bounds.X, bounds.Y, bounds.Width, bounds.Height) : null,
            ring is null ? MiniRailSlot.None : RailSlotFor(_focusedId));
        return root;
    }

    /// <inheritdoc />
    public void Perform(in UiaElementActionRequest request)
    {
        if (_disposed)
        {
            return;
        }

        if (!_dispatcher.IsOnDispatcherThread)
        {
            var copy = request;
            _dispatcher.Post(() => Execute(copy));
            return;
        }

        Execute(request);
    }

    /// <inheritdoc />
    public bool HandleKey(UiaKeyCommand command)
    {
        if (_disposed)
        {
            return false;
        }

        var root = BuildSnapshot();
        var order = root.Descendants()
            .Where(static node => node.State.IsFocusable && !node.State.IsOffscreen && node.State.IsEnabled)
            .ToArray();
        var index = Array.FindIndex(order, node => string.Equals(node.Id, _focusedId, StringComparison.Ordinal));

        switch (command)
        {
            case UiaKeyCommand.Tab:
            case UiaKeyCommand.ShiftTab:
                if (order.Length == 0)
                {
                    return false;
                }

                var next = command == UiaKeyCommand.Tab
                    ? (index < 0 ? 0 : (index + 1) % order.Length)
                    : (index < 0 ? order.Length - 1 : (index - 1 + order.Length) % order.Length);
                MoveFocus(order[next]);
                return true;

            case UiaKeyCommand.Left:
            case UiaKeyCommand.Right:
            case UiaKeyCommand.Up:
            case UiaKeyCommand.Down:
                if (index >= 0 && TryAdjustRange(order[index], command))
                {
                    return true;
                }

                if (command is UiaKeyCommand.Left or UiaKeyCommand.Right && _controller.RenderState.IsExpanded)
                {
                    _controller.HandlePageMove(command == UiaKeyCommand.Left ? -1 : 1);
                    return true;
                }

                if (order.Length == 0)
                {
                    return false;
                }

                var delta = command is UiaKeyCommand.Up or UiaKeyCommand.Left ? -1 : 1;
                var target = index < 0 ? 0 : (index + delta + order.Length) % order.Length;
                MoveFocus(order[target]);
                return true;

            case UiaKeyCommand.Home:
                if (_controller.RenderState.IsExpanded)
                {
                    _controller.HandlePageRequest(PageRef.Home);
                    return true;
                }

                if (order.Length > 0)
                {
                    MoveFocus(order[0]);
                    return true;
                }

                return false;

            case UiaKeyCommand.End:
                if (_controller.RenderState.IsExpanded)
                {
                    RequestPage(_controller.RenderState.Pages.PageCount - 1);
                    return true;
                }

                if (order.Length > 0)
                {
                    MoveFocus(order[^1]);
                    return true;
                }

                return false;

            case UiaKeyCommand.Enter:
            case UiaKeyCommand.Space:
                if (index < 0)
                {
                    MoveFocus(root);
                    return true;
                }

                Activate(order[index]);
                return true;

            case UiaKeyCommand.Escape:
                _controller.HandleEscape();
                return true;

            default:
                return false;
        }
    }

    /// <inheritdoc />
    public void NotifyWindowFocus(bool focused)
    {
        if (_windowFocused == focused)
        {
            return;
        }

        _windowFocused = focused;
        Changed?.Invoke();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        Changed = null;
        _focusedId = null;
    }

    // ------------------------------------------------------------------ element tree

    private UiaElementSnapshot BuildRoot(in IslandRenderState state)
    {
        var shell = state.IsVisible ? IslandMetrics.ShellRect(state) : default;
        var children = state.IsVisible
            ? (state.Form == ShellForm.Expanded ? BuildExpanded(state) : BuildCompact(state))
            : [];

        return Node(
            SkiaIslandUiaElementIds.Root,
            UiaElementRole.Group,
            "FluidBar",
            ToRect(shell),
            UiaPatternKind.Invoke | UiaPatternKind.ExpandCollapse,
            new UiaElementState
            {
                IsFocusable = true,
                IsOffscreen = !state.IsVisible,
                ExpandCollapse = state.IsExpanded ? UiaExpandCollapseState.Expanded : UiaExpandCollapseState.Collapsed,
            },
            help: L("Island.Accessibility.RootHelp",
                "Press Enter to expand or collapse the island. Use Tab and the arrow keys to move between controls."),
            children: children);
    }

    private List<UiaElementSnapshot> BuildCompact(in IslandRenderState state)
    {
        var shell = IslandMetrics.ShellRect(state);
        var children = new List<UiaElementSnapshot>(2)
        {
            Node(
                SkiaIslandUiaElementIds.Compact,
                UiaElementRole.Group,
                CompactName(state),
                ToRect(shell),
                state: new UiaElementState { IsOffscreen = false }),
        };

        if (state.OverflowCount > 0)
        {
            children.Add(Node(
                SkiaIslandUiaElementIds.Overflow,
                UiaElementRole.Button,
                F(LocKeys.Island.Accessibility.MoreActivities, "{0} more activities", state.OverflowCount),
                ToRect(IslandMetrics.OverflowBadgeRect(state)),
                UiaPatternKind.Invoke,
                new UiaElementState { IsFocusable = true },
                help: L(LocKeys.Island.Accessibility.MoreActivitiesHelp,
                    "Press Enter to open the page of the oldest background activity.")));
        }

        if (state.SecondaryCompact is { } secondaryActivity)
        {
            children.Add(Node(
                SkiaIslandUiaElementIds.SecondaryNotification,
                UiaElementRole.Button,
                CompactName(secondaryActivity),
                ToRect(IslandMetrics.SecondaryShellRect(state)),
                UiaPatternKind.Invoke,
                new UiaElementState { IsFocusable = true },
                help: L("Island.Accessibility.SecondaryNotificationHelp",
                    "Press Enter to open the notifications page; the current activity stays in the main pill.")));
        }

        if (state.Form != ShellForm.Expanded && state.SplitIslets.Length > 0)
        {
            var islets = IslandMetrics.SplitIsletRects(state);
            for (var i = 0; i < islets.Length && i < state.SplitIslets.Length; i++)
            {
                var islet = state.SplitIslets[i];
                var kind = _localizer.Get("Activity." + islet.Kind);
                var name = string.IsNullOrWhiteSpace(kind) ? islet.Kind.ToString() : kind;
                children.Add(Node(
                    SkiaIslandUiaElementIds.SplitIslet(i),
                    UiaElementRole.Button,
                    string.IsNullOrWhiteSpace(islet.Title) ? name : $"{name} · {islet.Title}",
                    ToRect(islets[i]),
                    UiaPatternKind.Invoke,
                    new UiaElementState { IsFocusable = true },
                    help: L("Island.Accessibility.SplitIsletHelp",
                        "Press Enter to merge this preview back into the pill and open its page.")));
            }
        }

        // The compact media pill has no play/pause button (removed on request), so the tree exposes the
        // pill as one expand group plus the overflow chip and the split previews.
        return children;
    }

    private List<UiaElementSnapshot> BuildExpanded(in IslandRenderState state)
    {
        var content = IslandMetrics.ContentRect(IslandMetrics.ShellRect(state));
        var page = Math.Clamp(state.PageIndex, 0, state.Pages.PageCount - 1);
        var children = new List<UiaElementSnapshot>(5)
        {
            Node(
                SkiaIslandUiaElementIds.Collapse,
                UiaElementRole.Button,
                L("Common.Collapse", "Collapse"),
                ToRect(PageLayout.CollapseRect(content)),
                UiaPatternKind.Invoke,
                new UiaElementState { IsFocusable = true },
                help: L("Island.Accessibility.CollapseHelp", "Collapse the island to the compact pill.")),
            Node(
                SkiaIslandUiaElementIds.Settings,
                UiaElementRole.Button,
                L("Page.Home.OpenSettings", "Settings"),
                ToRect(PageLayout.SettingsRect(content)),
                UiaPatternKind.Invoke,
                new UiaElementState { IsFocusable = true },
                help: L("Island.Accessibility.OpenSettingsHelp",
                    "Open the FluidBar settings window.")),
            BuildPages(state, page),
            BuildPageControls(state, page),
        };

        if (state.Archipelago.Visible && state.Archipelago.Opacity > 0.01)
        {
            children.Add(BuildStrip(state));
        }

        return children;
    }

    private UiaElementSnapshot BuildPages(in IslandRenderState state, int currentPage)
    {
        var strip = SkiaIslandHitTest.MeasureStrip(state);
        var stripVisible = state.Archipelago.Visible && state.Archipelago.Opacity > 0.01;
        var tabs = new List<UiaElementSnapshot>(state.Pages.PageCount);

        for (var page = 0; page < state.Pages.PageCount; page++)
        {
            var bounds = TabBounds(strip, page, stripVisible);
            var offscreen = !stripVisible || bounds.IsEmpty;
            tabs.Add(Node(
                SkiaIslandUiaElementIds.Page(page),
                UiaElementRole.TabItem,
                state.Pages.Title(page),
                bounds,
                UiaPatternKind.SelectionItem | UiaPatternKind.Invoke,
                new UiaElementState
                {
                    IsFocusable = true,
                    IsSelected = page == currentPage,
                    IsOffscreen = offscreen,
                },
                help: offscreen
                    ? L("Island.Accessibility.PageHiddenHelp",
                        "The page strip is hidden. Use the Left and Right arrow keys to change pages.")
                    : null));
        }

        return Node(
            SkiaIslandUiaElementIds.Pages,
            UiaElementRole.Tab,
            L("Island.Accessibility.Pages", "Pages"),
            stripVisible ? ToRect(strip.Strip) : UiaRect.Empty,
            state: new UiaElementState { IsOffscreen = !stripVisible },
            children: tabs);
    }

    private UiaElementSnapshot BuildPageControls(in IslandRenderState state, int pageIndex)
    {
        var page = IslandMetrics.PageRect(state, pageIndex);
        var slot = state.Pages.PageSlotAt(pageIndex);
        var children = slot?.PluginPage is { } pluginPage
            ? BuildPluginControls(state, pluginPage, pageIndex, page)
            : slot?.Kind switch
            {
                PageSlotKind.Home => BuildHomeControls(state, page),
                PageSlotKind.Media => BuildMediaControls(state, page),
                PageSlotKind.Notifications => BuildNotificationControls(state, page),
                PageSlotKind.Clipboard => BuildClipboardControls(state, page),
                PageSlotKind.Agent => BuildAgentControls(state, page),
                PageSlotKind.Widgets => BuildWidgetControls(state, page),
                _ => [],
            };

        return Node(
            SkiaIslandUiaElementIds.PageBody(pageIndex),
            UiaElementRole.Group,
            state.Pages.Title(pageIndex),
            ToRect(page),
            state: new UiaElementState { IsOffscreen = false },
            children: children);
    }

    private List<UiaElementSnapshot> BuildHomeControls(in IslandRenderState state, SKRect page)
    {
        var home = state.Pages.Home;
        var homeMetrics = FontScaleMetrics.From(state.FontScale);
        var children = new List<UiaElementSnapshot>(11);

        var clock = string.Join(" ", new[] { home.ClockText, home.DateText }
            .Where(static value => !string.IsNullOrWhiteSpace(value)));
        if (clock.Length > 0)
        {
            children.Add(Node(
                "island.page.0.clock",
                UiaElementRole.Text,
                clock,
                ToRect(PageLayout.HomeClock(page)),
                state: new UiaElementState { IsOffscreen = false }));
        }

        if (home.ShowWeather && !string.IsNullOrWhiteSpace(home.WeatherTemp))
        {
            children.Add(Node(
                "island.page.0.weather",
                UiaElementRole.Text,
                string.Join(", ", new[] { home.WeatherTemp, home.WeatherCondition }
                    .Where(static value => !string.IsNullOrWhiteSpace(value))),
                ToRect(PageLayout.HomeWeather(page)),
                state: new UiaElementState { IsOffscreen = false }));
        }

        var togglesMetrics = FontScaleMetrics.From(state.FontScale);
        for (var i = 0; i < Math.Min(4, home.Toggles.Count); i++)
        {
            var toggle = home.Toggles[i];
            children.Add(Node(
                $"island.page.0.toggle.{i}",
                UiaElementRole.Button,
                toggle.Label,
                ToRect(PageMetricsLayout.HomeToggle(page, i, togglesMetrics)),
                toggle.IsEnabled ? UiaPatternKind.Toggle | UiaPatternKind.Invoke : UiaPatternKind.None,
                new UiaElementState
                {
                    IsEnabled = toggle.IsEnabled,
                    IsFocusable = toggle.IsEnabled,
                    IsToggled = toggle.IsOn,
                    ItemStatus = toggle.IsEnabled
                        ? toggle.IsOn ? L("Common.On", "On") : L("Common.Off", "Off")
                        : toggle.DisabledReason ?? L("Common.Disabled", "Disabled"),
                },
                help: toggle.IsEnabled
                    ? L("Island.Accessibility.ToggleHelp", "Press Enter to toggle.")
                    : toggle.DisabledReason ?? L("Common.Disabled", "Disabled")));
        }

        var nowPlaying = home.NowPlaying;
        if (!string.IsNullOrWhiteSpace(nowPlaying.Title))
        {
            var summary = string.Join(" — ", new[] { nowPlaying.Title, nowPlaying.Subtitle }
                .Where(static value => !string.IsNullOrWhiteSpace(value)));
            children.Add(Node(
                "island.page.0.card",
                UiaElementRole.Button,
                F("Island.Accessibility.NowPlaying", "Now playing: {0}", summary),
                ToRect(PageLayout.HomeMediaCard(page)),
                UiaPatternKind.Invoke,
                new UiaElementState { IsFocusable = true },
                help: L("Island.Accessibility.OpenMediaPage", "Open the media page.")));
            children.Add(Node(
                "island.page.0.card.play",
                UiaElementRole.Button,
                nowPlaying.IsPlaying ? L("Media.Pause", "Pause") : L("Media.Play", "Play"),
                ToRect(PageLayout.HomeMediaPlay(page)),
                UiaPatternKind.Invoke,
                new UiaElementState { IsFocusable = true }));
        }

        children.Add(RangeNode(
            "island.page.0.volume",
            L("Hud.Volume", "Volume"),
            InflateVertical(PageLayout.HomeVolumeTrack(page), 8),
            home.Volume,
            home.IsMuted ? L("Hud.Muted", "Muted") : null));
        children.Add(Node(
            "island.page.0.mute",
            UiaElementRole.Button,
            home.IsMuted ? L("Hud.Muted", "Muted") : L("Hud.Volume", "Volume"),
            ToRect(PageMetricsLayout.HomeVolumeIcon(page, homeMetrics)),
            home.HasVolume ? UiaPatternKind.Invoke : UiaPatternKind.None,
            new UiaElementState
            {
                IsEnabled = home.HasVolume,
                IsFocusable = home.HasVolume,
                ItemStatus = home.HasVolume ? $"{home.Volume}%" : L("Page.Media.OutputNone", "No output"),
            },
            help: home.HasVolume
                ? L("Island.Accessibility.ToggleHelp", "Press Enter to toggle.")
                : L("Page.Media.OutputNone", "No output")));
        children.Add(RangeNode(
            "island.page.0.brightness",
            L("Hud.Brightness", "Brightness"),
            InflateVertical(PageLayout.HomeBrightnessTrack(page), 8),
            home.Brightness,
            null));

        var battery = F("Island.Accessibility.Battery", "Battery {0}%", home.BatteryPercent);
        if (!string.IsNullOrWhiteSpace(home.BatteryStatus))
        {
            battery = $"{battery}, {home.BatteryStatus}";
        }

        children.Add(Node(
            "island.page.0.battery",
            UiaElementRole.Text,
            battery,
            ToRect(PageLayout.HomeBattery(page)),
            state: new UiaElementState { IsOffscreen = false, ItemStatus = home.BatteryRemaining }));
        return children;
    }

    private List<UiaElementSnapshot> BuildMediaControls(in IslandRenderState state, SKRect page)
    {
        var media = state.Pages.Media;
        var mediaMetrics = FontScaleMetrics.From(state.FontScale);
        var children = new List<UiaElementSnapshot>(8);

        if (!media.HasSession)
        {
            children.Add(Node(
                "island.page.1.empty",
                UiaElementRole.Text,
                media.EmptyTitle ?? L("Media.NoSession", "Nothing playing"),
                ToRect(PageLayout.EmptyTitle(page)),
                state: new UiaElementState { IsOffscreen = false, ItemStatus = media.EmptyHint }));
            return children;
        }

        var track = string.Join(" — ", new[] { media.Title, media.Artist, media.Album }
            .Where(static value => !string.IsNullOrWhiteSpace(value)));
        if (track.Length > 0)
        {
            children.Add(Node(
                "island.page.1.track",
                UiaElementRole.Text,
                track,
                ToRect(PageLayout.MediaTitle(page)),
                state: new UiaElementState
                {
                    IsOffscreen = false,
                    ItemStatus = string.Join(" / ", new[] { media.PositionText, media.DurationText }
                        .Where(static value => !string.IsNullOrWhiteSpace(value))),
                }));
        }

        children.Add(Node(
            "island.page.1.previous",
            UiaElementRole.Button,
            L("Media.Previous", "Previous"),
            ToRect(PageLayout.MediaPrevious(page)),
            UiaPatternKind.Invoke,
            new UiaElementState { IsFocusable = true }));
        children.Add(Node(
            "island.page.1.play",
            UiaElementRole.Button,
            media.IsPlaying ? L("Media.Pause", "Pause") : L("Media.Play", "Play"),
            ToRect(PageLayout.MediaPlay(page)),
            UiaPatternKind.Invoke,
            new UiaElementState { IsFocusable = true }));
        children.Add(Node(
            "island.page.1.next",
            UiaElementRole.Button,
            L("Media.Next", "Next"),
            ToRect(PageLayout.MediaNext(page)),
            UiaPatternKind.Invoke,
            new UiaElementState { IsFocusable = true }));
        children.Add(RangeNode(
            "island.page.1.seek",
            L("Media.Seek", "Seek"),
            PageLayout.MediaSeekRow(page),
            media.ProgressPercent,
            media.PositionText));
        children.Add(Node(
            "island.page.1.mute",
            UiaElementRole.Button,
            media.IsMuted ? L("Hud.Muted", "Muted") : L("Hud.Volume", "Volume"),
            ToRect(PageMetricsLayout.MediaVolume(page, mediaMetrics)),
            media.HasVolume ? UiaPatternKind.Invoke : UiaPatternKind.None,
            new UiaElementState
            {
                IsEnabled = media.HasVolume,
                IsFocusable = media.HasVolume,
                ItemStatus = media.HasVolume
                    ? $"{media.VolumePercent ?? 0}%"
                    : L("Island.Accessibility.UnsupportedVolume", "No audio endpoint is available."),
            },
            help: media.HasVolume
                ? L("Island.Accessibility.ToggleHelp", "Press Enter to toggle.")
                : L("Island.Accessibility.UnsupportedVolume", "No audio endpoint is available.")));
        children.Add(Node(
            "island.page.1.output",
            UiaElementRole.Button,
            L("Page.Media.Output", "Audio output"),
            ToRect(PageLayout.MediaOutput(page)),
            // Disabled control: no Invoke pattern — an "invokable but disabled" button is exactly the
            // dead-but-clickable shape the user complained about. The reason travels in ItemStatus/help.
            UiaPatternKind.None,
            new UiaElementState
            {
                IsEnabled = false,
                IsFocusable = false,
                ItemStatus = L("Island.Accessibility.UnsupportedOutput",
                    "No audio-output service is available in this build."),
            },
            help: L("Island.Accessibility.UnsupportedOutput",
                "No audio-output service is available in this build.")));
        children.Add(Node(
            "island.page.1.queue",
            UiaElementRole.Button,
            L("Page.Media.Queue", "Play queue"),
            ToRect(PageLayout.MediaQueue(page)),
            UiaPatternKind.None,
            new UiaElementState
            {
                IsEnabled = false,
                IsFocusable = false,
                ItemStatus = L("Island.Accessibility.UnsupportedQueue",
                    "No media queue service is available in this build."),
            },
            help: L("Island.Accessibility.UnsupportedQueue",
                "No media queue service is available in this build.")));
        return children;
    }

    private List<UiaElementSnapshot> BuildNotificationControls(in IslandRenderState state, SKRect page)
    {
        var model = state.Pages.Notifications;
        var rows = new List<UiaElementSnapshot>();
        var metrics = FontScaleMetrics.From(state.FontScale);
        var body = PageMetricsLayout.NotificationBody(page);
        var scroll = PageMetricsLayout.ClampListScroll(
            model.Items.Count,
            PageMetricsLayout.NotificationRowHeight(metrics),
            PageMetricsLayout.NotificationGap,
            (float)model.ScrollOffset);

        for (var i = 0; i < Math.Min(model.Items.Count, MaxListRows); i++)
        {
            var item = model.Items[i];
            var row = PageMetricsLayout.NotificationRow(page, i, scroll, metrics);
            var name = JoinNotEmpty(" — ", item.AppName, item.Title, item.Body);
            var dismiss = PageMetricsLayout.NotificationDismiss(row);
            var children = new List<UiaElementSnapshot>(2);
            if (item.VerificationCode is { Length: > 0 })
            {
                var copy = PageMetricsLayout.NotificationCopyCode(row);
                children.Add(Node(
                    $"island.page.2.row.{i}.copy",
                    UiaElementRole.Button,
                    model.CopyCodeLabel ?? L("Notifications.CopyCode", "Copy code"),
                    ToRect(copy),
                    UiaPatternKind.Invoke,
                    new UiaElementState { IsFocusable = true }));
            }

            children.Add(Node(
                $"island.page.2.row.{i}.dismiss",
                UiaElementRole.Button,
                model.DismissLabel ?? L("Common.Close", "Dismiss"),
                ToRect(dismiss),
                UiaPatternKind.Invoke,
                new UiaElementState { IsFocusable = true }));

            rows.Add(Node(
                $"island.page.2.row.{i}",
                UiaElementRole.ListItem,
                name.Length > 0 ? name : F("Island.Accessibility.NotificationRow", "Notification {0}", i + 1),
                ToRect(row),
                UiaPatternKind.Invoke,
                new UiaElementState
                {
                    IsFocusable = true,
                    IsOffscreen = !Intersects(row, body),
                    ItemStatus = JoinNotEmpty(", ", item.TimeText, item.CodeChipLabel ?? item.VerificationCode),
                },
                children: children));
        }

        return
        [
            Node(
                "island.page.2.list",
                UiaElementRole.List,
                state.Pages.Title(2),
                ToRect(body),
                state: new UiaElementState { IsOffscreen = false },
                children: rows),
        ];
    }

    private List<UiaElementSnapshot> BuildClipboardControls(in IslandRenderState state, SKRect page)
    {
        var model = state.Pages.Clipboard;
        var rows = new List<UiaElementSnapshot>();
        var body = PageLayout.ListBody(page);
        var scroll = PageLayout.ClampScroll(model.ScrollOffset, model.Items.Count,
            PageLayout.ClipboardRowHeight, PageLayout.ClipboardRowGap);

        for (var i = 0; i < Math.Min(model.Items.Count, MaxListRows); i++)
        {
            var item = model.Items[i];
            var row = PageLayout.ClipboardRow(page, i, scroll);
            var children = new List<UiaElementSnapshot>(2)
            {
                Node(
                    $"island.page.3.row.{i}.pin",
                    UiaElementRole.Button,
                    item.IsPinned ? L("Island.Accessibility.Unpin", "Unpin") : L("Common.Pin", "Pin"),
                    ToRect(PageLayout.ClipboardPin(page, i, scroll)),
                    UiaPatternKind.Toggle | UiaPatternKind.Invoke,
                    new UiaElementState { IsFocusable = true, IsToggled = item.IsPinned }),
                Node(
                    $"island.page.3.row.{i}.copy",
                    UiaElementRole.Button,
                    L("Common.Copy", "Copy"),
                    ToRect(PageLayout.ClipboardCopy(page, i, scroll)),
                    UiaPatternKind.Invoke,
                    new UiaElementState { IsFocusable = true }),
            };

            rows.Add(Node(
                $"island.page.3.row.{i}",
                UiaElementRole.ListItem,
                item.Preview ?? F("Island.Accessibility.ClipboardRow", "Clipboard item {0}", i + 1),
                ToRect(row),
                UiaPatternKind.Invoke,
                new UiaElementState
                {
                    IsFocusable = true,
                    IsOffscreen = !Intersects(row, body),
                    ItemStatus = JoinNotEmpty(", ", item.TypeLabel, item.TimeText),
                },
                children: children));
        }

        return
        [
            Node(
                "island.page.3.list",
                UiaElementRole.List,
                state.Pages.Title(3),
                ToRect(body),
                state: new UiaElementState { IsOffscreen = false },
                children: rows),
        ];
    }

    private List<UiaElementSnapshot> BuildAgentControls(in IslandRenderState state, SKRect page)
    {
        var model = state.Pages.Agent;
        var rows = new List<UiaElementSnapshot>();
        var body = PageLayout.ListBody(page);
        var scroll = PageLayout.ClampScroll(model.ScrollOffset, model.Items.Count,
            PageLayout.AgentRowHeight, PageLayout.AgentRowGap);

        for (var i = 0; i < Math.Min(model.Items.Count, MaxListRows); i++)
        {
            var item = model.Items[i];
            var row = PageLayout.AgentRow(page, i, scroll);
            rows.Add(Node(
                $"island.page.4.row.{i}",
                UiaElementRole.ListItem,
                JoinNotEmpty(" — ", JoinNotEmpty(" · ", item.Tool, item.Project), item.Task),
                ToRect(row),
                UiaPatternKind.None,
                new UiaElementState
                {
                    IsFocusable = true,
                    IsOffscreen = !Intersects(row, body),
                    ItemStatus = JoinNotEmpty(", ", item.StatusText, item.DurationText),
                }));
        }

        return
        [
            Node(
                "island.page.4.list",
                UiaElementRole.List,
                state.Pages.Title(4),
                ToRect(body),
                state: new UiaElementState { IsOffscreen = false },
                children: rows),
        ];
    }

    private List<UiaElementSnapshot> BuildWidgetControls(in IslandRenderState state, SKRect page)
    {
        var model = state.Pages.Widgets;
        var children = new List<UiaElementSnapshot>(8);

        children.Add(Node(
            "island.page.5.calendar.prev",
            UiaElementRole.Button,
            L("Island.Accessibility.PreviousMonth", "Previous month"),
            ToRect(PageLayout.WidgetsCalendarNav(page, previous: true)),
            UiaPatternKind.Invoke,
            new UiaElementState { IsFocusable = true }));
        children.Add(Node(
            "island.page.5.calendar.next",
            UiaElementRole.Button,
            L("Island.Accessibility.NextMonth", "Next month"),
            ToRect(PageLayout.WidgetsCalendarNav(page, previous: false)),
            UiaPatternKind.Invoke,
            new UiaElementState { IsFocusable = true }));

        if (!string.IsNullOrWhiteSpace(model.CalendarMonthTitle))
        {
            children.Add(Node(
                "island.page.5.calendar.title",
                UiaElementRole.Text,
                model.CalendarMonthTitle,
                ToRect(new SKRect(page.Left + 26, page.Top + 36, page.Left + 168, page.Top + 56)),
                state: new UiaElementState
                {
                    IsOffscreen = false,
                    ItemStatus = JoinNotEmpty(", ", model.TodayLabel, model.CalendarEventText),
                }));
        }

        if (!string.IsNullOrWhiteSpace(model.WeatherTemp))
        {
            children.Add(Node(
                "island.page.5.weather",
                UiaElementRole.Text,
                JoinNotEmpty(", ", model.WeatherTemp, model.WeatherCondition, model.WeatherHighLow, model.WeatherWind),
                ToRect(PageLayout.WidgetsWeather(page)),
                state: new UiaElementState { IsOffscreen = false }));
        }

        var statsCard = PageLayout.WidgetsStats(page);
        var statHeight = statsCard.Height / Math.Max(1, model.Stats.Count);
        for (var i = 0; i < model.Stats.Count; i++)
        {
            var stat = model.Stats[i];
            var statRect = new SKRect(
                statsCard.Left,
                statsCard.Top + (i * statHeight),
                statsCard.Right,
                statsCard.Top + ((i + 1) * statHeight));
            children.Add(Node(
                $"island.page.5.stat.{i}",
                UiaElementRole.Text,
                $"{stat.Label} {stat.Value.ToString(CultureInfo.CurrentCulture)}%",
                ToRect(statRect),
                state: new UiaElementState { IsOffscreen = false }));
        }

        var paintedTasks = Math.Min(PageMetricsLayout.MaxPaintedTaskRows, model.Tasks.Count);
        for (var i = 0; i < paintedTasks; i++)
        {
            var task = model.Tasks[i];
            children.Add(Node(
                $"island.page.5.task.{i}",
                UiaElementRole.Button,
                task.Title,
                ToRect(PageLayout.WidgetsTaskRow(page, i)),
                UiaPatternKind.Toggle | UiaPatternKind.Invoke,
                new UiaElementState
                {
                    IsFocusable = true,
                    IsToggled = task.Done,
                    ItemStatus = task.Done ? L("Common.Done", "Done") : L("Common.Open", "Open"),
                },
                help: L("Island.Accessibility.TaskHelp", "Press Enter to toggle the task.")));
        }

        if (paintedTasks > 0 && !string.IsNullOrEmpty(model.TasksOverflowText))
        {
            children.Add(Node(
                "island.page.5.tasks.more",
                UiaElementRole.Text,
                model.TasksOverflowText,
                ToRect(PageLayout.WidgetsTaskRow(page, paintedTasks))));
        }

        return children;
    }

    private List<UiaElementSnapshot> BuildPluginControls(
        in IslandRenderState state,
        PluginPageModel pluginPage,
        int pageIndex,
        SKRect page)
    {
        var metrics = FontScaleMetrics.From(state.FontScale);
        var children = new List<UiaElementSnapshot>(pluginPage.Rows.Count * 2);
        for (var i = 0; i < pluginPage.Rows.Count; i++)
        {
            var row = pluginPage.Rows[i];
            var rowRect = PageMetricsLayout.NotificationRow(page, i, 0, metrics);
            var name = string.Join(": ", new[] { row.Title, row.Subtitle }
                .Where(static value => !string.IsNullOrWhiteSpace(value)));
            children.Add(Node(
                $"island.page.{pageIndex}.row.{i}",
                UiaElementRole.ListItem,
                name,
                ToRect(rowRect),
                UiaPatternKind.Invoke,
                new UiaElementState { IsFocusable = row.RowCommandId is not null }));

            if (!string.IsNullOrWhiteSpace(row.TrailingCommandId))
            {
                var button = new SKRect(rowRect.Right - 96, rowRect.MidY - 12, rowRect.Right - 8, rowRect.MidY + 12);
                children.Add(Node(
                    $"island.page.{pageIndex}.row.{i}.button",
                    UiaElementRole.Button,
                    row.TrailingLabel ?? row.Title,
                    ToRect(button),
                    UiaPatternKind.Invoke,
                    new UiaElementState { IsFocusable = true }));
            }
        }

        return children;
    }

    private UiaElementSnapshot BuildStrip(in IslandRenderState state)
    {
        var strip = SkiaIslandHitTest.MeasureStrip(state);
        var page = Math.Clamp(strip.Page, 0, Math.Max(1, state.Pages.PageCount) - 1);
        var children = new List<UiaElementSnapshot>(9);

        if (page > 0)
        {
            children.Add(StripButton($"island.strip.prev.{page - 1}", state.Pages.Title(page - 1),
                strip.Previous, F("Island.Accessibility.PreviousPage", "Previous page: {0}", state.Pages.Title(page - 1))));
        }

        children.Add(StripButton($"island.strip.current.{page}", state.Pages.Title(page),
            strip.Current, F("Island.Accessibility.CurrentPage", "Current page: {0}", state.Pages.Title(page))));

        if (page < state.Pages.PageCount - 1)
        {
            children.Add(StripButton($"island.strip.next.{page + 1}", state.Pages.Title(page + 1),
                strip.Next, F("Island.Accessibility.NextPage", "Next page: {0}", state.Pages.Title(page + 1))));
        }

        if (!strip.OverflowLeft.IsEmpty)
        {
            children.Add(StripButton("island.strip.overflow.left", L("Island.Accessibility.MorePages", "More pages"),
                strip.OverflowLeft, L("Island.Accessibility.MorePagesHelp", "Open the page selector.")));
        }

        if (!strip.OverflowRight.IsEmpty)
        {
            children.Add(StripButton("island.strip.overflow.right", L("Island.Accessibility.MorePages", "More pages"),
                strip.OverflowRight, L("Island.Accessibility.MorePagesHelp", "Open the page selector.")));
        }

        // Every dot of a cluster targets the same (farthest) page, so the ids are de-duplicated by
        // ordinal and the bounds are the exact dot hit rectangle the pointer path uses.
        for (var i = 0; i < strip.DotsLeft.Length; i++)
        {
            var target = strip.LeftDotPage(i);
            if (target >= 0)
            {
                children.Add(StripButton($"island.strip.dot.left.{target}.{i}", state.Pages.Title(target),
                    IslandStripLayout.DotHitRect(strip.DotsLeft[i]),
                    F("Island.Accessibility.PageDot", "Page {0}: {1}", target + 1, state.Pages.Title(target))));
            }
        }

        for (var i = 0; i < strip.DotsRight.Length; i++)
        {
            var target = strip.RightDotPage(i);
            if (target < state.Pages.PageCount)
            {
                children.Add(StripButton($"island.strip.dot.right.{target}.{i}", state.Pages.Title(target),
                    IslandStripLayout.DotHitRect(strip.DotsRight[i]),
                    F("Island.Accessibility.PageDot", "Page {0}: {1}", target + 1, state.Pages.Title(target))));
            }
        }

        return Node(
            SkiaIslandUiaElementIds.Strip,
            UiaElementRole.Group,
            L("Island.Accessibility.PageNavigation", "Page navigation"),
            ToRect(strip.Strip),
            state: new UiaElementState { IsOffscreen = false },
            children: children);
    }

    private UiaElementSnapshot StripButton(string id, string pageTitle, SKRect bounds, string name)
        => Node(
            id,
            UiaElementRole.Button,
            name,
            ToRect(bounds),
            UiaPatternKind.Invoke,
            new UiaElementState { IsFocusable = true, ItemStatus = pageTitle });

    /// <summary>
    /// Maps a focused strip button id to the rail slot that paints its focus outline. Only the two
    /// neighbour glasses are buttons in the prototype; the current capsule still reports a slot so a
    /// keyboard user gets the same outline around the focused element.
    /// </summary>
    /// <param name="elementId">Focused UI Automation element id, or null.</param>
    private static MiniRailSlot RailSlotFor(string? elementId)
    {
        if (elementId is null)
        {
            return MiniRailSlot.None;
        }

        if (elementId.StartsWith("island.strip.prev.", StringComparison.Ordinal))
        {
            return MiniRailSlot.Previous;
        }

        if (elementId.StartsWith("island.strip.current.", StringComparison.Ordinal))
        {
            return MiniRailSlot.Current;
        }

        return elementId.StartsWith("island.strip.next.", StringComparison.Ordinal)
            ? MiniRailSlot.Next
            : MiniRailSlot.None;
    }

    // ------------------------------------------------------------------ actions

    private void Execute(in UiaElementActionRequest request)
    {
        if (_disposed)
        {
            return;
        }

        if (request.Action == UiaElementAction.SetFocus)
        {
            FocusElement(request.ElementId);
            return;
        }
        var id = request.ElementId;
        if (string.Equals(id, SkiaIslandUiaElementIds.Root, StringComparison.Ordinal))
        {
            ExecuteRoot(request.Action);
            return;
        }

        if (string.Equals(id, SkiaIslandUiaElementIds.Collapse, StringComparison.Ordinal))
        {
            _controller.HandleEscape();
            return;
        }

        if (string.Equals(id, SkiaIslandUiaElementIds.Settings, StringComparison.Ordinal))
        {
            _controller.RequestSettings();
            return;
        }

        if (string.Equals(id, SkiaIslandUiaElementIds.Overflow, StringComparison.Ordinal))
        {
            _controller.ActivateOverflowBadge();
            return;
        }

        if (string.Equals(id, SkiaIslandUiaElementIds.SecondaryNotification, StringComparison.Ordinal))
        {
            _controller.ActivateSecondaryNotification();
            return;
        }

        if (id.StartsWith("island.compact.split.", StringComparison.Ordinal)
            && int.TryParse(id.AsSpan("island.compact.split.".Length), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var splitIndex))
        {
            _controller.ActivateSplitIslet(splitIndex);
            return;
        }

        if (id.StartsWith(StripPrefix, StringComparison.Ordinal))
        {
            ExecuteStrip(id);
            return;
        }

        if (id.StartsWith(PagePrefix, StringComparison.Ordinal))
        {
            ExecutePage(request);
        }
    }

    private void ExecuteRoot(UiaElementAction action)
    {
        var expanded = _controller.State.Kind == IslandStateKind.Expanded;
        switch (action)
        {
            case UiaElementAction.Expand:
                if (!expanded)
                {
                    _controller.HandleToggleHotkey();
                }

                break;
            case UiaElementAction.Collapse:
                if (expanded)
                {
                    _controller.HandleEscape();
                }

                break;
            default:
                if (expanded)
                {
                    _controller.HandleEscape();
                }
                else
                {
                    _controller.HandleToggleHotkey();
                }

                break;
        }
    }

    private void ExecuteStrip(string id)
    {
        var parts = id.Split('.');
        if (parts.Length < 4)
        {
            return;
        }

        if (parts[2] == "overflow")
        {
            _controller.RequestPageChooser();
            return;
        }

        if (parts[2] is "prev" or "current" or "next")
        {
            if (parts.Length >= 4 && int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var page))
            {
                RequestPage(page);
            }

            return;
        }

        if (parts[2] == "dot" && parts.Length >= 5
            && int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dotPage))
        {
            RequestPage(dotPage);
        }
    }

    private void ExecutePage(in UiaElementActionRequest request)
    {
        var parts = request.ElementId.Split('.');
        if (parts.Length < 3
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var page))
        {
            return;
        }

        // "island.page.N" is the tab itself.
        if (parts.Length == 3)
        {
            RequestPage(page);
            return;
        }

        var token = parts[3];
        var index = parts.Length > 4
            && int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : -1;

        switch (token)
        {
            case "body":
            case "clock":
            case "weather":
            case "battery":
            case "track":
            case "empty":
            case "list":
            case "calendar" when parts.Length > 4 && parts[4] == "title":
                return;
            case "toggle":
                _controller.ActivateAccessibleControl(IslandPageControl.QuickToggle, page, index);
                return;
            case "volume":
                _controller.SetAccessibleSlider(IslandPageControl.VolumeSlider, page, request.Value / 100.0);
                return;
            case "brightness":
                _controller.SetAccessibleSlider(IslandPageControl.BrightnessSlider, page, request.Value / 100.0);
                return;
            case "play":
                _controller.ActivateAccessibleControl(IslandPageControl.PlayPause, page);
                return;
            case "previous":
                _controller.ActivateAccessibleControl(IslandPageControl.Previous, page);
                return;
            case "next":
                _controller.ActivateAccessibleControl(IslandPageControl.Next, page);
                return;
            case "seek":
                _controller.SetAccessibleSlider(IslandPageControl.Seek, page, request.Value / 100.0);
                return;
            case "mute":
                _controller.ActivateAccessibleControl(IslandPageControl.VolumeMute, page);
                return;
            case "output":
                // Output/queue are inert (no platform capability): the tree marks them disabled and the
            // executor refuses to dispatch even if a client bypasses that.
            return;
                return;
            case "queue":
                return;
                return;
            case "card":
                if (parts.Length > 4 && parts[4] == "play")
                {
                    _controller.ActivateAccessibleControl(IslandPageControl.PlayPause, page);
                }
                else
                {
                    _controller.HandlePageRequest(PageRef.Media);
                }

                return;
            case "row":
                ExecuteRow(page, index, parts.Length > 5 ? parts[5] : null);
                return;
            case "task":
                _controller.ActivateAccessibleControl(IslandPageControl.TaskToggle, page, index);
                return;
            case "calendar" when parts.Length > 4:
                _controller.ActivateAccessibleControl(
                    parts[4] == "prev" ? IslandPageControl.CalendarPrevious : IslandPageControl.CalendarNext, page);
                return;
            default:
                return;
        }
    }

    private void ExecuteRow(int page, int index, string? action)
    {
        if (_controller.HasRenderState && _controller.RenderState.Pages.PluginPageAt(page) is not null)
        {
            _controller.ActivateAccessibleControl(
                action == "button" ? IslandPageControl.PluginRowButton : IslandPageControl.PluginRow,
                page,
                index);
            return;
        }

        var control = action switch
        {
            "dismiss" => IslandPageControl.Dismiss,
            "pin" => IslandPageControl.Pin,
            "copy" => IslandPageControl.Copy,
            _ => IslandPageControl.Row,
        };
        _controller.ActivateAccessibleControl(control, page, index);
    }

    private void RequestPage(int page)
    {
        if (!_controller.HasRenderState || page < 0 || page >= _controller.RenderState.Pages.PageCount)
        {
            return;
        }

        if (_controller.RenderState.Pages.PageSlotAt(page) is { PageId: { Length: > 0 } pageId })
        {
            _controller.HandlePageRequest(new PageRef(pageId));
        }
    }

    private void Activate(in UiaElementSnapshot node)
    {
        if (node.Role == UiaElementRole.TabItem)
        {
            if (int.TryParse(node.Id.AsSpan(PagePrefix.Length), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var page))
            {
                RequestPage(page);
            }

            return;
        }

        if ((node.Patterns & UiaPatternKind.Invoke) == 0)
        {
            return;
        }

        Execute(new UiaElementActionRequest(UiaElementAction.Invoke, node.Id));
    }

    private bool TryAdjustRange(in UiaElementSnapshot node, UiaKeyCommand command)
    {
        if ((node.Patterns & UiaPatternKind.RangeValue) == 0 || node.State.RangeValue is not { } value)
        {
            return false;
        }

        var step = Math.Max(1, (node.State.RangeMaximum - node.State.RangeMinimum) * 0.05);
        var next = command is UiaKeyCommand.Right or UiaKeyCommand.Up ? value + step : value - step;
        next = Math.Clamp(next, node.State.RangeMinimum, node.State.RangeMaximum);
        Execute(new UiaElementActionRequest(UiaElementAction.SetRangeValue, node.Id, next));
        return true;
    }

    private void MoveFocus(in UiaElementSnapshot node)
    {
        _focusedId = node.Id;
        BuildSnapshot();
        Changed?.Invoke();
    }

    private void FocusElement(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            _focusedId = null;
        }
        else
        {
            var root = BuildSnapshot();
            if (root.Find(id) is not { } node || !node.State.IsFocusable)
            {
                return;
            }

            _focusedId = id;
        }

        BuildSnapshot();
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ helpers

    private UiaElementSnapshot Node(
        string id,
        UiaElementRole role,
        string name,
        UiaRect bounds,
        UiaPatternKind patterns = UiaPatternKind.None,
        UiaElementState? state = null,
        string? help = null,
        IReadOnlyList<UiaElementSnapshot>? children = null)
    {
        var resolved = state ?? UiaElementState.Enabled;
        if (resolved.IsFocusable && _windowFocused
            && string.Equals(_focusedId, id, StringComparison.Ordinal))
        {
            resolved = resolved with { IsFocused = true };
        }

        return new UiaElementSnapshot
        {
            Id = id,
            Role = role,
            Name = name,
            BoundsDip = bounds,
            Patterns = patterns,
            State = resolved,
            HelpText = help,
            Children = children ?? [],
        };
    }

    private UiaElementSnapshot RangeNode(string id, string name, SKRect bounds, double value, string? itemStatus)
        => Node(
            id,
            UiaElementRole.Slider,
            name,
            ToRect(bounds),
            UiaPatternKind.RangeValue,
            new UiaElementState
            {
                IsFocusable = true,
                RangeValue = double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0,
                RangeMinimum = 0,
                RangeMaximum = 100,
                RangeSmallChange = 5,
                RangeLargeChange = 10,
                ItemStatus = itemStatus,
            },
            help: L("Island.Accessibility.SliderHelp",
                "Use the arrow keys or the RangeValue pattern to change the value."));

    /// <summary>
    /// Resolves the bounds a page tab reports inside the revealed strip.
    /// <para>
    /// The rail paints one target per neighbour and a <b>cluster</b> of far-page dots on each side.
    /// Every dot of a cluster jumps to the farthest page in its direction (prototype2), so a far tab
    /// has no painted element of its own and the two pages between it and the neighbour used to
    /// report an empty rectangle and an <c>Offscreen</c> state. A far tab now maps to the dot at its
    /// ordinal position in its cluster (nearest page ↔ nearest dot, clamped to the outermost dot for
    /// overflow pages), i.e. to the real hit rectangle of the element that represents its direction.
    /// SelectionItem/Invoke still selects the exact page, so the tab is reachable both by touch/click
    /// (jumps to the far page, exactly like clicking the dot) and by screen-reader activation.
    /// </para>
    /// </summary>
    private static UiaRect TabBounds(in SkiaStripGeometry strip, int page, bool visible)
    {
        if (!visible)
        {
            return UiaRect.Empty;
        }

        if (page == strip.Page)
        {
            return ToRect(strip.Current);
        }

        if (page == strip.Page - 1)
        {
            return ToRect(strip.Previous);
        }

        if (page == strip.Page + 1)
        {
            return ToRect(strip.Next);
        }

        return page < strip.Page - 1
            ? ToRect(DotHitBounds(strip.DotsLeft, strip.Page - page - 2))
            : ToRect(DotHitBounds(strip.DotsRight, page - strip.Page - 2));
    }

    /// <summary>Hit rectangle of the dot at an ordinal position (the outermost dot absorbs overflow).</summary>
    private static SKRect DotHitBounds(SKRect[] dots, int index)
        => dots.Length == 0
            ? SKRect.Empty
            : IslandStripLayout.DotHitRect(dots[Math.Clamp(index, 0, dots.Length - 1)]);

    private static SKRect InflateVertical(SKRect rect, float amount)
        => new(rect.Left, rect.Top - amount, rect.Right, rect.Bottom + amount);

    private static bool Intersects(SKRect a, SKRect b)
        => a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;

    private static UiaRect ToRect(SKRect rect)
        => rect.Width <= 0 || rect.Height <= 0
            ? UiaRect.Empty
            : new UiaRect(rect.Left, rect.Top, rect.Width, rect.Height);

    private string CompactName(in IslandRenderState state) => CompactName(state.Compact);

    private string CompactName(CompactActivity? activity)
    {
        var model = activity?.Model;
        if (model is null)
        {
            return L("Activity.Idle", "Idle");
        }

        var name = JoinNotEmpty(" · ", model.Title, model.Subtitle, model.Body, model.Badge,
            model.AppName, model.ClockText, model.StatusText, model.VerificationCode,
            model.TimerText, model.DurationText);
        return name.Length > 0 ? name : L("Activity.Idle", "Idle");
    }

    private static string JoinNotEmpty(string separator, params string?[] values)
        => string.Join(separator, values.Where(static value => !string.IsNullOrWhiteSpace(value)));

    private string L(string key, string fallback)
        => _localizer.TryGet(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private string F(string key, string fallback, params object?[] arguments)
        => string.Format(_localizer.CurrentCulture, L(key, fallback), arguments);
}




