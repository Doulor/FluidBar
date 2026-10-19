using System.Collections.Immutable;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.Views.Market;
using FluidBar.App.Views.Settings;
using FluidBar.Marketplace.Community;
using FluidBar.Platform.Windows.Update;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Prototype-parity guards for the two panes this pass reviewed.
/// <para>
/// About: the update block added with the real update pipeline has to render every service state with
/// the localized copy in both catalogs, an idle service must not leave an empty placeholder line, and
/// the feed description must stay visible as the prototype's muted mono meta line.
/// </para>
/// <para>
/// Marketplace: the structural numbers from docs/rewrite/prototype2/styles.css (122 px featured card
/// with an 18 px inset, 34 px search field, six category chips in prototype order, 112 px / 12 px / 10 px
/// cards in a two-column grid) are asserted against the live view so a refactor cannot drift them.
/// </para>
/// </summary>
public sealed class SettingsAboutAndMarketTests
{
    private static readonly Version InstalledVersion = new(2, 0, 0);
    private const long InstallerBytes = 86_400_000;

    /// <summary>Minimal update service stub: state snapshot only, no network, no files.</summary>
    private sealed class FakeUpdates : IUpdateUiService
    {
        private readonly UpdateStatusSnapshot _snapshot;

        public FakeUpdates(UpdateState state, string? message = null, string feed = FakeFeed)
        {
            FeedDescription = feed;
            CurrentVersion = InstalledVersion;
            var release = new UpdateRelease(
                "v2.1.0",
                SemanticVersion.Parse("2.1.0"),
                "FluidBar 2.1.0",
                null,
                new Uri("https://github.com/Doulor/FluidBar/releases/tag/v2.1.0"),
                false,
                DateTimeOffset.UnixEpoch,
                [],
                new UpdateAsset(
                    "FluidBar-2.1.0-Setup.exe",
                    new Uri("https://example.invalid/FluidBar-2.1.0-Setup.exe"),
                    InstallerBytes,
                    null),
                null);
            _snapshot = state == UpdateState.Idle
                ? UpdateStatusSnapshot.Idle(InstalledVersion)
                : new UpdateStatusSnapshot(state, InstalledVersion,
                    state is UpdateState.UpToDate ? null : release,
                    state is UpdateState.ReadyToInstall or UpdateState.RestartRequired ? @"C:\tmp\setup.exe" : null,
                    state is UpdateState.ReadyToInstall or UpdateState.RestartRequired
                        ? UpdateVerificationStatus.Verified
                        : UpdateVerificationStatus.NotVerified,
                    DateTimeOffset.UnixEpoch,
                    message);
        }

        public const string FakeFeed = "https://api.github.com/repos/Doulor/FluidBar/releases";

        public UpdateStatusSnapshot Current => _snapshot;

        public Version CurrentVersion { get; }

        public bool IsConfigured => true;

        public string FeedDescription { get; }

        public bool IsBusy => _snapshot.State is UpdateState.Checking or UpdateState.Downloading;

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public Task CheckNowAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DownloadCurrentAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public UpdateInstallResult Install() => new(true, _snapshot.DownloadedFilePath);

        public void Discard()
        {
        }
    }

    private static (List<string> Texts, List<Border> Chips) RenderAbout(ILocalizer localizer, IUpdateUiService updates)
    {
        var (controller, _) = TestFixtures.CreateSettings(AppSettings.Default);
        var window = new SettingsWindow(controller, localizer, SettingsWindowSection.About, updates: updates,
            restartToInstall: static () => { });
        window.Show();
        try
        {
            // Only the About pane itself: the window chrome (nav counts) has its own empty labels.
            var pane = window.GetVisualDescendants().OfType<ScrollViewer>()
                .First(scroll => Math.Abs(scroll.Padding.Top - 24) < 0.1);
            var texts = pane.GetVisualDescendants().OfType<TextBlock>()
                .Select(static text => text.Text ?? string.Empty)
                .ToList();
            var chips = pane.GetVisualDescendants().OfType<Border>()
                .Where(static border => border.CornerRadius.TopLeft is >= 6 and <= 8 && border.Height is >= 30 and <= 32)
                .ToList();
            return (texts, chips);
        }
        finally
        {
            window.Close();
        }
    }

