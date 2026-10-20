using System.Collections.Immutable;

namespace FluidBar.Core.Activities;

/// <summary>
/// Immutable, UI-free content snapshot published with an <c>IslandEvent</c>.
/// One payload type covers every activity class; producers fill the fields relevant to
/// their kind and leave the rest at their defaults.
/// </summary>
public sealed record ActivityPayload
{
    /// <summary>An empty payload, used for retractions.</summary>
    public static ActivityPayload Empty { get; } = new();

    /// <summary>Primary text (track title, "Copied", notification title...).</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Secondary line (artist, source app, device name...).</summary>
    public string? Subtitle { get; init; }

    /// <summary>Body/detail text (notification body, clipboard preview, lyric line...).</summary>
    public string? Body { get; init; }

    /// <summary>Short badge text (e.g. verification code, "+3").</summary>
    public string? Badge { get; init; }

    /// <summary>Human-readable producer name shown as a caption.</summary>
    public string? SourceName { get; init; }

    /// <summary>Optional icon token overriding the kind default (see <c>ActivityIconKind</c>).</summary>
    public string? IconToken { get; init; }

    /// <summary>Optional accent color as <c>#RRGGBB</c>/<c>#AARRGGBB</c> (artwork extraction result).</summary>
    public string? AccentColorHex { get; init; }

    /// <summary>Determinate progress in percent, clamped to 0..100 by the presenter.</summary>
    public int? ProgressPercent { get; init; }

    /// <summary>Whether progress is indeterminate (spinner instead of a track).</summary>
    public bool ProgressIndeterminate { get; init; }

    /// <summary>Playing/charging/active flag for the relevant activity classes.</summary>
    public bool? IsActive { get; init; }

    /// <summary>Whether the compact media surface should show the audio wave.</summary>
    public bool? ShowsAudioWave { get; init; }

    /// <summary>Local path to album artwork (platform resolves/decodes; core never touches files).</summary>
    public string? AlbumArtPath { get; init; }

    /// <summary>Local path to the source application icon.</summary>
    public string? AppIconPath { get; init; }

    /// <summary>Current lyric line, if lyrics are enabled.</summary>
    public string? LyricLine { get; init; }

    /// <summary>Next lyric line for the two-line expanded media page.</summary>
    public string? SecondaryLyricLine { get; init; }

    /// <summary>Additional structural lines (e.g. notification list preview).</summary>
    public ImmutableArray<string> DetailLines { get; init; } = [];

    /// <summary>Playback position in ticks (100 ns units) for media.</summary>
    public long PositionTicks { get; init; }

    /// <summary>Playback end position in ticks (100 ns units) for media.</summary>
    public long EndTicks { get; init; }

    /// <summary>Notification verification code, if one was detected.</summary>
    public string? VerificationCode { get; init; }

    /// <summary>Numerical level 0..100 for HUD surfaces (volume, brightness, battery).</summary>
    public int? Level { get; init; }

    /// <summary>Explicit TTL override for transient activities.</summary>
    public TimeSpan? Ttl { get; init; }

    /// <summary>Session pin flag; suppresses preemption and expiry for this activity.</summary>
    public bool IsPinned { get; init; }

    /// <summary>Preferred page to open on click, when the kind default is not wanted.</summary>
    public PageRef? PreferredPage { get; init; }

    /// <summary>
    /// Merges an incremental patch over this payload: non-null fields from
    /// <paramref name="patch"/> win, nulls keep the current value.
    /// </summary>
    /// <param name="patch">Latest incremental snapshot.</param>
    /// <returns>Merged immutable payload.</returns>
    public ActivityPayload Merge(ActivityPayload patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        return this with
        {
            Title = patch.Title.Length > 0 ? patch.Title : Title,
            Subtitle = patch.Subtitle ?? Subtitle,
            Body = patch.Body ?? Body,
            Badge = patch.Badge ?? Badge,
            SourceName = patch.SourceName ?? SourceName,
            IconToken = patch.IconToken ?? IconToken,
            AccentColorHex = patch.AccentColorHex ?? AccentColorHex,
            ProgressPercent = patch.ProgressPercent ?? ProgressPercent,
            ProgressIndeterminate = patch.ProgressIndeterminate || ProgressIndeterminate,
            IsActive = patch.IsActive ?? IsActive,
            ShowsAudioWave = patch.ShowsAudioWave ?? ShowsAudioWave,
            AlbumArtPath = patch.AlbumArtPath ?? AlbumArtPath,
            AppIconPath = patch.AppIconPath ?? AppIconPath,
            LyricLine = patch.LyricLine ?? LyricLine,
            SecondaryLyricLine = patch.SecondaryLyricLine ?? SecondaryLyricLine,
            DetailLines = patch.DetailLines.Length > 0 ? patch.DetailLines : DetailLines,
            PositionTicks = patch.PositionTicks != 0 ? patch.PositionTicks : PositionTicks,
            EndTicks = patch.EndTicks != 0 ? patch.EndTicks : EndTicks,
            VerificationCode = patch.VerificationCode ?? VerificationCode,
            Level = patch.Level ?? Level,
            Ttl = patch.Ttl ?? Ttl,
            IsPinned = patch.IsPinned || IsPinned,
            PreferredPage = patch.PreferredPage ?? PreferredPage,
        };
    }
}

