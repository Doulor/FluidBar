using FluidBar.App.State;
using FluidBar.Core.Activities;
using FluidBar.Platform.Windows.Notifications;

namespace FluidBar.Ui.Tests;

public sealed class NotificationPreviewTextTests
{
    [Fact]
    public void Emoji_joiners_variation_selectors_and_visible_symbols_are_preserved()
    {
        const string text = "技术群 👩🏽‍💻 ❤️ · 张三：消息";
        Assert.Equal(text, NotificationContentMapper.SingleLinePreview(text));
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\t")]
    [InlineData("\u001f")]
    [InlineData("\u2028")]
    public void Toast_separators_are_readable_spaces_in_the_banner_and_list_but_preserved_in_full_text(string separator)
    {
        using var fixture = ControllerFixture.Create();
        var title = "群名称" + separator + "发消息的人";
        var body = "第一行消息" + separator + "第二行消息";
        var entry = new NotificationEntry(42, "QQ", title, body, fixture.Time.GetUtcNow(), null);
        fixture.Data.Update(c => c with { Notifications = [NotificationContentMapper.MapEntry(entry, fixture.Time.GetUtcNow())] });
        fixture.Controller.PublishActivity(ActivityKind.Notification,
            new ActivityPayload { Title = title, Body = body, SourceName = "QQ" }, 1, "text-preview-test");
        fixture.Settle();
        var state = fixture.Controller.RenderState;
        Assert.Equal(ActivityKind.Notification, state.Compact?.Kind);
        Assert.Equal("群名称 发消息的人", state.Compact.Value.Model.Title);
        Assert.Equal("第一行消息 第二行消息", state.Compact.Value.Model.Body);
        var row = Assert.Single(state.Pages.Notifications.Items);
        Assert.Equal("群名称 发消息的人", row.Title);
        Assert.Equal("第一行消息 第二行消息", row.Body);
        Assert.Equal(body, row.FullBody);
    }
}
