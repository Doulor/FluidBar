using FluidBar;
using System.IO;

var settings = new FluidBarSettings
{
    CollapsedWidth = 92,
    CollapsedHeight = 24,
    ExpandedMaxWidth = 260,
    ExpandedHeight = 44
};

Test("battery charging state is explicit", () =>
{
    var view = IslandPresentation.FromEvent(
        new IslandEvent("battery", "充电中 87%", "约 12 分钟后充满", "battery_charge"),
        settings);

    AssertEqual(IslandViewKind.Status, view.Kind);
    AssertEqual("battery_charge", view.IconKind);
    AssertContains("充电中", view.StatusText);
    AssertContains("外接电源", view.StatusBadge);
});

Test("battery power state does not read as unknown", () =>
{
    var view = IslandPresentation.FromEvent(
        new IslandEvent("battery", "电池 63%", "电池供电中", "battery"),
        settings);

    AssertEqual(IslandViewKind.Status, view.Kind);
    AssertContains("电池供电", view.StatusText);
    AssertDoesNotContain("未充电", view.StatusText);
});

Test("progress percent is clamped for runaway monitor values", () =>
{
    var view = IslandPresentation.FromEvent(
        new IslandEvent("volume", "音量 255%", "255%", "volume"),
        settings);

    AssertEqual(IslandViewKind.Progress, view.Kind);
    AssertEqual(100, view.ProgressPercent);
    AssertEqual(true, view.ShowsAudioWave);
});

Test("expanded metrics protect the island from being crushed", () =>
{
    var view = IslandPresentation.FromEvent(
        new IslandEvent("clipboard", "已复制", new string('A', 120), "clipboard"),
        settings);

    AssertEqual(IslandViewKind.ScrollingText, view.Kind);
    AssertAtLeast(260, view.TargetWidth);
    AssertAtLeast(56, view.TargetHeight);
    AssertAtLeast(38, view.CollapsedHeight);
});

Test("scrolling text starts at the left edge before marquee motion", () =>
{
    var plan = ScrollingTextMotionPlan.CreateInitial();

    AssertEqual(0, plan.InitialOffset);
    AssertEqual(500, plan.HoldMilliseconds);
});

Test("monitor feature settings are created with hover card enabled", () =>
{
    var feature = new FluidBarSettings().GetMonitorFeatureSettings("volume");

    AssertEqual(true, feature.HoverCardEnabled);
    AssertEqual(3000, feature.DisplayDurationMs);
});

Test("display strategy defaults to latest only", () =>
{
    var defaultSettings = new FluidBarSettings();

    AssertEqual(IslandDisplayStrategy.LatestOnly, defaultSettings.DisplayStrategy);
    AssertEqual(false, IslandStackPolicy.CanStack(defaultSettings));
});

Test("plugin catalog contains the official source plugins", () =>
{
    var catalogPath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        "Plugins",
        "catalog.json"));
    var catalog = PluginCatalog.Load(catalogPath);

    AssertEqual(4, catalog.Plugins.Count);
    AssertEqual(true, catalog.Contains("clipboard"));
    AssertEqual(true, catalog.Contains("media"));
    AssertEqual(true, catalog.Contains("agent-status"));
    AssertEqual(true, catalog.Contains("notifications"));
    foreach (var plugin in catalog.Plugins)
    {
        AssertNotEmpty(plugin.Name);
        AssertNotEmpty(plugin.Category);
        AssertNotEmpty(plugin.EntryPoint);
    }
});

Test("legacy island events stay text compatible", () =>
{
    var evt = new IslandEvent("legacy", "旧插件", "普通内容", "info");
    var view = IslandPresentation.FromEvent(evt, settings);

    AssertEqual(IslandViewKind.Text, view.Kind);
    AssertEqual("旧插件", view.Title);
    AssertEqual("普通内容", view.Content);
    AssertEqual("info", view.IconKind);
});

Test("media payload projects to wave progress and lyrics", () =>
{
    var evt = new IslandEvent(
        "media",
        "在播放",
        "Cornfield Chase",
        "media",
        new IslandEventPayload(
            Kind: IslandEventKind.Media,
            Subtitle: "Hans Zimmer",
            Badge: "Spotify",
            SourceName: "Spotify",
            ProgressPercent: 42,
            IsActive: true,
            ShowsAudioWave: true,
            LyricLine: "I'm going home",
            SecondaryLyricLine: "Cooper Station"));

    var view = IslandPresentation.FromEvent(evt, settings);
    var card = HoverCardPresentation.FromCompact(view, settings);

    AssertEqual(IslandViewKind.Media, view.Kind);
    AssertEqual(true, view.ShowsAudioWave);
    AssertEqual(42, view.ProgressPercent);
    AssertContains("Hans Zimmer", view.StatusText);
    // Lyrics appear in the hover subtitle for media
    AssertContains("I'm going home", card.LyricLine ?? "");
    AssertEqual("Spotify", card.StatusBadge);
});

