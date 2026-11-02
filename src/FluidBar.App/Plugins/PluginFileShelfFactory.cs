using FluidBar.App.Localization;
using FluidBar.App.Views;
using FluidBar.Sdk;
using FluidBar.Sdk.Manifest;

namespace FluidBar.App.Plugins;

/// <summary>Production file-reference capability construction, shared by startup and regression tests.</summary>
internal static class PluginFileShelfFactory
{
    internal static IPluginFileShelf? Create(PluginManifest manifest, ILocalizer localizer,
        Func<Action<IReadOnlyList<string>>, IDisposable>? registerDrop,
        Func<IReadOnlyList<string>, CancellationToken, Task<bool>>? copyFiles)
    {
        // Synthetic built-in manifests legitimately have no capability object. Do not create a
        // file-reference service until both file access and host UI were actually declared.
        if (manifest.Capabilities is not { Files: not null, Ui: not null } capabilities) return null;
        return new FileShelfPresenter(localizer, registerDrop,
            capabilities.Clipboard is { Write: true } ? copyFiles : null);
    }
}
