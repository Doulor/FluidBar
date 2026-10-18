using System.Collections.Immutable;
using FluidBar.App.Settings;
using FluidBar.Core.Layout;
using CoreMonitorInfo = FluidBar.Core.Layout.MonitorInfo;

namespace FluidBar.App.Hosting.Skia;

/// <summary>
/// Pure monitor-set diff for the layered island host: which monitors should own a window, which
/// windows must be created or disposed, and whether a retained monitor's work area/DPI changed.
/// <para>
/// The host applies this plan on the UI thread; nothing here touches a HWND or a monitor service, so
/// the targeting and add/remove rules are fully unit-testable.
/// </para>
/// </summary>
/// <param name="Targets">Monitors that should own a window, ordered with the stable primary first.</param>
/// <param name="Primary">The stable primary monitor for this snapshot (null when no monitor exists).</param>
/// <param name="Added">Target monitors that do not have a window yet.</param>
/// <param name="Removed">Current windows whose monitor is no longer a target.</param>
/// <param name="Retained">Target monitors that already have a window (re-place them).</param>
/// <param name="GeometryChanged">Whether a retained monitor's bounds, work area or DPI changed.</param>
public sealed record SkiaMonitorPlan(
    ImmutableArray<CoreMonitorInfo> Targets,
    CoreMonitorInfo? Primary,
    ImmutableArray<CoreMonitorInfo> Added,
    ImmutableArray<CoreMonitorInfo> Removed,
    ImmutableArray<CoreMonitorInfo> Retained,
    bool GeometryChanged)
{
    /// <summary>An empty plan (no monitors were enumerated).</summary>
    public static SkiaMonitorPlan Empty { get; } = new(
        [],
        null,
        [],
        [],
        [],
        GeometryChanged: false);

    /// <summary>Whether a window must be created or destroyed.</summary>
    public bool RequiresWindowChanges => !Added.IsDefaultOrEmpty || !Removed.IsDefaultOrEmpty;
}

/// <summary>
/// Pure target-monitor selection and add/remove diff for <see cref="SkiaIslandHost"/>.
/// <para>
/// Rules (spec 2.6 + the layered-host contract):
/// <list type="bullet">
/// <item><see cref="DisplayTarget.Primary"/> keeps exactly one window on the primary monitor.</item>
/// <item><see cref="DisplayTarget.All"/> keeps one window per enumerated monitor.</item>
/// <item><see cref="DisplayTarget.FollowMouse"/> keeps one window on the monitor under the cursor
/// (passed in as an id by the host's debounced cursor poll); it falls back to the primary monitor
/// when the cursor monitor is unknown, so a failed query never produces a wrong-monitor window.</item>
/// <item>The primary is <b>stable</b>: the OS-flagged primary wins; when no monitor is flagged the
/// previously used primary is kept if it still exists, otherwise the first monitor is used.</item>
/// <item>The primary is always first in <see cref="SkiaMonitorPlan.Targets"/> so
/// <c>SkiaIslandHost.PrimaryWindow</c> never changes identity on a hot-plug.</item>
/// </list>
/// </para>
/// </summary>
public static class SkiaMonitorPlanner
{
    /// <summary>Resolves the stable primary monitor for a snapshot.</summary>
    /// <param name="monitors">Enumerated monitors (enumeration order).</param>
    /// <param name="previousPrimaryId">Monitor id that was primary before the change, when known.</param>
    public static CoreMonitorInfo? ResolvePrimary(
        IReadOnlyList<CoreMonitorInfo> monitors,
        string? previousPrimaryId = null)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (monitors.Count == 0)
        {
            return null;
        }

        var flagged = monitors.FirstOrDefault(static monitor => monitor.IsPrimary);
        if (flagged is not null)
        {
            return flagged;
        }

        if (previousPrimaryId is { Length: > 0 })
        {
            var previous = monitors.FirstOrDefault(
                monitor => string.Equals(monitor.Id, previousPrimaryId, StringComparison.Ordinal));
            if (previous is not null)
            {
                return previous;
            }
        }

