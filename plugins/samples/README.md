# Sample plugins

Reference plugins for the FluidBar v2 plugin SDK. They are compiled against `FluidBar.Sdk` and packaged as
`.fbp` by `fluidbar-plugin`; they are **not** built into the App (built-in compiled plugins live in
`src/FluidBar.Plugins.Builtin/`).

| Plugin | Demonstrates |
|---|---|
| `FluidBar.Plugins.SampleMinimal` | smallest lifecycle + declarative layout + localization + lease timer |
| `FluidBar.Plugins.SampleRich` | private local dependency, host-owned settings, commands, context menu, widgets, hover card, ICU plurals |
| `FluidBar.Plugins.SampleRich.Core` | private helper library shipped inside the rich sample package |

Build all samples:

```powershell
dotnet build plugins/samples/FluidBar.Plugins.SampleMinimal
dotnet build plugins/samples/FluidBar.Plugins.SampleRich
```

Package a sample:

```powershell
dotnet run --project src/FluidBar.Cli -- pack --plugin plugins/samples/FluidBar.Plugins.SampleMinimal/bin/Release/net10.0 --out artifacts/sample-minimal-1.0.0.fbp
```

Community plugins (not samples) live directly under `plugins/<id>/` in the `fluidbar/plugins` monorepo; the
contribution/review flow is documented in `marketplace/README.md` and `docs/rewrite/contracts/plugin.md`.
