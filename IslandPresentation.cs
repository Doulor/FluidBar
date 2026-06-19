using System.IO;
using System.Text.RegularExpressions;

namespace FluidBar;

public enum IslandViewKind
{
    Text,
    ScrollingText,
    Progress,
    Status,
    LockKey,
    InputMethod,
    Clock,
    Media,
    Notification,
    Agent
}

public enum IslandDisplayMode
{
    Compact,
    HoverCard
}

public enum IslandMediaIconKind
{
    DefaultGlyph,
    Artwork,
    AppIcon
}

public sealed record IslandMediaIconChoice(IslandMediaIconKind Kind, string? Path)
{
    public bool UsesImage => Kind != IslandMediaIconKind.DefaultGlyph && !string.IsNullOrWhiteSpace(Path);
}

public sealed record IslandMediaImageMetrics(double ImageWidth, double ImageHeight, bool CropsToCircle);

public static class IslandMediaVisualPolicy
{
    public static IslandMediaIconChoice ChooseIcon(
        string? albumArtPath,
        string? appIconPath,
        Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;

        if (IsUsablePath(albumArtPath, fileExists))
            return new IslandMediaIconChoice(IslandMediaIconKind.Artwork, albumArtPath);

        if (IsUsablePath(appIconPath, fileExists))
            return new IslandMediaIconChoice(IslandMediaIconKind.AppIcon, appIconPath);

        return new IslandMediaIconChoice(IslandMediaIconKind.DefaultGlyph, null);
    }

    public static IslandMediaImageMetrics ResolveImageMetrics(IslandMediaIconKind iconKind)
    {
        return iconKind switch
        {
            IslandMediaIconKind.Artwork => new IslandMediaImageMetrics(34, 34, true),
            IslandMediaIconKind.AppIcon => new IslandMediaImageMetrics(31, 31, false),
            _ => new IslandMediaImageMetrics(20, 20, false)
        };
    }

    private static bool IsUsablePath(string? path, Func<string, bool> fileExists)
    {
        return !string.IsNullOrWhiteSpace(path) && fileExists(path);
    }
}

public sealed record MediaHoverTransportLayout(
    double ProgressBottomFromBottom,
    double ProgressTopFromBottom,
    double ProgressHeight,
    double ControlsBottomFromBottom);

public static class MediaLayoutPolicy
{
    private const double PillHorizontalPadding = 24;
    private const double CompactIconSlotWidth = 50;
    private const double CompactWaveSlotWidth = 50;
    private const double CompactTrailingReserve = 30;

    public static double CompactContentWidth(double targetWidth, bool showsAudioWave)
    {
        var reserved = PillHorizontalPadding + CompactIconSlotWidth + CompactTrailingReserve;
        if (showsAudioWave)
            reserved += CompactWaveSlotWidth;

        var available = targetWidth - reserved;
        return Math.Max(72, available);
    }

    public static double CompactTextWidth(double targetWidth, bool showsAudioWave) =>
        CompactContentWidth(targetWidth, showsAudioWave);

    public static double CompactProgressWidth(double targetWidth, bool showsAudioWave) =>
        CompactContentWidth(targetWidth, showsAudioWave);

    public static MediaHoverTransportLayout HoverTransportLayout()
    {
        const double progressHeight = 8;
        const double progressBottom = 0;
        const double controlsBottom = 24;

        return new MediaHoverTransportLayout(
            progressBottom,
            progressBottom + progressHeight,
            progressHeight,
            controlsBottom);
    }
}

public sealed record IslandAnimationPerformanceProfile(
    int OpenMilliseconds,
    int ResizeMilliseconds,
    int PositionMilliseconds,
    int OpenScaleMilliseconds,
    int ResizeScaleMilliseconds,
    int ContentOpenMilliseconds,
    int ContentResizeMilliseconds,
    double HoverFrameApplyThreshold,
    bool UsesElasticShellEase);

