using System.Text.Json;
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
using FluidBar.Marketplace.Community;

namespace FluidBar.Acceptance.Evidence;

/// <summary>
/// First-run delivery acceptance harness: settings pages + persistence. Every image is the real
/// production <see cref="SettingsWindow"/>, every persistence value goes through the production
/// <see cref="JsonUiSettingsStore"/>; nothing is fabricated.
/// </summary>
internal static class Program
{
    private static readonly SettingsWindowSection[] Sections =
    [
        SettingsWindowSection.General,
        SettingsWindowSection.Appearance,
        SettingsWindowSection.Island,
        SettingsWindowSection.Plugins,
        SettingsWindowSection.Marketplace,
        SettingsWindowSection.Advanced,
        SettingsWindowSection.About,
    ];

    [STAThread]
    private static int Main(string[] args)
    {
        var output = Arg(args, "--output") ?? Path.Combine(FindRepoRoot(), "build", "reports", "acceptance");
        Directory.CreateDirectory(output);
        var marketplaceRoot = Arg(args, "--marketplace-root");
        if (!string.IsNullOrWhiteSpace(marketplaceRoot))
        {
            Environment.SetEnvironmentVariable(CommunityMarketplaceStore.RootOverrideVariable, Path.GetFullPath(marketplaceRoot));
        }

        var pluginsRoot = Arg(args, "--plugins-root");
        if (!string.IsNullOrWhiteSpace(pluginsRoot))
        {
            Environment.SetEnvironmentVariable(CommunityPluginPaths.PluginsRootOverrideVariable, Path.GetFullPath(pluginsRoot));
        }

        var evidence = new Evidence(output);
        var exitCode = 1;
        var builder = AppBuilder.Configure<EvidenceApplication>().UsePlatformDetect();
        var lifetime = new ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        builder.SetupWithLifetime(lifetime);
        lifetime.Startup += (_, _) => _ = RunAsync(lifetime, output, evidence, code => exitCode = code);
        lifetime.Start(Array.Empty<string>());
        return exitCode;
    }

    private static async Task RunAsync(
        ClassicDesktopStyleApplicationLifetime lifetime,
        string output,
        Evidence evidence,
        Action<int> setExitCode)
    {
        var status = 1;
        try
        {
            // Keep the UI-thread continuation: SettingsWindow must be constructed on the Avalonia thread.
            await PersistenceAsync(output, evidence);
            await CaptureSettingsAsync(output, evidence);
            foreach (var culture in new[] { "zh-Hans", "en-US" })
            {
                evidence.Add("ui.island-media", IslandMediaEvidence.Capture(output, culture));
            }

            status = 0;
        }
        catch (Exception exception)
        {
            evidence.Add("error", new { stage = "acceptance", message = exception.ToString() });
            Console.Error.WriteLine(exception);
        }
        finally
        {
            evidence.Flush();
            setExitCode(status);
            Dispatcher.UIThread.Post(() => lifetime.Shutdown(status));
        }
    }

    /// <summary>Create → edit → flush → dispose → new store/controller → read back (restart proof).</summary>
    private static async Task PersistenceAsync(string output, Evidence evidence)
    {
        var stateDir = Path.Combine(output, "state");
        Directory.CreateDirectory(stateDir);
        var path = Path.Combine(stateDir, "settings.ui.json");
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        AppSettings firstLoad;
        using (var store = new JsonUiSettingsStore(path, NullAppLogger.Instance))
        {
            firstLoad = await store.LoadAsync().ConfigureAwait(false);
        }

        evidence.Add("persistence.fresh-load", new
        {
            path,
            fileExisted = false,
            language = firstLoad.Language,
            theme = firstLoad.Theme.ToString(),
            fontScale = firstLoad.FontScale,
            autoCollapseMs = firstLoad.AutoCollapseMs,
            weatherCity = firstLoad.WeatherCity,
            note = "missing file must yield production defaults (first-run user)",
        });

        AppSettings edited;
        using (var store = new JsonUiSettingsStore(path, NullAppLogger.Instance))
        {
            edited = await store.LoadAsync().ConfigureAwait(false);
            using var controller = new SettingsController(edited, store, NullAppLogger.Instance);
            controller.Update(settings => settings with
            {
                Language = "en-US",
                Theme = AppTheme.Light,
                FontScale = 1.6,
                AutoCollapseMs = 0,
                AutoHideMs = 0,
                TrayIcon = false,
                WeatherCity = "beijing",
                ReducedMotion = ReducedMotionPreference.Off,
                AccentMode = "custom",
                AccentFallback = "#22CC88",
                PanelRadius = 30,
            });
            await store.FlushAsync().ConfigureAwait(false);
            await Task.Delay(200).ConfigureAwait(false);
        }

        var raw = File.Exists(path) ? await File.ReadAllTextAsync(path).ConfigureAwait(false) : string.Empty;
        var reloaded = AppSettings.Default;
        using (var store = new JsonUiSettingsStore(path, NullAppLogger.Instance))
        {
            reloaded = await store.LoadAsync().ConfigureAwait(false);
        }

        var checks = new
        {
            language = reloaded.Language,
            theme = reloaded.Theme.ToString(),
            fontScale = reloaded.FontScale,
            autoCollapseMs = reloaded.AutoCollapseMs,
            trayIcon = reloaded.TrayIcon,
            weatherCity = reloaded.WeatherCity,
            reducedMotion = reloaded.ReducedMotion.ToString(),
            accentMode = reloaded.AccentMode,
            accentFallback = reloaded.AccentFallback,
        };
        var conditions = new Dictionary<string, bool>
        {
            ["language"] = checks.language == "en-US",
            ["theme"] = checks.theme == nameof(AppTheme.Light),
            ["fontScale"] = Math.Abs(checks.fontScale - 1.6) < 0.001,
            ["autoCollapseMs"] = checks.autoCollapseMs == 0,
            ["trayIcon"] = !checks.trayIcon,
            ["weatherCity"] = checks.weatherCity == "beijing",
            ["reducedMotion"] = checks.reducedMotion == nameof(ReducedMotionPreference.Off),
            ["accentMode"] = checks.accentMode == "custom",
            // Normalized production form is #AARRGGBB ("#ff22cc88" for an opaque #22CC88).
            ["accentFallback"] = string.Equals(checks.accentFallback, "#ff22cc88", StringComparison.OrdinalIgnoreCase),
        };
        var survived = conditions.Values.All(static value => value);

        evidence.Add("persistence.restart-read-back", new
        {
            persistedFile = path,
            rawBytes = raw.Length,
            checks,
            conditions,
            survivedRestart = survived,
        });

        if (!survived)
        {
            throw new InvalidOperationException("Settings did not survive a fresh store instance: " + JsonSerializer.Serialize(checks));
        }
    }

