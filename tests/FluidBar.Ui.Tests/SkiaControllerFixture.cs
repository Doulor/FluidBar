using System.Globalization;
using FluidBar.App.Hosting.Skia;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.State;
using FluidBar.Core.Diagnostics;
using FluidBar.Island;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Headless controller fixture shared by the Skia mapping and page-control tests. No Avalonia, no
/// HWND and no UI thread: the controller is driven by a fake clock and a fake content source.
/// </summary>
internal sealed class ControllerFixture : IDisposable
{
    private ControllerFixture(
        SkiaIslandController controller,
        SettingsController settings,
        DesignIslandDataSource data,
        FakeTimeProvider time,
        RecordingAppLogger logger)
    {
        Controller = controller;
        Settings = settings;
        Data = data;
        Time = time;
        Logger = logger;
    }

    public SkiaIslandController Controller { get; }

    public SettingsController Settings { get; }

    public DesignIslandDataSource Data { get; }

    public FakeTimeProvider Time { get; }

    public RecordingAppLogger Logger { get; }

    public static ControllerFixture Create(
        AppSettings? settings = null,
        IIslandPlatformActions? actions = null,
        RecordingAppLogger? logger = null)
    {
        var time = new FakeTimeProvider();
        var localizer = new FakeLocalizer();
        var store = new InMemoryUiSettingsStore(settings ?? (AppSettings.Default with { AutoCollapseMs = 0, AutoHideMs = 0 }));
        var settingsController = new SettingsController(store.LastSaved, store, NullAppLogger.Instance);
        var data = new DesignIslandDataSource(localizer, time, static action => action());
        var recording = logger ?? new RecordingAppLogger();
        var controller = new SkiaIslandController(new SkiaIslandHostOptions
        {
            Localizer = localizer,
            Settings = settingsController,
            Data = data,
            Logger = recording,
            TimeProvider = time,
            PlatformActions = actions,
            TargetFrameRate = 120,
        });
        controller.Start();
        return new ControllerFixture(controller, settingsController, data, time, recording);
    }

    /// <summary>Snaps the springs to their targets and rebuilds the render state.</summary>
    public void Settle()
    {
        Controller.Motion.SnapToTarget();
        Controller.Advance(TimeSpan.FromMilliseconds(16));
    }

    /// <summary>Expands the island through a primary click and settles the shell.</summary>
    public void ExpandAndSettle()
    {
        var shell = IslandMetrics.ShellRect(Controller.RenderState);
        Controller.HandlePointerPressed(shell.MidX, shell.MidY);
        Settle();
    }

    /// <summary>Navigates the pager to a page and lets the snap/state follow.</summary>
    public void GoToPage(int page)
    {
        Controller.Pager.SelectPage(page);
        RunToRest();
    }

    /// <summary>Steps frames until every spring/pager/strip animation is at rest.</summary>
    public void RunToRest(int maxFrames = 900)
    {
        for (var i = 0; i < maxFrames && Controller.IsAnimating; i++)
        {
            Controller.Advance(TimeSpan.FromMilliseconds(16));
        }
    }

    public void Dispose()
    {
        Controller.Dispose();
        Data.Dispose();
        Settings.Dispose();
    }
}

/// <summary>Records every semantic action the controller dispatches.</summary>
internal sealed class RecordingIslandPlatformActions : IIslandPlatformActions
{
    private readonly List<IslandPlatformAction> _calls = [];

    public IReadOnlyList<IslandPlatformAction> Calls => _calls;

    /// <summary>Optional result override (defaults to handled).</summary>
    public Func<IslandPlatformAction, IslandActionResult>? ResultFactory { get; set; }

    public IslandActionResult Invoke(in IslandPlatformAction action)
    {
        _calls.Add(action);
        return ResultFactory?.Invoke(action) ?? IslandActionResult.Handled;
    }
}

/// <summary>Minimal localizer for headless mapping tests (no Avalonia asset loader).</summary>
internal sealed class FakeLocalizer : ILocalizer
{
    private readonly Dictionary<string, string> _messages = new(StringComparer.Ordinal)
    {
        ["Page.Home.Title"] = "Control center",
        ["Page.Media.Title"] = "Now playing",
        ["Page.Notifications.Title"] = "Notifications",
        ["Page.Clipboard.Title"] = "Clipboard",
        ["Page.Agent.Title"] = "Agent activity",
        ["Page.Widgets.Title"] = "Widgets",
        ["Page.Widgets.Tasks"] = "Today",
        ["Page.Widgets.Today"] = "Today",
        ["Page.Home.FocusTime"] = "Focus time",
        ["Page.Home.System"] = "System",
        ["Page.Widgets.Task.Review"] = "Review",
        ["Page.Widgets.Task.Ship"] = "Ship",
        ["Page.Widgets.Task.Inbox"] = "Inbox",
        ["Common.Next"] = "Next",
        ["Common.Charging"] = "Charging",
        ["Common.Copied"] = "Copied",
        ["Media.NowPlaying"] = "Now playing",
        ["Media.NoSession"] = "Nothing playing",
        ["Media.UnknownTitle"] = "Unknown title",
        ["Media.UnknownArtist"] = "Unknown artist",
        ["Clipboard.Image"] = "Image",
        ["Agent.Running"] = "Running",
        ["Agent.Completed"] = "Completed",
        ["Agent.Title"] = "Agent",
        ["Battery.Low"] = "Low battery",
        ["Timer.Pomodoro"] = "Pomodoro",
        ["Activity.Idle"] = "Idle",
    };

    private string _culture = "en-US";

    public string Culture => _culture;

    public int Version { get; private set; }

    public event EventHandler? Changed;

    public IReadOnlyList<string> AvailableCultures => ["en-US", "zh-Hans"];

    public bool IsRightToLeft => false;

    public CultureInfo CurrentCulture => CultureInfo.GetCultureInfo(_culture);

    public string GetCultureDisplayName(string cultureTag) => cultureTag;

    public string Get(string key) => _messages.TryGetValue(key, out var value) ? value : key;

    public string Format(string key, IReadOnlyDictionary<string, object?> arguments) => Get(key);

    public string Format(string key, params (string Name, object? Value)[] arguments) => Get(key);

    public bool TryGet(string key, out string value) => _messages.TryGetValue(key, out value!);

    /// <summary>Switches the pseudo culture so title changes are observable (tests).</summary>
    public void SetCulture(string culture)
    {
        if (string.Equals(_culture, culture, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _culture = culture;
        // Non-English titles exercise the relocalization path without a real catalog.
        _messages["Page.Home.Title"] = culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "控制中心" : "Control center";
        Version++;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
