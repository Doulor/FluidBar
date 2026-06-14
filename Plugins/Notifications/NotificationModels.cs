namespace FluidBar;

public sealed record NotificationSnapshot(
    uint Id,
    string AppName,
    string Title,
    string Body,
    DateTimeOffset Timestamp,
    string? AppIconPath = null);

public static class NotificationIslandEventFactory
{
    public static IslandEvent FromSnapshot(NotificationSnapshot snapshot)
    {
        var appName = string.IsNullOrWhiteSpace(snapshot.AppName)
            ? "系统通知"
            : snapshot.AppName;
        var title = string.IsNullOrWhiteSpace(snapshot.Title)
            ? appName
            : snapshot.Title;
        var body = string.IsNullOrWhiteSpace(snapshot.Body)
            ? "新的通知"
            : snapshot.Body;

        return new IslandEvent(
            Source: "notifications",
            Title: appName,
            Content: title,
            IconKind: "notification",
            Payload: new IslandEventPayload(
                Kind: IslandEventKind.Notification,
                Subtitle: title,
                Badge: "系统通知",
                SourceName: appName,
                AppIconPath: snapshot.AppIconPath,
                DetailLines: new[]
                {
                    body,
                    snapshot.Timestamp.LocalDateTime.ToString("HH:mm")
                }));
    }
}

