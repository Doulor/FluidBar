using System.Collections.Immutable;
using FluidBar.Sdk.Canvas;

namespace FluidBar.Sdk;

/// <summary>Plugin-facing island event kinds. Mirrors FluidBar v1 (Auto/Text/ScrollingText/Progress/Status/Media/Notification/Agent).</summary>
public enum IslandEventKind
{
    Auto = 0,
    Text = 1,
    ScrollingText = 2,
    Progress = 3,
    Status = 4,
    Media = 5,
    Notification = 6,
    Agent = 7,
}

/// <summary>
/// Immutable plugin-to-island event. Plugins supply data only; the host owns layout, sizing, animation and
/// theme. The host converts this to its internal activity/event model at the plugin boundary.
/// </summary>
public sealed record IslandEvent
{
    /// <summary>Logical source identity. For plugins the host enforces the plugin id here.</summary>
    public required string Source { get; init; }

    public required string Title { get; init; }

    /// <summary>Deprecated alias kept for v1 data parity: main text content.</summary>
    public string? Content { get; init; }

    /// <summary>Host icon token (e.g. <c>media</c>, <c>clipboard</c>) or an <c>assets/...</c> path for plugin icons.</summary>
    public string? IconKind { get; init; }

    public IslandEventKind Kind { get; init; } = IslandEventKind.Auto;

    /// <summary>Optional manifest layout id whose slot bindings the host should render.</summary>
    public string? LayoutId { get; init; }

    public IslandEventPayload? Payload { get; init; }
}

/// <summary>Optional event payload used by rich templates (media, notifications, agents, progress).</summary>
public sealed record IslandEventPayload
{
    public string? Subtitle { get; init; }

    public string? Badge { get; init; }

    public string? SourceName { get; init; }

    /// <summary>Progress in the range 0..1.</summary>
    public double? Progress { get; init; }

    public bool IsActive { get; init; }

    /// <summary>Normalized audio wave samples in the range 0..1.</summary>
    public IReadOnlyList<float>? AudioWave { get; init; }

    public string? AlbumArtPath { get; init; }

    public string? AppIconPath { get; init; }

    public IReadOnlyList<string>? LyricLines { get; init; }

    public IReadOnlyList<string>? DetailLines { get; init; }

    /// <summary>Playback position ticks (100 ns) for the host media interpolation.</summary>
    public long? PlaybackTicks { get; init; }

    public string? VerificationCode { get; init; }

    /// <summary>Optional validated custom compact canvas.</summary>
    public CompactViewDescriptor? CompactView { get; init; }

    /// <summary>Additional named slot values for declarative layouts. Bounded by <see cref="IslandEventLimits"/>.</summary>
    public IReadOnlyDictionary<string, string?>? Fields { get; init; }
}

/// <summary>Hard bounds applied by the host publisher so a plugin cannot flood or bloat the island pipeline.</summary>
public static class IslandEventLimits
{
    public const int MaxSourceLength = 64;
    public const int MaxTitleLength = 256;
    public const int MaxContentLength = 2_048;
    public const int MaxIconLength = 512;
    public const int MaxPathLength = 1_024;
    public const int MaxLines = 32;
    public const int MaxLineLength = 512;
    public const int MaxWaveSamples = 128;
    public const int MaxFields = 64;
    public const int MaxFieldNameLength = 64;
    public const int MaxFieldValueLength = 1_024;
}

