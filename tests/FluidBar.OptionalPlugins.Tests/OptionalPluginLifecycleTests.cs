using System.Collections.Concurrent;
using FluidBar.PluginHost.Loader;
using FluidBar.Plugins.FileShelf;
using FluidBar.Plugins.Timer;
using FluidBar.Sdk;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Pages;
using FluidBar.Sdk.Versioning;

namespace FluidBar.OptionalPlugins.Tests;

public sealed class OptionalPluginLifecycleTests
{
    [Fact]
    public async Task Timer_starts_only_on_command_and_stops_publishing_when_paused_or_unloaded()
    {
        var fixture = new PluginFixture("fluidbar-timer", new TimerPlugin());
        try
        {
            Assert.True((await fixture.Session.StartAsync()).Success);
            Assert.Empty(fixture.Publisher.Events);
            Assert.True((await fixture.Session.ExecuteCommandAsync("fluidbar-timer.start")).Succeeded);
            Assert.NotEmpty(fixture.Publisher.Events);
            Assert.True((await fixture.Session.ExecuteCommandAsync("fluidbar-timer.pause")).Succeeded);
            for (var cycle = 0; cycle < 70; cycle++)
            {
                Assert.True((await fixture.Session.ExecuteCommandAsync("fluidbar-timer.resume")).Succeeded);
                Assert.True((await fixture.Session.ExecuteCommandAsync("fluidbar-timer.pause")).Succeeded);
            }
            var paused = fixture.Publisher.Events.Count;
            await Task.Delay(1200);
            Assert.Equal(paused, fixture.Publisher.Events.Count);
            await fixture.Session.StopAsync();
            Assert.Empty(fixture.Publisher.Pages);
        }
        finally { await fixture.DisposeAsync(); }
    }

    [Fact]
    public async Task Shelf_stays_on_the_island_page_and_releases_drop_on_stop()
    {
        var shelf = new FakeShelf();
        var fixture = new PluginFixture("fluidbar-file-shelf", new FileShelfPlugin(), shelf);
        try
        {
            Assert.True((await fixture.Session.StartAsync()).Success);
            Assert.Equal(0, shelf.Shows);
            Assert.True(shelf.DropRegistered);
            Assert.False((await fixture.Session.ExecuteCommandAsync("fluidbar-file-shelf.open")).Succeeded);
            Assert.Equal(0, shelf.Shows);
            shelf.Drop!([Path.Combine(Path.GetTempPath(), "reference-only.txt")]);
            await fixture.Session.StopAsync();
            Assert.False(shelf.WindowDisposed);
            Assert.False(shelf.DropRegistered);
            Assert.Empty(fixture.Publisher.Pages);
        }
        finally { await fixture.DisposeAsync(); }
    }

    [Theory]
    [InlineData("fluidbar-timer")]
    [InlineData("fluidbar-file-shelf")]
    public void Manifest_and_locales_are_valid(string id)
    {
        var path = Path.Combine(Repository(), "plugins", id);
        var manifest = PluginManifestParser.Parse(File.ReadAllText(Path.Combine(path, "manifest.json")));
        var validation = PluginManifestValidator.Validate(manifest);
        Assert.Empty(validation.Errors);
        Assert.True(File.Exists(Path.Combine(path, "locales", "en-US.json")));
        Assert.True(File.Exists(Path.Combine(path, "locales", "zh-Hans.json")));
    }

    private static string Repository([System.Runtime.CompilerServices.CallerFilePath] string source = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(source)!);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "plugins", "fluidbar-timer"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository not found.");
    }

    private sealed class PluginFixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "fluidbar-optional-" + Guid.NewGuid().ToString("N"));
        public Publisher Publisher { get; } = new();
        public InProcessPluginSession Session { get; }
        public PluginFixture(string id, IFluidBarPlugin plugin, IPluginFileShelf? shelf = null)
        {
            var package = Path.Combine(Repository(), "plugins", id);
            var manifest = PluginManifestParser.Parse(File.ReadAllText(Path.Combine(package, "manifest.json")));
            var options = new PluginHostOptions { PluginRootDirectory = _root, AppVersion = new SemanticVersion(2, 0, 0),
                IslandPublisherFactory = _ => Publisher, FileShelfFactory = shelf is null ? null : _ => shelf };
            Session = new InProcessPluginSession(options, manifest, package, plugin);
        }
        public async ValueTask DisposeAsync()
        {
            await Session.DisposeAsync();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }

    private sealed class Publisher : IIslandPublisher
    {
        public ConcurrentQueue<IslandEvent> Events { get; } = new();
        public ConcurrentDictionary<string, PageData> Pages { get; } = new();
        public bool TryPublish(IslandEvent value) { Events.Enqueue(value); return true; }
        public bool TryRetract(string source) => true;
        public bool RegisterPage(PageDescriptor page) => true;
        public bool PublishPage(PageData page) { Pages[page.PageId] = page; return true; }
        public bool UnregisterPage(string id) => Pages.TryRemove(id, out _);
    }

    private sealed class FakeShelf : IPluginFileShelf
    {
        public int Shows { get; private set; }
        public bool WindowDisposed { get; private set; }
        public bool DropRegistered { get; private set; }
        public Action<IReadOnlyList<string>>? Change { get; private set; }
        public Action<IReadOnlyList<string>>? Drop { get; private set; }
        public IDisposable Show(IReadOnlyList<string> paths, Action<IReadOnlyList<string>> changed, CancellationToken lifetime)
        {
            Shows++; Change = changed;
            return new Cleanup(() => { WindowDisposed = true; Change = null; });
        }
        public IDisposable RegisterIslandDropTarget(Action<IReadOnlyList<string>> dropped, CancellationToken lifetime)
        {
            Drop = dropped;
            DropRegistered = true;
            return new Cleanup(() => DropRegistered = false);
        }
        private sealed class Cleanup(Action action) : IDisposable { public void Dispose() => action(); }
    }
}
