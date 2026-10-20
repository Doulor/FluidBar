using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.Views.Plugins;
using FluidBar.App.Views.Settings;
using FluidBar.Sdk.Contributions;
using FluidBar.Sdk.Manifest;
using AppTheme = FluidBar.App.Settings.AppTheme;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Task-22 guards: every settings surface is built from the one control layer, so a pane cannot drift
/// back to hand-rolled buttons, Fluent switches or bespoke text boxes, and the plugin configuration
/// page (the surface the user called out as simple) renders with the same row/switch/field language
/// as General/Appearance.
/// </summary>
public sealed class SettingsControlLayerTests
{
    private static readonly SettingsPalette Palette = SettingsPalette.For(AppTheme.Dark);

    private static SettingsPalette PaletteFor(string culture)
    {
        _ = culture;
        return Palette;
    }

    [Theory]
    [InlineData(SettingsWindowSection.General)]
    [InlineData(SettingsWindowSection.Appearance)]
    [InlineData(SettingsWindowSection.Island)]
    [InlineData(SettingsWindowSection.Plugins)]
    [InlineData(SettingsWindowSection.Marketplace)]
    [InlineData(SettingsWindowSection.Advanced)]
    [InlineData(SettingsWindowSection.About)]
    public async Task SettingsPanes_ShareOneControlImplementation(SettingsWindowSection section)
    {
        var snapshot = await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var (controller, _) = TestFixtures.CreateSettings(AppSettings.Default);
            var window = new SettingsWindow(controller, localizer, section);
            window.Show();
            try
            {
                var pane = window.GetVisualDescendants().OfType<ScrollViewer>()
                    .First(scroll => Math.Abs(scroll.Padding.Top - 24) < 0.1);
                // Scrollbars are RepeatButtons (Button subclasses) and are Fluent-owned chrome.
                var buttons = pane.GetVisualDescendants().OfType<Button>()
                    .Where(button => button is not RepeatButton)
                    .ToList();
                return new PaneSnapshot(
                    buttons.Count,
                    buttons.Select(button => button.CornerRadius.TopLeft).Distinct().Order().ToArray(),
                    buttons.Select(button => button.Height).Distinct().Order().ToArray(),
                    pane.GetVisualDescendants().OfType<ToggleSwitch>().Count(),
                    pane.GetVisualDescendants().OfType<CheckBox>().Count(),
                    pane.GetVisualDescendants().OfType<RadioButton>().Count(),
                    pane.GetVisualDescendants().OfType<Slider>().Count(),
                    pane.GetVisualDescendants().OfType<SettingsSlider>().Count(),
                    pane.GetVisualDescendants().OfType<ComboBox>().Count());
            }
            finally
            {
                window.Close();
            }
        });

        Assert.True(snapshot.FluentToggles == 0, "Fluent ToggleSwitch must not appear in the settings window");
        Assert.True(snapshot.FluentCheckBoxes == 0, "Fluent CheckBox must not appear in the settings window");
        Assert.True(snapshot.FluentRadios == 0, "Fluent RadioButton must not appear in the settings window");

        // Sliders are the prototype control (SettingsSlider), never Fluent's.
        Assert.True(snapshot.FluentSliders <= snapshot.SettingsSliders);

        // One button implementation: 24/27/28/30/32 px tall, 6/7 px radius.
        foreach (var radius in snapshot.ButtonRadii)
        {
            Assert.True(radius is 6 or 7, $"unexpected button radius {radius}");
        }

        foreach (var height in snapshot.ButtonHeights)
        {
            Assert.True(height is 24 or 27 or 28 or 30 or 32, $"unexpected button height {height}");
        }
    }

    [Fact]
    public async Task PluginDetailPage_UsesRowsSwitchesAndCommandButtons()
    {
        var snapshot = await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var (controller, _) = TestFixtures.CreateSettings(AppSettings.Default);
            var service = new BuiltinPluginContributionUiService(controller, localizer);
            var plugin = service.ListAsync(CancellationToken.None).GetAwaiter().GetResult()
                .First(item => item.Id == "fluidbar-media");
            var detail = PluginDetailView.Build(plugin, localizer, PaletteFor("en-US"), service,
                static _ => { }, static () => { });
            var host = new Border { Width = 620, Child = detail };
            var window = new Window { Content = host, Width = 900, Height = 620 };
            window.Show();

            var descendants = host.GetVisualDescendants().ToList();
            var texts = descendants.OfType<TextBlock>().Select(text => text.Text ?? string.Empty).ToList();
            var buttons = descendants.OfType<Button>()
                .Where(button => button is not RepeatButton)
                .ToList();
            var rows = descendants.OfType<Border>()
                .Count(border => border.BorderThickness.Bottom > 0
                    && Math.Abs(border.MinHeight - 53) < 0.1);
            var snapshot = new DetailSnapshot(
                descendants.OfType<SettingsSwitch>().Count(),
                descendants.OfType<ToggleSwitch>().Count(),
                descendants.OfType<ComboBox>().Count(),
                descendants.OfType<Slider>().Count(),
                rows,
                buttons.Count(button => button.Content is string content && content.Length > 0),
                plugin.Name,
                texts,
                buttons.Select(button => button.CornerRadius.TopLeft).Distinct().Order().ToArray());
            window.Close();
            return snapshot;
        });

        // The page renders through the shared layer only.
        Assert.Equal(0, snapshot.FluentToggles);
        Assert.Equal(0, snapshot.FluentCombos);
        Assert.Equal(0, snapshot.FluentSliders);

        // The media plugin declares three toggles → three prototype switches.
        Assert.Equal(3, snapshot.Switches);

        // Three setting rows + three command rows share the 53 px row rhythm.
        Assert.Equal(6, snapshot.Rows);

        // Header, meta line and the localized back affordance are present.
        Assert.Contains(snapshot.Texts, text => text == snapshot.PluginName);
        Assert.Contains(snapshot.Texts, text => text.Contains("v2.0.0", StringComparison.Ordinal));
        Assert.Contains(snapshot.Texts, text => text == "Back");

        foreach (var radius in snapshot.ButtonRadii)
        {
            Assert.True(radius is 6 or 7, $"unexpected button radius {radius}");
        }
    }

    [Fact]
    public async Task PluginPage_RendersEveryDeclaredControlType()
    {
        var outcome = await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var values = new InMemoryPluginSettingsValues();
            values.Set("sample", "sample.status", System.Text.Json.JsonSerializer.SerializeToElement("Connected"));
            values.Set("sample", "sample.info", System.Text.Json.JsonSerializer.SerializeToElement("1.4.0"));
            var page = new SettingsPageContribution
            {
                Id = "sample.page",
                Title = LocalizedString.From("Sample page"),
                Controls =
                [
                    new SettingControlContribution
                    {
                        Type = SettingControlType.Toggle, Key = "sample.toggle", Title = LocalizedString.From("Toggle"),
                    },
                    new SettingControlContribution
                    {
                        Type = SettingControlType.Select, Key = "sample.select", Title = LocalizedString.From("Select"),
                        Options =
                        [
                            new SettingOptionContribution { Value = "a", Label = LocalizedString.From("A") },
                            new SettingOptionContribution { Value = "b", Label = LocalizedString.From("B") },
                            new SettingOptionContribution { Value = "c", Label = LocalizedString.From("C") },
                        ],
                    },
                    new SettingControlContribution
                    {
                        Type = SettingControlType.Slider, Key = "sample.slider", Title = LocalizedString.From("Slider"),
                        Min = 0, Max = 100, Step = 10,
                    },
                    new SettingControlContribution
                    {
                        Type = SettingControlType.Number, Key = "sample.number", Title = LocalizedString.From("Number"),
                    },
                    new SettingControlContribution
                    {
                        Type = SettingControlType.Text, Key = "sample.text", Title = LocalizedString.From("Text"),
                    },
                    new SettingControlContribution
                    {
                        Type = SettingControlType.Path, Key = "sample.path", Title = LocalizedString.From("Path"),
                    },
                    new SettingControlContribution
                    {
                        Type = SettingControlType.Secret, Key = "sample.secret", Title = LocalizedString.From("Secret"),
                    },
                    new SettingControlContribution
                    {
                        Type = SettingControlType.Button, Key = "sample.button", Title = LocalizedString.From("Button"),
                        Command = "sample.run",
                    },
                    new SettingControlContribution
                    {
                        Type = SettingControlType.Link, Key = "sample.link", Title = LocalizedString.From("Link"),
                        Default = System.Text.Json.JsonSerializer.SerializeToElement("https://example.invalid"),
                    },
                    new SettingControlContribution
                    {
                        Type = SettingControlType.Info, Key = "sample.info", Title = LocalizedString.From("Info"),
                    },
                    new SettingControlContribution
                    {
                        Type = SettingControlType.Status, Key = "sample.status", Title = LocalizedString.From("Status"),
                        Format = SettingStatusFormat.Text,
                    },
                ],
            };
            var content = PluginSettingsPageRenderer.BuildPage(
                page, "sample", localizer, values, static (_, _) => { }, static _ => { }, PaletteFor("en-US"));
            var host = new Border { Width = 620, Child = content };
            var window = new Window { Content = host, Width = 900, Height = 620 };
            window.Show();
            var descendants = host.GetVisualDescendants().ToList();
            var result = new PageSnapshot(
                descendants.OfType<SettingsSwitch>().Count(),
                descendants.OfType<ToggleSwitch>().Count(),
                descendants.OfType<ComboBox>().Single().ItemCount,
                descendants.OfType<SettingsSlider>().Count(),
                descendants.OfType<Slider>().Count(),
                descendants.OfType<TextBox>().Count(),
                descendants.OfType<TextBox>().Count(box => box.IsReadOnly),
                descendants.OfType<TextBox>().Count(box => (box.Text ?? string.Empty) == "••••••••"),
                descendants.OfType<Border>()
                    .Count(border => border.BorderThickness.Bottom > 0
                        && Math.Abs(border.MinHeight - 53) < 0.1),
                descendants.OfType<TextBlock>().Select(text => text.Text ?? string.Empty).ToList());
            window.Close();
            return result;
        });

        Assert.Equal(1, outcome.Switches);
        Assert.Equal(0, outcome.FluentToggles);
        Assert.Equal(3, outcome.SelectItems);
        Assert.Equal(1, outcome.Sliders);
        Assert.Equal(0, outcome.FluentSliders);
        Assert.Equal(5, outcome.TextBoxes); // number, text, path, secret (+ search-less page)
        Assert.Equal(2, outcome.ReadonlyBoxes); // path + secret
        Assert.Equal(1, outcome.MaskedBoxes);
        Assert.Equal(11, outcome.Rows);
        Assert.Contains(outcome.Texts, text => text == "1.4.0");
        Assert.Contains(outcome.Texts, text => text == "Connected");
    }

    [Fact]
    public async Task PluginPage_WritesValuesBackToTheHostStore()
    {
        var outcome = await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var values = new InMemoryPluginSettingsValues();
            var commands = new List<string>();
            var page = new SettingsPageContribution
            {
                Id = "sample.page",
                Controls =
                [
                    new SettingControlContribution
                    {
                        Type = SettingControlType.Toggle, Key = "sample.toggle", Title = LocalizedString.From("Toggle"),
                    },
                    new SettingControlContribution
                    {
                        Type = SettingControlType.Select, Key = "sample.select", Title = LocalizedString.From("Select"),
                        Options =
                        [
                            new SettingOptionContribution { Value = "a", Label = LocalizedString.From("A") },
                            new SettingOptionContribution { Value = "b", Label = LocalizedString.From("B") },
                        ],
                    },
                    new SettingControlContribution
                    {
                        Type = SettingControlType.Slider, Key = "sample.slider", Title = LocalizedString.From("Slider"),
                        Min = 0, Max = 100, Step = 5,
                    },
                    new SettingControlContribution
                    {
                        Type = SettingControlType.Button, Key = "sample.button", Title = LocalizedString.From("Button"),
                        Command = "sample.run",
                    },
                ],
            };
            var content = PluginSettingsPageRenderer.BuildPage(
                page, "sample", localizer, values, (_, command) => commands.Add(command), static _ => { },
                PaletteFor("en-US"));
            var window = new Window { Content = new Border { Width = 620, Child = content }, Width = 900, Height = 620 };
            window.Show();

            // Slider (pointer drag) + select write through the host store like before the redo.
            var slider = content.GetVisualDescendants().OfType<SettingsSlider>().Single();
            var trackPoint = slider.TranslatePoint(
                new Point(slider.Bounds.Width * 0.8, slider.Bounds.Height / 2), window)!.Value;
            window.MouseMove(trackPoint, Avalonia.Input.RawInputModifiers.None);
            window.MouseDown(trackPoint, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None);
            window.MouseUp(trackPoint, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None);
            content.GetVisualDescendants().OfType<ComboBox>().Single().SelectedIndex = 1;

            // The button row still invokes the declared command.
            var run = content.GetVisualDescendants().OfType<Button>()
                .Single(button => button.GetVisualDescendants().OfType<TextBlock>()
                    .Any(text => text.Text == "Run"));
            run.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

            var toggle = content.GetVisualDescendants().OfType<SettingsSwitch>().Single();
            var center = toggle.TranslatePoint(new Point(toggle.Bounds.Width / 2, toggle.Bounds.Height / 2), window)!.Value;
            window.MouseMove(center, Avalonia.Input.RawInputModifiers.None);
            window.MouseDown(center, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None);
            window.MouseUp(center, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None);

            var snapshot = (
                Slider: values.Get("sample", "sample.slider")?.GetDouble(),
                Select: values.Get("sample", "sample.select")?.GetString(),
                Toggle: values.Get("sample", "sample.toggle")?.GetBoolean(),
                Commands: commands.ToArray());
            window.Close();
            return snapshot;
        });

        Assert.NotNull(outcome.Slider);
        Assert.InRange(outcome.Slider!.Value, 70, 90);
        Assert.Equal("b", outcome.Select);
        Assert.True(outcome.Toggle);
        Assert.Equal(["sample.run"], outcome.Commands);
    }

    [Fact]
    public async Task PluginPage_WithoutControls_ShowsTheUnifiedEmptyState()
    {
        var texts = await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var values = new InMemoryPluginSettingsValues();
            var page = new SettingsPageContribution { Id = "empty.page" };
            var content = PluginSettingsPageRenderer.BuildPage(
                page, "empty", localizer, values, static (_, _) => { }, static _ => { }, PaletteFor("en-US"));
            var host = new Border { Width = 620, Child = content };
            var window = new Window { Content = host, Width = 900, Height = 620 };
            window.Show();
            var texts = host.GetVisualDescendants().OfType<TextBlock>()
                .Select(text => text.Text ?? string.Empty)
                .ToList();
            window.Close();
            return texts;
        });

        Assert.Contains(texts, text => text == "This plugin has no settings yet.");
    }

    private sealed record PaneSnapshot(
        int ButtonCount,
        double[] ButtonRadii,
        double[] ButtonHeights,
        int FluentToggles,
        int FluentCheckBoxes,
        int FluentRadios,
        int FluentSliders,
        int SettingsSliders,
        int FluentCombos);

    private sealed record DetailSnapshot(
        int Switches,
        int FluentToggles,
        int FluentCombos,
        int FluentSliders,
        int Rows,
        int LabelledButtons,
        string PluginName,
        List<string> Texts,
        double[] ButtonRadii);

    private sealed record PageSnapshot(
        int Switches,
        int FluentToggles,
        int SelectItems,
        int Sliders,
        int FluentSliders,
        int TextBoxes,
        int ReadonlyBoxes,
        int MaskedBoxes,
        int Rows,
        List<string> Texts);
}
