using FluidBar.Sdk.Localization;

namespace FluidBar.Sdk.Tests;

public sealed class MessageFormatTests
{
    private static string Format(string template, params (string Key, object? Value)[] arguments) =>
        MessageFormat.Format(
            template,
            arguments.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal));

    [Fact]
    public void Formats_simple_placeholders()
    {
        Assert.Equal("Hello, Ada!", Format("Hello, {name}!", ("name", "Ada")));
        Assert.Equal("x y z", Format("{a} {b} {c}", ("a", "x"), ("b", "y"), ("c", "z")));
    }

    [Fact]
    public void Escapes_braces_and_quotes()
    {
        Assert.Equal("{literal}", Format("'{literal}'"));
        Assert.Equal("it's", Format("it''s"));
    }

    [Fact]
    public void Missing_argument_is_reported_but_not_thrown()
    {
        var ok = MessageFormat.TryFormat("Hello {name}", null, "en-US", out var result, out var errors);
        Assert.False(ok);
        Assert.Equal("Hello {name}", result);
        Assert.NotEmpty(errors);
    }

    [Theory]
    [InlineData("en-US", 1, "one")]
    [InlineData("en-US", 2, "other")]
    [InlineData("zh-Hans", 1, "other")]
    [InlineData("ru-RU", 1, "one")]
    [InlineData("ru-RU", 3, "few")]
    [InlineData("ru-RU", 5, "many")]
    [InlineData("ru-RU", 21, "one")]
    [InlineData("ar-SA", 0, "zero")]
    [InlineData("ar-SA", 2, "two")]
    [InlineData("ar-SA", 11, "many")]
    public void Plural_branch_follows_cldr_categories(string locale, int count, string messagePart)
    {
        const string Template = "{count, plural, zero {zero} one {one} two {two} few {few} many {many} other {other}}";
        var result = MessageFormat.Format(
            Template,
            new Dictionary<string, object?> { ["count"] = count },
            locale);
        Assert.Equal(messagePart, result);
    }

    [Fact]
    public void Plural_exact_selector_wins()
    {
        const string Template = "{count, plural, =0 {none} one {# item} other {# items}}";
        Assert.Equal("none", MessageFormat.Format(Template, new Dictionary<string, object?> { ["count"] = 0 }, "en-US"));
        Assert.Equal("1 item", MessageFormat.Format(Template, new Dictionary<string, object?> { ["count"] = 1 }, "en-US"));
        Assert.Equal("3 items", MessageFormat.Format(Template, new Dictionary<string, object?> { ["count"] = 3 }, "en-US"));
    }

    [Fact]
    public void Plural_requires_other_branch()
    {
        var valid = MessageFormat.TryValidate("{count, plural, one {#}}", out var errors);
        Assert.False(valid);
        Assert.Contains(errors, error => error.Contains("other", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Extracts_placeholders_including_plural_branches()
    {
        var placeholders = MessageFormat.ExtractPlaceholders("Hi {name}, {count, plural, =0 {none} other {# items}}");
        Assert.Contains(new MessagePlaceholder("name", MessagePlaceholderKind.Simple), placeholders);
        Assert.Contains(new MessagePlaceholder("count", MessagePlaceholderKind.Plural), placeholders);
    }

    [Fact]
    public void Placeholder_parity_detects_mismatches()
    {
        var en = new LocaleBundle
        {
            Locale = "en-US",
            Messages = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["greeting"] = "Hello {name}",
                ["count"] = "{count, plural, other {# items}}",
            },
        };
        var zh = new LocaleBundle
        {
            Locale = "zh-Hans",
            Messages = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["greeting"] = "你好",
                ["count"] = "{count, plural, other {# 项}}",
            },
        };

        var mismatches = LocaleBundleConsistency.FindPlaceholderMismatches(en, zh);
        Assert.Contains(mismatches, mismatch => mismatch.Contains("greeting", StringComparison.Ordinal));
    }

    [Fact]
    public void Invalid_template_reports_position()
    {
        Assert.False(MessageFormat.TryValidate("Hello {name", out var errors));
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Number_formatting_uses_locale()
    {
        var result = MessageFormat.Format(
            "{value, number}",
            new Dictionary<string, object?> { ["value"] = 1234.5 },
            "de-DE");
        Assert.Contains("1234,5", result, StringComparison.Ordinal);
    }
}

public sealed class LocaleCatalogTests
{
    [Fact]
    public void Loads_valid_bundle()
    {
        var json = TestPackageFactory.LocaleJson("en-US", ("island.title", "Hello"), ("tracks.count", "{count, plural, one {# track} other {# tracks}}"));
        Assert.True(LocaleBundleLoader.TryParse(json, out var bundle, out var errors), string.Join("; ", errors));
        Assert.NotNull(bundle);
        Assert.Equal("en-US", bundle!.Locale);
        Assert.Equal(2, bundle.Messages.Count);
    }

    [Fact]
    public void Rejects_bundle_with_invalid_icu()
    {
        var json = TestPackageFactory.LocaleJson("en-US", ("bad", "{count, plural, one {#}}"));
        Assert.False(LocaleBundleLoader.TryParse(json, out _, out var errors));
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Rejects_non_canonical_locale()
    {
        var json = TestPackageFactory.LocaleJson("english-US", ("a", "b"));
        Assert.False(LocaleBundleLoader.TryParse(json, out _, out _));
    }

    [Fact]
    public void Catalog_falls_back_to_parent_then_default()
    {
        var catalog = new MessageCatalog(
            "en-US",
            [
                new LocaleBundle { Locale = "en-US", Messages = new Dictionary<string, string>(StringComparer.Ordinal) { ["a"] = "A", ["only"] = "EN" } },
                new LocaleBundle { Locale = "zh-Hans", Messages = new Dictionary<string, string>(StringComparer.Ordinal) { ["a"] = "A-zh" } },
            ],
            hostFallbackLocales: ["en-US"]);

        Assert.Equal("A-zh", catalog.Get("a", "zh-Hans-CN"));
        Assert.Equal("EN", catalog.Get("only", "zh-Hans"));
        Assert.Equal("missing.key", catalog.Get("missing.key", "zh-Hans"));
    }

    [Fact]
    public void Host_fallback_locale_is_used_after_plugin_locales()
    {
        var catalog = new MessageCatalog(
            "en-US",
            [
                new LocaleBundle { Locale = "en-US", Messages = new Dictionary<string, string>(StringComparer.Ordinal) { ["a"] = "A" } },
            ],
            hostFallbackLocales: ["zh-Hans"]);

        Assert.Equal("A", catalog.Get("a", "ja-JP"));
    }

    [Fact]
    public void Format_uses_catalog_value()
    {
        var catalog = new MessageCatalog(
            "en-US",
            [new LocaleBundle { Locale = "en-US", Messages = new Dictionary<string, string>(StringComparer.Ordinal) { ["greet"] = "Hi {name}" } }]);
        Assert.Equal("Hi Ada", catalog.Format("greet", new Dictionary<string, object?> { ["name"] = "Ada" }, "en-US"));
    }

    [Fact]
    public void Serialize_is_deterministic()
    {
        var bundle = new LocaleBundle
        {
            Locale = "en-US",
            Messages = new Dictionary<string, string>(StringComparer.Ordinal) { ["b"] = "2", ["a"] = "1" },
        };
        var first = LocaleBundleLoader.Serialize(bundle);
        var second = LocaleBundleLoader.Serialize(bundle with
        {
            Messages = new Dictionary<string, string>(StringComparer.Ordinal) { ["a"] = "1", ["b"] = "2" },
        });
        Assert.Equal(first, second);
        Assert.True(first.IndexOf("\"a\"", StringComparison.Ordinal) < first.IndexOf("\"b\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Locale_tags_expand_parent_chain()
    {
        Assert.Equal(["zh-Hans-CN", "zh-Hans", "zh"], LocaleTags.EnumerateParents("zh-Hans-CN"));
        Assert.Equal(["en-US", "en", "zh-Hans", "zh"], LocaleTags.EnumerateCandidates("en-US", "zh-Hans"));
    }
}
