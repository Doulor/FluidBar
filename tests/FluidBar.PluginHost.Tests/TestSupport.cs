using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using FluidBar.PluginHost.Loader;
using FluidBar.PluginHost.Logging;
using FluidBar.Sdk;
using FluidBar.Sdk.Json;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Versioning;

namespace FluidBar.PluginHost.Tests;

internal static class TestPaths
{
    public static string RepoRoot([CallerFilePath] string callerFilePath = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(callerFilePath)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FluidBar.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    public static string FixturePluginOutput() =>
        FindOutput(Path.Combine(RepoRoot(), "tests", "FluidBar.PluginHost.Tests", "Fixtures", "FluidBar.PluginHost.TestPlugin"));

    public static string SampleOutput(string projectName) =>
        FindOutput(Path.Combine(RepoRoot(), "plugins", "samples", projectName));

    private static string FindOutput(string projectDirectory)
    {
        var bin = Path.Combine(projectDirectory, "bin");
        if (!Directory.Exists(bin))
        {
            throw new DirectoryNotFoundException($"Build the project first: '{bin}' does not exist.");
        }

        return Directory.EnumerateDirectories(bin, "net10.0", SearchOption.AllDirectories)
            .OrderByDescending(static directory => directory, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?? throw new DirectoryNotFoundException($"No net10.0 output found under '{bin}'.");
    }
}

internal sealed class TempPluginRoot : IDisposable
{
    private TempPluginRoot(string root) => Root = root;

    public string Root { get; }

    public static TempPluginRoot Create()
    {
        var root = Path.Combine(Path.GetTempPath(), "fluidbar-host-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new TempPluginRoot(root);
    }

    public string Combine(params string[] segments) => Path.Combine([Root, .. segments]);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort test temp cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort test temp cleanup.
        }
    }
}

internal static class FixturePackageFactory
{
    public const string EntryAssemblyName = "FluidBar.PluginHost.TestPlugin";
    public const string EntryAssemblyRelativePath = "plugin/FluidBar.PluginHost.TestPlugin.dll";
    public const string EntryTypePrefix = "FluidBar.PluginHost.TestPlugin.";

    /// <summary>Creates a flat dev/plugin package: &lt;dir&gt;/manifest.json + &lt;dir&gt;/plugin/*.dll + locales.</summary>
    public static string Create(
        string parentDirectory,
        string pluginId = "test-plugin",
        string pluginType = "HappyPlugin",
        string version = "1.0.0",
        Action<JsonObject>? customizeManifest = null,
        bool includeLocales = true)
    {
        var packageDirectory = Path.Combine(parentDirectory, pluginId);
        var pluginBin = Path.Combine(packageDirectory, "plugin");
        Directory.CreateDirectory(pluginBin);

        var output = TestPaths.FixturePluginOutput();
        foreach (var dll in Directory.EnumerateFiles(output, "*.dll", SearchOption.TopDirectoryOnly))
        {
            if (string.Equals(Path.GetFileNameWithoutExtension(dll), "FluidBar.Sdk", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            File.Copy(dll, Path.Combine(pluginBin, Path.GetFileName(dll)), overwrite: true);
        }

        foreach (var deps in Directory.EnumerateFiles(output, "*.deps.json", SearchOption.TopDirectoryOnly))
        {
            File.Copy(deps, Path.Combine(pluginBin, Path.GetFileName(deps)), overwrite: true);
        }

        var manifest = CreateManifest(pluginId, pluginType, version);
        customizeManifest?.Invoke(manifest);
        File.WriteAllText(Path.Combine(packageDirectory, "manifest.json"), manifest.ToJsonString(PluginJson.CreateStrictOptions()));

        if (includeLocales)
        {
            var locales = Path.Combine(packageDirectory, "locales");
            Directory.CreateDirectory(locales);
            File.WriteAllText(
                Path.Combine(locales, "en-US.json"),
                new JsonObject
                {
                    ["$schema"] = "https://fluidbar.app/schemas/locale.schema.json",
                    ["locale"] = "en-US",
                    ["version"] = 1,
                    ["messages"] = new JsonObject
                    {
                        ["fixture.body"] = "fixture body",
                        ["items.count"] = "{count, plural, one {# item} other {# items}}",
                    },
                }.ToJsonString(PluginJson.CreateStrictOptions()));
        }

        return packageDirectory;
    }

    public static JsonObject CreateManifest(string pluginId, string pluginType, string version = "1.0.0")
    {
        return new JsonObject
        {
            ["$schema"] = "https://fluidbar.app/schemas/plugin.schema.json",
            ["schemaVersion"] = 2,
            ["id"] = pluginId,
            ["version"] = version,
            ["apiVersion"] = "2.0.0",
            ["minAppVersion"] = "2.0.0",
            ["publisher"] = new JsonObject
            {
                ["id"] = "test-publisher",
                ["displayName"] = "Test Publisher",
                ["keyId"] = "test-publisher-2026",
            },
            ["name"] = new JsonObject { ["en-US"] = "Fixture " + pluginType },
            ["summary"] = new JsonObject { ["en-US"] = "Fixture plugin." },
            ["license"] = "MIT",
            ["categories"] = new JsonArray("developer"),
            ["entry"] = new JsonObject
            {
                ["runtime"] = "inproc",
                ["assembly"] = EntryAssemblyRelativePath,
                ["type"] = EntryTypePrefix + pluginType,
                ["targetFramework"] = "net10.0",
            },
            ["platforms"] = new JsonArray("win-x64"),
            ["locales"] = new JsonObject
            {
                ["default"] = "en-US",
                ["available"] = new JsonArray("en-US"),
                ["path"] = "locales",
                ["fallback"] = "en-US",
            },
            ["capabilities"] = new JsonObject
            {
                ["ui"] = new JsonObject
                {
                    ["island"] = true,
                    ["settings"] = true,
                    ["commands"] = true,
                    ["xaml"] = false,
                },
            },
            ["activation"] = new JsonArray("startup"),
            ["contributes"] = new JsonObject
            {
                ["layouts"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["id"] = pluginId + ".island",
                        ["surface"] = "island.compact",
                        ["template"] = "text.v1",
                        ["slots"] = new JsonObject
                        {
                            ["title"] = new JsonObject { ["field"] = "title", ["style"] = "title" },
                        },
                        ["constraints"] = new JsonObject
                        {
                            ["minWidth"] = 132,
                            ["maxWidth"] = 300,
                            ["minHeight"] = 36,
                            ["maxHeight"] = 44,
                        },
                    },
                },
                ["commands"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["id"] = pluginId + ".ping",
                        ["title"] = new JsonObject { ["en-US"] = "Ping" },
                        ["contexts"] = new JsonArray("commandPalette"),
                    },
                },
            },
        };
    }
}

internal sealed class RecordingHostLogger : IPluginHostLogger
{
    private readonly List<string> _entries = [];
    private readonly object _gate = new();