public static class IslandAnimationPerformancePolicy
{
    public static IslandAnimationPerformanceProfile Default { get; } = new(
        OpenMilliseconds: 260,
        ResizeMilliseconds: 220,
        PositionMilliseconds: 240,
        OpenScaleMilliseconds: 260,
        ResizeScaleMilliseconds: 220,
        ContentOpenMilliseconds: 180,
        ContentResizeMilliseconds: 150,
        HoverFrameApplyThreshold: 0.75,
        UsesElasticShellEase: false);
}

public static class MediaPlaybackUiPolicy
{
    public const string PlayGlyph = "\uE768";
    public const string PauseGlyph = "\uE769";

    public static string PlayPauseGlyph(bool isPlaying) => isPlaying ? PauseGlyph : PlayGlyph;

    public static bool ShouldKeepHoverCardForInactiveMedia(
        bool isHoverCard,
        string? sourceName = null)
    {
        if (!isHoverCard)
            return false;

        return MediaSnapshotSelectionPolicy.GetSourcePriority(sourceName) < 100;
    }

    public static bool ShouldShowTransportControls(IslandViewKind kind) =>
        kind == IslandViewKind.Media;
}

public enum HoverCardMotionKind
{
    WarpOpen,
    WarpClose
}

public sealed record HoverCardMotionPlan(
    HoverCardMotionKind Kind,
    double FromWidth,
    double FromHeight,
    double ToWidth,
    double ToHeight,
    int DurationMilliseconds,
    int WidthKeyFrames,
    int HeightKeyFrames,
    int HeightDelayMilliseconds,
    bool UsesOvershoot,
    double OvershootRatio,
    int ContentRevealDelayMilliseconds,
    bool UsesContinuousSpring,
    bool UsesRenderSynchronizedFrames,
    bool AnimatesWindowBoundsEveryFrame,
    double ExpandingStiffness,
    double ExpandingDamping,
    double ContractingStiffness,
    double ContractingDamping)
{
    public static HoverCardMotionPlan CreateOpening(
        double fromWidth,
        double fromHeight,
        double toWidth,
        double toHeight)
    {
        return new HoverCardMotionPlan(
            HoverCardMotionKind.WarpOpen,
            fromWidth,
            fromHeight,
            toWidth,
            toHeight,
            0,
            0,
            0,
            0,
            false,
            0,
            115,
            true,
            true,
            false,
            380,
            26,
            200,
            28);
    }

    public static HoverCardMotionPlan CreateClosing(
        double fromWidth,
        double fromHeight,
        double toWidth,
        double toHeight)
    {
        return new HoverCardMotionPlan(
            HoverCardMotionKind.WarpClose,
            fromWidth,
            fromHeight,
            toWidth,
            toHeight,
            0,
            0,
            0,
            0,
            false,
            0,
            0,
            true,
            true,
            false,
            300,
            27,
            200,
            30);
    }
}

public sealed class SpringValue
{
    public double Value { get; private set; }
    public double Velocity { get; private set; }
    public double Target { get; set; }

    public void Reset(double value)
    {
        Value = value;
        Target = value;
        Velocity = 0;
    }

    public void Step(double dt, double stiffness, double damping)
    {
        dt = Math.Clamp(dt, 0.001, 0.050);
        var displacement = Value - Target;
        var acceleration = -stiffness * displacement - damping * Velocity;
        Velocity += acceleration * dt;
        Value += Velocity * dt;

        if (Math.Abs(Value - Target) < 0.01 && Math.Abs(Velocity) < 0.01)
        {
            Value = Target;
            Velocity = 0;
        }
    }

    public bool IsSettled => Math.Abs(Value - Target) < 0.01 && Math.Abs(Velocity) < 0.01;
}

public sealed record IslandViewPresentation(
    IslandViewKind Kind,
    string IconKind,
    string Title,
    string Content,
    string StatusText,
    string StatusBadge,
    int ProgressPercent,
    bool ShowsAudioWave,
    double TargetWidth,
    double TargetHeight,
    double CollapsedWidth,
    double CollapsedHeight,
    string Subtitle = "",
    string SourceName = "",
    string LyricLine = "",
    string SecondaryLyricLine = "",
    IReadOnlyList<string>? DetailLines = null,
    long PositionTicks = 0,
    long EndTicks = 0,
    long StartTimeTicks = 0,
    long LastUpdatedTicks = 0,
    string? AlbumArtPath = null,
    string? AppIconPath = null);

