using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

public sealed class NotificationCardLayoutTests
{
    [Theory]
    [InlineData(0.8)]
    [InlineData(1.0)]
    [InlineData(1.6)]
    public void Detail_wrap_preserves_all_text_elements_and_fits_measured_width(double scale)
    {
        var text = string.Concat(Enumerable.Repeat("通知内容 WWWWW 👩🏽‍💻 é 验证码 123456 ", 20));
        var metrics = FontScaleMetrics.From(scale);
        var lines = PageMetricsLayout.NotificationDetailLines(text, 320, metrics);
        Assert.Equal(text, string.Concat(lines));
        using var engine = new TextEngine();
        foreach (var line in lines)
            Assert.True(engine.Measure(line, metrics.Style(PageMetricsLayout.NotificationBodyStyle)) <= 320.01f);
        Assert.Same(lines, PageMetricsLayout.NotificationDetailLines(text, 320, metrics));
        var titleLines = PageMetricsLayout.NotificationDetailLines(text, 320, metrics, title: true);
        Assert.Equal(text, string.Concat(titleLines));
        foreach (var line in titleLines)
            Assert.True(engine.Measure(line, metrics.Style(PageMetricsLayout.NotificationTitleStyle)) <= 320.01f);
    }

    [Theory]
    [InlineData(false, false, 1.0)]
    [InlineData(false, true, 1.6)]
    [InlineData(true, false, 1.6)]
    [InlineData(true, true, 1.0)]
    public void Render_notification_list_code_and_long_detail(bool english, bool light, double scale)
    {
        var theme = light ? IslandTheme.Light : IslandTheme.Dark;
        var item = new NotificationItemModel { Id = 42, AppName = english ? "Account security" : "账号安全", Title = english ? "Confirm your sign-in" : "请验证本次登录操作",
            Body = english ? "Your one-time code is 418302. Never share this code." : "验证码为 418302，请勿将验证码告知他人。",
            FullBody = string.Concat(Enumerable.Repeat(english ? "Your code is 418302. Please review the sign-in location before continuing.\n" : "验证码为 418302，请确认登录地点后继续操作。请勿将验证码告知他人。\n", 18)),
            VerificationCode = "418302", TimeText = english ? "2 min ago" : "2 分钟前" };
        var model = new NotificationsPageModel { Items = [item, item with { Id = 43, AppName = "Outlook", Title = english ? "Review materials are ready" : "评审材料已准备完毕", VerificationCode = null }],
            FilterLabels = english ? ["All", "Unread", "Codes"] : ["全部", "未读", "验证码"], MarkReadLabel = english ? "Read all" : "全部已读", UndoLabel = english ? "Undo" : "撤销",
            ClearLabel = english ? "Clear" : "清空", BackLabel = english ? "Back" : "返回", CopyBodyLabel = english ? "Copy text" : "复制正文", CopyCodeLabel = english ? "Copy code" : "复制验证码", OpenSourceLabel = english ? "Open app" : "打开来源" };
        var state = IslandTestFixtures.WithFontScale(IslandTestFixtures.Expanded(2, theme: theme), scale);
        var directory = Path.Combine(IslandTestFixtures.RepoRoot, "build", "reports", "content-cards-preview");
        Directory.CreateDirectory(directory);
        using var renderer = new IslandRenderer(theme);
        foreach (var mode in new[] { "list", "code", "long" })
        {
            var detail = mode == "list" ? null : mode == "code" ? item : item with { VerificationCode = null, Title = english ? "Review materials are ready — please read the complete context and confirm your availability before the meeting" : "评审材料已经准备完毕，请完整阅读本次通知中的所有背景信息，并在会议开始之前确认参与时间和讨论议题" };
            var view = state with { Pages = state.Pages with { Notifications = model with { Detail = detail } } };
            using var bitmap = renderer.RenderToBitmap(view, 1.5f);
            var shell = IslandMetrics.ShellRect(view);
            using var card = new SKBitmap();
            Assert.True(bitmap.ExtractSubset(card, new SKRectI((int)(shell.Left * 1.5f) - 6, (int)(shell.Top * 1.5f) - 6,
                (int)(shell.Right * 1.5f) + 6, (int)(shell.Bottom * 1.5f) + 6)));
            IslandTestFixtures.SavePng(card, Path.Combine(directory, $"notifications-{mode}-{(english ? "en" : "zh")}-{(light ? "light" : "dark")}-fs{scale:0.0}.png"));
        }
    }

    [Fact]
    public void Detail_actions_replace_list_targets_and_code_is_only_live_when_present()
    {
        var page = new SKRect(0, 0, 400, 260);
        var model = new NotificationsPageModel { Detail = new NotificationItemModel { Id = 1, Body = "Full text" } };
        var copy = PageMetricsLayout.NotificationDetailAction(page, 0);
        Assert.Equal(IslandPageControl.NotificationCopyBody, PageMetricsLayout.HitTestNotifications(page, model, copy.MidX, copy.MidY, FontScaleMetrics.One, out _));
        var code = PageMetricsLayout.NotificationDetailAction(page, 1);
        Assert.Equal(IslandPageControl.None, PageMetricsLayout.HitTestNotifications(page, model, code.MidX, code.MidY, FontScaleMetrics.One, out _));
        var body = PageMetricsLayout.NotificationDetailBody(page);
        Assert.Equal(IslandPageControl.Scroll, PageMetricsLayout.HitTestNotifications(page, model, body.MidX, body.MidY, FontScaleMetrics.One, out _));
    }
}
