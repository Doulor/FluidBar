using System.Text.Json;
using FluidBar.PluginHost.Loader;
using FluidBar.PluginHost.Runtime;
using FluidBar.Sdk;
using FluidBar.Sdk.Contributions;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Versioning;

namespace FluidBar.PluginHost.Tests;

/// <summary>
/// Store-level regression tests for the one-file-per-plugin settings layout.
/// <para>
/// Every flush writes the same <c>settings.json.tmp</c> path, and the host bridge, a plugin command and the
/// unload path can all flush the same store concurrently, so the temp file must have a single writer at a
/// time or one flush fails (and the newest values are lost). Stored values must also own their JSON: callers
/// hand in elements borrowed from a <see cref="JsonDocument"/> they may dispose immediately afterwards.
/// </para>
/// </summary>
public sealed class PluginSettingsStoreTests
{
    private const string PluginId = "builtin-test";
    private const string GlowKey = "media.lyricGlowStrength";

    /// <summary>Concurrent flushes must not collide on the temp path and must persist every value.</summary>
    [Fact]
    public async Task Concurrent_flushes_serialize_and_persist_every_value()
    {
        using var workspace = TempPluginRoot.Create();
        var (store, path) = CreateStore(workspace);
        using (store)
        {
            for (var index = 0; index < 600; index++)
            {
                store.SetValue($"bulk.key{index}", JsonSerializer.SerializeToElement(new string('x', 48)));
            }

            var failures = new List<Exception>();
            for (var round = 0; round < 3; round++)
            {
                using var start = new ManualResetEventSlim(false);
                var saves = Enumerable.Range(0, 16)
                    .Select(_ => Task.Factory.StartNew(
                        async () =>
                        {
                            start.Wait();
                            try
                            {
                                await store.SaveAsync().ConfigureAwait(false);
                            }
                            catch (Exception exception)
                            {
                                lock (failures)
                                {
                                    failures.Add(exception);
                                }
                            }
                        },
                        CancellationToken.None,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default).Unwrap())
                    .ToArray();
                start.Set();
                await Task.WhenAll(saves);
            }

            Assert.Empty(failures.Select(static exception => exception.GetType().Name + ": " + exception.Message));
            Assert.False(File.Exists(path + ".tmp"), "a flush left its temp file behind");
            var persisted = ReadValues(path);
            Assert.Equal(600, persisted.Count);
            Assert.Equal(new string('x', 48), persisted["bulk.key599"].GetString());
        }
    }

