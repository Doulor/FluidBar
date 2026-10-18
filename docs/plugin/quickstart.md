# Plugin quickstart (≈15 minutes)

This walks through creating a working FluidBar v2 plugin, running it locally, packaging and opening a PR. The
fastest path is to copy `plugins/samples/FluidBar.Plugins.SampleMinimal` and rename it.

## 1. Prerequisites

- Windows 10/11, .NET SDK 10.0.301+ (`dotnet --list-sdks`)
- FluidBar v2 checkout (`D:\build\GitLocal\FluidBar` here)
- No signing key is needed to develop, validate, package or open a PR.

## 2. Scaffold

```
plugins/<your-id>/                     # lower kebab-case, e.g. now-playing
  FluidBar.Plugins.NowPlaying.csproj
  NowPlayingPlugin.cs
  manifest.json
  locales/en-US.json
  locales/zh-Hans.json
  LICENSE
  README.md
```

Copy `plugins/samples/FluidBar.Plugins.SampleMinimal` and rename the folder/project/assembly/type; keep the
`<id>` folder name equal to the manifest `id`.

The csproj pattern is mandatory:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <EnableDynamicLoading>true</EnableDynamicLoading>        <!-- deps.json + private deps copied -->
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>FluidBar.Plugins.NowPlaying</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\..\src\FluidBar.Sdk\FluidBar.Sdk.csproj" Private="false" />
  </ItemGroup>
  <ItemGroup>
    <None Include="manifest.json" CopyToOutputDirectory="PreserveNewest" />
    <None Include="LICENSE" CopyToOutputDirectory="PreserveNewest" />
    <None Include="README.md" CopyToOutputDirectory="PreserveNewest" />
    <None Include="locales\*.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

Rules: reference **only** `FluidBar.Sdk`; never copy `FluidBar.Sdk.dll` into your output (`Private="false"`);
never reference WPF/Avalonia/`FluidBar.App`/host assemblies; do not publish single-file/trimmed.

## 3. Write the plugin

```csharp
using FluidBar.Sdk;

public sealed class NowPlayingPlugin : IFluidBarPlugin
{
    private IPluginContext? _context;
    private IPluginLease? _lease;

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken ct)
    {
        _context = context;
        return ValueTask.CompletedTask;
    }

    public ValueTask StartAsync(CancellationToken ct)
    {
        var context = _context!;
        _lease = context.Leases.CreateLease("now-playing");
        _lease.SchedulePeriodic(TimeSpan.FromMinutes(1), _ =>
        {
            context.Islands.TryPublish(new IslandEvent
            {
                Source = context.PluginId,
                Title = context.Localization.Get("island.title"),
                Kind = IslandEventKind.Text,
                LayoutId = "now-playing.island",
                Payload = new IslandEventPayload { Subtitle = "Hello" },
            });
            return ValueTask.CompletedTask;
        });
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken ct) { _lease?.Dispose(); _lease = null; return ValueTask.CompletedTask; }
    public ValueTask DisposeAsync() => StopAsync(CancellationToken.None);
}
```

## 4. Manifest (minimum)

Copy the sample manifest, change `id`/`version`/`entry`/`publisher`, keep `schemaVersion: 2`, `apiVersion` on
2.x, `locales` with a complete `en-US` bundle, `capabilities` (disclosure only), `contributes` with your layout
(see [manifest-reference.md](manifest-reference.md) and [declarative-ui.md](declarative-ui.md)).

## 5. Build, validate, run locally

```powershell
dotnet build plugins/<your-id> -c Debug

# same validation CI runs:
dotnet run --project src/FluidBar.Cli -- validate --plugin plugins/<your-id>/bin/Debug/net10.0
dotnet run --project src/FluidBar.Cli -- policy   --plugin plugins/<your-id>/bin/Release/net10.0

# load/unload smoke through the real collectible ALC (fails on unload if you leak a timer):
dotnet run --project src/FluidBar.Cli -- dev --plugin plugins/<your-id>
```

`dev` prints `dev load OK`, then `collected=True`. If it prints `restartRequired=True`, see
[troubleshooting.md](troubleshooting.md) — usually an undisposed timer/thread or a static event.

## 6. Package + local install

```powershell
dotnet run --project src/FluidBar.Cli -- pack --plugin plugins/<your-id>/bin/Release/net10.0 --out artifacts/<id>-1.0.0.fbp
```

The App's Settings → Plugins → *Install from file…* accepts the `.fbp`. Unsigned local installs show an
"Unverified" warning and are allowed only with explicit consent (see the consent sheet). For the real
marketplace the CI signs the package; see [signing-and-keys.md](signing-and-keys.md).

## 7. Open the PR

1. Put the plugin under `plugins/<id>/` in the `fluidbar/plugins` monorepo (this repo uses
   `plugins/samples/` for the samples).
2. Fill the PR checklist from [review-checklist.md](review-checklist.md).
3. CI runs validate/policy/pack/tests; Doulor reviews; merge triggers the publish workflow
   ([publish-runbook.md](publish-runbook.md)).

## Definition of done (short)

- `validate` + `policy` exit 0; built deterministically; no forbidden references; locales complete/parity.
- Smoke test (load → start → stop → unload, ALC collected) passes in the host harness.
- Capabilities are minimal and each risky one has a plain-language `reason`.
- README/LICENSE/icon present; no obfuscation, self-update, secrets or telemetry without disclosure.
- Version strictly greater than the last published version; changelog entry included.
