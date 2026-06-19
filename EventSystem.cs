namespace FluidBar;

public enum IslandEventKind
{
    Auto,
    Text,
    ScrollingText,
    Progress,
    Status,
    Media,
    Notification,
    Agent
}

public sealed record IslandEventPayload(
    IslandEventKind Kind = IslandEventKind.Auto,
    string? Subtitle = null,
    string? Badge = null,
    string? SourceName = null,
    int? ProgressPercent = null,
    bool? IsActive = null,
    bool? ShowsAudioWave = null,
    string? AlbumArtPath = null,
    string? AppIconPath = null,
    string? LyricLine = null,
    string? SecondaryLyricLine = null,
    IReadOnlyList<string>? DetailLines = null,
    long PositionTicks = 0,
    long EndTicks = 0,
    long StartTimeTicks = 0,
    long LastUpdatedTicks = 0);

/// <summary>
/// 灵动岛事件数据
/// </summary>
/// <param name="Source">事件来源标识（如 "clipboard", "notification"）</param>
/// <param name="Title">标题（如 "已复制"）</param>
/// <param name="Content">内容文本</param>
/// <param name="IconKind">图标类型标识，用于 UI 区分</param>
/// <param name="Payload">可选富事件数据，用于媒体、通知、Agent 状态等复杂插件</param>
public sealed record IslandEvent(
    string Source,
    string Title,
    string Content,
    string? IconKind = null,
    IslandEventPayload? Payload = null);

/// <summary>
/// 事件总线 - 所有事件源通过此接口触发灵动岛
/// MainWindow 订阅 EventTriggered 统一处理展示
/// </summary>
public sealed class EventBus
{
    public event Action<IslandEvent>? EventTriggered;

    public void Publish(IslandEvent evt)
    {
        EventTriggered?.Invoke(evt);
    }
}

/// <summary>
/// 事件源接口 - 每个事件源（剪贴板、通知等）实现此接口
/// </summary>
public interface IEventSource : IDisposable
{
    void Start();
    void Stop();
}
