using System.Net;
using System.Text;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Update;

namespace FluidBar.Platform.Tests;

/// <summary>Stub HTTP handler: no sockets, no network — deterministic responses for update-check tests.</summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => _responder = responder;

    public int Calls { get; private set; }

    public List<HttpRequestMessage> Requests { get; } = new();

    public static HttpResponseMessage Json(string json, string? etag = null, HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (etag is not null)
        {
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
        }

        return response;
    }

    public static HttpResponseMessage Bytes(byte[] data, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new ByteArrayContent(data) };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Calls++;
        Requests.Add(request);
        return Task.FromResult(_responder(request));
    }
}

public sealed class UpdateTests
{
    private static readonly string Manifest = """
    [
      {
        "tag_name": "v2.1.0-beta.1",
        "name": "2.1.0 beta",
        "body": "beta notes",
        "draft": false,
        "prerelease": true,
        "html_url": "https://example.test/releases/beta",
        "published_at": "2026-11-01T00:00:00Z",
        "assets": [
          { "name": "FluidBar-2.1.0-beta.1.exe", "browser_download_url": "https://example.test/beta.exe",
            "size": 200, "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" }
        ]
      },
      {
        "tag_name": "v2.0.0",
        "name": "2.0.0",
        "body": "stable notes",
        "draft": false,
        "prerelease": false,
        "html_url": "https://example.test/releases/stable",
        "published_at": "2026-10-01T00:00:00Z",
        "assets": [
          { "name": "notes.txt", "browser_download_url": "https://example.test/notes.txt", "size": 10 },
          { "name": "FluidBar-2.0.0-setup.exe", "browser_download_url": "https://example.test/setup.exe",
            "size": 300, "digest": "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" }
        ]
      }
    ]
    """;

    private sealed class FakeSignatureVerifier : IBinarySignatureVerifier
    {
        public AuthenticodeStatus Status { get; set; } = AuthenticodeStatus.Verified;

        public string? LastPath { get; private set; }

        public AuthenticodeStatus VerifyFile(string filePath)
        {
            LastPath = filePath;
            return Status;
        }
    }

    private static UpdateCheckClient CreateClient(StubHttpMessageHandler handler,
        IUpdatePackageVerifier? verifier = null, Version? current = null, string? downloadDir = null,
        CapturingLogger? logger = null)
    {
        var client = new HttpClient(handler);
        var options = new UpdateClientOptions
        {
            ReleaseFeedUrl = new Uri("https://example.test/releases.json"),
            DownloadDirectory = downloadDir ?? Path.Combine(Path.GetTempPath(), "FluidBarTests", "updates",
                Guid.NewGuid().ToString("N")),
            MaxInstallerBytes = 4096,
        };
        return new UpdateCheckClient(client, options, current ?? new Version(1, 9, 0),
            verifier ?? new StableFakeVerifier(), logger ?? new CapturingLogger(), new PlatformCounters());
    }

    private sealed class StableFakeVerifier : IUpdatePackageVerifier
    {
        public UpdateVerificationStatus Status { get; set; } = UpdateVerificationStatus.Verified;

        public Task<UpdateVerificationStatus> VerifyAsync(string filePath, UpdateRelease release,
            CancellationToken cancellationToken) => Task.FromResult(Status);
    }

    [Theory]
    [InlineData("2.0.0", 2, 0, 0, null)]
    [InlineData("v2.1.3", 2, 1, 3, null)]
    [InlineData("2.1.0-beta.2", 2, 1, 0, "beta.2")]
    [InlineData("2.1.0+build.7", 2, 1, 0, null)]
    [InlineData("1.0", 1, 0, 0, null)]
    public void SemanticVersion_Parses(string text, int major, int minor, int patch, string? prerelease)
    {
        Assert.True(SemanticVersion.TryParse(text, out var version));
        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
        Assert.Equal(prerelease, version.Prerelease);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("v")]
    [InlineData("1.a.0")]
    public void SemanticVersion_RejectsGarbage(string? text)
    {
        Assert.False(SemanticVersion.TryParse(text, out _));
    }

