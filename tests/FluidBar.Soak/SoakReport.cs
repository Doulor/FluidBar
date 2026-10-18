using System.Collections.Immutable;
using System.Text.Json;

namespace FluidBar.Soak;

/// <summary>One enforced budget check inside a scenario.</summary>
/// <param name="Name">Budget name.</param>
/// <param name="Comparison">One of <c>&lt;=</c>, <c>&gt;=</c>, <c>==</c>.</param>
/// <param name="Budget">Threshold.</param>
/// <param name="Actual">Measured value.</param>
/// <param name="Passed">Whether the measured value satisfies the budget.</param>
public sealed record SoakBudgetCheck(string Name, string Comparison, double Budget, double Actual, bool Passed);

/// <summary>Result of one soak scenario.</summary>
public sealed record SoakScenarioResult
{
    /// <summary>Scenario letter (A-F).</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable scenario name.</summary>
    public required string Name { get; init; }

    /// <summary>Whether all budgets passed.</summary>
    public required bool Passed { get; init; }

    /// <summary>Scenario wall time in milliseconds.</summary>
    public required double DurationMs { get; init; }

    /// <summary>Free-form measured metrics (also reported on failure).</summary>
    public ImmutableDictionary<string, double> Metrics { get; init; } = ImmutableDictionary<string, double>.Empty;

    /// <summary>Budget checks.</summary>
    public ImmutableArray<SoakBudgetCheck> Budgets { get; init; } = [];

    /// <summary>Failure descriptions (empty when passed).</summary>
    public ImmutableArray<string> Failures { get; init; } = [];
}

/// <summary>Environment fingerprint in the report.</summary>
/// <param name="Framework">Runtime version.</param>
/// <param name="Os">OS description.</param>
/// <param name="ProcessorCount">Logical CPU count.</param>
/// <param name="MachineName">Machine name (diagnostics; CI runners vary).</param>
/// <param name="BuildConfiguration">Debug/Release.</param>
public sealed record SoakEnvironment(
    string Framework,
    string Os,
    int ProcessorCount,
    string MachineName,
    string BuildConfiguration);

/// <summary>Top-level machine-readable soak report.</summary>
public sealed record SoakReport
{
    /// <summary>Report schema version.</summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>UTC generation time.</summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>Seed used by every deterministic scenario.</summary>
    public required int Seed { get; init; }

    /// <summary>Environment fingerprint.</summary>
    public required SoakEnvironment Environment { get; init; }

    /// <summary>Total harness runtime in milliseconds.</summary>
    public required double TotalDurationMs { get; init; }

    /// <summary>Whether every scenario passed and the total runtime budget held.</summary>
    public required bool Passed { get; init; }

    /// <summary>Scenario results, in execution order.</summary>
    public required ImmutableArray<SoakScenarioResult> Scenarios { get; init; }

    /// <summary>Serializes the report as indented camelCase JSON.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, ReportJsonOptions);

    private static readonly JsonSerializerOptions ReportJsonOptions = CreateReportOptions();

    private static JsonSerializerOptions CreateReportOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        return options;
    }

    /// <summary>One-line-per-scenario human summary (printed to stderr in CI logs).</summary>
    public string ToHumanSummary()
    {
        var lines = new List<string>
        {
            $"soak: {(Passed ? "PASS" : "FAIL")} ({TotalDurationMs:0} ms, seed {Seed}, {Environment.Framework})",
        };
        foreach (var scenario in Scenarios)
        {
            lines.Add(
                $"  [{(scenario.Passed ? "PASS" : "FAIL")}] {scenario.Id} {scenario.Name} — {scenario.DurationMs:0} ms");
            foreach (var failure in scenario.Failures)
            {
                lines.Add($"         ! {failure}");
            }
        }

        return string.Join(System.Environment.NewLine, lines);
    }
}

/// <summary>Mutable accumulator for one scenario; converted to <see cref="SoakScenarioResult"/>.</summary>
public sealed class ScenarioResultBuilder
{
    private readonly List<SoakBudgetCheck> _budgets = [];
    private readonly List<string> _failures = [];
    private readonly Dictionary<string, double> _metrics = new(StringComparer.Ordinal);

    /// <summary>Creates a builder for a scenario.</summary>
    public ScenarioResultBuilder(string id, string name)
    {
        Id = id;
        Name = name;
    }

    /// <summary>Scenario letter.</summary>
    public string Id { get; }

    /// <summary>Scenario name.</summary>
    public string Name { get; }

    /// <summary>Adds an informational metric.</summary>
    public void Metric(string name, double value) => _metrics[name] = value;

    /// <summary>Adds an informational metric as long.</summary>
    public void Metric(string name, long value) => Metric(name, (double)value);

    /// <summary>Adds a metric plus a budget check.</summary>
    public bool Check(string name, double actual, double budget, string comparison = "<=")
    {
        var passed = comparison switch
        {
            "<=" => actual <= budget,
            ">=" => actual >= budget,
            "==" => Math.Abs(actual - budget) < double.Epsilon,
            _ => throw new ArgumentOutOfRangeException(nameof(comparison)),
        };
        Metric(name, actual);
        _budgets.Add(new SoakBudgetCheck(name, comparison, budget, actual, passed));
        if (!passed)
        {
            _failures.Add($"{name}: measured {actual:0.###}, budget {comparison} {budget:0.###}");
        }

        return passed;
    }

    /// <summary>Records an informative-only assertion failure (no budget metric).</summary>
    public void Fail(string message) => _failures.Add(message);

    /// <summary>Whether all budgets passed so far.</summary>
    public bool Passed => _failures.Count == 0;

    /// <summary>All recorded failures.</summary>
    public IReadOnlyList<string> Failures => _failures;

    /// <summary>Finishes the scenario.</summary>
    public SoakScenarioResult Build(double durationMs) => new()
    {
        Id = Id,
        Name = Name,
        Passed = Passed,
        DurationMs = durationMs,
        Metrics = _metrics.ToImmutableDictionary(StringComparer.Ordinal),
        Budgets = [.. _budgets],
        Failures = [.. _failures],
    };
}
