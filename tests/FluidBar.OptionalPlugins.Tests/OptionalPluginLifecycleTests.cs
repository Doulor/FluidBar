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
    [Theory]
    [InlineData("4.0.0", false)]
    [InlineData("4.1.0", true)]
    public void Shelf_compatibility_protects_hosts_without_the_new_drag_and_copy_contract(string appVersion, bool compatible)
    {
        var manifest = PluginManifestParser.Parse(File.ReadAllText(Path.Combine(Repository(), "plugins", "fluidbar-file-shelf", "manifest.json")));
        var result = PluginApiVersion.CheckCompatibility(SemanticVersion.Parse(manifest.ApiVersion),
            SemanticVersion.Parse(manifest.MinAppVersion), null, PluginApiVersion.Current, SemanticVersion.Parse(appVersion));
        Assert.Equal(compatible, result.IsCompatible);
    }

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
            var idlePage = Assert.Single(fixture.Publisher.Pages.Values);
            Assert.Equal("drag-hint", Assert.Single(idlePage.List!.Rows).RowId);
            shelf.Drop!([Path.Combine(Path.GetTempPath(), "reference-only.txt")]);
            var updatedPage = Assert.Single(fixture.Publisher.Pages.Values);
            Assert.Contains(updatedPage.List!.Rows, row => row.RowId.StartsWith("file-", StringComparison.Ordinal));
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

    [Fact]
    public async Task Shelf_drop_publishes_copy_remove_actions_and_a_persistent_file_activity()
    {
        var shelf = new FakeShelf();
        await using var fixture = new PluginFixture("fluidbar-file-shelf", new FileShelfPlugin(), shelf);
        Assert.True((await fixture.Session.StartAsync()).Success);
        var path = Path.Combine(Path.GetTempPath(), "shelf-copy.txt");
        shelf.Drop!([path]);
        var page = Assert.Single(fixture.Publisher.Pages.Values);
        var file = Assert.Single(page.List!.Rows.Where(row => row.RowId.StartsWith("file-")));
        Assert.Equal("fluidbar-file-shelf.copy", file.RowCommandId);
        Assert.Equal("fluidbar-file-shelf.remove", file.TrailingCommandId);
        var dragFiles = Assert.IsAssignableFrom<IReadOnlyList<string>>(file.GetType().GetProperty("DragFiles")?.GetValue(file));
        Assert.Equal(path, Assert.Single(dragFiles));
        var activity = fixture.Publisher.Events.Last();
        Assert.Equal("file", activity.IconKind);
        Assert.True(activity.Payload!.IsActive);
        var args = new Dictionary<string, string?> { ["rowId"] = file.RowId };
        Assert.True((await fixture.Session.ExecuteCommandAsync(file.RowCommandId!, arguments: args)).Succeeded);
        Assert.Equal(path, Assert.Single(shelf.Copied));
        Assert.True((await fixture.Session.ExecuteCommandAsync(file.TrailingCommandId!, arguments: args)).Succeeded);
        Assert.Equal(0, fixture.Publisher.Pages.Values.Single().BadgeCount);
        Assert.Contains("fluidbar-file-shelf", fixture.Publisher.Retractions);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Shelf_all_32_references_are_accessible_within_the_host_row_limit()
    {
        var shelf = new FakeShelf();
        await using var fixture = new PluginFixture("fluidbar-file-shelf", new FileShelfPlugin(), shelf);
        Assert.True((await fixture.Session.StartAsync()).Success);
        var paths = Enumerable.Range(0, 32).Select(i => Path.Combine(Path.GetTempPath(), $"shelf-{i}.txt")).ToArray();
        shelf.Drop!(paths);
        var first = fixture.Publisher.Pages.Values.Single();
        Assert.InRange(first.List!.Rows.Count, 1, 20);
        var next = Assert.Single(first.List.Rows.Where(row => row.RowId == "navigation"));
        var seen = first.List.Rows.Where(row => row.RowId.StartsWith("file-")).Select(row => row.Subtitle).ToList();
        Assert.True((await fixture.Session.ExecuteCommandAsync(next.TrailingCommandId!)).Succeeded);
        var second = fixture.Publisher.Pages.Values.Single();
        Assert.InRange(second.List!.Rows.Count, 1, 20);
        seen.AddRange(second.List.Rows.Where(row => row.RowId.StartsWith("file-")).Select(row => row.Subtitle));
        Assert.Equal(paths, seen);
        Assert.True((await fixture.Session.ExecuteCommandAsync("fluidbar-file-shelf.copy-all")).Succeeded);
        Assert.Equal(paths, shelf.Copied);
    }

    [Fact]
    public async Task Shelf_copy_failure_is_visible_and_does_not_remove_references()
    {
        var shelf = new FakeShelf { CopySucceeds = false };
        await using var fixture = new PluginFixture("fluidbar-file-shelf", new FileShelfPlugin(), shelf);
        Assert.True((await fixture.Session.StartAsync()).Success);
        shelf.Drop!([Path.Combine(Path.GetTempPath(), "missing.txt")]);
        Assert.True((await fixture.Session.ExecuteCommandAsync("fluidbar-file-shelf.copy-all")).Succeeded);
        var page = fixture.Publisher.Pages.Values.Single();
        Assert.Equal(1, page.BadgeCount);
        Assert.Contains("Copy failed", page.List!.Rows.Single(row => row.RowId == "actions").Subtitle);
    }

    [Fact]
    public async Task Shelf_stale_row_commands_cannot_copy_or_remove_a_different_file()
    {
        var shelf = new FakeShelf();
        await using var fixture = new PluginFixture("fluidbar-file-shelf", new FileShelfPlugin(), shelf);
        Assert.True((await fixture.Session.StartAsync()).Success);
        shelf.Drop!([Path.Combine(Path.GetTempPath(), "first.txt")]);
        var stale = fixture.Publisher.Pages.Values.Single().List!.Rows.Single(row => row.RowId.StartsWith("file-", StringComparison.Ordinal)).RowId;
        Assert.True((await fixture.Session.ExecuteCommandAsync("fluidbar-file-shelf.clear")).Succeeded);
        shelf.Drop!([Path.Combine(Path.GetTempPath(), "second.txt")]);
        var args = new Dictionary<string, string?> { ["rowId"] = stale };
        Assert.True((await fixture.Session.ExecuteCommandAsync("fluidbar-file-shelf.copy", arguments: args)).Succeeded);
        Assert.True((await fixture.Session.ExecuteCommandAsync("fluidbar-file-shelf.remove", arguments: args)).Succeeded);
        Assert.Empty(shelf.Copied);
        Assert.Equal(1, fixture.Publisher.Pages.Values.Single().BadgeCount);
    }

    [Fact]
    public async Task Shelf_restores_references_and_activity_after_restart()
    {
        var shelf = new FakeShelf();
        await using var fixture = new PluginFixture("fluidbar-file-shelf", new FileShelfPlugin(), shelf);
        Assert.True((await fixture.Session.StartAsync()).Success);
        var path = Path.Combine(Path.GetTempPath(), "persisted.txt");
        shelf.Drop!([path]);
        await fixture.Session.StopAsync();
        Assert.True((await fixture.Session.StartAsync()).Success);
        Assert.Equal(path, fixture.Publisher.Pages.Values.Single().List!.Rows.Single(row => row.RowId.StartsWith("file-", StringComparison.Ordinal)).Subtitle);
        Assert.Equal("persisted.txt", fixture.Publisher.Events.Last().Title);
    }

    private static string Repository([System.Runtime.CompilerServices.CallerFilePath] string source = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(source)!);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "plugins", "fluidbar-timer"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository not found.");
    }

    [Fact]
    public async Task Legacy_shelf_file_rows_are_adapted_for_dragging_without_reinstalling_the_plugin()
    {
        var path = Path.Combine(Path.GetTempPath(), "legacy-shelf.txt");
        await using var fixture = new PluginFixture("fluidbar-file-shelf", new LegacyShelfPlugin(path));
        Assert.True((await fixture.Session.StartAsync()).Success);
        var row = fixture.Publisher.Pages.Values.Single().List!.Rows.Single();
        Assert.Equal(path, Assert.Single(row.DragFiles!));
        Assert.Null(row.RowCommandId);
    }

    private sealed class LegacyShelfPlugin(string path) : IFluidBarPlugin
    {
        private IPluginContext? _context;
        public ValueTask InitializeAsync(IPluginContext context, CancellationToken token)
        { _context = context; return ValueTask.CompletedTask; }
        public ValueTask StartAsync(CancellationToken token)
        {
            _context!.Islands.RegisterPage(new PageDescriptor
            { PageId = "fluidbar-file-shelf.page", Title = new LocalizedString("Files"), IconToken = "file",
                TemplateId = PageTemplates.List, Order = 110 });
            _context.Islands.PublishPage(new PageData
            { PageId = "fluidbar-file-shelf.page", TemplateId = PageTemplates.List,
                List = new PageListData { Rows = [new PageListRow
                { RowId = "file-0", Title = "legacy-shelf.txt", Subtitle = path, IconToken = "file" }] } });
            return ValueTask.CompletedTask;
        }
        public ValueTask StopAsync(CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
            var options = new PluginHostOptions { PluginRootDirectory = _root, AppVersion = new SemanticVersion(4, 1, 0),
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
        public ConcurrentQueue<string> Retractions { get; } = new();
        public ConcurrentDictionary<string, PageData> Pages { get; } = new();
        public bool TryPublish(IslandEvent value) { Events.Enqueue(value); return true; }
        public bool TryRetract(string source) { Retractions.Enqueue(source); return true; }
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
        public IReadOnlyList<string> Copied { get; private set; } = [];
        public bool CopySucceeds { get; init; } = true;
        public ValueTask<bool> CopyFilesAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
        {
            Copied = paths.ToArray();
            return ValueTask.FromResult(CopySucceeds);
        }
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
