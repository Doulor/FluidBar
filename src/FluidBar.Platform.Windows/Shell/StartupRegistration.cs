using System.Runtime.InteropServices;
using System.Text;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Interop;

namespace FluidBar.Platform.Windows.Shell;

/// <summary>Minimal store abstraction over the HKCU Run key so startup registration is unit testable.</summary>
public interface IRegistryValueStore
{
    /// <summary>Reads a REG_SZ value; returns null when the key/value does not exist.</summary>
    string? GetString(string subKeyPath, string valueName);

    /// <summary>Writes a REG_SZ value, creating the key when needed.</summary>
    void SetString(string subKeyPath, string valueName, string value);

    /// <summary>Deletes a value; returns false when it did not exist or the key is missing.</summary>
    bool DeleteValue(string subKeyPath, string valueName);
}

/// <summary>
/// Registry store over <c>advapi32</c>. All calls are blocking Win32 registry I/O and are therefore
/// always invoked from the thread pool by <see cref="StartupRegistrationService"/>.
/// </summary>
internal sealed class NativeRegistryValueStore : IRegistryValueStore
{
    public string? GetString(string subKeyPath, string valueName)
    {
        var result = NativeMethods.Advapi32.RegOpenKeyExW(NativeMethods.HKEY_CURRENT_USER, subKeyPath, 0,
            NativeMethods.KEY_QUERY_VALUE, out var rawKey);
        if (result != 0)
        {
            return null;
        }

        using var key = SafeRegistryKey.FromRaw(rawKey);
        uint type = 0;
        uint size = 0;
        result = NativeMethods.Advapi32.RegQueryValueExW(key.DangerousGetHandle(), valueName, IntPtr.Zero,
            out type, null, ref size);
        if (result != 0 || size == 0)
        {
            return null;
        }

        var buffer = new byte[size];
        result = NativeMethods.Advapi32.RegQueryValueExW(key.DangerousGetHandle(), valueName, IntPtr.Zero,
            out type, buffer, ref size);
        if (result != 0 || type is not (NativeMethods.REG_SZ or NativeMethods.REG_EXPAND_SZ))
        {
            return null;
        }

        var text = Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0');
        return text;
    }

    public void SetString(string subKeyPath, string valueName, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        const uint access = NativeMethods.KEY_SET_VALUE | NativeMethods.KEY_QUERY_VALUE;
        var result = NativeMethods.Advapi32.RegCreateKeyExW(NativeMethods.HKEY_CURRENT_USER, subKeyPath, 0, null,
            NativeMethods.REG_OPTION_NON_VOLATILE, access, IntPtr.Zero, out var rawKey, out _);
        if (result != 0)
        {
            throw Win32Error.CreateException($"RegCreateKeyEx('{subKeyPath}')", result);
        }

        using var key = SafeRegistryKey.FromRaw(rawKey);
        var bytes = Encoding.Unicode.GetBytes(value + "\0");
        result = NativeMethods.Advapi32.RegSetValueExW(key.DangerousGetHandle(), valueName, 0,
            NativeMethods.REG_SZ, bytes, (uint)bytes.Length);
        if (result != 0)
        {
            throw Win32Error.CreateException($"RegSetValueEx('{subKeyPath}\\{valueName}')", result);
        }
    }

    public bool DeleteValue(string subKeyPath, string valueName)
    {
        var result = NativeMethods.Advapi32.RegOpenKeyExW(NativeMethods.HKEY_CURRENT_USER, subKeyPath, 0,
            NativeMethods.KEY_SET_VALUE, out var rawKey);
        if (result != 0)
        {
            return false;
        }

        using var key = SafeRegistryKey.FromRaw(rawKey);
        result = NativeMethods.Advapi32.RegDeleteValueW(key.DangerousGetHandle(), valueName);
        return result == 0;
    }
}

/// <summary>Builds the <c>Run</c> command line for the current executable.</summary>
public static class StartupCommandBuilder
{
    /// <summary>Quotes an absolute executable path and appends arguments (already-quoted arguments are left alone).</summary>
    public static string BuildCommand(string executablePath, string? arguments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (!Path.IsPathFullyQualified(executablePath))
        {
            throw new ArgumentException("The startup executable path must be absolute.", nameof(executablePath));
        }

        var command = Quote(executablePath);
        return string.IsNullOrWhiteSpace(arguments) ? command : $"{command} {arguments.Trim()}";
    }

    private static string Quote(string value)
        => value.Contains(' ', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
}

/// <summary>Current startup-registration state.</summary>
/// <param name="IsEnabled">Whether a Run value exists.</param>
/// <param name="Command">Registered command line, if any.</param>
/// <param name="MatchesCurrentExecutable">Whether the command starts the given executable path.</param>
public readonly record struct StartupRegistrationState(bool IsEnabled, string? Command, bool MatchesCurrentExecutable);

/// <summary>
/// Opt-in "start with Windows" registration in <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>.
/// </summary>
/// <remarks>
/// HKCU only (never machine-wide, no elevation). Reads/writes are small registry operations executed on
/// the thread pool — never on the UI thread, and never on a timer: the user toggles it explicitly, and the
/// service only reconciles when asked.
/// </remarks>
public sealed class StartupRegistrationService
{
    /// <summary>HKCU Run key path.</summary>
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Default value name used for the app.</summary>
    public const string DefaultValueName = "FluidBar";

    private readonly IRegistryValueStore _store;
    private readonly IPlatformLogger _logger;
    private readonly string _valueName;

    public StartupRegistrationService(IRegistryValueStore store, IPlatformLogger logger, string valueName = DefaultValueName)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? NullPlatformLogger.Instance;
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);
        _valueName = valueName;
    }

    /// <summary>Reads the current registration on the thread pool.</summary>
    public async Task<StartupRegistrationState> QueryAsync(string? expectedExecutablePath = null,
        CancellationToken cancellationToken = default)
    {
        var command = await Task.Run(() => _store.GetString(RunKeyPath, _valueName), cancellationToken)
            .ConfigureAwait(false);
        var matches = !string.IsNullOrEmpty(command) && !string.IsNullOrEmpty(expectedExecutablePath)
                      && command.Contains(expectedExecutablePath, StringComparison.OrdinalIgnoreCase);
        return new StartupRegistrationState(!string.IsNullOrEmpty(command), command, matches);
    }

    /// <summary>Enables startup for the given executable. Throws when the registry write fails (caller shows an error).</summary>
    public async Task EnableAsync(string executablePath, string? arguments = null,
        CancellationToken cancellationToken = default)
    {
        var command = StartupCommandBuilder.BuildCommand(executablePath, arguments);
        await Task.Run(() => _store.SetString(RunKeyPath, _valueName, command), cancellationToken)
            .ConfigureAwait(false);
        _logger.LogInfo(PlatformLogCategory.Shell, "startup.enabled", "Startup registration enabled.",
            new Dictionary<string, object?> { ["command"] = command });
    }

    /// <summary>Disables startup. Missing values are not an error (idempotent).</summary>
    public async Task DisableAsync(CancellationToken cancellationToken = default)
    {
        var deleted = await Task.Run(() => _store.DeleteValue(RunKeyPath, _valueName), cancellationToken)
            .ConfigureAwait(false);
        _logger.LogInfo(PlatformLogCategory.Shell, "startup.disabled", "Startup registration disabled.",
            new Dictionary<string, object?> { ["existed"] = deleted });
    }
}