        return monitors[0];
    }

    /// <summary>Resolves the monitors that should own a window for a display-target setting.</summary>
    /// <param name="monitors">Enumerated monitors (enumeration order).</param>
    /// <param name="displayOn">Display targeting setting.</param>
    /// <param name="primary">Stable primary monitor (see <see cref="ResolvePrimary"/>).</param>
    /// <param name="cursorMonitorId">
    /// Monitor under the cursor for <see cref="DisplayTarget.FollowMouse"/> (from the host's debounced
    /// poll). Null/stale ids fall back to <paramref name="primary"/>; ignored by other targets.
    /// </param>
    public static ImmutableArray<CoreMonitorInfo> ResolveTargets(
        IReadOnlyList<CoreMonitorInfo> monitors,
        DisplayTarget displayOn,
        CoreMonitorInfo? primary,
        string? cursorMonitorId = null)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (primary is null)
        {
            return [];
        }

        return displayOn switch
        {
            DisplayTarget.All => PrimaryFirst(monitors, primary),
            DisplayTarget.FollowMouse => ResolveFollowMouse(monitors, primary, cursorMonitorId),
            _ => [primary],
        };
    }

    /// <summary>
    /// FollowMouse target: the cursor monitor when it is in the snapshot, else the stable primary.
    /// A hot-unplugged cursor monitor therefore degrades to the primary instead of dropping every
    /// window (the host re-polls on the next tick and moves the window back).
    /// </summary>
    private static ImmutableArray<CoreMonitorInfo> ResolveFollowMouse(
        IReadOnlyList<CoreMonitorInfo> monitors,
        CoreMonitorInfo primary,
        string? cursorMonitorId)
    {
        if (cursorMonitorId is { Length: > 0 })
        {
            foreach (var monitor in monitors)
            {
                if (string.Equals(monitor.Id, cursorMonitorId, StringComparison.Ordinal))
                {
                    return [monitor];
                }
            }
        }

        return [primary];
    }

    /// <summary>Diffs the current window set against a freshly enumerated monitor snapshot.</summary>
    /// <param name="current">Monitors that currently own a window.</param>
    /// <param name="available">Monitors from the latest enumeration.</param>
    /// <param name="displayOn">Display targeting setting.</param>
    /// <param name="previousPrimaryId">Monitor id that was primary before the change, when known.</param>
    /// <param name="cursorMonitorId">
    /// Monitor under the cursor for <see cref="DisplayTarget.FollowMouse"/>; ignored by other targets.
    /// </param>
    public static SkiaMonitorPlan Plan(
        IReadOnlyList<CoreMonitorInfo> current,
        IReadOnlyList<CoreMonitorInfo> available,
        DisplayTarget displayOn,
        string? previousPrimaryId = null,
        string? cursorMonitorId = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(available);

        var primary = ResolvePrimary(available, previousPrimaryId);
        var targets = ResolveTargets(available, displayOn, primary, cursorMonitorId);
        if (targets.IsDefaultOrEmpty)
        {
            return new SkiaMonitorPlan([], primary, [], [.. current], [], GeometryChanged: false);
        }

        var currentById = new Dictionary<string, CoreMonitorInfo>(current.Count, StringComparer.Ordinal);
        foreach (var monitor in current)
        {
            currentById[monitor.Id] = monitor;
        }

        var targetIds = new HashSet<string>(targets.Length, StringComparer.Ordinal);
        foreach (var monitor in targets)
        {
            targetIds.Add(monitor.Id);
        }

        var added = ImmutableArray.CreateBuilder<CoreMonitorInfo>();
        var retained = ImmutableArray.CreateBuilder<CoreMonitorInfo>();
        var geometryChanged = false;
        foreach (var target in targets)
        {
            if (!currentById.TryGetValue(target.Id, out var previous))
            {
                added.Add(target);
                continue;
            }

            retained.Add(target);
            geometryChanged |= !SameGeometry(previous, target);
        }

        var removed = ImmutableArray.CreateBuilder<CoreMonitorInfo>();
        foreach (var monitor in current)
        {
            if (!targetIds.Contains(monitor.Id))
            {
                removed.Add(monitor);
            }
        }

        return new SkiaMonitorPlan(
            targets,
            primary,
            added.ToImmutable(),
            removed.ToImmutable(),
            retained.ToImmutable(),
            geometryChanged);
    }

    private static ImmutableArray<CoreMonitorInfo> PrimaryFirst(
        IReadOnlyList<CoreMonitorInfo> monitors,
        CoreMonitorInfo primary)
    {
        var builder = ImmutableArray.CreateBuilder<CoreMonitorInfo>(monitors.Count);
        builder.Add(primary);
        foreach (var monitor in monitors)
        {
            if (!string.Equals(monitor.Id, primary.Id, StringComparison.Ordinal))
            {
                builder.Add(monitor);
            }
        }

        return builder.ToImmutable();
    }

    private static bool SameGeometry(CoreMonitorInfo previous, CoreMonitorInfo current)
        => previous.Bounds == current.Bounds
            && previous.WorkArea == current.WorkArea
            && previous.Dpi == current.Dpi;
}
