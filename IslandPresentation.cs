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
    Clock
}

public enum IslandDisplayMode
{
    Compact,
    HoverCard
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
    double CollapsedHeight);

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
    IslandDisplayMode Mode)
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
            IslandViewKind.Progress => 176,
            IslandViewKind.Status => 184,
            IslandViewKind.Clock => 176,
            _ => 178
        };
        var detailLines = compact.Kind is IslandViewKind.ScrollingText or IslandViewKind.Text
            ? 5
            : 3;

        return new HoverCardPresentation(
            compact.Kind,
            compact.IconKind,
            compact.Title,
            compact.Content,
            compact.StatusText,
            compact.StatusBadge,
            compact.ProgressPercent,
            compact.ShowsAudioWave,
            targetWidth,
            targetHeight,
            detailLines,
            compact.Kind is IslandViewKind.ScrollingText or IslandViewKind.Text,
            IslandDisplayMode.HoverCard);
    }
}

public static class IslandPresentationFactory
{
    public const double MinimumCollapsedWidth = 126;
    public const double MinimumCollapsedHeight = 38;
    public const double MinimumExpandedWidth = 260;
    public const double MinimumExpandedHeight = 56;
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

        var kind = ResolveKind(iconKind, evt.Content, scrollThreshold);
        var targetWidth = ResolveWidth(kind, evt, collapsedWidth, targetConfiguredWidth);
        var progressPercent = kind == IslandViewKind.Progress
            ? ParsePercent(evt.Content)
            : 0;
        var status = ResolveStatus(iconKind, evt.Content);

        return new IslandViewPresentation(
            kind,
            iconKind,
            evt.Title,
            evt.Content,
            status.Text,
            status.Badge,
            progressPercent,
            iconKind is "volume" or "volume_mute",
            targetWidth,
            expandedHeight,
            collapsedWidth,
            collapsedHeight);
    }

    public static int ParsePercent(string content)
    {
        var match = Regex.Match(content, @"(\d{1,3})\s*%");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var p))
            return Math.Clamp(p, 0, 100);
        return 0;
    }

    private static IslandViewKind ResolveKind(
        string iconKind,
        string content,
        int scrollThreshold)
    {
        return iconKind switch
        {
            "volume" or "volume_mute" or "brightness" => IslandViewKind.Progress,
            "battery" or "battery_charge" or "battery_low" or "network" or
                "network_off" or "usb" or "bluetooth" => IslandViewKind.Status,
            "lockkey" => IslandViewKind.LockKey,
            "inputmethod" => IslandViewKind.InputMethod,
            "clock" => IslandViewKind.Clock,
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
            IslandViewKind.ScrollingText => Math.Min(430, 270 + evt.Content.Length * 1.8),
            _ => Math.Min(360, 244 + evt.Content.Length * 5.2)
        };

        return Math.Max(Math.Max(minimum, configuredWidth), contentMinimum);
    }

    private static (string Text, string Badge) ResolveStatus(string iconKind, string content)
    {
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
    DateTimeOffset CreatedAt);

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
            return currentItems.ToList();

        if (!CanStack(settings))
        {
            return new[]
            {
                new IslandStackItem(source, nextView, DateTimeOffset.UtcNow)
            };
        }

        var next = currentItems
            .Where(item => item.View.Kind != IslandViewKind.Clock && item.Source != source)
            .ToList();
        next.Add(new IslandStackItem(source, nextView, DateTimeOffset.UtcNow));

        var max = Math.Clamp(settings.MaxVisibleIslands, 1, 8);
        if (next.Count > max)
            next.RemoveRange(0, next.Count - max);

        return next;
    }
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