    [Fact]
    public void SemanticVersion_ComparesNumericallyAndStableBeatsPrerelease()
    {
        Assert.True(SemanticVersion.Parse("2.0.0").IsNewerThan(SemanticVersion.Parse("1.9.9")));
        Assert.True(SemanticVersion.Parse("2.1.0").IsNewerThan(SemanticVersion.Parse("2.1.0-beta.1")));
        Assert.False(SemanticVersion.Parse("2.1.0-beta.1").IsNewerThan(SemanticVersion.Parse("2.1.0")));
        Assert.True(SemanticVersion.Parse("2.0.1").IsNewerThan(new Version(2, 0, 0)));
        Assert.False(SemanticVersion.Parse("2.0.0-beta").IsNewerThan(new Version(2, 0, 0)));
    }

    [Fact]
    public void ManifestParser_StableChannelSkipsPrerelease()
    {
        var release = ReleaseManifestParser.SelectRelease(Manifest, UpdateChannel.Stable, @"FluidBar.*\.exe$");
        Assert.NotNull(release);
        Assert.Equal("2.0.0", release!.Version.ToString());
        Assert.Equal("FluidBar-2.0.0-setup.exe", release.Installer!.Name);
        Assert.Equal("bbbb", release.InstallerSha256Hex![..4]);
        Assert.True(release.CanOfferInstaller);
        Assert.Equal("stable notes", release.Notes);
    }

