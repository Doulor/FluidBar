# FluidBar v2 — In-proc Plugin Loading (Collectible ALC) Spike Report

**Task:** GO/NO-GO spike for in-proc plugin loading in "FluidBar v2"  
**Date:** 2026-10-18 (Asia/Shanghai)  
**Environment:** Windows 11 (10.0.26200), .NET SDK 10.0.301, .NET runtime 10.0.9, win-x64  
**Spike location:** `%TEMP%\fluidbar-spikes\plugin-alc`  
**Report location:** `%TEMP%\fluidbar-research\06-plugin-alc-spike.md`  
**Real repo `D:\build\GitLocal\FluidBar`:** not modified. All spike code, build outputs, plugin packages and evidence are under `%TEMP%`.

---

## 1. Verdict

### **GO — conditional — for signed, trusted plugins loaded in-proc**

All required mechanics work and are measured:

- Collectible `AssemblyLoadContext` per plugin, with `AssemblyDependencyResolver` and `EnableDynamicLoading`.
- Shared `FluidBar.Sdk` type identity preserved from the default ALC (never a second SDK copy, even when a plugin package illegally ships one).
- Load → Start → Stop → Dispose → `Unload()` → forced GC → **ALC actually collected** in clean cycles.
- Reload of the same plugin id from v1 to v2 after unload, with no type-identity conflict.
- Failure isolation for bad manifests, API mismatch, missing entry, bad entry type, throwing constructor, throwing `Start`, missing private dependency, and forbidden references. The host survives and marks the plugin disabled.
- Two plugins carrying incompatible versions of the same private helper coexist in isolation.
- Self-contained **single-file** host publishes and loads external plugin folders at runtime, with `Assembly.Location == ""` handled correctly.
- Manifest/API/capability gates and a metadata-only forbidden-reference scan that refuses a plugin **before** creating an ALC.

### **NO-GO — for untrusted code or "sandbox" expectations**

An in-proc ALC is a **version/isolation boundary, not a security boundary**. A plugin runs with host privileges, can P/Invoke, throw process-fatal exceptions (`StackOverflowException`, `AccessViolationException`, `Environment.Exit`), or keep the ALC alive with an undisposed timer/thread/static external root. Capabilities are metadata; no code enforcement is claimed or implemented. For untrusted third-party code, a future out-of-process plugin host (named pipes + protocol/SDK DTOs) is required.

### Conditions that must be part of the v2 implementation

1. Shared contract assembly `FluidBar.Sdk` is loaded exactly once from the host; plugins compile against it with `Private=false`; build/package validation rejects any package containing `FluidBar.Sdk.dll`.
2. Every package is versioned under `plugins/<id>/<version>/`, with an atomic `active.json` pointer; **never overwrite a loaded DLL**.
3. Manifest + signature/hash verification + forbidden-reference scan happen **before** ALC creation.
4. `EnableDynamicLoading=true` on all plugin projects; plugin `.deps.json`, private dependencies and native/runtime assets are shipped in the package.
5. No static events in the SDK. Host notifications are instance-scoped through `IPluginContext` and cleared by the loader on disposal; plugins may register resources with `IPluginContext.RegisterForDisposal`.
6. `Stop()` is idempotent, fast and releases timers/threads/subscriptions; `Dispose()` is exception-safe. The loader still applies its own context-disposal safety net, but cannot fix an unregistered timer/thread.
7. Unload is verified with a `WeakReference` to the ALC plus loaded-assembly checks. If not collected after retries, mark the plugin "unload pending/quarantined", retain the old version directory, and retry rather than deleting files.
8. Crash containment for untrusted plugins remains out of scope; document this explicitly in the plugin developer contract.
9. Host remains untrimmed (`PublishTrimmed=false`) for v2; plugins are never published as single-file or trimmed.
10. Avalonia lifecycle/threading: `Start/Stop/Dispose` must be invoked on a defined thread; the spike is UI-free and does not test dispatcher-affine plugin code. Add a dispatcher abstraction or mandate a lifecycle thread in the real host.

---

## 2. Scope and method

The spike implements a minimal end-to-end system entirely in temp:

- **SDK:** `FluidBar.Sdk` (`net10.0`, UI-free) with `IFluidBarPlugin`, `IPluginContext`, `PluginManifest`, strict manifest parser/serializer, `PluginApiVersion`, `PluginCapability`, logger and a deliberately dangerous static event bus used to demonstrate a leak.
- **Plugins:** 10 plugin projects (16 total projects incl. SDK/helpers/stubs/host), producing 22 flat + 2 versioned + 1 lock-probe = 25 staged packages. Includes a simple plugin (v1/v2), a plugin with a local `Shared.Helper` dependency, two conflicting-helper plugins, leaky/fixed/static-cache/timer fixtures, throwing fixtures, a forbidden-reference fixture and invalid-manifest fixtures.
- **Host:** UI-free `net10.0-windows` console app with `PluginLoadContext` (collectible + ADR), `PluginHost` (manifest/API/policy gates, load, cleanup, disable), `LoadedPlugin` (ref-nulling + unload), `PluginContext` (events, disposal registry, `HostStopping`), `PluginDiscovery` (flat + versioned + active pointer), forbidden-reference metadata scanner, diagnostics/evidence collector.
- **Harness:** two host modes:
  - `--mode suite`: 166 checks covering requirements 1–8.
  - `--mode singlefile`: 13 checks run from the published self-contained single-file host.
- **Evidence:** `%TEMP%\fluidbar-spikes\plugin-alc\evidence\suite.json`, `singlefile.json`, `msbuild-properties.txt`.

Commands:

```powershell
cd $env:TEMP\fluidbar-spikes\plugin-alc
dotnet build FluidBar.Spike.slnx -c Release
.\tools\run-suite.ps1              # restage + suite + single-file publish/run
# or individually:
.\tools\stage.ps1
.\host\FluidBar.Host\bin\Release\net10.0-windows\FluidBar.Host.exe --mode suite `
  --plugins artifacts\plugins --versioned artifacts\plugins-versioned --lock artifacts\plugins-lock `
  --evidence evidence\suite.json
```

Result: **166/166 suite checks passed, 0 failed**; **13/13 single-file checks passed, 0 failed**.

---

## 3. Results at a glance