Test("media snapshot creates a rich island event", () =>
{
    var snapshot = new MediaSnapshot(
        SourceAppUserModelId: "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify",
        SourceName: "Spotify",
        Title: "Cornfield Chase",
        Artist: "Hans Zimmer",
        Album: "Interstellar",
        IsPlaying: true,
        ProgressPercent: 42,
        LyricLine: "I'm going home",
        SecondaryLyricLine: "Cooper Station");

    var evt = MediaIslandEventFactory.FromSnapshot(snapshot);

    AssertEqual("media", evt.Source);
    AssertEqual("media", evt.IconKind);
    AssertEqual(IslandEventKind.Media, evt.Payload?.Kind);
    AssertEqual(true, evt.Payload?.ShowsAudioWave);
    AssertEqual(42, evt.Payload?.ProgressPercent);
    AssertContains("Hans Zimmer", evt.Payload?.Subtitle ?? "");
    AssertContains("I'm going home", evt.Payload?.LyricLine ?? "");
});

Test("agent payload projects to agent status view", () =>
{
    var evt = new IslandEvent(
        "agent-status",
        "Claude Code 完成",
        "重构完成",
        "agent",
        new IslandEventPayload(
            Kind: IslandEventKind.Agent,
            Subtitle: "FluidBar",
            Badge: "完成",
            SourceName: "Claude Code",
            IsActive: false,
            DetailLines: new[] { "分支 main", "耗时 46s" }));

    var view = IslandPresentation.FromEvent(evt, settings);
    var card = HoverCardPresentation.FromCompact(view, settings);

    AssertEqual(IslandViewKind.Agent, view.Kind);
    AssertContains("Claude Code", view.StatusBadge);
    AssertContains("分支 main", card.Content);
});

Test("agent hook json creates completion island event", () =>
{
    const string json = """
    {
      "tool": "claude-code",
      "status": "completed",
      "project": "FluidBar",
      "summary": "任务完成",
      "branch": "main",
      "durationMs": 46000
    }
    """;

    var hook = AgentHookEvent.Parse(json);
    var evt = AgentStatusIslandEventFactory.FromHook(hook);

    AssertEqual("agent-status", evt.Source);
    AssertEqual("agent", evt.IconKind);
    AssertEqual(IslandEventKind.Agent, evt.Payload?.Kind);
    AssertContains("Claude Code", evt.Payload?.SourceName ?? "");
    AssertContains("FluidBar", evt.Payload?.Subtitle ?? "");
    AssertContains("46s", string.Join(" ", evt.Payload?.DetailLines ?? Array.Empty<string>()));
});

Test("notification payload projects to notification view", () =>
{
    var evt = new IslandEvent(
        "notifications",
        "微信",
        "新的消息",
        "notification",
        new IslandEventPayload(
            Kind: IslandEventKind.Notification,
            Subtitle: "张三",
            Badge: "系统通知",
            SourceName: "微信",
            DetailLines: new[] { "今晚 8 点开会", "点击系统通知查看详情" }));

    var view = IslandPresentation.FromEvent(evt, settings);
    var card = HoverCardPresentation.FromCompact(view, settings);

    AssertEqual(IslandViewKind.Notification, view.Kind);
    AssertContains("张三", view.StatusText);
    AssertContains("今晚 8 点开会", card.Content);
});

Test("notification snapshot creates a rich island event", () =>
{
    var snapshot = new NotificationSnapshot(
        Id: 42,
        AppName: "微信",
        Title: "张三",
        Body: "今晚 8 点开会",
        Timestamp: new DateTimeOffset(2026, 6, 14, 20, 0, 0, TimeSpan.FromHours(8)),
        AppIconPath: null);

    var evt = NotificationIslandEventFactory.FromSnapshot(snapshot);

    AssertEqual("notifications", evt.Source);
    AssertEqual("notification", evt.IconKind);
    AssertEqual(IslandEventKind.Notification, evt.Payload?.Kind);
    AssertContains("微信", evt.Payload?.SourceName ?? "");
    AssertContains("张三", evt.Payload?.Subtitle ?? "");
    AssertContains("今晚 8 点开会", string.Join(" ", evt.Payload?.DetailLines ?? Array.Empty<string>()));
});