public sealed record HoverCardPresentation(
    IslandViewKind Kind,
    string IconKind,
    string Title,
    string Content,
    string StatusText,
    string StatusBadge,
    int ProgressPercent,
    bool ShowsAudioWave,
    double TargetWidth,
    double TargetHeight,
    int DetailLines,
    bool AllowsMultilineContent,
    IslandDisplayMode Mode,
    string Subtitle = "",
    string SourceName = "",
    string LyricLine = "",
    string SecondaryLyricLine = "",
    long PositionTicks = 0,
    long EndTicks = 0,
    long StartTimeTicks = 0,
    long LastUpdatedTicks = 0,
    string? AlbumArtPath = null,
    string? AppIconPath = null)
{
    public static HoverCardPresentation FromCompact(
        IslandViewPresentation compact,
        FluidBarSettings settings)
    {
        var baseWidth = Math.Max(settings.ExpandedMaxWidth, compact.TargetWidth);
        var maxCardWidth = Math.Clamp(settings.ExpandedMaxWidth + 130, 460, 760);
        var targetWidth = Math.Clamp(
            Math.Max(baseWidth, compact.TargetWidth + 120),
            360,
            maxCardWidth);
        var targetHeight = compact.Kind switch
        {
            IslandViewKind.ScrollingText or IslandViewKind.Text => 210,
            IslandViewKind.Media => string.IsNullOrWhiteSpace(compact.LyricLine) ? 206 : 232,
            IslandViewKind.Notification => 218,
            IslandViewKind.Agent => 198,
            IslandViewKind.Progress => 176,
            IslandViewKind.Status => 184,
            IslandViewKind.Clock => 176,
            _ => 178
        };
        var detailLines = compact.Kind switch
        {
            IslandViewKind.ScrollingText or IslandViewKind.Text => 5,
            IslandViewKind.Notification => 5,
            IslandViewKind.Media => string.IsNullOrWhiteSpace(compact.LyricLine) ? 4 : 5,
            IslandViewKind.Agent => 4,
            _ => 3
        };
        var body = BuildHoverBody(compact);

        return new HoverCardPresentation(
            compact.Kind,
            compact.IconKind,
            compact.Title,
            body,
            compact.StatusText,
            compact.StatusBadge,
            compact.ProgressPercent,
            compact.ShowsAudioWave,
            targetWidth,
            targetHeight,
            detailLines,
            compact.Kind is IslandViewKind.ScrollingText or IslandViewKind.Text or
                IslandViewKind.Notification or IslandViewKind.Agent or IslandViewKind.Media,
            IslandDisplayMode.HoverCard,
            compact.Subtitle,
            compact.SourceName,
            compact.LyricLine,
            compact.SecondaryLyricLine,
            compact.PositionTicks,
            compact.EndTicks,
            compact.StartTimeTicks,
            compact.LastUpdatedTicks,
            compact.AlbumArtPath,
            compact.AppIconPath);
    }

    private static string BuildHoverBody(IslandViewPresentation compact)
    {
        var lines = new List<string>();
        if (compact.Kind != IslandViewKind.Media &&
            !string.IsNullOrWhiteSpace(compact.Content))
        {
            lines.Add(compact.Content);
        }

        // For media: lyrics appear in the subtitle area, skip them in body
        if (compact.Kind != IslandViewKind.Media)
        {
            if (!string.IsNullOrWhiteSpace(compact.LyricLine))
                lines.Add(compact.LyricLine);
            if (!string.IsNullOrWhiteSpace(compact.SecondaryLyricLine))
                lines.Add(compact.SecondaryLyricLine);
        }
        else
        {
            // For media without lyrics, show artist·album info in body
            if (string.IsNullOrWhiteSpace(compact.LyricLine) &&
                !string.IsNullOrWhiteSpace(compact.Subtitle))
            {
                lines.Add(compact.Subtitle);
            }
        }

        if (compact.DetailLines is not null)
            lines.AddRange(compact.DetailLines.Where(line => !string.IsNullOrWhiteSpace(line)));
        return string.Join(Environment.NewLine, lines.Distinct());
    }
}