| # | Area | Checks | Result | Key evidence |
|---|---|---:|---|---|
| 1 | Shared SDK identity, ADR, `EnableDynamicLoading` | 39 + 6 | PASS | 3/3 clean cycles collected; 1 SDK copy; ALC name pattern `FluidBar.Plugin:FluidBar.Plugin.Simple:<guid>`; smuggled SDK ignored |
| 2 | Unload correctness, leaks, dispose contract | 19 | PASS | static-event leak reproduced and fixed; timer leak reproduced and not fixable by loader; internal static cache collected |
| 3 | Reload/update and failure isolation | 15 + 28 | PASS | v1 then v2 reload; 7 distinct rejection codes; all failed ALCs collected; host survived |
| 4 | Single-file / self-contained | 13 | PASS | host/SDK `Location == ""`; plugin real path; loads/unloads externally; 73.5 MB win-x64 single-file |
| 5 | Dependency conflicts | 22 | PASS | helper 1.0.0.0 and 2.0.0.0 coexist in separate ALCs/folders; A unload does not affect B |
| 6 | Manifest/capability/API gate | 15 | PASS | round-trip flags; unknown/duplicate/traversal rejected; 2.0 compatible, 2.1/3.0/1.9 not |
| 7 | Layout, active pointer, Windows file locks | 10 | PASS | flat + versioned; v1 active; highest fallback; loaded DLL delete blocked, succeeds after unload |
| 8 | Forbidden references | 9 | PASS | `FluidBar.App` + `Avalonia.Base` references and shipped UI DLLs refused before ALC creation |

### Managed heap evidence (bytes; `GC.GetTotalMemory(forceFullCollection: true)`)

| Point | Heap | Delta vs baseline | Collected? |
|---|---:|---:|---|
| Baseline before first load | 361,896 | — | — |
| Cycle 1 after unload + GC | 363,832 | +1,936 | yes (ALC + assembly) |
| Cycle 2 after unload + GC | 370,656 | +8,760 | yes |
| Cycle 3 after unload + GC | 377,704 | +15,808 | yes |

The deltas are cumulative test-harness allocations (log strings, evidence records), not a per-cycle ALC leak: `AssemblyLoadContext.All` reports 0 collectible ALCs and 0 plugin assemblies after each cycle. The authoritative unload checks are the `WeakReference` and `AppDomain.CurrentDomain.GetAssemblies()` counts, not the heap delta alone.

Leak measurements (cumulative across the leak scenario):

| Point | Heap | Delta vs leak baseline | ALC state |
|---|---:|---:|---|
| Before leak cycles | 447,664 | — | — |
| Static event leak after unload + 12 GCs | 461,032 | +13,368 | **not collected** (`SubscriberCount=1`) |
| After `ClearAllSubscribers()` + GC | 453,168 | +5,504 | **collected** |
| Fixed plugin (context event + registered timer) after unload | 475,816 | +28,048 | **collected** |
| Timer leak after unload + 12 GCs | 497,560 | +49,896 | **not collected** |

Again, absolute heap numbers include accumulated strings/logs. Causality is established by weak-reference state plus loaded-assembly count, not by the size of the delta.

---

## 4. Requirement 1 — Collectible ALC + ADR + `EnableDynamicLoading` + shared type identity

### 4.1 Actual load context (spike code)

`host/FluidBar.Host/PluginLoadContext.cs`:

```csharp
using System.Reflection;
using System.Runtime.Loader;

public sealed class PluginLoadContext : AssemblyLoadContext
{
    private static readonly HashSet<string> SharedAssemblyNames =
        new(StringComparer.OrdinalIgnoreCase) { "FluidBar.Sdk" };

    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string entryAssemblyPath)
        : base($"FluidBar.Plugin:{Path.GetFileNameWithoutExtension(entryAssemblyPath)}:{Guid.NewGuid():N}",
               isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
        PluginDirectory = Path.GetDirectoryName(entryAssemblyPath)!;
    }

    public string PluginDirectory { get; }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Shared contract MUST be checked before ADR: the host owns the type identity.
        if (assemblyName.Name is not null && SharedAssemblyNames.Contains(assemblyName.Name))
            return null; // fall back to the default ALC

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}
```

Why this ordering matters: if `AssemblyDependencyResolver` is consulted first and a plugin package contains its own `FluidBar.Sdk.dll`, ADR returns the plugin-local path. The plugin then implements a *different* `IFluidBarPlugin`, `typeof(IFluidBarPlugin).IsAssignableFrom(type)` is false, and every plugin fails. The spike proves the guard works: `sample.sdk-duplicate` deliberately ships `FluidBar.Sdk.dll`; the plugin still loads, `SdkCopyCount == 1`, and the interface assembly location is the host binary, not the plugin folder.

### 4.2 SDK csproj / plugin csproj pattern

SDK:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>FluidBar.Sdk</AssemblyName>
    <Version>2.0.0</Version>
    <AssemblyVersion>2.0.0.0</AssemblyVersion>
  </PropertyGroup>
</Project>
```

Plugin:

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <EnableDynamicLoading>true</EnableDynamicLoading>
  <AssemblyName>Vendor.Plugin</AssemblyName>
  <Version>1.2.3</Version>
</PropertyGroup>
<ItemGroup>
  <!-- Compile against the shared contract; never copy it into the package. -->
  <ProjectReference Include="..\..\FluidBar.Sdk\FluidBar.Sdk.csproj" Private="false" />
</ItemGroup>
```

### 4.3 Measured identity results

Clean cycle (`sample.simple` v1), repeated three times:

- `typeof(IFluidBarPlugin).IsAssignableFrom(pluginType)` = **True**.
- Plugin interface assembly location equals host `FluidBar.Sdk.dll` location.
- Interface ALC = **Default**.
- `AppDomain.CurrentDomain.GetAssemblies()` count for `FluidBar.Sdk` = **1** before, during and after all cycles.
- Plugin assembly location is inside `artifacts/plugins/sample.simple`.
- `AssemblyLoadContext.GetLoadContext(pluginAssembly)` = the plugin ALC, `IsCollectible = true`.
- Plugin references SDK **2.0.0.0** (assembly version), host SDK is **2.0.0.0**.
- WeakReference to the ALC and to the entry `Assembly`: both **not alive** after unload + GC.
- Plugin assembly count after unload: **0**.
- `AssemblyLoadContext.All` collectible entries after clean cycles: **0**.

### 4.4 `EnableDynamicLoading` evidence

MSBuild property inspection (`evidence/msbuild-properties.txt`):