    [Theory]
    [InlineData("en-US", UpdateState.Available)]
    [InlineData("zh-Hans", UpdateState.Available)]
    [InlineData("en-US", UpdateState.Downloading)]
    [InlineData("zh-Hans", UpdateState.Downloading)]
    [InlineData("en-US", UpdateState.ReadyToInstall)]
    [InlineData("zh-Hans", UpdateState.ReadyToInstall)]
    [InlineData("en-US", UpdateState.RestartRequired)]
    [InlineData("zh-Hans", UpdateState.RestartRequired)]
    [InlineData("en-US", UpdateState.UpToDate)]
    [InlineData("zh-Hans", UpdateState.UpToDate)]
    [InlineData("en-US", UpdateState.Failed)]
    [InlineData("zh-Hans", UpdateState.Failed)]
    public async Task AboutPane_ShowsTheLocalizedStatusForEveryUpdateState(string culture, UpdateState state)
    {
        var (texts, expected) = await AvaloniaTest.Run(() =>
        {
            var localizer = LocalizationService.CreateDefault(culture, showMissingKeys: false, enablePseudoLocale: false);
            var rendered = RenderAbout(localizer, new FakeUpdates(state, "SHA-256 mismatch"));
            var expectedText = state switch
            {
                UpdateState.Available => localizer.Format(LocKeys.Settings.About.Update.Available,
                    ("version", "2.1.0"), ("size", "82.4 MB")),
                UpdateState.Downloading => localizer.Format(LocKeys.Settings.About.Update.Downloading,
                    ("version", "2.1.0")),
                UpdateState.ReadyToInstall => localizer.Format(LocKeys.Settings.About.Update.Ready,
                    ("version", "2.1.0")),
                UpdateState.RestartRequired => localizer.Get(LocKeys.Settings.About.Update.RestartRequired),
                UpdateState.UpToDate => localizer.Get(LocKeys.Settings.About.UpToDate),
                UpdateState.Failed => localizer.Format(LocKeys.Settings.About.Update.Failed,
                    ("reason", "SHA-256 mismatch")),
                _ => string.Empty,
            };
            return (rendered.Texts, expectedText);
        });

        Assert.False(string.IsNullOrEmpty(expected));
        Assert.Contains(texts, text => string.Equals(text, expected, StringComparison.Ordinal));
        Assert.DoesNotContain(texts, static text => text.Length == 0);
    }

    [Fact]
    public async Task AboutPane_SpeaksBothCatalogs_AndKeepsTheFeedLine()
    {
        var (english, chinese) = await AvaloniaTest.Run(() =>
        {
            var en = LocalizationService.CreateDefault("en-US", showMissingKeys: false, enablePseudoLocale: false);
            var zh = LocalizationService.CreateDefault("zh-Hans", showMissingKeys: false, enablePseudoLocale: false);
            return (RenderAbout(en, new FakeUpdates(UpdateState.Available)),
                RenderAbout(zh, new FakeUpdates(UpdateState.Available)));
        });

        // Status copy differs per catalog, the feed stays a diagnostic string.
        Assert.Contains(english.Texts, text => text.Contains("is available", StringComparison.Ordinal));
        Assert.Contains(chinese.Texts, text => text.Contains("发现新版本", StringComparison.Ordinal));
        Assert.Contains(english.Texts, text => text.Contains(FakeUpdates.FakeFeed, StringComparison.Ordinal));
        Assert.Contains(chinese.Texts, text => text.Contains(FakeUpdates.FakeFeed, StringComparison.Ordinal));

        // Both locales offer the download action, titled by their own catalog.
        Assert.Contains(english.Texts, text => text == "Download update");
        Assert.Contains(chinese.Texts, text => text == "下载更新");
    }

    [Fact]
    public async Task AboutPane_IdleService_LeavesNoEmptyStatusLine()
    {
        var (texts, _) = await AvaloniaTest.Run(() =>
        {
            var localizer = LocalizationService.CreateDefault("en-US", showMissingKeys: false, enablePseudoLocale: false);
            return RenderAbout(localizer, new FakeUpdates(UpdateState.Idle));
        });

        Assert.DoesNotContain(texts, static text => text.Length == 0);
        Assert.Contains(texts, static text => text == "FluidBar");
        Assert.Contains(texts, text => text.Contains(FakeUpdates.FakeFeed, StringComparison.Ordinal));
    }

    private static (SettingsController Controller, ILocalizer Localizer) MarketFixture(string culture)
    {
        var localizer = LocalizationService.CreateDefault(culture, showMissingKeys: false, enablePseudoLocale: false);
        LocalizationRuntime.Current = localizer;
        var (controller, _) = TestFixtures.CreateSettings(AppSettings.Default);
        return (controller, localizer);
    }

