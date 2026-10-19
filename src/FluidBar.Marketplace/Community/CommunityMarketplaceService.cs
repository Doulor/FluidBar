using FluidBar.Marketplace.Install;
using FluidBar.Marketplace.Packaging;
using FluidBar.Marketplace.Signing;
using FluidBar.Sdk.Manifest;

namespace FluidBar.Marketplace.Community;

/// <summary>Configuration of the local community marketplace.</summary>
public sealed record CommunityMarketplaceOptions
{
    /// <summary>Store that owns the on-disk marketplace directory.</summary>
    public required CommunityMarketplaceStore Store { get; init; }

    /// <summary>Trusted keys used to verify detached signatures at submit time (empty = unsigned only).</summary>
    public MarketplaceTrustOptions Trust { get; init; } = MarketplaceTrustOptions.Empty;

    /// <summary>Installer used by <see cref="CommunityMarketplaceService.InstallAsync"/>; <see langword="null"/> disables installs.</summary>
    public ICommunityPackageInstaller? Installer { get; init; }

    /// <summary>Clock for audit timestamps (tests inject a fake clock).</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>Locale used to resolve localized manifest strings.</summary>
    public string Locale { get; init; } = CommunityMarketplaceDefaults.Locale;
}

/// <summary>Submit request for one <c>.fbp</c> file.</summary>
public sealed record CommunitySubmitRequest
{
    /// <summary>Absolute path of the <c>.fbp</c> to submit.</summary>
    public required string PackagePath { get; init; }

    /// <summary>Absolute path of the detached <c>.sig.json</c>, or <see langword="null"/> for an unsigned submission.</summary>
    public string? SignaturePath { get; init; }

    /// <summary>Reviewer identity recorded on the submission.</summary>
    public string Reviewer { get; init; } = CommunityMarketplaceDefaults.Reviewer;

    /// <summary>Allow an unsigned package to enter the review queue (flagged as unverified).</summary>
    public bool AllowUnsigned { get; init; } = true;

    /// <summary>Reject unsigned packages outright (production-style policy).</summary>
    public bool RequireSignature { get; init; }
}

/// <summary>Submit outcome; failures list every validation error instead of throwing.</summary>
public sealed record CommunitySubmitResult(
    bool Success,
    CommunitySubmission? Submission,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    /// <summary>Creates a failure result.</summary>
    public static CommunitySubmitResult Fail(params string[] errors) => new(false, null, errors, []);
}

/// <summary>Review outcome.</summary>
public sealed record CommunityReviewResult(
    bool Success,
    CommunitySubmission? Submission,
    CommunityListing? Listing,
    IReadOnlyList<string> Errors)
{
    /// <summary>Creates a failure result.</summary>
    public static CommunityReviewResult Fail(params string[] errors) => new(false, null, null, errors);
}

/// <summary>Install outcome of one listed community package.</summary>
public sealed record CommunityInstallOutcome
{
    public required bool Success { get; init; }

    public required string PluginId { get; init; }

    public string? Version { get; init; }

    /// <summary>The underlying install state machine result (warnings, failure kind).</summary>
    public InstallResult? Install { get; init; }

    /// <summary>The listing after the download counter was incremented.</summary>
    public CommunityListing? Listing { get; init; }

    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// The local community marketplace: submit → review (approve/reject) → list in <c>index.json</c> → install →
/// download counter → rating. Everything is persisted under the marketplace root, so state survives a restart.
/// </summary>
public sealed class CommunityMarketplaceService
{
    private readonly CommunityMarketplaceStore _store;
    private readonly CommunityMarketplaceOptions _options;
    private readonly PackageSignatureVerifier _verifier;
    private readonly object _gate = new();

    /// <summary>Creates the service.</summary>
    public CommunityMarketplaceService(CommunityMarketplaceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Store);
        _options = options;
        _store = options.Store;
        _verifier = new PackageSignatureVerifier(options.Trust);
    }

    /// <summary>Raised after the index was written by a submit/review/rating/download/install action.</summary>
    public event EventHandler? Changed;

    /// <summary>The store backing this service.</summary>
    public CommunityMarketplaceStore Store => _store;

