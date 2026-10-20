namespace FluidBar.App.Views.Settings;

/// <summary>The eight settings sections (spec §8 / deliverable 5 + agent integration).</summary>
public enum SettingsWindowSection
{
    /// <summary>Language, startup, updates, tray, hotkeys.</summary>
    General,

    /// <summary>Theme, material, palette, accent, opacity, font scale, motion.</summary>
    Appearance,

    /// <summary>Mode, anchor, stacking, hover peek, timers, display target.</summary>
    Island,

    /// <summary>Installed plugin list and enable toggles.</summary>
    Plugins,

    /// <summary>Native, read-only agent support status (no install actions).</summary>
    AgentSupport,

    /// <summary>Marketplace browse/install/update/rollback.</summary>
    Marketplace,

    /// <summary>Data folder, logs, export/import, diagnostics, reset.</summary>
    Advanced,

    /// <summary>Version, license, contributors, links.</summary>
    About,
}
