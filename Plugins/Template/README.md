# Plugin Template

Use this folder as the checklist for a new FluidBar source plugin.

Required shape:

```csharp
public sealed class MyPlugin : IIslandPlugin
{
    public string Id => "my-plugin";
    public string Name => "My Plugin";
    public string Description => "What it shows in FluidBar.";
    public string Icon => "\uE946";
    public bool Enabled { get; set; } = true;
    public IPluginConfig? Config => null;
    public event Action<IslandEvent>? EventTriggered;

    public void Initialize() { }
    public void Start() { }
    public void Stop() { }
    public void Dispose() => Stop();
}
```

Prefer emitting `IslandEvent` with an `IslandEventPayload` when the plugin needs a media, notification, agent, progress, or status layout.

