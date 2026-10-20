using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace FluidBar.App;

/// <summary>
/// Process entry point for the FluidBar Avalonia shell (spec 5.6/5.7).
/// </summary>
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

        // Skia host mode rarely shows Avalonia UI (only the settings/market window). Rendering that
        // window on the CPU keeps the GPU stack (ANGLE/D3D/NVIDIA user-mode driver) out of the
        // process for the whole session; measured with SkiaHostPerfProbe in task-37. Applied last so
        // it wins over the composition-mode presets above (a later Win32PlatformOptions wins).
        if (AvaloniaRenderingPolicy.ShouldUseSoftwareRendering())
        {
            builder = builder.With(AvaloniaRenderingPolicy.CreatePlatformOptions());
        }

        return builder.LogToTrace();
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
                return;
            }

            var bootstrap = new AppBootstrap(desktop);
            _ = bootstrap.StartAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

/// <summary>Minimal probe visual for host-transparency diagnostics (no background fill).</summary>
internal sealed class ProbeVisual : Control
{
    public override void Render(Avalonia.Media.DrawingContext context)
    {
        base.Render(context);
        var fill = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#08080A"));
        context.DrawRectangle(fill, null, new Rect(24, 24, 132, 36), 18, 18);
    }
}
