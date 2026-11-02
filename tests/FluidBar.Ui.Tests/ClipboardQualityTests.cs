using System.Collections.Immutable;
using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using FluidBar.App.Hosting.Skia;
using FluidBar.App.Settings;
using FluidBar.Core.Diagnostics;
using FluidBar.Platform.Windows.Clipboard;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Ui.Tests;

public sealed class ClipboardQualityTests
{
    [Fact]
    public void File_summary_prioritizes_names_while_preserving_the_original_copy_paths()
    {
        var entry = Text(null) with { Kind = ClipboardContentKind.Files,
            FilePaths = ImmutableArray.Create(@"C:\projects\long-directory\设计稿.pdf", @"D:\Music\Video Project 1.mp3") };
        var method = typeof(SkiaPlatformActions).GetMethod("ClipboardPreview", BindingFlags.Static | BindingFlags.NonPublic)!;
        var summary = Assert.IsType<string>(method.Invoke(null, [entry]));
        Assert.Contains("设计稿.pdf", summary);
        Assert.Contains("Video Project 1.mp3", summary);
        Assert.DoesNotContain("long-directory", summary);
        Assert.Equal(@"C:\projects\long-directory\设计稿.pdf", entry.FilePaths[0]);
    }

    [Fact]
    public void Preview_has_a_separate_action_and_row_click_still_copies()
    {
        Assert.True(Enum.TryParse<FluidBar.Island.IslandPageControl>("ClipboardPreview", out var preview));
        var state = new FluidBar.Island.IslandRenderState
        {
            Pages = new FluidBar.Island.ExpandedPageContent
            {
                Clipboard = new FluidBar.Island.ClipboardPageModel { Items = [new() { Preview = "original" }] },
            },
        };
        var previewAction = SkiaIslandControlRouter.Map(new(FluidBar.Island.IslandHitKind.PageControl, 3, preview, 0), state, 0);
        Assert.NotNull(previewAction);
        Assert.Equal("ClipboardPreview", previewAction.Value.Kind.ToString());
        var rowAction = SkiaIslandControlRouter.Map(new(FluidBar.Island.IslandHitKind.PageControl, 3,
            FluidBar.Island.IslandPageControl.Row, 0), state, 0);
        Assert.Equal(IslandPlatformActionKind.ClipboardCopy, rowAction!.Value.Kind);
    }

