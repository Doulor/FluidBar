using System.Collections.Immutable;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Update;

namespace FluidBar.Platform.Tests;

/// <summary>
/// End-to-end update-service tests over a real local folder feed: a real JSON index on disk, a real
/// asset file, a real SHA-256 and the real download → verify → install-gate pipeline. No network and
/// no fabricated "up to date" states.
/// </summary>
public sealed class UpdateServiceTests
{
    private sealed record Feed(string Root, string IndexPath, string ArtifactPath, string Sha256, byte[] Payload);

    private sealed class FakeLauncher : IUpdateInstallerLauncher
    {
        public string? LaunchedPath { get; private set; }

        public bool Result { get; set; } = true;

        public bool Launch(string installerPath)
        {
            LaunchedPath = installerPath;
            return Result;
        }
    }

    private sealed class FakeSignatureVerifier : IBinarySignatureVerifier
    {
        public AuthenticodeStatus Status { get; set; } = AuthenticodeStatus.NoSignature;

        public AuthenticodeStatus VerifyFile(string filePath) => Status;
    }

    private static async Task<Feed> CreateFeedAsync(string version = "2.1.0", byte[]? payload = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "FluidBarTests", "update-feed", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var bytes = payload ?? [0x4D, 0x5A, 0x01, 0x02, 0x03, 0x04, 0x05];
        var artifact = Path.Combine(root, $"FluidBar-{version}-setup.exe");
        await File.WriteAllBytesAsync(artifact, bytes);
        var sha = await Sha256UpdateVerifier.ComputeFileSha256Async(artifact, CancellationToken.None);

        var index = Path.Combine(root, "index.json");
        var json = $$"""
        [
          {
            "tag_name": "v{{version}}",
            "name": "{{version}}",
            "body": "notes",
            "prerelease": false,
            "html_url": "https://example.test/releases/{{version}}",
            "published_at": "2026-10-20T00:00:00Z",
            "assets": [
              {
                "name": "FluidBar-{{version}}-setup.exe",
                "browser_download_url": "{{new Uri(artifact).AbsoluteUri}}",
                "size": {{bytes.Length}},
                "digest": "sha256:{{sha}}"
              }
            ]
          }
        ]
        """;
        await File.WriteAllTextAsync(index, json);
        return new Feed(root, index, artifact, sha, bytes);
    }

    private static UpdateService CreateService(Feed feed, string autoUpdate = "notify",
        IUpdatePackageVerifier? verifier = null, IUpdateInstallerLauncher? launcher = null,
        TimeProvider? time = null, Version? current = null, CapturingLogger? logger = null)
        => new(
            new UpdateServiceOptions
            {
                FeedUrl = new Uri(feed.IndexPath),
                DownloadDirectory = Path.Combine(feed.Root, "downloads"),
                CurrentVersion = current ?? new Version(1, 0, 0),
                AutoUpdate = autoUpdate,
                StartupDelay = TimeSpan.FromSeconds(5),
                CheckInterval = TimeSpan.FromHours(24),
            },
            logger ?? new CapturingLogger(),
            new PlatformCounters(),
            time,
            verifier ?? new Sha256UpdateVerifier(),
            launcher);

    private static async Task WaitUntilAsync(Func<bool> condition, string because, int timeoutMs = 8000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.True(condition(), because);
    }