    [Fact]
    public void ManifestParser_PublishedAtKeepsTheFeedOffset()
    {
        // Feed timestamps are ISO-8601; parsing must be culture-invariant and round-trip so the offset
        // in the feed is preserved instead of being reinterpreted by the machine's current culture.
        const string json = """
        [{"tag_name":"v2.0.0","prerelease":false,"assets":[],
          "published_at":"2026-10-01T00:00:00.1234567+02:00"}]
        """;
        var release = ReleaseManifestParser.SelectRelease(json, UpdateChannel.Stable, "x");
        Assert.NotNull(release);
        var expected = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(2)).AddTicks(1234567);
        Assert.Equal(expected, release!.PublishedAt);
        Assert.Equal(TimeSpan.FromHours(2), release.PublishedAt!.Value.Offset);
    }

    [Fact]
    public void ManifestParser_PrereleaseChannelPicksNewest()
    {
        var release = ReleaseManifestParser.SelectRelease(Manifest, UpdateChannel.Prerelease, @"FluidBar.*\.exe$");
        Assert.Equal("2.1.0-beta.1", release!.Version.ToString());
        Assert.True(release.IsPrerelease);
    }

    [Fact]
    public void ManifestParser_NoUsableReleaseReturnsNull()
    {
        Assert.Null(ReleaseManifestParser.SelectRelease("not json", UpdateChannel.Stable, "x"));
        Assert.Null(ReleaseManifestParser.SelectRelease("[]", UpdateChannel.Stable, "x"));
        Assert.Null(ReleaseManifestParser.SelectRelease("""{"tag_name":"v1.0.0"}""", UpdateChannel.Stable, "x"));
    }

    [Fact]
    public void ManifestParser_ReleaseWithoutInstallerCannotOfferInstall()
    {
        const string json = """
        [{"tag_name":"v2.0.0","name":"2.0.0","prerelease":false,"assets":[
            {"name":"notes.txt","browser_download_url":"https://example.test/n.txt","size":5}]}]
        """;
        var release = ReleaseManifestParser.SelectRelease(json, UpdateChannel.Stable, @"FluidBar.*\.exe$");
        Assert.NotNull(release);
        Assert.False(release!.CanOfferInstaller);
        Assert.Null(release.Installer);
    }

    [Fact]
    public void ManifestParser_ExtractsDigestFormats()
    {
        var tagOnly = ReleaseManifestParser.SelectRelease("""[{"tag_name":"v1.0.0"}]""", UpdateChannel.Stable, "x");
        Assert.NotNull(tagOnly);
        Assert.False(tagOnly!.CanOfferInstaller);
        const string json = """
        [{"tag_name":"v3.0.0","prerelease":false,"assets":[
            {"name":"FluidBar-3.0.0.exe","browser_download_url":"https://example.test/a.exe","size":10,
             "digest":"sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"}]}]
        """;
        var release = ReleaseManifestParser.SelectRelease(json, UpdateChannel.Stable, @"FluidBar.*\.exe$");
        Assert.Equal(64, release!.InstallerSha256Hex!.Length);
    }

    [Fact]
    public async Task CheckAsync_UpdateAvailable_AndPublishesState()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(Manifest, "\"etag-1\""));
        await using var client = CreateClient(handler);

        var result = await client.CheckAsync(new UpdateCheckRequest(new Version(1, 9, 0)));
        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal("2.0.0", result.Latest!.Version.ToString());
        Assert.Equal("\"etag-1\"", result.ETag);
        Assert.Equal(UpdateState.Available, client.Current.State);
    }

    [Fact]
    public async Task CheckAsync_UsesEtagForConditionalGet()
    {
        var handler = new StubHttpMessageHandler(request =>
            request.Headers.IfNoneMatch.Count > 0
                ? StubHttpMessageHandler.Json("{}", status: HttpStatusCode.NotModified)
                : StubHttpMessageHandler.Json(Manifest, "\"etag-1\""));
        await using var client = CreateClient(handler);

        var first = await client.CheckAsync(new UpdateCheckRequest(new Version(1, 9, 0)));
        var second = await client.CheckAsync(new UpdateCheckRequest(new Version(1, 9, 0)));

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, first.Status);
        Assert.Equal(UpdateCheckStatus.NotModified, second.Status);
        Assert.Equal(2, handler.Calls);
        Assert.Contains(handler.Requests, request => request.Headers.IfNoneMatch.Count > 0);
    }

    [Fact]
    public async Task CheckAsync_UpToDateWhenCurrentIsNewer()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(Manifest));
        await using var client = CreateClient(handler, current: new Version(2, 0, 0));
        var result = await client.CheckAsync(new UpdateCheckRequest(new Version(2, 0, 0)));
        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.Equal(UpdateState.UpToDate, client.Current.State);
    }

    [Fact]
    public async Task CheckAsync_HttpErrorIsExplicitFailure()
    {
        var handler = new StubHttpMessageHandler(_ =>
            StubHttpMessageHandler.Json("{}", status: HttpStatusCode.InternalServerError));
        await using var client = CreateClient(handler);
        var result = await client.CheckAsync(new UpdateCheckRequest(new Version(1, 0, 0)));
        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.NotNull(result.FailureReason);
        Assert.Equal(UpdateState.Failed, client.Current.State);
    }

    [Fact]
    public async Task CheckAsync_OversizedManifestIsRejected()
    {
        var big = new string('x', 2 * 1024 * 1024);
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json($"[{{\"tag_name\":\"v9.0.0\",\"pad\":\"{big}\"}}]"));
        await using var client = CreateClient(handler);
        var result = await client.CheckAsync(new UpdateCheckRequest(new Version(1, 0, 0)));
        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
    }

    [Fact]
    public async Task DownloadInstaller_VerifiedWritesFile()
    {
        var downloadDir = Path.Combine(Path.GetTempPath(), "FluidBarTests", "updates", Guid.NewGuid().ToString("N"));
        var payload = new byte[] { 1, 2, 3, 4, 5 };
        var hash = await Sha256UpdateVerifier.ComputeFileSha256Async(
            await WriteTempAsync(payload), CancellationToken.None);

        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Bytes(payload));
        await using var client = CreateClient(handler, new StableFakeVerifier(), downloadDir: downloadDir);
        var release = BuildRelease(hash, payload.Length);

        var result = await client.DownloadInstallerAsync(release);
        Assert.True(result.Success);
        Assert.True(result.CanInstall);
        Assert.True(File.Exists(result.FilePath!));
        Assert.Equal(UpdateState.ReadyToInstall, client.Current.State);
        Assert.Equal(1, handler.Calls);

        // Second explicit download uses the verified cache instead of the network.
        var again = await client.DownloadInstallerAsync(release);
        Assert.True(again.Success);
        Assert.Equal(1, handler.Calls);
        Directory.Delete(downloadDir, recursive: true);
    }

    [Fact]
    public async Task DownloadInstaller_VerificationFailureRemovesFileAndBlocksInstall()
    {
        var downloadDir = Path.Combine(Path.GetTempPath(), "FluidBarTests", "updates", Guid.NewGuid().ToString("N"));
        var payload = new byte[] { 9, 9, 9 };
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Bytes(payload));
        var failing = new StableFakeVerifier { Status = UpdateVerificationStatus.Sha256Mismatch };
        await using var client = CreateClient(handler, failing, downloadDir: downloadDir);
        var release = BuildRelease(new string('0', 64), payload.Length);

        var result = await client.DownloadInstallerAsync(release);
        Assert.False(result.Success);
        Assert.False(result.CanInstall);
        Assert.Equal(UpdateVerificationStatus.Sha256Mismatch, result.Verification);
        Assert.False(Directory.Exists(downloadDir) && Directory.GetFiles(downloadDir, "*.exe").Length > 0);
        Assert.Equal(UpdateState.Failed, client.Current.State);
    }

    [Fact]
    public async Task Sha256Verifier_DetectsMatchAndMismatch()
    {
        var path = await WriteTempAsync(new byte[] { 10, 20, 30 });
        try
        {
            var hash = await Sha256UpdateVerifier.ComputeFileSha256Async(path, CancellationToken.None);
            var verifier = new Sha256UpdateVerifier();
            var matching = BuildRelease(hash, 3);
            Assert.Equal(UpdateVerificationStatus.Verified,
                await verifier.VerifyAsync(path, matching, CancellationToken.None));

            var mismatch = BuildRelease(new string('a', 64), 3);
            Assert.Equal(UpdateVerificationStatus.Sha256Mismatch,
                await verifier.VerifyAsync(path, mismatch, CancellationToken.None));

            var noHash = BuildRelease(null, 3);
            Assert.Equal(UpdateVerificationStatus.NotVerified,
                await verifier.VerifyAsync(path, noHash, CancellationToken.None));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(AuthenticodeStatus.Verified, UpdateVerificationStatus.Verified)]
    [InlineData(AuthenticodeStatus.NoSignature, UpdateVerificationStatus.SignatureUnsigned)]
    [InlineData(AuthenticodeStatus.Invalid, UpdateVerificationStatus.SignatureInvalid)]
    [InlineData(AuthenticodeStatus.UntrustedRoot, UpdateVerificationStatus.SignatureUntrusted)]
    [InlineData(AuthenticodeStatus.CheckFailed, UpdateVerificationStatus.SignatureCheckFailed)]
    public async Task CompositeVerifier_RequiresAuthenticode(AuthenticodeStatus signature,
        UpdateVerificationStatus expected)
    {
        var path = await WriteTempAsync(new byte[] { 1, 2, 3 });
        try
        {
            var hash = await Sha256UpdateVerifier.ComputeFileSha256Async(path, CancellationToken.None);
            var release = BuildRelease(hash, 3);
            var verifier = new CompositeUpdateVerifier(new FakeSignatureVerifier { Status = signature },
                new CapturingLogger());
            Assert.Equal(expected, await verifier.VerifyAsync(path, release, CancellationToken.None));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<string> WriteTempAsync(byte[] data)
    {
        var path = Path.Combine(Path.GetTempPath(), "FluidBarTests", "hash", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, data);
        return path;
    }

    private static UpdateRelease BuildRelease(string? sha256, long size)
    {
        var asset = new UpdateAsset("FluidBar-2.0.0-setup.exe",
            new Uri("https://example.test/setup.exe"), size, sha256);
        return new UpdateRelease("v2.0.0", new SemanticVersion(2, 0, 0, null), "2.0.0", "notes",
            null, false, null, System.Collections.Immutable.ImmutableArray.Create(asset), asset, sha256);
    }
}
