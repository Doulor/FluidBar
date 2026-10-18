using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FluidBar.Sdk.Localization;

/// <summary>BCP-47 tag helpers shared by the plugin localizer, the manifest validator and the marketplace.</summary>
public static class LocaleTags
{
    private static readonly Regex ValidTagPattern = new(
        @"^[a-z]{2,3}(?:-[A-Z][a-z]{3})?(?:-[A-Z]{2}|-[0-9]{3})?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>The canonical form validated by the manifest / locale bundle JSON Schemas.</summary>
    public static bool IsValid(string? tag) => !string.IsNullOrEmpty(tag) && ValidTagPattern.IsMatch(tag);

    /// <summary>Returns <c>zh-Hans-CN → zh-Hans → zh</c> style parents, including the tag itself.</summary>
    public static IReadOnlyList<string> EnumerateParents(string locale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        var parts = locale.Split('-', StringSplitOptions.RemoveEmptyEntries);
        var results = new List<string>(parts.Length);
        for (var length = parts.Length; length >= 1; length--)
        {
            results.Add(string.Join('-', parts.Take(length)));
        }

        return results;
    }

    /// <summary>
    /// Expands a requested locale plus fallback locales into a distinct candidate chain:
    /// for each tag, itself first, then its parents. Case-insensitive de-duplication preserving order.
    /// </summary>
    public static IReadOnlyList<string> EnumerateCandidates(string? locale, params string?[] fallbackLocales)
    {
        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Add(locale);
        foreach (var fallback in fallbackLocales)
        {
            Add(fallback);
        }

        return results;

        void Add(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            foreach (var parent in EnumerateParents(candidate.Trim()))
            {
                if (seen.Add(parent))
                {
                    results.Add(parent);
                }
            }
        }
    }

    /// <summary>Returns the primary language subtag of a locale tag, lower-cased.</summary>
    public static string GetLanguage(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return string.Empty;
        }

        var separator = locale.IndexOf('-');
        return (separator < 0 ? locale : locale[..separator]).ToLowerInvariant();
    }

    /// <summary>Resolves a culture for number/date formatting; falls back to the invariant culture.</summary>
    public static CultureInfo ResolveCulture(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return CultureInfo.InvariantCulture;
        }

        try
        {
            return CultureInfo.GetCultureInfo(locale);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}

/// <summary>Kind of a placeholder found in an ICU-subset message.</summary>
public enum MessagePlaceholderKind
{
    Simple = 0,
    Plural = 1,
    Number = 2,
}

/// <summary>A placeholder extracted from a message, used for locale parity validation.</summary>
public readonly record struct MessagePlaceholder(string Name, MessagePlaceholderKind Kind);

/// <summary>
/// The FluidBar ICU-MessageFormat subset: <c>{name}</c>, <c>{count, plural, =0 {...} one {...} other {...}}</c>,
/// <c>{count, number}</c>, <c>#</c> inside plural branches and <c>''</c> / <c>'{'</c> escaping.
/// This is intentionally small and dependency-free: the same formatter is used by the app, the plugin host
/// and the contributor validator so plugin locale bundles behave identically everywhere.
/// </summary>
public static class MessageFormat
{
    private static readonly IReadOnlyDictionary<string, object?> NoArguments =
        ImmutableDictionary<string, object?>.Empty;

    /// <summary>Formats a message; throws <see cref="FormatException"/> for malformed templates.</summary>
    public static string Format(string message, IReadOnlyDictionary<string, object?>? arguments = null, string? locale = null)
    {
        if (!TryFormat(message, arguments, locale, out var result, out var errors))
        {
            throw new FormatException($"Message template is invalid: {string.Join("; ", errors)}");
        }

        return result;
    }

    /// <summary>
    /// Best-effort formatting. Missing arguments leave the placeholder text in place and are reported in
    /// <paramref name="errors"/>; the localizer logs them and never throws in release builds.
    /// </summary>
    public static bool TryFormat(
        string message,
        IReadOnlyDictionary<string, object?>? arguments,
        string? locale,
        out string result,
        out IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(message);
        var errorList = new List<string>();
        var output = new StringBuilder(message.Length + 16);
        var parser = new MessageParser(message, errorList, placeholders: null);
        parser.Render(output, arguments ?? NoArguments, locale);
        result = output.ToString();
        errors = errorList;
        return errorList.Count == 0;
    }

    /// <summary>Validates a message template without formatting it.</summary>
    public static bool TryValidate(string message, out IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(message);
        var errorList = new List<string>();
        var parser = new MessageParser(message, errorList, placeholders: null);
        parser.Render(new StringBuilder(message.Length), NoArguments, locale: null, collectOnly: true);
        errors = errorList;
        return errorList.Count == 0;
    }