    private static string TempRoot()
        => Path.Combine(Path.GetTempPath(), "fluidbar-ui-market-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task MarketplacePane_MatchesThePrototypeStructure()
    {
        var root = TempRoot();
        try
        {
            // Geometry is read inside the headless session; only plain values cross the boundary.
            var snapshot = await AvaloniaTest.RunAsync(async () =>
            {
                var (_, localizer) = MarketFixture("en-US");
                var community = new CommunityMarketplaceService(new CommunityMarketplaceOptions
                {
                    Store = new CommunityMarketplaceStore(Path.Combine(root, "marketplace")),
                });
                var view = new MarketplaceSectionView(
                    new DesignMarketplaceUiService(),
                    localizer,
                    AppSettings.Default.Surfaces,
                    static () => null,
                    "stable",
                    community);
                var window = new Window { Content = view, Width = 900, Height = 620 };
                window.Show();
                await view.ReloadAsync();

                var borders = view.GetVisualDescendants().OfType<Border>().ToList();
                var featured = borders.FirstOrDefault(border => border.MinHeight == 122);
                var cards = borders.Where(border => border.MinHeight == 112).ToList();
                var search = borders.FirstOrDefault(border => border.Height == 34);
                var chips = view.GetVisualDescendants().OfType<StackPanel>()
                    .FirstOrDefault(panel => panel.Children.Count == 6
                        && panel.Children.All(child => child is Border { Height: 26 }))
                    ?.Children.OfType<Border>()
                    .SelectMany(border => border.GetVisualDescendants().OfType<TextBlock>())
                    .Select(text => text.Text)
                    .ToList();
                var gridCount = view.GetVisualDescendants().OfType<Grid>()
                    .Count(grid => grid.ColumnDefinitions.Count == 2);
                var texts = view.GetVisualDescendants().OfType<TextBlock>()
                    .Select(text => text.Text ?? string.Empty)
                    .ToList();
                var placeholder = view.GetVisualDescendants().OfType<TextBox>()
                    .Select(box => box.Watermark)
                    .FirstOrDefault();
                window.Close();

                // The prototype's 18px inset lives on the content border inside the card shell.
                var featuredInset = featured is null
                    ? 0
                    : featured.GetVisualDescendants().OfType<Border>()
                        .Select(border => border.Padding.Top)
                        .FirstOrDefault(padding => padding == 18);

                return new MarketSnapshot(
                    featured?.MinHeight ?? 0,
                    featuredInset,
                    featured?.CornerRadius.TopLeft ?? 0,
                    search?.Height ?? 0,
                    placeholder,
                    chips,
                    cards.Count,
                    cards.FirstOrDefault()?.Padding.Top ?? 0,
                    cards.FirstOrDefault()?.CornerRadius.TopLeft ?? 0,
                    gridCount,
                    texts);
            });

            // .market-featured { min-height: 122px; padding: 18px; border-radius: 14px }
            Assert.Equal(122, snapshot.FeaturedMinHeight);
            Assert.Equal(18, snapshot.FeaturedPadding);
            Assert.Equal(14, snapshot.FeaturedRadius);

            // .search-field { height: 34px } with the prototype placeholder
            Assert.Equal(34, snapshot.SearchHeight);
            Assert.Equal("Search plugins, authors or capabilities", snapshot.SearchPlaceholder);

            // .market-cats: 全部 · 效率 · 媒体 · 开发 · 系统 · AI (prototype order, one chip per category)
            Assert.Equal(
                ["All", "Productivity", "Media", "Developer", "System", "AI"],
                snapshot.Chips);

            // .market-card { min-height: 112px; padding: 12px; border-radius: 10px } in a two-column grid
            Assert.Equal(5, snapshot.CardCount);
            Assert.Equal(12, snapshot.CardPadding);
            Assert.Equal(10, snapshot.CardRadius);
            Assert.True(snapshot.TwoColumnGrids > 0, "the card grid must have two columns");

            // .market-footnote keeps the shield glyph in front of the copy.
            Assert.Contains(snapshot.Texts,
                text => text.StartsWith("Only signed plugins", StringComparison.Ordinal));
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (IOException)
            {
                // best-effort temp cleanup
            }
        }
    }
    /// <summary>Geometry read from the live market view (plain values only: Avalonia objects are thread-affine).</summary>
    private sealed record MarketSnapshot(
        double FeaturedMinHeight,
        double FeaturedPadding,
        double FeaturedRadius,
        double SearchHeight,
        string? SearchPlaceholder,
        List<string?>? Chips,
        int CardCount,
        double CardPadding,
        double CardRadius,
        int TwoColumnGrids,
        List<string> Texts);
}
