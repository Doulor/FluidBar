namespace FluidBar.Sdk.Versioning;

/// <summary>
/// A bounded, well-documented subset of semver ranges used by <c>dependencies.sdk</c> and
/// <c>dependencies.plugins[].range</c>. Supported syntax:
/// <list type="bullet">
///   <item><description><c>*</c> or empty (any version)</description></item>
///   <item><description>exact <c>1.2.3</c></description></item>
///   <item><description>comparators <c>&gt;=1.2.3</c>, <c>&gt;</c>, <c>&lt;=</c>, <c>&lt;</c>, <c>=1.2.3</c></description></item>
///   <item><description>caret <c>^1.2.3</c> (compatible-with), tilde <c>~1.2.3</c></description></item>
///   <item><description>space separated conjunctions <c>&gt;=1.2.0 &lt;2.0.0</c></description></item>
///   <item><description><c>||</c> disjunctions</description></item>
/// </list>
/// This is deliberately not a full npm-style range parser: plugin dependencies are simple and a strict
/// parser keeps the validator, CI and client installer in agreement.
/// </summary>
public sealed class SemanticVersionRange
{
    private readonly IReadOnlyList<IReadOnlyList<VersionComparator>> _alternatives;

    private SemanticVersionRange(string original, IReadOnlyList<IReadOnlyList<VersionComparator>> alternatives)
    {
        Original = original;
        _alternatives = alternatives;
    }

    /// <summary>The original, trimmed range text.</summary>
    public string Original { get; }

    /// <summary>Parses range text. Returns <see langword="false"/> for unsupported syntax.</summary>
    public static bool TryParse(string? value, out SemanticVersionRange? range)
    {
        range = null;
        if (value is null)
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 64)
        {
            return false;
        }

        var alternatives = new List<IReadOnlyList<VersionComparator>>();
        foreach (var alternativeText in trimmed.Split("||", StringSplitOptions.TrimEntries))
        {
            if (alternativeText.Length == 0)
            {
                return false;
            }

            if (alternativeText == "*")
            {
                alternatives.Add(Array.Empty<VersionComparator>());
                continue;
            }

            var comparators = new List<VersionComparator>();
            foreach (var token in alternativeText.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!TryParseToken(token, out var comparator))
                {
                    return false;
                }

                comparators.Add(comparator);
            }

            if (comparators.Count == 0)
            {
                return false;
            }

            alternatives.Add(comparators);
        }

        range = new SemanticVersionRange(trimmed, alternatives);
        return true;
    }

    /// <summary>Parses range text or throws <see cref="FormatException"/>.</summary>
    public static SemanticVersionRange Parse(string value) =>
        TryParse(value, out var range) ? range! : throw new FormatException($"'{value}' is not a supported version range.");

    private static bool TryParseToken(string token, out VersionComparator comparator)
    {
        comparator = default;
        if (token == "*")
        {
            comparator = new VersionComparator(ComparisonOperator.Any, default);
            return true;
        }

        var op = ComparisonOperator.Equal;
        var rest = token;
        if (token.StartsWith(">=", StringComparison.Ordinal))
        {
            op = ComparisonOperator.GreaterThanOrEqual;
            rest = token[2..];
        }
        else if (token.StartsWith("<=", StringComparison.Ordinal))
        {
            op = ComparisonOperator.LessThanOrEqual;
            rest = token[2..];
        }
        else if (token.StartsWith('>'))
        {
            op = ComparisonOperator.GreaterThan;
            rest = token[1..];
        }
        else if (token.StartsWith('<'))
        {
            op = ComparisonOperator.LessThan;
            rest = token[1..];
        }
        else if (token.StartsWith('='))
        {
            op = ComparisonOperator.Equal;
            rest = token[1..];
        }
        else if (token.StartsWith('^'))
        {
            op = ComparisonOperator.Caret;
            rest = token[1..];
        }
        else if (token.StartsWith('~'))
        {
            op = ComparisonOperator.Tilde;
            rest = token[1..];
        }

        if (!SemanticVersion.TryParse(rest, out var version))
        {
            return false;
        }

        comparator = new VersionComparator(op, version);
        return true;
    }

    /// <summary>Returns <see langword="true"/> when <paramref name="version"/> lies inside the range.</summary>
    public bool IsSatisfiedBy(SemanticVersion version)
    {
        foreach (var alternative in _alternatives)
        {
            var satisfied = true;
            foreach (var comparator in alternative)
            {
                if (!IsSatisfied(comparator, version))
                {
                    satisfied = false;
                    break;
                }
            }

            if (satisfied)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSatisfied(VersionComparator comparator, SemanticVersion version) =>
        comparator.Operator switch
        {
            ComparisonOperator.Any => true,
            ComparisonOperator.Equal => version == comparator.Version,
            ComparisonOperator.GreaterThan => version > comparator.Version,
            ComparisonOperator.GreaterThanOrEqual => version >= comparator.Version,
            ComparisonOperator.LessThan => version < comparator.Version,
            ComparisonOperator.LessThanOrEqual => version <= comparator.Version,
            ComparisonOperator.Caret => IsCaretSatisfied(comparator.Version, version),
            ComparisonOperator.Tilde => IsTildeSatisfied(comparator.Version, version),
            _ => false,
        };

    private static bool IsCaretSatisfied(SemanticVersion lowerBound, SemanticVersion version)
    {
        if (version < lowerBound)
        {
            return false;
        }

        var upperBound = lowerBound.Major > 0
            ? new SemanticVersion(lowerBound.Major + 1, 0, 0)
            : lowerBound.Minor > 0
                ? new SemanticVersion(0, lowerBound.Minor + 1, 0)
                : new SemanticVersion(0, 0, lowerBound.Patch + 1);
        return version < upperBound;
    }

    private static bool IsTildeSatisfied(SemanticVersion lowerBound, SemanticVersion version)
    {
        if (version < lowerBound)
        {
            return false;
        }

        // ~1.2.3 => >=1.2.3 <1.3.0 ; ~1.2 => handled the same because the parser requires full semver.
        var upperBound = new SemanticVersion(lowerBound.Major, lowerBound.Minor + 1, 0);
        return version < upperBound;
    }

    public override string ToString() => Original;

    private enum ComparisonOperator
    {
        Any,
        Equal,
        GreaterThan,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual,
        Caret,
        Tilde,
    }

    private readonly record struct VersionComparator(ComparisonOperator Operator, SemanticVersion Version);
}
