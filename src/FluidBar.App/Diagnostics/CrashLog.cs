using System.Text;
using FluidBar.Core.Diagnostics;

namespace FluidBar.App.Diagnostics;

/// <summary>
/// Last-resort crash logging (Lead request 2026-10-18): installs process-wide handlers that append a
/// crash entry to <c>%LOCALAPPDATA%\FluidBar\logs\crash.log</c> before the process dies. Hook it once
/// from <c>Program.Main</c>: <c>CrashLog.Install()</c> (Program.cs is owned by Platform during the host
/// hotfix; the line is intentionally not added there yet).
/// </summary>
public static class CrashLog
{
    private static readonly object Gate = new();
    private static int _installed;

    /// <summary>Installs the process-wide unhandled-exception handlers (idempotent).</summary>
    public static void Install(IAppLogger? logger = null)
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write("appdomain", e.ExceptionObject as Exception, e.IsTerminating, logger);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            Write("task", e.Exception, isTerminating: false, logger);
        };
    }

    /// <summary>Default crash log path (local app data).</summary>
    public static string DefaultPath
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FluidBar",
            "logs",
            "crash.log");

    /// <summary>Formats one crash entry (pure; unit-tested).</summary>
    public static string FormatEntry(string source, Exception? exception, bool isTerminating, DateTimeOffset timestamp)
    {
        var builder = new StringBuilder(512);
        builder.Append(timestamp.ToString("O")).Append(" source=").Append(source)
            .Append(" terminating=").Append(isTerminating ? "true" : "false");
        if (exception is null)
        {
            builder.Append(" exception=<none>");
            return builder.ToString();
        }

        builder.Append(' ').Append(exception.GetType().FullName).Append(": ").Append(exception.Message)
            .AppendLine().Append(exception.StackTrace);
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            builder.AppendLine().Append(" ---> ").Append(inner.GetType().FullName).Append(": ").Append(inner.Message)
                .AppendLine().Append(inner.StackTrace);
        }

        return builder.ToString();
    }

    /// <summary>Writes a crash entry synchronously (crash handlers cannot await).</summary>
    public static void Write(string source, Exception? exception, bool isTerminating, IAppLogger? logger = null)
    {
        var entry = FormatEntry(source, exception, isTerminating, DateTimeOffset.Now);
        try
        {
            var path = DefaultPath;
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, entry + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Nothing else can be done while the process is crashing; keep the diagnostic visible.
            System.Diagnostics.Debug.WriteLine("CrashLog write failed: " + ex.Message);
        }

        try
        {
            logger?.Critical(LogCategories.Lifecycle, "app.unhandled",
                "Unhandled exception captured by CrashLog.", exception,
                new Dictionary<string, object?> { ["source"] = source, ["terminating"] = isTerminating });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("CrashLog logger failed: " + ex.Message);
        }
    }
}
