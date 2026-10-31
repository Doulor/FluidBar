using System.Diagnostics;
using Avalonia.Threading;
using FluidBar.App.Settings;
using FluidBar.Core.Diagnostics;
using FluidBar.Core.Settings;

namespace FluidBar.App;

internal sealed partial class AppBootstrap
{
    private async Task ExecuteQuickActionAsync(QuickActionSetting action, CancellationToken cancellationToken)
    {
        if (_settings is null || _platformActions is null)
        {
            throw new InvalidOperationException("The island is not available.");
        }

        var executor = new QuickActionExecutor(_settings, _platformActions.InvokeAndWaitAsync,
            static (target, token) => Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                if (!File.Exists(target) && !Directory.Exists(target))
                {
                    throw new FileNotFoundException("The quick-action target does not exist.");
                }

                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }, token),
            async (pluginId, commandId, token) =>
            {
                if (_pluginRuntime is null) { throw new InvalidOperationException("Plugins are not available."); }
                var result = await _pluginRuntime.ExecuteCommandAsync(pluginId, commandId,
                    cancellationToken: token).ConfigureAwait(false);
                if (!result.Executed || !result.Succeeded)
                {
                    throw new InvalidOperationException("The plugin command is unavailable or failed.");
                }
            }, () => Dispatcher.UIThread.Post(OpenSettings));
        try
        {
            await executor.ExecuteAsync(action, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Platform, "quick-action.failed",
                "A user-invoked quick action could not complete.", ex);
            throw;
        }
    }
}
