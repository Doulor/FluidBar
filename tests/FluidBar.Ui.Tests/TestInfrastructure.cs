using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Themes.Fluent;
using Avalonia.Headless;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.State;
using FluidBar.Core.Diagnostics;
using Microsoft.VisualStudio.TestPlatform.TestHost;

[assembly: AvaloniaTestApplication(typeof(FluidBar.Ui.Tests.TestAppBuilder))]
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace FluidBar.Ui.Tests;

/// <summary>Minimal Avalonia application used by the headless test session (no production bootstrap).</summary>
public sealed class TestApp : Application
{
    /// <inheritdoc />
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }
}

/// <summary>App builder for the headless xunit session.</summary>
public static class TestAppBuilder
{
    /// <summary>Builds the headless Avalonia app.</summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<TestApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}

/// <summary>Shared headless session + helpers for deterministic UI tests.</summary>
public static class AvaloniaTest
{
    private static readonly Lazy<HeadlessUnitTestSession> LazySession =
        new(() => HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder)), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Runs <paramref name="action"/> on the headless UI thread.</summary>
    public static Task Run(Action action) => LazySession.Value.Dispatch(action, CancellationToken.None);

    /// <summary>Runs <paramref name="func"/> on the headless UI thread.</summary>
    public static Task<T> Run<T>(Func<T> func) => LazySession.Value.Dispatch(func, CancellationToken.None);

    /// <summary>Runs an async UI-thread action.</summary>
    public static Task RunAsync(Func<Task> action) => LazySession.Value.Dispatch(action, CancellationToken.None);

    /// <summary>Runs an async UI-thread function returning a value.</summary>
    public static Task<T> RunAsync<T>(Func<Task<T>> func) => LazySession.Value.Dispatch(func, CancellationToken.None);
}

/// <summary>Deterministic fake clock for the host's deadline/TTL inputs.</summary>
public sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now;
    private long _timestamp;

    /// <summary>Creates a fake clock at <paramref name="start"/>.</summary>
    public FakeTimeProvider(DateTimeOffset? start = null) => _now = start ?? new DateTimeOffset(2026, 10, 18, 9, 41, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;

    /// <inheritdoc />
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    /// <summary>
    /// Monotonic timestamp on the same fake clock (per-frame animation code reads this instead of
    /// <see cref="GetUtcNow"/>, so it has to advance with <see cref="Advance"/> too).
    /// </summary>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <inheritdoc />
    public override long GetTimestamp() => _timestamp;

    /// <summary>Advances the fake clock.</summary>
    public void Advance(TimeSpan delta)
    {
        _now += delta;
        _timestamp += delta.Ticks;
    }
}

/// <summary>Shared catalog/service fixtures.</summary>
public static class TestFixtures
{
    private static LocalizationService? _localizer;

    /// <summary>Shared localization service with real embedded catalogs + pseudo locale.</summary>
    public static LocalizationService Localizer => _localizer ?? throw new InvalidOperationException("Call EnsureLocalizer inside a headless session first.");

    /// <summary>Creates the shared localizer (must run on the headless UI thread for AssetLoader).</summary>
    public static LocalizationService CreateLocalizer(string? culture = "en-US")
    {
        if (_localizer is null)
        {
            _localizer = LocalizationService.CreateDefault(culture, showMissingKeys: false, enablePseudoLocale: true);
            LocalizationRuntime.Current = _localizer;
        }
        else
        {
            _localizer.SetCulture(culture ?? "en-US");
        }

        return _localizer;
    }

    /// <summary>Creates an in-memory settings controller.</summary>
    public static (SettingsController Controller, InMemoryUiSettingsStore Store) CreateSettings(AppSettings? initial = null)
    {
        var store = new InMemoryUiSettingsStore(initial);
        var controller = new SettingsController(store.LastSaved, store, NullAppLogger.Instance);
        return (controller, store);
    }

    /// <summary>Creates a design data source with a fake clock.</summary>
    public static DesignIslandDataSource CreateData(ILocalizer localizer, FakeTimeProvider time)
        => new(localizer, time);
}
