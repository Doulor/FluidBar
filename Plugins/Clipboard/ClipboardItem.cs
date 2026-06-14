namespace FluidBar;

public sealed class ClipboardItem
{
    public string Text { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string SourceApp { get; set; } = string.Empty;
}