public static class IslandPresentationFactory
{
    public const double MinimumCollapsedWidth = 126;
    public const double MinimumCollapsedHeight = 38;
    public const double MinimumExpandedWidth = 260;
    public const double MinimumExpandedHeight = 48;
    public const double MaximumExpandedHeight = 96;
    private const int DefaultScrollThreshold = 20;

    public static IslandViewPresentation FromEvent(
        IslandEvent evt,
        FluidBarSettings settings,
        int scrollThreshold = DefaultScrollThreshold)
    {
        var iconKind = string.IsNullOrWhiteSpace(evt.IconKind) ? "info" : evt.IconKind!;
        var collapsedWidth = Math.Max(settings.CollapsedWidth, MinimumCollapsedWidth);
        var collapsedHeight = Math.Max(settings.CollapsedHeight, MinimumCollapsedHeight);
        var targetConfiguredWidth = Math.Max(settings.ExpandedMaxWidth, MinimumExpandedWidth);
        var expandedHeight = Math.Clamp(
            Math.Max(settings.ExpandedHeight, MinimumExpandedHeight),
            MinimumExpandedHeight,
            MaximumExpandedHeight);

        var kind = ResolveKind(evt, iconKind, evt.Content, scrollThreshold);
        var targetWidth = ResolveWidth(kind, evt, collapsedWidth, targetConfiguredWidth);
        var rawProgress = evt.Payload?.ProgressPercent;
        var progressPercent = rawProgress is int payloadPercent
            ? (payloadPercent < 0 ? -1 : Math.Clamp(payloadPercent, 0, 100))
            : kind == IslandViewKind.Progress
                ? ParsePercent(evt.Content)
                : 0;
        var status = ResolveStatus(evt, iconKind, evt.Content);
        var payload = evt.Payload;

        return new IslandViewPresentation(
            kind,
            iconKind,
            evt.Title,
            evt.Content,
            status.Text,
            status.Badge,
            progressPercent,
            payload?.ShowsAudioWave ?? iconKind is "volume" or "volume_mute",
            targetWidth,
            expandedHeight,
            collapsedWidth,
            collapsedHeight,
            payload?.Subtitle ?? "",
            payload?.SourceName ?? "",
            payload?.LyricLine ?? "",
            payload?.SecondaryLyricLine ?? "",
            payload?.DetailLines,
            payload?.PositionTicks ?? 0,
            payload?.EndTicks ?? 0,
            payload?.StartTimeTicks ?? 0,
            payload?.LastUpdatedTicks ?? 0,
            payload?.AlbumArtPath,
            payload?.AppIconPath);
    }

