using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FluidBar.App.Localization;
using FluidBar.Sdk;

namespace FluidBar.App.Views;

/// <summary>Optional host UI, instantiated only for plugins requesting file-reference access.</summary>
internal sealed class FileShelfPresenter(ILocalizer localizer,
    Func<Action<IReadOnlyList<string>>, IDisposable>? registerDrop = null,
    Func<IReadOnlyList<string>, CancellationToken, Task<bool>>? copyFiles = null) : IPluginFileShelf
{
    public async ValueTask<bool> CopyFilesAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
        => copyFiles is not null && await copyFiles(paths.ToArray(), cancellationToken).ConfigureAwait(false);

    public IDisposable? RegisterIslandDropTarget(Action<IReadOnlyList<string>> dropped, CancellationToken lifetime)
    {
        var registration = registerDrop?.Invoke(dropped);
        if (registration is null) return null;
        var cancellation = lifetime.Register(registration.Dispose);
        return new DropRegistration(registration, cancellation);
    }

    private sealed class DropRegistration(IDisposable registration, CancellationTokenRegistration cancellation) : IDisposable
    {
        public void Dispose() { cancellation.Dispose(); registration.Dispose(); }
    }

    public IDisposable Show(IReadOnlyList<string> paths, Action<IReadOnlyList<string>> changed, CancellationToken lifetime)
    {
        var session = new Session(localizer, paths, changed);
        session.Start(lifetime);
        return session;
    }

    private sealed class Session(ILocalizer localizer, IReadOnlyList<string> paths, Action<IReadOnlyList<string>> changed) : IDisposable
    {
        private FileShelfWindow? _window;
        private Action<IReadOnlyList<string>>? _changed = changed;
        private CancellationTokenRegistration _cancel;
        private int _disposed;

        public void Start(CancellationToken lifetime)
        {
            _cancel = lifetime.Register(Dispose);
            Dispatcher.UIThread.Post(() =>
            {
                if (Volatile.Read(ref _disposed) != 0) return;
                _window = new FileShelfWindow(localizer, paths, next => _changed?.Invoke(next));
                _window.Closed += (_, _) => Dispose();
                _window.Show();
            });
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _changed = null;
            _cancel.Dispose();
            Dispatcher.UIThread.Post(() => { _window?.Close(); _window = null; });
        }
    }
}

/// <summary>Stores and drags references only. Every outbound drag is explicitly Copy, never Move.</summary>
internal sealed class FileShelfWindow : Window
{
    private readonly List<string> _paths;
    private readonly Action<IReadOnlyList<string>> _changed;
    private readonly ILocalizer _localizer;
    private readonly StackPanel _rows = new() { Spacing = 8 };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap };

    public FileShelfWindow(ILocalizer localizer, IReadOnlyList<string> paths, Action<IReadOnlyList<string>> changed)
    {
        _localizer = localizer;
        _changed = changed;
        _paths = paths.Where(Path.IsPathFullyQualified).Distinct(StringComparer.OrdinalIgnoreCase).Take(32).ToList();
        Title = L("Title"); Width = 580; Height = 460; MinWidth = 420; MinHeight = 300;
        ShowInTaskbar = false; Topmost = true; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), RowSpacing = 12, Margin = new Thickness(20) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var add = new Button { Content = L("Add") };
        add.Click += async (_, _) =>
        {
            try
            {
                var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = true, Title = L("Add") });
                AddPaths(picked.Select(item => item.TryGetLocalPath()).OfType<string>());
            }
            catch (Exception) { _message.Text = L("Unavailable"); }
        };
        var clear = new Button { Content = L("Clear") };
        clear.Click += (_, _) => { _paths.Clear(); Notify(); };
        buttons.Children.Add(add); buttons.Children.Add(clear); content.Children.Add(buttons);
        _message.Text = L("Hint"); Grid.SetRow(_message, 1); content.Children.Add(_message);
        var scroll = new ScrollViewer { Content = _rows }; Grid.SetRow(scroll, 2); content.Children.Add(scroll);
        var close = new Button { Content = localizer.Get(LocKeys.Common.Close), HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close(); Grid.SetRow(close, 3); content.Children.Add(close);
        Content = content;
        AutomationProperties.SetName(this, Title);
        DragDrop.SetAllowDrop(content, true);
        content.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.DataTransfer.TryGetFiles()?.Any() == true ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        });
        content.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            AddPaths((e.DataTransfer.TryGetFiles() ?? []).Select(item => item.TryGetLocalPath()).OfType<string>());
            e.Handled = true;
        });
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        Render();
    }

    internal void AddPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (_paths.Count >= 32) break;
            if (Path.IsPathFullyQualified(path) && !_paths.Contains(path, StringComparer.OrdinalIgnoreCase)) _paths.Add(path);
        }
        Notify();
    }

    private void Notify() { _changed(_paths.ToArray()); Render(); }
    private string L(string key) => _localizer.Get("FileShelf." + key);

    private void Render()
    {
        _rows.Children.Clear();
        if (_paths.Count == 0) _rows.Children.Add(new TextBlock { Text = L("Empty"), Opacity = .7 });
        foreach (var path in _paths)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            var label = new TextBlock { Text = Path.GetFileName(path), TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(8) };
            ToolTip.SetTip(label, path);
            label.PointerPressed += async (_, e) =>
            {
                if (!e.GetCurrentPoint(label).Properties.IsLeftButtonPressed) return;
                try
                {
                    var file = await StorageProvider.TryGetFileFromPathAsync(new Uri(path));
                    if (file is null) { _message.Text = L("Unavailable"); return; }
                    using var data = new DataTransfer();
                    data.Add(DataTransferItem.CreateFile(file));
                    await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy);
                }
                catch (Exception) { _message.Text = L("Unavailable"); }
            };
            var remove = new Button { Content = L("Remove") };
            AutomationProperties.SetName(remove, L("Remove") + " " + Path.GetFileName(path));
            remove.Click += (_, _) => { _paths.Remove(path); Notify(); };
            row.Children.Add(label); Grid.SetColumn(remove, 1); row.Children.Add(remove); _rows.Children.Add(row);
        }
    }
}
