# Sample Rich plugin

Reference plugin for the full v2 declarative contribution surface plus a private local dependency.

## What it demonstrates

- `plugin/FluidBar.Plugins.SampleRich.Core.dll` local dependency (isolated inside the plugin ALC).
- Host-owned settings (`IPluginContext.Settings`) with defaults declared by the manifest controls.
- Runtime command handlers bound to manifest-declared command ids (`IPluginContext.Commands`).
- Declarative compact layout + hover card, settings page, context-menu action, accessory widget.
- Localized messages with ICU plural forms (`locales/en-US.json`, `locales/zh-Hans.json`).
- `IPluginContext.RegisterForDisposal` for resources the loader should release even if `StopAsync` is skipped.

## Build, validate, package

```powershell
dotnet build plugins/samples/FluidBar.Plugins.SampleRich
dotnet run --project src/FluidBar.Cli -- validate --plugin plugins/samples/FluidBar.Plugins.SampleRich/bin/Debug/net10.0
dotnet run --project src/FluidBar.Cli -- pack --plugin plugins/samples/FluidBar.Plugins.SampleRich/bin/Release/net10.0 --out artifacts/sample-rich-1.0.0.fbp
```
