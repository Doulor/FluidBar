using System.Globalization;
using System.Text;

namespace FluidBar.App.Localization;

/// <summary>
/// ICU MessageFormat subset (spec 7.3): named arguments, <c>{count, plural, ...}</c>,
/// <c>{n, number}</c>, <c>{value, select, ...}</c>, <c>{value, date, short|medium|long}</c>,
/// the <c>#</c> plural placeholder and <c>'{'</c>/<c>''</c> escaping. Unknown constructs throw
/// <see cref="FormatException"/> instead of silently producing broken UI text.
/// </summary>
public static class MessageFormatter
{
    /// <summary>Formats <paramref name="template"/> with named arguments.</summary>
    /// <param name="template">Message template from a catalog.</param>
    /// <param name="arguments">Named argument values (culture-typed: numbers, dates, strings).</param>
    /// <param name="culture">Formatting culture.</param>
    public static string Format(string template, IReadOnlyDictionary<string, object?> arguments, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(culture);
        var builder = new StringBuilder(template.Length + 16);
        Render(template, 0, arguments, culture, builder, pluralValue: null, isPluralOption: false);
        return builder.ToString();
    }

    /// <summary>Convenience overload with tuple arguments.</summary>
    public static string Format(string template, CultureInfo culture, params (string Name, object? Value)[] arguments)
    {
        var dictionary = new Dictionary<string, object?>(arguments.Length, StringComparer.Ordinal);
        foreach (var (name, value) in arguments)
        {
            dictionary[name] = value;
        }

        return Format(template, dictionary, culture);
    }

    private static int Render(
        string template,
        int position,
        IReadOnlyDictionary<string, object?> arguments,
        CultureInfo culture,
        StringBuilder output,
        long? pluralValue,
        bool isPluralOption)
    {
        while (position < template.Length)
        {
            var c = template[position];
            switch (c)
            {
                case '\'':
                    position = RenderQuote(template, position, output);
                    break;

                case '#':
                    if (isPluralOption)
                    {
                        output.Append((pluralValue ?? 0).ToString("N0", culture));
                        position++;
                    }
                    else
                    {
                        output.Append(c);
                        position++;
                    }

                    break;

                case '{':
                    position = RenderArgument(template, position, arguments, culture, output);
                    break;

                case '}':
                    // A closing brace ends the current argument/option context when nested.
                    return position;

                default:
                    output.Append(c);
                    position++;
                    break;
            }
        }

        return position;
    }

    private static int RenderQuote(string template, int position, StringBuilder output)
    {
        // ICU quoting: '' is a literal quote; a quote before '{' opens a quoted literal section
        // that ends at the next single quote (all braces inside are literal text).
        if (position + 1 < template.Length && template[position + 1] == '\'')
        {
            output.Append('\'');
            return position + 2;
        }

        if (position + 1 < template.Length && (template[position + 1] == '{' || template[position + 1] == '}'))
        {
            var close = template.IndexOf('\'', position + 1);
            if (close < 0)
            {
                output.Append(template, position + 1, template.Length - position - 1);
                return template.Length;
            }

            output.Append(template, position + 1, close - position - 1);
            return close + 1;
        }

        output.Append('\'');
        return position + 1;
    }

    private static int RenderArgument(
        string template,
        int position,
        IReadOnlyDictionary<string, object?> arguments,
        CultureInfo culture,
        StringBuilder output)
    {
        var close = FindMatchingBrace(template, position);
        if (close < 0)
        {
            throw new FormatException($"Unterminated '{{' in message template: \"{template}\".");
        }

        var body = template.Substring(position + 1, close - position - 1);
        var firstComma = body.IndexOf(',');
        if (firstComma < 0)
        {
            AppendSimpleArgument(body.Trim(), arguments, culture, output);
            return close + 1;
        }

        var name = body[..firstComma].Trim();
        var remainder = body[(firstComma + 1)..];
        var secondComma = remainder.IndexOf(',');
        var type = (secondComma < 0 ? remainder : remainder[..secondComma]).Trim();
        var pattern = secondComma < 0 ? string.Empty : remainder[(secondComma + 1)..].Trim();

        switch (type)
        {
            case "number":
                AppendNumberArgument(name, pattern, arguments, culture, output);
                break;
            case "plural":
                AppendPluralArgument(name, pattern, arguments, culture, output);
                break;
            case "select":
                AppendSelectArgument(name, pattern, arguments, culture, output);
                break;
            case "date":
                AppendDateArgument(name, pattern, arguments, culture, output);
                break;
            default:
                throw new FormatException($"Unsupported ICU argument type '{type}' in \"{template}\".");
        }

        return close + 1;
    }