Test("settings panel suppresses multi island snapshot rendering", () =>
{
    var multiSettings = new FluidBarSettings
    {
        DisplayStrategy = IslandDisplayStrategy.Multiple
    };

    AssertEqual(true, IslandStackVisibilityPolicy.ShouldRender(
        multiSettings,
        stackCount: 2,
        isSettingsPanelOpen: false,
        currentKind: IslandViewKind.Status));
    AssertEqual(false, IslandStackVisibilityPolicy.ShouldRender(
        multiSettings,
        stackCount: 2,
        isSettingsPanelOpen: true,
        currentKind: IslandViewKind.Status));
    AssertEqual(false, IslandStackVisibilityPolicy.ShouldRender(
        multiSettings,
        stackCount: 2,
        isSettingsPanelOpen: false,
        currentKind: IslandViewKind.Clock));
});

Test("multiple display strategy appends non clock islands up to the max count", () =>
{
    var multiSettings = new FluidBarSettings
    {
        DisplayStrategy = IslandDisplayStrategy.Multiple,
        MaxVisibleIslands = 3
    };
    var first = IslandPresentation.FromEvent(
        new IslandEvent("volume", "音量 40%", "40%", "volume"),
        settings);
    var second = IslandPresentation.FromEvent(
        new IslandEvent("battery", "电池 88%", "电池供电中", "battery"),
        settings);
    var third = IslandPresentation.FromEvent(
        new IslandEvent("clipboard", "已复制", "一段复制内容", "clipboard"),
        settings);
    var fourth = IslandPresentation.FromEvent(
        new IslandEvent("network", "网络已连接", "Wi-Fi", "network"),
        settings);

    var stack = IslandStackPolicy.Apply(Array.Empty<IslandStackItem>(), first, "volume", multiSettings);
    stack = IslandStackPolicy.Apply(stack, second, "battery", multiSettings);
    stack = IslandStackPolicy.Apply(stack, third, "clipboard", multiSettings);
    stack = IslandStackPolicy.Apply(stack, fourth, "network", multiSettings);

    AssertEqual(3, stack.Count);
    AssertEqual("battery", stack[0].Source);
    AssertEqual("clipboard", stack[1].Source);
    AssertEqual("network", stack[2].Source);
});

Test("multiple display strategy updates an existing source as the newest island", () =>
{
    var multiSettings = new FluidBarSettings
    {
        DisplayStrategy = IslandDisplayStrategy.Multiple
    };
    var firstVolume = IslandPresentation.FromEvent(
        new IslandEvent("volume", "音量 40%", "40%", "volume"),
        settings);
    var battery = IslandPresentation.FromEvent(
        new IslandEvent("battery", "电池 88%", "电池供电中", "battery"),
        settings);
    var secondVolume = IslandPresentation.FromEvent(
        new IslandEvent("volume", "音量 62%", "62%", "volume"),
        settings);

    var stack = IslandStackPolicy.Apply(Array.Empty<IslandStackItem>(), firstVolume, "volume", multiSettings);
    stack = IslandStackPolicy.Apply(stack, battery, "battery", multiSettings);
    stack = IslandStackPolicy.Apply(stack, secondVolume, "volume", multiSettings);

    AssertEqual(2, stack.Count);
    AssertEqual("battery", stack[0].Source);
    AssertEqual("volume", stack[1].Source);
    AssertEqual(62, stack[1].View.ProgressPercent);
});

Test("clock island never joins the multi island stack", () =>
{
    var multiSettings = new FluidBarSettings
    {
        DisplayStrategy = IslandDisplayStrategy.Multiple
    };
    var volume = IslandPresentation.FromEvent(
        new IslandEvent("volume", "音量 40%", "40%", "volume"),
        settings);
    var clock = IslandPresentation.FromEvent(
        new IslandEvent("clock", "18:30", "6月14日 周日", "clock"),
        settings);

    var stack = IslandStackPolicy.Apply(Array.Empty<IslandStackItem>(), volume, "volume", multiSettings);
    stack = IslandStackPolicy.Apply(stack, clock, "clock", multiSettings);

    AssertEqual(1, stack.Count);
    AssertEqual("volume", stack[0].Source);
});