    public static int ParsePercent(string content)
    {
        var match = Regex.Match(content, @"(\d{1,3})\s*%");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var p))
            return Math.Clamp(p, 0, 100);
        return 0;
    }

    private static IslandViewKind ResolveKind(
        IslandEvent evt,
        string iconKind,
        string content,
        int scrollThreshold)
    {
        if (evt.Payload?.Kind is { } payloadKind && payloadKind != IslandEventKind.Auto)
        {
            return payloadKind switch
            {
                IslandEventKind.Text => IslandViewKind.Text,
                IslandEventKind.ScrollingText => IslandViewKind.ScrollingText,
                IslandEventKind.Progress => IslandViewKind.Progress,
                IslandEventKind.Status => IslandViewKind.Status,
                IslandEventKind.Media => IslandViewKind.Media,
                IslandEventKind.Notification => IslandViewKind.Notification,
                IslandEventKind.Agent => IslandViewKind.Agent,
                _ => IslandViewKind.Text
            };
        }

        return iconKind switch
        {
            "volume" or "volume_mute" or "brightness" => IslandViewKind.Progress,
            "battery" or "battery_charge" or "battery_low" or "network" or
                "network_off" or "usb" or "bluetooth" => IslandViewKind.Status,
            "lockkey" => IslandViewKind.LockKey,
            "inputmethod" => IslandViewKind.InputMethod,
            "clock" => IslandViewKind.Clock,
            "media" => IslandViewKind.Media,
            "notification" => IslandViewKind.Notification,
            "agent" => IslandViewKind.Agent,
            _ when content.Length > scrollThreshold => IslandViewKind.ScrollingText,
            _ => IslandViewKind.Text
        };
    }

    private static double ResolveWidth(
        IslandViewKind kind,
        IslandEvent evt,
        double collapsedWidth,
        double configuredWidth)
    {
        var minimum = Math.Max(collapsedWidth, MinimumExpandedWidth);
        var contentMinimum = kind switch
        {
            IslandViewKind.Progress => 342,
            IslandViewKind.Status => 336,
            IslandViewKind.LockKey => 282,
            IslandViewKind.InputMethod => 214,
            IslandViewKind.Clock => 244,
            IslandViewKind.Media => MinimumExpandedWidth,
            IslandViewKind.Notification => 360,
            IslandViewKind.Agent => 352,
            IslandViewKind.ScrollingText => Math.Min(430, 270 + evt.Content.Length * 1.8),
            _ => Math.Min(360, 244 + evt.Content.Length * 5.2)
        };

        return Math.Max(Math.Max(minimum, configuredWidth), contentMinimum);
    }

    private static (string Text, string Badge) ResolveStatus(
        IslandEvent evt,
        string iconKind,
        string content)
    {
        if (evt.Payload is { } payload && payload.Kind != IslandEventKind.Auto)
        {
            var text = string.Join(" · ", new[]
            {
                payload.Subtitle,
                string.IsNullOrWhiteSpace(payload.LyricLine) ? null : payload.LyricLine
            }.Where(part => !string.IsNullOrWhiteSpace(part)));
            if (string.IsNullOrWhiteSpace(text))
                text = content;
            var badge = !string.IsNullOrWhiteSpace(payload.Badge)
                ? payload.Badge!
                : !string.IsNullOrWhiteSpace(payload.SourceName)
                    ? payload.SourceName!
                    : "就绪";
            var keepBadgeStandalone =
                payload.Kind == IslandEventKind.Media &&
                BrowserMediaSitePolicy.IsBrowserSourceName(payload.SourceName) &&
                BrowserMediaSitePolicy.IsKnownSiteDisplayName(badge);
            if (!keepBadgeStandalone &&
                !string.IsNullOrWhiteSpace(payload.SourceName) &&
                !badge.Contains(payload.SourceName, StringComparison.Ordinal))
            {
                badge = $"{payload.SourceName} · {badge}";
            }
            return (text, badge);
        }

        return iconKind switch
        {
            "battery_charge" => (
                EnsurePhrase(content, "充电中"),
                "外接电源 · 充电中"),
            "battery_low" => (
                EnsurePhrase(content, "请尽快充电"),
                "低电量"),
            "battery" => (
                content.Contains("电池供电", StringComparison.Ordinal)
                    ? content
                    : EnsurePhrase(content, "电池供电中"),
                "电池供电"),
            "network" => (content, "在线"),
            "network_off" => (content, "离线"),
            "usb" => (content, "USB"),
            "bluetooth" => (content, "蓝牙"),
            _ => (content, "就绪")
        };
    }

    private static string EnsurePhrase(string content, string phrase)
    {
        if (content.Contains(phrase, StringComparison.Ordinal))
            return content;
        if (string.IsNullOrWhiteSpace(content))
            return phrase;
        return $"{content} · {phrase}";
    }
}

public static class IslandPresentation
{
    public static IslandViewPresentation FromEvent(
        IslandEvent evt,
        FluidBarSettings settings,
        int scrollThreshold = 20)
    {
        return IslandPresentationFactory.FromEvent(evt, settings, scrollThreshold);
    }
}

public sealed record ScrollingTextMotionPlan(double InitialOffset, int HoldMilliseconds)
{
    public static ScrollingTextMotionPlan CreateInitial()
    {
        return new ScrollingTextMotionPlan(0, 500);
    }
}

