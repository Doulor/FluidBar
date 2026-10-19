using FluidBar.Core.Diagnostics;
using FluidBar.Platform.Windows.CoreIntegration;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Shell;

namespace FluidBar.App.Settings;

/// <summary>
/// Keeps the "start with Windows" setting true to reality: reconciles
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> with <see cref="AppSettings.StartWithWindows"/>
/// at startup (including a one-shot self-heal when the value was removed externally) and whenever the
/// toggle changes. A failed registry write is surfaced and the setting is reverted instead of lying.
/// </summary>
/// <remarks>
/// HKCU only, no elevation, no scheduled task (spec §8.4). All registry I/O happens on the thread pool;
/// the settings callback never blocks the UI thread.
/// </remarks>
public sealed class StartupRegistrationSync : IDisposable
{
    /// <summary>Argument appended to the Run command (spec §8.4: start in the background).</summary>
    public const string StartupArgument = "--background";

    private readonly SettingsController _settings;
    private readonly StartupRegistrationService _service;
    private readonly IAppLogger _logger;
    private readonly string _executablePath;
    private readonly object _sync = new();
    private int _disposed;
    private int _reverting;

    /// <summary>Creates the sync and attaches to settings changes.</summary>
    /// <param name="settings">Live settings controller.</param>
    /// <param name="logger">Structured logger.</param>
    /// <param name="service">Registry seam (tests inject a fake); defaults to the real HKCU Run key.</param>
    /// <param name="executablePath">Executable registered for startup; defaults to the current process.</param>
    public StartupRegistrationSync(SettingsController settings, IAppLogger logger,
        StartupRegistrationService? service = null, string? executablePath = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? NullAppLogger.Instance;
        _executablePath = executablePath
            ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("The current executable path is unavailable.");
        _service = service ?? StartupRegistrationService.CreateForCurrentUser(
            new AppLoggerPlatformAdapter(_logger));
    }

    /// <summary>Attaches the settings-changed handler (call once after construction).</summary>
    public void Attach() => _settings.Changed += OnSettingsChanged;

    /// <summary>Reconciles the Run key with the current setting (startup + first enable).</summary>
    /// <param name="selfHeal">Rewrite the value when it is missing/mismatched (startup self-heal).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SyncAsync(bool selfHeal, CancellationToken cancellationToken = default)
    {
        var desired = _settings.Current.StartWithWindows;
        var state = await _service.QueryAsync(_executablePath, cancellationToken).ConfigureAwait(false);
        if (desired)
        {
            if (state.MatchesCurrentExecutable && !string.IsNullOrEmpty(state.Command))
            {
                return;
            }

            if (!selfHeal && state.IsEnabled && state.MatchesCurrentExecutable)
            {
                return;
            }

            await ApplyAsync(true, cancellationToken).ConfigureAwait(false);
            if (state.IsEnabled)
            {
                _logger.Warning(LogCategories.Settings, "settings.startup.self-healed",
                    "The startup registration was missing or pointed at another executable; it was rewritten.",
                    data: new Dictionary<string, object?> { ["command"] = state.Command });
            }

            return;
        }

        if (state.IsEnabled)
        {
            await ApplyAsync(false, cancellationToken).ConfigureAwait(false);
        }
    }

    private void OnSettingsChanged(AppSettings previous, AppSettings current)
    {
        if (previous.StartWithWindows == current.StartWithWindows || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await ApplyAsync(current.StartWithWindows).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warning(LogCategories.Settings, "settings.startup.apply-failed",
                    "Applying the start-with-Windows setting failed.", ex);
            }
        });
    }

    private async Task ApplyAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        try
        {
            if (enabled)
            {
                await _service.EnableAsync(_executablePath, StartupArgument, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _service.DisableAsync(cancellationToken).ConfigureAwait(false);
            }

            var actual = await _service.QueryAsync(_executablePath, cancellationToken).ConfigureAwait(false);
            var matches = enabled ? actual.MatchesCurrentExecutable : !actual.IsEnabled;
            if (!matches)
            {
                RevertSetting(actual.IsEnabled);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.Warning(LogCategories.Settings, "settings.startup.registry-failed",
                "The start-with-Windows registry value could not be written; reverting the setting.", ex);
            var actual = await _service.QueryAsync(_executablePath, cancellationToken).ConfigureAwait(false);
            RevertSetting(actual.IsEnabled);
        }
    }

    private void RevertSetting(bool actualEnabled)
    {
        if (Interlocked.Exchange(ref _reverting, 1) != 0)
        {
            return;
        }

        try
        {
            _settings.Update(settings => settings.StartWithWindows == actualEnabled
                ? settings
                : settings with { StartWithWindows = actualEnabled });
        }
        finally
        {
            Interlocked.Exchange(ref _reverting, 0);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _settings.Changed -= OnSettingsChanged;
    }
}