    private static async Task CaptureSettingsAsync(string output, Evidence evidence)
    {
        var localizer = LocalizationService.CreateDefault("zh-Hans", showMissingKeys: false, enablePseudoLocale: false);
        LocalizationRuntime.Current = localizer;
        // First-run defaults, only pinning the UI language to the user's (zh-Hans) so the pages are
        // captured exactly as a Chinese first-run user sees them.
        var settingsStore = new InMemoryUiSettingsStore(AppSettings.Default with { Language = "zh-Hans" });
        var controller = new SettingsController(settingsStore.LastSaved, settingsStore, NullAppLogger.Instance);
        var screenshots = Path.Combine(output, "screenshots");
        Directory.CreateDirectory(screenshots);
        var captured = new List<object>();
        var index = 1;
        foreach (var section in Sections)
        {
            var window = new SettingsWindow(controller, localizer, section);
            window.Show();
            try
            {
                await Task.Delay(1800).ConfigureAwait(true);
                Dispatcher.UIThread.RunJobs();
                var content = window.Content as Control
                    ?? throw new InvalidOperationException("settings window has no content");
                var name = $"{index:00}-settings-{section.ToString().ToLowerInvariant()}.png";
                captured.Add(Capture(content, Path.Combine(screenshots, name)));
                if (section == SettingsWindowSection.General
                    && Environment.GetEnvironmentVariable("FLUIDBAR_TOKEN_SAMPLE") == "1")
                {
                    // Task-36 evidence: 2x render so the tertiary hint glyphs have solid token pixels.
                    captured.Add(Capture(content, Path.Combine(screenshots, "01-settings-general@2x.png"), 192));
                }
                captured.Add(new
                {
                    section = section.ToString(),
                    title = window.Title,
                    width = content.Bounds.Width,
                    height = content.Bounds.Height,
                    buttons = Descendants(content).Count(c => c is Button),
                    textBlocks = Descendants(content).Count(c => c is TextBlock),
                });

                var scroll = Descendants(content).OfType<ScrollViewer>()
                    .OrderByDescending(s => s.Bounds.Height)
                    .FirstOrDefault();
                if (scroll is not null && scroll.Extent.Height > scroll.Viewport.Height + 1)
                {
                    scroll.Offset = new Vector(0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height));
                    await Task.Delay(600).ConfigureAwait(true);
                    Dispatcher.UIThread.RunJobs();
                    captured.Add(Capture(content, Path.Combine(screenshots,
                        $"{index:00}-settings-{section.ToString().ToLowerInvariant()}-bottom.png")));
                    captured.Add(new
                    {
                        section = section.ToString(),
                        scrollExtent = scroll.Extent.Height,
                        scrollViewport = scroll.Viewport.Height,
                    });
                }
            }
            finally
            {
                window.Close();
                await Task.Delay(250).ConfigureAwait(true);
                Dispatcher.UIThread.RunJobs();
            }

            index++;
        }

        evidence.Add("ui.settings-pages", captured);
    }

    private static IEnumerable<Control> Descendants(Control root)
        => root.GetVisualDescendants().OfType<Control>();

    private static object Capture(Control content, string path, double dpi = 96)
    {
        var scale = dpi / 96.0;
        var width = (int)Math.Ceiling(content.Bounds.Width * scale);
        var height = (int)Math.Ceiling(content.Bounds.Height * scale);
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException($"control has no size ({width}x{height})");
        }

        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(dpi, dpi));
        bitmap.Render(content);
        bitmap.Save(path);
        return new { path, width, height, bytes = new FileInfo(path).Length };
    }

    private static string? Arg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FluidBar.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    internal sealed class EvidenceApplication : Application
    {
        public override void Initialize() => Styles.Add(new FluentTheme());
    }

    internal sealed class Evidence
    {
        private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
        private readonly List<object> _steps = [];
        private readonly string _path;

        internal Evidence(string directory) => _path = Path.Combine(directory, "settings-evidence.json");

        internal void Add(string step, object data) => _steps.Add(new { step, at = DateTimeOffset.Now, data });

        internal void Flush() =>
            File.WriteAllText(_path, JsonSerializer.Serialize(new { generatedAt = DateTimeOffset.Now, steps = _steps }, Options));
    }
}
