using System.Text.Json;
using FluidBar.Sdk;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Pages;

namespace FluidBar.Plugins.FileShelf;

public sealed class FileShelfPlugin : IFluidBarPlugin
{
    private readonly object _gate = new();
    private IPluginContext? _context;
    private readonly List<IDisposable> _commands = [];
    private IDisposable? _view;
    private IDisposable? _dropTarget;
    private string[] _paths = [];
    private Task _save = Task.CompletedTask;
    private bool _started;
    private long _revision;
    private const string PageId = "fluidbar-file-shelf.page";

    public async ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        _context = context;
        context.RegisterForDisposal(() => { _view?.Dispose(); _dropTarget?.Dispose(); });
        var json = await context.Storage.ReadTextAsync("references.json", cancellationToken).ConfigureAwait(false);
        if (json is not null)
        {
            try { _paths = FileReferenceShelf.Normalize(JsonSerializer.Deserialize<string[]>(json)); }
            catch (JsonException) { context.Log.Warning("File reference list could not be read; original files were not touched."); }
        }
    }

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        var context = _context ?? throw new InvalidOperationException("Plugin is not initialized.");
        lock (_gate)
        {
            if (_started) return ValueTask.CompletedTask;
            _started = true;
            _dropTarget = context.FileShelf?.RegisterIslandDropTarget(paths =>
            {
                lock (_gate)
                {
                    if (!_started) return;
                    Changed(_paths.Concat(paths).ToArray());
                    _view?.Dispose();
                    _view = context.FileShelf!.Show(_paths, Changed, context.HostStopping);
                }
            }, context.HostStopping);
            _commands.Add(context.Commands.Register(context.PluginId + ".open", (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                lock (_gate)
                {
                    if (!_started) return ValueTask.CompletedTask;
                    var shelf = context.FileShelf ?? throw new InvalidOperationException("The host file shelf is unavailable or lacks consent.");
                    _view?.Dispose();
                    _view = shelf.Show(_paths, Changed, context.HostStopping);
                }
                return ValueTask.CompletedTask;
            }));
            _commands.Add(context.Commands.Register(context.PluginId + ".clear", (_, _) =>
            {
                Changed([]);
                _view?.Dispose(); _view = null;
                return ValueTask.CompletedTask;
            }));
            context.Islands.RegisterPage(new PageDescriptor
            {
                PageId = PageId, Order = 110, Title = LocalizedString.From(context.Localization.Get("title")),
                IconToken = "clipboard", TemplateId = PageTemplates.List,
            });
            Publish();
        }
        return ValueTask.CompletedTask;
    }

    private void Changed(IReadOnlyList<string> paths)
    {
        lock (_gate)
        {
            if (!_started) return;
            _paths = FileReferenceShelf.Normalize(paths);
            var snapshot = _paths;
            _save = _save.ContinueWith(async _ =>
            {
                try { await _context!.Storage.WriteTextAsync("references.json", JsonSerializer.Serialize(snapshot)).ConfigureAwait(false); }
                catch (Exception ex) { _context!.Log.Warning("File references could not be saved: " + ex.GetType().Name); }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
            Publish();
        }
    }

    private void Publish()
    {
        var context = _context!;
        string L(string key) => context.Localization.Get(key);
        List<PageListRow> rows =
        [
            new() { RowId = "open", Title = L("open"), Subtitle = L("hint"), RowCommandId = context.PluginId + ".open" },
        ];
        rows.AddRange(_paths.Select((path, index) => new PageListRow
        {
            RowId = "file-" + index, Title = Path.GetFileName(path), Subtitle = path,
            RowCommandId = context.PluginId + ".open", IconToken = "file",
        }));
        context.Islands.PublishPage(new PageData
        {
            PageId = PageId, TemplateId = PageTemplates.List, Revision = ++_revision,
            BadgeCount = _paths.Length, List = new PageListData { Rows = rows, EmptyTitle = L("empty") },
        });
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        Task pending;
        lock (_gate)
        {
            _started = false;
            _dropTarget?.Dispose(); _dropTarget = null;
            _view?.Dispose(); _view = null;
            foreach (var command in _commands) command.Dispose();
            _commands.Clear();
            _context?.Islands.UnregisterPage(PageId);
            pending = _save;
        }
        await pending.ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => StopAsync(CancellationToken.None);
}
