using FluidBar.Sdk.Manifest;

namespace FluidBar.Sdk.Tests;

public sealed class ManifestParserTests
{
    [Fact]
    public void Parses_full_manifest_round_trip()
    {
        var json = TestPackageFactory.MinimalManifestJson(id: "now-playing", version: "2.1.0");
        Assert.True(PluginManifestParser.TryParse(json, out var manifest, out var errors), string.Join("; ", errors));
        Assert.NotNull(manifest);
        Assert.Equal(2, manifest!.SchemaVersion);
        Assert.Equal("now-playing", manifest.Id);
        Assert.Equal("2.1.0", manifest.Version);
        Assert.Equal("test-publisher-2026", manifest.Publisher!.KeyId);
        Assert.Equal("plugin/Fake.Plugin.dll", manifest.Entry!.Assembly);
        Assert.Equal("Fake.Plugin.Plugin", manifest.Entry.Type);
        Assert.Equal("en-US", manifest.Locales!.Default);
        Assert.Single(manifest.Contributes!.Layouts!);
    }

    [Fact]
    public void Rejects_unknown_properties()
    {
        var json = TestPackageFactory.MinimalManifestJson(customize: manifest => manifest["unexpectedProperty"] = true);
        Assert.False(PluginManifestParser.TryParse(json, out _, out var errors));
        Assert.Contains(errors, error => error.Contains("unexpectedProperty", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_invalid_json()
    {
        Assert.False(PluginManifestParser.TryParse("{ not json", out _, out var errors));
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Rejects_oversized_manifest()
    {
        var json = TestPackageFactory.MinimalManifestJson(customize: manifest =>
            manifest["description"] = new string('x', PluginManifestLimits.MaxManifestBytes));
        Assert.False(PluginManifestParser.TryParse(json, out _, out var errors));
        Assert.Contains(errors, error => error.Contains("limit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Localized_string_accepts_string_and_map_forms()
    {
        var json = TestPackageFactory.MinimalManifestJson(customize: manifest =>
        {
            manifest["name"] = new System.Text.Json.Nodes.JsonObject
            {
                ["en-US"] = "Name",
                ["zh-Hans"] = "名称",
            };
        });
        var manifest2 = PluginManifestParser.Parse(json);
        Assert.Equal("Name", manifest2.Name!.Resolve("en-US"));
        Assert.Equal("名称", manifest2.Name.Resolve("zh-Hans"));
        Assert.Equal("Name", manifest2.Name.Resolve("ja"));
    }

    [Fact]
    public void Parses_capabilities_and_contributions()
    {
        var json = TestPackageFactory.MinimalManifestJson(customize: manifest =>
        {
            manifest["capabilities"] = new System.Text.Json.Nodes.JsonObject
            {
                ["network"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["domains"] = new System.Text.Json.Nodes.JsonArray("api.example.com"),
                    ["methods"] = new System.Text.Json.Nodes.JsonArray("GET"),
                    ["reason"] = "Fetch lyrics for the current track.",
                },
                ["background"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["intervalMinMs"] = 1000,
                    ["reason"] = "Refresh the current media card.",
                },
            };
            manifest["contributes"] = new System.Text.Json.Nodes.JsonObject
            {
                ["settingsPages"] = new System.Text.Json.Nodes.JsonArray
                {
                    new System.Text.Json.Nodes.JsonObject
                    {
                        ["id"] = "general",
                        ["title"] = new System.Text.Json.Nodes.JsonObject { ["en-US"] = "General" },
                        ["controls"] = new System.Text.Json.Nodes.JsonArray
                        {
                            new System.Text.Json.Nodes.JsonObject
                            {
                                ["type"] = "toggle",
                                ["key"] = "enabled",
                                ["title"] = new System.Text.Json.Nodes.JsonObject { ["en-US"] = "Enabled" },
                                ["default"] = true,
                            },
                        },
                    },
                },
            };
        });

        var manifest3 = PluginManifestParser.Parse(json);
        Assert.Equal("api.example.com", manifest3.Capabilities!.Network!.Domains![0]);
        Assert.Equal(1000, manifest3.Capabilities.Background!.IntervalMinMs);
        var page = Assert.Single(manifest3.Contributes!.SettingsPages!);
        Assert.Equal(FluidBar.Sdk.Contributions.SettingControlType.Toggle, Assert.Single(page.Controls!).Type);
    }

    [Fact]
    public void Unknown_wire_enum_value_is_rejected()
    {
        var json = TestPackageFactory.MinimalManifestJson(customize: manifest =>
        {
            var entry = (System.Text.Json.Nodes.JsonObject)manifest["entry"]!;
            entry["runtime"] = "inprocess";
        });
        Assert.False(PluginManifestParser.TryParse(json, out _, out var errors));
        Assert.NotEmpty(errors);
    }

    [Fact]
    public async Task Parse_file_reads_manifest()
    {
        using var temp = TempDirectory.Create();
        var path = TestPackageFactory.WriteTemp(temp.Path, "manifest.json", TestPackageFactory.MinimalManifestJson());
        var manifest = await PluginManifestParser.ParseFileAsync(path);
        Assert.Equal("sample-minimal", manifest.Id);
    }
}

public sealed class ManifestValidatorTests
{
    private static PluginManifest ParseManifest(Action<System.Text.Json.Nodes.JsonObject>? customize = null) =>
        PluginManifestParser.Parse(TestPackageFactory.MinimalManifestJson(customize: customize));

    [Fact]
    public void Valid_manifest_passes()
    {
        var result = PluginManifestValidator.Validate(ParseManifest());
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void Reserved_id_is_rejected()
    {
        var result = PluginManifestValidator.Validate(ParseManifest(manifest => manifest["id"] = "system"));
        Assert.Contains(result.Errors, issue => issue.Code == "manifest.id.reserved");
    }

    [Theory]
    [InlineData("Bad_Id", "manifest.id.invalid")]
    [InlineData("-leading", "manifest.id.invalid")]
    [InlineData("ok-id", null)]
    public void Id_pattern(string id, string? expectedCode)
    {
        var result = PluginManifestValidator.Validate(ParseManifest(manifest => manifest["id"] = id));
        if (expectedCode is null)
        {
            Assert.DoesNotContain(result.Errors, issue => issue.Code == expectedCode);
        }
        else
        {
            Assert.Contains(result.Errors, issue => issue.Code == expectedCode);
        }
    }

    [Fact]
    public void Unsupported_api_major_is_rejected()
    {
        var result = PluginManifestValidator.Validate(ParseManifest(manifest => manifest["apiVersion"] = "3.0.0"));
        Assert.Contains(result.Errors, issue => issue.Code == "manifest.apiVersion.unsupported");
    }

    [Fact]
    public void Max_app_version_before_min_is_rejected()
    {
        var result = PluginManifestValidator.Validate(ParseManifest(manifest =>
        {
            manifest["minAppVersion"] = "2.5.0";
            manifest["maxAppVersion"] = "2.1.0";
        }));
        Assert.Contains(result.Errors, issue => issue.Code == "manifest.maxAppVersion.beforeMin");
    }

    [Fact]
    public void Locale_default_must_be_available()
    {
        var result = PluginManifestValidator.Validate(ParseManifest(manifest =>
        {
            manifest["locales"] = new System.Text.Json.Nodes.JsonObject
            {
                ["default"] = "en-US",
                ["available"] = new System.Text.Json.Nodes.JsonArray("zh-Hans"),
            };
        }));
        Assert.Contains(result.Errors, issue => issue.Code == "manifest.locales.default.notAvailable");
    }

    [Fact]
    public void Risky_network_capability_requires_reason()
    {
        var result = PluginManifestValidator.Validate(ParseManifest(manifest =>
        {
            manifest["capabilities"] = new System.Text.Json.Nodes.JsonObject
            {
                ["network"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["domains"] = new System.Text.Json.Nodes.JsonArray("api.example.com"),
                },
            };
        }));
        Assert.Contains(result.Errors, issue => issue.Code == "manifest.capability.reason.missing");
    }

    [Fact]
    public void Xaml_capability_is_rejected_for_marketplace()
    {
        var result = PluginManifestValidator.Validate(ParseManifest(manifest =>
        {
            manifest["capabilities"] = new System.Text.Json.Nodes.JsonObject
            {
                ["ui"] = new System.Text.Json.Nodes.JsonObject { ["island"] = true, ["xaml"] = true },
            };
        }));
        Assert.Contains(result.Errors, issue => issue.Code == "manifest.capabilities.ui.xaml");
    }

    [Fact]
    public void Out_of_process_runtime_is_rejected_until_supported()
    {
        var result = PluginManifestValidator.Validate(ParseManifest(manifest =>
        {
            var entry = (System.Text.Json.Nodes.JsonObject)manifest["entry"]!;
            entry["runtime"] = "outofproc";
        }));
        Assert.Contains(result.Errors, issue => issue.Code == "manifest.entry.runtimeUnsupported");
    }

    [Fact]
    public void Layout_ids_must_be_namespaced_under_plugin_id()
    {
        var result = PluginManifestValidator.Validate(ParseManifest(manifest =>
        {
            var layouts = (System.Text.Json.Nodes.JsonArray)manifest["contributes"]!["layouts"]!;
            ((System.Text.Json.Nodes.JsonObject)layouts[0]!)["id"] = "other-plugin.island";
        }));
        Assert.Contains(result.Errors, issue => issue.Code == "manifest.contributes.layout.id.namespace");
    }

    [Fact]
    public void Slot_must_be_valid_for_template()
    {
        var result = PluginManifestValidator.Validate(ParseManifest(manifest =>
        {
            var layouts = (System.Text.Json.Nodes.JsonArray)manifest["contributes"]!["layouts"]!;
            var slots = (System.Text.Json.Nodes.JsonObject)((System.Text.Json.Nodes.JsonObject)layouts[0]!)["slots"]!;
            slots["artwork"] = new System.Text.Json.Nodes.JsonObject { ["field"] = "art" };
        }));
        Assert.Contains(result.Errors, issue => issue.Code == "manifest.contributes.layout.slot.unknown");
    }

    [Fact]
    public void Command_references_must_exist()
    {
        var result = PluginManifestValidator.Validate(ParseManifest(manifest =>
        {
            manifest["contributes"] = new System.Text.Json.Nodes.JsonObject
            {
                ["contextMenu"] = new System.Text.Json.Nodes.JsonArray
                {
                    new System.Text.Json.Nodes.JsonObject
                    {
                        ["id"] = "sample-minimal.menu",
                        ["command"] = "missing.command",
                        ["title"] = new System.Text.Json.Nodes.JsonObject { ["en-US"] = "Do it" },
                        ["when"] = "source == 'sample-minimal'",
                    },
                },
            };
        }));
        Assert.Contains(result.Errors, issue => issue.Code == "manifest.contributes.contextMenu.command.unknown");
    }

    [Fact]
    public void Slider_control_requires_range_and_step()
    {
        var result = PluginManifestValidator.Validate(ParseManifest(manifest =>
        {
            manifest["contributes"] = new System.Text.Json.Nodes.JsonObject
            {
                ["settingsPages"] = new System.Text.Json.Nodes.JsonArray
                {
                    new System.Text.Json.Nodes.JsonObject
                    {
                        ["id"] = "general",
                        ["title"] = new System.Text.Json.Nodes.JsonObject { ["en-US"] = "General" },
                        ["controls"] = new System.Text.Json.Nodes.JsonArray
                        {
                            new System.Text.Json.Nodes.JsonObject
                            {
                                ["type"] = "slider",
                                ["key"] = "pollMs",
                                ["title"] = new System.Text.Json.Nodes.JsonObject { ["en-US"] = "Poll" },
                            },
                        },
                    },
                },
            };
        }));
        Assert.Contains(result.Errors, issue => issue.Code == "manifest.contributes.setting.slider.range");
    }

    [Fact]
    public void Sdk_dependency_range_is_checked()
    {
        var result = PluginManifestValidator.Validate(ParseManifest(manifest =>
            manifest["dependencies"] = new System.Text.Json.Nodes.JsonObject { ["sdk"] = "^3.0.0" }));
        Assert.Contains(result.Errors, issue => issue.Code == "manifest.dependencies.sdk.unsatisfied");
    }

    [Fact]
    public void Integrity_hash_must_be_lowercase_hex()
    {
        var result = PluginManifestValidator.Validate(ParseManifest(manifest =>
        {
            manifest["integrity"] = new System.Text.Json.Nodes.JsonObject { ["entrySha256"] = new string('A', 64) };
        }));
        Assert.Contains(result.Errors, issue => issue.Code == "manifest.integrity.entrySha256.invalid");
    }

    [Fact]
    public void Missing_required_blocks_are_reported()
    {
        var result = PluginManifestValidator.Validate(new PluginManifest());
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, issue => issue.Code == "manifest.id.missing");
        Assert.Contains(result.Errors, issue => issue.Code == "manifest.entry.missing");
        Assert.Contains(result.Errors, issue => issue.Code == "manifest.capabilities.missing");
        Assert.Contains(result.Errors, issue => issue.Code == "manifest.contributes.missing");
    }
}