public static class HoverCardPolicy
{
    public static bool CanShow(
        bool isExpanded,
        bool isSettingsPanelOpen,
        string? currentSource,
        bool currentViewExists,
        FluidBarSettings settings)
    {
        if (!isExpanded || !currentViewExists || isSettingsPanelOpen)
            return false;

        if (string.IsNullOrWhiteSpace(currentSource))
            return false;

        if (currentSource == "app")
            return true;

        if (currentSource == "clipboard")
            return true;

        return settings.GetMonitorFeatureSettings(currentSource).HoverCardEnabled;
    }
}

public sealed record IslandStackItem(
    string Source,
    IslandViewPresentation View,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt = default);

public static class IslandStackPolicy
{
    public static bool CanStack(FluidBarSettings settings)
    {
        return settings.DisplayStrategy == IslandDisplayStrategy.Multiple;
    }

    public static IReadOnlyList<IslandStackItem> Apply(
        IEnumerable<IslandStackItem> currentItems,
        IslandViewPresentation nextView,
        string source,
        FluidBarSettings settings)
    {
        if (nextView.Kind == IslandViewKind.Clock || source == "clock")
            return PruneExpiredItems(currentItems, DateTimeOffset.UtcNow).ToList();

        if (!CanStack(settings))
        {
            return new[]
            {
                new IslandStackItem(source, nextView, DateTimeOffset.UtcNow)
            };
        }

        var now = DateTimeOffset.UtcNow;
        var next = PruneExpiredItems(currentItems, now)
            .Where(item => item.View.Kind != IslandViewKind.Clock && item.Source != source)
            .ToList();

        // Non-media events expire after 5 seconds; media stays indefinitely
        var expires = source == "media"
            ? DateTimeOffset.MaxValue
            : now.AddSeconds(5);
        next.Add(new IslandStackItem(source, nextView, now, expires));

        var max = Math.Clamp(settings.MaxVisibleIslands, 1, 8);
        if (next.Count > max)
            next.RemoveRange(0, next.Count - max);

        return next;
    }

    public static IReadOnlyList<IslandStackItem> PinSourceAsLatest(
        IEnumerable<IslandStackItem> currentItems,
        string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return PruneExpiredItems(currentItems, DateTimeOffset.UtcNow).ToList();

        var items = PruneExpiredItems(currentItems, DateTimeOffset.UtcNow).ToList();
        var index = items.FindIndex(item => item.Source == source);
        if (index < 0 || index == items.Count - 1)
            return items;

        var pinned = items[index];
        items.RemoveAt(index);
        items.Add(pinned);
        return items;
    }

    public static IEnumerable<IslandStackItem> PruneExpiredItems(
        IEnumerable<IslandStackItem> items,
        DateTimeOffset now)
    {
        return items.Where(item =>
            item.ExpiresAt == default || item.ExpiresAt == DateTimeOffset.MaxValue || item.ExpiresAt > now);
    }
}

public static class HoldToHideKeyPolicy
{
    public const string Disabled = "Disabled";
    public const string LeftAlt = "LeftAlt";
    public const string RightAlt = "RightAlt";
    public const string LeftCtrl = "LeftCtrl";
    public const string RightCtrl = "RightCtrl";
    public const string LeftShift = "LeftShift";
    public const string RightShift = "RightShift";

    public static IReadOnlyList<string> Values { get; } =
    [
        Disabled,
        LeftAlt,
        RightAlt,
        LeftCtrl,
        RightCtrl,
        LeftShift,
        RightShift
    ];

    public static string Coerce(string? value)
    {
        return Values.Contains(value) ? value! : LeftAlt;
    }

    public static string DisplayName(string value)
    {
        return Coerce(value) switch
        {
            Disabled => "不启用",
            RightAlt => "右 Alt",
            LeftCtrl => "左 Ctrl",
            RightCtrl => "右 Ctrl",
            LeftShift => "左 Shift",
            RightShift => "右 Shift",
            _ => "左 Alt"
        };
    }