Test("switching back to latest only keeps just the newest island", () =>
{
    var latestSettings = new FluidBarSettings
    {
        DisplayStrategy = IslandDisplayStrategy.LatestOnly
    };
    var existing = new[]
    {
        new IslandStackItem("volume", IslandPresentation.FromEvent(
            new IslandEvent("volume", "音量 40%", "40%", "volume"),
            settings), DateTimeOffset.UnixEpoch)
    };
    var battery = IslandPresentation.FromEvent(
        new IslandEvent("battery", "电池 88%", "电池供电中", "battery"),
        settings);

    var stack = IslandStackPolicy.Apply(existing, battery, "battery", latestSettings);

    AssertEqual(1, stack.Count);
    AssertEqual("battery", stack[0].Source);
});

Test("multi island group stays centered around the base anchor", () =>
{
    var slots = new[]
    {
        new IslandSlotMetrics(180, 56),
        new IslandSlotMetrics(260, 56)
    };

    var layout = IslandGroupLayout.Calculate(
        slots,
        position: "Top",
        screenWidth: 1000,
        screenHeight: 700,
        offsetX: 0,
        offsetY: 0,
        gap: 10);

    AssertEqual(450.0, layout.VisualWidth);
    AssertNear(275, layout.Left, 0.1);
    AssertEqual(0.0, layout.Slots[0].OffsetX);
    AssertEqual(190.0, layout.Slots[1].OffsetX);
});

Test("left anchored multi island keeps the first island at the base edge", () =>
{
    var slots = new[]
    {
        new IslandSlotMetrics(180, 56),
        new IslandSlotMetrics(260, 56)
    };

    var layout = IslandGroupLayout.Calculate(
        slots,
        position: "TopLeft",
        screenWidth: 1000,
        screenHeight: 700,
        offsetX: 0,
        offsetY: 0,
        gap: 10);

    AssertNear(16, layout.Left, 0.1);
    AssertEqual(190.0, layout.Slots[1].OffsetX);
});

Test("right anchored multi island keeps the group right edge at the base edge", () =>
{
    var slots = new[]
    {
        new IslandSlotMetrics(180, 56),
        new IslandSlotMetrics(260, 56)
    };

    var layout = IslandGroupLayout.Calculate(
        slots,
        position: "TopRight",
        screenWidth: 1000,
        screenHeight: 700,
        offsetX: 0,
        offsetY: 0,
        gap: 10);

    AssertNear(534, layout.Left, 0.1);
    AssertNear(984, layout.Left + layout.VisualWidth, 0.1);
});

Test("clock hover card can be disabled from feature settings", () =>
{
    var clockSettings = new FluidBarSettings();
    clockSettings.GetMonitorFeatureSettings("clock").HoverCardEnabled = false;

    AssertEqual(false, HoverCardPolicy.CanShow(
        isExpanded: true,
        isSettingsPanelOpen: false,
        currentSource: "clock",
        currentViewExists: true,
        clockSettings));
});

Test("configured island width becomes the normal target width", () =>
{
    var configured = new FluidBarSettings
    {
        CollapsedWidth = 126,
        CollapsedHeight = 38,
        ExpandedMaxWidth = 520,
        ExpandedHeight = 62
    };
    var view = IslandPresentation.FromEvent(
        new IslandEvent("clipboard", "已复制", "短文本", "clipboard"),
        configured);

    AssertEqual(520.0, view.TargetWidth);
});

Test("configured island height can go below the old 72 floor", () =>
{
    var compactHeight = new FluidBarSettings
    {
        CollapsedWidth = 126,
        CollapsedHeight = 38,
        ExpandedMaxWidth = 320,
        ExpandedHeight = 56
    };
    var view = IslandPresentation.FromEvent(
        new IslandEvent("volume", "音量 50%", "50%", "volume"),
        compactHeight);

    AssertEqual(56.0, view.TargetHeight);
});

Test("hover card metrics expand into a larger card shape", () =>
{
    var compact = IslandPresentation.FromEvent(
        new IslandEvent("clipboard", "已复制", "短文本", "clipboard"),
        settings);
    var card = HoverCardPresentation.FromCompact(compact, settings);

    AssertAtLeast(compact.TargetWidth + 96, card.TargetWidth);
    AssertAtLeast(168, card.TargetHeight);
    AssertEqual(IslandDisplayMode.HoverCard, card.Mode);
});

Test("clipboard hover card allows multiline copied content", () =>
{
    var compact = IslandPresentation.FromEvent(
        new IslandEvent("clipboard", "已复制", new string('中', 160), "clipboard"),
        settings);
    var card = HoverCardPresentation.FromCompact(compact, settings);

    AssertAtLeast(4, card.DetailLines);
    AssertEqual(true, card.AllowsMultilineContent);
});