/// <summary>
/// Validates plugin-published events against <see cref="IslandEventLimits"/> before they enter the host
/// pipeline. The host drops invalid events and logs once per plugin/source, never throwing at the plugin.
/// </summary>
public static class IslandEventValidator
{
    /// <summary>Returns an empty list when the event is acceptable; otherwise one message per violation.</summary>
    public static IReadOnlyList<string> Validate(IslandEvent? islandEvent)
    {
        if (islandEvent is null)
        {
            return ["Island event is null."];
        }

        var errors = new List<string>();
        ValidateText(islandEvent.Source, nameof(IslandEvent.Source), IslandEventLimits.MaxSourceLength, required: true, errors);
        ValidateText(islandEvent.Title, nameof(IslandEvent.Title), IslandEventLimits.MaxTitleLength, required: true, errors);
        ValidateOptionalText(islandEvent.Content, nameof(IslandEvent.Content), IslandEventLimits.MaxContentLength, errors);
        ValidateOptionalText(islandEvent.IconKind, nameof(IslandEvent.IconKind), IslandEventLimits.MaxIconLength, errors);

        var payload = islandEvent.Payload;
        if (payload is null)
        {
            return errors;
        }

        if (payload.Progress is { } progress && (double.IsNaN(progress) || progress < 0 || progress > 1))
        {
            errors.Add($"{nameof(IslandEventPayload.Progress)} must be between 0 and 1.");
        }

        if (payload.AudioWave is { } wave)
        {
            if (wave.Count > IslandEventLimits.MaxWaveSamples)
            {
                errors.Add($"AudioWave has {wave.Count} samples; the limit is {IslandEventLimits.MaxWaveSamples}.");
            }

            if (wave.Any(static sample => float.IsNaN(sample) || sample < 0 || sample > 1))
            {
                errors.Add("AudioWave samples must be between 0 and 1.");
            }
        }

        ValidateLines(payload.LyricLines, nameof(IslandEventPayload.LyricLines), errors);
        ValidateLines(payload.DetailLines, nameof(IslandEventPayload.DetailLines), errors);
        ValidateOptionalText(payload.AlbumArtPath, nameof(IslandEventPayload.AlbumArtPath), IslandEventLimits.MaxPathLength, errors);
        ValidateOptionalText(payload.AppIconPath, nameof(IslandEventPayload.AppIconPath), IslandEventLimits.MaxPathLength, errors);
        ValidateOptionalText(payload.VerificationCode, nameof(IslandEventPayload.VerificationCode), 32, errors);

        if (payload.Fields is { } fields)
        {
            if (fields.Count > IslandEventLimits.MaxFields)
            {
                errors.Add($"{nameof(IslandEventPayload.Fields)} has {fields.Count} entries; the limit is {IslandEventLimits.MaxFields}.");
            }

            foreach (var pair in fields)
            {
                if (string.IsNullOrEmpty(pair.Key) || pair.Key.Length > IslandEventLimits.MaxFieldNameLength)
                {
                    errors.Add($"{nameof(IslandEventPayload.Fields)} key '{pair.Key}' is invalid.");
                }

                if (pair.Value is { Length: > IslandEventLimits.MaxFieldValueLength })
                {
                    errors.Add($"{nameof(IslandEventPayload.Fields)} value for '{pair.Key}' exceeds {IslandEventLimits.MaxFieldValueLength} characters.");
                }
            }
        }

        return errors;
    }

    private static void ValidateLines(IReadOnlyList<string>? lines, string name, List<string> errors)
    {
        if (lines is null)
        {
            return;
        }

        if (lines.Count > IslandEventLimits.MaxLines)
        {
            errors.Add($"{name} has {lines.Count} lines; the limit is {IslandEventLimits.MaxLines}.");
        }

        if (lines.Any(line => line is { Length: > IslandEventLimits.MaxLineLength }))
        {
            errors.Add($"{name} contains a line longer than {IslandEventLimits.MaxLineLength} characters.");
        }
    }

    private static void ValidateText(string? value, string name, int maxLength, bool required, List<string> errors)
    {
        if (required && string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{name} is required.");
            return;
        }

        if (value is { Length: > 0 } && value.Length > maxLength)
        {
            errors.Add($"{name} exceeds {maxLength} characters.");
        }
    }

    private static void ValidateOptionalText(string? value, string name, int maxLength, List<string> errors)
    {
        if (value is { Length: > 0 } && value.Length > maxLength)
        {
            errors.Add($"{name} exceeds {maxLength} characters.");
        }
    }
}

/// <summary>Optional convenience builders so plugin code stays readable.</summary>
public static class IslandEventFactory
{
    /// <summary>Creates a simple text event for <paramref name="source"/>.</summary>
    public static IslandEvent Text(string source, string title, string? content = null, string? iconKind = null) =>
        new()
        {
            Source = source,
            Title = title,
            Content = content,
            IconKind = iconKind,
            Kind = IslandEventKind.Auto,
        };

    /// <summary>Creates a progress event (progress clamped to 0..1 by the caller's data model).</summary>
    public static IslandEvent Progress(string source, string title, double progress, string? subtitle = null) =>
        new()
        {
            Source = source,
            Title = title,
            Kind = IslandEventKind.Progress,
            Payload = new IslandEventPayload { Progress = Math.Clamp(progress, 0, 1), Subtitle = subtitle },
        };

    /// <summary>Creates a status/activity event.</summary>
    public static IslandEvent Status(string source, string title, string? subtitle = null, string? badge = null, bool isActive = true) =>
        new()
        {
            Source = source,
            Title = title,
            Kind = IslandEventKind.Status,
            Payload = new IslandEventPayload { Subtitle = subtitle, Badge = badge, IsActive = isActive },
        };

    /// <summary>Creates an event with named slot fields for a declarative layout.</summary>
    public static IslandEvent Layout(
        string source,
        string layoutId,
        string title,
        IReadOnlyDictionary<string, string?> fields,
        IslandEventKind kind = IslandEventKind.Auto) =>
        new()
        {
            Source = source,
            Title = title,
            Kind = kind,
            LayoutId = layoutId,
            Payload = new IslandEventPayload { Fields = fields },
        };
}

/// <summary>Empty/marker set helpers (keeps plugin code allocation-free for the common case).</summary>
public static class PluginCollections
{
    /// <summary>An immutable empty dictionary usable as a default argument.</summary>
    public static IReadOnlyDictionary<string, string?> EmptyFields { get; } = ImmutableDictionary<string, string?>.Empty;
}
