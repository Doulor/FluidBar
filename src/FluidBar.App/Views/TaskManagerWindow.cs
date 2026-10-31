using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using FluidBar.App.Localization;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Tasks;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.App.Views;

/// <summary>Keyboard-capable task editor backed by the same task service as the island card.</summary>
internal sealed class TaskManagerWindow : Window
{
    private readonly TaskListService _service;
    private readonly ILocalizer _localizer;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TextBox _title = new() { Name = "task-title", MaxLength = 500 };
    private readonly Button _save = new() { Name = "task-add" };
    private readonly Button _cancel = new();
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap, Opacity = .6, FontSize = 12 };
    private readonly StackPanel _rows = new() { Spacing = 8 };
    private readonly StackPanel _editor = new() { Spacing = 10 };
    private TaskListSnapshot? _snapshot;
    private TaskListSnapshot? _editingSnapshot;
    private int _editingIndex = -1;
    private bool _busy;
    private bool _closed;
    private string? _messageKey;

    public TaskManagerWindow(TaskListService service, ILocalizer localizer)
    {
        _service = service;
        _localizer = localizer;
        Width = 560;
        Height = 540;
        MinWidth = 440;
        MinHeight = 340;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var dark = Application.Current?.ActualThemeVariant != ThemeVariant.Light;
        Background = new SolidColorBrush(Color.Parse(dark ? "#15151A" : "#F7F7FA"));
        Foreground = new SolidColorBrush(Color.Parse(dark ? "#E0E0E8" : "#24242C"));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _save, _cancel } };
        _editor.Children.Add(_title);
        _editor.Children.Add(buttons);
        _editor.Children.Add(_hint);
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), RowSpacing = 16 };
        layout.Children.Add(_editor);
        Grid.SetRow(_message, 1);
        layout.Children.Add(_message);
        var scroll = new ScrollViewer { Content = _rows, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 2);
        layout.Children.Add(scroll);
        Content = new Border { Padding = new Thickness(22), Child = layout };
        _save.Click += async (_, _) => await SaveAsync();
        _cancel.Click += (_, _) => CancelEdit();
        _title.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; await SaveAsync(); }
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        Opened += (_, _) => FocusInput();
        Closed += (_, _) =>
        {
            _closed = true;
            _service.SnapshotChanged -= OnSnapshotChanged;
            _localizer.Changed -= OnLocaleChanged;
            _lifetime.Cancel();
            _lifetime.Dispose();
        };
        _service.SnapshotChanged += OnSnapshotChanged;
        _localizer.Changed += OnLocaleChanged;
        _busy = true;
        _messageKey = "Tasks.Manage.Loading";
        Render();
        _ = ReloadAsync();
    }

    public void FocusInput() => _title.Focus();

    private async Task ReloadAsync()
    {
        var snapshot = await Task.Run(_service.Load);
        if (_closed) return;
        _snapshot = snapshot;
        _busy = false;
        _messageKey = null;
        Render();
    }

    private void OnSnapshotChanged(object? sender, SnapshotEventArgs<TaskListSnapshot> e)
        => Dispatcher.UIThread.Post(() => { if (!_closed) { _snapshot = e.Snapshot; Render(); } });

    private void OnLocaleChanged(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(() => { if (!_closed) Render(); });

    private async Task SaveAsync()
    {
        if (_busy) return;
        var title = _title.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title))
        {
            _messageKey = "Tasks.Manage.Error";
            Render();
            return;
        }
        var index = _editingIndex;
        var expected = _editingSnapshot;
        await ExecuteAsync(() => index < 0
            ? _service.AddAsync(title, _lifetime.Token)
            : _service.RenameAsync(index, title, _lifetime.Token, expected), clearEditor: true);
    }

    private async Task ExecuteAsync(Func<Task<ToggleTaskResult>> operation, bool clearEditor = false)
    {
        if (_busy || _closed) return;
        _busy = true;
        _messageKey = null;
        Render();
        try
        {
            var result = await operation();
            if (_closed) return;
            _snapshot = result.Snapshot;
            if (result.Succeeded)
            {
                if (clearEditor) CancelEdit();
            }
            else
            {
                _messageKey = result.FailureReason?.StartsWith("tasks changed", StringComparison.Ordinal) == true
                    ? "Tasks.Manage.Changed" : "Tasks.Manage.Error";
                // A refreshed index may refer to another task. Keep the typed text, return to add mode.
                if (_messageKey == "Tasks.Manage.Changed") { _editingIndex = -1; _editingSnapshot = null; }
            }
        }
        catch (OperationCanceledException) when (_closed) { }
        catch (Exception)
        {
            if (!_closed) _messageKey = "Tasks.Manage.Error";
        }
        finally
        {
            _busy = false;
            if (!_closed) Render();
        }
    }

    private void CancelEdit()
    {
        _editingIndex = -1;
        _editingSnapshot = null;
        _title.Text = string.Empty;
        Render();
        FocusInput();
    }

    private void Render()
    {
        Title = _localizer.Get("Tasks.Manage.Title");
        AutomationProperties.SetName(this, Title);
        _title.Watermark = _localizer.Get("Tasks.Manage.Input");
        AutomationProperties.SetName(_title, _title.Watermark.ToString());
        _save.Content = _localizer.Get(_editingIndex < 0 ? "Tasks.Manage.Add" : "Tasks.Manage.Save");
        _cancel.Content = _localizer.Get("Tasks.Manage.Cancel");
        _cancel.IsVisible = _editingIndex >= 0;
        _hint.Text = _localizer.Get("Tasks.Manage.Hint");
        var invalid = _snapshot?.Status == TaskListStatus.Invalid;
        _editor.IsEnabled = !_busy && !invalid;
        _message.Text = _messageKey is not null ? _localizer.Get(_messageKey)
            : invalid ? _localizer.Get("Tasks.Manage.Invalid")
            : _snapshot?.Count == 0 ? _localizer.Get("Tasks.Manage.Empty") : string.Empty;
        _rows.Children.Clear();
        if (_snapshot is not { } snapshot) return;
        for (var i = 0; i < snapshot.Count; i++)
        {
            var index = i;
            var item = snapshot.Items[i];
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 6, IsEnabled = !_busy };
            var check = new CheckBox
            {
                IsChecked = item.Done,
                Content = new TextBlock { Text = item.Title, TextWrapping = TextWrapping.Wrap, MaxWidth = 300 },
                VerticalAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetName(check, item.Title);
            check.IsCheckedChanged += async (_, _) => await ExecuteAsync(() => _service.ToggleAsync(index, snapshot, _lifetime.Token));
            row.Children.Add(check);
            var edit = new Button { Name = "task-edit-" + index, Content = _localizer.Get("Tasks.Manage.Edit") };
            Grid.SetColumn(edit, 1);
            edit.Click += (_, _) =>
            {
                _editingIndex = index;
                _editingSnapshot = snapshot;
                _title.Text = item.Title;
                _messageKey = null;
                Render();
                FocusInput();
                _title.SelectAll();
            };
            row.Children.Add(edit);
            var delete = new Button { Name = "task-delete-" + index, Content = _localizer.Get("Tasks.Manage.Delete") };
            Grid.SetColumn(delete, 2);
            delete.Click += async (_, _) => await ExecuteAsync(() => _service.RemoveAsync(index, _lifetime.Token, snapshot));
            row.Children.Add(delete);
            _rows.Children.Add(row);
        }
    }
}
