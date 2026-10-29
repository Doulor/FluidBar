using FluidBar.Core.Diagnostics;

namespace FluidBar.Core.Settings;

/// <summary>Options for <see cref="SettingsStore"/>; all paths are host-provided.</summary>
public sealed record SettingsStoreOptions
{
    /// <summary>Absolute path of the v2 settings file (typically <c>%APPDATA%\FluidBar\settings.json</c>).</summary>
    public required string SettingsPath { get; init; }

    /// <summary>Backup path for atomic replace; defaults to <c>settings.json.bak</c>.</summary>
    public string? BackupPath { get; init; }

    /// <summary>Temp file path for atomic writes; defaults to <c>settings.json.tmp</c>.</summary>
    public string? TempPath { get; init; }

    /// <summary>Where the legacy v1 file is preserved after migration; defaults to <c>settings.v1.backup.json</c>.</summary>
    public string? V1BackupPath { get; init; }

    /// <summary>Migrate a legacy (schemaVersion-less) file on load.</summary>
    public bool MigrateV1 { get; init; } = true;

    /// <summary>Keep a <c>.bak</c> of the previous file on every save.</summary>
    public bool KeepBackup { get; init; } = true;

    /// <summary>Effective backup path.</summary>
    public string EffectiveBackupPath => BackupPath ?? SettingsPath + ".bak";

    /// <summary>Effective temp path.</summary>
    public string EffectiveTempPath => TempPath ?? SettingsPath + ".tmp";

    /// <summary>Effective v1 backup path.</summary>
    public string EffectiveV1BackupPath => V1BackupPath ?? CombineSibling(SettingsPath, "settings.v1.backup.json");

    /// <summary>Builds a settings store for an app-data directory (pure string composition).</summary>
    /// <param name="appDataDirectory">The FluidBar app-data directory.</param>
    public static SettingsStoreOptions ForAppData(string appDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataDirectory);
        var separator = appDataDirectory.Contains('\\', StringComparison.Ordinal) ? '\\' : '/';
        var normalized = appDataDirectory.TrimEnd('\\', '/');
        var root = normalized + separator;
        return new SettingsStoreOptions
        {
            SettingsPath = root + "settings.json",
            BackupPath = root + "settings.json.bak",
            TempPath = root + "settings.json.tmp",
            V1BackupPath = root + "settings.v1.backup.json",
        };
    }

    private static string CombineSibling(string settingsPath, string fileName)
    {
        var index = settingsPath.LastIndexOfAny(['/', '\\']);
        var separator = index > 0 ? settingsPath[index] : '/';
        return index < 0 ? fileName : settingsPath[..(index + 1)] + fileName;
    }
}

/// <summary>Outcome of <see cref="SettingsStore.Load"/>.</summary>
public enum SettingsLoadStatus
{
    /// <summary>v2 file loaded and validated.</summary>
    Loaded,

    /// <summary>No file found; defaults were created (not necessarily persisted).</summary>
    DefaultsCreated,

    /// <summary>Legacy v1 file migrated, backed up and saved as v2.</summary>
    MigratedFromV1,

    /// <summary>Primary file was unusable; the backup was loaded.</summary>
    RecoveredFromBackup,

    /// <summary>Everything failed; defaults are used and the failure is logged/reported.</summary>
    FailedDefaults,
}

/// <summary>Result of loading settings.</summary>
/// <param name="Status">Outcome category.</param>
/// <param name="Settings">Usable settings snapshot (defaults on failure).</param>
/// <param name="Validation">Validation report for the returned snapshot.</param>
/// <param name="Migration">Migration report when a v1 file was migrated.</param>
/// <param name="Error">Failure detail when <paramref name="Status"/> is <see cref="SettingsLoadStatus.FailedDefaults"/>.</param>
public sealed record SettingsLoadResult(
    SettingsLoadStatus Status,
    FluidBarSettings Settings,
    SettingsValidationReport Validation,
    MigrationReport? Migration = null,
    string? Error = null)
{
    /// <summary>Whether a usable snapshot was produced (always true except for hard failures).</summary>
    public bool Success => Status != SettingsLoadStatus.FailedDefaults;
}

/// <summary>Result of saving settings.</summary>
/// <param name="Success">Whether the file was written atomically.</param>
/// <param name="Settings">Normalized snapshot that was (or would have been) written.</param>
/// <param name="Validation">Normalization/validation report.</param>
/// <param name="Error">I/O failure detail.</param>
public sealed record SettingsSaveResult(
    bool Success,
    FluidBarSettings Settings,
    SettingsValidationReport Validation,
    string? Error = null);

