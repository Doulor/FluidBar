using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

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

    public ClipboardSearchWindow(Action<string?> onQuery, string title, string watermark, string hint, string? initialQuery = null)
    {
        _onQuery = onQuery ?? throw new ArgumentNullException(nameof(onQuery));
        Title = title;
        Width = 420;
        Height = 150;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var dark = Application.Current?.ActualThemeVariant != ThemeVariant.Light;
        var foreground = dark ? "#D8D8E0" : "#24242C";
        var muted = dark ? "#8A8A98" : "#626270";
        Background = new SolidColorBrush(Color.Parse(dark ? "#101014" : "#F7F7FA"));

        _query = new TextBox
        {
            Watermark = watermark,
            FontSize = 16,
            Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 10, 0, 0),
        };
        AutomationProperties.SetName(this, title);
        AutomationProperties.SetName(_query, watermark);
        AutomationProperties.SetHelpText(_query, hint);
        _query.Text = initialQuery ?? string.Empty;
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
                        Foreground = new SolidColorBrush(Color.Parse(foreground)),
                        FontSize = 13,
                    },
                    _query,
                    new TextBlock
                    {
                        Text = hint,
                        Foreground = new SolidColorBrush(Color.Parse(muted)),
                        FontSize = 11,
                        Margin = new Thickness(0, 4, 0, 0),
                    },
                },
            },
        };

        Opened += (_, _) => FocusQuery();
        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Escape or Key.Enter)
            {
                Close();
                e.Handled = true;
            }
        };
    }

    /// <summary>Focuses the query and selects the existing text so typing replaces it.</summary>
    public void FocusQuery()
    {
        _query.Focus();
        _query.SelectAll();
    }
}