    /// <summary>Extracts placeholders (including those inside plural branches) for locale parity checks.</summary>
    public static IReadOnlyList<MessagePlaceholder> ExtractPlaceholders(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var errors = new List<string>();
        var placeholders = new List<MessagePlaceholder>();
        var parser = new MessageParser(message, errors, placeholders);
        parser.Render(new StringBuilder(message.Length), NoArguments, locale: null, collectOnly: true);
        return placeholders;
    }

    private sealed class MessageParser
    {
        private readonly string _message;
        private readonly List<string> _errors;
        private readonly List<MessagePlaceholder>? _placeholders;
        private int _index;

        internal MessageParser(string message, List<string> errors, List<MessagePlaceholder>? placeholders)
        {
            _message = message;
            _errors = errors;
            _placeholders = placeholders;
        }

        internal void Render(
            StringBuilder output,
            IReadOnlyDictionary<string, object?> arguments,
            string? locale,
            bool collectOnly = false)
        {
            var culture = LocaleTags.ResolveCulture(locale);
            RenderMessage(output, arguments, culture, locale ?? string.Empty, terminator: '\0', collectOnly);
        }

        private void RenderMessage(
            StringBuilder output,
            IReadOnlyDictionary<string, object?> arguments,
            CultureInfo culture,
            string locale,
            char terminator,
            bool collectOnly)
        {
            while (_index < _message.Length)
            {
                var current = _message[_index];
                if (terminator != '\0' && current == terminator)
                {
                    return;
                }

                if (current == '\'')
                {
                    RenderQuote(output, collectOnly);
                    continue;
                }

                if (current == '{')
                {
                    ParseArgument(output, arguments, culture, locale, collectOnly);
                    continue;
                }

                if (current == '}')
                {
                    AddError(_index, "Unmatched '}'.");
                    _index++;
                    continue;
                }

                output.Append(current);
                _index++;
            }
        }

        private void RenderQuote(StringBuilder output, bool collectOnly)
        {
            if (_index + 1 >= _message.Length)
            {
                if (!collectOnly)
                {
                    output.Append('\'');
                }

                _index++;
                return;
            }

            var next = _message[_index + 1];
            if (next == '\'')
            {
                if (!collectOnly)
                {
                    output.Append('\'');
                }

                _index += 2;
                return;
            }

            if (next is not ('{' or '}'))
            {
                // A lone apostrophe in prose is a literal character, not a quote opener.
                if (!collectOnly)
                {
                    output.Append('\'');
                }

                _index++;
                return;
            }

            // ICU quoted literal: '...' makes braces inside literal (e.g. '{literal}' => {literal}).
            _index++; // opening quote
            while (_index < _message.Length)
            {
                var current = _message[_index];
                if (current == '\'')
                {
                    if (_index + 1 < _message.Length && _message[_index + 1] == '\'')
                    {
                        if (!collectOnly)
                        {
                            output.Append('\'');
                        }

                        _index += 2;
                        continue;
                    }

                    _index++; // closing quote
                    return;
                }

                if (!collectOnly)
                {
                    output.Append(current);
                }

                _index++;
            }
        }

        private void ParseArgument(
            StringBuilder output,
            IReadOnlyDictionary<string, object?> arguments,
            CultureInfo culture,
            string locale,
            bool collectOnly)
        {
            var start = _index;
            _index++; // consume '{'
            SkipWhitespace();
            var name = ReadIdentifier();
            if (name.Length == 0)
            {
                AddError(start, "Expected an argument name after '{'.");
                SkipToArgumentEnd();
                return;
            }

            SkipWhitespace();
            if (_index < _message.Length && _message[_index] == '}')
            {
                _index++;
                RecordPlaceholder(name, MessagePlaceholderKind.Simple, collectOnly);
                if (collectOnly)
                {
                    return;
                }

                if (arguments.TryGetValue(name, out var value))
                {
                    output.Append(FormatSimpleValue(value, culture));
                }
                else
                {
                    AddError(start, $"Missing argument '{name}'.");
                    output.Append('{').Append(name).Append('}');
                }

                return;
            }

            if (_index >= _message.Length || _message[_index] != ',')
            {
                AddError(start, $"Expected ',' or '}}' in argument '{name}'.");
                SkipToArgumentEnd();
                return;
            }

            _index++; // consume ','
            SkipWhitespace();
            var formatType = ReadIdentifier();
            SkipWhitespace();
            switch (formatType)
            {
                case "plural":
                    ParsePlural(output, arguments, culture, locale, name, start, collectOnly);
                    return;
                case "number":
                    ExpectClosingBrace(name, start);
                    RecordPlaceholder(name, MessagePlaceholderKind.Number, collectOnly);
                    if (collectOnly)
                    {
                        return;
                    }

                    if (arguments.TryGetValue(name, out var numberValue))
                    {
                        output.Append(FormatNumberLike(numberValue, culture, name, start));
                    }
                    else
                    {
                        AddError(start, $"Missing argument '{name}'.");
                        output.Append('{').Append(name).Append('}');
                    }

                    return;
                default:
                    AddError(start, $"Unsupported message format type '{formatType}' in argument '{name}'.");
                    SkipToArgumentEnd();
                    return;
            }
        }

