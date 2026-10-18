using System.Text.Json.Nodes;
using FluidBar.Sdk.Contributions;
using FluidBar.Sdk.Json;
using FluidBar.Sdk.Manifest;

namespace FluidBar.Sdk.Tests;

/// <summary>
/// Contract tests for the declarative settings-page descriptor the host/UI renders: pages with groups and
/// rows (toggle/slider/select/text/path/button/status), i18n titles/descriptions, validation + limits.
/// </summary>
public sealed class SettingsDescriptorTests
{
    private static string PageManifest(string settingsPageJson) =>
        TestPackageFactory.MinimalManifestJson(customize: manifest =>
        {
            manifest["capabilities"] = new JsonObject
            {
                ["ui"] = new JsonObject { ["island"] = true, ["settings"] = true, ["commands"] = true, ["xaml"] = false },
            };
            manifest["contributes"] = new JsonObject
            {
                ["layouts"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["id"] = "sample-minimal.island",
                        ["surface"] = "island.compact",
                        ["template"] = "text.v1",
                        ["slots"] = new JsonObject { ["title"] = new JsonObject { ["field"] = "title" } },
                    },
                },
                ["commands"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["id"] = "sample-minimal.clear",
                        ["title"] = new JsonObject { ["en-US"] = "Clear" },
                        ["contexts"] = new JsonArray("settings"),
                    },
                },
                ["settingsPages"] = JsonNode.Parse(settingsPageJson),
            };
        });

    private const string GroupsPage = """
        [
          {
            "id": "general",
            "title": { "en-US": "General", "zh-Hans": "常规" },
            "description": { "en-US": "Settings rendered by the host." },
            "groups": [
              {
                "id": "playback",
                "title": { "en-US": "Playback", "zh-Hans": "播放" },
                "controls": [
                  { "type": "toggle", "key": "showDetails", "title": { "en-US": "Show details" }, "default": true },
                  { "type": "slider", "key": "refreshMinutes", "title": { "en-US": "Refresh" }, "min": 1, "max": 60, "step": 1, "default": 5, "unit": "min" }
                ]
              },
              {
                "id": "diagnostics",
                "title": { "en-US": "Diagnostics", "zh-Hans": "诊断" },
                "controls": [
                  { "type": "status", "key": "status.lastRefresh", "format": "timestamp", "title": { "en-US": "Last refresh" } },
                  { "type": "select", "key": "mode", "title": { "en-US": "Mode" }, "default": "auto",
                    "options": [ { "value": "auto", "label": { "en-US": "Automatic" } }, { "value": "manual", "label": { "en-US": "Manual" } } ] },
                  { "type": "path", "key": "cacheDir", "title": { "en-US": "Cache directory" } },
                  { "type": "text", "key": "displayName", "title": { "en-US": "Display name" }, "validation": { "minLength": 1, "maxLength": 40 } },
                  { "type": "button", "key": "clear", "command": "sample-minimal.clear", "title": { "en-US": "Clear" }, "style": "secondary" }
                ]
              }
            ]
          }
        ]
        """;

    [Fact]
    public void Groups_and_status_rows_parse_into_typed_descriptors()
    {
        var manifest = PluginManifestParser.Parse(PageManifest(GroupsPage));
        var page = Assert.Single(manifest.Contributes!.SettingsPages!);
        Assert.Null(page.Controls);
        Assert.Equal(2, page.Groups!.Count);
        var diagnostics = page.Groups[1];
        Assert.Equal("diagnostics", diagnostics.Id);
        Assert.Equal(5, diagnostics.Controls!.Count);
        var status = diagnostics.Controls[0];
        Assert.Equal(SettingControlType.Status, status.Type);
        Assert.Equal(SettingStatusFormat.Timestamp, status.Format);
        var button = diagnostics.Controls[4];
        Assert.Equal(SettingControlType.Button, button.Type);
        Assert.Equal("sample-minimal.clear", button.Command);
    }

    [Fact]
    public void Groups_and_status_rows_validate()
    {
        var manifest = PluginManifestParser.Parse(PageManifest(GroupsPage));
        var result = PluginManifestValidator.Validate(manifest);
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void Loose_controls_form_is_still_supported()
    {
        var manifest = PluginManifestParser.Parse(PageManifest("""
            [
              {
                "id": "general",
                "title": { "en-US": "General" },
                "controls": [ { "type": "toggle", "key": "enabled", "title": { "en-US": "Enabled" }, "default": false } ]
              }
            ]
            """));
        var result = PluginManifestValidator.Validate(manifest);
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void Page_needs_at_least_one_control_or_group()
    {
        var manifest = PluginManifestParser.Parse(PageManifest("""
            [ { "id": "general", "title": { "en-US": "General" } } ]
            """));
        var result = PluginManifestValidator.Validate(manifest);
        Assert.Contains(result.Errors, issue => issue.Code == "manifest.contributes.settingsPage.controls.missing");
    }

    [Fact]
    public void Group_needs_a_title_and_controls()
    {
        var manifest = PluginManifestParser.Parse(PageManifest("""
            [ { "id": "general", "title": { "en-US": "General" }, "groups": [ { "id": "empty", "title": { "en-US": "G" }, "controls": [] } ] } ]
            """));
        var result = PluginManifestValidator.Validate(manifest);
        Assert.Contains(result.Errors, issue => issue.Code == "manifest.contributes.settingGroup.controls.missing");
    }

    [Fact]
    public void Duplicate_setting_key_across_controls_and_groups_is_rejected()
    {
        var manifest = PluginManifestParser.Parse(PageManifest("""
            [
              {
                "id": "general",
                "title": { "en-US": "General" },
                "controls": [ { "type": "toggle", "key": "enabled", "title": { "en-US": "Enabled" } } ],
                "groups": [ { "id": "more", "title": { "en-US": "More" }, "controls": [ { "type": "text", "key": "enabled", "title": { "en-US": "Enabled again" } } ] } ]
              }
            ]
            """));
        var result = PluginManifestValidator.Validate(manifest);
        Assert.Contains(result.Errors, issue => issue.Code == "manifest.contributes.setting.key.duplicate");
    }

    [Fact]
    public void Duplicate_group_id_is_rejected()
    {
        var manifest = PluginManifestParser.Parse(PageManifest("""
            [
              {
                "id": "general",
                "title": { "en-US": "General" },
                "groups": [
                  { "id": "group-a", "title": { "en-US": "A" }, "controls": [ { "type": "toggle", "key": "one", "title": { "en-US": "One" } } ] },
                  { "id": "group-a", "title": { "en-US": "A2" }, "controls": [ { "type": "toggle", "key": "two", "title": { "en-US": "Two" } } ] }
                ]
              }
            ]
            """));
        var result = PluginManifestValidator.Validate(manifest);
        Assert.Contains(result.Errors, issue => issue.Code == "manifest.contributes.settingGroup.duplicate");
    }

    [Fact]
    public void Status_row_cannot_carry_range_or_options()
    {
        var manifest = PluginManifestParser.Parse(PageManifest("""
            [
              {
                "id": "general",
                "title": { "en-US": "General" },
                "controls": [ { "type": "status", "key": "status.value", "title": { "en-US": "Status" }, "min": 0, "max": 10 } ]
              }
            ]
            """));
        var result = PluginManifestValidator.Validate(manifest);
        Assert.Contains(result.Errors, issue => issue.Code == "manifest.contributes.setting.status.shape");
    }

    [Fact]
    public void Status_refresh_command_must_be_declared()
    {
        var manifest = PluginManifestParser.Parse(PageManifest("""
            [
              {
                "id": "general",
                "title": { "en-US": "General" },
                "controls": [ { "type": "status", "key": "status.value", "title": { "en-US": "Status" }, "command": "sample-minimal.unknown" } ]
              }
            ]
            """));
        var result = PluginManifestValidator.Validate(manifest);
        Assert.Contains(result.Errors, issue => issue.Code == "manifest.contributes.setting.status.command");
    }

    [Fact]
    public void Too_many_groups_is_rejected()
    {
        var groups = new JsonArray();
        for (var index = 0; index < PluginManifestLimits.MaxGroupsPerPage + 1; index++)
        {
            var controls = new JsonArray
            {
                new JsonObject { ["type"] = "toggle", ["key"] = "toggle" + index, ["title"] = new JsonObject { ["en-US"] = "T" } },
            };
            groups.Add(new JsonObject { ["id"] = "group-" + index, ["title"] = new JsonObject { ["en-US"] = "G" }, ["controls"] = controls });
        }

        var manifestJson = PageManifest(new JsonArray
        {
            new JsonObject { ["id"] = "general", ["title"] = new JsonObject { ["en-US"] = "General" }, ["groups"] = groups },
        }.ToJsonString());
        var manifest = PluginManifestParser.Parse(manifestJson);
        var result = PluginManifestValidator.Validate(manifest);
        Assert.Contains(result.Errors, issue => issue.Code == "manifest.contributes.settingsPage.groups.tooMany");
    }

    [Fact]
    public void Json_schema_files_are_valid_json_and_describe_settings_groups()
    {
        var repositoryRoot = TestPaths.RepoRoot();
        foreach (var schema in new[] { "plugin.schema.json", "marketplace-index.schema.json", "locale.schema.json", "signature.schema.json" })
        {
            var text = File.ReadAllText(Path.Combine(repositoryRoot, "schemas", schema));
            using var document = System.Text.Json.JsonDocument.Parse(text);
            Assert.True(document.RootElement.TryGetProperty("$id", out _), $"{schema} must declare an $id.");
        }

        var pluginSchema = File.ReadAllText(Path.Combine(repositoryRoot, "schemas", "plugin.schema.json"));
        using var parsed = System.Text.Json.JsonDocument.Parse(pluginSchema);
        var definitions = parsed.RootElement.GetProperty("$defs");
        var settingsPage = definitions.GetProperty("settingsPage");
        Assert.True(settingsPage.GetProperty("properties").TryGetProperty("groups", out _));
        var controlTypes = definitions.GetProperty("settingControl").GetProperty("properties").GetProperty("type").GetProperty("enum");
        Assert.Contains(controlTypes.EnumerateArray(), value => value.GetString() == "status");
    }

    [Fact]
    public void Sample_rich_manifest_uses_the_final_settings_contract()
    {
        var repositoryRoot = TestPaths.RepoRoot();
        var manifest = PluginManifestParser.ParseFileAsync(
            Path.Combine(repositoryRoot, "plugins", "samples", "FluidBar.Plugins.SampleRich", "manifest.json")).GetAwaiter().GetResult();
        var result = PluginManifestValidator.Validate(manifest);
        Assert.True(result.IsValid, string.Join("; ", result.Errors));

        var page = Assert.Single(manifest.Contributes!.SettingsPages!);
        Assert.NotNull(page.Groups);
        Assert.Contains(page.Groups!, group => group.Controls!.Any(control => control.Type == SettingControlType.Status));
        Assert.Contains(page.Groups!, group => group.Controls!.Any(control => control.Type == SettingControlType.Button));
    }
}
