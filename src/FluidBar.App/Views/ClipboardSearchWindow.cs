using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace FluidBar.App.Views;

/// <summary>
/// Small always-on-top search box for the clipboard page. The island surface is a non-activating layered
/// window, so it cannot host a text field directly; this window owns keyboard focus and feeds the query
/// back to <c>SkiaPlatformActions</c> while the island rows filter live.
/// </summary>
internal sealed class ClipboardSearchWindow : Window
{
    private readonly TextBox _query;
    private readonly Action<string?> _onQuery;

    public ClipboardSearchWindow(Action<string?> onQuery, string title, string watermark, string hint)
    {
        _onQuery = onQuery ?? throw new ArgumentNullException(nameof(onQuery));
        Title = title;
        Width = 420;
        Height = 150;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.Parse("#101014"));

        _query = new TextBox
        {
            Watermark = watermark,
            FontSize = 16,
            Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 10, 0, 0),
        };
        _query.TextChanged += (_, _) => _onQuery(_query.Text);

        Content = new Border
        {
            Padding = new Thickness(18, 14),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock
                    {
                        Text = watermark,
                        Foreground = new SolidColorBrush(Color.Parse("#D8D8E0")),
                        FontSize = 13,
                    },
                    _query,
                    new TextBlock
                    {
                        Text = hint,
                        Foreground = new SolidColorBrush(Color.Parse("#8A8A98")),
                        FontSize = 11,
                        Margin = new Thickness(0, 4, 0, 0),
                    },
                },
            },
        };

        Opened += (_, _) => _query.Focus();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
    }
}