Test("hover card remains larger when island width is customized wide", () =>
{
    var wideSettings = new FluidBarSettings
    {
        CollapsedWidth = 180,
        CollapsedHeight = 44,
        ExpandedMaxWidth = 620,
        ExpandedHeight = 88
    };
    var compact = IslandPresentation.FromEvent(
        new IslandEvent("clipboard", "已复制", "宽灵动岛测试", "clipboard"),
        wideSettings);
    var card = HoverCardPresentation.FromCompact(compact, wideSettings);

    AssertAtLeast(compact.TargetWidth + 96, card.TargetWidth);
});

Test("hover card motion uses the example continuous spring model", () =>
{
    var plan = HoverCardMotionPlan.CreateOpening(
        fromWidth: 260,
        fromHeight: 56,
        toWidth: 430,
        toHeight: 190);

    AssertEqual(HoverCardMotionKind.WarpOpen, plan.Kind);
    AssertEqual(true, plan.UsesContinuousSpring);
    AssertEqual(0, plan.WidthKeyFrames);
    AssertEqual(0, plan.HeightKeyFrames);
    AssertEqual(0, plan.HeightDelayMilliseconds);
    AssertEqual(false, plan.UsesOvershoot);
    AssertAtLeast(360, plan.ExpandingStiffness);
    AssertAtLeast(24, plan.ExpandingDamping);
    AssertAtLeast(190, plan.ContractingStiffness);
    AssertAtLeast(26, plan.ContractingDamping);
});

Test("hover card motion is render synchronized and avoids per frame window resizing", () =>
{
    var plan = HoverCardMotionPlan.CreateOpening(
        fromWidth: 260,
        fromHeight: 56,
        toWidth: 430,
        toHeight: 190);

    AssertEqual(true, plan.UsesRenderSynchronizedFrames);
    AssertEqual(false, plan.AnimatesWindowBoundsEveryFrame);
});

Test("hover card close motion is faster and lighter than opening", () =>
{
    var open = HoverCardMotionPlan.CreateOpening(
        fromWidth: 260,
        fromHeight: 56,
        toWidth: 430,
        toHeight: 190);
    var close = HoverCardMotionPlan.CreateClosing(
        fromWidth: 430,
        fromHeight: 190,
        toWidth: 260,
        toHeight: 56);

    AssertEqual(HoverCardMotionKind.WarpClose, close.Kind);
    AssertEqual(true, close.UsesContinuousSpring);
    AssertAtMost(open.ExpandingStiffness, close.ExpandingStiffness);
    AssertAtLeast(open.ContractingDamping, close.ContractingDamping);
});

Test("spring value approaches target gradually without a keyframe jump", () =>
{
    var spring = new SpringValue();
    spring.Reset(120);
    spring.Target = 380;

    spring.Step(1.0 / 60.0, stiffness: 380, damping: 26);

    AssertAtLeast(120.1, spring.Value);
    AssertAtMost(160, spring.Value);

    for (var i = 0; i < 120; i++)
        spring.Step(1.0 / 60.0, stiffness: 380, damping: 26);

    AssertNear(380, spring.Value, 0.35);
});

Console.WriteLine("All FluidBar presentation tests passed.");

static void Test(string name, Action body)
{
    try
    {
        body();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
        Environment.ExitCode = 1;
    }
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"expected {expected}, got {actual}");
}

static void AssertContains(string expected, string actual)
{
    if (!actual.Contains(expected, StringComparison.Ordinal))
        throw new InvalidOperationException($"expected '{actual}' to contain '{expected}'");
}

static void AssertDoesNotContain(string unexpected, string actual)
{
    if (actual.Contains(unexpected, StringComparison.Ordinal))
        throw new InvalidOperationException($"expected '{actual}' not to contain '{unexpected}'");
}

static void AssertNotEmpty(string actual)
{
    if (string.IsNullOrWhiteSpace(actual))
        throw new InvalidOperationException("expected non-empty text");
}

static void AssertAtLeast(double minimum, double actual)
{
    if (actual < minimum)
        throw new InvalidOperationException($"expected at least {minimum}, got {actual}");
}

static void AssertAtMost(double maximum, double actual)
{
    if (actual > maximum)
        throw new InvalidOperationException($"expected at most {maximum}, got {actual}");
}

static void AssertNear(double expected, double actual, double tolerance)
{
    if (Math.Abs(expected - actual) > tolerance)
        throw new InvalidOperationException(
            $"expected {actual} to be within {tolerance} of {expected}");
}
