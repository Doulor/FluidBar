using System.Collections.Immutable;
using FluidBar.App.Localization;
using FluidBar.Platform.Windows.Notifications;

namespace FluidBar.App.State;

/// <summary>
/// Maps the Windows toast snapshot (<see cref="NotificationsSnapshot"/>) onto the island notification
/// page rows. Pure and localizer-only: no HWND/WinRT here, so the page content can be unit-tested with
/// hand-built snapshots (truncation, missing app name, relative time, cap).
/// </summary>
public static class NotificationContentMapper
{
    /// <summary>Rows the island page renders at most (five fit without scrolling; the rest stay bounded).</summary>
    public const int DefaultMaxItems = 12;

    /// <summary>Body preview cap; a toast body can be several KiB and is ellipsised by the renderer anyway.</summary>
    public const int MaxBodyCharacters = 160;

    /// <summary>App-name fallback when Windows does not report one (brand name, language neutral).</summary>
    public const string FallbackAppName = "Windows";

    /// <summary>Maps a snapshot to page rows (newest first, capped).</summary>
    /// <param name="snapshot">Toast snapshot from the platform listener, or null before the first read.</param>
    /// <param name="maxItems">Maximum rows to emit.</param>
    /// <param name="now">Reference time for the relative "time ago" label; defaults to the system clock.</param>
    /// <param name="localizer">Localizer for the relative label; English fallbacks are used when null.</param>
    public static ImmutableArray<NotificationItemData> Map(
        NotificationsSnapshot? snapshot,
        int maxItems = DefaultMaxItems,
        DateTimeOffset? now = null,
        ILocalizer? localizer = null)
    {
        if (snapshot is null || snapshot.Recent.IsDefaultOrEmpty || maxItems <= 0)
        {
            return ImmutableArray<NotificationItemData>.Empty;
        }

        var reference = now ?? DateTimeOffset.Now;
        var items = ImmutableArray.CreateBuilder<NotificationItemData>(Math.Min(maxItems, snapshot.Recent.Length));
        foreach (var entry in snapshot.Recent)
        {
            if (items.Count >= maxItems)
            {
                break;
            }

            items.Add(MapEntry(entry, reference, localizer));
        }

        return items.MoveToImmutable();
    }

    /// <summary>Maps one toast entry (exposed for focused tests).</summary>
    public static NotificationItemData MapEntry(NotificationEntry entry, DateTimeOffset now, ILocalizer? localizer = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var source = string.IsNullOrWhiteSpace(entry.AppName) ? FallbackAppName : entry.AppName.Trim();
        var title = string.IsNullOrWhiteSpace(entry.Title) ? source : CollapseWhitespace(entry.Title);
        return new NotificationItemData
        {
            Title = title,
            Body = TruncateBody(CollapseWhitespace(entry.Body)),
            Source = source,
            TimeText = RelativeTime(entry.CreatedAt, now, localizer),
            VerificationCode = string.IsNullOrWhiteSpace(entry.VerificationCode) ? null : entry.VerificationCode,
        };
    }

    /// <summary>Localized "time ago" label ("just now" / "5 minutes ago" / "3 hours ago").</summary>
    public static string RelativeTime(DateTimeOffset createdAt, DateTimeOffset now, ILocalizer? localizer = null)
    {
        var delta = now - createdAt;
        if (delta < TimeSpan.Zero)
        {
            delta = TimeSpan.Zero;
        }

        if (delta.TotalSeconds < 60)
        {
            return localizer?.Get(LocKeys.Notifications.JustNow) ?? "now";
        }

        if (delta.TotalMinutes < 60)
        {
            var minutes = Math.Max(1, (int)delta.TotalMinutes);
            return localizer is null
                ? $"{minutes} min"
                : localizer.Format(LocKeys.Notifications.MinutesAgo, ("count", minutes));
        }

        var hours = Math.Max(1, (int)delta.TotalHours);
        return localizer is null
            ? $"{hours} h"
            : localizer.Format(LocKeys.Notifications.HoursAgo, ("count", hours));
    }

    private static string CollapseWhitespace(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static string TruncateBody(string body)
    {
        if (body.Length <= MaxBodyCharacters)
        {
            return body;
        }

        var cut = body[..MaxBodyCharacters];
        var lastSpace = cut.LastIndexOf(' ');
        if (lastSpace > MaxBodyCharacters / 2)
        {
            cut = cut[..lastSpace];
        }

        return string.Concat(cut.TrimEnd(), "\u2026");
    }
}
