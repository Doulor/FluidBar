namespace FluidBar.App.Hosting.Skia;

/// <summary>A plugin page row/button command raised by a real island click or UIA invocation.</summary>
/// <param name="PageIndex">Total page index (built-ins first, then plugin pages).</param>
/// <param name="OwnerPluginId">Plugin that registered the page.</param>
/// <param name="PageId">Registered PageId.</param>
/// <param name="RowId">Row id supplied in page data.</param>
/// <param name="CommandId">Manifest-declared command to execute.</param>
public sealed record PluginPageCommandRequest(
    int PageIndex,
    string OwnerPluginId,
    string PageId,
    string RowId,
    string CommandId,
    double? Value = null,
    bool LongPress = false,
    bool DoubleClick = false);