        private void ParsePlural(
            StringBuilder output,
            IReadOnlyDictionary<string, object?> arguments,
            CultureInfo culture,
            string locale,
            string name,
            int argumentStart,
            bool collectOnly)
        {
            SkipWhitespace();
            if (_index >= _message.Length || _message[_index] != ',')
            {
                AddError(argumentStart, $"Expected ',' after plural in argument '{name}'.");
                SkipToArgumentEnd();
                return;
            }

            _index++; // consume ','
            RecordPlaceholder(name, MessagePlaceholderKind.Plural, collectOnly);

            var branches = new Dictionary<string, string>(StringComparer.Ordinal);
            var hasOther = false;
            while (true)
            {
                SkipWhitespace();
                if (_index >= _message.Length)
                {
                    AddError(argumentStart, $"Unterminated plural in argument '{name}'.");
                    return;
                }

                if (_message[_index] == '}')
                {
                    _index++;
                    break;
                }

                var selector = ReadPluralSelector();
                if (selector.Length == 0)
                {
                    AddError(_index, "Expected a plural selector.");
                    SkipToArgumentEnd();
                    return;
                }

                SkipWhitespace();
                if (_index >= _message.Length || _message[_index] != '{')
                {
                    AddError(_index, $"Expected '{{' after plural selector '{selector}'.");
                    SkipToArgumentEnd();
                    return;
                }

                var branchStart = _index;
                _index++; // consume '{'
                var branchOutput = new StringBuilder();
                RenderMessage(branchOutput, arguments, culture, locale, terminator: '}', collectOnly);
                if (_index >= _message.Length || _message[_index] != '}')
                {
                    AddError(branchStart, $"Unterminated plural branch '{selector}'.");
                    return;
                }

                _index++; // consume '}'
                if (!branches.TryAdd(selector, branchOutput.ToString()))
                {
                    AddError(branchStart, $"Duplicate plural selector '{selector}'.");
                }

                if (selector is "other")
                {
                    hasOther = true;
                }
            }

            if (!hasOther)
            {
                AddError(argumentStart, $"Plural argument '{name}' is missing the required 'other' branch.");
            }

            if (collectOnly)
            {
                return;
            }

            if (!arguments.TryGetValue(name, out var countValue) || !TryGetInteger(countValue, out var count))
            {
                AddError(argumentStart, $"Plural argument '{name}' requires an integer value.");
                output.Append('{').Append(name).Append('}');
                return;
            }

            var exactSelector = "=" + count.ToString(CultureInfo.InvariantCulture);
            if (branches.TryGetValue(exactSelector, out var exactBranch))
            {
                output.Append(ReplacePound(exactBranch, count, culture));
                return;
            }

            var category = GetPluralCategory(LocaleTags.GetLanguage(locale), count);
            if (branches.TryGetValue(category, out var categoryBranch))
            {
                output.Append(ReplacePound(categoryBranch, count, culture));
                return;
            }

            if (branches.TryGetValue("other", out var otherBranch))
            {
                output.Append(ReplacePound(otherBranch, count, culture));
                return;
            }

            output.Append('{').Append(name).Append('}');
        }

        /// <summary>Replaces ICU '#' with the formatted count in a plural branch.</summary>
        private static string ReplacePound(string branch, long count, CultureInfo culture) =>
            branch.Replace("#", count.ToString(culture), StringComparison.Ordinal);

        private void ExpectClosingBrace(string name, int start)
        {
            SkipWhitespace();
            if (_index < _message.Length && _message[_index] == '}')
            {
                _index++;
                return;
            }

            AddError(start, $"Expected '}}' to close argument '{name}'.");
            SkipToArgumentEnd();
        }

