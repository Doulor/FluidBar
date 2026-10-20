using FluidBar.Core.Diagnostics;
using FluidBar.Core.Settings;
using AppAppearance = FluidBar.App.Settings;
using CoreTheme = FluidBar.Core.Settings.AppTheme;
using CoreMaterial = FluidBar.Core.Settings.IslandMaterial;
using CorePalette = FluidBar.Core.Settings.ColorPalette;
using CoreAccentMode = FluidBar.Core.Settings.AccentMode;
using CoreMotion = FluidBar.Core.Settings.MotionPreference;
using CoreStacking = FluidBar.Core.Settings.StackingPreference;
using CoreDisplayTarget = FluidBar.Core.Settings.DisplayTarget;

namespace FluidBar.App.Settings;

/// <summary>
/// Production <see cref="ISettingsFileSystem"/> over <see cref="System.IO"/> for Core
/// <see cref="SettingsStore"/>. All calls happen on the thread pool (the adapter wraps the store in
/// <see cref="Task.Run(System.Action)"/>); nothing here runs on the UI thread.
/// </summary>
public sealed class SystemSettingsFileSystem : ISettingsFileSystem
{
    /// <inheritdoc />
    public bool FileExists(string path) => File.Exists(path);

    /// <inheritdoc />
    public string ReadAllText(string path) => File.ReadAllText(path);

    /// <inheritdoc />
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    /// <inheritdoc />
    public void WriteAllText(string path, string contents)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream);
        writer.Write(contents);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    /// <inheritdoc />
    public void ReplaceFile(string source, string destination, string? backupPath)
    {
        if (File.Exists(destination))
        {
            File.Replace(source, destination, backupPath, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(source, destination, overwrite: true);
        }
    }

    /// <inheritdoc />
    public void MoveFile(string source, string destination, bool overwrite) => File.Move(source, destination, overwrite);

    /// <inheritdoc />
    public void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}

/// <summary>
/// Core-<see cref="SettingsStore"/> backed implementation of the App's <see cref="IUiSettingsStore"/>
/// seam (spec 8.1/8.2). It replaces the temporary <c>settings.ui.json</c> adapter while keeping the UI
/// strongly-typed model: <see cref="AppSettings"/> is mapped onto <see cref="FluidBarSettings"/> for
/// persistence (read-modify-write preserves Core-owned sections such as hotkeys/activities/placement).
/// Load/save run on the thread pool; saving is debounced and goes through Core's atomic write protocol.
/// </summary>
public sealed class CoreSettingsStoreAdapter : IUiSettingsStore, IDisposable
{
    private readonly SettingsStore _store;
    private readonly IAppLogger _logger;
    private readonly TimeSpan _debounce;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _ioGate = new(1, 1);
    private FluidBarSettings _lastCore = new();
    private bool _legacyImportChecked;
    private AppSettings? _pending;
    private long _pendingSequence;
    private long _writtenSequence;
    private ITimer? _debounceTimer;
    private Task _writeTask = Task.CompletedTask;
    private bool _disposed;

    /// <summary>Creates an adapter over an existing Core store (tests/injection).</summary>
    public CoreSettingsStoreAdapter(SettingsStore store, IAppLogger? logger = null, TimeSpan? debounce = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? NullAppLogger.Instance;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(500);
    }

    /// <summary>Creates the production adapter rooted at <c>%APPDATA%\FluidBar\settings.json</c>.</summary>
    public static CoreSettingsStoreAdapter CreateDefault(IAppLogger? logger = null, string? appDataDirectory = null)
    {
        var root = appDataDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluidBar");
        var fileSystem = new SystemSettingsFileSystem();
        var options = SettingsStoreOptions.ForAppData(root);
        var store = new SettingsStore(fileSystem, options, logger);
        var legacyPath = Path.Combine(root, LegacySettingsImporter.LegacyFileName);
        return new CoreSettingsStoreAdapter(store, logger)
        {
            LegacyImport = () => LegacySettingsImporter.ImportIfNeeded(
                fileSystem, store, legacyPath, options.SettingsPath, logger),
        };
    }

