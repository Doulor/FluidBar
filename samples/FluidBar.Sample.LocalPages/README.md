# Local Pages Sample (M0)

This sample proves the M0 plugin-page loop through the **local unsigned channel**:

- `RegisterPage` adds a seventh host-rendered page after the six built-ins.
- `PublishPage` updates the list rows through the host list template.
- The row's trailing button invokes a manifest-declared command `fluidbar-sample-local-pages.refresh`.

## Build and install

```powershell
dotnet build samples\FluidBar.Sample.LocalPages\FluidBar.Sample.LocalPages.csproj -c Debug

$src = "samples\FluidBar.Sample.LocalPages\bin\Debug\net10.0"
$dst = Join-Path $env:APPDATA "FluidBar\plugins\local\fluidbar-sample-local-pages"
New-Item -ItemType Directory -Force -Path (Join-Path $dst "plugin"), (Join-Path $dst "locales") | Out-Null
Copy-Item "$src\FluidBar.Sample.LocalPages.dll" (Join-Path $dst "plugin") -Force
Copy-Item "$src\manifest.json" $dst -Force
Copy-Item "$src\locales\en-US.json" (Join-Path $dst "locales") -Force
```

Restart FluidBar. The page appears as **07 Sample pages** in the island and rail; Settings → Plugins →
**Local plugins** offers Reload/Unload. Unload removes the page immediately without restarting the app.

The manifest declares:

```json
"capabilities": {
  "ui": { "island": true, "pages": true, "pageTemplates": ["list"], "commands": true }
}
```
