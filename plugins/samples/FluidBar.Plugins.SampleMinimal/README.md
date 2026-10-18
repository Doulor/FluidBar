# Sample Minimal plugin

The smallest end-to-end FluidBar v2 plugin. It exists to validate the SDK, the manifest validator, the
loader, the packager and the tests — copy it as a starting point for a new plugin.

## Build, validate, package

```powershell
dotnet build plugins/samples/FluidBar.Plugins.SampleMinimal
dotnet run --project src/FluidBar.Cli -- validate --plugin plugins/samples/FluidBar.Plugins.SampleMinimal/bin/Debug/net10.0
dotnet run --project src/FluidBar.Cli -- pack --plugin plugins/samples/FluidBar.Plugins.SampleMinimal/bin/Release/net10.0 --out artifacts/sample-minimal-1.0.0.fbp
```

## What it demonstrates

- `IFluidBarPlugin` lifecycle (`InitializeAsync` / `StartAsync` / `StopAsync` / `DisposeAsync`).
- Localization through `IPluginContext.Localization` (+ `locales/*.json`).
- A declarative island layout (`contributes.layouts`, template `text.v1`) with no UI code or XAML.
- A lease-owned timer (`IPluginContext.Leases`) that the loader can dispose on unload.
- An event published through `IPluginContext.Islands`.
