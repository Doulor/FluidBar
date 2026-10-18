using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace FluidBar.App;

/// <summary>Process entry point for the FluidBar Avalonia shell (spec 5.6/5.7).</summary>
internal static class Program
{
    /// <summary>Starts the desktop application.</summary>
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Builds the Avalonia app with the composition backend required for per-pixel alpha.</summary>
    public static AppBuilder BuildAvaloniaApp()
    {
        var traceLog = Environment.GetEnvironmentVariable("FLUIDBAR_TRACE_LOG");
        if (!string.IsNullOrWhiteSpace(traceLog))
        {
            System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(traceLog)
            {
                TraceOutputOptions = System.Diagnostics.TraceOptions.Timestamp,
            });
            System.Diagnostics.Trace.AutoFlush = true;
        }

        if (string.Equals(Environment.GetEnvironmentVariable("FLUIDBAR_SPIKE_APP"), "1", StringComparison.Ordinal))
        {
            // Diagnostic A/B: exact spike Application/window code inside the App assembly (M6 hotfix).
            return AppBuilder.Configure<SpikeProbeApp>().UsePlatformDetect()
                .LogToTrace(Avalonia.Logging.LogEventLevel.Verbose);
        }

        var builder = AppBuilder.Configure<FluidBarApplication>().UsePlatformDetect();
        var composition = Environment.GetEnvironmentVariable("FLUIDBAR_COMPOSITION");
        if (string.Equals(composition, "winui", StringComparison.OrdinalIgnoreCase))
        {
            builder = builder.With(new Win32PlatformOptions { CompositionMode = [Win32CompositionMode.WinUIComposition] });
        }
        else if (string.Equals(composition, "redirection", StringComparison.OrdinalIgnoreCase))
        {
            builder = builder.With(new Win32PlatformOptions { CompositionMode = [Win32CompositionMode.RedirectionSurface] });
        }
        else if (string.Equals(composition, "direct", StringComparison.OrdinalIgnoreCase))
        {
            builder = builder.With(new Win32PlatformOptions { CompositionMode = [Win32CompositionMode.DirectComposition] });
        }
        else if (!string.Equals(composition, "default", StringComparison.OrdinalIgnoreCase))
        {
            // spec 5.7 / spike: WinUIComposition first (DWM-composited alpha), then graceful fallbacks.
            builder = builder.With(new Win32PlatformOptions
            {
                CompositionMode =
                [
                    Win32CompositionMode.WinUIComposition,
                    Win32CompositionMode.DirectComposition,
                    Win32CompositionMode.RedirectionSurface,
                ],
            });
        }

        return builder.LogToTrace(string.IsNullOrWhiteSpace(traceLog)
            ? Avalonia.Logging.LogEventLevel.Warning
            : Avalonia.Logging.LogEventLevel.Verbose);
    }
}

/// <summary>Application root: composes services and starts the island host.</summary>
public sealed class FluidBarApplication : Application
{
    /// <inheritdoc />
    public override void Initialize()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("FLUIDBAR_NO_FLUENT"), "1", StringComparison.Ordinal))
        {
            Styles.Add(new FluentTheme());
        }
    }

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
            if (string.Equals(Environment.GetEnvironmentVariable("FLUIDBAR_SPIKE_WINDOW"), "1", StringComparison.Ordinal))
            {
                var probe = new Window
                {
                    WindowDecorations = WindowDecorations.None,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Topmost = true,
                    CanResize = false,
                    Width = 488,
                    Height = 332,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.Transparent),
                    TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
                    Content = new ProbeVisual(),
                    Position = new PixelPoint(100, 100),
                };
                desktop.MainWindow = probe;
                probe.Show();
                if (!string.Equals(Environment.GetEnvironmentVariable("FLUIDBAR_PROBE_NO_BASE"),
                        "1", StringComparison.Ordinal))
                {
                    base.OnFrameworkInitializationCompleted();
                }

                var diagPath = Environment.GetEnvironmentVariable("FLUIDBAR_PROBE_DIAG");
                if (!string.IsNullOrWhiteSpace(diagPath))
                {
                    var diagTimer = new Avalonia.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(1500),
                    };
                    diagTimer.Tick += (_, _) =>
                    {
                        diagTimer.Stop();
                        try
                        {
                            var hwnd = probe.TryGetPlatformHandle()?.Handle ?? 0;
                            File.WriteAllText(diagPath,
                                $"actual={probe.ActualTransparencyLevel} " +
                                $"hint=[{string.Join(",", probe.TransparencyLevelHint)}] " +
                                $"fallback={probe.TransparencyBackgroundFallback} " +
                                $"bg={probe.Background} scale={probe.RenderScaling} " +
                                $"hwnd=0x{hwnd.ToInt64():X} client={probe.ClientSize} " +
                                $"bounds={probe.Bounds} decorations={probe.WindowDecorations} " +
                                $"topmost={probe.Topmost} visible={probe.IsVisible}");
                        }
                        catch (Exception ex)
                        {
                            File.WriteAllText(diagPath, "diag-failed: " + ex);
                        }

                        probe.Close();
                    };
                    diagTimer.Start();
                }

                return;
            }

            var bootstrap = new AppBootstrap(desktop);
            _ = bootstrap.StartAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

/// <summary>Minimal squircle probe used to A/B transparency against the shipping host.</summary>
internal sealed class ProbeVisual : Control
{
    public override void Render(Avalonia.Media.DrawingContext context)
    {
        var fill = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#08080A"));
        context.DrawRectangle(fill, null, new Rect(24, 24, 132, 36), 18, 18);
    }
}
/// <summary>Diagnostic A/B app replicating the standalone spike exactly (see docs/rewrite/contracts/host-transparency.md).</summary>
internal sealed class SpikeProbeApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new SpikeProbeWindow();
            desktop.MainWindow = window;
            window.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class SpikeProbeWindow : Window
{
    public SpikeProbeWindow()
    {
        WindowDecorations = WindowDecorations.None;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        CanResize = false;
        Width = 488;
        Height = 332;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = new SolidColorBrush(Colors.Transparent);
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Content = new SpikeProbeVisual();
    }
}

internal sealed class SpikeProbeVisual : Control
{
    public override void Render(Avalonia.Media.DrawingContext context)
    {
        base.Render(context);
        var fill = new SolidColorBrush(Avalonia.Media.Color.Parse("#08080A"));
        context.DrawRectangle(fill, null, new Rect(24, 24, 132, 36), 18, 18);
    }
}