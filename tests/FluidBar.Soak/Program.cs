using System.Runtime.InteropServices;
using FluidBar.Soak.Scenarios;

namespace FluidBar.Soak;

/// <summary>
/// M6 Core soak/performance harness.
/// <para>Runs deterministic scenarios A-F against the real Core pipeline, emits a JSON report to
/// stdout, writes it to <c>--report</c> when requested, and exits non-zero on any budget breach.</para>
/// <para>Usage: <c>dotnet run --project tests/FluidBar.Soak -c Release [-- --scenarios A,B,D] [--report artifacts/soak.json] [--seed 20261018]</c></para>
/// </summary>
public static class Program
{
    private static readonly Dictionary<string, (string Name, Func<int, SoakScenarioResult> Run)> Scenarios = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = ("event storm (100k mixed events)", EventStormScenario.Run),
        ["B"] = ("incremental media (10k updates)", IncrementalMediaScenario.Run),
        ["C"] = ("activity churn/expiry (10k events)", ActivityChurnScenario.Run),
        ["D"] = ("motion settle/allocation (9 tokens)", MotionScenario.Run),
        ["E"] = ("settings atomic saves (10k)", SettingsScenario.Run),
        ["F"] = ("recording/replay fixtures", FixtureReplayScenario.Run),
    };

    /// <summary>Harness entry point.</summary>
    public static int Main(string[] args)
    {
        SoakArgs parsed;
        try
        {
            parsed = SoakArgs.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"soak: usage error: {ex.Message}");
            PrintUsage();
            return 2;
        }

        if (parsed.ShowHelp)
        {
            PrintUsage();
            return 0;
        }

        if (parsed.List)
        {
            foreach (var (id, scenario) in Scenarios)
            {
                Console.WriteLine($"{id}\t{scenario.Name}");
            }

            return 0;
        }

        var selected = parsed.Scenarios.Count == 0
            ? Scenarios.Keys.ToArray()
            : parsed.Scenarios.ToArray();

        var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var results = new List<SoakScenarioResult>(selected.Length + 1);
        foreach (var id in selected)
        {
            if (!Scenarios.TryGetValue(id, out var scenario))
            {
                Console.Error.WriteLine($"soak: unknown scenario '{id}'.");
                return 2;
            }

            Console.Error.WriteLine($"soak: running {id} — {scenario.Name} ...");
            var result = RunSafely(id, scenario.Name, scenario.Run, parsed.Seed);
            results.Add(result);
            Console.Error.WriteLine(
                $"soak: {id} {(result.Passed ? "PASS" : "FAIL")} in {result.DurationMs:0} ms");
            if (!result.Passed)
            {
                foreach (var failure in result.Failures)
                {
                    Console.Error.WriteLine($"       ! {failure}");
                }

                if (parsed.FailFast)
                {
                    break;
                }
            }
        }

        totalStopwatch.Stop();
        var totalMs = totalStopwatch.Elapsed.TotalMilliseconds;
        var runtimeBuilder = new ScenarioResultBuilder("T", "total runtime");
        runtimeBuilder.Check("totalRuntimeMs", totalMs, SoakBudgets.TotalRuntimeBudgetMs, "<=");
        runtimeBuilder.Metric("totalRuntimeMs", totalMs);
        results.Add(runtimeBuilder.Build(totalMs));

        var report = new SoakReport
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Seed = parsed.Seed,
            Environment = new SoakEnvironment(
                RuntimeInformation.FrameworkDescription,
                RuntimeInformation.OSDescription,
                Environment.ProcessorCount,
                Environment.MachineName,
#if DEBUG
                "Debug"),
#else
                "Release"),
#endif
            TotalDurationMs = totalMs,
            Passed = results.All(result => result.Passed),
            Scenarios = [.. results],
        };

        if (parsed.ReportPath is not null)
        {
            try
            {
                var directory = Path.GetDirectoryName(parsed.ReportPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(parsed.ReportPath, report.ToJson());
                Console.Error.WriteLine($"soak: report written to {parsed.ReportPath}");
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"soak: could not write report '{parsed.ReportPath}': {ex.Message}");
                return 1;
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine($"soak: could not write report '{parsed.ReportPath}': {ex.Message}");
                return 1;
            }
        }

        Console.Out.WriteLine(report.ToJson());
        Console.Error.WriteLine(report.ToHumanSummary());
        return report.Passed ? 0 : 1;
    }

    private static SoakScenarioResult RunSafely(
        string id,
        string name,
        Func<int, SoakScenarioResult> run,
        int seed)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = run(seed);
            return result with { Id = id, Name = name };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var builder = new ScenarioResultBuilder(id, name);
            builder.Fail($"{ex.GetType().Name}: {ex.Message}");
            return builder.Build(stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine(
            """
            FluidBar Core soak harness (M6)
              dotnet run --project tests/FluidBar.Soak -c Release [options]
            Options:
              --scenarios A,B,C   Scenarios to run (default: all)
              --scenario A        Single scenario (repeatable)
              --report <path>     Also write the JSON report to this path
              --seed <int>        Deterministic seed (default: 20261018)
              --fail-fast         Stop after the first failing scenario
              --list              List scenario ids
              --help              Show this help
            Exit codes: 0 = pass, 1 = budget violation, 2 = usage error.
            """);
    }
}

/// <summary>Parsed command-line options for the soak harness.</summary>
public sealed record SoakArgs
{
    /// <summary>Default deterministic seed.</summary>
    public const int DefaultSeed = 20261018;

    /// <summary>Selected scenario ids (empty = all).</summary>
    public IReadOnlyList<string> Scenarios { get; init; } = [];

    /// <summary>Optional report output path.</summary>
    public string? ReportPath { get; init; }

    /// <summary>Deterministic seed.</summary>
    public int Seed { get; init; } = DefaultSeed;

    /// <summary>Stop after first failure.</summary>
    public bool FailFast { get; init; }

    /// <summary>List scenarios and exit.</summary>
    public bool List { get; init; }

    /// <summary>Show usage and exit.</summary>
    public bool ShowHelp { get; init; }

    /// <summary>Parses harness arguments.</summary>
    public static SoakArgs Parse(string[] args)
    {
        var scenarios = new List<string>();
        string? reportPath = null;
        var seed = DefaultSeed;
        var failFast = false;
        var list = false;
        var help = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--scenarios":
                case "--scenario":
                    if (++i >= args.Length)
                    {
                        throw new ArgumentException($"{arg} requires a value.");
                    }

                    scenarios.AddRange(args[i]
                        .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                        .Select(value => value.ToUpperInvariant()));
                    break;
                case "--report":
                    if (++i >= args.Length)
                    {
                        throw new ArgumentException("--report requires a path.");
                    }

                    reportPath = args[i];
                    break;
                case "--seed":
                    if (++i >= args.Length || !int.TryParse(args[i], out seed))
                    {
                        throw new ArgumentException("--seed requires an integer.");
                    }

                    break;
                case "--fail-fast":
                    failFast = true;
                    break;
                case "--list":
                    list = true;
                    break;
                case "--help":
                case "-h":
                    help = true;
                    break;
                default:
                    throw new ArgumentException($"unknown option '{arg}'.");
            }
        }

        return new SoakArgs
        {
            Scenarios = scenarios,
            ReportPath = reportPath,
            Seed = seed,
            FailFast = failFast,
            List = list,
            ShowHelp = help,
        };
    }
}
