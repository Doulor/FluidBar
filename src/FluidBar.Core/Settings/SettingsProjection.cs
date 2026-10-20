using System.Collections.Immutable;
using FluidBar.Core.Activities;
using FluidBar.Core.Layout;
using FluidBar.Core.Motion;

namespace FluidBar.Core.Settings;

/// <summary>
/// Projects the persisted settings tree into the pure policy objects consumed by the core
/// (behaviour options, stack policy, motion profile, layout request). Keeps the App layer free
/// of mapping code and the core free of settings lookups in hot paths.
/// </summary>
public static class SettingsProjection
{
    /// <summary>
    /// Resolves the effective reduced-motion flag from the tri-state setting and the Windows
    /// "show animations" state.
    /// </summary>
    public static bool ResolveReducedMotion(MotionPreference preference, bool systemReducedMotion) => preference switch
    {
        MotionPreference.Enabled => false,
        MotionPreference.Off => true,
        _ => systemReducedMotion,
    };

    /// <summary>Builds reducer behaviour options from settings.</summary>
    /// <param name="settings">Settings snapshot.</param>
    /// <param name="systemReducedMotion">Windows "show animations" state (true = reduced).</param>
    public static IslandBehaviorOptions ToBehaviorOptions(FluidBarSettings settings, bool systemReducedMotion)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new IslandBehaviorOptions
        {
            Enabled = settings.Island.Enabled,
            HoverPeek = settings.Island.HoverPeek,
            HoverReveal = settings.Island.HoverReveal,
            AutoCollapseMs = settings.Island.AutoCollapseMs,
            AutoHideMs = settings.Island.AutoHideMs,
            ReducedMotion = ResolveReducedMotion(settings.Appearance.ReducedMotion, systemReducedMotion),
        };
    }

    /// <summary>Builds the activity stack policy (stacking + per-kind TTL overrides).</summary>
    public static ActivityStackPolicy ToStackPolicy(FluidBarSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var overrides = ImmutableDictionary.CreateBuilder<ActivityKind, TimeSpan>();
        foreach (var kind in Enum.GetValues<ActivityKind>())
        {
            if (kind == ActivityKind.Idle)
            {
                continue;
            }

            var feature = settings.Activities.For(kind);
            if (feature.DurationMs is > 0 and var durationMs)
            {
                overrides[kind] = TimeSpan.FromMilliseconds(durationMs);
            }
        }

        return new ActivityStackPolicy
        {
            Stacking = settings.Island.Stacking switch
            {
                StackingPreference.Latest => StackingStrategy.LatestOnly,
                StackingPreference.Split => StackingStrategy.Split,
                _ => StackingStrategy.Stacked,
            },
            TtlOverrides = overrides.ToImmutable(),
        };
    }

    /// <summary>Builds the effective motion profile from settings.</summary>
    public static MotionProfile ToMotionProfile(FluidBarSettings settings, bool systemReducedMotion)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new MotionProfile
        {
            ReducedMotion = ResolveReducedMotion(settings.Appearance.ReducedMotion, systemReducedMotion),
            Strength = settings.Appearance.MotionStrength,
        };
    }

    /// <summary>Resolves the persisted placement for a monitor.</summary>
    public static MonitorPlacement ResolvePlacement(FluidBarSettings settings, MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(monitor);
        return settings.Placement.Resolve(monitor.Id, monitor.IsPrimary);
    }

    /// <summary>
    /// Builds the layout request for the current island state on a monitor, applying mode,
    /// per-monitor anchor/offsets, hidden-strip and stacking preferences.
    /// </summary>
    public static IslandLayoutRequest ToLayoutRequest(FluidBarSettings settings, IslandState state, MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(state);
        var placement = ResolvePlacement(settings, monitor);
        return IslandLayoutRequest.FromState(
            state,
            mode: settings.Island.Mode,
            anchor: placement.Anchor,
            offsetX: placement.OffsetX,
            offsetY: placement.OffsetY ?? double.NaN,
            stacking: settings.Island.Stacking switch
            {
                StackingPreference.Latest => StackingStrategy.LatestOnly,
                StackingPreference.Split => StackingStrategy.Split,
                _ => StackingStrategy.Stacked,
            },
            hiddenHoverStrip: settings.Island.HiddenHoverStrip);
    }

    /// <summary>Whether an activity class is enabled in settings.</summary>
    public static bool IsActivityEnabled(FluidBarSettings settings, ActivityKind kind)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Activities.For(kind).Enabled;
    }

    /// <summary>Resolved TTL override for an activity kind (null = spec default).</summary>
    public static TimeSpan? TtlFor(FluidBarSettings settings, ActivityKind kind)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var durationMs = settings.Activities.For(kind).DurationMs;
        return durationMs is > 0 ? TimeSpan.FromMilliseconds(durationMs.Value) : null;
    }
}
