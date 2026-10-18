using System.Collections.Immutable;
using FluidBar.Core.Activities;

namespace FluidBar.Core.Presentation;

/// <summary>Icon identity resolved by the presentation layer; the UI maps it to a font glyph/vector.</summary>
public enum ActivityIconKind
{
    /// <summary>Idle clock surface.</summary>
    Clock,

    /// <summary>Media / now playing.</summary>
    Media,

    /// <summary>Clipboard.</summary>
    Clipboard,

    /// <summary>Notification bell.</summary>
    Notification,

    /// <summary>Volume (HUD).</summary>
    Volume,

    /// <summary>Brightness (HUD).</summary>
    Brightness,

    /// <summary>Battery / charging.</summary>
    Battery,

    /// <summary>Timer / pomodoro.</summary>
    Timer,

    /// <summary>Caps/Num/Scroll lock.</summary>
    LockKeys,

    /// <summary>USB device.</summary>
    Device,

    /// <summary>Bluetooth.</summary>
    Bluetooth,

    /// <summary>Focus assist.</summary>
    Focus,

    /// <summary>Progress / download.</summary>
    Progress,

    /// <summary>Lookup / search.</summary>
    Search,

    /// <summary>Agent.</summary>
    Agent,
}

/// <summary>
/// Accent selection rule (spec 3.3: one accent per state). The UI resolves the actual color;
/// the core only states which rule applies and, when artwork-derived, carries the hex value.
/// </summary>
public enum AccentRule
{
    /// <summary>No accent (neutral foreground).</summary>
    Neutral,

    /// <summary>Accent extracted from album artwork.</summary>
    Artwork,

    /// <summary>Windows system accent color.</summary>
    SystemAccent,

    /// <summary>Theme accent (fallback).</summary>
    ThemeAccent,

    /// <summary>Informational blue.</summary>
    Info,

    /// <summary>Success green.</summary>
    Success,

    /// <summary>Warning orange.</summary>
    Warning,

    /// <summary>Danger red (low battery, failures).</summary>
    Danger,
}

/// <summary>Single-line HUD view model for volume/brightness (spec 2.8). No UI types.</summary>
public sealed record HudViewModel
{
    /// <summary>Localization key for the label, e.g. <c>Hud.Volume</c>.</summary>
    public required string LabelKey { get; init; }

    /// <summary>English fallback label for diagnostics/tests.</summary>
    public required string LabelFallback { get; init; }

    /// <summary>Numeric value 0..100.</summary>
    public required int Value { get; init; }

    /// <summary>Whether the surface is muted (volume) or at minimum (brightness).</summary>
    public bool IsMuted { get; init; }

    /// <summary>Icon fill level 0..3 (mute/low/medium/high).</summary>
    public required int IconLevel { get; init; }

    /// <summary>Invariant numeric text (the host applies locale number formatting and the unit).</summary>
    public required string ValueText { get; init; }
}

/// <summary>
/// UI-free view model of one activity: icon kind, accent rule, title/content/subtitle slots,
/// progress, wave and priority. Produced only by <see cref="ActivityPresenter"/>.
/// </summary>
public sealed record ActivityViewModel
{
    /// <summary>Activity identity.</summary>
    public required ActivityKey Key { get; init; }

    /// <summary>Activity class.</summary>
    public required ActivityKind Kind { get; init; }

    /// <summary>Icon identity.</summary>
    public required ActivityIconKind Icon { get; init; }

    /// <summary>Accent rule.</summary>
    public required AccentRule Accent { get; init; }

    /// <summary>Localization key for the kind label (host localizes when <see cref="Title"/> is empty).</summary>
    public required string KindLabelKey { get; init; }

    /// <summary>Primary slot text (raw user/plugin data, never translated).</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Secondary slot text (artist, source app...).</summary>
    public string? Subtitle { get; init; }

    /// <summary>Body/content slot (notification body, clipboard preview...).</summary>
    public string? Body { get; init; }

    /// <summary>Badge text (verification code, +N...).</summary>
    public string? Badge { get; init; }

    /// <summary>Source caption.</summary>
    public string? SourceName { get; init; }

    /// <summary>Expanded page this activity belongs to.</summary>
    public required IslandPage Page { get; init; }

    /// <summary>Determinate progress 0..100, or <see langword="null"/> when absent.</summary>
    public int? ProgressPercent { get; init; }

    /// <summary>Indeterminate progress.</summary>
    public bool ProgressIndeterminate { get; init; }

    /// <summary>Whether the audio wave should animate.</summary>
    public bool ShowsWave { get; init; }

    /// <summary>Playing/charging/active flag.</summary>
    public bool IsActive { get; init; }

    /// <summary>Priority rank (lower wins).</summary>
    public required int PriorityRank { get; init; }

    /// <summary>Session pin flag.</summary>
    public bool IsPinned { get; init; }

    /// <summary>Stack index (0 = primary).</summary>
    public int StackIndex { get; init; }

    /// <summary>Whether this view model is a ghost layer.</summary>
    public bool IsGhost { get; init; }

