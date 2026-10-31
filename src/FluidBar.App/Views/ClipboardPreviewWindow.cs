using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FluidBar.Platform.Windows.Clipboard;

namespace FluidBar.App.Views;

/// <summary>Focused, selectable clipboard preview; copy always uses the captured original payload.</summary>
internal sealed class ClipboardPreviewWindow : Window
{
    public ClipboardPreviewWindow(ClipboardEntry entry, Action copy, string title, string copyLabel,
        string closeLabel, string imageUnavailable, string? truncatedText = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(copy);
        Title = title;
        Width = 640;
        Height = 520;
        MinWidth = 400;
        MinHeight = 280;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        AutomationProperties.SetName(this, title);
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(18) };
        var incomplete = entry.Kind == ClipboardContentKind.Text && entry.CompleteText is null;
        if (incomplete)
            grid.Children.Add(new TextBlock { Text = truncatedText ?? "Only a preview was retained. Copy the full text from its source.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        Bitmap? bitmap = null;
        if (entry.Kind == ClipboardContentKind.Image && entry.ImagePng is { Length: > 0 } png)
        {
            try
            {
                using var stream = new MemoryStream(png, writable: false);
                bitmap = new Bitmap(stream);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException or NotSupportedException)
            {
                // Older/unsupported DIB entries remain copyable even when a decoder cannot preview them.
            }
        }

        if (entry.Kind == ClipboardContentKind.Image)
        {
            Control image = bitmap is not null
                ? new Image { Source = bitmap, Stretch = Stretch.Uniform }
                : new TextBlock { Text = imageUnavailable, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetName(image, title);
            Grid.SetRow(image, 1);
            grid.Children.Add(image);
        }
        else
        {
            var text = new TextBox
            {
                Text = entry.Kind == ClipboardContentKind.Files ? string.Join(Environment.NewLine, entry.FilePaths) : entry.CompleteText ?? entry.Text ?? string.Empty,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalContentAlignment = VerticalAlignment.Top,
            };
            AutomationProperties.SetName(text, title);
            ScrollViewer.SetVerticalScrollBarVisibility(text, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
            Grid.SetRow(text, 1);
            grid.Children.Add(text);
        }

        var copyButton = new Button { Content = copyLabel, MinWidth = 90, IsEnabled = !incomplete };
        AutomationProperties.SetName(copyButton, copyLabel);
        copyButton.Click += (_, _) => copy();
        var closeButton = new Button { Content = closeLabel, MinWidth = 90 };
        AutomationProperties.SetName(closeButton, closeLabel);
        closeButton.Click += (_, _) => Close();
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(0, 14, 0, 0),
            Children = { copyButton, closeButton },
        };
        Grid.SetRow(footer, 2);
        grid.Children.Add(footer);
        Content = grid;
        Closed += (_, _) => bitmap?.Dispose();
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Close();
            e.Handled = true;
        };
    }
}
