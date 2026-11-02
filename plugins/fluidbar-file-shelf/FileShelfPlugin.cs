using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using FluidBar.Sdk;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Pages;

namespace FluidBar.Plugins.FileShelf;

public sealed class FileShelfPlugin : IFluidBarPlugin
{
    private readonly object _gate = new();
    private IPluginContext? _context;
    private readonly List<IDisposable> _commands = [];
    private IDisposable? _dropTarget;
    private string[] _paths = [];
    private Task _save = Task.CompletedTask;
    private bool _started;
    private long _revision;
    private int _page;
    private string? _message;
    private const int PageSize = 16;
    private const string PageId = "fluidbar-file-shelf.page";

    public async ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        _context = context;
        context.RegisterForDisposal(() => { _dropTarget?.Dispose(); });
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
                }
            }, context.HostStopping);
            foreach (var name in new[] { "clear", "copy", "copy-all", "remove", "next", "previous" })
            {
                var action = name;
                _commands.Add(context.Commands.Register(context.PluginId + "." + name,
                    (command, token) => ExecuteAsync(action, command, token)));
            }
            context.Islands.RegisterPage(new PageDescriptor
            {
                PageId = PageId, Order = 110, Title = LocalizedString.From(context.Localization.Get("title")),
                IconToken = "file", TemplateId = PageTemplates.List,
            });
            Publish();
        }
        return ValueTask.CompletedTask;
    }

    private async ValueTask ExecuteAsync(string action, PluginCommandContext command, CancellationToken token)
    {
        string[] copy;
        long revision;
        lock (_gate)
        {
            if (!_started) return;
            token.ThrowIfCancellationRequested();
            var rowId = command.Arguments is { } args && args.TryGetValue("rowId", out var id) ? id : null;
            var path = _paths.FirstOrDefault(path => RowId(path) == rowId);
            if (action == "clear") { Changed([]); return; }
            if (action == "remove")
            {
                if (path is not null) Changed(_paths.Where(item => item != path).ToArray());
                return;
            }
            if (action is "next" or "previous")
            {
                _page = Math.Clamp(_page + (action == "next" ? 1 : -1), 0, Math.Max(0, (_paths.Length - 1) / PageSize));
                Publish();
                return;
            }
            copy = action == "copy-all" ? _paths.ToArray() : path is null ? [] : [path];
            if (copy.Length == 0) return;
            revision = _revision;
        }

        var succeeded = false;
        try
        {
            if (_context!.FileShelf is { } shelf)
                succeeded = await shelf.CopyFilesAsync(copy, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) { _context!.Log.Warning("File copy failed: " + ex.GetType().Name); }
        lock (_gate)
        {
            // A late clipboard result must not overwrite a newer drop/clear or publish after stop.
            if (!_started || _revision != revision) return;
            _message = _context!.Localization.Get(succeeded ? "copied" : "unavailable");
            Publish();
        }
    }

    private static string RowId(string path) => "file-" + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant()))).Substring(0, 16);

    private void Changed(IReadOnlyList<string> paths)
    {
        lock (_gate)
        {
            if (!_started) return;
            _paths = FileReferenceShelf.Normalize(paths);
            _page = Math.Clamp(_page, 0, Math.Max(0, (_paths.Length - 1) / PageSize));
            _message = null;
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
        List<PageListRow> rows = [];
        if (_paths.Length == 0)
        {
            rows.Add(new PageListRow
            {
                RowId = "drag-hint", Title = L("empty"), Subtitle = L(_dropTarget is null ? "unsupported" : "hint"),
                IconToken = "file",
            });
        }
        else
        {
            rows.Add(new PageListRow
            {
                RowId = "actions", Title = L("copy-all"), Subtitle = _message ?? L("copy-hint"),
                RowCommandId = context.PluginId + ".copy-all", IconToken = "file",
                TrailingLabel = L("clear"), TrailingCommandId = context.PluginId + ".clear",
            });
            if (_paths.Length > PageSize)
                rows.Add(new PageListRow
                {
                    RowId = "navigation", Title = L(_page > 0 ? "previous" : "page"),
                    Value = $"{_page + 1}/{(_paths.Length + PageSize - 1) / PageSize}",
                    RowCommandId = _page > 0 ? context.PluginId + ".previous" : null,
                    TrailingLabel = L("next"),
                    TrailingCommandId = _page < (_paths.Length - 1) / PageSize ? context.PluginId + ".next" : null,
                });
            rows.AddRange(_paths.Skip(_page * PageSize).Take(PageSize).Select(path => new PageListRow
            {
                RowId = RowId(path), Title = FileName(path), Subtitle = path,
                IconToken = "file", RowCommandId = context.PluginId + ".copy",
                DragFiles = [path],
                TrailingLabel = L("remove"), TrailingCommandId = context.PluginId + ".remove",
            }));
        }

        context.Islands.PublishPage(new PageData
        {
            PageId = PageId, TemplateId = PageTemplates.List, Revision = ++_revision,
            BadgeCount = _paths.Length, List = new PageListData
            {
                Rows = rows,
                EmptyTitle = L("empty"),
                EmptyHint = L("hint"),
            },
        });
        if (_paths.Length == 0) context.Islands.TryRetract(context.PluginId);
        else context.Islands.TryPublish(new IslandEvent
        {
            Source = context.PluginId, Title = FileName(_paths[0]),
            Content = FileName(_paths[0]), IconKind = "file", Kind = IslandEventKind.Status,
            Payload = new IslandEventPayload
            {
                IsActive = true, Badge = _paths.Length.ToString(CultureInfo.InvariantCulture),
                SourceName = L("title"), Subtitle = L("title"),
            },
        });
    }

    private static string FileName(string path) => Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } name ? name : path;

    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        Task pending;
        lock (_gate)
        {
            _started = false;
            _dropTarget?.Dispose(); _dropTarget = null;
            foreach (var command in _commands) command.Dispose();
            _commands.Clear();
            _context?.Islands.TryRetract(_context.PluginId);
            _context?.Islands.UnregisterPage(PageId);
            pending = _save;
        }
        await pending.ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => StopAsync(CancellationToken.None);
}