    /// <summary>Artwork-derived accent hex, when <see cref="Accent"/> is <see cref="AccentRule.Artwork"/>.</summary>
    public string? AccentColorHex { get; init; }

    /// <summary>Album artwork path (decoded by the platform).</summary>
    public string? AlbumArtPath { get; init; }

    /// <summary>Source app icon path.</summary>
    public string? AppIconPath { get; init; }

    /// <summary>Detected verification code, if any.</summary>
    public string? VerificationCode { get; init; }

    /// <summary>Current lyric line.</summary>
    public string? LyricLine { get; init; }

    /// <summary>Next lyric line.</summary>
    public string? SecondaryLyricLine { get; init; }

    /// <summary>Extra structural lines.</summary>
    public ImmutableArray<string> DetailLines { get; init; } = [];

    /// <summary>Playback position ticks.</summary>
    public long PositionTicks { get; init; }

    /// <summary>Playback end ticks.</summary>
    public long EndTicks { get; init; }

    /// <summary>Content signature of this view model (drives cross-fade decisions).</summary>
    public ActivityContentSignature Signature { get; init; }

    /// <summary>Whether this activity is one of the single-line HUD surfaces.</summary>
    public bool IsHud => Kind is ActivityKind.Volume or ActivityKind.Brightness;
}

/// <summary>UI-free view model of the whole island at one instant.</summary>
public sealed record IslandViewModel
{
    /// <summary>Current island state kind.</summary>
    public required IslandStateKind StateKind { get; init; }

    /// <summary>Rail page (expanded state).</summary>
    public required IslandPage Page { get; init; }

    /// <summary>Primary activity view model, or <see langword="null"/> for the idle surface.</summary>
    public ActivityViewModel? Primary { get; init; }

    /// <summary>Ghost layers behind the primary pill (highest priority first).</summary>
    public ImmutableArray<ActivityViewModel> Ghosts { get; init; } = [];

    /// <summary>Hidden activity count for the <c>+N</c> badge.</summary>
    public int OverflowCount { get; init; }

    /// <summary>Total live activity count.</summary>
    public int ActivityCount { get; init; }

    /// <summary>Whether the primary activity is pinned.</summary>
    public bool IsPinned { get; init; }

    /// <summary>HUD view model when the primary activity is volume/brightness.</summary>
    public HudViewModel? Hud { get; init; }

    /// <summary>Whether the idle clock surface is showing.</summary>
    public bool IsIdle => Primary is null;
}

/// <summary>
/// Maps scheduler records/state to UI-free view models. Pure and allocation-bounded:
/// one view model per visible layer, ghosts included.
/// </summary>
public static class ActivityPresenter
{
    /// <summary>Maximum ghost layers (mirrors <c>LayoutTokens.MaxGhosts</c> without a Layout dependency).</summary>
    public const int MaxGhosts = 2;

    /// <summary>Maps an activity kind to its icon identity.</summary>
    public static ActivityIconKind IconFor(ActivityKind kind) => kind switch
    {
        ActivityKind.Idle => ActivityIconKind.Clock,
        ActivityKind.Media => ActivityIconKind.Media,
        ActivityKind.Clipboard => ActivityIconKind.Clipboard,
        ActivityKind.Notification => ActivityIconKind.Notification,
        ActivityKind.Volume => ActivityIconKind.Volume,
        ActivityKind.Brightness => ActivityIconKind.Brightness,
        ActivityKind.BatteryLow => ActivityIconKind.Battery,
        ActivityKind.Timer => ActivityIconKind.Timer,
        ActivityKind.LockKeys => ActivityIconKind.LockKeys,
        ActivityKind.Device => ActivityIconKind.Device,
        ActivityKind.Bluetooth => ActivityIconKind.Bluetooth,
        ActivityKind.Focus => ActivityIconKind.Focus,
        ActivityKind.Progress => ActivityIconKind.Progress,
        ActivityKind.Lookup => ActivityIconKind.Search,
        ActivityKind.Agent => ActivityIconKind.Agent,
        _ => ActivityIconKind.Clock,
    };

    /// <summary>Maps an activity kind/payload to the accent rule of spec 3.3.</summary>
    public static AccentRule AccentFor(ActivityKind kind, ActivityPayload? payload = null)
    {
        if (payload?.AccentColorHex is { Length: > 0 })
        {
            return kind == ActivityKind.Media ? AccentRule.Artwork : AccentRule.SystemAccent;
        }

        return kind switch
        {
            ActivityKind.Media => AccentRule.Artwork,
            ActivityKind.BatteryLow => AccentRule.Danger,
            ActivityKind.Agent => payload?.IsActive == false ? AccentRule.Danger : AccentRule.Info,
            ActivityKind.Notification or ActivityKind.Clipboard => AccentRule.Info,
            ActivityKind.Progress => AccentRule.SystemAccent,
            ActivityKind.Focus => AccentRule.ThemeAccent,
            ActivityKind.Lookup => AccentRule.SystemAccent,
            _ => AccentRule.Neutral,
        };
    }