        private string ReadIdentifier()
        {
            var start = _index;
            if (_index < _message.Length && (char.IsAsciiLetter(_message[_index]) || _message[_index] == '_'))
            {
                _index++;
                while (_index < _message.Length && (char.IsAsciiLetterOrDigit(_message[_index]) || _message[_index] == '_'))
                {
                    _index++;
                }
            }

            return _message[start.._index];
        }

        private string ReadPluralSelector()
        {
            var start = _index;
            if (_index < _message.Length && _message[_index] == '=')
            {
                _index++;
                while (_index < _message.Length && char.IsAsciiDigit(_message[_index]))
                {
                    _index++;
                }

                return _message[start.._index];
            }

            return ReadIdentifier();
        }

        private void SkipWhitespace()
        {
            while (_index < _message.Length && char.IsWhiteSpace(_message[_index]))
            {
                _index++;
            }
        }

        private void SkipToArgumentEnd()
        {
            var depth = 0;
            while (_index < _message.Length)
            {
                var current = _message[_index];
                if (current == '{')
                {
                    depth++;
                }
                else if (current == '}')
                {
                    if (depth == 0)
                    {
                        _index++;
                        return;
                    }

                    depth--;
                }

                _index++;
            }
        }

        private void RecordPlaceholder(string name, MessagePlaceholderKind kind, bool collectOnly)
        {
            if (!collectOnly || _placeholders is null)
            {
                return;
            }

            if (!_placeholders.Any(existing => string.Equals(existing.Name, name, StringComparison.Ordinal) && existing.Kind == kind))
            {
                _placeholders.Add(new MessagePlaceholder(name, kind));
            }
        }

        private void AddError(int position, string message) => _errors.Add($"{message} (at offset {position})");

        private static string FormatSimpleValue(object? value, CultureInfo culture) => value switch
        {
            null => string.Empty,
            string text => text,
            IFormattable formattable => formattable.ToString(format: null, culture) ?? string.Empty,
            _ => Convert.ToString(value, culture) ?? string.Empty,
        };

        private string FormatNumberLike(object? value, CultureInfo culture, string name, int start)
        {
            if (value is null)
            {
                AddError(start, $"Number argument '{name}' is null.");
                return string.Empty;
            }

            return value is IFormattable formattable
                ? formattable.ToString(format: null, culture) ?? string.Empty
                : Convert.ToString(value, culture) ?? string.Empty;
        }
    }

    private static bool TryGetInteger(object? value, out long result)
    {
        result = 0;
        switch (value)
        {
            case null:
                return false;
            case int intValue:
                result = intValue;
                return true;
            case long longValue:
                result = longValue;
                return true;
            case short shortValue:
                result = shortValue;
                return true;
            case byte byteValue:
                result = byteValue;
                return true;
            case uint uintValue:
                result = uintValue;
                return true;
            case ulong ulongValue when ulongValue <= long.MaxValue:
                result = (long)ulongValue;
                return true;
            case decimal decimalValue when decimalValue == decimal.Truncate(decimalValue):
                result = (long)decimalValue;
                return true;
            case double doubleValue when doubleValue == Math.Truncate(doubleValue) && !double.IsInfinity(doubleValue):
                result = (long)doubleValue;
                return true;
            case string text:
                return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
            default:
                return false;
        }
    }

    /// <summary>CLDR cardinal plural category for the languages FluidBar ships/validates today.</summary>
    private static string GetPluralCategory(string language, long count) => language switch
    {
        "zh" or "ja" or "ko" or "th" or "vi" or "id" or "tr" => "other",
        "ru" or "uk" or "be" => GetRussianCategory(count),
        "ar" => GetArabicCategory(count),
        "fr" => count is 0 or 1 ? "one" : "other",
        "pt" => count is 0 or 1 ? "one" : "other",
        _ => count == 1 ? "one" : "other",
    };

    private static string GetRussianCategory(long count)
    {
        var mod10 = count % 10;
        var mod100 = count % 100;
        if (mod10 == 1 && mod100 != 11)
        {
            return "one";
        }

        if (mod10 is >= 2 and <= 4 && mod100 is not (12 or 13 or 14))
        {
            return "few";
        }

        return "many";
    }

    private static string GetArabicCategory(long count)
    {
        if (count == 0)
        {
            return "zero";
        }

        if (count == 1)
        {
            return "one";
        }

        if (count == 2)
        {
            return "two";
        }

        var mod100 = count % 100;
        if (mod100 is >= 3 and <= 10)
        {
            return "few";
        }

        if (mod100 is >= 11 and <= 99)
        {
            return "many";
        }

        return "other";
    }
}
