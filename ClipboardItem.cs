namespace FluidBar;

/// <summary>
/// 剪贴板内容数据模型
/// </summary>
public sealed class ClipboardItem
{
    public string Text { get; init; } = string.Empty;
    public DateTime CopiedAt { get; init; } = DateTime.Now;
}