    [Fact]
    public async Task LocalFeed_CheckDownloadVerify_ReachesReadyToInstall_AndRestartPrompt()
    {
        var feed = await CreateFeedAsync();
        var launcher = new FakeLauncher();
        await using var service = CreateService(feed, launcher: launcher);
        await service.StartAsync();
        service.Stop();

        var check = await service.CheckNowAsync();
        Assert.Equal(UpdateCheckStatus.UpdateAvailable, check.Status);
        Assert.Equal(new Version(2, 1, 0), check.Latest!.Version.ToVersion());
        await WaitUntilAsync(() => service.Current.State == UpdateState.Available,
            "the check must publish the available state");

        var download = await service.DownloadCurrentAsync();
        Assert.True(download.Success, download.FailureReason);
        // The local feed path uses the SHA-256-only verifier, which reports hash-only verification.
        Assert.Equal(UpdateVerificationStatus.VerifiedByHashOnly, download.Verification);
        Assert.True(File.Exists(download.FilePath));
        Assert.Equal(feed.Sha256, download.Sha256Hex);
        await WaitUntilAsync(() => service.Current.State == UpdateState.ReadyToInstall,
            "a verified download must publish ready-to-install");
        Assert.True(service.CanInstall);
        Assert.False(File.Exists(download.FilePath + ".part"));

        // The downloaded bytes are the real artifact, byte for byte.
        Assert.Equal(feed.Payload, await File.ReadAllBytesAsync(download.FilePath!));

        var install = service.Install();
        Assert.True(install.Launched, install.FailureReason);
        await WaitUntilAsync(() => service.Current.State == UpdateState.RestartRequired,
            "a launched installer must publish the restart-required state");
        Assert.Equal(download.FilePath, launcher.LaunchedPath);
        Assert.Equal(UpdateState.RestartRequired, service.Current.State);
    }

    [Fact]
    public async Task LocalFeed_UpToDateWhenCurrentVersionIsNewer()
    {
        var feed = await CreateFeedAsync();
        await using var service = CreateService(feed, current: new Version(2, 2, 0));
        var check = await service.CheckNowAsync();
        Assert.Equal(UpdateCheckStatus.UpToDate, check.Status);
        await WaitUntilAsync(() => service.Current.State == UpdateState.UpToDate,
            "an older feed must publish up-to-date");
        Assert.False(service.CanInstall);
    }