    /// <summary>The session seam (what the App bridge calls) must survive the same overlap.</summary>
    [Fact]
    public async Task Session_flush_is_safe_while_values_change()
    {
        using var workspace = TempPluginRoot.Create();
        await using var session = CreateSession(workspace);
        Assert.True((await session.StartAsync()).Success);
        for (var index = 0; index < 400; index++)
        {
            Assert.True(session.SetSettingValue($"bulk.key{index}", JsonSerializer.SerializeToElement(new string('y', 48))));
        }

        var settingsPath = Path.Combine(workspace.Root, PluginId, "settings.json");
        var failures = new List<Exception>();
        using var start = new ManualResetEventSlim(false);
        var flushers = Enumerable.Range(0, 12)
            .Select(_ => Task.Factory.StartNew(
                async () =>
                {
                    start.Wait();
                    try
                    {
                        for (var round = 0; round < 4; round++)
                        {
                            await session.SaveSettingsAsync().ConfigureAwait(false);
                        }
                    }
                    catch (Exception exception)
                    {
                        lock (failures)
                        {
                            failures.Add(exception);
                        }
                    }
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap())
            .ToArray();
        var writerLoop = Task.Factory.StartNew(
            async () =>
            {
                start.Wait();
                for (var percent = 0; percent < 400; percent++)
                {
                    session.SetSettingValue(GlowKey, JsonSerializer.SerializeToElement(percent % 100));
                    if (percent % 20 == 0)
                    {
                        await Task.Yield();
                    }
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
        start.Set();
        await Task.WhenAll(flushers.Append(writerLoop));
        await session.SaveSettingsAsync();

        Assert.Empty(failures.Select(static exception => exception.GetType().Name + ": " + exception.Message));
        Assert.False(File.Exists(settingsPath + ".tmp"), "a flush left its temp file behind");
        var persisted = ReadValues(settingsPath);
        Assert.Equal(401, persisted.Count);
        Assert.Equal(99, persisted[GlowKey].GetInt32());
    }

    /// <summary>A stored value must not borrow the caller's document: the writer disposes it right away.</summary>
    [Fact]
    public async Task Stored_value_owns_its_json_when_the_caller_document_is_disposed()
    {
        using var workspace = TempPluginRoot.Create();
        var (store, path) = CreateStore(workspace);
        using (store)
        {
            using (var document = JsonDocument.Parse("{\"setting\":35}"))
            {
                store.SetValue(GlowKey, document.RootElement.GetProperty("setting"));
            }

            Assert.True(store.TryGetDouble(GlowKey, out var cached));
            Assert.Equal(35, cached);
            await store.SaveAsync();
            Assert.Equal(35, ReadValues(path)[GlowKey].GetDouble());
        }
    }

    /// <summary>The session path used by the settings page hands in an element the caller may dispose.</summary>
    [Fact]
    public async Task Session_setting_survives_the_caller_document_being_disposed()
    {
        using var workspace = TempPluginRoot.Create();
        await using var session = CreateSession(workspace);
        Assert.True((await session.StartAsync()).Success);
        using (var document = JsonDocument.Parse("35"))
        {
            Assert.True(session.SetSettingValue(GlowKey, document.RootElement));
        }

        Assert.Equal(35, session.GetSettingValue(GlowKey)!.Value.GetDouble());
        await session.SaveSettingsAsync();
        var settingsPath = Path.Combine(workspace.Root, PluginId, "settings.json");
        Assert.Equal(35, ReadValues(settingsPath)[GlowKey].GetDouble());
        Assert.False(File.Exists(settingsPath + ".tmp"));
    }

    /// <summary>A disposed store must refuse the flush before it touches the file system.</summary>
    [Fact]
    public async Task Disposed_store_never_writes()
    {
        using var workspace = TempPluginRoot.Create();
        var (store, path) = CreateStore(workspace);
        store.SetValue(GlowKey, JsonSerializer.SerializeToElement(35));
        store.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.SaveAsync().AsTask());
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    /// <summary>A cancelled flush must leave no temp file and must not keep the store's write gate.</summary>
    [Fact]
    public async Task Cancelled_flush_leaves_no_temp_and_keeps_the_store_usable()
    {
        using var workspace = TempPluginRoot.Create();
        var (store, path) = CreateStore(workspace);
        using (store)
        {
            store.SetValue(GlowKey, JsonSerializer.SerializeToElement(35));
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => store.SaveAsync(cancellation.Token).AsTask());
            }

            Assert.False(File.Exists(path));
            Assert.False(File.Exists(path + ".tmp"), "a cancelled flush left its temp file behind");

            await store.SaveAsync();
            Assert.Equal(35, ReadValues(path)[GlowKey].GetDouble());
        }
    }

    private static (PluginSettingsStore Store, string Path) CreateStore(TempPluginRoot workspace)
    {
        var path = Path.Combine(workspace.Root, PluginId, "settings.json");
        return (new PluginSettingsStore(PluginId, path, new RecordingHostLogger()), path);
    }

    private static Dictionary<string, JsonElement> ReadValues(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.GetProperty("values").EnumerateObject())
        {
            values[property.Name] = property.Value.Clone();
        }

        return values;
    }

    private static InProcessPluginSession CreateSession(TempPluginRoot workspace)
    {
        var options = new PluginHostOptions
        {
            PluginRootDirectory = workspace.Root,
            AppVersion = new SemanticVersion(2, 0, 0),
            Logger = new RecordingHostLogger(),
            InitializeTimeout = TimeSpan.FromSeconds(5),
            StartTimeout = TimeSpan.FromSeconds(5),
            StopTimeout = TimeSpan.FromSeconds(5),
            DisposeTimeout = TimeSpan.FromSeconds(5),
        };
        var manifest = new PluginManifest
        {
            SchemaVersion = 2,
            Id = PluginId,
            Version = "2.0.0",
            ApiVersion = "2.0.0",
            MinAppVersion = "2.0.0",
            Publisher = new PublisherInfo { Id = "fluidbar", DisplayName = "FluidBar" },
            Name = LocalizedString.From("Store fixture"),
            Summary = LocalizedString.From("Settings-store fixture."),
            License = "MIT",
            Contributes = new PluginContributions
            {
                Commands =
                [
                    new CommandContribution { Id = "builtin-test.ping", Title = LocalizedString.From("Ping") },
                ],
            },
        };

        return new InProcessPluginSession(options, manifest, workspace.Root, new StoreFixturePlugin());
    }

    private sealed class StoreFixturePlugin : IFluidBarPlugin
    {
        private IDisposable? _registration;
        private IPluginContext? _context;

        public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
        {
            _context = context;
            return ValueTask.CompletedTask;
        }

        public ValueTask StartAsync(CancellationToken cancellationToken)
        {
            _registration = _context!.Commands.Register("builtin-test.ping", (_, _) => ValueTask.CompletedTask);
            return ValueTask.CompletedTask;
        }

        public ValueTask StopAsync(CancellationToken cancellationToken)
        {
            _registration?.Dispose();
            _registration = null;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
