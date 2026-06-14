using FluidBar;

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