/// <summary>Export envelope (spec 8.3); plugin secrets are never part of it.</summary>
public sealed record SettingsExportEnvelope
{
    /// <summary>Schema version of the exported settings.</summary>
    public int SchemaVersion { get; init; } = FluidBarSettings.CurrentSchemaVersion;

    /// <summary>Export timestamp (UTC).</summary>
    public DateTimeOffset ExportedAt { get; init; }

    /// <summary>Complete settings tree.</summary>
    public FluidBarSettings Settings { get; init; } = new();
}

/// <summary>Result of exporting settings.</summary>
/// <param name="Success">Whether the file was written.</param>
/// <param name="Path">Target path.</param>
/// <param name="Error">Failure detail.</param>
public sealed record SettingsExportResult(bool Success, string Path, string? Error = null);

/// <summary>Import preview: parsed, migrated and validated settings plus a diff summary.</summary>
/// <param name="Success">Whether the file parsed and validated.</param>
/// <param name="Settings">Normalized settings ready to apply.</param>
/// <param name="Diff">Difference summary against the current settings.</param>
/// <param name="Migration">Migration report when a v1 document was imported.</param>
/// <param name="Validation">Validation report.</param>
/// <param name="Error">Failure detail.</param>
public sealed record SettingsImportPreview(
    bool Success,
    FluidBarSettings? Settings,
    SettingsDiff? Diff,
    MigrationReport? Migration,
    SettingsValidationReport? Validation,
    string? Error = null);

/// <summary>
/// Versioned, atomic settings store (spec 8.1/8.2/hard rule 9): single source of truth,
/// schema-versioned, atomic temp+replace writes with backup, v1 migration, export/import,
/// every failure logged with an event id. The core performs no I/O itself — it composes
/// <see cref="ISettingsFileSystem"/> primitives.
/// </summary>
public sealed class SettingsStore
{
    private readonly object _gate = new();
    private readonly ISettingsFileSystem _fileSystem;
    private readonly SettingsStoreOptions _options;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly DiagnosticCounters? _counters;
    private FluidBarSettings _current;