| Project | `EnableDynamicLoading` | `GenerateDependencyFile` | `CopyLocalLockFileAssemblies` | `OutputType` |
|---|---|---|---|---|
| `Dependent` | true | true | **true** | Library |
| `Dependent.NoDeps` | false | true (still generated in .NET 10) | **false** | Library |

The flag's runtime-relevant effect is `CopyLocalLockFileAssemblies=true`, which copies NuGet/runtime assets into the plugin output. Project-reference dependencies were copied in both builds. Keep `EnableDynamicLoading=true` in production for package dependencies; do not rely on the .NET 10 default deps.json generation.

**Important measured nuance:** the loader uses `AssemblyDependencyResolver`, but ADR also resolved an *adjacent* `Shared.Helper.dll` after the plugin's `.deps.json` was deliberately deleted:

```
ADR resolve Shared.Helper with deps.json    = resolved (sample.dependent\Shared.Helper.dll)
ADR resolve Shared.Helper without deps.json = resolved (sample.dependent-nodeps\Shared.Helper.dll)
```

That no-deps package still loaded and unloaded cleanly. So absence of `deps.json` was not a load-failure vector for simple adjacent project-reference dependencies in this configuration. Do not treat that as permission to omit it: package/NuGet runtime assets, RID-specific natives and satellite resources depend on the deps.json + copy-local behavior. The production build must verify the package contents, not assume the directory probe will save a bad package.

---

## 5. Requirement 2 — Unload correctness, leaks and the loader dispose contract

### 5.1 Strong-reference rules that made unload work

The host keeps only these per installed plugin after unload:

- manifest, id, version, folder paths (strings),
- `WeakReference` to the ALC and to the entry `Assembly`,
- status strings (`enabled`, `disabled:<code>`, `unload-pending`).

The host never caches `Type`, `MethodInfo`, `Assembly`, plugin instances, exception objects or plugin delegates. Plugin logging immediately formats `Exception` to a string (`GetType().FullName + Message`); retaining the exception object can root the plugin ALC through its stack trace / `TargetSite`.

Plugin use is contained in `[MethodImpl(MethodImplOptions.NoInlining)]` methods. Forced GC and weak-reference verification happen in a **different stack frame** after the load/use/unload method returns; otherwise JIT local lifetimes can extend the ALC's life and produce a false "not collected" result.

### 5.2 Leak #1 — static event handler (deliberate, reproduced, fixed)

Fixture `sample.leaky-static`:

```csharp
public void Start(IPluginContext context)
{
    _context = context;
    HostEvents.SettingsChanged += OnSettingsChanged; // BUG: no unsubscribe
}
// Stop/Dispose do not remove it.
```

`HostEvents` lives in `FluidBar.Sdk`, loaded in the **default** ALC. Its static event roots the plugin delegate → plugin instance → plugin ALC. Measured after `Unload()` and 12 forced GC cycles:

- `HostEvents.SubscriberCount` = **1**.
- ALC WeakReference = **alive**.
- `FluidBar.Plugin.Leaky` assembly count = **1**.
- Managed heap: **461,032** bytes vs **447,664** baseline (+13,368).

Remedy in the spike: `HostEvents.ClearAllSubscribers()` returned **1**; after one forced GC:

- ALC WeakReference = **not alive** (collected).
- Plugin assembly count = **0**.
- Heap back to **453,168** (+5,504 vs baseline).

The production fix is not an emergency clear API. It is: **no static events in the SDK**. Use `IPluginContext.SettingsChanged`, whose subscriptions are cleared when `PluginContext.Dispose()` runs during unload. The spike's fixed plugin proves that contract with empty `Stop()`/`Dispose()` methods:

```csharp
public void Start(IPluginContext context)
{
    _timer = new Timer(_ => Interlocked.Increment(ref _seen), null, 50, 50);
    context.RegisterForDisposal(_timer);       // loader disposes it
    context.SettingsChanged += OnSettingsChanged; // loader clears it
}
```

Measured: collected, plugin assembly count 0, logs contain `PluginContext disposed; events cleared and registered resources disposed.` No static subscriber leaked (`SubscriberCount=0`).

### 5.3 Leak #2 — undisposed `System.Threading.Timer` (loader cannot fix)

Fixture `sample.leaky-timer` creates a `Timer` and never disposes it. The timer queue is a CLR root, so the callback delegate roots the plugin instance and ALC. Measured after unload + GC:

- ALC WeakReference **alive**.
- Plugin assembly count **1**.
- No host-side remedy exists: the host does not own the timer and `Stop()` was already called. Heap grew to 497,560 (+49,896 cumulative).

Only the plugin can fix this by disposing the timer in `Stop()` or registering it with `RegisterForDisposal`. If a plugin cannot be trusted to do that, the only robust isolation is a separate process.

### 5.4 Static cache — not a leak by itself (measured)

Fixture `sample.static-cache` keeps itself in a `static readonly List<object>` inside the plugin assembly. Because that static field is owned by the collectible ALC, it is part of the ALC's internal object graph; with no external root, it must still be collectible. Measured: ALC **collected**, plugin assembly **0**. So "plugin has static caches" is not automatically a loader leak — a **host** static event/cache/delegate is.

### 5.5 Loader dispose contract (recommended and implemented)

| Step | Action | Failure handling | Reason |
|---|---|---|---|
| 1 | `plugin.Stop()` | catch, log, continue | release timers/threads/subscriptions; must be idempotent |
| 2 | `plugin.Dispose()` | catch, log, continue | final plugin cleanup |
| 3 | `IPluginContext.Dispose()` | clear `SettingsChanged`; dispose registered resources LIFO; cancel `HostStopping`; catch/log each | safety net for forgotten resources; context-scoped events cannot leak |
| 4 | null all strong host refs (`_plugin`, `_context`, `_assembly`, `_alc`) | — | break host roots |
| 5 | `alc.Unload()` | catch, log | request unload |
| 6 | forced GC in a separate frame | retry 10–12 cycles, `WaitForPendingFinalizers` | actual collection is asynchronous |
| 7 | verify `WeakReference` + loaded assemblies | if still alive → quarantine / retry / keep old dir | never delete files still mapped |

Order matters: context disposal runs **after** the plugin's Stop/Dispose so the plugin can still use its logger and context during cleanup; then the loader removes everything the plugin registered. The host must not `Stop()` a plugin whose `Start()` never completed; on `Start` failure it calls `Dispose()` only, then context dispose and unload.