    private static void AppendSimpleArgument(
        string name,
        IReadOnlyDictionary<string, object?> arguments,
        CultureInfo culture,
        StringBuilder output)
    {
        if (!arguments.TryGetValue(name, out var value) || value is null)
        {
            output.Append('{').Append(name).Append('}');
            return;
        }

        switch (value)
        {
            case string text:
                output.Append(text);
                break;
            case IFormattable formattable:
                output.Append(formattable.ToString(null, culture));
                break;
            default:
                output.Append(value.ToString());
                break;
        }
    }

    private static void AppendNumberArgument(
        string name,
        string pattern,
        IReadOnlyDictionary<string, object?> arguments,
        CultureInfo culture,
        StringBuilder output)
    {
        if (!arguments.TryGetValue(name, out var value) || value is null)
        {
            output.Append('{').Append(name).Append('}');
            return;
        }

        var format = pattern.Length == 0 ? "N0" : pattern;
        output.Append(value is IFormattable formattable
            ? formattable.ToString(format, culture)
            : Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString(format, culture));
    }

    private static void AppendDateArgument(
        string name,
        string style,
        IReadOnlyDictionary<string, object?> arguments,
        CultureInfo culture,
        StringBuilder output)
    {
        if (!arguments.TryGetValue(name, out var value) || value is null)
        {
            output.Append('{').Append(name).Append('}');
            return;
        }

        var format = style switch
        {
            "short" => "d",
            "long" => "D",
            "medium" or "" => "f",
            _ => style,
        };

        output.Append(value switch
        {
            DateTimeOffset dto => dto.ToString(format, culture),
            DateTime dt => dt.ToString(format, culture),
            _ => value.ToString(),
        });
    }

    private static void AppendPluralArgument(
        string name,
        string pattern,
        IReadOnlyDictionary<string, object?> arguments,
        CultureInfo culture,
        StringBuilder output)
    {
        if (!arguments.TryGetValue(name, out var value) || value is null)
        {
            output.Append('{').Append(name).Append('}');
            return;
        }

        var number = ToDouble(value);
        var offset = ParseOffset(pattern, out var remainder);
        var options = ParseOptions(remainder, "plural", out var order);
        if (options.Count == 0)
        {
            throw new FormatException($"Plural argument '{name}' has no options.");
        }

        var adjusted = number - offset;
        var category = PluralRules.Category(culture, adjusted);
        string? selected = TrySelectPlural(options, adjusted, category, out var usedKey);
        if (selected is null)
        {
            throw new FormatException(
                $"Plural argument '{name}' has no 'other' option (available: {string.Join(", ", order)}).");
        }

        var builder = new StringBuilder(selected.Length + 8);
        Render(selected, 0, arguments, culture, builder, (long)Math.Round(adjusted), isPluralOption: true);
        AppendCategoryVariant(output, builder.ToString(), adjusted, usedKey);
    }

    private static void AppendSelectArgument(
        string name,
        string pattern,
        IReadOnlyDictionary<string, object?> arguments,
        CultureInfo culture,
        StringBuilder output)
    {
        if (!arguments.TryGetValue(name, out var value) || value is null)
        {
            output.Append('{').Append(name).Append('}');
            return;
        }

        var options = ParseOptions(pattern, "select", out var order);
        var key = value.ToString() ?? string.Empty;
        if (!options.TryGetValue(key, out var selected))
        {
            if (!options.TryGetValue("other", out selected))
            {
                throw new FormatException(
                    $"Select argument '{name}' has no option for '{key}' and no 'other' fallback (available: {string.Join(", ", order)}).");
            }
        }

        var builder = new StringBuilder(selected.Length + 8);
        Render(selected, 0, arguments, culture, builder, pluralValue: null, isPluralOption: false);
        output.Append(builder);
    }

    private static string? TrySelectPlural(
        IReadOnlyDictionary<string, string> options,
        double adjusted,
        string category,
        out string usedKey)
    {
        if (adjusted == Math.Floor(adjusted))
        {
            var exact = ((long)adjusted).ToString(CultureInfo.InvariantCulture);
            if (options.TryGetValue("=" + exact, out var exactText))
            {
                usedKey = "=" + exact;
                return exactText;
            }
        }

        if (options.TryGetValue(category, out var text))
        {
            usedKey = category;
            return text;
        }

        usedKey = "other";
        return options.TryGetValue("other", out var other) ? other : null;
    }

    private static void AppendCategoryVariant(StringBuilder output, string text, double number, string key)
    {
        // The exponent symbol is reserved for pseudo-locale diagnostics; normal catalogs never
        // contain it, so this is a no-op for production text.
        output.Append(text);
    }

