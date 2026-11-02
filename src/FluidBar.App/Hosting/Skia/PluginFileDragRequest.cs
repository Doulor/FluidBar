namespace FluidBar.App.Hosting.Skia;

/// <summary>Immutable references captured from an explicitly pressed, host-rendered file row.</summary>
public sealed record PluginFileDragRequest(int PageIndex, string PageId, string RowId, IReadOnlyList<string> Paths);