    /// <summary>Creates a store.</summary>
    /// <param name="fileSystem">Injected file-system abstraction.</param>
    /// <param name="options">Paths and migration switches.</param>
    /// <param name="logger">Structured logger (failures are never silent).</param>
    /// <param name="counters">Optional diagnostics counters.</param>
    /// <param name="timeProvider">Clock for export timestamps.</param>
    public SettingsStore(
        ISettingsFileSystem fileSystem,
        SettingsStoreOptions options,
        IAppLogger? logger = null,
        DiagnosticCounters? counters = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.SettingsPath);
        _fileSystem = fileSystem;
        _options = options;
        _logger = logger ?? NullAppLogger.Instance;
        _counters = counters;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _current = FluidBarSettings.CreateDefault();
    }

    /// <summary>Last successfully loaded/saved snapshot.</summary>
    public FluidBarSettings Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>Loads settings, migrating v1 and recovering from backups when needed.</summary>
    public SettingsLoadResult Load()
    {
        lock (_gate)
        {
            if (!_fileSystem.FileExists(_options.SettingsPath))
            {
                _current = SettingsValidator.Normalize(FluidBarSettings.CreateDefault(), out var defaultsReport);
                _logger.Information(LogCategories.Settings, "settings.load.defaults",
                    "No settings file found; using defaults.");
                return new SettingsLoadResult(SettingsLoadStatus.DefaultsCreated, _current, defaultsReport);
            }

            string text;
            try
            {
                text = _fileSystem.ReadAllText(_options.SettingsPath);
            }
            catch (Exception ex)
            {
                _logger.Error(LogCategories.Settings, "settings.load.readFailed",
                    "Reading the settings file failed.", ex,
                    new Dictionary<string, object?> { ["path"] = _options.SettingsPath });
                return RecoverOrDefaults("settings.load.readFailed", ex.Message);
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return RecoverOrDefaults("settings.load.empty", "Settings file is empty.");
            }

            var version = SettingsMigrator.DetectVersion(text, out var versionError);
            if (versionError is not null)
            {
                return RecoverOrDefaults("settings.load.invalidJson", versionError);
            }

            if (version is null)
            {
                if (!_options.MigrateV1)
                {
                    return RecoverOrDefaults("settings.load.legacyWithoutMigration",
                        "Legacy settings found but v1 migration is disabled.");
                }

                var migration = SettingsMigrator.MigrateV1(text);
                if (!migration.Report.Success)
                {
                    _counters?.Increment(CounterKind.MigrationFailures);
                    _logger.Error(LogCategories.Settings, "settings.migrate.failed",
                        "v1 settings migration failed.", null,
                        new Dictionary<string, object?> { ["error"] = migration.Report.Error });
                    return RecoverOrDefaults("settings.migrate.failed", migration.Report.Error);
                }

                _counters?.Increment(CounterKind.MigrationRuns);
                var normalized = SettingsValidator.Normalize(migration.Settings, out var migrationValidation);
                // Preserve the legacy file before writing the v2 document (spec 10.3.2).
                try
                {
                    _fileSystem.CreateDirectory(DirectoryOf(_options.SettingsPath));
                    _fileSystem.WriteAllText(_options.EffectiveV1BackupPath, text);
                }
                catch (Exception ex)
                {
                    _logger.Error(LogCategories.Settings, "settings.migrate.backupFailed",
                        "Could not preserve the v1 settings file; migration continues.", ex);
                }

                var saveError = WriteLocked(normalized).Error;
                _current = normalized;
                _logger.Information(LogCategories.Settings, "settings.migrate.succeeded",
                    $"Migrated v1 settings ({migration.Report.Mapped.Length} mapped, "
                    + $"{migration.Report.Dropped.Length} dropped).");
                return new SettingsLoadResult(
                    SettingsLoadStatus.MigratedFromV1,
                    normalized,
                    migrationValidation,
                    migration.Report,
                    saveError);
            }

            if (version.Value < FluidBarSettings.CurrentSchemaVersion)
            {
                // In-place schema upgrade (v2 -> v3, 2026-10-30): pre-v3 builds stored general.telemetry from a
                // switch that could not be toggled, so the migrator resets it to the shipped default.
                var upgrade = SettingsMigrator.Migrate(text);
                if (!upgrade.Report.Success)
                {
                    _counters?.Increment(CounterKind.MigrationFailures);
                    _logger.Error(LogCategories.Settings, "settings.upgrade.failed",
                        "Settings schema upgrade failed.", null,
                        new Dictionary<string, object?> { ["error"] = upgrade.Report.Error });
                    return RecoverOrDefaults("settings.upgrade.failed", upgrade.Report.Error);
                }

                _counters?.Increment(CounterKind.MigrationRuns);
                var upgraded = SettingsValidator.Normalize(upgrade.Settings, out var upgradeValidation);
                LogValidation("settings.upgrade", upgradeValidation);
                var upgradeWriteError = WriteLocked(upgraded).Error;
                _current = upgraded;
                _logger.Information(LogCategories.Settings, "settings.upgrade.succeeded",
                    $"Upgraded settings schemaVersion {version.Value} -> {FluidBarSettings.CurrentSchemaVersion}.");
                return new SettingsLoadResult(
                    SettingsLoadStatus.Loaded, upgraded, upgradeValidation, upgrade.Report, upgradeWriteError);
            }

            if (version > FluidBarSettings.CurrentSchemaVersion)
            {
                _logger.Error(LogCategories.Settings, "settings.load.futureSchema",
                    $"Settings schemaVersion {version} is newer than supported {FluidBarSettings.CurrentSchemaVersion}.");
                _current = SettingsValidator.Normalize(FluidBarSettings.CreateDefault(), out var futureReport);
                return new SettingsLoadResult(SettingsLoadStatus.FailedDefaults, _current, futureReport,
                    Error: $"Unsupported future settings schemaVersion {version}.");
            }

            if (!SettingsJson.TryDeserialize(text, out var parsed, out var deserializeError) || parsed is null)
            {
                _logger.Error(LogCategories.Settings, "settings.load.invalidJson",
                    "Settings JSON is invalid.", null,
                    new Dictionary<string, object?> { ["error"] = deserializeError });
                return RecoverOrDefaults("settings.load.invalidJson", deserializeError);
            }

            var validated = SettingsValidator.Normalize(parsed, out var report);
            _current = validated;
            _counters?.Increment(CounterKind.SettingsLoads);
            LogValidation("settings.load", report);
            return new SettingsLoadResult(SettingsLoadStatus.Loaded, validated, report);
        }
    }

    /// <summary>Normalizes and atomically saves settings; updates <see cref="Current"/>.</summary>
    public SettingsSaveResult Save(FluidBarSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            var normalized = SettingsValidator.Normalize(settings, out var report);
            LogValidation("settings.save", report);
            var write = WriteLocked(normalized);
            _current = normalized;
            return new SettingsSaveResult(write.Success, normalized, report, write.Error);
        }
    }

    /// <summary>Exports settings (excluding secrets) to a single JSON envelope, atomically.</summary>
    public SettingsExportResult Export(string path, FluidBarSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            var normalized = SettingsValidator.Normalize(settings, out var report);
            LogValidation("settings.export", report);
            var envelope = new SettingsExportEnvelope
            {
                ExportedAt = _timeProvider.GetUtcNow(),
                Settings = normalized,
            };
            var json = System.Text.Json.JsonSerializer.Serialize(envelope, SettingsJson.Options);
            try
            {
                WriteAtomicLocked(path, json);
                _logger.Information(LogCategories.Settings, "settings.export.succeeded",
                    "Settings exported.", data: new Dictionary<string, object?> { ["path"] = path });
                return new SettingsExportResult(true, path);
            }
            catch (Exception ex)
            {
                _logger.Error(LogCategories.Settings, "settings.export.failed",
                    "Exporting settings failed.", ex);
                return new SettingsExportResult(false, path, ex.Message);
            }
        }
    }

    /// <summary>
    /// Parses an exported/imported file, migrates/validates it and returns a preview with a diff
    /// against <paramref name="current"/>. Nothing is written until the caller confirms and saves.
    /// </summary>
    public SettingsImportPreview Import(string path, FluidBarSettings current)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(current);
        lock (_gate)
        {
            string text;
            try
            {
                if (!_fileSystem.FileExists(path))
                {
                    return new SettingsImportPreview(false, null, null, null, null, $"Import file not found: {path}");
                }

                text = _fileSystem.ReadAllText(path);
            }
            catch (Exception ex)
            {
                _logger.Error(LogCategories.Settings, "settings.import.readFailed", "Reading the import file failed.", ex);
                return new SettingsImportPreview(false, null, null, null, null, ex.Message);
            }

            var raw = text;
            var version = SettingsMigrator.DetectVersion(raw, out var error);
            if (error is not null)
            {
                return new SettingsImportPreview(false, null, null, null, null, error);
            }

            MigrationReport? migration = null;
            FluidBarSettings? settings;
            if (LooksLikeEnvelope(raw))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(raw);
                    var settingsElement = doc.RootElement.GetProperty("settings");
                    raw = settingsElement.GetRawText();
                    version = doc.RootElement.TryGetProperty("schemaVersion", out var versionElement)
                        && versionElement.TryGetInt32(out var exportedVersion)
                        ? exportedVersion
                        : FluidBarSettings.CurrentSchemaVersion;
                }
                catch (System.Text.Json.JsonException ex)
                {
                    return new SettingsImportPreview(false, null, null, null, null, ex.Message);
                }
            }

            if (version is null)
            {
                var migrated = SettingsMigrator.MigrateV1(raw);
                settings = migrated.Settings;
                migration = migrated.Report;
                if (!migrated.Report.Success)
                {
                    return new SettingsImportPreview(false, null, null, migration, null, migrated.Report.Error);
                }
            }
            else if (version > FluidBarSettings.CurrentSchemaVersion)
            {
                return new SettingsImportPreview(false, null, null, null, null,
                    $"Unsupported future settings schemaVersion {version}.");
            }
            else if (!SettingsJson.TryDeserialize(raw, out settings, out var deserializeError) || settings is null)
            {
                return new SettingsImportPreview(false, null, null, null, null, deserializeError);
            }

            var normalized = SettingsValidator.Normalize(settings, out var report);
            var diff = SettingsDiffCalculator.Compare(current, normalized);
            _logger.Information(LogCategories.Settings, "settings.import.preview",
                $"Import preview: +{diff.Added} ~{diff.Modified} -{diff.Removed}.");
            return new SettingsImportPreview(true, normalized, diff, migration, report);
        }
    }

    /// <summary>Loads a snapshot from the backup file (diagnostics/manual recovery).</summary>
    public SettingsLoadResult RecoverFromBackup()
    {
        lock (_gate)
        {
            return RecoverOrDefaults("settings.load.recovered", "Requested manual backup recovery.");
        }
    }

    private SettingsLoadResult RecoverOrDefaults(string eventId, string? error)
    {
        var backupPath = _options.EffectiveBackupPath;
        if (_fileSystem.FileExists(backupPath))
        {
            try
            {
                var backupText = _fileSystem.ReadAllText(backupPath);
                var version = SettingsMigrator.DetectVersion(backupText, out _);
                FluidBarSettings? recovered = null;
                if (version is null)
                {
                    var migration = SettingsMigrator.MigrateV1(backupText);
                    recovered = migration.Report.Success ? migration.Settings : null;
                }
                else if (SettingsJson.TryDeserialize(backupText, out var parsed, out _))
                {
                    recovered = parsed;
                }

                if (recovered is not null)
                {
                    var normalized = SettingsValidator.Normalize(recovered, out var report);
                    _current = normalized;
                    _logger.Warning(LogCategories.Settings, "settings.load.recoveredFromBackup",
                        $"Settings recovered from backup after '{eventId}'.", null,
                        new Dictionary<string, object?> { ["backup"] = backupPath, ["cause"] = error });
                    return new SettingsLoadResult(SettingsLoadStatus.RecoveredFromBackup, normalized, report,
                        Error: error);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(LogCategories.Settings, "settings.load.backupFailed",
                    "Backup recovery failed.", ex);
            }
        }

        _current = SettingsValidator.Normalize(FluidBarSettings.CreateDefault(), out var defaultsReport);
        _logger.Error(LogCategories.Settings, eventId, "Settings could not be loaded; running on defaults.", null,
            new Dictionary<string, object?> { ["error"] = error });
        return new SettingsLoadResult(SettingsLoadStatus.FailedDefaults, _current, defaultsReport, Error: error);
    }

    private (bool Success, string? Error) WriteLocked(FluidBarSettings normalized)
    {
        var json = SettingsJson.Serialize(normalized);
        try
        {
            WriteAtomicLocked(_options.SettingsPath, json);
            _counters?.Increment(CounterKind.SettingsSaves);
            return (true, null);
        }
        catch (Exception ex)
        {
            _counters?.Increment(CounterKind.SettingsSaveFailures);
            _logger.Error(LogCategories.Settings, "settings.save.failed",
                "Atomic settings save failed.", ex,
                new Dictionary<string, object?> { ["path"] = _options.SettingsPath });
            return (false, ex.Message);
        }
    }

    /// <summary>Serialization -&gt; temp write -&gt; atomic replace (with backup) protocol.</summary>
    private void WriteAtomicLocked(string targetPath, string json)
    {
        _fileSystem.CreateDirectory(DirectoryOf(targetPath));
        var tempPath = string.Equals(targetPath, _options.SettingsPath, StringComparison.Ordinal)
            ? _options.EffectiveTempPath
            : targetPath + ".tmp";
        _fileSystem.WriteAllText(tempPath, json);
        if (_fileSystem.FileExists(targetPath))
        {
            try
            {
                _fileSystem.ReplaceFile(tempPath, targetPath, _options.KeepBackup ? _options.EffectiveBackupPath : null);
                return;
            }
            catch (Exception ex)
            {
                // Replace is unavailable (e.g. first write on some file systems); fall back to move.
                _logger.Warning(LogCategories.Settings, "settings.save.replaceFallback",
                    "Atomic replace failed; falling back to move with overwrite.", ex);
            }

            _fileSystem.MoveFile(tempPath, targetPath, overwrite: true);
            return;
        }

        _fileSystem.MoveFile(tempPath, targetPath, overwrite: false);
    }

    private void LogValidation(string eventPrefix, SettingsValidationReport report)
    {
        foreach (var issue in report.Issues)
        {
            var level = issue.Severity == SettingsIssueSeverity.Error
                ? LogLevel.Warning
                : LogLevel.Debug;
            _logger.Log(level, LogCategories.Settings, $"{eventPrefix}.validation",
                $"Settings {issue.ErrorCodeOrPath()}: {issue.Message}",
                data: new Dictionary<string, object?>
                {
                    ["path"] = issue.Path,
                    ["code"] = issue.Code,
                    ["severity"] = issue.Severity.ToString(),
                });
        }
    }

    private static SettingsValidationReport MergeReports(SettingsValidationReport first, SettingsValidationReport second)
    {
        if (second.Count == 0)
        {
            return first;
        }

        return new SettingsValidationReport { Issues = [.. first.Issues, .. second.Issues] };
    }

    private static string DirectoryOf(string path)
    {
        var index = path.LastIndexOfAny(['/', '\\']);
        return index <= 0 ? string.Empty : path[..index];
    }

    private static bool LooksLikeEnvelope(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && doc.RootElement.TryGetProperty("settings", out _);
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }
}

internal static class SettingsIssueExtensions
{
    public static string ErrorCodeOrPath(this SettingsIssue issue) => $"[{issue.Code}] {issue.Path}";
}
