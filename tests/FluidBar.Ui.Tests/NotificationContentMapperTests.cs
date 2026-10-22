using System.Collections.Immutable;
using FluidBar.App.State;
using FluidBar.Platform.Windows.Notifications;

namespace FluidBar.Ui.Tests;

/// <summary>Toast snapshot → island notification page rows (real data source mapping).</summary>
public sealed class NotificationContentMapperTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 6, 0, 0, TimeSpan.Zero);

    private static NotificationEntry Entry(
        string app = "QQ",
        string title = "新邮件",
        string body = "body",
        string? code = null,
        int ageSeconds = 0,
        uint id = 1)
        => new(id, app, title, body, Now.AddSeconds(-ageSeconds), code);

    private static NotificationsSnapshot Snapshot(params NotificationEntry[] entries)
        => new(NotificationAccessState.Allowed, entries.ToImmutableArray());

    [Fact]
    public void Map_CopiesAppTitleBodyAndVerificationCode()
    {
        var item = NotificationContentMapper.MapEntry(
            Entry(app: "Windows PowerShell", title: "FluidBar 通知测试", body: "验证码 482913", code: "482913"),
            Now);

        Assert.Equal("Windows PowerShell", item.Source);
        Assert.Equal("FluidBar 通知测试", item.Title);
        Assert.Equal("验证码 482913", item.Body);
        Assert.Equal("482913", item.VerificationCode);
    }

    [Fact]
    public void Map_CollapsesMultilineToastBodyIntoSingleLine()
    {
        var item = NotificationContentMapper.MapEntry(
            Entry(title: "T", body: "first line\n\tsecond   line"), Now);

        Assert.Equal("first line second line", item.Body);
    }

    [Fact]
    public void Map_TruncatesLongBodyWithEllipsis()
    {
        var body = string.Join(' ', Enumerable.Repeat("word", 80));

        var item = NotificationContentMapper.MapEntry(Entry(title: "T", body: body), Now);

        Assert.True(item.Body.Length <= NotificationContentMapper.MaxBodyCharacters + 1);
        Assert.EndsWith("…", item.Body);
        Assert.StartsWith("word word", item.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_FallsBackToWindowsAndAppNameForMissingMetadata()
    {
        var noApp = NotificationContentMapper.MapEntry(Entry(app: " ", title: "T", body: "B"), Now);
        Assert.Equal(NotificationContentMapper.FallbackAppName, noApp.Source);

        var noTitle = NotificationContentMapper.MapEntry(Entry(app: "Outlook", title: "", body: "B"), Now);
        Assert.Equal("Outlook", noTitle.Title);
    }

    [Fact]
    public void Map_EmptyCodeBecomesNull()
    {
        var item = NotificationContentMapper.MapEntry(Entry(code: ""), Now);

        Assert.Null(item.VerificationCode);
    }

    [Fact]
    public void Map_BoundsTheRowCount()
    {
        var snapshot = Snapshot(Enumerable.Range(1, 20)
            .Select(i => Entry(id: (uint)i, title: $"n{i}"))
            .ToArray());

        var items = NotificationContentMapper.Map(snapshot, maxItems: 5, now: Now);

        Assert.Equal(5, items.Length);
        Assert.Equal("n1", items[0].Title);
    }

    [Fact]
    public void Map_UsesReadIdsForTheUnreadDot()
    {
        var snapshot = Snapshot(Entry(id: 1, title: "read"), Entry(id: 2, title: "unread"));
        var readIds = new HashSet<uint> { 1 };

        var items = NotificationContentMapper.Map(snapshot, now: Now, readIds: readIds);

        Assert.False(items[0].IsUnread);
        Assert.True(items[1].IsUnread);
    }

    [Fact]
    public void Map_EmptyOrMissingSnapshotYieldsNoRows()
    {
        Assert.Empty(NotificationContentMapper.Map(null, now: Now));
        Assert.Empty(NotificationContentMapper.Map(Snapshot(), now: Now));
        Assert.Empty(NotificationContentMapper.Map(Snapshot(Entry()), maxItems: 0, now: Now));
    }

    [Fact]
    public void RelativeTime_UsesEnglishFallbacksWithoutALocalizer()
    {
        Assert.Equal("now", NotificationContentMapper.RelativeTime(Now, Now));
        Assert.Equal("5 min", NotificationContentMapper.RelativeTime(Now.AddMinutes(-5), Now));
        Assert.Equal("3 h", NotificationContentMapper.RelativeTime(Now.AddHours(-3), Now));
    }

    [Fact]
    public void RelativeTime_TreatsFutureTimestampsAsNow()
    {
        Assert.Equal("now", NotificationContentMapper.RelativeTime(Now.AddMinutes(5), Now));
    }

    [Fact]
    public async Task RelativeTime_UsesTheLocalizedPluralCatalog()
    {
        var labels = await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("zh-Hans");
            return (
                JustNow: NotificationContentMapper.RelativeTime(Now, Now, localizer),
                Minutes: NotificationContentMapper.RelativeTime(Now.AddMinutes(-5), Now, localizer),
                Hours: NotificationContentMapper.RelativeTime(Now.AddHours(-2), Now, localizer));
        });

        Assert.Equal("刚刚", labels.JustNow);
        Assert.Equal("5 分钟前", labels.Minutes);
        Assert.Equal("2 小时前", labels.Hours);
        await AvaloniaTest.Run(() => TestFixtures.Localizer.SetCulture("en-US"));
    }
}