    public bool IsEnabled(PluginHostLogLevel level) => true;

    public IReadOnlyList<string> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    public void Log(
        PluginHostLogLevel level,
        string pluginId,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        lock (_gate)
        {
            _entries.Add($"{level}|{pluginId}|{message}");
        }
    }

    public bool Contains(string fragment) =>
        Entries.Any(entry => entry.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}

internal sealed class RecordingPublisher : IIslandPublisher
{
    private readonly List<IslandEvent> _events = [];
    private readonly object _gate = new();

    public IReadOnlyList<IslandEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return [.. _events];
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _events.Count;
            }
        }
    }

    public bool TryPublish(IslandEvent islandEvent)
    {
        lock (_gate)
        {
            _events.Add(islandEvent);
        }

        return true;
    }

    public bool TryRetract(string source) => true;

    public bool HasTitle(string title) =>
        Events.Any(islandEvent => string.Equals(islandEvent.Title, title, StringComparison.Ordinal));
}

internal sealed class RecordingNotificationSink : INotificationSink
{
    private readonly List<PluginNotification> _notifications = [];

    public IReadOnlyList<PluginNotification> Notifications => _notifications;

    public bool TryPost(PluginNotification notification)
    {
        _notifications.Add(notification);
        return true;
    }
}

internal static class HostFactory
{
    public static (Loader.PluginHost Host, RecordingHostLogger Logger, RecordingPublisher Publisher) Create(
        string pluginRoot,
        Func<PluginHostOptions, PluginHostOptions>? configure = null)
    {
        var logger = new RecordingHostLogger();
        var publisher = new RecordingPublisher();
        PluginHostOptions options = new PluginHostOptions
        {
            PluginRootDirectory = pluginRoot,
            AppVersion = new SemanticVersion(2, 0, 0),
            Logger = logger,
            IslandPublisherFactory = _ => publisher,
            NotificationSinkFactory = _ => new RecordingNotificationSink(),
            InitializeTimeout = TimeSpan.FromSeconds(2),
            StartTimeout = TimeSpan.FromSeconds(2),
            StopTimeout = TimeSpan.FromSeconds(2),
            DisposeTimeout = TimeSpan.FromSeconds(2),
            UnloadCollectionTimeout = TimeSpan.FromSeconds(2),
        };
        if (configure is not null)
        {
            options = configure(options);
        }

        return (new Loader.PluginHost(options), logger, publisher);
    }
}