    public static int VirtualKey(string value)
    {
        return Coerce(value) switch
        {
            RightAlt => 0xA5,
            LeftCtrl => 0xA2,
            RightCtrl => 0xA3,
            LeftShift => 0xA0,
            RightShift => 0xA1,
            Disabled => 0,
            _ => 0xA4
        };
    }

    public static bool ShouldHide(
        string configuredKey,
        bool configuredKeyDown,
        bool leftCtrlDown,
        bool rightCtrlDown,
        bool leftAltDown,
        bool rightAltDown)
    {
        if (Coerce(configuredKey) == Disabled || !configuredKeyDown)
            return false;

        return !((leftCtrlDown || rightCtrlDown) && (leftAltDown || rightAltDown));
    }
}

public static class SettingsPerformancePolicy
{
    public const int SettingsApplyDebounceMs = 120;
    public const int SettingsSaveDebounceMs = 220;
    public const int PluginSaveDebounceMs = 280;
    public const bool UseVirtualizedLists = true;
}

public static class IslandStackVisibilityPolicy
{
    public static bool ShouldRender(
        FluidBarSettings settings,
        int stackCount,
        bool isSettingsPanelOpen,
        IslandViewKind? currentKind)
    {
        return settings.DisplayStrategy == IslandDisplayStrategy.Multiple
            && stackCount > 1
            && !isSettingsPanelOpen
            && currentKind != IslandViewKind.Clock;
    }
}

public sealed record IslandSlotMetrics(double Width, double Height);

public sealed record IslandSlotLayout(
    double OffsetX,
    double OffsetY,
    double Width,
    double Height);

public sealed record IslandGroupLayoutResult(
    double Left,
    double Top,
    double VisualWidth,
    double VisualHeight,
    IReadOnlyList<IslandSlotLayout> Slots);

public static class IslandGroupLayout
{
    private const double EdgeX = 16;
    private const double TopY = 8;
    private const double BottomY = 12;
    private const double ScreenMargin = 8;

    public static IslandGroupLayoutResult Calculate(
        IReadOnlyList<IslandSlotMetrics> slots,
        string position,
        double screenWidth,
        double screenHeight,
        double offsetX,
        double offsetY,
        double gap)
    {
        if (slots.Count == 0)
        {
            return new IslandGroupLayoutResult(
                EdgeX + offsetX,
                TopY + offsetY,
                0,
                0,
                Array.Empty<IslandSlotLayout>());
        }

        gap = Math.Max(0, gap);
        var visualWidth = slots.Sum(slot => slot.Width) + gap * Math.Max(0, slots.Count - 1);
        var visualHeight = slots.Max(slot => slot.Height);
        var left = ResolveLeft(position, screenWidth, visualWidth, offsetX);
        var top = ResolveTop(position, screenHeight, visualHeight, offsetY);

        left = Math.Clamp(left, ScreenMargin, Math.Max(ScreenMargin, screenWidth - visualWidth - ScreenMargin));
        top = Math.Clamp(top, ScreenMargin, Math.Max(ScreenMargin, screenHeight - visualHeight - ScreenMargin));

        var layouts = new List<IslandSlotLayout>(slots.Count);
        var x = 0.0;
        foreach (var slot in slots)
        {
            layouts.Add(new IslandSlotLayout(
                x,
                Math.Max(0, (visualHeight - slot.Height) / 2),
                slot.Width,
                slot.Height));
            x += slot.Width + gap;
        }

        return new IslandGroupLayoutResult(left, top, visualWidth, visualHeight, layouts);
    }

    private static double ResolveLeft(string position, double screenWidth, double width, double offsetX)
    {
        return position switch
        {
            "TopLeft" or "BottomLeft" => EdgeX + offsetX,
            "TopRight" or "BottomRight" => screenWidth - width - EdgeX + offsetX,
            _ => (screenWidth - width) / 2 + offsetX
        };
    }

    private static double ResolveTop(string position, double screenHeight, double height, double offsetY)
    {
        return position switch
        {
            "Bottom" or "BottomLeft" or "BottomRight" => screenHeight - height - BottomY + offsetY,
            _ => TopY + offsetY
        };
    }
}