    [Fact]
    public async Task LocalFeed_MissingIndexIsAnExplicitFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), "FluidBarTests", "update-feed", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var feed = new Feed(root, Path.Combine(root, "missing.json"), string.Empty, string.Empty, []);
        await using var service = CreateService(feed);
        var check = await service.CheckNowAsync();
        Assert.Equal(UpdateCheckStatus.Failed, check.Status);
        Assert.Contains("does not exist", check.FailureReason!);
        await WaitUntilAsync(() => service.Current.State == UpdateState.Failed,
            "a missing local feed must publish the failed state");
        Assert.Equal(UpdateFailureKind.Check, service.Current.FailureKind);
    }

    [Fact]
    public async Task LocalFeed_TamperedArtifactFailsHashVerification_AndIsNotInstallable()
    {
        var feed = await CreateFeedAsync();
        // The index still carries the original digest; the bytes on disk changed afterwards.
        await File.WriteAllBytesAsync(feed.ArtifactPath, [0x00, 0x01, 0x02, 0x03]);
        var launcher = new FakeLauncher();
        await using var service = CreateService(feed, launcher: launcher);

        var check = await service.CheckNowAsync();
        Assert.Equal(UpdateCheckStatus.UpdateAvailable, check.Status);
        var download = await service.DownloadCurrentAsync();
        Assert.True(!download.Success && download.Verification == UpdateVerificationStatus.Sha256Mismatch,
            $"expected a hash mismatch, got success={download.Success} verification={download.Verification} reason={download.FailureReason}");
        await WaitUntilAsync(() => service.Current.State == UpdateState.Failed,
            "a hash mismatch must publish the failed state");
        Assert.Equal(UpdateFailureKind.Verification, service.Current.FailureKind);
        Assert.False(service.CanInstall);
        Assert.False(service.Install().Launched);
        Assert.Null(launcher.LaunchedPath);
        Assert.False(Directory.Exists(Path.Combine(feed.Root, "downloads")) && Directory.EnumerateFiles(Path.Combine(feed.Root, "downloads")).Any());
    }

    [Fact]
    public async Task LocalFeed_CancelledDownloadLeavesNoFiles()
    {
        var feed = await CreateFeedAsync();
        await using var service = CreateService(feed);
        await service.CheckNowAsync();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.DownloadCurrentAsync(cancellationToken: cancellation.Token));

        var downloads = Path.Combine(feed.Root, "downloads");
        Assert.False(Directory.Exists(downloads) && Directory.EnumerateFiles(downloads).Any());
        Assert.NotEqual(UpdateState.ReadyToInstall, service.Current.State);
    }

    [Fact]
    public async Task Policy_OffNeverChecksAutomatically()
    {
        var feed = await CreateFeedAsync();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 20, 3, 0, 0, TimeSpan.Zero));
        await using var service = CreateService(feed, autoUpdate: "off", time: time);
        await service.StartAsync();
        time.Advance(TimeSpan.FromSeconds(10));
        await Task.Delay(100);
        Assert.Equal(UpdateState.Idle, service.Current.State);
    }

    [Fact]
    public async Task Policy_AutoChecksAtStartupThenDownloadsInTheBackground()
    {
        var feed = await CreateFeedAsync();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 20, 3, 0, 0, TimeSpan.Zero));
        await using var service = CreateService(feed, autoUpdate: "auto", time: time);
        await service.StartAsync();

        time.Advance(TimeSpan.FromSeconds(6));
        await WaitUntilAsync(() => service.Current.State == UpdateState.ReadyToInstall,
            "the automatic policy should check and download after the startup delay");
        Assert.True(service.CanInstall);
    }

    [Fact]
    public async Task Policy_NotifyChecksButDoesNotDownload()
    {
        var feed = await CreateFeedAsync();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 20, 3, 0, 0, TimeSpan.Zero));
        await using var service = CreateService(feed, autoUpdate: "notify", time: time);
        await service.StartAsync();

        time.Advance(TimeSpan.FromSeconds(6));
        await WaitUntilAsync(() => service.Current.State == UpdateState.Available,
            "notify policy should check but leave the download to the user");
        await Task.Delay(150);
        Assert.Equal(UpdateState.Available, service.Current.State);
        Assert.False(service.CanInstall);
    }

    [Fact]
    public async Task Policy_ChangeAppliesLive_ChannelAndSchedule()
    {
        var feed = await CreateFeedAsync();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 20, 3, 0, 0, TimeSpan.Zero));
        await using var service = CreateService(feed, autoUpdate: "off", time: time);
        await service.StartAsync();
        Assert.Equal(AutoUpdateMode.Off, service.Mode);

        service.ApplyPolicy("notify", "beta");
        Assert.Equal(AutoUpdateMode.Notify, service.Mode);
        Assert.Equal(UpdateChannel.Prerelease, service.Channel);

        time.Advance(TimeSpan.FromSeconds(6));
        await WaitUntilAsync(() => service.Current.State == UpdateState.Available,
            "changing the policy to notify must arm the schedule");
    }

    [Fact]
    public async Task CompositeVerifier_UnsignedInstallerIsNotTrusted()
    {
        var root = Path.Combine(Path.GetTempPath(), "FluidBarTests", "update-verify", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "FluidBar-2.1.0-setup.exe");
        await File.WriteAllBytesAsync(file, [1, 2, 3, 4]);
        var sha = await Sha256UpdateVerifier.ComputeFileSha256Async(file, CancellationToken.None);

        var asset = new UpdateAsset("FluidBar-2.1.0-setup.exe", new Uri(file), 4, sha);
        var release = new UpdateRelease("v2.1.0", SemanticVersion.Parse("2.1.0"), "2.1.0", null, null, false,
            null, [asset], asset, sha);

        var verifier = new CompositeUpdateVerifier(
            new FakeSignatureVerifier { Status = AuthenticodeStatus.NoSignature }, new CapturingLogger());
        var status = await verifier.VerifyAsync(file, release, CancellationToken.None);
        Assert.Equal(UpdateVerificationStatus.SignatureUnsigned, status);

        var trusted = new CompositeUpdateVerifier(
            new FakeSignatureVerifier { Status = AuthenticodeStatus.Verified }, new CapturingLogger());
        Assert.Equal(UpdateVerificationStatus.Verified,
            await trusted.VerifyAsync(file, release, CancellationToken.None));
    }

    [Fact]
    public async Task LocalFeed_MissingAssetPublishesADownloadFailure()
    {
        var feed = await CreateFeedAsync();
        File.Delete(feed.ArtifactPath); // manifest points at a real (now missing) asset
        await using var service = CreateService(feed);

        await service.CheckNowAsync();
        await WaitUntilAsync(() => service.Current.State == UpdateState.Available,
            "the release is still advertised even when the asset disappeared");

        var download = await service.DownloadCurrentAsync();
        Assert.False(download.Success);
        await WaitUntilAsync(() => service.Current.State == UpdateState.Failed,
            "a missing asset must publish the failed state");
        Assert.Equal(UpdateFailureKind.Download, service.Current.FailureKind);
        Assert.Null(service.Current.DownloadedFilePath);
    }

    [Fact]
    public async Task Policy_OffLogsDisabledAndDoesNotScheduleACheck()
    {
        var feed = await CreateFeedAsync();
        var logger = new CapturingLogger();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 20, 3, 0, 0, TimeSpan.Zero));
        await using var service = CreateService(feed, autoUpdate: "off", time: time, logger: logger);
        await service.StartAsync();

        time.Advance(TimeSpan.FromSeconds(6));
        await Task.Delay(150);

        var entries = logger.Entries.ToArray();
        Assert.Contains(entries, entry => entry.EventId == "update.auto-check-disabled");
        Assert.DoesNotContain(entries, entry => entry.EventId == "update.auto-check-armed");
        Assert.DoesNotContain(entries, entry => entry.EventId == "update.check-started");
        Assert.DoesNotContain(entries, entry => entry.EventId == "update.check-finished");
        Assert.Equal(UpdateState.Idle, service.Current.State);
    }

    [Fact]
    public async Task Policy_NotifyLogsTheStartupDelayAndTheScheduledCheck()
    {
        var feed = await CreateFeedAsync();
        var logger = new CapturingLogger();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 20, 3, 0, 0, TimeSpan.Zero));
        await using var service = CreateService(feed, autoUpdate: "notify", time: time, logger: logger);
        await service.StartAsync();

        var armed = Assert.Single(logger.Entries, entry => entry.EventId == "update.auto-check-armed");
        Assert.Equal(5d, Convert.ToDouble(armed.Data!["dueSeconds"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("startup", armed.Data!["reason"]?.ToString());

        time.Advance(TimeSpan.FromSeconds(6));
        await WaitUntilAsync(() => service.Current.State == UpdateState.Available,
            "the delayed check must run and publish the release");

        var started = Assert.Single(logger.Entries, entry => entry.EventId == "update.check-started");
        Assert.Equal("scheduled", started.Data!["trigger"]?.ToString());
        var finished = Assert.Single(logger.Entries, entry => entry.EventId == "update.check-finished");
        Assert.Equal("UpdateAvailable", finished.Data!["status"]?.ToString());
        Assert.Equal("scheduled", finished.Data!["trigger"]?.ToString());
    }

    [Fact]
    public async Task Policy_ChangeLogsAndRearmsLive()
    {
        var feed = await CreateFeedAsync();
        var logger = new CapturingLogger();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 20, 3, 0, 0, TimeSpan.Zero));
        await using var service = CreateService(feed, autoUpdate: "off", time: time, logger: logger);
        await service.StartAsync();

        service.ApplyPolicy("notify", "stable");

        var policy = Assert.Single(logger.Entries, entry => entry.EventId == "update.policy");
        Assert.Equal("Notify", policy.Data!["autoUpdate"]?.ToString());
        var armed = Assert.Single(logger.Entries, entry => entry.EventId == "update.auto-check-armed");
        Assert.Equal("policy-change", armed.Data!["reason"]?.ToString());

        time.Advance(TimeSpan.FromSeconds(6));
        await WaitUntilAsync(() => service.Current.State == UpdateState.Available,
            "the re-armed schedule must run the check");
    }

    [Fact]
    public void SemanticVersion_ToVersionMatchesForInstallerComparison()
    {
        Assert.Equal(new Version(2, 1, 0), SemanticVersion.Parse("2.1.0").ToVersion());
    }
}
