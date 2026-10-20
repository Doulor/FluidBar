using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.Views.Settings;
using FluidBar.Core.Diagnostics;

namespace FluidBar.SettingsEvidence;

/// <summary>
/// Developer/QA evidence harness: renders the real settings window (island section) so the weather-city
/// row can be reviewed in both UI cultures. It never fabricates data: the window is the production
/// <see cref="SettingsWindow"/> over a real <see cref="SettingsController"/>.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var output = args.Length > 0
            ? Path.GetFullPath(args[0])
            : Path.Combine(Path.GetTempPath(), "fluidbar-evidence");
        Directory.CreateDirectory(output);

        var exitCode = 1;
        var builder = AppBuilder.Configure<EvidenceApplication>().UsePlatformDetect();
        var lifetime = new ClassicDesktopStyleApplicationLifetime
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };
        builder.SetupWithLifetime(lifetime);
        lifetime.Startup += (_, _) => _ = CaptureAsync(output, lifetime, code => exitCode = code);
        lifetime.Start(Array.Empty<string>());
        return exitCode;
    }

    private static async Task CaptureAsync(
        string output,
        ClassicDesktopStyleApplicationLifetime lifetime,
        Action<int> setExitCode)
    {
        var status = 1;
        try
        {
            foreach (var culture in new[] { "zh-Hans", "en-US" })
            {
                await CaptureCultureAsync(output, culture);
                await CaptureAgentsAsync(output, culture);
            }

            await CaptureSectionsAsync(output);

            status = 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
        }
        finally
        {
            setExitCode(status);
            Dispatcher.UIThread.Post(() => lifetime.Shutdown(status));
        }
    }

    private static async Task CaptureCultureAsync(string output, string culture)
    {
        var localizer = LocalizationService.CreateDefault(culture, showMissingKeys: false, enablePseudoLocale: false);
        LocalizationRuntime.Current = localizer;
        var store = new InMemoryUiSettingsStore(AppSettings.Default);
        var controller = new SettingsController(store.LastSaved, store, NullAppLogger.Instance);
        var window = new SettingsWindow(controller, localizer, SettingsWindowSection.Island)
        {
            Width = 1400,
            Height = 1000,
        };
        window.Show();
        try
        {
            await Task.Delay(1400);
            Dispatcher.UIThread.RunJobs();

            var content = window.Content as Control
                ?? throw new InvalidOperationException("The settings window has no content.");
            Save(content, Path.Combine(output, "settings-island-" + culture + "-top.png"));

            var label = localizer.Get(LocKeys.Settings.Island.WeatherCity);
            var text = window.GetVisualDescendants().OfType<TextBlock>()
                .FirstOrDefault(candidate => candidate.Text == label)
                ?? throw new InvalidOperationException("The weather-city row is missing from the island section.");
            text.BringIntoView();
            await Task.Delay(700);
            Dispatcher.UIThread.RunJobs();
            Save(content, Path.Combine(output, "settings-island-" + culture + "-city.png"));
            Console.WriteLine(culture + ": city row bounds=" + text.Bounds);
        }
        finally
        {
            window.Close();
            await Task.Delay(300);
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Renders the read-only agent support page in one culture.</summary>
    private static async Task CaptureAgentsAsync(string output, string culture)
    {
        var localizer = LocalizationService.CreateDefault(culture, showMissingKeys: false, enablePseudoLocale: false);
        LocalizationRuntime.Current = localizer;
        var store = new InMemoryUiSettingsStore(AppSettings.Default);
        var controller = new SettingsController(store.LastSaved, store, NullAppLogger.Instance);
        var window = new SettingsWindow(controller, localizer, SettingsWindowSection.AgentSupport)
        {
            Width = 1400,
            Height = 1750,
        };
        window.Show();
        try
        {
            await Task.Delay(1200);
            Dispatcher.UIThread.RunJobs();

            var content = window.Content as Control
                ?? throw new InvalidOperationException("The settings window has no content.");
            Save(content, Path.Combine(output, "settings-support-" + culture + ".png"));
        }
        finally
        {
            window.Close();
            await Task.Delay(300);
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Renders every settings section at the prototype modal size (visual-audit evidence).</summary>
    private static async Task CaptureSectionsAsync(string output)
    {
        var directory = Path.Combine(output, "settings-pages");
        Directory.CreateDirectory(directory);
        foreach (var section in Enum.GetValues<SettingsWindowSection>())
        {
            var localizer = LocalizationService.CreateDefault("zh-Hans", showMissingKeys: false, enablePseudoLocale: false);
            LocalizationRuntime.Current = localizer;
            var store = new InMemoryUiSettingsStore(AppSettings.Default);
            var controller = new SettingsController(store.LastSaved, store, NullAppLogger.Instance);
            var window = new SettingsWindow(controller, localizer, section);
            window.Show();
            try
            {
                await Task.Delay(700);
                Dispatcher.UIThread.RunJobs();
                var content = window.Content as Control
                    ?? throw new InvalidOperationException("The settings window has no content.");
                Save(content, Path.Combine(directory, "settings-" + section + "-zh-Hans.png"));
            }
            finally
            {
                window.Close();
                await Task.Delay(200);
                Dispatcher.UIThread.RunJobs();
            }
        }
    }

    private static void Save(Control content, string path)
    {
        var width = (int)Math.Ceiling(content.Bounds.Width);
        var height = (int)Math.Ceiling(content.Bounds.Height);
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("The settings window has no size (" + width + "x" + height + ").");
        }

        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        bitmap.Render(content);
        bitmap.Save(path);
        Console.WriteLine("saved " + path + " " + width + "x" + height + " bytes=" + new FileInfo(path).Length);
    }
}

internal sealed class EvidenceApplication : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}