---

## 6. Requirement 3 — Reload/update and failure isolation

### 6.1 Reload v1 → v2

Layout: `plugins-versioned/sample.simple/1.0.0/` and `2.0.0/`, `active.json` pointing at 1.0.0. Both versions use the same assembly name `FluidBar.Plugin.Simple`, same namespace and entry type, but versions `1.0.0.0` and `2.0.0.0`.

Measured sequence:

1. Load v1 in ALC `FluidBar.Plugin:FluidBar.Plugin.Simple:<guid1>`; greeting `simple:1.0.0`; assembly version `1.0.0.0`.
2. Stop/Dispose/Unload + GC: v1 ALC **collected**, plugin assembly count **0**.
3. Load v2 in a **new** ALC `<guid2>`; greeting `simple:2.0.0`; assembly version `2.0.0.0`.
4. Interface assembly is still exactly the host SDK, 1 SDK copy, same host SDK location; no type-identity conflict.
5. Unload v2: collected, assembly count 0, SDK count 1.

Both ALCs exist one at a time in this test; ALC names are unique per load. Loading v1 and v2 simultaneously is technically possible in two ALCs, but the host should enforce one active version per plugin id at the application level.

### 6.2 Failure isolation matrix

All cases below logged an error, marked the plugin state `disabled:<code>`, left the host alive, and unloaded the partial ALC where one was created.

| Fixture | Failure point | Code | Failed ALC collected |
|---|---|---|---|
| malformed `plugin.json` | discovery | `manifest.malformed_json` (discovery issue) | no ALC created |
| manifest id ≠ folder | discovery | `discovery.id_mismatch` | no ALC created |
| `apiVersion: 3.0` | loader gate | `api.incompatible` | no ALC created |
| `minimumHostVersion: 9.0.0` | loader gate | `host.too_old` | no ALC created |
| missing entry DLL | loader preflight | `plugin.entry_missing` | no ALC created |
| entry type missing | type resolution | `plugin.entry_type_missing` | yes |
| entry type not implementing `IFluidBarPlugin` | type validation | `plugin.entry_type_invalid` | yes |
| constructor throws | activation | `plugin.activation_failed` | yes |
| `Start` throws | start | `plugin.start_failed` | yes |
| missing private dependency at `Start` | start | `plugin.start_failed` | yes |
| forbidden reference | pre-load scan | `policy.forbidden_reference` | no ALC created |
| smuggled UI DLL | pre-load scan | `policy.forbidden_reference` | no ALC created |

For `Start` failure, logs showed the exception text and `Dispose()` was called (`start-throws plugin Dispose called after failed Start.`). After all failures, loading `sample.simple` still succeeded and unloaded, and `AssemblyLoadContext.All` collectible count returned to 0.

**Discovery-time vs load-time:** malformed JSON/id mismatch never reach the loader (no ALC). API/host-version gates and path checks happen before ALC creation. Type/activation/start failures create an ALC but the loader cleans it up in the same call, then GC collects it.

---

## 7. Requirement 4 — Single-file / self-contained implications

Published with:

```powershell
dotnet publish host\FluidBar.Host\FluidBar.Host.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None -p:DebugSymbols=false -o artifacts\publish\singlefile
```

Result: `FluidBar.Host.exe` = **73,559,193 bytes** (73.5 MB), self-contained win-x64 single-file, no separate DLLs in the publish folder. Single-file mode: **13/13 checks passed**.

Measured:

