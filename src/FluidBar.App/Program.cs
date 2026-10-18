using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace FluidBar.App;

/// <summary>
/// Temporary M0 bootstrap for the Avalonia shell. The real island window host
/// (Win32 + Windows.UI.Composition, fixed 488x332 host) is implemented in M4.
/// </summary>
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<FluidBarApplication>()
            .UsePlatformDetect()
            .LogToTrace();
}

/// <summary>Temporary application root used to validate the Avalonia 12 toolchain on .NET 10.</summary>
public sealed class FluidBarApplication : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new Window
            {
                Title = "FluidBar v2 (M0 bootstrap)",
                Width = 360,
                Height = 200,
                Content = new TextBlock
                {
                    Text = "FluidBar v2 bootstrap — island shell lands in M4.",
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                },
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