/// <summary>
/// Stable content signature (spec 2.7 rule 2):
/// <c>(activityKey, kind, titleHash, bodyHash, progressBucket)</c>.
/// Equal signatures mean "nothing visible changed", so the update is discarded with zero work.
/// </summary>
public readonly record struct ActivityContentSignature
{
    /// <summary>Stable hash of <see cref="ActivityKey.ToString"/>.</summary>
    public int KeyHash { get; init; }

    /// <summary>Activity kind.</summary>
    public ActivityKind Kind { get; init; }

    /// <summary>Stable hash of <see cref="ActivityPayload.Title"/>.</summary>
    public int TitleHash { get; init; }

    /// <summary>Stable hash of the visible body/detail text.</summary>
    public int BodyHash { get; init; }

    /// <summary>Progress quantized to 5-point buckets so 1 % updates do not replace content.</summary>
    public int ProgressBucket { get; init; }

    /// <summary>Whether any visible content differs from <paramref name="other"/>.</summary>
    public bool DiffersFrom(ActivityContentSignature other)
        => KeyHash != other.KeyHash
           || Kind != other.Kind
           || TitleHash != other.TitleHash
           || BodyHash != other.BodyHash
           || ProgressBucket != other.ProgressBucket;
}

/// <summary>Builds <see cref="ActivityContentSignature"/> values and applies the discard rule.</summary>
public static class ContentSignaturePolicy
{
    /// <summary>Progress quantization step in percent (spec: "progressBucket").</summary>
    public const int ProgressBucketSize = 5;

    /// <summary>Computes the signature of a payload for a given key/kind.</summary>
    public static ActivityContentSignature Compute(ActivityKey key, ActivityKind kind, ActivityPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var bucket = payload.ProgressPercent is { } percent
            ? Math.Clamp(percent, 0, 100) / ProgressBucketSize
            : -1;
        return new ActivityContentSignature
        {
            KeyHash = StableHash.Of(key.ToString()),
            Kind = kind,
            TitleHash = StableHash.Of(payload.Title),
            BodyHash = StableHash.Of(BuildBodyText(payload)),
            ProgressBucket = bucket,
        };
    }

    /// <summary>Whether the incoming record should trigger a content update (spec 2.7 rule 2).</summary>
    public static bool ShouldUpdateContent(ActivityRecord? existing, ActivityRecord incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        return existing is null || existing.Signature.DiffersFrom(incoming.Signature);
    }

    private static string BuildBodyText(ActivityPayload payload)
    {
        if (payload.Body is { Length: > 0 })
        {
            return payload.Body;
        }

        if (payload.LyricLine is { Length: > 0 })
        {
            return payload.LyricLine;
        }

        if (!payload.DetailLines.IsDefaultOrEmpty)
        {
            return string.Join('\u001F', payload.DetailLines);
        }

        return payload.Subtitle ?? string.Empty;
    }
}

/// <summary>Process-stable FNV-1a hashing; never use <see cref="string.GetHashCode()"/> for snapshots.</summary>
public static class StableHash
{
    private const uint FnvOffsetBasis = 2166136261;
    private const uint FnvPrime = 16777619;

    /// <summary>Hash of <paramref name="value"/>; deterministic across processes and runs.</summary>
    public static int Of(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        var hash = FnvOffsetBasis;
        foreach (var c in value)
        {
            hash ^= c;
            hash *= FnvPrime;
        }

        return unchecked((int)hash);
    }
}