    /// <summary>Presentation key for the kind label (e.g. <c>Activity.Media</c>).</summary>
    public static string KindLabelKeyFor(ActivityKind kind) => $"Activity.{kind}";

    /// <summary>Clamps/normalizes a progress percentage; <see langword="null"/> stays null.</summary>
    public static int? NormalizeProgress(int? percent) => percent is null ? null : Math.Clamp(percent.Value, 0, 100);

    /// <summary>
    /// Derives media progress from playback ticks when the producer did not send a percentage.
    /// </summary>
    public static int? DeriveProgress(ActivityPayload payload)
    {
        if (payload.ProgressPercent is { } percent)
        {
            return NormalizeProgress(percent);
        }

        if (payload.EndTicks > 0 && payload.PositionTicks >= 0)
        {
            var ratio = payload.PositionTicks / (double)payload.EndTicks;
            return NormalizeProgress((int)Math.Round(Math.Clamp(ratio, 0, 1) * 100));
        }

        return null;
    }

    /// <summary>Whether the compact media surface should show the audio wave.</summary>
    public static bool ShowsWave(ActivityKind kind, ActivityPayload payload)
    {
        if (kind != ActivityKind.Media)
        {
            return false;
        }

        return payload.ShowsAudioWave ?? payload.IsActive ?? false;
    }

    /// <summary>Builds the view model for one activity record.</summary>
    public static ActivityViewModel Present(ActivityRecord record, int stackIndex = 0, bool isGhost = false)
    {
        ArgumentNullException.ThrowIfNull(record);
        var payload = record.Payload;
        return new ActivityViewModel
        {
            Key = record.Key,
            Kind = record.Kind,
            Icon = IconFor(record.Kind),
            Accent = AccentFor(record.Kind, payload),
            KindLabelKey = KindLabelKeyFor(record.Kind),
            Title = payload.Title,
            Subtitle = payload.Subtitle,
            Body = payload.Body,
            Badge = payload.Badge,
            SourceName = payload.SourceName,
            Page = payload.PreferredPage ?? record.Kind.ToPage(),
            ProgressPercent = record.Kind == ActivityKind.Media ? DeriveProgress(payload) : NormalizeProgress(payload.ProgressPercent),
            ProgressIndeterminate = payload.ProgressIndeterminate,
            ShowsWave = ShowsWave(record.Kind, payload),
            IsActive = payload.IsActive ?? false,
            PriorityRank = record.PriorityRank,
            IsPinned = record.IsPinned,
            StackIndex = stackIndex,
            IsGhost = isGhost,
            AccentColorHex = payload.AccentColorHex,
            AlbumArtPath = payload.AlbumArtPath,
            AppIconPath = payload.AppIconPath,
            VerificationCode = payload.VerificationCode,
            LyricLine = payload.LyricLine,
            SecondaryLyricLine = payload.SecondaryLyricLine,
            DetailLines = payload.DetailLines,
            PositionTicks = payload.PositionTicks,
            EndTicks = payload.EndTicks,
            Signature = record.Signature,
        };
    }

    /// <summary>Builds the HUD view model for a volume/brightness activity, otherwise null.</summary>
    public static HudViewModel? PresentHud(ActivityRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Kind is not (ActivityKind.Volume or ActivityKind.Brightness))
        {
            return null;
        }

        var value = Math.Clamp(record.Payload.Level ?? record.Payload.ProgressPercent ?? 0, 0, 100);
        var isMuted = record.Payload.IsActive == false;
        var level = isMuted ? 0 : value switch
        {
            0 => 0,
            <= 33 => 1,
            <= 66 => 2,
            _ => 3,
        };
        return new HudViewModel
        {
            LabelKey = record.Kind == ActivityKind.Volume ? "Hud.Volume" : "Hud.Brightness",
            LabelFallback = record.Kind == ActivityKind.Volume ? "Volume" : "Brightness",
            Value = value,
            IsMuted = isMuted,
            IconLevel = level,
            ValueText = value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    /// <summary>Builds the island view model from the scheduler snapshot and island state.</summary>
    public static IslandViewModel PresentIsland(ActivitySchedulerState scheduler, IslandState state)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(state);
        var primaryRecord = scheduler.Primary;
        var primary = primaryRecord is null ? null : Present(primaryRecord);
        var ghostsBuilder = ImmutableArray.CreateBuilder<ActivityViewModel>(
            Math.Min(MaxGhosts, Math.Max(0, scheduler.Visible.Length - 1)));
        for (var i = 1; i < scheduler.Visible.Length && i <= MaxGhosts; i++)
        {
            ghostsBuilder.Add(Present(scheduler.Visible[i], i, isGhost: true));
        }

        return new IslandViewModel
        {
            StateKind = state.Kind,
            Page = state.Page,
            Primary = primary,
            Ghosts = ghostsBuilder.ToImmutable(),
            OverflowCount = scheduler.BadgeCount,
            ActivityCount = scheduler.ActivityCount,
            IsPinned = state.IsPinned,
            Hud = primaryRecord is null ? null : PresentHud(primaryRecord),
        };
    }
}
