using System.Runtime.CompilerServices;
using FluidBar.Platform.Windows.Diagnostics;

namespace FluidBar.Platform.Windows.Media;

/// <summary>Explicitly enabled, redacted diagnostics. Never appends plaintext to a shared temporary file.</summary>
public static class MediaDiagnostics
{
    private static int _stopped;
    private static readonly Lazy<DiagnosticTrace> Trace = new(() => new DiagnosticTrace(new RollingLogOptions
    {
        DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluidBar", "diagnostics"),
        FilePrefix = "fluidbar-diagnostics", MaxFileBytes = 1024 * 1024, MaxFiles = 2, ChannelCapacity = 256,
        MinimumLevel = PlatformLogLevel.Debug, MaxEntryBytes = 4096,
    }, () => Enabled));

    public static bool Enabled => Volatile.Read(ref _stopped) == 0
        && string.Equals(Environment.GetEnvironmentVariable("FLUIDBAR_PIN_DIAG"), "1", StringComparison.Ordinal);

    public static void Log(ref DiagnosticMessage message, [CallerMemberName] string caller = "")
    {
        if (Enabled && message.IsEnabled) Trace.Value.WriteSafe(message.GetText(), caller);
    }

    // Retain the legacy API name, with the same opt-in and privacy policy as Log.
    public static void LogAlways(ref DiagnosticMessage message, [CallerMemberName] string caller = "") => Log(ref message, caller);

    public static void Log(string message, [CallerMemberName] string caller = "")
    {
        if (Enabled) Trace.Value.Log(message, caller);
    }
    public static void LogAlways(string message, [CallerMemberName] string caller = "") => Log(message, caller);

    public static ValueTask ShutdownAsync()
    {
        Interlocked.Exchange(ref _stopped, 1);
        return Trace.IsValueCreated ? Trace.Value.DisposeAsync() : ValueTask.CompletedTask;
    }
}
