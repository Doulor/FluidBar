using FluidBar.App.Hosting.Skia;
using FluidBar.Core.Settings;

namespace FluidBar.App.Settings;

/// <summary>Runs only an explicit user invocation; configuration and startup have no side effects.</summary>
public sealed class QuickActionExecutor(
    SettingsController settings,
    Func<IslandPlatformAction, CancellationToken, Task<IslandActionFeedback>> invoke,
    Func<string, CancellationToken, Task> openPath,
    Func<string, string, CancellationToken, Task> pluginCommand,
    Action openSettings)
{
    public async Task ExecuteAsync(QuickActionSetting action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action = action.Normalize();
        if (action.Validate() is not null)
        {
            throw new ArgumentException("The quick action is incomplete or invalid.", nameof(action));
        }

        switch (action.Kind)
        {
            case "settings":
                openSettings();
                return;
            case "clipboard-pause":
                settings.Update(current => current with { ClipboardPaused = !current.ClipboardPaused });
                return;
            case "open":
                await openPath(action.Target, cancellationToken).ConfigureAwait(false);
                return;
            case "plugin":
                action.TryGetPluginCommand(out var pluginId, out var commandId);
                await pluginCommand(pluginId, commandId, cancellationToken).ConfigureAwait(false);
                return;
        }

        var request = action.Kind switch
        {
            "theme" => new IslandPlatformAction(IslandPlatformActionKind.QuickToggle, Toggle: IslandQuickToggle.Theme),
            "mute" => new IslandPlatformAction(IslandPlatformActionKind.VolumeMute),
            "sound" => new IslandPlatformAction(IslandPlatformActionKind.MediaOutput),
            "clipboard-clear" => new IslandPlatformAction(IslandPlatformActionKind.ClipboardClearAll),
            "clipboard-clear-all" => new IslandPlatformAction(IslandPlatformActionKind.ClipboardClearIncludingPinned),
            _ => throw new InvalidOperationException("Unknown quick action."),
        };
        var result = await invoke(request, cancellationToken).ConfigureAwait(false);
        if (result.Status != IslandActionFeedbackStatus.Succeeded)
        {
            throw new InvalidOperationException("The host could not complete the requested action.");
        }
    }
}
