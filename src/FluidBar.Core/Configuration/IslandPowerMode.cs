using System.Text.Json.Serialization;

namespace FluidBar.Core.Configuration;

/// <summary>Island scheduling policy: Performance keeps the full frame pipeline warm; Saver trades latency for idle CPU.</summary>
public enum IslandPowerMode
{
    /// <summary>Low-power cadence: 30 fps, idle frame clock stop, slower media/ambient polls.</summary>
    [JsonStringEnumMemberName("saver")]
    Saver,

    /// <summary>Default cadence: 60 fps, continuous idle frames, responsive media/ambient polling.</summary>
    [JsonStringEnumMemberName("performance")]
    Performance,
}