    /// <summary>Creates the default service over <c>%APPDATA%\FluidBar\marketplace</c> with the real installer.</summary>
    public static CommunityMarketplaceService CreateDefault(MarketplaceTrustOptions? trust = null)
    {
        var effectiveTrust = trust ?? MarketplaceTrustOptions.Empty;
        return new CommunityMarketplaceService(new CommunityMarketplaceOptions
        {
            Store = new CommunityMarketplaceStore(CommunityMarketplaceStore.ResolveRoot()),
            Trust = effectiveTrust,
            Installer = PluginInstallServiceCommunityInstaller.CreateDefault(effectiveTrust),
        });
    }

    /// <summary>Submits a package into the pending review queue after manifest/package/signature validation.</summary>
    public async Task<CommunitySubmitResult> SubmitAsync(CommunitySubmitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.PackagePath))
        {
            return CommunitySubmitResult.Fail("A .fbp package path is required.");
        }

        var packagePath = Path.GetFullPath(request.PackagePath);
        if (!File.Exists(packagePath))
        {
            return CommunitySubmitResult.Fail($"Package '{packagePath}' does not exist.");
        }

        if (!string.Equals(Path.GetExtension(packagePath), ".fbp", StringComparison.OrdinalIgnoreCase))
        {
            return CommunitySubmitResult.Fail($"'{Path.GetFileName(packagePath)}' is not a .fbp package.");
        }

        byte[] packageBytes;
        try
        {
            packageBytes = await File.ReadAllBytesAsync(packagePath, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException exception)
        {
            return CommunitySubmitResult.Fail($"Package '{packagePath}' could not be read: {exception.Message}");
        }

        PluginManifest manifest;
        try
        {
            manifest = FbpPackageReader.Inspect(packageBytes).Manifest;
        }
        catch (PackageFormatException exception)
        {
            return CommunitySubmitResult.Fail($"Package validation failed: {exception.Message}");
        }

        var warnings = new List<string>();
        PackageSignatureDocument? signatureDocument = null;
        var signatureState = CommunitySignatureState.Unsigned;
        string? signatureKeyId = null;
        if (!string.IsNullOrWhiteSpace(request.SignaturePath))
        {
            var signaturePath = Path.GetFullPath(request.SignaturePath);
            if (!File.Exists(signaturePath))
            {
                return CommunitySubmitResult.Fail($"Signature '{signaturePath}' does not exist.");
            }

            if (!PackageSignatureDocument.TryParse(await File.ReadAllTextAsync(signaturePath, cancellationToken).ConfigureAwait(false), out signatureDocument, out var signatureErrors) ||
                signatureDocument is null)
            {
                return CommunitySubmitResult.Fail($"Signature document is invalid: {string.Join("; ", signatureErrors)}");
            }

            var verification = await _verifier
                .VerifyFileAsync(packagePath, signatureDocument, new PackageVerificationRequest { RequireMarketplaceSignature = true }, cancellationToken)
                .ConfigureAwait(false);
            if (!verification.Verified)
            {
                return CommunitySubmitResult.Fail($"Package signature verification failed: {string.Join("; ", verification.Errors)}");
            }

            signatureState = CommunitySignatureState.Verified;
            signatureKeyId = signatureDocument.Signatures.Count > 0 ? signatureDocument.Signatures[0].KeyId : null;
        }
        else if (request.RequireSignature)
        {
            return CommunitySubmitResult.Fail("This marketplace requires a detached signature (.sig.json) for every submission.");
        }

        if (signatureState == CommunitySignatureState.Unsigned)
        {
            if (!request.AllowUnsigned)
            {
                return CommunitySubmitResult.Fail("Unsigned packages are not accepted; provide a detached signature.");
            }

            warnings.Add("The package is unsigned; the listing is marked unverified.");
        }

        var now = _options.TimeProvider.GetUtcNow();
        var packageSha256 = await FbpPackager.ComputeSha256Async(packagePath, cancellationToken).ConfigureAwait(false);
        var submission = new CommunitySubmission
        {
            PluginId = manifest.Id,
            Version = manifest.Version,
            PackageFileName = CommunityMarketplaceStore.PackageFileName(manifest.Id, manifest.Version),
            PackageSha256 = packageSha256,
            PackageSize = packageBytes.LongLength,
            SignatureFileName = signatureDocument is null ? null : CommunityMarketplaceStore.SignatureFileName(manifest.Id, manifest.Version),
            SignatureState = signatureState,
            SignatureKeyId = signatureKeyId,
            Plugin = CommunityPluginInfo.FromManifest(manifest, _options.Locale),
            SubmittedAt = now,
            Reviewer = string.IsNullOrWhiteSpace(request.Reviewer) ? CommunityMarketplaceDefaults.Reviewer : request.Reviewer,
            State = CommunitySubmissionState.Pending,
        };

        lock (_gate)
        {
            var index = _store.Load();
            var existing = index.Submissions.FirstOrDefault(candidate =>
                string.Equals(candidate.PluginId, submission.PluginId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.Version, submission.Version, StringComparison.OrdinalIgnoreCase));
            if (existing is not null && existing.State != CommunitySubmissionState.Rejected)
            {
                return CommunitySubmitResult.Fail(
                    $"'{submission.PluginId}' version {submission.Version} was already submitted and is {existing.State.ToString().ToLowerInvariant()}.");
            }

            _store.SavePackage(submission.PluginId, submission.Version, packageBytes);
            if (signatureDocument is not null)
            {
                _store.SaveSignature(submission.PluginId, submission.Version, signatureDocument.ToJson());
            }

            var submissions = index.Submissions
                .Where(candidate => !(string.Equals(candidate.PluginId, submission.PluginId, StringComparison.OrdinalIgnoreCase) &&
                                      string.Equals(candidate.Version, submission.Version, StringComparison.OrdinalIgnoreCase)))
                .Append(submission)
                .OrderBy(static candidate => candidate.PluginId, StringComparer.Ordinal)
                .ThenByDescending(static candidate => candidate.Version, StringComparer.Ordinal)
                .ToArray();
            _store.Save(index with { GeneratedAt = now, Submissions = submissions });
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return new CommunitySubmitResult(true, submission, [], warnings);
    }

    /// <summary>Reads the review queue (optionally filtered by state), newest first.</summary>
    public Task<IReadOnlyList<CommunitySubmission>> GetSubmissionsAsync(
        CommunitySubmissionState? state = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var index = _store.Load();
            IReadOnlyList<CommunitySubmission> submissions = index.Submissions
                .Where(candidate => state is null || candidate.State == state)
                .OrderByDescending(static candidate => candidate.SubmittedAt)
                .ThenBy(static candidate => candidate.PluginId, StringComparer.Ordinal)
                .ToArray();
            return Task.FromResult(submissions);
        }
    }

    /// <summary>Returns pending submissions, newest first.</summary>
    public Task<IReadOnlyList<CommunitySubmission>> GetPendingAsync(CancellationToken cancellationToken = default) =>
        GetSubmissionsAsync(CommunitySubmissionState.Pending, cancellationToken);

    /// <summary>
    /// Applies a review decision. Approving writes (or refreshes) the listing in <c>index.json</c>; rejecting a
    /// listed plugin removes it from the marketplace. Existing downloads/ratings survive a re-review.
    /// </summary>
    public Task<CommunityReviewResult> ReviewAsync(
        string pluginId,
        string version,
        CommunityReviewDecision decision,
        string? note = null,
        string? reviewer = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        cancellationToken.ThrowIfCancellationRequested();

        CommunityReviewResult result;
        lock (_gate)
        {
            var index = _store.Load();
            var target = index.Submissions.FirstOrDefault(candidate =>
                string.Equals(candidate.PluginId, pluginId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.Version, version, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                return Task.FromResult(CommunityReviewResult.Fail($"No submission for '{pluginId}' version {version} exists."));
            }

            var now = _options.TimeProvider.GetUtcNow();
            var reviewed = target with
            {
                State = decision == CommunityReviewDecision.Approve ? CommunitySubmissionState.Approved : CommunitySubmissionState.Rejected,
                ReviewedAt = now,
                ReviewNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                Reviewer = string.IsNullOrWhiteSpace(reviewer) ? CommunityMarketplaceDefaults.Reviewer : reviewer,
            };

            var submissions = index.Submissions
                .Select(candidate => ReferenceEquals(candidate, target) ? reviewed : candidate)
                .ToArray();
            var plugins = RebuildListings(index.Plugins, submissions);
            var updated = index with { GeneratedAt = now, Submissions = submissions, Plugins = plugins };
            _store.Save(updated);

            result = new CommunityReviewResult(
                true,
                reviewed,
                plugins.FirstOrDefault(candidate => string.Equals(candidate.Id, reviewed.PluginId, StringComparison.OrdinalIgnoreCase)),
                []);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(result);
    }

    /// <summary>Approves a submission and lists it in <c>index.json</c>.</summary>
    public Task<CommunityReviewResult> ApproveAsync(
        string pluginId,
        string version,
        string? note = null,
        string? reviewer = null,
        CancellationToken cancellationToken = default) =>
        ReviewAsync(pluginId, version, CommunityReviewDecision.Approve, note, reviewer, cancellationToken);

    /// <summary>Rejects a submission (or delists an approved plugin).</summary>
    public Task<CommunityReviewResult> RejectAsync(
        string pluginId,
        string version,
        string? note = null,
        string? reviewer = null,
        CancellationToken cancellationToken = default) =>
        ReviewAsync(pluginId, version, CommunityReviewDecision.Reject, note, reviewer, cancellationToken);

    /// <summary>Browses the listed (approved) plugins, newest first.</summary>
    public Task<IReadOnlyList<CommunityListing>> BrowseAsync(string? search = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var plugins = _store.Load().Plugins;
            IEnumerable<CommunityListing> items = plugins;
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                items = items.Where(listing =>
                    listing.Id.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    listing.Plugin.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    listing.Plugin.Summary.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    listing.Plugin.PublisherName.Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            IReadOnlyList<CommunityListing> listings = items
                .OrderByDescending(static listing => listing.PublishedAt)
                .ThenBy(static listing => listing.Id, StringComparer.Ordinal)
                .ToArray();
            return Task.FromResult(listings);
        }
    }

    /// <summary>Returns one listing, or <see langword="null"/> when it is not listed.</summary>
    public Task<CommunityListing?> GetListingAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(FindListing(pluginId));
        }
    }

    /// <summary>
    /// Records a 1–5 star rating for a listed plugin and persists it. The local marketplace is single-device, so
    /// rating again replaces this device's own vote instead of appending a fake one.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The rating is outside 1..5.</exception>
    /// <exception cref="InvalidOperationException">The plugin is not listed.</exception>
    public Task<CommunityListing> RateAsync(string pluginId, int stars, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        if (stars is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(stars), stars, "A star rating must be between 1 and 5.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        CommunityListing updated;
        lock (_gate)
        {
            var index = _store.Load();
            var match = index.Plugins.FirstOrDefault(candidate => string.Equals(candidate.Id, pluginId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Plugin '{pluginId}' is not listed in the local marketplace.");
            var ratingCount = Math.Max(1, match.Stats.RatingCount);
            var rating = Math.Round((double)stars, 2);
            updated = match with
            {
                Stats = match.Stats with
                {
                    Rating = rating,
                    RatingCount = ratingCount,
                    UserRating = stars,
                },
            };
            var plugins = index.Plugins.Select(candidate => ReferenceEquals(candidate, match) ? updated : candidate).ToArray();
            _store.Save(index with { GeneratedAt = _options.TimeProvider.GetUtcNow(), Plugins = plugins });
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(updated);
    }

    /// <summary>Increments the persisted download counter of a listed plugin.</summary>
    public Task<CommunityListing> RecordDownloadAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        cancellationToken.ThrowIfCancellationRequested();
        CommunityListing updated;
        lock (_gate)
        {
            var index = _store.Load();
            var match = index.Plugins.FirstOrDefault(candidate => string.Equals(candidate.Id, pluginId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Plugin '{pluginId}' is not listed in the local marketplace.");
            updated = match with { Stats = match.Stats with { Downloads = match.Stats.Downloads + 1 } };
            var plugins = index.Plugins.Select(candidate => ReferenceEquals(candidate, match) ? updated : candidate).ToArray();
            _store.Save(index with { GeneratedAt = _options.TimeProvider.GetUtcNow(), Plugins = plugins });
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(updated);
    }

    /// <summary>Installs a listed plugin through the injected installer; a successful install increments downloads.</summary>
    public async Task<CommunityInstallOutcome> InstallAsync(
        string pluginId,
        IProgress<InstallPhase>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        var listing = await GetListingAsync(pluginId, cancellationToken).ConfigureAwait(false);
        if (listing is null)
        {
            return new CommunityInstallOutcome { Success = false, PluginId = pluginId, Message = $"Plugin '{pluginId}' is not listed in the local marketplace." };
        }

        if (_options.Installer is null)
        {
            return new CommunityInstallOutcome { Success = false, PluginId = pluginId, Version = listing.Version, Listing = listing, Message = "No local installer is configured." };
        }

        var packagePath = _store.ResolvePackagePath(listing.PackageFileName);
        if (!File.Exists(packagePath))
        {
            return new CommunityInstallOutcome
            {
                Success = false,
                PluginId = pluginId,
                Version = listing.Version,
                Listing = listing,
                Message = $"The reviewed package copy '{packagePath}' is missing from the marketplace directory.",
            };
        }

        var result = await _options.Installer.InstallAsync(
            new CommunityPackageInstallRequest
            {
                PackagePath = packagePath,
                SignaturePath = _store.ResolveSignaturePath(listing.SignatureFileName),
                PluginId = listing.Id,
                Version = listing.Version,
                AllowUnsigned = listing.SignatureState != CommunitySignatureState.Verified,
                Progress = progress,
            },
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            return new CommunityInstallOutcome
            {
                Success = false,
                PluginId = pluginId,
                Version = listing.Version,
                Install = result,
                Listing = listing,
                Message = result.Message,
            };
        }

        var updated = await RecordDownloadAsync(pluginId, cancellationToken).ConfigureAwait(false);
        return new CommunityInstallOutcome
        {
            Success = true,
            PluginId = pluginId,
            Version = listing.Version,
            Install = result,
            Listing = updated,
            Message = $"Installed '{listing.Id}' {listing.Version}. Restart FluidBar to load it into the running host.",
        };
    }

    private CommunityListing? FindListing(string pluginId) =>
        _store.Load().Plugins.FirstOrDefault(candidate => string.Equals(candidate.Id, pluginId, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<CommunityListing> RebuildListings(
        IReadOnlyList<CommunityListing> existing,
        IReadOnlyList<CommunitySubmission> submissions)
    {
        var result = new List<CommunityListing>();
        foreach (var group in submissions
                     .Where(static submission => submission.State == CommunitySubmissionState.Approved)
                     .GroupBy(static submission => submission.PluginId, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            var approved = group.OrderByDescending(static submission => submission.Version, Comparer<string>.Create(CommunityVersionOrder.Compare)).ToArray();
            var latest = approved[0];
            var previous = existing.FirstOrDefault(candidate => string.Equals(candidate.Id, group.Key, StringComparison.OrdinalIgnoreCase));
            result.Add(new CommunityListing
            {
                Id = latest.PluginId,
                Plugin = latest.Plugin,
                Version = latest.Version,
                PackageFileName = latest.PackageFileName,
                PackageSha256 = latest.PackageSha256,
                PackageSize = latest.PackageSize,
                SignatureFileName = latest.SignatureFileName,
                SignatureState = latest.SignatureState,
                SignatureKeyId = latest.SignatureKeyId,
                PublishedAt = latest.ReviewedAt ?? latest.SubmittedAt,
                ApprovedNote = latest.ReviewNote,
                Stats = previous?.Stats ?? new CommunityStats(),
                Versions = [.. approved.Select(static submission => submission.Version)],
            });
        }

        return result;
    }
}