    private static double ToDouble(object value)
    {
        return value switch
        {
            int i => i,
            long l => l,
            double d => d,
            float f => f,
            decimal m => (double)m,
            _ => double.Parse(value.ToString() ?? "0", CultureInfo.InvariantCulture),
        };
    }

    private static double ParseOffset(string pattern, out string remainder)
    {
        var trimmed = pattern.Trim();
        if (trimmed.StartsWith("offset:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = trimmed.IndexOf(',');
            var offsetText = comma < 0 ? trimmed["offset:".Length..] : trimmed["offset:".Length..comma];
            remainder = comma < 0 ? string.Empty : trimmed[(comma + 1)..];
            return double.Parse(offsetText.Trim(), CultureInfo.InvariantCulture);
        }

        remainder = trimmed;
        return 0;
    }

    private static Dictionary<string, string> ParseOptions(string pattern, string type, out List<string> order)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        order = [];
        var position = 0;
        while (position < pattern.Length)
        {
            while (position < pattern.Length && char.IsWhiteSpace(pattern[position]))
            {
                position++;
            }

            if (position >= pattern.Length)
            {
                break;
            }

            var selectorStart = position;
            while (position < pattern.Length && pattern[position] != '{')
            {
                position++;
            }

            if (position >= pattern.Length)
            {
                throw new FormatException($"Unterminated {type} selector in pattern \"{pattern}\".");
            }

            var selector = pattern[selectorStart..position].Trim();
            if (selector.Length == 0)
            {
                throw new FormatException($"Empty {type} selector in pattern \"{pattern}\".");
            }

            var close = FindMatchingBrace(pattern, position);
            if (close < 0)
            {
                throw new FormatException($"Unterminated {type} option body in pattern \"{pattern}\".");
            }

            var body = pattern.Substring(position + 1, close - position - 1);
            options[selector] = body;
            order.Add(selector);
            position = close + 1;
        }

        return options;
    }

    private static int FindMatchingBrace(string text, int openPosition)
    {
        var depth = 0;
        for (var i = openPosition; i < text.Length; i++)
        {
            var c = text[i];
            // Skip quoted literals: '' and '{' / '}' escapes must not affect depth.
            if (c == '\'' && i + 1 < text.Length)
            {
                if (text[i + 1] == '\'')
                {
                    i++;
                    continue;
                }

                if (text[i + 1] is '{' or '}')
                {
                    i++;
                    continue;
                }
            }

            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }
}

/// <summary>CLDR plural categories for the shipped and tested locales (spec 7.3).</summary>
public static class PluralRules
{
    /// <summary>Returns the plural category (<c>zero/one/two/few/many/other</c>) for a number.</summary>
    public static string Category(CultureInfo culture, double number)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var language = culture.TwoLetterISOLanguageName;
        if (number == 0 && language is "ar")
        {
            return "zero";
        }

        var isInteger = Math.Abs(number - Math.Round(number)) < 1e-9;
        return language switch
        {
            "en" or "de" or "es" or "it" or "nl" or "sv" or "da" or "no" or "fi" or "pt" or "fr"
                => Math.Abs(number - 1) < double.Epsilon ? "one" : "other",
            "ru" or "uk" when !isInteger => "other",
            "ar" when !isInteger => "other",
            "zh" or "ja" or "ko" or "vi" or "th" or "id" => "other",
            "ru" or "uk" => RussianCategory(number),
            "ar" => ArabicCategory(number),
            "pl" => PolishCategory(number),
            _ => "other",
        };
    }

    private static string RussianCategory(double number)
    {
        var n = (long)Math.Abs(number);
        var mod10 = n % 10;
        var mod100 = n % 100;
        if (mod10 == 1 && mod100 != 11)
        {
            return "one";
        }

        if (mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14)
        {
            return "few";
        }

        if (mod10 == 0 || mod10 is >= 5 and <= 9 || mod100 is >= 11 and <= 14)
        {
            return "many";
        }

        return "other";
    }

    private static string ArabicCategory(double number)
    {
        var n = (long)Math.Abs(number);
        if (n == 0)
        {
            return "zero";
        }

        if (n == 1)
        {
            return "one";
        }

        if (n == 2)
        {
            return "two";
        }

        var mod100 = n % 100;
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

    private static string PolishCategory(double number)
    {
        var n = (long)Math.Abs(number);
        var mod10 = n % 10;
        var mod100 = n % 100;
        if (n == 1)
        {
            return "one";
        }

        if (mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14)
        {
            return "few";
        }

        return "many";
    }
}
