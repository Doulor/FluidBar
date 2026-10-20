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
using FluidBar.App.Hosting.Skia;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.State;
using FluidBar.App.Views.Settings;
using FluidBar.Core.Activities;
using FluidBar.Core.Diagnostics;
using FluidBar.Island;
using FluidBar.Marketplace.Community;
using FluidBar.Marketplace.Install;
using FluidBar.Marketplace.Packaging;
using FluidBar.Marketplace.Signing;
using FluidBar.Sdk.Manifest;
using SkiaSharp;

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

        if (args.Contains("--motion-capture", StringComparer.OrdinalIgnoreCase))
        {
            return RunMotionCapture(options);
        }

        if (args.Contains("--shadow-probe", StringComparer.OrdinalIgnoreCase))
        {
            return RunShadowProbe(options);
        }

        if (args.Contains("--settings-material", StringComparer.OrdinalIgnoreCase))
        {
            var section = SettingsWindowSection.Appearance;
            var requested = ArgValue(args, "--settings-section");
            if (!string.IsNullOrWhiteSpace(requested) && !Enum.TryParse(requested, ignoreCase: true, out section))
            {
                throw new ArgumentException($"Unknown settings section '{requested}'.");
            }

            return RunSettingsMaterialCapture(options, section);
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

    /// <summary>
    /// Offscreen evidence for the settings material states: renders the real SettingsWindow on the Appearance
    /// page in Idle / Pressed (L4 inset) / Hover (L3 glow) using <c>SettingsWindow.ForceMaterialState</c>, so no
    /// pointer input is involved. Writes build/reports/ui-audit/material/settings-{idle,press,hover}.png plus
    /// 2x crops of the first card row.
    /// </summary>
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

    /// <summary>Diagnostic: does Avalonia 12 render an inset BoxShadow on a Border? Renders a plain and an
    /// inset-shadowed card side by side and reports the pixel delta (0 means the inset shadow is ignored).</summary>
    private static int RunShadowProbe(Options options)
    {
        var exitCode = 1;
        var builder = AppBuilder.Configure<EvidenceApplication>().UsePlatformDetect();
        var lifetime = new ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        builder.SetupWithLifetime(lifetime);
        lifetime.Startup += (_, _) => _ = ProbeAsync(lifetime, options, code => exitCode = code);
        lifetime.Start(Array.Empty<string>());
        return exitCode;
    }

    private static async Task ProbeAsync(
        ClassicDesktopStyleApplicationLifetime lifetime,
        Options options,
        Action<int> setExitCode)
    {
        var status = 1;
        try
        {
            var outputDir = Path.Combine(options.RepoRoot, "build", "reports", "ui-audit", "material");
            Directory.CreateDirectory(outputDir);
            var window = new Window
            {
                Width = 940,
                Height = 260,
                Background = new SolidColorBrush(Color.Parse("#08080A")),
                Content = BuildProbe(),
                WindowDecorations = WindowDecorations.None,
                ShowInTaskbar = false,
            };
            window.Show();
            await Task.Delay(900);
            Dispatcher.UIThread.RunJobs();
            var content = window.Content as Control ?? throw new InvalidOperationException("Probe content missing.");
            var png = Path.Combine(outputDir, "shadow-probe.png");
            Capture(content, png);

            using var bitmap = SKBitmap.Decode(png) ?? throw new InvalidOperationException("Probe render failed.");
            var opaque = Measure(bitmap, 12, 236);
            var translucent = Measure(bitmap, 460, 684);

            var report = new
            {
                probe = png,
                opaqueCard = new { meanDelta = opaque.meanDelta, maxDelta = opaque.maxDelta, changedPixels = opaque.changedPixels },
                translucentCard = new { meanDelta = translucent.meanDelta, maxDelta = translucent.maxDelta, changedPixels = translucent.changedPixels },
                verdict = opaque.maxDelta <= 1.0
                    ? "inset BoxShadow is ignored by the renderer"
                    : translucent.maxDelta <= 1.0
                        ? "inset BoxShadow renders on opaque cards but NOT on the production translucent card"
                        : "inset BoxShadow renders on both card kinds",
            };
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(outputDir, "shadow-probe.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            window.Close();
            status = 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
        }
        finally
        {
            setExitCode(status);
            Dispatcher.UIThread.Post(() => lifetime.Shutdown(status));
        }
    }

    private static (double meanDelta, double maxDelta, int changedPixels) Measure(SKBitmap bitmap, int leftX, int rightX)
    {
        var deltas = new List<double>();
        for (var y = 40; y < 200; y++)
        {
            for (var x = leftX; x < leftX + 200; x++)
            {
                var left = bitmap.GetPixel(x, y);
                var right = bitmap.GetPixel(rightX + (x - leftX), y);
                deltas.Add((Math.Abs(left.Red - right.Red) + Math.Abs(left.Green - right.Green) + Math.Abs(left.Blue - right.Blue)) / 3.0);
            }
        }

        return (deltas.Average(), deltas.Max(), deltas.Count(static delta => delta > 1.0));
    }

    private static Control BuildProbe()
    {
        static Border Card(string background, BoxShadows shadow) => new()
        {
            Width = 200,
            Height = 200,
            Margin = new Thickness(12),
            Background = new SolidColorBrush(Color.Parse(background)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(23, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            BoxShadow = shadow,
            Child = new TextBlock { Text = background, Foreground = new SolidColorBrush(Color.Parse("#EDEDEE")), Margin = new Thickness(16) },
        };

        var inset = new BoxShadows(new BoxShadow
        {
            IsInset = true,
            Blur = 4,
            Spread = -8,
            OffsetY = 8,
            Color = Color.FromArgb(46, 0, 0, 0),
        });

        // Opaque card (control) / translucent card (production #99141418) each without and with the inset.
        return new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Children =
            {
                Card("#141418", default),
                Card("#141418", inset),
                Card("#99141418", default),
                Card("#99141418", inset),
            },
        };
    }

    // -------------------------------------------------------------------------------------------------
    // Motion capture: drive the real SkiaIslandController + IslandRenderer at 60 fps and record frames.
    // -------------------------------------------------------------------------------------------------

    private static int RunMotionCapture(Options options)
    {
        var outputDir = Path.Combine(options.RepoRoot, "build", "reports", "ui-audit", "motion-capture");
        Directory.CreateDirectory(outputDir);
        var time = new HarnessTimeProvider();
        var localizer = new HarnessLocalizer();
        var store = new InMemoryUiSettingsStore(AppSettings.Default with { AutoCollapseMs = 0, AutoHideMs = 0 });
        var settings = new SettingsController(store.LastSaved, store, NullAppLogger.Instance);
        var data = new DesignIslandDataSource(localizer, time, static action => action());
        using var controller = new SkiaIslandController(
            new SkiaIslandHostOptions
            {
                Localizer = localizer,
                Settings = settings,
                Data = data,
                TimeProvider = time,
                TargetFrameRate = 60,
            },
            mediaPill: MediaPillPreferences.Default);
        controller.Start();
        using var renderer = new IslandRenderer(IslandTheme.Dark);

        var step = TimeSpan.FromSeconds(1.0 / 60.0);

        // Warm up Skia/text/glyph caches so the first captured frame is not a JIT outlier.
        for (var warmup = 0; warmup < 3; warmup++)
        {
            time.Advance(step);
            controller.Advance(step);
            using var warm = renderer.RenderToBitmap(controller.RenderState, 1.5f);
        }

        controller.HandleShowHotkey(); // deterministic start: compact pill visible
        Settle(controller, time, step);

        var segments = new List<MotionSegment>();
        segments.Add(CaptureSegment(controller, renderer, time, outputDir, step, "expand", 60, MotionDirection.Increasing,
            () => controller.HandleToggleHotkey(),
            () => controller.Motion.ExpandedFactor));

        Settle(controller, time, step);
        segments.Add(CaptureSegment(controller, renderer, time, outputDir, step, "collapse", 60, MotionDirection.Decreasing,
            () => controller.HandleEscape(),
            () => controller.Motion.ExpandedFactor));

        Settle(controller, time, step);
        controller.HandleToggleHotkey();
        Settle(controller, time, step);
        segments.Add(CaptureSegment(controller, renderer, time, outputDir, step, "page", 60, MotionDirection.Increasing,
            () => controller.HandlePageMove(1),
            () => controller.Pager.PagePosition));

        // Satellite birth (frames 0..29) then fusion (frames 30..59) inside one 1 s segment.
        // Split islets only exist in the compact pill, so collapse the panel first.
        Settle(controller, time, step);
        controller.HandleEscape();
        Settle(controller, time, step);
        controller.PublishActivity(
            ActivityKind.Media,
            new ActivityPayload
            {
                Title = "Midnight City",
                Subtitle = "M83",
                IsActive = true,
                ShowsAudioWave = true,
                ProgressPercent = 46,
            },
            sequence: 1);
        controller.PublishActivity(
            ActivityKind.Agent,
            new ActivityPayload { Title = "Refactor parser", Body = "Running tests", IsActive = true },
            sequence: 2);
        segments.Add(CaptureSegment(controller, renderer, time, outputDir, step, "satellite", 60, MotionDirection.Peaks,
            static () => { },
            () => controller.Motion.SplitProgress,
            midTrigger: () => settings.Update(current => current with { Stacking = StackingStrategySetting.Latest })));

        WriteTimingCsv(outputDir, segments);
        WriteMotionReport(outputDir, segments);
        BuildMotionStrip(outputDir, segments);
        BuildKeyFrames(outputDir, [("expand", 30), ("page", 30), ("satellite", 15)]);

        Console.WriteLine(JsonSerializer.Serialize(
            segments.Select(static segment => new
            {
                segment = segment.Name,
                frames = segment.Frames.Count,
                p50 = Math.Round(segment.Percentile(50), 3),
                p99 = Math.Round(segment.Percentile(99), 3),
                overBudget = segment.OverBudgetCount,
                backJumps = segment.BackJumpCount,
                stalledFrames = segment.StalledFrames,
                maxJump = Math.Round(segment.MaxJump, 4),
            }),
            new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private enum MotionDirection
    {
        Increasing,
        Decreasing,
        Peaks,
    }

    private sealed record MotionFrame(
        int Index,
        double AdvanceMs,
        double RenderMs,
        double EncodeMs,
        double WallMs,
        double SimulatedMs,
        double Metric,
        double Width,
        double Height,
        string Form,
        bool AnimatingAfter,
        ulong Hash)
    {
        /// <summary>UI frame cost: controller advance + Skia render (PNG encoding is harness overhead).</summary>
        public double UiMs => AdvanceMs + RenderMs;
    }

    private sealed class MotionSegment
    {
        public required string Name { get; init; }

        public required MotionDirection Direction { get; init; }

        public required List<MotionFrame> Frames { get; init; }

        public int OverBudgetCount => Frames.Count(static frame => frame.UiMs > 16.7);

        public int BackJumpCount { get; set; }

        public int StalledFrames { get; set; }

        public int DuplicateFrames { get; set; }

        public double Overshoot { get; set; }

        public double MaxJump { get; set; }

        public double Percentile(double percentile) => Percentile(static frame => frame.UiMs, percentile);

        public double Percentile(Func<MotionFrame, double> selector, double percentile)
        {
            if (Frames.Count == 0)
            {
                return 0;
            }

            var sorted = Frames.Select(selector).OrderBy(static value => value).ToArray();
            var rank = (int)Math.Clamp(Math.Ceiling((percentile / 100.0) * sorted.Length) - 1, 0, sorted.Length - 1);
            return sorted[rank];
        }
    }

    private static MotionSegment CaptureSegment(
        SkiaIslandController controller,
        IslandRenderer renderer,
        HarnessTimeProvider time,
        string outputDir,
        TimeSpan step,
        string name,
        int frames,
        MotionDirection direction,
        Action trigger,
        Func<double> metric,
        Action? midTrigger = null)
    {
        var segmentDir = Path.Combine(outputDir, name);
        Directory.CreateDirectory(segmentDir);
        foreach (var stale in Directory.EnumerateFiles(segmentDir, "frame-*.png"))
        {
            File.Delete(stale);
        }

        trigger();
        var framesRecorded = new List<MotionFrame>(frames);
        var maxJump = 0.0;
        for (var index = 0; index < frames; index++)
        {
            if (index == frames / 2)
            {
                midTrigger?.Invoke();
            }

            time.Advance(step);
            var advanceWatch = System.Diagnostics.Stopwatch.StartNew();
            controller.Advance(step);
            advanceWatch.Stop();
            var state = controller.RenderState;

            var renderWatch = System.Diagnostics.Stopwatch.StartNew();
            using var bitmap = renderer.RenderToBitmap(state, 1.5f);
            renderWatch.Stop();
            var crop = IslandCrop(controller, state, 1.5f, bitmap.Width, bitmap.Height);

            var encodeWatch = System.Diagnostics.Stopwatch.StartNew();
            var hash = SavePng(bitmap, Path.Combine(segmentDir, $"frame-{index:000}.png"), crop);
            encodeWatch.Stop();

            framesRecorded.Add(new MotionFrame(
                index,
                advanceWatch.Elapsed.TotalMilliseconds,
                renderWatch.Elapsed.TotalMilliseconds,
                encodeWatch.Elapsed.TotalMilliseconds,
                advanceWatch.Elapsed.TotalMilliseconds + renderWatch.Elapsed.TotalMilliseconds + encodeWatch.Elapsed.TotalMilliseconds,
                step.TotalMilliseconds,
                metric(),
                state.ShellWidth,
                state.ShellHeight,
                state.Form.ToString(),
                controller.IsAnimating,
                hash));
        }

        var segment = new MotionSegment { Name = name, Direction = direction, Frames = framesRecorded };
        var peakIndex = direction == MotionDirection.Decreasing
            ? framesRecorded.IndexOf(framesRecorded.MinBy(static frame => frame.Metric)!)
            : framesRecorded.IndexOf(framesRecorded.MaxBy(static frame => frame.Metric)!);
        var finalMetric = framesRecorded[^1].Metric;
        for (var index = 1; index < framesRecorded.Count; index++)
        {
            var previous = framesRecorded[index - 1];
            var current = framesRecorded[index];
            var delta = current.Metric - previous.Metric;
            maxJump = Math.Max(maxJump, Math.Abs(delta));

            if (index <= peakIndex)
            {
                // Only motion that stalls or reverses before the target is reached is a defect; the damped
                // spring settling afterwards legitimately oscillates around the target.
                var backJump = direction == MotionDirection.Decreasing ? delta > 1e-4 : delta < -1e-4;
                if (backJump)
                {
                    segment.BackJumpCount++;
                }

                if (Math.Abs(delta) < 1e-6)
                {
                    segment.StalledFrames++;
                }

                if (current.Hash == previous.Hash)
                {
                    segment.DuplicateFrames++;
                }
            }
            else
            {
                segment.Overshoot = Math.Max(segment.Overshoot, Math.Abs(current.Metric - finalMetric));
            }
        }

        segment.MaxJump = maxJump;
        return segment;
    }

    /// <summary>Pixels (at dpiScale) of the island plus a shadow margin, so frames are not mostly empty canvas.</summary>
    private static System.Drawing.Rectangle? IslandCrop(
        SkiaIslandController controller,
        IslandRenderState state,
        float dpiScale,
        int bitmapWidth,
        int bitmapHeight)
    {
        if (state.ShellWidth < 1 || state.ShellHeight < 1)
        {
            return null;
        }

        var origin = controller.Motion.ShellOrigin;
        const double pad = 40;
        var left = (int)Math.Round((origin.X - pad) * dpiScale);
        var top = (int)Math.Round((origin.Y - pad) * dpiScale);
        var right = (int)Math.Round((origin.X + state.ShellWidth + pad) * dpiScale);
        var bottom = (int)Math.Round((origin.Y + state.ShellHeight + pad) * dpiScale);
        left = Math.Clamp(left, 0, bitmapWidth - 1);
        top = Math.Clamp(top, 0, bitmapHeight - 1);
        right = Math.Clamp(right, left + 1, bitmapWidth);
        bottom = Math.Clamp(bottom, top + 1, bitmapHeight);
        return new System.Drawing.Rectangle(left, top, right - left, bottom - top);
    }

    private static void Settle(SkiaIslandController controller, HarnessTimeProvider time, TimeSpan step)
    {
        var guard = 0;
        while (controller.IsAnimating && guard++ < 900)
        {
            time.Advance(step);
            controller.Advance(step);
        }
    }

    private static void WriteTimingCsv(string outputDir, IReadOnlyList<MotionSegment> segments)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("segment,frame,advance_ms,render_ms,encode_ms,wall_ms,simulated_ms,metric,width,height,form,animating_after");
        foreach (var segment in segments)
        {
            foreach (var frame in segment.Frames)
            {
                builder.Append(segment.Name).Append(',')
                    .Append(frame.Index).Append(',')
                    .Append(frame.AdvanceMs.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                    .Append(frame.RenderMs.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                    .Append(frame.EncodeMs.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                    .Append(frame.WallMs.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                    .Append(frame.SimulatedMs.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                    .Append(frame.Metric.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                    .Append(frame.Width.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                    .Append(frame.Height.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                    .Append(frame.Form).Append(',')
                    .Append(frame.AnimatingAfter ? "1" : "0")
                    .AppendLine();
            }
        }

        File.WriteAllText(Path.Combine(outputDir, "timing.csv"), builder.ToString(), System.Text.Encoding.UTF8);
    }

    private static void WriteMotionReport(string outputDir, IReadOnlyList<MotionSegment> segments)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("# Motion capture — 60 fps frame evidence").AppendLine();
        builder.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}").AppendLine();
        builder.AppendLine("Driver: real SkiaIslandController (motion/pager/split springs) + IslandRenderer.RenderToBitmap at 1.5x, 3 warm-up frames before capture.");
        builder.AppendLine("Per-frame cost is split into advance (controller), render (Skia) and encode (PNG, harness overhead); the 16.7 ms budget is judged on advance + render (ui).");
        builder.AppendLine();
        builder.AppendLine("| segment | frames | duration (sim) | advance p50/p99 | render p50/p99 | encode p50/p99 | ui p50/p99 | frames > 16.7 ms (ui) | back-jumps | stalled | duplicate | settle overshoot | max step |");
        builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var segment in segments)
        {
            var advance = $"{segment.Percentile(static frame => frame.AdvanceMs, 50):0.00}/{segment.Percentile(static frame => frame.AdvanceMs, 99):0.00} ms";
            var render = $"{segment.Percentile(static frame => frame.RenderMs, 50):0.00}/{segment.Percentile(static frame => frame.RenderMs, 99):0.00} ms";
            var encode = $"{segment.Percentile(static frame => frame.EncodeMs, 50):0.00}/{segment.Percentile(static frame => frame.EncodeMs, 99):0.00} ms";
            var ui = $"{segment.Percentile(50):0.00}/{segment.Percentile(99):0.00} ms";
            builder.AppendLine($"| {segment.Name} | {segment.Frames.Count} | {segment.Frames.Count / 60.0:0.00} s | {advance} | {render} | {encode} | {ui} | {segment.OverBudgetCount} | {segment.BackJumpCount} | {segment.StalledFrames} | {segment.DuplicateFrames} | {segment.Overshoot:0.000} | {segment.MaxJump:0.000} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Notes");
        builder.AppendLine("- back-jumps: frames before the target is reached where the driving metric moves opposite to the expected direction (expand/page: rising; collapse: falling; satellite: rising until the fusion trigger, then falling).");
        builder.AppendLine("- stalled / duplicate: frames before the target where the metric does not change, or the encoded frame is byte-identical to the previous one (a dropped or duplicated frame on screen).");
        builder.AppendLine("- settle overshoot: after the peak, the largest distance from the final value; damped spring oscillation is expected there and is not counted as a back-jump.");
        builder.AppendLine("- ui = advance + render (controller + Skia) and is what the 16.7 ms budget is judged on; encode (PNG at 1.5x) is harness overhead and is reported separately.");
        builder.AppendLine("- Per-frame bitmaps: expand/, collapse/, page/, satellite/ (frame-000.png ...). Raw timings: timing.csv (advance_ms / render_ms / encode_ms / wall_ms columns).");
        builder.AppendLine("- Key frames (2x): expand-frame-030-2x.png, page-frame-030-2x.png, satellite-frame-015-2x.png.");
        builder.AppendLine("- Frame crop: island bounds + 40 DIP margin at 1.5x, composited on #08080A.");
        File.WriteAllText(Path.Combine(outputDir, "motion-capture.md"), builder.ToString(), System.Text.Encoding.UTF8);
    }

    private static void BuildMotionStrip(string outputDir, IReadOnlyList<MotionSegment> segments)
    {
        const int columns = 8;
        const int thumbWidth = 220;
        const int labelHeight = 26;
        const int padding = 8;
        var first = Path.Combine(outputDir, segments[0].Name, "frame-000.png");
        using var probe = System.Drawing.Image.FromFile(first);
        var thumbHeight = (int)Math.Round(thumbWidth * (double)probe.Height / probe.Width);
        var width = (columns * (thumbWidth + padding)) + padding;
        var height = (segments.Count * (thumbHeight + labelHeight + padding)) + padding;

        using var target = new System.Drawing.Bitmap(width, height);
        using var graphics = System.Drawing.Graphics.FromImage(target);
        graphics.Clear(System.Drawing.Color.FromArgb(5, 5, 7));
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        using var font = new System.Drawing.Font("Segoe UI", 11, System.Drawing.FontStyle.Bold);
        using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(237, 237, 238));

        for (var row = 0; row < segments.Count; row++)
        {
            var segment = segments[row];
            var top = padding + (row * (thumbHeight + labelHeight + padding));
            var stride = Math.Max(1, segment.Frames.Count / columns);
            graphics.DrawString($"{segment.Name} — every {stride} frames", font, brush, padding, top);
            for (var column = 0; column < columns; column++)
            {
                var frameIndex = Math.Min(segment.Frames.Count - 1, column * stride);
                var file = Path.Combine(outputDir, segment.Name, $"frame-{frameIndex:000}.png");
                using var image = System.Drawing.Image.FromFile(file);
                graphics.DrawImage(
                    image,
                    new System.Drawing.Rectangle(padding + (column * (thumbWidth + padding)), top + labelHeight, thumbWidth, thumbHeight));
            }
        }

        target.Save(Path.Combine(outputDir, "motion-strip.png"), System.Drawing.Imaging.ImageFormat.Png);
    }

    /// <summary>2x upscales of the frames humans should eyeball (one per motion family).</summary>
    private static void BuildKeyFrames(string outputDir, IReadOnlyList<(string Segment, int Frame)> keys)
    {
        foreach (var (segment, frame) in keys)
        {
            var sourcePath = Path.Combine(outputDir, segment, $"frame-{frame:000}.png");
            var targetPath = Path.Combine(outputDir, $"{segment}-frame-{frame:000}-2x.png");
            using var source = System.Drawing.Image.FromFile(sourcePath);
            using var target = new System.Drawing.Bitmap(source.Width * 2, source.Height * 2);
            using var graphics = System.Drawing.Graphics.FromImage(target);
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, new System.Drawing.Rectangle(0, 0, target.Width, target.Height));
            target.Save(targetPath, System.Drawing.Imaging.ImageFormat.Png);
        }
    }

    private static ulong SavePng(SKBitmap bitmap, string path, System.Drawing.Rectangle? crop = null)
    {
        SKBitmap? cropped = null;
        try
        {
            var source = bitmap;
            if (crop is { } region && (region.Width < bitmap.Width || region.Height < bitmap.Height))
            {
                cropped = new SKBitmap(new SKImageInfo(region.Width, region.Height, bitmap.ColorType, bitmap.AlphaType));
                using (var canvas = new SKCanvas(cropped))
                {
                    // Host surface is transparent; composite on the desktop-ish L0 shade so frames read as
                    // screenshots instead of black-on-checkerboard.
                    canvas.Clear(new SKColor(8, 8, 10));
                    canvas.DrawBitmap(bitmap, -region.Left, -region.Top);
                }

                source = cropped;
            }

            using var image = SKImage.FromBitmap(source);
            using var data = image.Encode(SKEncodedImageFormat.Png, 92);
            var bytes = data.ToArray();
            File.WriteAllBytes(path, bytes);
            var hash = System.Security.Cryptography.SHA256.HashData(bytes);
            return BitConverter.ToUInt64(hash, 0);
        }
        finally
        {
            cropped?.Dispose();
        }
    }

    /// <summary>Minimal localizer for the headless motion capture (same keys the island pages resolve).</summary>
    private sealed class HarnessLocalizer : ILocalizer
    {
        private readonly Dictionary<string, string> _messages = new(StringComparer.Ordinal)
        {
            ["Page.Home.Title"] = "Control center",
            ["Page.Media.Title"] = "Now playing",
            ["Page.Notifications.Title"] = "Notifications",
            ["Page.Clipboard.Title"] = "Clipboard",
            ["Page.Agent.Title"] = "Agent activity",
            ["Page.Widgets.Title"] = "Widgets",
            ["Page.Widgets.Tasks"] = "Today",
            ["Page.Widgets.Today"] = "Today",
            ["Page.Home.FocusTime"] = "Focus",
            ["Page.Home.System"] = "System",
            ["Page.Widgets.Task.Review"] = "Review",
            ["Page.Widgets.Task.Ship"] = "Ship",
            ["Page.Widgets.Task.Inbox"] = "Inbox",
            ["Common.Next"] = "Next",
            ["Common.Charging"] = "Charging",
            ["Common.Copied"] = "Copied",
            ["Media.NowPlaying"] = "Now playing",
            ["Media.NoSession"] = "Nothing playing",
            ["Media.UnknownTitle"] = "Unknown title",
            ["Media.UnknownArtist"] = "Unknown artist",
            ["Clipboard.Image"] = "Image",
            ["Agent.Running"] = "Running",
            ["Agent.Completed"] = "Completed",
            ["Agent.Title"] = "Agent",
            ["Rail.Home"] = "Home",
            ["Rail.Media"] = "Media",
            ["Rail.Notifications"] = "Alerts",
            ["Rail.Clipboard"] = "Clip",
            ["Rail.Agent"] = "Agent",
            ["Rail.Widgets"] = "Widgets",
            ["Battery.Low"] = "Low battery",
            ["Timer.Pomodoro"] = "Pomodoro",
            ["Activity.Idle"] = "Idle",
        };

        public string Culture => "en-US";

        public int Version => 1;

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<string> AvailableCultures => ["en-US"];

        public bool IsRightToLeft => false;

        public System.Globalization.CultureInfo CurrentCulture => System.Globalization.CultureInfo.GetCultureInfo("en-US");

        public string GetCultureDisplayName(string cultureTag) => cultureTag;

        public string Get(string key)
        {
            if (_messages.TryGetValue(key, out var value))
            {
                return value;
            }

            // Fallback: "Activity.Bluetooth" -> "Bluetooth" so unknown host keys still read
            // like labels instead of raw resource ids in the evidence frames.
            var tail = key.Split('.').LastOrDefault();
            return string.IsNullOrWhiteSpace(tail) ? key : tail;
        }

        public string Format(string key, IReadOnlyDictionary<string, object?> arguments) => Get(key);

        public string Format(string key, params (string Name, object? Value)[] arguments) => Get(key);

        public bool TryGet(string key, out string value) => _messages.TryGetValue(key, out value!);
    }

    /// <summary>Manual clock: the capture loop owns time so every frame is deterministic.</summary>
    private sealed class HarnessTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public void Advance(TimeSpan delta) => _now += delta;
    }

    private static int RunSettingsMaterialCapture(Options options, SettingsWindowSection section)
    {
        var exitCode = 1;
        var builder = AppBuilder.Configure<EvidenceApplication>().UsePlatformDetect();
        var lifetime = new ClassicDesktopStyleApplicationLifetime
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };
        builder.SetupWithLifetime(lifetime);
        lifetime.Startup += (_, _) => _ = CaptureSettingsMaterialAsync(lifetime, options, section, code => exitCode = code);
        lifetime.Start(Array.Empty<string>());
        return exitCode;
    }

    private static async Task CaptureSettingsMaterialAsync(
        ClassicDesktopStyleApplicationLifetime lifetime,
        Options options,
        SettingsWindowSection section,
        Action<int> setExitCode)
    {
        var status = 1;
        try
        {
            var outputDir = Path.Combine(options.RepoRoot, "build", "reports", "ui-audit", "material");
            Directory.CreateDirectory(outputDir);

            var localizer = LocalizationService.CreateDefault("en-US", showMissingKeys: false, enablePseudoLocale: false);
            LocalizationRuntime.Current = localizer;
            var settingsStore = new InMemoryUiSettingsStore(AppSettings.Default);
            var controller = new SettingsController(AppSettings.Default, settingsStore, NullAppLogger.Instance);
            var window = new SettingsWindow(controller, localizer, section);
            window.Show();
            await Task.Delay(1600);
            Dispatcher.UIThread.RunJobs();

            var content = window.Content as Control
                ?? throw new InvalidOperationException("The settings window has no content.");

            var captures = new List<object>();
            var rowBounds = new Dictionary<string, object>(StringComparer.Ordinal);
            Point? rowCardOrigin = null;
            Size rowCardSize = default;
            var states = new (SettingsWindow.SettingsMaterialVisualState State, string File)[]
            {
                (SettingsWindow.SettingsMaterialVisualState.Idle, "settings-idle.png"),
                (SettingsWindow.SettingsMaterialVisualState.Pressed, "settings-press.png"),
                (SettingsWindow.SettingsMaterialVisualState.Hover, "settings-hover.png"),
            };

            foreach (var (state, file) in states)
            {
                var applied = window.ForceMaterialState(state);
                await Task.Delay(300);
                Dispatcher.UIThread.RunJobs();

                // Re-apply right before the render: a background refresh may have rebuilt the section and
                // dropped the shadow between the first call and the capture.
                applied = Math.Max(applied, window.ForceMaterialState(state));
                Dispatcher.UIThread.RunJobs();
                var matchedBorders = window.GetVisualDescendants().OfType<Border>()
                    .Where(static border => border.Child is Grid && border.BorderThickness.Left == 1 && border.CornerRadius.TopLeft > 0)
                    .ToArray();
                var matched = matchedBorders
                    .Select(static border => new
                    {
                        x = Math.Round(border.Bounds.X, 1),
                        y = Math.Round(border.Bounds.Y, 1),
                        w = Math.Round(border.Bounds.Width, 1),
                        h = Math.Round(border.Bounds.Height, 1),
                        child = border.Child?.GetType().Name,
                    })
                    .ToArray();

                // First row card (h > 40): its Bounds must stay identical for every state (scale is draw-only).
                var rowCard = matchedBorders.FirstOrDefault(static border => border.Bounds.Height > 40);
                var rowOrigin = rowCard?.TranslatePoint(new Point(0, 0), content);
                if (rowCard is not null)
                {
                    rowCardOrigin ??= rowOrigin;
                    rowCardSize = rowCard.Bounds.Size;
                }

                var rowSnapshot = rowCard is null
                    ? null
                    : new
                    {
                        // Layout bounds (must not change) plus the draw-time position (includes RenderTransform).
                        layoutWidth = Math.Round(rowCard.Bounds.Width, 2),
                        layoutHeight = Math.Round(rowCard.Bounds.Height, 2),
                        visualX = rowOrigin is { } point ? Math.Round(point.X, 2) : (double?)null,
                        visualY = rowOrigin is { } pointY ? Math.Round(pointY.Y, 2) : (double?)null,
                    };
                if (rowSnapshot is not null)
                {
                    rowBounds[state.ToString()] = rowSnapshot;
                }

                var full = Path.Combine(outputDir, file);
                captures.Add(new { state = state.ToString(), cardsMatched = matched, rowCard = rowSnapshot });
                captures.Add(Capture(content, full));
                var zoom = Path.Combine(outputDir, file.Replace(".png", "-card2x.png", StringComparison.Ordinal));
                CropAndZoom(full, zoom, CardCropRegion(content.Bounds.Width, content.Bounds.Height));
                captures.Add(new { state = state.ToString(), cards = applied, zoom });
            }

            var deltaRegion = CardCropRegion(900, 620);
            var pressDelta = MeasureDelta(
                Path.Combine(outputDir, "settings-idle.png"),
                Path.Combine(outputDir, "settings-press.png"),
                deltaRegion);
            var hoverDelta = MeasureDelta(
                Path.Combine(outputDir, "settings-idle.png"),
                Path.Combine(outputDir, "settings-hover.png"),
                deltaRegion);
            var deltas = new
            {
                region = new { deltaRegion.Left, deltaRegion.Top, deltaRegion.Width, deltaRegion.Height },
                pressVsIdle = new { mean = pressDelta.Mean, max = pressDelta.Max, changedPixels = pressDelta.ChangedPixels },
                hoverVsIdle = new { mean = hoverDelta.Mean, max = hoverDelta.Max, changedPixels = hoverDelta.ChangedPixels },
            };

            object? edgeComparison = null;
            if (rowCardOrigin is { } cardOrigin)
            {
                var edge = Path.Combine(outputDir, "settings-press-edge4x.png");
                BuildEdgeComparison(
                    edge,
                    Path.Combine(outputDir, "settings-idle.png"),
                    Path.Combine(outputDir, "settings-press.png"),
                    cardOrigin,
                    rowCardSize);
                edgeComparison = new
                {
                    path = edge,
                    x = Math.Round(cardOrigin.X, 2),
                    y = Math.Round(cardOrigin.Y, 2),
                    profile = MeasureEdgeProfile(
                        Path.Combine(outputDir, "settings-idle.png"),
                        Path.Combine(outputDir, "settings-press.png"),
                        cardOrigin),
                };
            }

            var comparison = Path.Combine(outputDir, "settings-material-states-compare.png");
            BuildComparison(
                comparison,
                ["Idle (baseline)", "Pressed — L4 inset (black 18 %, top inset 8, σ 4)", "Hover — L3 soft glow (white 8 %)"],
                states.Select(state => Path.Combine(outputDir, state.File)).ToArray());
            captures.Add(new { comparison });

            var report = new
            {
                section = section.ToString(),
                pixelDelta = deltas,
                boundsBefore = rowBounds.GetValueOrDefault("Idle"),
                boundsAfter = rowBounds.GetValueOrDefault("Pressed"),
                rowCardBounds = rowBounds,
                edgeComparison,
                captures,
            };
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(
                Path.Combine(outputDir, "settings-material-states.json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            window.Close();
            status = 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
        }
        finally
        {
            setExitCode(status);
            Dispatcher.UIThread.Post(() => lifetime.Shutdown(status));
        }
    }

    /// <summary>Card region (window DIP) used for the 2x zoom: the first two plugin rows on the Plugins page.</summary>
    private static System.Drawing.Rectangle CardCropRegion(double contentWidth, double contentHeight)
    {
        _ = contentWidth;
        _ = contentHeight;
        return new System.Drawing.Rectangle(238, 198, 632, 176);
    }

    private static void CropAndZoom(string sourcePng, string targetPng, System.Drawing.Rectangle region, int scale = 2)
    {
        using var source = System.Drawing.Image.FromFile(sourcePng);
        var width = Math.Min(region.Width, source.Width - region.Left);
        var height = Math.Min(region.Height, source.Height - region.Top);
        using var target = new System.Drawing.Bitmap(width * scale, height * scale);
        using (var graphics = System.Drawing.Graphics.FromImage(target))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            graphics.DrawImage(
                source,
                new System.Drawing.Rectangle(0, 0, width * scale, height * scale),
                new System.Drawing.Rectangle(region.Left, region.Top, width, height),
                System.Drawing.GraphicsUnit.Pixel);
        }

        target.Save(targetPng, System.Drawing.Imaging.ImageFormat.Png);
    }

    /// <summary>Mean/max per-channel delta between two PNGs inside a region (press/hover evidence numbers).</summary>
    private static (double Mean, double Max, int ChangedPixels) MeasureDelta(string leftPng, string rightPng, System.Drawing.Rectangle region)
    {
        using var left = System.Drawing.Image.FromFile(leftPng) as System.Drawing.Bitmap
            ?? throw new InvalidOperationException($"'{leftPng}' is not a bitmap.");
        using var rightSource = System.Drawing.Image.FromFile(rightPng) as System.Drawing.Bitmap
            ?? throw new InvalidOperationException($"'{rightPng}' is not a bitmap.");
        var sum = 0.0;
        var max = 0.0;
        var changed = 0;
        var count = 0;
        for (var y = region.Top; y < region.Top + region.Height; y++)
        {
            for (var x = region.Left; x < region.Left + region.Width; x++)
            {
                var a = left.GetPixel(x, y);
                var b = rightSource.GetPixel(x, y);
                var delta = (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B)) / 3.0;
                sum += delta;
                count++;
                if (delta > 1.0)
                {
                    changed++;
                }

                max = Math.Max(max, delta);
            }
        }

        return (Math.Round(sum / Math.Max(1, count), 2), Math.Round(max, 1), changed);
    }

    /// <summary>Stacks the same card crop for idle/pressed/hover into one labelled comparison sheet.</summary>
    private static void BuildComparison(string targetPng, string[] labels, string[] files)
    {
        var region = CardCropRegion(900, 620);
        const int scale = 2;
        const int labelHeight = 34;
        var width = region.Width * scale;
        var height = region.Height * scale;

        using var target = new System.Drawing.Bitmap(width, (labelHeight + height) * labels.Length);
        using var graphics = System.Drawing.Graphics.FromImage(target);
        graphics.Clear(System.Drawing.Color.FromArgb(5, 5, 7));
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        using var font = new System.Drawing.Font("Segoe UI", 15, System.Drawing.FontStyle.Bold);
        using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(237, 237, 238));

        for (var index = 0; index < labels.Length; index++)
        {
            var blockTop = index * (labelHeight + height);
            using var source = System.Drawing.Image.FromFile(files[index]);
            graphics.DrawImage(
                source,
                new System.Drawing.Rectangle(0, blockTop + labelHeight, width, height),
                new System.Drawing.Rectangle(region.Left, region.Top, region.Width, region.Height),
                System.Drawing.GraphicsUnit.Pixel);
            graphics.DrawString(labels[index], font, brush, 10, blockTop + 6);
        }

        target.Save(targetPng, System.Drawing.Imaging.ImageFormat.Png);
    }

    /// <summary>
    /// 4x crop of the first row card's top edge: idle vs pressed. Proves the draw-only 0.99 scale neither
    /// clips the top edge (no missing pixel row) nor overlaps the neighbouring row.
    /// </summary>
    /// <summary>Per-row mean luminance around the first row card's top edge (idle vs pressed), proving the
    /// draw-only scale does not move the edge into the gap above the card.</summary>
    private static object MeasureEdgeProfile(string idlePng, string pressPng, Point origin)
    {
        using var idle = System.Drawing.Image.FromFile(idlePng) as System.Drawing.Bitmap
            ?? throw new InvalidOperationException("idle capture is not a bitmap.");
        using var press = System.Drawing.Image.FromFile(pressPng) as System.Drawing.Bitmap
            ?? throw new InvalidOperationException("press capture is not a bitmap.");

        var left = (int)Math.Round(origin.X) + 40;
        var right = Math.Min(left + 160, idle.Width - 1);
        var top = (int)Math.Round(origin.Y);
        static double Luminance(System.Drawing.Color color) => (0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B);
        double RowMean(System.Drawing.Bitmap bitmap, int y)
        {
            var sum = 0.0;
            for (var x = left; x < right; x++)
            {
                sum += Luminance(bitmap.GetPixel(x, y));
            }

            return Math.Round(sum / Math.Max(1, right - left), 3);
        }

        var rows = new List<object>();
        for (var y = top - 4; y <= top + 8; y++)
        {
            if (y < 0 || y >= idle.Height)
            {
                continue;
            }

            rows.Add(new { y, idle = RowMean(idle, y), press = RowMean(press, y) });
        }

        return new { cardTopY = top, rows };
    }

    private static void BuildEdgeComparison(string targetPng, string idlePng, string pressPng, Point origin, Size cardSize)
    {
        // Whole first card, 2x: the top-edge treatment (1.5 DIP highlight + 8 DIP L4 band + 0.99 scale)
        // is visible without guessing a sub-pixel crop window.
        var x = Math.Max(0, (int)Math.Round(origin.X));
        var y = Math.Max(0, (int)Math.Round(origin.Y));
        var width = Math.Max(48, Math.Min(640, (int)Math.Round(cardSize.Width)));
        var height = Math.Max(24, Math.Min(96, (int)Math.Round(cardSize.Height)));
        const int scale = 2;
        const int labelHeight = 30;
        var labels = new[]
        {
            "Idle — first row card 2x (baseline)",
            "Pressed — 0.99 draw scale + L4 8 DIP band + 1.5 DIP top highlight",
            "Delta x6 — white = where pressed differs (top band / edges only)",
        };

        using var idle = System.Drawing.Image.FromFile(idlePng) as System.Drawing.Bitmap
            ?? throw new InvalidOperationException("idle capture is not a bitmap.");
        using var press = System.Drawing.Image.FromFile(pressPng) as System.Drawing.Bitmap
            ?? throw new InvalidOperationException("press capture is not a bitmap.");

        using var delta = new System.Drawing.Bitmap(width, height);
        for (var dy = 0; dy < height; dy++)
        {
            for (var dx = 0; dx < width; dx++)
            {
                var a = idle.GetPixel(x + dx, y + dy);
                var b = press.GetPixel(x + dx, y + dy);
                var difference = (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B)) / 3.0;
                var value = (int)Math.Clamp(Math.Round(difference * 8), 0, 255);
                delta.SetPixel(dx, dy, System.Drawing.Color.FromArgb(value, value, value));
            }
        }

        using var target = new System.Drawing.Bitmap(width * scale, (labelHeight + (height * scale)) * labels.Length);
        using var graphics = System.Drawing.Graphics.FromImage(target);
        graphics.Clear(System.Drawing.Color.FromArgb(5, 5, 7));
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        using var font = new System.Drawing.Font("Segoe UI", 12, System.Drawing.FontStyle.Bold);
        using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(237, 237, 238));

        for (var index = 0; index < labels.Length; index++)
        {
            var blockTop = index * (labelHeight + (height * scale));
            var source = index switch
            {
                0 => (System.Drawing.Image)idle,
                1 => press,
                _ => delta,
            };
            graphics.DrawImage(
                source,
                new System.Drawing.Rectangle(0, blockTop + labelHeight, width * scale, height * scale),
                new System.Drawing.Rectangle(index == 2 ? 0 : x, index == 2 ? 0 : y, width, height),
                System.Drawing.GraphicsUnit.Pixel);
            graphics.DrawString(labels[index], font, brush, 8, blockTop + 8);
        }

        target.Save(targetPng, System.Drawing.Imaging.ImageFormat.Png);
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
