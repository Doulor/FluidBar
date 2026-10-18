using FluidBar.Core.Diagnostics;
using FluidBar.Core.Settings;

namespace FluidBar.App.Settings;

/// <summary>
/// One-time migration from the temporary App-only <c>settings.ui.json</c> adapter to the Core
/// <see cref="SettingsStore"/> format (Lead hand-off 2026-10-18: keep an import fallback when the
/// legacy file exists). The import runs inside the adapter's background load path; it never touches
/// the UI thread and never overwrites an existing Core settings file.
/// </summary>
public static class LegacySettingsImporter
{
    /// <summary>Legacy file name written by <see cref="JsonUiSettingsStore"/>.</summary>
    public const string LegacyFileName = "settings.ui.json";

    /// <summary>Suffix appended after a successful import (the legacy file is retained for forensics).</summary>
    public const string ImportedSuffix = ".imported";

    /// <summary>
    /// Imports the legacy file into Core settings when (a) the legacy file exists and (b) no Core
    /// settings file exists yet. The legacy file is renamed to <c>&lt;legacy&gt;.imported</c> on success.
    /// </summary>
    /// <param name="fileSystem">Core settings file-system abstraction (injectable for tests).</param>
    /// <param name="store">Core settings store.</param>
    /// <param name="legacyPath">Absolute legacy JSON path.</param>
    /// <param name="corePath">Absolute Core settings path.</param>
    /// <param name="logger">Structured logger.</param>
    public static LegacyImportResult ImportIfNeeded(
        ISettingsFileSystem fileSystem,
        SettingsStore store,
        string legacyPath,
        string corePath,
        FluidBar.Core.Diagnostics.IAppLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(corePath);

        if (!fileSystem.FileExists(legacyPath))
        {
            return LegacyImportResult.None;
        }

        if (fileSystem.FileExists(corePath))
        {
            // The user already has a Core settings file; never overwrite it with the legacy adapter.
            logger?.Information(FluidBar.Core.Diagnostics.LogCategories.Settings, "settings.legacy.skipped",
                "Legacy UI settings file exists but Core settings are already present; leaving it untouched.");
            return LegacyImportResult.Skipped;
        }

        try
        {
            var json = fileSystem.ReadAllText(legacyPath);
            var parsed = AppSettings.TryParse(json, out var error);
            if (parsed is null)
            {
                logger?.Warning(FluidBar.Core.Diagnostics.LogCategories.Settings, "settings.legacy.invalid",
                    "Legacy UI settings file could not be parsed; keeping the original file.",
                    data: new Dictionary<string, object?> { ["error"] = error, ["path"] = legacyPath });
                return LegacyImportResult.Failed;
            }

            var migrated = CoreSettingsStoreAdapter.ApplyToCore(store.Load().Settings, parsed);
            var save = store.Save(migrated);
            if (!save.Success)
            {
                logger?.Error(FluidBar.Core.Diagnostics.LogCategories.Settings, "settings.legacy.save-failed",
                    "Core settings save failed while importing legacy UI settings.",
                    data: new Dictionary<string, object?> { ["error"] = save.Error });
                return LegacyImportResult.Failed;
            }

            var backup = legacyPath + ImportedSuffix;
            try
            {
                fileSystem.MoveFile(legacyPath, backup, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger?.Warning(FluidBar.Core.Diagnostics.LogCategories.Settings, "settings.legacy.rename-failed",
                    "Legacy settings were imported but the file could not be renamed.", ex);
            }

            logger?.Information(FluidBar.Core.Diagnostics.LogCategories.Settings, "settings.legacy.imported",
                "Imported legacy UI settings into the Core settings store.",
                data: new Dictionary<string, object?> { ["backup"] = backup });
            return LegacyImportResult.Imported;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            logger?.Error(FluidBar.Core.Diagnostics.LogCategories.Settings, "settings.legacy.failed",
                "Failed to import legacy UI settings.", ex);
            return LegacyImportResult.Failed;
        }
    }
}

/// <summary>Outcome of the one-time legacy settings import.</summary>
public enum LegacyImportOutcome
{
    /// <summary>Nothing to do (legacy file absent).</summary>
    None,

    /// <summary>Legacy settings imported into Core and the file renamed.</summary>
    Imported,

    /// <summary>Core settings already present; legacy file left untouched.</summary>
    Skipped,

    /// <summary>Import failed; the legacy file is preserved for a later retry.</summary>
    Failed,
}

/// <summary>Result of <see cref="LegacySettingsImporter.ImportIfNeeded"/>.</summary>
/// <param name="Outcome">Import outcome.</param>
public readonly record struct LegacyImportResult(LegacyImportOutcome Outcome)
{
    /// <summary>No legacy file existed.</summary>
    public static LegacyImportResult None { get; } = new(LegacyImportOutcome.None);

    /// <summary>Legacy file skipped because Core settings exist.</summary>
    public static LegacyImportResult Skipped { get; } = new(LegacyImportOutcome.Skipped);

    /// <summary>Legacy file imported successfully.</summary>
    public static LegacyImportResult Imported { get; } = new(LegacyImportOutcome.Imported);

    /// <summary>Legacy file could not be imported; it is preserved.</summary>
    public static LegacyImportResult Failed { get; } = new(LegacyImportOutcome.Failed);
}
