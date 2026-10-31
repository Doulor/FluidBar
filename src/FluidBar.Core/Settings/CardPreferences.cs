using System.Collections.Immutable;

namespace FluidBar.Core.Settings;

/// <summary>User-configured card privacy and explicit quick actions; no action runs on load.</summary>
public sealed record CardPreferences
{
    public bool ClipboardPaused { get; init; }
    public bool ClipboardHidePreviews { get; init; }
    public ImmutableArray<QuickActionSetting> QuickActions { get; init; } = [];

    public CardPreferences Normalize()
    {
        ImmutableArray<QuickActionSetting> actions = QuickActions.IsDefault ? [] : [.. QuickActions
            .Where(static action => action is not null)
            .Take(16)
            .Select(static action => action.Normalize())
            .DistinctBy(static action => action.Id, StringComparer.Ordinal)];
        return !QuickActions.IsDefault && QuickActions.SequenceEqual(actions) ? this : this with { QuickActions = actions };
    }
}

/// <summary>A named, user-invoked host action or an installed plugin command.</summary>
public sealed record QuickActionSetting
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Kind { get; init; } = "theme";
    public string Target { get; init; } = string.Empty;

    public QuickActionSetting Normalize() => this with
    {
        Id = Trim(Id, 64), Name = Trim(Name, 48), Kind = Trim(Kind, 32), Target = Trim(Target, 2048),
    };

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name))
        {
            return "name";
        }

        return Kind switch
        {
            "theme" or "mute" or "clipboard-pause" or "clipboard-clear" or "clipboard-clear-all" or "settings" or "sound" => null,
            "open" when !string.IsNullOrWhiteSpace(Target) && !Target.Any(char.IsControl)
                && Path.IsPathFullyQualified(Target) => null,
            "plugin" when TryGetPluginCommand(out _, out _) => null,
            "open" or "plugin" => "target",
            _ => "kind",
        };
    }

    public bool TryGetPluginCommand(out string pluginId, out string commandId)
    {
        var target = Target ?? string.Empty;
        var parts = target.Split('/', 2, StringSplitOptions.TrimEntries);
        pluginId = parts[0];
        commandId = parts.Length == 2 ? parts[1] : string.Empty;
        return pluginId.Length > 0 && commandId.Length > 0 && !target.Any(char.IsControl);
    }

    private static string Trim(string? value, int max)
    {
        var text = value?.Trim() ?? string.Empty;
        return text.Length <= max ? text : text[..max];
    }
}
