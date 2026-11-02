using System.Runtime.CompilerServices;

namespace FluidBar.Platform.Windows.Diagnostics;

/// <summary>Opt-in trace using a bounded queue, background writer and rolling files.</summary>
internal sealed class DiagnosticTrace : IAsyncDisposable
{
    private readonly RollingFileLogger _sink;
    private readonly Func<bool> _enabled;
    private readonly Lazy<Task> _start;
    private int _failed;
    private int _disposed;
    private int _flushFailures;
    internal DiagnosticTrace(RollingLogOptions options, Func<bool> enabled)
    {
        _sink = new RollingFileLogger(options);
        _enabled = enabled;
        _start = new Lazy<Task>(StartAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    }
    internal bool Enabled => Volatile.Read(ref _disposed) == 0 && Volatile.Read(ref _failed) == 0 && _enabled();
    internal void Log([InterpolatedStringHandlerArgument("")] ref DiagnosticMessage message, [CallerMemberName] string caller = "")
    {
        if (message.IsEnabled) WriteSafe(message.GetText(), caller);
    }
    internal void Log(string unstructuredMessage, [CallerMemberName] string caller = "")
        => WriteSafe("Unstructured diagnostic content omitted.", caller);
    internal void WriteSafe(string message, string caller)
    {
        if (!Enabled) return;
        _ = _start.Value;
        // Debug entries drop when full, never taking RollingFileLogger's synchronous Error fallback.
        _sink.Log(new PlatformLogEntry(DateTimeOffset.UtcNow, PlatformLogLevel.Debug, PlatformLogCategory.Diagnostics,
            "diagnostics.trace", message, new Dictionary<string, object?> { ["caller"] = caller }));
    }
    private async Task StartAsync()
    {
        try { await _sink.StartAsync().ConfigureAwait(false); }
        catch (Exception) { Volatile.Write(ref _failed, 1); }
    }
    internal async Task FlushAsync()
    {
        if (!_start.IsValueCreated) return;
        await _start.Value.ConfigureAwait(false);
        if (Volatile.Read(ref _failed) != 0) return;
        try { await _sink.FlushAsync().ConfigureAwait(false); }
        catch (Exception) { Interlocked.Increment(ref _flushFailures); }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_start.IsValueCreated) await _start.Value.ConfigureAwait(false);
        await _sink.DisposeAsync().ConfigureAwait(false);
    }
}