    /// <summary>Optional one-time legacy import executed on the first background load (tests/integration).</summary>
    public Func<LegacyImportResult>? LegacyImport { get; init; }

    /// <summary>Result of the one-time legacy import (diagnostics/tests); null when not run yet.</summary>
    public LegacyImportResult? LegacyImportResult { get; private set; }

    /// <inheritdoc />
    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_legacyImportChecked)
                {
                    _legacyImportChecked = true;
                    if (LegacyImport is not null)
                    {
                        LegacyImportResult = LegacyImport();
                    }
                }

                var result = _store.Load();
                _lastCore = result.Settings;
                if (!result.Success)
                {
                    _logger.Warning(LogCategories.Settings, "settings.core.load-failed",
                        "Core settings store reported a failed load; using defaults.",
                        data: new Dictionary<string, object?> { ["status"] = result.Status.ToString(), ["error"] = result.Error });
                }

                return ToAppSettings(result.Settings);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public void QueueSave(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _pending = settings.Normalize();
            _pendingSequence++;
            _debounceTimer?.Dispose();
            _debounceTimer = TimeProvider.System.CreateTimer(
                static state => _ = ((CoreSettingsStoreAdapter)state!).WritePendingAsync(),
                this,
                _debounce,
                Timeout.InfiniteTimeSpan);
        }
    }

    /// <inheritdoc />
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        Task writeTask;
        lock (_gate)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
            writeTask = _writeTask;
        }

        await writeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        await WritePendingAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
    }

    /// <summary>Maps the Core persisted model to the App's strongly-typed UI model.</summary>
    public static AppSettings ToAppSettings(FluidBarSettings core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return new AppSettings
        {
            SchemaVersion = core.SchemaVersion,
            Language = core.General.Language,
            StartWithWindows = core.General.StartWithWindows,
            TrayIcon = core.General.TrayIcon,
            AutoUpdate = core.General.AutoUpdate switch
            {
                AutoUpdatePreference.Notify => "notify",
                AutoUpdatePreference.Off => "off",
                _ => "notify",
            },
            Theme = core.Appearance.Theme switch
            {
                CoreTheme.Dark => AppAppearance.AppTheme.Dark,
                CoreTheme.Light => AppAppearance.AppTheme.Light,
                _ => AppAppearance.AppTheme.System,
            },
            Material = core.Appearance.Material == CoreMaterial.Acrylic ? AppAppearance.IslandMaterial.Acrylic : AppAppearance.IslandMaterial.Obsidian,
            Palette = core.Appearance.Palette switch
            {
                CorePalette.Graphite => "graphite",
                CorePalette.Slate => "slate",
                CorePalette.Nord => "nord",
                _ => "obsidian",
            },
            AccentMode = core.Appearance.Accent.Mode switch
            {
                CoreAccentMode.System => "system",
                CoreAccentMode.Custom => "custom",
                _ => "artwork",
            },
            AccentFallback = core.Appearance.Accent.FallbackHex,
            FillOpacity = core.Appearance.FillOpacity,
            FontScale = core.Appearance.FontScale,
            MotionStrength = core.Appearance.MotionStrength,
            ReducedMotion = core.Appearance.ReducedMotion switch
            {
                CoreMotion.Off => AppAppearance.ReducedMotionPreference.On,
                CoreMotion.Enabled => AppAppearance.ReducedMotionPreference.Off,
                _ => AppAppearance.ReducedMotionPreference.System,
            },
            Mode = core.Island.Mode switch
            {
                FluidBar.Core.Layout.IslandMode.Flush => "flush",
                FluidBar.Core.Layout.IslandMode.Dock => "dock",
                _ => "float",
            },
            Anchor = core.Island.Anchor switch
            {
                FluidBar.Core.Layout.IslandAnchor.TopLeft => "top-left",
                FluidBar.Core.Layout.IslandAnchor.TopRight => "top-right",
                FluidBar.Core.Layout.IslandAnchor.BottomLeft => "bottom-left",
                FluidBar.Core.Layout.IslandAnchor.BottomCenter => "bottom-center",
                FluidBar.Core.Layout.IslandAnchor.BottomRight => "bottom-right",
                _ => "top-center",
            },
            Stacking = core.Island.Stacking switch
            {
                CoreStacking.Latest => AppAppearance.StackingStrategySetting.Latest,
                CoreStacking.Stack => AppAppearance.StackingStrategySetting.Stacked,
                _ => AppAppearance.StackingStrategySetting.Split,
            },
            HoverPeek = core.Island.HoverPeek,
            AutoCollapseMs = core.Island.AutoCollapseMs,
            AutoHideMs = core.Island.AutoHideMs,
            PeekOnTrackChange = core.Island.PeekOnTrackChange,
            DisplayOn = core.Island.DisplayOn switch
            {
                CoreDisplayTarget.All => AppAppearance.DisplayTarget.All,
                CoreDisplayTarget.FollowMouse => AppAppearance.DisplayTarget.FollowMouse,
                _ => AppAppearance.DisplayTarget.Primary,
            },
            Dock = new DockSettings { HideSystemTaskbar = core.Dock.HideSystemTaskbar, Height = core.Dock.Height },
            Plugins = new PluginSettings
            {
                Enabled = core.Plugins.Enabled,
                Channel = core.Plugins.Channel,
                CheckIntervalHours = core.Plugins.CheckIntervalHours,
            },
            Advanced = new AdvancedSettings
            {
                LogLevel = core.Advanced.LogLevel,
                CrashGuard = core.Advanced.CrashGuard,
                FrameStats = core.Advanced.FrameStats,
                SafeMode = core.General.SafeMode,
            },
        }.Normalize();
    }

    /// <summary>
    /// Applies the App model onto an existing Core snapshot (read-modify-write so Core-owned sections
    /// such as hotkeys, activities, placement and market URLs are preserved).
    /// </summary>
    public static FluidBarSettings ApplyToCore(FluidBarSettings core, AppSettings app)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(app);
        return core with
        {
            SchemaVersion = FluidBarSettings.CurrentSchemaVersion,
            General = core.General with
            {
                Language = app.Language,
                StartWithWindows = app.StartWithWindows,
                TrayIcon = app.TrayIcon,
                AutoUpdate = app.AutoUpdate switch
                {
                    "auto" => AutoUpdatePreference.Notify,
                    "off" => AutoUpdatePreference.Off,
                    _ => AutoUpdatePreference.Notify,
                },
                SafeMode = app.Advanced.SafeMode,
            },
            Appearance = core.Appearance with
            {
                Theme = app.Theme switch
                {
                    AppAppearance.AppTheme.Dark => CoreTheme.Dark,
                    AppAppearance.AppTheme.Light => CoreTheme.Light,
                    _ => CoreTheme.System,
                },
                Material = app.Material == AppAppearance.IslandMaterial.Acrylic ? CoreMaterial.Acrylic : CoreMaterial.Obsidian,
                Palette = app.Palette switch
                {
                    "graphite" => CorePalette.Graphite,
                    "slate" => CorePalette.Slate,
                    "nord" => CorePalette.Nord,
                    _ => CorePalette.Obsidian,
                },
                Accent = core.Appearance.Accent with
                {
                    Mode = app.AccentMode switch
                    {
                        "system" => CoreAccentMode.System,
                        "custom" => CoreAccentMode.Custom,
                        _ => CoreAccentMode.Artwork,
                    },
                    FallbackHex = app.AccentFallback,
                },
                FillOpacity = app.FillOpacity,
                FontScale = app.FontScale,
                MotionStrength = app.MotionStrength,
                ReducedMotion = app.ReducedMotion switch
                {
                    AppAppearance.ReducedMotionPreference.On => CoreMotion.Off,
                    AppAppearance.ReducedMotionPreference.Off => CoreMotion.Enabled,
                    _ => CoreMotion.System,
                },
            },
            Island = core.Island with
            {
                Mode = app.Mode switch
                {
                    "flush" => FluidBar.Core.Layout.IslandMode.Flush,
                    "dock" => FluidBar.Core.Layout.IslandMode.Dock,
                    _ => FluidBar.Core.Layout.IslandMode.Float,
                },
                Anchor = app.Anchor switch
                {
                    "top-left" => FluidBar.Core.Layout.IslandAnchor.TopLeft,
                    "top-right" => FluidBar.Core.Layout.IslandAnchor.TopRight,
                    "bottom-left" => FluidBar.Core.Layout.IslandAnchor.BottomLeft,
                    "bottom-center" => FluidBar.Core.Layout.IslandAnchor.BottomCenter,
                    "bottom-right" => FluidBar.Core.Layout.IslandAnchor.BottomRight,
                    _ => FluidBar.Core.Layout.IslandAnchor.TopCenter,
                },
                Stacking = app.Stacking switch
                {
                    AppAppearance.StackingStrategySetting.Latest => CoreStacking.Latest,
                    AppAppearance.StackingStrategySetting.Stacked => CoreStacking.Stack,
                    _ => CoreStacking.Split,
                },
                HoverPeek = app.HoverPeek,
                AutoCollapseMs = app.AutoCollapseMs,
                AutoHideMs = app.AutoHideMs,
                PeekOnTrackChange = app.PeekOnTrackChange,
                DisplayOn = app.DisplayOn switch
                {
                    AppAppearance.DisplayTarget.All => CoreDisplayTarget.All,
                    AppAppearance.DisplayTarget.FollowMouse => CoreDisplayTarget.FollowMouse,
                    _ => CoreDisplayTarget.Primary,
                },
            },
            Dock = core.Dock with { HideSystemTaskbar = app.Dock.HideSystemTaskbar, Height = app.Dock.Height },
            Plugins = core.Plugins with
            {
                Enabled = [.. app.Plugins.Enabled],
                Channel = app.Plugins.Channel,
                CheckIntervalHours = app.Plugins.CheckIntervalHours,
            },
            Advanced = core.Advanced with
            {
                LogLevel = app.Advanced.LogLevel,
                CrashGuard = app.Advanced.CrashGuard,
                FrameStats = app.Advanced.FrameStats,
            },
        };
    }

    /// <summary>Number of completed Core saves (tests/diagnostics).</summary>
    public int SaveCount => Interlocked.CompareExchange(ref _saveCount, 0, 0);

    private int _saveCount;

    /// <summary>Last Core snapshot written/loaded (tests/diagnostics).</summary>
    public FluidBarSettings LastCore => _lastCore;

    private Task WritePendingAsync()
    {
        AppSettings? snapshot;
        long sequence;
        lock (_gate)
        {
            if (_pending is null || _pendingSequence == _writtenSequence)
            {
                return _writeTask;
            }

            snapshot = _pending;
            sequence = _pendingSequence;
        }

        if (snapshot is null)
        {
            return _writeTask;
        }

        _writeTask = Task.Run(async () =>
        {
            await _ioGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var next = ApplyToCore(_lastCore, snapshot);
                var result = _store.Save(next);
                if (!result.Success)
                {
                    _logger.Error(LogCategories.Settings, "settings.core.save-failed",
                        "Core settings save reported failure.",
                        data: new Dictionary<string, object?> { ["error"] = result.Error });
                    return;
                }

                _lastCore = result.Settings;
                Interlocked.Increment(ref _saveCount);
                lock (_gate)
                {
                    _writtenSequence = Math.Max(_writtenSequence, sequence);
                }
            }
            finally
            {
                _ioGate.Release();
            }
        });
        return _writeTask;
    }
}
