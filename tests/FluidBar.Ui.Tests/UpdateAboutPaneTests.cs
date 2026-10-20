using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.Views.Settings;
using FluidBar.Platform.Windows.Update;

namespace FluidBar.Ui.Tests;

/// <summary>
/// UX contract for the update status the About pane shows for each updater state: the text must be
/// specific (check vs download vs installer verification), localized, and the action buttons must
/// exist with the right labels. The fake service mirrors the real snapshot types, so this also guards
/// the failure taxonomy introduced for task-20.
/// </summary>
public sealed class UpdateAboutPaneTests
{
    private sealed class FakeUpdateUiService : IUpdateUiService
    {
        public UpdateStatusSnapshot Current { get; set; } =
            UpdateStatusSnapshot.Idle(new Version(1, 0, 0));

        public Version CurrentVersion { get; set; } = new(1, 0, 0);

        public bool IsConfigured { get; set; } = true;

        public string FeedDescription { get; set; } = "file:///tmp/index.json";

        public bool IsBusy { get; set; }

        public double DownloadProgress { get; set; } = -1;

        public event EventHandler? Changed;

        public List<string> Calls { get; } = [];

        public Task CheckNowAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add("check");
            return Task.CompletedTask;
        }

        public Task DownloadCurrentAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add("download");
            return Task.CompletedTask;
        }

        public UpdateInstallResult Install()
        {
            Calls.Add("install");
            return new UpdateInstallResult(true, "x");
        }

        public void Discard() => Calls.Add("discard");
    }

    private static UpdateRelease Release(string version = "2.1.0")
    {
        var asset = new UpdateAsset($"FluidBar-{version}-setup.exe",
            new Uri($"file:///tmp/FluidBar-{version}-setup.exe"), 180224, new string('a', 64));
        return new UpdateRelease($"v{version}", SemanticVersion.Parse(version), version, null, null, false,
            null, [asset], asset, new string('a', 64));
    }

    private static UpdateStatusSnapshot Snapshot(UpdateState state, UpdateFailureKind failure = UpdateFailureKind.None,
        string? message = null)
        => new(state, new Version(1, 0, 0), Release(),
            state is UpdateState.ReadyToInstall or UpdateState.Downloading ? "/tmp/FluidBar-2.1.0-setup.exe" : null,
            UpdateVerificationStatus.NotVerified, DateTimeOffset.UtcNow, message, failure);

    private static void WithAboutWindow(ILocalizer localizer, FakeUpdateUiService updates,
        Action<SettingsWindow> body, AppSettings? settings = null, Action? restart = null)
    {
        var (controller, _) = TestFixtures.CreateSettings(settings ?? AppSettings.Default);
        var window = new SettingsWindow(controller, localizer, SettingsWindowSection.About,
            updates: updates, restartToInstall: restart);
        window.Show();
        try
        {
            body(window);
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertHasText(SettingsWindow window, string expected)
    {
        var texts = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(text => text.Text)
            .Where(text => !string.IsNullOrEmpty(text))
            .Select(text => text!)
            .ToArray();
        Assert.True(texts.Contains(expected, StringComparer.Ordinal),
            $"expected status '{expected}' not found; pane texts: {string.Join(" | ", texts)}");
    }

    private static void ClickChip(SettingsWindow window, string label)
    {
        // Pane actions are real buttons (one control layer): click the button that owns the label.
        var text = window.GetVisualDescendants().OfType<TextBlock>().First(block => block.Text == label);
        var chip = text.GetVisualAncestors().OfType<Button>().First();
        var center = chip.TranslatePoint(new Point(chip.Bounds.Width / 2, chip.Bounds.Height / 2), window)!.Value;
        window.MouseMove(center, RawInputModifiers.None);
        window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(center, MouseButton.Left, RawInputModifiers.None);
    }

    [Fact]
    public async Task AboutUpdateStatus_ShowsSpecificTextForEveryState()
    {
        await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var updates = new FakeUpdateUiService();

            updates.Current = Snapshot(UpdateState.Checking);
            WithAboutWindow(localizer, updates, window =>
                AssertHasText(window, localizer.Get(LocKeys.Settings.About.Update.Checking)));

            updates.Current = Snapshot(UpdateState.UpToDate);
            WithAboutWindow(localizer, updates, window =>
                AssertHasText(window, localizer.Get(LocKeys.Settings.About.UpToDate)));

            updates.Current = Snapshot(UpdateState.Available);
            WithAboutWindow(localizer, updates, window =>
                AssertHasText(window, localizer.Format(LocKeys.Settings.About.Update.Available,
                    ("version", "2.1.0"), ("size", "176 KB"))));

            updates.Current = Snapshot(UpdateState.Downloading);
            WithAboutWindow(localizer, updates, window =>
                AssertHasText(window, localizer.Format(LocKeys.Settings.About.Update.Downloading,
                    ("version", "2.1.0"))));

            updates.Current = Snapshot(UpdateState.Downloading);
            updates.DownloadProgress = 0.42;
            WithAboutWindow(localizer, updates, window =>
                AssertHasText(window, localizer.Format(LocKeys.Settings.About.Update.DownloadingProgress,
                    ("version", "2.1.0"), ("percent", 42))));

            updates.DownloadProgress = -1;
            updates.Current = Snapshot(UpdateState.ReadyToInstall);
            WithAboutWindow(localizer, updates, window =>
                AssertHasText(window, localizer.Format(LocKeys.Settings.About.Update.Ready,
                    ("version", "2.1.0"))));

            updates.Current = Snapshot(UpdateState.RestartRequired);
            WithAboutWindow(localizer, updates, window =>
                AssertHasText(window, localizer.Get(LocKeys.Settings.About.Update.RestartRequired)));

            updates.Current = Snapshot(UpdateState.Failed, UpdateFailureKind.Check, "No usable release found.");
            WithAboutWindow(localizer, updates, window =>
                AssertHasText(window, localizer.Format(LocKeys.Settings.About.Update.Failed,
                    ("reason", "No usable release found."))));

            updates.Current = Snapshot(UpdateState.Failed, UpdateFailureKind.Download,
                "Installer download returned HTTP 404.");
            WithAboutWindow(localizer, updates, window =>
                AssertHasText(window, localizer.Format(LocKeys.Settings.About.Update.DownloadFailed,
                    ("reason", "Installer download returned HTTP 404."))));

            updates.Current = Snapshot(UpdateState.Failed, UpdateFailureKind.Verification,
                "Installer verification failed (Sha256Mismatch).");
            WithAboutWindow(localizer, updates, window =>
                AssertHasText(window, localizer.Get(LocKeys.Settings.About.Update.VerificationFailed)));
        });
    }

    [Fact]
    public async Task AboutUpdateStatus_IsLocalizedInChinese()
    {
        await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("zh-Hans");
            var updates = new FakeUpdateUiService
            {
                Current = Snapshot(UpdateState.Failed, UpdateFailureKind.Verification),
            };

            WithAboutWindow(localizer, updates, window =>
                AssertHasText(window, localizer.Get(LocKeys.Settings.About.Update.VerificationFailed)));

            updates.Current = Snapshot(UpdateState.Idle);
            WithAboutWindow(localizer, updates, window =>
                    AssertHasText(window, localizer.Get(LocKeys.Settings.About.Update.Off)),
                AppSettings.Default with { AutoUpdate = "off" });

            updates.Current = Snapshot(UpdateState.Available);
            WithAboutWindow(localizer, updates, window =>
                AssertHasText(window, localizer.Format(LocKeys.Settings.About.Update.Available,
                    ("version", "2.1.0"), ("size", "176 KB"))));
        });
    }

    [Fact]
    public async Task AboutUpdateStatus_IdleWithAutomaticChecksOnHasNoStatusLine()
    {
        await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var updates = new FakeUpdateUiService { Current = Snapshot(UpdateState.Idle) };
            WithAboutWindow(localizer, updates, window =>
            {
                var texts = window.GetVisualDescendants().OfType<TextBlock>()
                    .Select(text => text.Text)
                    .Where(text => !string.IsNullOrEmpty(text))
                    .ToArray();
                Assert.DoesNotContain(localizer.Get(LocKeys.Settings.About.Update.Off), texts);
                Assert.DoesNotContain(localizer.Get(LocKeys.Settings.About.UpToDate), texts);
            });
        });
    }

    [Fact]
    public async Task AboutUpdateButtons_ExposeRetryDownloadAndInstall()
    {
        await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var updates = new FakeUpdateUiService
            {
                Current = Snapshot(UpdateState.Failed, UpdateFailureKind.Verification,
                    "Installer verification failed (Sha256Mismatch)."),
            };

            WithAboutWindow(localizer, updates, window =>
            {
                ClickChip(window, localizer.Get(LocKeys.Settings.About.CheckForUpdates));
                Assert.Contains("check", updates.Calls);
                ClickChip(window, localizer.Get(LocKeys.Settings.About.Update.Download));
                Assert.Contains("download", updates.Calls);
                ClickChip(window, localizer.Get(LocKeys.Settings.About.Update.Retry));
                Assert.Equal(2, updates.Calls.Count(call => call == "check"));
            });

            var restarts = 0;
            updates.Current = Snapshot(UpdateState.ReadyToInstall);
            WithAboutWindow(localizer, updates, window =>
            {
                ClickChip(window, localizer.Get(LocKeys.Settings.About.Update.Install));
                Assert.Equal(1, restarts);
            }, restart: () => restarts++);

            updates.Current = Snapshot(UpdateState.Available);
            WithAboutWindow(localizer, updates, window =>
            {
                ClickChip(window, localizer.Get(LocKeys.Settings.About.Update.Download));
                Assert.Equal(2, updates.Calls.Count(call => call == "download"));
            });
        });
    }
}
