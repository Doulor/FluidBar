using Avalonia.Controls;
using Avalonia.Media;
using FluidBar.App.Localization;
using FluidBar.Platform.Windows.Agent;

namespace FluidBar.App.Views.Settings;

/// <summary>
/// The "Agent support" settings page. Informational only: it lists which agents FluidBar can observe
/// natively (read-only local files), which are not installed, and which have no readable local state.
/// There are no install buttons and no configuration writes - the page only reads paths.
/// </summary>
internal static class AgentSupportSectionView
{
    /// <summary>Builds the page from the detected support catalog.</summary>
    public static Control Build(IReadOnlyList<AgentSupportStatus> statuses, SettingsWindowContext context)
    {
        var palette = context.Palette;
        var localizer = context.Localizer;
        var panel = new StackPanel { Spacing = 0 };
        panel.Children.Add(SettingsWidgets.PaneHeader(
            localizer.Get(LocKeys.Settings.Support.Title),
            localizer.Get(LocKeys.Settings.Support.Subtitle),
            palette));

        var native = statuses.Where(static status => status.Kind != AgentSupportKind.Infeasible).ToArray();
        if (native.Length > 0)
        {
            panel.Children.Add(SettingsWidgets.Caption(localizer.Get(LocKeys.Settings.Support.Group.Native), palette));
            panel.Children.Add(SettingsWidgets.Group(palette, topBorder: true,
                native.Select(status => BuildNativeRow(status, context)).ToArray()));
        }

        var infeasible = statuses.Where(static status => status.Kind == AgentSupportKind.Infeasible).ToArray();
        if (infeasible.Length > 0)
        {
            panel.Children.Add(SettingsWidgets.Caption(
                localizer.Get(LocKeys.Settings.Support.Group.Infeasible), palette));
            panel.Children.Add(SettingsWidgets.Group(palette, topBorder: true,
                infeasible.Select(status => BuildInfeasibleRow(status, context)).ToArray()));
        }

        return panel;
    }

    private static Control BuildNativeRow(AgentSupportStatus status, SettingsWindowContext context)
    {
        var palette = context.Palette;
        var localizer = context.Localizer;
        var chip = SettingsControls.Chip(StateLabel(localizer, status.Kind), palette, SettingsChipKind.Tag,
            dot: StateColor(status.Kind));
        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(SettingsWidgets.Row(status.DisplayName, Describe(status, localizer), chip, palette));
        var source = SettingsControls.StatusLine(
            localizer.Get(LocKeys.Settings.Support.DataSource) + ": " + (status.DataPath ?? "—"),
            palette, SettingsTone.Neutral);
        source.Margin = new Avalonia.Thickness(0, 2, 0, 4);
        stack.Children.Add(source);
        if (status.Kind == AgentSupportKind.NotDetected)
        {
            var text = NoteText(localizer, status.NoteCode);
            var note = SettingsControls.StatusLine(
                string.IsNullOrEmpty(text) ? localizer.Get(LocKeys.Settings.Support.Note.NotDetected) : text,
                palette, SettingsTone.Warning);
            note.Margin = new Avalonia.Thickness(0, 0, 0, 8);
            stack.Children.Add(note);
        }

        return stack;
    }

    private static Control BuildInfeasibleRow(AgentSupportStatus status, SettingsWindowContext context)
    {
        var palette = context.Palette;
        var localizer = context.Localizer;
        var chip = SettingsControls.Chip(StateLabel(localizer, status.Kind), palette, SettingsChipKind.Tag,
            dot: StateColor(status.Kind));
        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(SettingsWidgets.Row(status.DisplayName, null, chip, palette));
        var note = SettingsControls.StatusLine(NoteText(localizer, status.NoteCode), palette,
            SettingsTone.Danger);
        note.Margin = new Avalonia.Thickness(0, 2, 0, 8);
        stack.Children.Add(note);
        return stack;
    }

    private static string? Describe(AgentSupportStatus status, ILocalizer localizer)
    {
        var fields = status.Id switch
        {
            "dsh" => LocKeys.Settings.Support.Fields.Dsh,
            "claude-code" => LocKeys.Settings.Support.Fields.Claude,
            "codex" => LocKeys.Settings.Support.Fields.Codex,
            "copilot" => LocKeys.Settings.Support.Fields.Copilot,
            "aider" => LocKeys.Settings.Support.Fields.Aider,
            "workbuddy" => LocKeys.Settings.Support.Fields.WorkBuddy,
            _ => null,
        };
        return fields is null ? null : localizer.Get(fields);
    }

    private static string NoteText(ILocalizer localizer, string? noteCode) => noteCode switch
    {
        "sqlite-state" => localizer.Get(LocKeys.Settings.Support.Note.SqliteState),
        "ide-only" => localizer.Get(LocKeys.Settings.Support.Note.IdeOnly),
        "electron-only" => localizer.Get(LocKeys.Settings.Support.Note.ElectronOnly),
        "no-public-state" => localizer.Get(LocKeys.Settings.Support.Note.NoPublicState),
        "not-detected" => localizer.Get(LocKeys.Settings.Support.Note.NotDetected),
        "not-installed" => localizer.Get(LocKeys.Settings.Support.Note.NotInstalled),
        "no-activity" => localizer.Get(LocKeys.Settings.Support.Note.NoActivity),
        _ => string.Empty,
    };

    private static string StateLabel(ILocalizer localizer, AgentSupportKind kind) => kind switch
    {
        AgentSupportKind.Native => localizer.Get(LocKeys.Settings.Support.Status.Native),
        AgentSupportKind.Partial => localizer.Get(LocKeys.Settings.Support.Status.Partial),
        AgentSupportKind.Infeasible => localizer.Get(LocKeys.Settings.Support.Status.Infeasible),
        _ => localizer.Get(LocKeys.Settings.Support.Status.NotDetected),
    };

    private static Color StateColor(AgentSupportKind kind) => kind switch
    {
        AgentSupportKind.Native => Color.Parse("#30D158"),
        AgentSupportKind.Partial => Color.Parse("#FF9F0A"),
        AgentSupportKind.NotDetected => Color.Parse("#0A84FF"),
        _ => Color.Parse("#FF453A"),
    };
}
