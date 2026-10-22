using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless;
using FluidBar.App.Views;

namespace FluidBar.Ui.Tests;

/// <summary>The search window must expose its label and select prefilled text when focused.</summary>
public sealed class ClipboardSearchWindowTests
{
    [Fact]
    public async Task Window_forwards_typing_and_closes_on_enter()
    {
        await AvaloniaTest.Run(() =>
        {
            string? last = null;
            var window = new ClipboardSearchWindow(
                query => last = query,
                "Search — FluidBar",
                "Search",
                "Esc closes and clears the filter");

            try
            {
                window.Show();
                var panel = Assert.IsType<StackPanel>(Assert.IsType<Border>(window.Content).Child);
                var textBox = Assert.IsType<TextBox>(panel.Children[1]);
                textBox.Focus();
                window.KeyTextInput("clipboard");
                Assert.Equal("clipboard", last);

                window.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Enter,
                });
                Assert.False(window.IsVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task Window_sets_automation_name_and_selects_the_initial_query()
    {
        await AvaloniaTest.Run(() =>
        {
            var window = new ClipboardSearchWindow(
                _ => { },
                "Search — FluidBar",
                "Search",
                "Esc closes and clears the filter",
                "known-query");

            try
            {
                Assert.Equal("Search — FluidBar", AutomationProperties.GetName(window));
                var panel = Assert.IsType<StackPanel>(Assert.IsType<Border>(window.Content).Child);
                var textBox = Assert.IsType<TextBox>(panel.Children[1]);
                Assert.Equal("Search", AutomationProperties.GetName(textBox));
                Assert.Equal("known-query", textBox.Text);

                window.Show();
                window.FocusQuery();
                Assert.Equal(0, textBox.SelectionStart);
                Assert.Equal(textBox.Text!.Length, textBox.SelectionEnd);

                window.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Escape,
                });
                Assert.False(window.IsVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
