using System.Security.Cryptography;
using System.Text.Json;
using FluidBar.Marketplace.Index;
using FluidBar.Marketplace.Packaging;
using FluidBar.Marketplace.Signing;
using FluidBar.PluginHost.Loader;
using FluidBar.PluginHost.Logging;
using FluidBar.Sdk.Capabilities;
using FluidBar.Sdk.Json;
using FluidBar.Sdk.Localization;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Versioning;

return FluidBarCli.Run(args);

internal static class FluidBarCli
{
    private const string Version = "2.0.0";

    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "-h" or "--help")
        {
            PrintUsage();
            return 0;
        }

        try
        {
            return args[0] switch
            {
                "validate" => Validate(args),
                "pack" => Pack(args),
                "sign" => Sign(args),
                "sign-index" => SignIndex(args),
                "sample-index" => SampleIndex(args),
                "keygen" => KeyGen(args),
                "publish-prepare" => PublishPrepare(args),
                "verify-index" => VerifyIndex(args),
                "policy" => Policy(args),
                "dev" => Dev(args),
                "version" or "--version" => PrintVersion(),
                _ => Unknown(args[0]),
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or CryptographicException or FormatException)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 1;
        }
    }

    private static int PrintVersion()
    {
        Console.WriteLine($"fluidbar-plugin {Version} (SDK apiVersion {PluginApiVersion.Current})");
        return 0;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"error: unknown command '{command}'.");
        PrintUsage();
        return 2;
    }

    private static int Validate(string[] args)
    {
        var options = ParseOptions(args);
        var pluginPath = RequireOption(options, "plugin");
        var configuration = Option(options, "configuration") ?? "Debug";
        _ = ResolvePackageLayout(pluginPath, configuration, out var staging, out var resolvedDirectory);
        using (staging)
        {
            var manifest = ReadManifest(resolvedDirectory);
            var errors = new List<string>();
            var validation = PluginManifestValidator.Validate(manifest);
            errors.AddRange(validation.Errors.Select(static issue => issue.ToString()));
            var warnings = validation.Warnings.Select(static issue => issue.ToString()).ToList();
            ValidateLocales(resolvedDirectory, manifest, errors, warnings);

            var inspection = PluginPackageInspector.Inspect(resolvedDirectory, manifest, new HostAssemblyCatalog());
            errors.AddRange(inspection.Errors.Select(static issue => issue.ToString()));

            PrintIssues(errors, warnings);
            Console.WriteLine(errors.Count == 0
                ? $"validate OK: {manifest.Id} v{manifest.Version} (apiVersion {manifest.ApiVersion})"
                : $"validate FAILED: {errors.Count} error(s)");
            return errors.Count == 0 ? 0 : 2;
        }
    }

    private static int Pack(string[] args)
    {
        var options = ParseOptions(args);
        var pluginPath = RequireOption(options, "plugin");
        var output = RequireOption(options, "out");
        var configuration = Option(options, "configuration") ?? "Release";
        _ = ResolvePackageLayout(pluginPath, configuration, out var staging, out var resolvedDirectory);
        using (staging)
        {
            var result = FbpPackager.PackDirectory(resolvedDirectory, output, new FbpPackagerOptions { ValidateMarketplaceLayout = true });
            Console.WriteLine($"pack OK: {result.PackagePath}");
            Console.WriteLine($"  sha256: {result.Sha256}");
            Console.WriteLine($"  size:   {result.Size} bytes");
            Console.WriteLine($"  entries: {result.Entries.Count}");
            foreach (var entry in result.Entries)
            {
                Console.WriteLine($"    {entry}");
            }

            return 0;
        }
    }

    private static int Sign(string[] args)
    {
        var options = ParseOptions(args);
        var package = RequireOption(options, "package");
        var keyPath = RequireOption(options, "key");
        var keyId = RequireOption(options, "key-id");
        var role = (Option(options, "role") ?? "marketplace") switch
        {
            "marketplace" => SignatureRole.Marketplace,
            "author" or "publisher" => SignatureRole.Author,
            var other => throw new FormatException($"Unknown role '{other}' (use marketplace|author)."),
        };
        var output = Option(options, "out") ?? package + ".sig.json";
        using var key = LoadKey(keyPath);
        var document = PackageSigner.SignPackageFileAsync(package, output, key, keyId, role).GetAwaiter().GetResult();
        Console.WriteLine($"sign OK: {output}");
        Console.WriteLine($"  package sha256: {document.Package.Sha256}");
        Console.WriteLine($"  key: {keyId} ({role})");
        return 0;
    }

    private static int SignIndex(string[] args)
    {
        var options = ParseOptions(args);
        var indexPath = RequireOption(options, "index");
        var keyPath = RequireOption(options, "key");
        var keyId = RequireOption(options, "key-id");
        var output = Option(options, "out") ?? indexPath;
        var index = JsonSerializer.Deserialize<MarketplaceIndexDocument>(
            File.ReadAllText(indexPath),
            PluginJson.CreateStrictOptions()) ?? throw new FormatException("Index payload is empty.");
        using var key = LoadKey(keyPath);
        var unsigned = index with { Signatures = [] };
        var signature = IndexSigner.Sign(unsigned, key, keyId);
        var signedJson = IndexSigner.CreateSignedJson(unsigned, [signature]);
        File.WriteAllText(output, signedJson);
        Console.WriteLine($"sign-index OK: {output}");
        Console.WriteLine($"  generatedAt: {index.GeneratedAt:O}");
        Console.WriteLine($"  key: {keyId}");
        return 0;
    }

    private static int KeyGen(string[] args)
    {
        var options = ParseOptions(args);
        var output = RequireOption(options, "out");
        var publicOutput = Option(options, "public-out");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(output, key.ExportPkcs8PrivateKeyPem());
        Console.WriteLine($"keygen OK: {output} (ECDSA P-256 PKCS#8 PEM; keep this private key safe)");
        if (publicOutput is not null)
        {
            File.WriteAllText(publicOutput, EcdsaKeyMaterial.ExportPublicKeySpkiBase64(key));
            Console.WriteLine($"public key (SPKI base64): {publicOutput}");
        }
        else
        {
            Console.WriteLine($"public key (SPKI base64): {EcdsaKeyMaterial.ExportPublicKeySpkiBase64(key)}");
        }

        return 0;
    }

    /// <summary>
    /// CI/offline publish pipeline: pack every plugin under --plugins-root, verify deterministic hashes, sign the
    /// packages with the marketplace key (from a protected file or an environment variable), then regenerate the
    /// signed root index + per-plugin shards + keys.json under --marketplace.
    /// </summary>
    private static int PublishPrepare(string[] args)
    {
        var options = ParseOptions(args);
        var pluginsRoot = RequireOption(options, "plugins-root");
        var artifactsDirectory = RequireOption(options, "artifacts");
        var marketplaceDirectory = RequireOption(options, "marketplace");
        var keyId = RequireOption(options, "key-id");
        var configuration = Option(options, "configuration") ?? "Release";
        var artifactBaseUrl = Option(options, "artifact-base-url") ?? "https://github.com/fluidbar/plugins/releases/download";
        var marketplaceBaseUrl = Option(options, "marketplace-base-url") ?? "https://marketplace.fluidbar.app";
        var channel = Option(options, "channel") ?? "stable";
        var sourceCommit = Option(options, "source-commit") ?? Environment.GetEnvironmentVariable("GITHUB_SHA") ?? new string('0', 40);
        var generatedAtText = Option(options, "generated-at");
        var generatedAt = string.IsNullOrWhiteSpace(generatedAtText)
            ? (DateTimeOffset?)null
            : DateTimeOffset.Parse(generatedAtText, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
        var assetBaseUrl = Option(options, "asset-base-url");

        using var signingKey = LoadKeyFromOptions(options);
        Directory.CreateDirectory(artifactsDirectory);
        var artifactList = new List<MarketplacePluginArtifact>();
        var pluginDirectories = Directory.EnumerateDirectories(Path.GetFullPath(pluginsRoot))
            .Where(static directory =>
            {
                var name = Path.GetFileName(directory);
                return !name.StartsWith('.') &&
                       !name.StartsWith('_') &&
                       File.Exists(Path.Combine(directory, "manifest.json"));
            })
            .OrderBy(static directory => directory, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (pluginDirectories.Length == 0)
        {
            Console.Error.WriteLine($"error: no plugin directories found under '{pluginsRoot}'.");
            return 2;
        }

        foreach (var pluginDirectory in pluginDirectories)
        {
            _ = ResolvePackageLayout(pluginDirectory, configuration, out var staging, out var resolvedDirectory);
            using (staging)
            {
                var manifest = ReadManifest(resolvedDirectory);
                var baseName = $"{manifest.Id}-{manifest.Version}";
                var packagePath = Path.Combine(Path.GetFullPath(artifactsDirectory), baseName + ".fbp");
                var build = FbpPackager.PackDirectory(
                    resolvedDirectory,
                    packagePath,
                    new FbpPackagerOptions { ValidateMarketplaceLayout = true });

                // Determinism gate: a second pack of the same input must produce the identical SHA-256.
                var determinismPath = packagePath + ".determinism-check.fbp";
                try
                {
                    var second = FbpPackager.PackDirectory(
                        resolvedDirectory,
                        determinismPath,
                        new FbpPackagerOptions { ValidateMarketplaceLayout = true });
                    if (!string.Equals(second.Sha256, build.Sha256, StringComparison.Ordinal))
                    {
                        Console.Error.WriteLine($"error: packaging is not deterministic for '{manifest.Id}' ({build.Sha256} != {second.Sha256}).");
                        return 2;
                    }
                }
                finally
                {
                    if (File.Exists(determinismPath))
                    {
                        File.Delete(determinismPath);
                    }
                }

                var signaturePath = packagePath + ".sig.json";
                PackageSigner
                    .SignPackageFileAsync(packagePath, signaturePath, signingKey, keyId, SignatureRole.Marketplace)
                    .GetAwaiter()
                    .GetResult();

                artifactList.Add(new MarketplacePluginArtifact
                {
                    Manifest = manifest,
                    PackagePath = packagePath,
                    SignaturePath = signaturePath,
                    Sha256 = build.Sha256,
                    Size = build.Size,
                });
                Console.WriteLine($"prepared {manifest.Id} v{manifest.Version}: sha256={build.Sha256} size={build.Size}");
            }
        }

        var buildResult = MarketplaceIndexGenerator.Build(
            artifactList,
            new MarketplaceIndexGeneratorOptions
            {
                OutputDirectory = marketplaceDirectory,
                ArtifactBaseUrl = artifactBaseUrl,
                MarketplaceBaseUrl = marketplaceBaseUrl,
                AssetBaseUrl = assetBaseUrl,
                KeyId = keyId,
                SigningKey = signingKey,
                GeneratedAt = generatedAt,
                SourceCommit = sourceCommit,
                Channel = channel,
            });

        var keys = MarketplaceIndexGenerator.ReadKeysFile(buildResult.KeysPath);
        var verification = MarketplaceIndexGenerator.VerifySignedIndex(buildResult.IndexPath, keys);
        if (!verification.Verified)
        {
            Console.Error.WriteLine($"error: generated index failed verification: {string.Join("; ", verification.Errors)}");
            return 2;
        }

        Console.WriteLine($"index OK: {buildResult.IndexPath}");
        Console.WriteLine($"  keys:        {buildResult.KeysPath}");
        Console.WriteLine($"  shards:      {buildResult.ShardPaths.Count}");
        Console.WriteLine($"  plugins:     {buildResult.Index.Plugins!.Count}");
        Console.WriteLine($"  generatedAt: {buildResult.Index.GeneratedAt:O} (signed by {keyId})");
        return 0;
    }

    /// <summary>Verifies a generated/signed index against keys.json (post-publish CI verification).</summary>
    private static int VerifyIndex(string[] args)
    {
        var options = ParseOptions(args);
        var indexPath = Option(options, "index")
            ?? Path.Combine(RequireOption(options, "marketplace"), MarketplaceIndexGenerator.IndexFileName);
        var keysPath = Option(options, "keys")
            ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(indexPath))!, MarketplaceIndexGenerator.KeysFileName);
        var keys = MarketplaceIndexGenerator.ReadKeysFile(keysPath);
        var result = MarketplaceIndexGenerator.VerifySignedIndex(indexPath, keys);
        if (!result.Verified)
        {
            Console.Error.WriteLine($"verify-index FAILED: {string.Join("; ", result.Errors)}");
            return 2;
        }

        Console.WriteLine($"verify-index OK: generatedAt={result.GeneratedAt:O} keys=[{string.Join(", ", result.VerifiedKeyIds)}]");
        return 0;
    }

    private static ECDsa LoadKeyFromOptions(IReadOnlyDictionary<string, string> options)
    {
        var keyPath = Option(options, "key");
        if (!string.IsNullOrWhiteSpace(keyPath))
        {
            return LoadKey(keyPath);
        }

        var environmentVariable = Option(options, "key-env");
        if (!string.IsNullOrWhiteSpace(environmentVariable))
        {
            var pem = Environment.GetEnvironmentVariable(environmentVariable);
            if (string.IsNullOrWhiteSpace(pem))
            {
                throw new FormatException(
                    $"Environment variable '{environmentVariable}' is empty. The maintainer must configure the marketplace signing key secret (see docs/plugin/signing-and-keys.md).");
            }

            var key = ECDsa.Create();
            key.ImportFromPem(pem);
            EcdsaKeyMaterial.EnsureP256(key);
            return key;
        }

        throw new FormatException("Missing signing key: pass --key <private.pem> or --key-env <ENVIRONMENT_VARIABLE>.");
    }

    private static int SampleIndex(string[] args)
    {
        var options = ParseOptions(args);
        var payloadPath = RequireOption(options, "payload");
        var outDirectory = RequireOption(options, "out-dir");
        var keyId = Option(options, "key-id") ?? "fluidbar-marketplace-sample";
        var index = JsonSerializer.Deserialize<MarketplaceIndexDocument>(
            File.ReadAllText(payloadPath),
            PluginJson.CreateStrictOptions()) ?? throw new FormatException("Index payload is empty.");

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var now = DateTimeOffset.UtcNow;
        var keyInfo = new MarketplaceKeyInfo
        {
            KeyId = keyId,
            Role = "marketplace",
            Algorithm = SignatureAlgorithm.EcdsaP256Sha256,
            PublicKey = EcdsaKeyMaterial.ExportPublicKeySpkiBase64(key),
            NotBefore = now.AddYears(-1),
            NotAfter = now.AddYears(1),
        };
        var unsigned = index with { Keys = [keyInfo], Signatures = [] };
        var signature = IndexSigner.Sign(unsigned, key, keyId, now);
        var signedJson = IndexSigner.CreateSignedJson(unsigned, [signature]);
        Directory.CreateDirectory(outDirectory);
        var indexPath = Path.Combine(outDirectory, "index.json");
        File.WriteAllText(indexPath, signedJson);
        File.WriteAllText(
            Path.Combine(outDirectory, "keys.json"),
            JsonSerializer.Serialize(new[] { keyInfo }, PluginJson.CreateStrictOptions()));
        Console.WriteLine($"sample-index OK: {indexPath} (sample key '{keyId}', private key discarded)");
        return 0;
    }

    private static int Policy(string[] args)
    {
        var options = ParseOptions(args);
        var pluginPath = RequireOption(options, "plugin");
        var configuration = Option(options, "configuration") ?? "Release";
        _ = ResolvePackageLayout(pluginPath, configuration, out var staging, out var resolvedDirectory);
        using (staging)
        {
            var manifest = ReadManifest(resolvedDirectory);
            var errors = new List<string>();
            var warnings = new List<string>();
            var validation = PluginManifestValidator.Validate(
                manifest,
                new ManifestValidationOptions { RequirePublisherKeyId = true });
            errors.AddRange(validation.Errors.Select(static issue => issue.ToString()));
            warnings.AddRange(validation.Warnings.Select(static issue => issue.ToString()));

            var inspection = PluginPackageInspector.Inspect(resolvedDirectory, manifest, new HostAssemblyCatalog());
            errors.AddRange(inspection.Errors.Select(static issue => issue.ToString()));
            warnings.AddRange(inspection.Issues
                .Where(static issue => issue.Severity == PluginPackageIssueSeverity.Warning)
                .Select(static issue => issue.ToString()));

            ValidateLocales(resolvedDirectory, manifest, errors, warnings);
            if (manifest.Capabilities.HasWildcardNetwork())
            {
                warnings.Add("policy: wildcard network allowlist requires a security review entry in the PR.");
            }

            PrintIssues(errors, warnings);
            Console.WriteLine(errors.Count == 0
                ? $"policy OK: {manifest.Id} ({warnings.Count} warning(s))"
                : $"policy FAILED: {errors.Count} error(s)");
            return errors.Count == 0 ? 0 : 2;
        }
    }

    private static int Dev(string[] args)
    {
        var options = ParseOptions(args);
        var pluginPath = RequireOption(options, "plugin");
        var configuration = Option(options, "configuration") ?? "Debug";
        _ = ResolvePackageLayout(pluginPath, configuration, out var staging, out var resolvedDirectory);
        using (staging)
        {
            var manifest = ReadManifest(resolvedDirectory);
            var validation = PluginManifestValidator.Validate(manifest);
            if (!validation.IsValid)
            {
                PrintIssues(validation.Errors.Select(static issue => issue.ToString()).ToArray(), []);
                return 2;
            }

            var tempRoot = Path.Combine(Path.GetTempPath(), "fluidbar-plugin-dev", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                var logger = new ConsoleHostLogger();
                var hostOptions = new PluginHostOptions
                {
                    PluginRootDirectory = tempRoot,
                    AppVersion = new SemanticVersion(2, 0, 0),
                    Logger = logger,
                };
                using var host = new AsyncHostScope(new PluginHost(hostOptions));
                var load = host.Host.LoadFromDirectoryAsync(resolvedDirectory).GetAwaiter().GetResult();
                if (!load.Success)
                {
                    Console.Error.WriteLine($"dev load FAILED ({load.FailureKind}): {load.Message}");
                    return 2;
                }

                Console.WriteLine($"dev load OK: {manifest.Id} state={load.Handle?.State}");
                var unload = host.Host.UnloadAsync(manifest.Id).GetAwaiter().GetResult();
                Console.WriteLine($"dev unload: collected={unload.Collected} attempts={unload.CollectionAttempts} restartRequired={unload.RestartRequired}");
                return unload.Collected ? 0 : 2;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempRoot))
                    {
                        Directory.Delete(tempRoot, recursive: true);
                    }
                }
                catch (IOException)
                {
                    // Temp dev directory cleanup is best effort.
                }
            }
        }
    }

    private static bool ResolvePackageLayout(string pluginPath, string configuration, out IDisposable? stagingDisposer, out string resolvedDirectory)
    {
        stagingDisposer = null;
        var full = Path.GetFullPath(pluginPath);
        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException($"Plugin path '{full}' does not exist.");
        }

        // 1. Already a package layout (.fbp content): manifest.json + plugin/.
        if (File.Exists(Path.Combine(full, "manifest.json")) && Directory.Exists(Path.Combine(full, "plugin")))
        {
            resolvedDirectory = full;
            return true;
        }

        // 2. Build output directory: manifest.json + assemblies next to it. The target framework directory is
        //    discovered (not hardcoded) so a TFM change does not break CI.
        var configurationDirectory = Path.Combine(full, "bin", configuration);
        if (Directory.Exists(configurationDirectory))
        {
            var buildOutput = Directory.EnumerateDirectories(configurationDirectory)
                .OrderBy(static directory => directory, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(static directory => File.Exists(Path.Combine(directory, "manifest.json")));
            if (buildOutput is not null)
            {
                var staging = FbpStagingBuilder.FromPluginOutput(buildOutput);
                stagingDisposer = staging;
                resolvedDirectory = staging.StagingDirectory;
                return false;
            }
        }

        // 3. Flat directory containing manifest + assemblies (e.g. a build output path passed directly).
        if (File.Exists(Path.Combine(full, "manifest.json")) &&
            Directory.EnumerateFiles(full, "*.dll", SearchOption.TopDirectoryOnly).Any())
        {
            var staging = FbpStagingBuilder.FromPluginOutput(full);
            stagingDisposer = staging;
            resolvedDirectory = staging.StagingDirectory;
            return false;
        }

        throw new DirectoryNotFoundException(
            $"No built plugin found for '{full}'. Expected a package layout or a build output with manifest.json under '{configurationDirectory}'. " +
            $"Build it first (dotnet build \"{full}\" -c {configuration}).");
    }

    private static PluginManifest ReadManifest(string packageDirectory) =>
        PluginManifestParser.ParseFileAsync(Path.Combine(packageDirectory, "manifest.json")).GetAwaiter().GetResult();

    private static void ValidateLocales(string packageDirectory, PluginManifest manifest, List<string> errors, List<string> warnings)
    {
        if (manifest.Locales is not { } locales)
        {
            return;
        }

        var localeDirectory = Path.Combine(packageDirectory, "locales");
        if (!System.IO.Directory.Exists(localeDirectory))
        {
            errors.Add("locales: directory is missing from the package.");
            return;
        }

        var bundles = new Dictionary<string, LocaleBundle>(StringComparer.OrdinalIgnoreCase);
        foreach (var locale in locales.Available ?? [])
        {
            var path = Path.Combine(localeDirectory, locale + ".json");
            if (!File.Exists(path))
            {
                if (string.Equals(locale, locales.Default, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"locales: required default bundle '{locale}.json' is missing.");
                }
                else
                {
                    warnings.Add($"locales: declared bundle '{locale}.json' is missing.");
                }

                continue;
            }

            if (!LocaleBundleLoader.TryParse(File.ReadAllText(path), out var bundle, out var bundleErrors) || bundle is null)
            {
                errors.Add($"locales: '{locale}.json' is invalid: {string.Join("; ", bundleErrors)}");
                continue;
            }

            bundles[locale] = bundle;
        }

        if (bundles.TryGetValue(locales.Default, out var defaultBundle))
        {
            foreach (var pair in bundles)
            {
                if (!string.Equals(pair.Key, locales.Default, StringComparison.OrdinalIgnoreCase))
                {
                    errors.AddRange(LocaleBundleConsistency.FindPlaceholderMismatches(defaultBundle, pair.Value));
                }
            }
        }
    }

    private static void PrintIssues(IReadOnlyList<string> errors, IReadOnlyList<string> warnings)
    {
        foreach (var warning in warnings)
        {
            Console.WriteLine($"warning: {warning}");
        }

        foreach (var error in errors)
        {
            Console.Error.WriteLine($"error: {error}");
        }
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index < args.Length; index++)
        {
            var argument = args[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var name = argument[2..];
            if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options[name] = args[++index];
            }
            else
            {
                options[name] = "true";
            }
        }

        return options;
    }

    private static string RequireOption(IReadOnlyDictionary<string, string> options, string name) =>
        Option(options, name) ?? throw new FormatException($"Missing required option --{name}.");

    private static string? Option(IReadOnlyDictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) ? value : null;

    private static ECDsa LoadKey(string path)
    {
        var pem = File.ReadAllText(path);
        var key = ECDsa.Create();
        key.ImportFromPem(pem);
        EcdsaKeyMaterial.EnsureP256(key);
        return key;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            fluidbar-plugin — FluidBar v2 plugin tooling

            usage:
              fluidbar-plugin validate --plugin <dir|build-output> [--configuration Release]
              fluidbar-plugin pack     --plugin <dir|build-output> --out <file.fbp> [--configuration Release]
              fluidbar-plugin sign     --package <file.fbp> --key <private.pem> --key-id <id> [--role marketplace|author] [--out <signature.json>]
              fluidbar-plugin sign-index --index <index.json> --key <private.pem> --key-id <id> [--out <index.json>]
              fluidbar-plugin sample-index --payload <index.payload.json> --out-dir <dir> [--key-id <id>]
              fluidbar-plugin keygen   --out <private.pem> [--public-out <public.spki.txt>]
              fluidbar-plugin publish-prepare --plugins-root <dir> --artifacts <dir> --marketplace <dir> --key-id <id>
                                              (--key <private.pem> | --key-env <ENV_VAR>)
                                              [--artifact-base-url <https://...>] [--marketplace-base-url <https://...>]
                                              [--configuration Release] [--channel stable] [--generated-at <ISO8601>]
                                              [--source-commit <40-hex>] [--asset-base-url <https://...>]
              fluidbar-plugin verify-index [--index <index.json> | --marketplace <dir>] [--keys <keys.json>]
              fluidbar-plugin policy   --plugin <dir|build-output> [--configuration Release]
              fluidbar-plugin dev      --plugin <dir|build-output> [--configuration Debug]

            The CLI uses the same Sdk/Marketplace implementation as the App and CI: validate and pack are
            network-free, sign requires an ECDSA P-256 private key (CI uses an HSM/KMS instead).
            """);
    }

    private sealed class AsyncHostScope : IDisposable
    {
        internal AsyncHostScope(PluginHost host) => Host = host;

        internal PluginHost Host { get; }

        public void Dispose() => Host.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private sealed class ConsoleHostLogger : IPluginHostLogger
    {
        public bool IsEnabled(PluginHostLogLevel level) => level >= PluginHostLogLevel.Information;

        public void Log(PluginHostLogLevel level, string pluginId, string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? properties = null) =>
            Console.WriteLine($"[{level}] {pluginId}: {message}{(exception is null ? string.Empty : $" ({exception.GetType().Name})")}");
    }
}
