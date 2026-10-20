using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace FluidBar.App.Views;

/// <summary>Simple page selector opened from the rail overflow ellipsis; native Avalonia UIA/keyboard/scroll.</summary>
public sealed class PageChooserWindow : Window
{
    /// <summary>One selectable page.</summary>
    public sealed record Item(string PageId, string Glyph, string Title);

    private readonly ListBox _list;
    private readonly Action<int> _select;

    /// <summary>Creates the selector.</summary>
    public PageChooserWindow(IReadOnlyList<Item> pages, int selected, Action<int> select)
    {
        ArgumentNullException.ThrowIfNull(pages);
        _select = select ?? throw new ArgumentNullException(nameof(select));
        Title = "Pages";
        Width = 360;
        Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        CanResize = false;
        Background = new SolidColorBrush(Color.Parse("#141418"));

        _list = new ListBox { Margin = new Thickness(12), Background = Brushes.Transparent };
        for (var i = 0; i < pages.Count; i++)
        {
            _list.Items.Add(new TextBlock
            {
                Text = $"{pages[i].Glyph}   {pages[i].Title}",
                Foreground = new SolidColorBrush(Color.Parse("#F2F2F5")),
                Margin = new Thickness(6, 8),
                FontSize = 14,
            });
        }

        _list.SelectedIndex = Math.Clamp(selected, 0, Math.Max(0, pages.Count - 1));
        _list.DoubleTapped += (_, _) => Commit();
        _list.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                Commit();
            }
            else if (args.Key == Key.Escape)
            {
                Close();
            }
        };

        var open = new Button { Content = "Open page", Margin = new Thickness(12, 0, 12, 12), HorizontalAlignment = HorizontalAlignment.Right };
        open.Click += (_, _) => Commit();

        Content = new DockPanel
        {
            Children =
            {
                open,
                _list,
            },
        };
        DockPanel.SetDock(open, Dock.Bottom);
        Opened += (_, _) => _list.Focus();
    }

    private void Commit()
    {
        var index = _list.SelectedIndex;
        if (index < 0)
        {
            return;
        }

        Close();
        _select(index);
    }
}
