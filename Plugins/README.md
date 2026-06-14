# FluidBar Plugins

FluidBar uses source-based official plugins. A plugin lives in this repository, is reviewed through a pull request, and ships with the app after it is accepted.

## Add A Plugin

1. Fork the repository.
2. Create a folder under `Plugins/<PluginName>/`.
3. Implement `IIslandPlugin`.
4. Add a catalog entry to `Plugins/catalog.json`.
5. Add settings UI through the plugin config object if needed.
6. Add tests in `FluidBar.Tests/Program.cs`.
7. Run:

```powershell
dotnet run --project FluidBar.Tests\FluidBar.Tests.csproj
dotnet build -c CodexVerify
```

8. Open a pull request to `main`.

Runtime loading of arbitrary third-party DLLs is intentionally out of scope. The review process protects users from untrusted code while keeping contributions lightweight.

## Official Plugins

- `clipboard`: copied text display.
- `media`: Windows media session display with best-effort lyrics.
- `agent-status`: Claude Code / Codex hook bridge.
- `notifications`: Windows toast notification display.

