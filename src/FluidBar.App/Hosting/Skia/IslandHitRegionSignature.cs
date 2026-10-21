using FluidBar.Core.Layout;
using FluidBar.Island;

namespace FluidBar.App.Hosting.Skia;

/// <summary>
/// Identity of the island's clickable region (layered hit model + global mouse-hook rectangles).
/// The region is only rebuilt when this value changes, so it must cover everything that moves the
/// clickable geometry — in particular the painted shell size.
/// </summary>
/// <remarks>
/// Regression guard (2026-10-21): the signature used to carry only the shell *form*. The expand
/// transition changes the size over 380 ms, and while a media/agent animation kept
/// <see cref="SkiaIslandController.IsAnimating"/> true the signature never changed again — the hook
/// kept consuming clicks only over the old compact pill, so every button in the panel body (media card
/// transport, toggles, rows) was dead until some unrelated state change refreshed the region.
/// </remarks>
/// <param name="Form">Shell form (compact / expanded / hidden).</param>
/// <param name="Visible">Whether the island is visible.</param>
/// <param name="ArchipelagoVisible">Whether the detached preview rail is painted.</param>
/// <param name="Page">Committed page.</param>
/// <param name="Animating">Whether motion is in flight.</param>
/// <param name="ShellWidthTenths">Painted shell width in tenths of a DIP.</param>
/// <param name="ShellHeightTenths">Painted shell height in tenths of a DIP.</param>
public readonly record struct IslandHitRegionSignature(
    ShellForm Form,
    bool Visible,
    bool ArchipelagoVisible,
    int Page,
    bool Animating,
    int ShellWidthTenths,
    int ShellHeightTenths,
    int SecondaryKind,
    int SecondaryWidthTenths,
    int SecondaryHeightTenths)
{
    /// <summary>Builds the signature for a render state.</summary>
    /// <param name="state">Current render state.</param>
    /// <param name="animating">Whether motion is in flight right now.</param>
    public static IslandHitRegionSignature From(in IslandRenderState state, bool animating)
    {
        var secondary = IslandMetrics.SecondaryShellRect(state);
        return new(
            state.Form,
            state.IsVisible,
            state.Archipelago.Visible,
            state.PageIndex,
            animating,
            (int)Math.Round(state.ShellWidth * 10),
            (int)Math.Round(state.ShellHeight * 10),
            state.SecondaryCompact is { } activity ? (int)activity.Kind : -1,
            (int)Math.Round(secondary.Width * 10),
            (int)Math.Round(secondary.Height * 10));
    }
}