| Check | Result |
|---|---|
| `typeof(Program).Assembly.Location` | `""` |
| `typeof(IFluidBarPlugin).Assembly.Location` (bundled SDK) | `""` |
| `AppContext.BaseDirectory` | publish folder (`...\artifacts\publish\singlefile\`) |
| `Environment.ProcessPath` | `...\artifacts\publish\singlefile\FluidBar.Host.exe` |
| plugin entry assembly `Location` | real path under `artifacts\plugins\sample.dependent\...` |
| SDK copies | 1, host identity true, `Location` empty (bundled) |
| local `Shared.Helper` | loaded from plugin folder (`1.0.0.0`) |
| plugin unload | ALC collected, plugin assembly count 0 |
| forbidden-reference scan | still refuses `sample.forbidden` |

Patterns for the real app:

```csharp
// Host-relative paths: never use Assembly.Location in a single-file/self-contained host.
var hostDirectory = AppContext.BaseDirectory;

// Plugin-relative paths: derive from the package directory, not from the host directory.
var pluginDirectory = Path.GetDirectoryName(descriptor.EntryAssemblyPath)!;
var dataDirectory   = Path.Combine(pluginDataRoot, descriptor.Id);
```

Constraints:

- Plugins must remain **normal folder deployments** (`plugin.json`, entry DLL, deps.json, private deps). A plugin published as single-file/trimmed has no usable `Assembly.Location`/`deps.json` layout for ADR; do not support it in v2.
- Keep `PublishTrimmed=false` for the host in v2. Plugin loading is reflection-heavy; trimming would need explicit roots/metadata and is not worth the risk.
- Single-file bundling of the host/SDK is fine; the bundled SDK is resolved by the default ALC and the plugin's ADR is bypassed for `FluidBar.Sdk` by design.
- `IncludeNativeLibrariesForSelfExtract` extracts native runtime libraries but leaves `AppContext.BaseDirectory` as the publish directory; plugin native dependencies still come from the plugin package via `ResolveUnmanagedDllToPath`.

---

## 8. Requirement 5 — Dependency conflicts

Two plugins carry different builds of the same assembly identity `Shared.Helper`:

- `sample.conflict-a`: `Shared.Helper` **1.0.0.0**, under `sample.conflict-a/`.
- `sample.conflict-b`: `Shared.Helper` **2.0.0.0**, under `sample.conflict-b/`.

Both loaded simultaneously:

- Plugin A diagnostics: `1.0.0.0|FluidBar.Plugin:FluidBar.Plugin.ConflictA:<guid>|...\sample.conflict-a\Shared.Helper.dll`
- Plugin B diagnostics: `2.0.0.0|FluidBar.Plugin:FluidBar.Plugin.ConflictB:<guid>|...\sample.conflict-b\Shared.Helper.dll`
- `AppDomain` count for `Shared.Helper` = **2** concurrently; SDK count = **1**.
- Unloading A while B stays active: A's ALC collected, `Shared.Helper` count drops to 1, B still returns 2.0.0.0 and its helper path.
- Loading `sample.broken-dep` (DLL copied without `Shared.Helper.dll`) fails isolated with `plugin.start_failed`; B continues to work; broken ALC collected.
- After B unload: `Shared.Helper` count 0, collectible ALC count 0.

Caveats:

- Types from a private dependency cannot cross the plugin boundary. If the SDK exposes a type from a dependency (e.g. a JSON library), that dependency becomes part of the shared contract and must be resolved from the default ALC, not per plugin. Keep the SDK contract dependency-free (as in this spike).
- Native DLLs are a weaker story: `LoadUnmanagedDllFromPath`/OS loader behavior may leave native modules loaded even after ALC unload. Avoid native dependencies in plugins for v2, or plan process isolation for them.

---

## 9. Requirement 6 — Permission/capability metadata and API gate (no enforcement)

### 9.1 Manifest contract used by the spike

```json
{
  "id": "sample.simple",
  "name": "Simple Sample",
  "version": "1.0.0",
  "apiVersion": "2.0",
  "entryAssembly": "FluidBar.Plugin.Simple.dll",
  "entryType": "FluidBar.Plugin.Simple.SimplePlugin",
  "capabilities": ["SettingsRead", "HostEvents"],
  "description": "...",
  "author": "..."
}
```

Strict parser rules (fail closed, all measured):

- malformed JSON → `manifest.malformed_json`;
- non-object root, duplicate top-level properties, missing/blank required fields, wrong JSON types → reject;
- id: 1–64 chars `[a-z0-9._-]`, starts alphanumeric, no `..`, no reserved Windows device names; path traversal `../evil` rejected;
- version: exactly three numeric parts (`1.2.3`); folder version must match manifest version exactly;
- apiVersion: exactly `<major>.<minor>`; `2.1` rejected by a 2.0 host; `3.0`, `1.9` rejected; `2.0` accepted;
- `entryAssembly`: bare `.dll` file name, no separators, must stay inside the plugin folder;
- `entryType`: unqualified type name, no comma/assembly qualification;
- capabilities: array of strings, exact defined enum names (case-insensitive), no numeric values, no combined `"A, B"` strings, no `None`, duplicates rejected, unknown capability rejected (an unrecognised permission must never be silently granted);
- unknown top-level properties: ignored with a warning (forward compatibility);
- `minimumHostVersion` optional; host compares against 2.0.0.

API compatibility rule:

```csharp
public bool IsCompatibleWith(PluginApiVersion host) =>
    Major == host.Major && Minor <= host.Minor;
```

Plugin 2.0 ↔ host 2.0: compatible. Plugin 2.1 → host 2.0: incompatible. Plugin 3.0 or 1.9: incompatible. Major = breaking; host minor >= plugin minor.

### 9.2 Capability round-trip

`PluginCapability` is a `[Flags]` enum: `None, SettingsRead, SettingsWrite, FileSystem, Network, BackgroundWork, Clipboard, Notifications, HostEvents`.

Measured:

- Parse manifest `["SettingsRead","Network","BackgroundWork"]` → three flags.
- Serialize canonically → parse again → identical flag set; canonical bytes stable (suitable for signing/hashing).
- Strict parser accepts `Network` and `network`; rejects `"5"`, `"Network, FileSystem"`, `"None"`, `"SuperUser"`.
- Unknown capability → error `capability.unknown`; duplicate → `capability.duplicate`.
- Plugin context receives declared capabilities; `HasCapability(PluginCapability.Network)` returned **false** for a plugin that declared only `SettingsRead,HostEvents`, while the plugin still ran normally. This is the explicit "metadata, not enforcement" demonstration: the host does not block filesystem/network/process access based on capabilities.
- Loader gates `apiVersion` and `minimumHostVersion` before creating the ALC.

**No code enforcement claim.** Capability metadata is for install-time consent/UI, diagnostics and future policy checks. It is not a sandbox. A malicious plugin can ignore the declaration entirely.

---

## 10. Requirement 7 — Folder layout, manifest contract and Windows update/rollback

### 10.1 Measured Windows file-lock behavior

While a plugin DLL is loaded and its ALC is alive:

- `File.Delete(pluginDll)` → **`UnauthorizedAccessException`** ("Access to the path ... is denied").
- `Directory.Delete(pluginFolder, recursive: true)` → **`UnauthorizedAccessException`** (denied).
- After `Stop()` + `Dispose()` + `Unload()` + forced GC:
  - ALC collected; `File.Delete(pluginDll)` → **succeeded** (file gone).

So in-place replacement of a loaded plugin is impossible on Windows, regardless of how the update is packaged. Versioned directories + an atomic pointer are mandatory, not stylistic.

### 10.2 Recommended layout

```
%APPDATA%\FluidBar\
  plugins\
    <id>\
      active.json                     # {"activeVersion":"1.2.3"}  (atomic pointer)
      1.2.3\
        plugin.json
        Vendor.Plugin.dll
        Vendor.Plugin.deps.json
        Vendor.Plugin.runtimeconfig.json
        <private dependency>.dll
        runtimes\win-x64\native\...   # if native assets are unavoidable
      1.3.0\
        ...
  plugin-data\<id>\                   # version-independent writable data (preserve across updates)
  plugin-cache\<id>\<version>-<hash>\ # optional shadow copy when install source is read-only
```

Why `<id>/<version>/` beats `<id>/`:

- Windows locks loaded DLLs; a new version must go to a new directory while the old one is still loaded.
- Rollback is a pointer change, not a reinstall.
- Multiple versions can be validated/staged before cutover.
- File hashes/manifests per version are immutable after staging.
- Old version deletion is deferred until unload is verified.

Flat `<id>/` is supported by the spike for simple installs and small plugins, but it cannot support safe in-place update. Use versioned layout for v2.

### 10.3 Discovery and selection

- Scan `plugins/<id>/`:
  - if `plugin.json` exists directly → flat package;
  - else scan child version directories, each with `plugin.json`, and require folder name == manifest version.
- Require manifest id == `<id>` folder.
- Select `active.json` version if present; otherwise highest installed version.
- Dangling pointer → warning `layout.active_version_missing` and fall back to highest (fail-safe for a corrupted pointer, not for security policy).
- Reject symlinks/reparse points or paths that escape the plugin root; resolve and verify full paths (the spike checks entry path containment).

### 10.4 Update sequence (Windows-safe)

1. Download/verify package in `%TEMP%\FluidBar\staging\<id>-<version>-<guid>`.
2. Verify signature/hashes, parse manifest, check id/version/folder, API gate, forbidden refs, dependency closure.
3. Create `plugins\<id>\<newVersion>.staging-<guid>` on the same volume; copy package contents; verify staged hashes.
4. Atomically rename staging dir to `plugins\<id>\<newVersion>` (`Directory.Move` within the same volume).
5. Atomically switch `active.json`:

```csharp
var tmp = activePath + ".tmp";
File.WriteAllText(tmp, JsonSerializer.Serialize(new { activeVersion = newVersion }));
File.Move(tmp, activePath, overwrite: true); // same volume; replace semantics on Windows
```

6. Stop/Dispose old plugin, dispose context, unload old ALC, force GC, verify collection.
7. Load new version in a new ALC; call Start.
8. If new Start fails: switch pointer back to the old version, unload the failed new ALC, reload the old version, mark new version disabled.
9. After successful cutover and verified unload, delete old version directories (retry on file locks).

Recommended cutover order for minimal downtime: stage/verify new → switch pointer → load new → stop/unload old. The host must never have two *active* versions of the same plugin id in its registry, even though two ALCs can temporarily coexist.

### 10.5 Rollback

- Keep at least the previous version directory (N-1), optionally N-2.
- `active.json` points at the rollback version; reload the old package after unloading the bad one.
- Data lives in `plugin-data/<id>/`, independent of version. If schema changes, plugin must migrate/backup; the host should expose a data-version convention.
- A failed new version should remain installed but disabled/quarantined for diagnostics; pointer goes back to the working version.

### 10.6 Shadow copy

Use shadow copy when the package source cannot be kept stable (Program Files/MSIX/read-only share, antivirus, or corporate deployment):

1. Verify signature/hash of the source package.
2. Copy the **entire** package to `%LOCALAPPDATA%\FluidBar\plugin-cache\<id>\<version>-<sha256>\`.
3. Verify hashes of the copy.
4. Load from the shadow path; `active.json` still records the logical version.
5. Update writes a new shadow directory; old shadow directory can only be deleted after unload verification.

Do **not** use `Assembly.Load(byte[])`/stream loading as a lock workaround: it loses `Assembly.Location`, deps.json/ADR behavior, satellite resources and native probing, and makes diagnostics much harder. Shadow-copy + versioned directories is the supported Windows pattern.

---

## 11. Requirement 8 — Forbidden references policy

### 11.1 Policy

```
exact:  FluidBar.App, FluidBar.Host,
        PresentationFramework, PresentationCore, WindowsBase, System.Windows.Forms
prefix: Avalonia, Microsoft.Web.WebView2
```

The policy is applied to:

- every `*.dll` in the package, recursively,
- the assembly definition name (a shipped UI assembly is a violation even if unreferenced),
- the file stem (catches native/renamed DLLs whose metadata is unavailable or misleading),
- every `AssemblyReference` row.

Measured on the forbidden fixture:

```
Avalonia.Base.dll:assembly-file-present:Avalonia.Base
Avalonia.Base.dll:assembly-present:Avalonia.Base
FluidBar.App.dll:assembly-file-present:FluidBar.App
FluidBar.App.dll:assembly-present:FluidBar.App
FluidBar.Plugin.Forbidden.dll:assembly-reference:FluidBar.App
FluidBar.Plugin.Forbidden.dll:assembly-reference:Avalonia.Base
```

The loader then returned `policy.forbidden_reference` **before creating an ALC** (`Failure.AlcWeak == null`, `AssemblyLoadContext.All` count unchanged, plugin assembly never appeared in `AppDomain`). A clean plugin with a local dependency passed the scan; a plugin package containing an unreferenced `Avalonia.Base.dll` was refused (`sample.smuggle`); legitimate plugins still loaded after refusals.

### 11.2 Scanner core

```csharp
foreach (var dll in Directory.EnumerateFiles(folder, "*.dll", SearchOption.AllDirectories))
{
    if (IsForbidden(Path.GetFileNameWithoutExtension(dll)))
        AddViolation(dll, "assembly-file-present", ...);

    using var pe = new PEReader(File.OpenRead(dll));
    if (!pe.HasMetadata) continue;                 // native library: file-name policy only
    var md = pe.GetMetadataReader();
    if (!md.IsAssembly) continue;

    var defName = md.GetString(md.GetAssemblyDefinition().Name);
    if (IsForbidden(defName)) AddViolation(dll, "assembly-present", defName);

    foreach (var handle in md.AssemblyReferences)
    {
        var name = md.GetString(md.GetAssemblyReference(handle).Name);
        if (IsForbidden(name)) AddViolation(dll, "assembly-reference", name);
    }
}
```

Policy decisions:

- Scan **before** ALC creation and before any plugin code runs.
- Fail closed if a managed DLL cannot be read/parsed (`policy.scan_failed`); do not load a partly scanned package.
- Native DLLs need a separate allowlist/hash policy (metadata scan cannot prove anything about them).
- The scan is not malware detection. A malicious plugin can P/Invoke/`Assembly.Load` at runtime; the policy prevents accidental UI/app coupling and reduces the contract surface, not a determined attacker.

**Pitfall found during the spike:** the first stub used `public const string Name`; the C# compiler inlined the constant and the plugin had **no `AssemblyRef`** to `FluidBar.App`/`Avalonia.Base`. Only the shipped DLL files were detected. Changing the stubs to properties produced real `AssemblyReference` rows. Do not rely on references alone: always combine assembly-reference scanning with shipped-file scanning.

---

## 12. Recommended production design

### 12.1 Load/unload class (~60 lines, distilled from the spike)

```csharp
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using FluidBar.Sdk;

public sealed class PluginSession
{
    private PluginLoadContext? _alc;
    private Assembly? _assembly;
    private IFluidBarPlugin? _plugin;
    private PluginContext? _context;

    public static PluginSession Load(PluginDescriptor d, string dataRoot)
    {
        var alc = new PluginLoadContext(d.EntryAssemblyPath); // shared SDK short-circuit is inside
        try
        {
            var assembly = alc.LoadFromAssemblyPath(d.EntryAssemblyPath);
            var type = assembly.GetType(d.Manifest.EntryType, throwOnError: false)
                ?? throw new PluginLoadException("plugin.entry_type_missing");
            if (!typeof(IFluidBarPlugin).IsAssignableFrom(type))
                throw new PluginLoadException("plugin.entry_type_invalid");

            var plugin = (IFluidBarPlugin)Activator.CreateInstance(type)!;
            var context = new PluginContext(d, dataRoot);
            try { plugin.Start(context); }
            catch { plugin.Dispose(); context.Dispose(); alc.Unload(); throw; }

            return new PluginSession
            {
                _alc = alc, _assembly = assembly, _plugin = plugin, _context = context
            };
        }
        catch
        {
            try { alc.Unload(); } catch { /* log */ }
            throw;
        }
    }

    public string InvokeString(string methodName) // test/diagnostic only; do not cache MethodInfo
    {
        var method = _plugin!.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MissingMethodException(methodName);
        return method.Invoke(_plugin, null)?.ToString() ?? "";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Unload()
    {
        try { _plugin?.Stop(); } catch (Exception ex) { Log(ex); }
        try { _plugin?.Dispose(); } catch (Exception ex) { Log(ex); }
        try { _context?.Dispose(); } catch (Exception ex) { Log(ex); } // clears events + registrations

        _plugin = null;
        _context = null;
        _assembly = null;

        var alc = _alc;
        _alc = null;
        alc?.Unload(); // caller force-GCs in another frame and verifies a WeakReference
    }
}
```

The real loader must add: manifest/API/policy gates before this class, `WeakReference` capture, status/quarantine state machine, structured error codes, and non-cached diagnostics.

### 12.2 Manifest contract recommendation

```json
{
  "schemaVersion": 1,
  "id": "vendor.plugin-id",
  "name": "Human readable name",
  "version": "1.2.3",
  "apiVersion": "2.0",
  "minimumHostVersion": "2.1.0",
  "entryAssembly": "Vendor.Plugin.dll",
  "entryType": "Vendor.Plugin.EntryPoint",
  "capabilities": ["SettingsRead", "Network"],
  "publisher": "Vendor Ltd",
  "description": "Short description"
}
```

Recommended additions for production (not implemented as crypto in this spike):

- `schemaVersion` for forward-compatible manifest evolution.
- `publisher` and package signature (`plugin.sig` or Authenticode on DLLs) verified before load.
- `files` hash list (or package hash) covering every DLL/native asset; loader verifies before staging/copying.
- `minimumHostVersion` enforced by the host.
- `capabilities` only; no ad-hoc permission strings outside the enum.
- Unknown *top-level* fields ignored with warning; unknown **capabilities** rejected.
- Canonical JSON bytes for signature/hash: deterministic property order and sorted capability tokens (the spike's serializer demonstrates stable output).

A signing chain recommendation:

1. Sign the plugin package (Authenticode or detached signature over canonical `plugin.json` + file hashes).
2. Store trusted publisher public keys/cert thumbprints in the host.
3. Verify signature + hashes in the staging directory before `Directory.Move` into `plugins/<id>/<version>/`.
4. Re-verify manifest/hashes at load time (defense in depth) or trust an install-time marker with ACL protection.
5. Never load from `%TEMP%` or a user-writable staging path without verification.

### 12.3 Loader state machine

```
Discovered
  → Rejected (manifest/id/version/API/policy/signature/hash)
  → Validated
  → Loading   (create ALC, load entry, validate type, activate, start)
  → Active
  → Stopping  (Stop, Dispose, context dispose)
  → Unloading (Unload + GC verification)
  → Unloaded  (files may be deleted)
       └─ if verification fails: Quarantined / UnloadPending, retry; keep files
```

Host registry entries after unload contain only strings, timestamps, status, weak refs and error codes. Never keep plugin `Type`/`Assembly`/delegate/exception objects in the registry, a static cache, a `ConcurrentDictionary` value, or a log sink.

### 12.4 Lifecycle and threading

- Define a lifecycle thread for `Start/Stop/Dispose` (Avalonia UI thread if plugin APIs can touch UI, otherwise a dedicated host lifecycle thread with clear rules).
- `Start` must be fast; long work belongs on plugin-owned tasks/threads registered for disposal.
- In-proc `Stop()` cannot be safely aborted or timed out (no thread abort in .NET Core). A hung `Stop()` hangs unload. Document this; for untrusted plugins, use out-of-proc.
- `HostStopping` token is cancelled during context disposal; plugin async work should observe it.
- Unload GC/verification can run off the UI thread, but plugin `Stop/Dispose` may need UI affinity; measure in the real Avalonia host.

### 12.5 Build/package CI checks

- Build host and plugins with the same SDK/API contract package.
- Plugin projects: `EnableDynamicLoading=true`, `Private=false` on `FluidBar.Sdk`.
- Package inspection must fail if:
  - `FluidBar.Sdk.dll` is present,
  - `plugin.json` missing/invalid/id mismatch/version mismatch,
  - forbidden assembly names/files/references present,
  - `.deps.json` missing,
  - private dependency listed in deps.json but not present,
  - plugin is published single-file/trimmed.
- Publish host with `PublishSingleFile` optionally, but `PublishTrimmed=false`.
- Run a smoke test that loads each package, starts/stops, unloads and verifies the ALC WeakReference is dead.

---

## 13. Pitfalls and gotchas found

1. **Shared-contract check must precede ADR.** Otherwise a smuggled `FluidBar.Sdk.dll` silently creates a second type universe and all plugins fail `IsAssignableFrom`.
2. **`AssemblyLoadContext.All` is not proof of collection.** It removes the ALC from enumeration as soon as `Unload()` is requested, even while the object is still alive because a static event roots it. Measured: leaked ALC weak ref alive, `AssemblyLoadContext.All` collectible count 0. Use `WeakReference` + `AppDomain.GetAssemblies()`.
3. **`Assembly.Location` is empty for bundled host/SDK.** Use `AppContext.BaseDirectory` for host paths and the plugin package folder for plugin paths. Do not use `Assembly.Location` to find plugins.
4. **Windows locks loaded DLLs.** `UnauthorizedAccessException` on delete/replace while loaded; deletion only works after unload + collection. Versioned dirs + atomic pointer.
5. **Static events in the SDK are a leak factory.** Any plugin that forgets to unsubscribe pins its ALC forever. Prefer context-scoped events and loader-cleared subscriptions. An emergency "clear all" API also unsubscribes healthy plugins; do not use it in normal operation.
6. **Undisposed timers/threads cannot be fixed by the loader.** Only the plugin or process isolation can release them. `RegisterForDisposal` helps only if the plugin registers the resource.
7. **Plugin-internal static caches are not automatically leaks.** The whole ALC graph is collectible; host-owned statics are the danger.
8. **Never retain plugin `Exception` objects.** Log `ex.ToString()`/type+message immediately; retain only strings.
9. **JIT local lifetimes can fake a leak.** Unload and GC verification must happen in a separate `[MethodImpl(NoInlining)]` frame; force several GC cycles and `WaitForPendingFinalizers`.
10. **`deps.json` absence is not always fatal.** ADR resolved adjacent project-reference dependencies even without it in .NET 10. Do not use that as justification to skip `EnableDynamicLoading`/package validation; NuGet/RID assets need the deps.json + copy-local behavior.
11. **Forbidden-reference scan cannot rely on `AssemblyRef` alone.** `const` values are inlined and leave no reference; scan shipped files and file names as well. Native DLLs need an allowlist/hash.
12. **Capabilities are not security.** There is no enforcement; a plugin can ignore the manifest. Do not market capability metadata as sandboxing.
13. **A hung `Stop()` hangs unload.** In-proc code cannot be safely aborted; design plugins to never block, and move untrusted plugins out-of-proc.
14. **ALC unload is asynchronous.** `Unload()` returns before collection; the old directory remains locked until GC. Production needs retries and a quarantine path, not a one-shot delete.
15. **Native dependencies may not unload.** OS loader semantics can keep native modules mapped even after managed unload; avoid native deps or isolate the process.
16. **One active version per id is an application rule.** Two ALCs can load v1 and v2 simultaneously, but the host should enforce one active version to avoid duplicate timers/UI/settings writers.
17. **Manifest path safety matters.** Reject `..`, absolute paths, separators and assembly-qualified type names; verify the final entry path stays under the plugin folder; cap manifest size.
18. **Do not use `Assembly.Load(byte[])` as a lock workaround.** It breaks Location/ADR/deps/native/satellite behavior and makes diagnostics unreliable.
19. **Do not trim the host in v2.** Plugin activation uses reflection; trimming can remove metadata the loader needs. Validate a trimmed publish only if you add explicit roots and test every plugin.
20. **UI/threading is untested in this UI-free spike.** Avalonia dispatcher affinity, UI thread marshalling and plugin UI extensions need a follow-up spike. The forbidden-reference policy intentionally blocks direct Avalonia/`FluidBar.App` coupling.

---

## 14. Limitations of this spike

- UI-free console host; no Avalonia dispatcher/UI-thread tests, no actual plugin UI extension.
- No Authenticode/manifest signature verification, no trust store, no hash allowlist implementation.
- No NuGet-package dependency runtime test (project-reference dependency only); `EnableDynamicLoading` package-asset behavior was validated via MSBuild properties, not a packed NuGet dependency.
- Single-file test is self-contained win-x64, untrimmed; no trimmed/single-file plugin scenario (deliberately unsupported).
- No long-duration stress (hundreds/thousands of load-unload cycles), no multi-threaded lifecycle stress, no native DLL unload test.
- Intentionally leaked timer ALC remains alive until process exit; no attempt to recover it.
- Timing of GC collection is machine/JIT dependent; the harness retries 12 GC cycles. Production should measure and use a bounded verification policy.
- Windows-only evidence (file locks, single-file win-x64). Linux/macOS behavior differs for file locks and native loading.
- The static event bus exists only to demonstrate a leak; the production SDK should not ship it.

---

## 15. Evidence index and reproduction

Everything is under `%TEMP%\fluidbar-spikes\plugin-alc`:

| Path | Contents |
|---|---|
| `FluidBar.Sdk\` | SDK contract + parser/serializer + API/capability types |
| `host\FluidBar.Host\` | Loader, ALC, discovery, scanner, context, evidence harness |
| `plugins\` | 10 plugin projects (simple v1/v2, dependent, no-deps, leaky, fixed, throwing, conflict A/B, forbidden) |
| `libs\` | `Shared.Helper` 1.0.0.0 and 2.0.0.0 |
| `stubs\` | fake `FluidBar.App` / `Avalonia.Base` for policy tests |
| `manifests\` | 25 fixture manifests (26 JSON files incl. the active-pointer template) |
| `tools\stage.ps1` | restages 25 packages into flat/versioned/lock roots |
| `tools\run-suite.ps1` | build/stage/suite + single-file publish/run |
| `evidence\suite.json` | 166 checks, 0 failures |
| `evidence\singlefile.json` | 13 checks, 0 failures |
| `evidence\msbuild-properties.txt` | `EnableDynamicLoading` property evidence |
| `artifacts\publish\singlefile\FluidBar.Host.exe` | 73.5 MB self-contained single-file host |

Minimal rerun:

```powershell
cd $env:TEMP\fluidbar-spikes\plugin-alc
dotnet build FluidBar.Spike.slnx -c Release
.\tools\run-suite.ps1
```

Or server-free evidence regeneration:

```powershell
.\tools\stage.ps1
.\host\FluidBar.Host\bin\Release\net10.0-windows\FluidBar.Host.exe --mode suite `
  --plugins artifacts\plugins --versioned artifacts\plugins-versioned --lock artifacts\plugins-lock `
  --evidence evidence\suite.json
.\artifacts\publish\singlefile\FluidBar.Host.exe --mode singlefile `
  --plugins artifacts\plugins --evidence evidence\singlefile.json
```

---

## 16. Final recommendation

**Ship the in-proc ALC design in v2 for signed, trusted plugins, with the conditions in section 1.** The mechanics are proven: unload really collects, reload works, dependency conflicts are isolated, failures do not take down the host, single-file publishing works, and policy/manifest gates can reject packages before code runs.

**Do not ship it as a security sandbox.** Invest next in:

1. Authenticode/package signature verification and a trusted-publisher store.
2. A process-isolated backend for untrusted plugins (future v3), using the same manifest/capability protocol.
3. Avalonia UI-thread/UI-extension spike with an SDK-provided UI abstraction (no direct Avalonia reference in plugins).
4. A bounded unload-verification + quarantine policy in the host, with telemetry for "unload pending" rates.
5. CI package validation for SDK duplicates, forbidden refs, deps.json and versioned layout.
