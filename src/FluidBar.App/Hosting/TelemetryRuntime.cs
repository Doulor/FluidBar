namespace FluidBar.App.Hosting;

/// <summary>Settings-window actions for the anonymous-usage-data row.</summary>
internal interface ITelemetryControls
{
    /// <summary>Applies the user's choice immediately (start sending or discard what is pending).</summary>
    /// <param name="enabled">New value of the setting.</param>
    void SetEnabled(bool enabled);

    /// <summary>Writes the next batch to a readable file and returns its path.</summary>
    string WritePreview();

    /// <summary>Forgets the anonymous identifier and drops everything queued.</summary>
    void ResetInstallId();
}

/// <summary>
/// Ambient access to the telemetry service, mirroring <see cref="LocalizationRuntime"/>: the settings window is
/// created in several places (hotkey, tray, tests) and must not grow another constructor parameter for this.
/// </summary>
internal static class TelemetryRuntime
{
    /// <summary>The running service, or null when telemetry never started (tests, safe mode, init failure).</summary>
    public static ITelemetryControls? Current { get; set; }
}
