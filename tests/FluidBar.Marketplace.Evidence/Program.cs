using System.Text.Encodings.Web;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.Views.Settings;
using FluidBar.Core.Diagnostics;
using FluidBar.Marketplace.Community;
using FluidBar.Marketplace.Install;
using FluidBar.Marketplace.Packaging;
using FluidBar.Marketplace.Signing;
using FluidBar.Sdk.Manifest;

namespace FluidBar.Marketplace.Evidence;

/// <summary>
/// Developer/QA evidence harness for the local marketplace. It never fabricates data: the packages are built
/// from the compiled sample plugins, the install goes through the production install state machine, and the
/// screenshots render the real settings window against the store this run just wrote.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var options = Options.Parse(args);
        if (args.Contains("--verify", StringComparer.OrdinalIgnoreCase))
        {
            return VerifyOnlyAsync(options).GetAwaiter().GetResult();
        }

        ResetOutputDirectory(options);
        Directory.CreateDirectory(options.OutputDirectory);
        Directory.CreateDirectory(options.MarketplaceRoot);
        Directory.CreateDirectory(options.PluginsRoot);

        // The settings window builds its own community service from these overrides.
        Environment.SetEnvironmentVariable(CommunityMarketplaceStore.RootOverrideVariable, options.MarketplaceRoot);
        Environment.SetEnvironmentVariable(CommunityPluginPaths.PluginsRootOverrideVariable, options.PluginsRoot);

        var evidence = new EvidenceRecorder(options.OutputDirectory);
        FlowContext? flow = null;
        try
        {
            flow = RunFlowAsync(options, evidence).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            evidence.Add("error", new { stage = "flow", message = exception.ToString() });
            evidence.Flush();
            Console.Error.WriteLine(exception);
            return 1;
        }

        var exitCode = 0;
        try
        {
            exitCode = RunUiCapture(options, evidence);
        }
        catch (Exception exception)
        {
            evidence.Add("error", new { stage = "ui", message = exception.ToString() });
            exitCode = 1;
        }

        try
        {
            FinishFlowAsync(flow!, evidence).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            evidence.Add("error", new { stage = "finish", message = exception.ToString() });
            exitCode = 1;
        }

        evidence.Flush();
        Console.WriteLine("EVIDENCE: " + evidence.Path);
        return exitCode;
    }

    /// <summary>
    /// Fresh-process read-back: proves the counters/ratings survive a restart. Run after a flow run with
    /// <c>--verify</c>; it never writes to the marketplace.
    /// </summary>
    private static async Task<int> VerifyOnlyAsync(Options options)
    {
        var store = new CommunityMarketplaceStore(options.MarketplaceRoot);
        var service = new CommunityMarketplaceService(new CommunityMarketplaceOptions { Store = store });
        var listings = await service.BrowseAsync();
        var submissions = await service.GetSubmissionsAsync();
        var payload = new
        {
            indexJsonPath = store.IndexPath,
            indexJsonLength = File.Exists(store.IndexPath) ? new FileInfo(store.IndexPath).Length : 0,
            listings = listings.Select(static listing => new
            {
                listing.Id,
                listing.Version,
                downloads = listing.Stats.Downloads,
                rating = listing.Stats.Rating,
                ratingCount = listing.Stats.RatingCount,
                userRating = listing.Stats.UserRating,
                listing.SignatureState,
                listing.Versions,
            }).ToArray(),
            reviewQueue = submissions.Select(static submission => new
            {
                submission.PluginId,
                submission.Version,
                submission.State,
                submission.Reviewer,
                submission.ReviewedAt,
            }).ToArray(),
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(json);
        if (Directory.Exists(options.OutputDirectory))
        {
            File.WriteAllText(Path.Combine(options.OutputDirectory, "verify-fresh-process.json"), json);
        }

        return listings.Count == 0 ? 1 : 0;
    }

    private static async Task<FlowContext> RunFlowAsync(Options options, EvidenceRecorder evidence)
    {
        var activator = new RecordingActivator();
        using var installer = new PluginInstallServiceCommunityInstaller(new PluginInstallOptions
        {
            PluginRootDirectory = options.PluginsRoot,
            Activator = activator,
            Trust = MarketplaceTrustOptions.Empty,
            RequireMarketplaceSignature = false,
            AllowUnsignedLocalInstall = true,
        });
        var store = new CommunityMarketplaceStore(options.MarketplaceRoot);
        var service = new CommunityMarketplaceService(new CommunityMarketplaceOptions
        {
            Store = store,
            Installer = installer,
        });

        var minimal = BuildRealPackage(options, "FluidBar.Plugins.SampleMinimal");
        evidence.Add("1.package-built.sample-minimal", minimal);
        var rich = BuildRealPackage(options, "FluidBar.Plugins.SampleRich");
        evidence.Add("1.package-built.sample-rich", rich);

        var submitMinimal = await service.SubmitAsync(new CommunitySubmitRequest { PackagePath = minimal.PackagePath });
        var pendingAfterSubmit = await service.GetPendingAsync();
        evidence.Add("2.submit.sample-minimal", new
        {
            success = submitMinimal.Success,
            errors = submitMinimal.Errors,
            warnings = submitMinimal.Warnings,
            state = submitMinimal.Submission?.State.ToString(),
            sha256 = submitMinimal.Submission?.PackageSha256,
            pendingCount = pendingAfterSubmit.Count,
            storedPackage = submitMinimal.Submission is null ? null : store.ResolvePackagePath(submitMinimal.Submission.PackageFileName),
            storedPackageExists = submitMinimal.Submission is not null && File.Exists(store.ResolvePackagePath(submitMinimal.Submission.PackageFileName)),
        });

        var approveMinimal = await service.ApproveAsync(
            minimal.Id,
            minimal.Version,
            "Manifest valid, checksums verified inside the package, hash recorded at submit time.");
        evidence.Add("3.approve.sample-minimal", new
        {
            success = approveMinimal.Success,
            errors = approveMinimal.Errors,
            listedVersion = approveMinimal.Listing?.Version,
            listedCount = (await service.BrowseAsync()).Count,
            pendingCount = (await service.GetPendingAsync()).Count,
        });

        // A second real package stays in the review queue; the screenshot shows it as pending.
        var submitRich = await service.SubmitAsync(new CommunitySubmitRequest { PackagePath = rich.PackagePath });
        evidence.Add("4.submit.sample-rich", new
        {
            success = submitRich.Success,
            errors = submitRich.Errors,
            state = submitRich.Submission?.State.ToString(),
            pendingCount = (await service.GetPendingAsync()).Count,
        });

        var phases = new List<string>();
        var progress = new Progress<InstallPhase>(phase => phases.Add(phase.ToString()));
        var install = await service.InstallAsync(minimal.Id, progress);
        var versionDirectory = Path.Combine(options.PluginsRoot, minimal.Id, minimal.Version);
        evidence.Add("5.install.sample-minimal", new
        {
            success = install.Success,
            message = install.Message,
            failureKind = install.Install?.FailureKind.ToString(),
            warnings = install.Install?.Warnings,
            phases = phases.ToArray(),
            activated = activator.Activated.ToArray(),
            downloadsAfterInstall = install.Listing?.Stats.Downloads,
            installedVersionDirectory = versionDirectory,
            installedFiles = Directory.Exists(versionDirectory)
                ? Directory.EnumerateFiles(versionDirectory, "*", SearchOption.AllDirectories)
                    .Select(path => Path.GetRelativePath(versionDirectory, path).Replace('\\', '/'))
                    .OrderBy(static path => path, StringComparer.Ordinal)
                    .ToArray()
                : [],
            activePointer = File.Exists(Path.Combine(options.PluginsRoot, minimal.Id, "active.json"))
                ? File.ReadAllText(Path.Combine(options.PluginsRoot, minimal.Id, "active.json"))
                : null,
        });

        var rated = await service.RateAsync(minimal.Id, 5);
        evidence.Add("6.rate.sample-minimal", new
        {
            rating = rated.Stats.Rating,
            ratingCount = rated.Stats.RatingCount,
            userRating = rated.Stats.UserRating,
            downloads = rated.Stats.Downloads,
        });

        // Read everything back through brand-new service instances: this is the "restart" proof.
        var restarted = new CommunityMarketplaceService(new CommunityMarketplaceOptions
        {
            Store = new CommunityMarketplaceStore(options.MarketplaceRoot),
        });
        var reread = await restarted.GetListingAsync(minimal.Id);
        evidence.Add("7.restart-read-back", new
        {
            listing = reread,
            pendingCount = (await restarted.GetPendingAsync()).Count,
            indexJsonPath = store.IndexPath,
        });

        var snapshot = Path.Combine(options.OutputDirectory, "index-after-flow.json");
        File.Copy(store.IndexPath, snapshot, overwrite: true);
        evidence.Add("8.index-json", new { path = snapshot, length = new FileInfo(snapshot).Length });

        return new FlowContext(service, store, rich, options);
    }

    private static async Task FinishFlowAsync(FlowContext flow, EvidenceRecorder evidence)
    {
        var approveRich = await flow.Service.ApproveAsync(
            flow.Rich.Id,
            flow.Rich.Version,
            "Approved after the screenshot run; the same reviewed package as the pending entry.");
        evidence.Add("9.approve.sample-rich", new
        {
            success = approveRich.Success,
            errors = approveRich.Errors,
            listed = (await flow.Service.BrowseAsync()).Select(static listing => new
            {
                listing.Id,
                listing.Version,
                downloads = listing.Stats.Downloads,
                rating = listing.Stats.Rating,
                listing.Stats.RatingCount,
                listing.SignatureState,
            }).ToArray(),
            pendingCount = (await flow.Service.GetPendingAsync()).Count,
        });

        var final = Path.Combine(flow.Options.OutputDirectory, "index-final.json");
        File.Copy(flow.Store.IndexPath, final, overwrite: true);
        evidence.Add("10.final-state", new { path = final, length = new FileInfo(final).Length });
    }

    private static void ResetOutputDirectory(Options options)
    {
        if (!Directory.Exists(options.OutputDirectory))
        {
            return;
        }

        // Safety: the harness only deletes its own scratch directory inside the repository's build folder.
        var normalized = Path.GetFullPath(options.OutputDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var expected = Path.Combine(options.RepoRoot, "build") + Path.DirectorySeparatorChar;
        if (!(normalized + Path.DirectorySeparatorChar).StartsWith(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to clean '{options.OutputDirectory}'; use --output under the repository build directory.");
        }

        Directory.Delete(normalized, recursive: true);
    }

    private static PackageInfo BuildRealPackage(Options options, string projectName)
    {
        var sampleOutput = FindSampleOutput(options.RepoRoot, projectName);
        var outputDirectory = Path.Combine(options.OutputDirectory, "source-packages");
        Directory.CreateDirectory(outputDirectory);
        var packagePath = Path.Combine(outputDirectory, projectName + ".fbp");
        using var staging = FbpStagingBuilder.FromPluginOutput(sampleOutput);
        var build = staging.Build(packagePath);
        var bytes = File.ReadAllBytes(packagePath);
        var inspection = FbpPackageReader.Inspect(bytes);
        if (new FileInfo(packagePath).Length != build.Size)
        {
            throw new InvalidOperationException("Package size mismatch between builder and disk.");
        }

        return new PackageInfo(
            inspection.Manifest.Id,
            inspection.Manifest.Version,
            inspection.Manifest.Name?.Resolve("en-US", "en-US") ?? inspection.Manifest.Id,
            packagePath,
            build.Sha256,
            build.Size,
            build.Entries.Count,
            FbpPackager.ComputeSha256(packagePath) == build.Sha256,
            sampleOutput);
    }

    private static string FindSampleOutput(string repoRoot, string projectName)
    {
        var bin = Path.Combine(repoRoot, "plugins", "samples", projectName, "bin");
        if (!Directory.Exists(bin))
        {
            throw new DirectoryNotFoundException(
                $"'{bin}' does not exist. Build the solution first: dotnet build FluidBar.sln");
        }

        var candidates = Directory.EnumerateDirectories(bin, "net10.0", SearchOption.AllDirectories)
            .OrderByDescending(static directory => Directory.GetLastWriteTimeUtc(directory))
            .ToArray();
        return candidates.FirstOrDefault()
            ?? throw new DirectoryNotFoundException($"No net10.0 output under '{bin}'. Build the solution first.");
    }

    private static int RunUiCapture(Options options, EvidenceRecorder evidence)
    {
        var exitCode = 1;
        var builder = AppBuilder.Configure<EvidenceApplication>().UsePlatformDetect();
        var lifetime = new ClassicDesktopStyleApplicationLifetime
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };
        builder.SetupWithLifetime(lifetime);
        lifetime.Startup += (_, _) => _ = CaptureAsync(lifetime, options, evidence, code => exitCode = code);
        lifetime.Start(Array.Empty<string>());
        return exitCode;
    }

    private static readonly (string Culture, string Top, string Full, string Bottom)[] CaptureCultures =
    [
        ("en-US", "01-marketplace-window-top.png", "02-marketplace-section-full.png", "03-marketplace-window-bottom.png"),
        ("zh-Hans", "04-marketplace-zh-window-top.png", "05-marketplace-zh-section-full.png", "06-marketplace-zh-window-bottom.png"),
    ];

    private static async Task CaptureAsync(
        ClassicDesktopStyleApplicationLifetime lifetime,
        Options options,
        EvidenceRecorder evidence,
        Action<int> setExitCode)
    {
        var status = 1;
        try
        {
            var screenshots = Path.Combine(options.OutputDirectory, "screenshots");
            Directory.CreateDirectory(screenshots);
            var allCaptures = new List<object>();
            foreach (var names in CaptureCultures)
            {
                allCaptures.Add(await CaptureCultureAsync(options, screenshots, names));
            }

            evidence.Add("ui.screenshots", allCaptures);
            status = 0;
        }
        catch (Exception exception)
        {
            evidence.Add("error", new { stage = "ui-capture", message = exception.ToString() });
            Console.Error.WriteLine(exception);
        }
        finally
        {
            setExitCode(status);
            Dispatcher.UIThread.Post(() => lifetime.Shutdown(status));
        }
    }

    /// <summary>Renders the real settings window (marketplace section) in one UI culture.</summary>
    private static async Task<object> CaptureCultureAsync(
        Options options,
        string screenshots,
        (string Culture, string Top, string Full, string Bottom) names)
    {
        var localizer = LocalizationService.CreateDefault(names.Culture, showMissingKeys: false, enablePseudoLocale: false);
        LocalizationRuntime.Current = localizer;
        var settingsStore = new InMemoryUiSettingsStore(AppSettings.Default);
        var controller = new SettingsController(AppSettings.Default, settingsStore, NullAppLogger.Instance);
        var window = new SettingsWindow(controller, localizer, SettingsWindowSection.Marketplace);
        window.Show();
        var captures = new List<object>();
        try
        {
            await Task.Delay(1800);
            Dispatcher.UIThread.RunJobs();

            var content = window.Content as Control
                ?? throw new InvalidOperationException("The settings window has no content.");
            captures.Add(Capture(content, Path.Combine(screenshots, names.Top)));

            var section = FindDescendant<MarketplaceSectionView>(window);
            if (section is not null)
            {
                // Render the whole section (real production view) at its arranged size: one tall image that
                // contains the signed catalog cards AND the community submit/review/rating block.
                await Task.Delay(400);
                Dispatcher.UIThread.RunJobs();
                captures.Add(Capture(section, Path.Combine(screenshots, names.Full)));
                captures.Add(new
                {
                    culture = names.Culture,
                    community = section.Community.Store.RootDirectory,
                    listingCount = section.Community.Store.Load().Plugins.Count,
                    pendingCount = section.Community.Store.Load().Submissions.Count(
                        static submission => submission.State == CommunitySubmissionState.Pending),
                    sectionWidth = section.Bounds.Width,
                    sectionHeight = section.Bounds.Height,
                });

                var scroll = section.FindAncestorOfType<ScrollViewer>();
                if (scroll is not null)
                {
                    var maxOffset = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
                    captures.Add(new { culture = names.Culture, scrollExtent = scroll.Extent.Height, scrollViewport = scroll.Viewport.Height, maxOffset });
                    scroll.Offset = new Vector(0, maxOffset);
                    await Task.Delay(700);
                    Dispatcher.UIThread.RunJobs();
                    captures.Add(Capture(content, Path.Combine(screenshots, names.Bottom)));
                }
            }
        }
        finally
        {
            window.Close();
            await Task.Delay(400);
            Dispatcher.UIThread.RunJobs();
        }

        return new { culture = names.Culture, captures };
    }

    private static object Capture(Control content, string path)
    {
        var width = (int)Math.Ceiling(content.Bounds.Width);
        var height = (int)Math.Ceiling(content.Bounds.Height);
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException($"The settings window has no size ({width}x{height}).");
        }

        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        bitmap.Render(content);
        bitmap.Save(path);
        return new { path, width, height, bytes = new FileInfo(path).Length };
    }

    private static T? FindDescendant<T>(Visual root)
        where T : Visual
    {
        foreach (var descendant in FindDescendants<T>(root))
        {
            return descendant;
        }

        return null;
    }

    private static IEnumerable<T> FindDescendants<T>(Visual root)
        where T : Visual
    {
        foreach (var child in root.GetVisualChildren())
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindDescendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    internal sealed record PackageInfo(
        string Id,
        string Version,
        string Name,
        string PackagePath,
        string Sha256,
        long Size,
        int EntryCount,
        bool DiskHashMatches,
        string SourceOutputDirectory);

    internal sealed record FlowContext(
        CommunityMarketplaceService Service,
        CommunityMarketplaceStore Store,
        PackageInfo Rich,
        Options Options);

    internal sealed class RecordingActivator : IPluginActivator
    {
        internal List<string> Activated { get; } = [];

        public Task<ActivationResult> ActivateAsync(PluginManifest manifest, string versionDirectory, CancellationToken cancellationToken)
        {
            Activated.Add(manifest.Id + "@" + manifest.Version);
            return Task.FromResult(ActivationResult.Ok());
        }

        public Task DeactivateAsync(string pluginId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    internal sealed class EvidenceApplication : Application
    {
        public override void Initialize() => Styles.Add(new FluentTheme());
    }

    internal sealed class EvidenceRecorder
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private readonly List<object> _steps = [];
        private readonly string _directory;

        internal EvidenceRecorder(string directory) => _directory = directory;

        internal string Path => System.IO.Path.Combine(_directory, "evidence.json");

        internal void Add(string name, object data) => _steps.Add(new { step = name, at = DateTimeOffset.Now, data });

        internal void Flush() =>
            File.WriteAllText(Path, JsonSerializer.Serialize(new { generatedAt = DateTimeOffset.Now, steps = _steps }, Options));
    }

    internal sealed class Options
    {
        public required string RepoRoot { get; init; }

        public required string OutputDirectory { get; init; }

        public required string MarketplaceRoot { get; init; }

        public required string PluginsRoot { get; init; }

        internal static Options Parse(string[] args)
        {
            var repoRoot = FindRepoRoot();
            var output = ArgValue(args, "--output") ?? System.IO.Path.Combine(repoRoot, "build", "evidence", "marketplace");
            return new Options
            {
                RepoRoot = repoRoot,
                OutputDirectory = System.IO.Path.GetFullPath(output),
                MarketplaceRoot = System.IO.Path.Combine(System.IO.Path.GetFullPath(output), "marketplace"),
                PluginsRoot = System.IO.Path.Combine(System.IO.Path.GetFullPath(output), "plugins"),
            };
        }

        private static string? ArgValue(string[] args, string name)
        {
            for (var index = 0; index < args.Length - 1; index++)
            {
                if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[index + 1];
                }
            }

            return null;
        }

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "FluidBar.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new InvalidOperationException("Repository root (FluidBar.sln) was not found.");
        }
    }
}