    [Fact]
    public async Task Preview_exposes_selectable_original_text_copy_button_and_escape_close()
    {
        await AvaloniaTest.Run(() =>
        {
            var copies = 0;
            var original = "first line\n" + new string('x', 20_000) + "\nlast line";
            var entry = Text(original[..16_384]) with { OriginalTextLength = original.Length, FullText = original };
            var window = Preview(entry, () => copies++);
            try
            {
                window.Show();
                var grid = Assert.IsType<Grid>(window.Content);
                var text = Assert.Single(grid.Children.OfType<TextBox>());
                Assert.True(text.IsReadOnly);
                Assert.Equal(original, text.Text);
                text.SelectAll();
                Assert.Equal(original.Length, text.SelectionEnd - text.SelectionStart);
                var footer = Assert.Single(grid.Children.OfType<StackPanel>());
                var copy = footer.Children.OfType<Button>().Single(button => Equals(button.Content, "Copy"));
                Assert.Equal("Copy", AutomationProperties.GetName(copy));
                copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, copies);
                window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                Assert.False(window.IsVisible);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task Image_preview_fits_the_available_space()
    {
        await AvaloniaTest.Run(() =>
        {
            var entry = Text(null) with
            {
                Kind = ClipboardContentKind.Image,
                ImagePng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aRXsAAAAASUVORK5CYII="),
            };
            var window = Preview(entry, () => { });
            try
            {
                var image = Assert.Single(Assert.IsType<Grid>(window.Content).Children.OfType<Image>());
                Assert.NotNull(image.Source);
                Assert.Equal(Stretch.Uniform, image.Stretch);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task Incomplete_preview_warns_and_disables_copy()
    {
        await AvaloniaTest.Run(() =>
        {
            var entry = Text("preview") with { OriginalTextLength = 2_000_000, TextDigest = "original-digest" };
            var window = Preview(entry, () => throw new InvalidOperationException("Incomplete text must not be copied."));
            try
            {
                var grid = Assert.IsType<Grid>(window.Content);
                Assert.Contains(grid.Children.OfType<TextBlock>(), text => text.Text == "Only a preview was retained.");
                var footer = Assert.Single(grid.Children.OfType<StackPanel>());
                Assert.False(footer.Children.OfType<Button>().Single(button => Equals(button.Content, "Copy")).IsEnabled);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task Incomplete_text_with_the_same_preview_has_distinct_pin_identity()
    {
        var store = new InMemoryUiSettingsStore(AppSettings.Default);
        using var settings = new SettingsController(store.LastSaved, store, NullAppLogger.Instance);
        await using var actions = new SkiaPlatformActions(settings, null, null, null,
            clipboardPinsPath: Path.Combine(Path.GetTempPath(), $"fluidbar-clipboard-quality-{Guid.NewGuid():N}.json"));
        var first = Text("shared prefix") with { OriginalTextLength = 2_000_000, TextDigest = "first-digest" };
        var second = first with { Sequence = 2, TextDigest = "second-digest" };
        var method = typeof(SkiaPlatformActions).GetMethod("PinKey", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.NotEqual(method.Invoke(actions, [first]), method.Invoke(actions, [second]));
    }

    [Fact]
    public async Task Privacy_mask_changes_only_display_and_keeps_search_on_original_payload()
    {
        var store = new InMemoryUiSettingsStore(AppSettings.Default);
        using var settings = new SettingsController(store.LastSaved, store, NullAppLogger.Instance);
        var property = typeof(AppSettings).GetProperty("ClipboardHidePreviews");
        Assert.NotNull(property);
        property!.SetValue(settings.Current, true);
        await using var actions = new SkiaPlatformActions(settings, null, null, null,
            clipboardPinsPath: Path.Combine(Path.GetTempPath(), $"fluidbar-clipboard-quality-{Guid.NewGuid():N}.json"));
        var entry = Text("private original payload");
        var method = typeof(SkiaPlatformActions).GetMethod("MapClipboard", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var mapped = Assert.IsType<FluidBar.App.State.ClipboardItemData>(method.Invoke(actions, [entry]));
        Assert.DoesNotContain("private", mapped.Preview, StringComparison.Ordinal);
        Assert.True(SkiaPlatformActions.MatchesClipboardSearch(entry, "private original"));
        Assert.Equal("private original payload", entry.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Clear_preserves_pinned_rows_unless_explicitly_including_them(bool includePinned)
    {
        var pinsPath = Path.Combine(Path.GetTempPath(), $"fluidbar-clipboard-quality-{Guid.NewGuid():N}.json");
        try
        {
            var store = new InMemoryUiSettingsStore(AppSettings.Default);
            using var settings = new SettingsController(store.LastSaved, store, NullAppLogger.Instance);
            await using var actions = new SkiaPlatformActions(settings, null, null, null, clipboardPinsPath: pinsPath);
            var history = new ClipboardHistory();
            var pinned = Text("pinned original");
            history.TryAdd(pinned, out _);
            history.TryAdd(Text("ordinary") with { Sequence = 2 }, out var snapshot);
            var service = (ClipboardService)typeof(SkiaPlatformActions).GetField("_clipboard", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(actions)!;
            typeof(ClipboardService).GetField("_history", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(service, history);
            typeof(PlatformMonitor<ClipboardSnapshot>).GetMethod("Publish", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, [snapshot]);
            await WaitUntilAsync(() => service.GetHistory().Count == 2);
            var pins = (HashSet<string>)typeof(SkiaPlatformActions).GetField("_pinnedClipboard", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(actions)!;
            var pinKey = (string)typeof(SkiaPlatformActions).GetMethod("PinKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(actions, [pinned])!;
            pins.Add(pinKey);

            Assert.True(Enum.TryParse<IslandPlatformActionKind>(includePinned ? "ClipboardClearIncludingPinned" : "ClipboardClearAll", out var kind));
            actions.Invoke(new IslandPlatformAction(kind));

            await WaitUntilAsync(() => service.GetHistory().Count == (includePinned ? 0 : 1));
            Assert.Equal(includePinned ? 0 : 1, service.GetHistory().Count);
            Assert.Equal(!includePinned, pins.Contains(pinKey));
        }
        finally { File.Delete(pinsPath); }
    }

    private static ClipboardEntry Text(string? value)
        => new(1, DateTimeOffset.UnixEpoch, ClipboardContentKind.Text, value, value?.Length ?? 0, ImmutableArray<string>.Empty);

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!predicate() && DateTime.UtcNow < deadline) { await Task.Delay(10); }
        Assert.True(predicate());
    }

    private static Window Preview(ClipboardEntry entry, Action copy)
    {
        var type = typeof(SkiaPlatformActions).Assembly.GetType("FluidBar.App.Views.ClipboardPreviewWindow");
        Assert.NotNull(type);
        return Assert.IsAssignableFrom<Window>(Activator.CreateInstance(type!, entry, copy,
            "Preview", "Copy", "Close", "Image unavailable", "Only a preview was retained."));
    }
}
