using FluidBar.Core.Configuration;

namespace FluidBar.App.Hosting.Skia;

/// <summary>Frame/media scheduling profile for one <see cref="IslandPowerMode"/>.</summary>
/// <param name="FrameRate">Animation frame rate (60 Performance / 30 Saver).</param>
/// <param name="MediaPlayingPoll">Media liveness poll while a session is playing.</param>
/// <param name="MediaIdlePoll">Media discovery poll when nothing is playing.</param>
/// <param name="KeepIdleFrames">Whether the host keeps the frame clock running with no damage.</param>
/// <param name="AmbientAlways">Whether ambient motion runs even when the pointer is elsewhere.</param>
/// <param name="EmptyFrameStopThreshold">Consecutive no-damage frames before the clock stops; 0 never stops.</param>
public readonly record struct IslandPowerProfile(
    double FrameRate,
    TimeSpan MediaPlayingPoll,
    TimeSpan MediaIdlePoll,
    bool KeepIdleFrames,
    bool AmbientAlways,
    int EmptyFrameStopThreshold)
{
    /// <summary>Frame interval derived from <see cref="FrameRate"/>.</summary>
    public TimeSpan FrameInterval => TimeSpan.FromMilliseconds(1000.0 / FrameRate);
}

/// <summary>The two power tables. Every frame/poll decision reads this table by mode.</summary>
public static class IslandPowerPolicy
{
    /// <summary>Returns the immutable parameter table for <paramref name="mode"/>.</summary>
    public static IslandPowerProfile For(IslandPowerMode mode) => mode switch
    {
        IslandPowerMode.Saver => new IslandPowerProfile(
            FrameRate: 30,
            MediaPlayingPoll: TimeSpan.FromSeconds(2),
            MediaIdlePoll: TimeSpan.FromSeconds(5),
            KeepIdleFrames: false,
            AmbientAlways: false,
            EmptyFrameStopThreshold: 2),
        _ => new IslandPowerProfile(
            FrameRate: 60,
            MediaPlayingPoll: TimeSpan.FromSeconds(1),
            MediaIdlePoll: TimeSpan.FromSeconds(1),
            KeepIdleFrames: true,
            AmbientAlways: true,
            EmptyFrameStopThreshold: 0),
    };
}
