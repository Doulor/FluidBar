using FluidBar.Core.Activities;
using FluidBar.Core.Layout;
using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

public sealed class IslandRenderTests
{
    [Theory]
    [InlineData(IslandMode.Float, 1263, 384)]
    [InlineData(IslandMode.Flush, 1263, 384)]
    [InlineData(IslandMode.Dock, 488, 444)]
    public void Measure_returns_the_fixed_host_surface(IslandMode mode, double width, double height)
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = IslandTestFixtures.Expanded(mode: mode);

        var size = renderer.Measure(state);
        Assert.Equal(width, size.Width);
        Assert.Equal(height, size.Height);

        var pixels = renderer.MeasurePixels(state, 1.5f);
        Assert.Equal((int)Math.Ceiling(width * 1.5), pixels.Width);
        Assert.Equal((int)Math.Ceiling(height * 1.5), pixels.Height);
    }

    public static TheoryData<string> CompactStates => new()
    {
        "idle", "media", "hud", "notification", "clipboard", "agent", "timer", "battery",
    };

    [Theory]
    [MemberData(nameof(CompactStates))]
    public void Compact_states_render_at_the_exact_token_size_and_stay_inside_the_shell(string name)
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = name switch
        {
            "idle" => IslandTestFixtures.Idle(),
            "media" => IslandTestFixtures.MediaCompact(),
            "hud" => IslandTestFixtures.Hud(),
            "notification" => IslandTestFixtures.Notification(),
            "clipboard" => IslandTestFixtures.Clipboard(),
            "agent" => IslandTestFixtures.Agent(),
            "timer" => IslandTestFixtures.Timer(),
            _ => IslandTestFixtures.Battery(),
        };

        using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
        Assert.Equal(1895, bitmap.Width);
        Assert.Equal(576, bitmap.Height);

        var expected = LayoutTokens.CompactSize(state.Compact!.Value.Kind);
        Assert.Equal(expected.Width, state.ShellWidth);
        Assert.Equal(expected.Height, state.ShellHeight);

        var shell = IslandMetrics.ShellRect(state);
        Assert.Equal(0, IslandTestFixtures.CountOpaqueOutside(bitmap, shell, IslandTestFixtures.Scale));
        Assert.True(IslandTestFixtures.CountOpaqueInside(bitmap, shell, IslandTestFixtures.Scale) > 200,
            $"{name}: the shell/content should be painted");
    }

    [Fact]
    public void Expanded_notifications_survive_unpaired_utf16_surrogates()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var baseState = IslandTestFixtures.Expanded();
        var items = baseState.Pages.Notifications.Items.ToList();
        items.Add(new NotificationItemModel
        {
            AppName = "Broken toast",
            Title = "before\uD83C",
            Body = "\uDE00after",
            TimeText = "now",
        });
        var state = baseState with
        {
            Pages = baseState.Pages with
            {
                Notifications = baseState.Pages.Notifications with { Items = items },
            },
        };

        using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
        Assert.Equal(1895, bitmap.Width);
        Assert.Equal(576, bitmap.Height);
    }

    [Fact]
    public void Compact_dock_uses_the_440x52_dock_strip()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = IslandTestFixtures.Compact(
            new CompactActivity(ActivityKind.Media, new CompactModel { Title = "Midnight City", Subtitle = "M83", IsActive = true }),
            IslandMode.Dock);

        Assert.Equal(440, state.ShellWidth);
        Assert.Equal(52, state.ShellHeight);
        using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
        Assert.Equal(732, bitmap.Width);
        Assert.Equal(666, bitmap.Height);
        Assert.Equal(0, IslandTestFixtures.CountOpaqueOutside(bitmap, IslandMetrics.ShellRect(state), IslandTestFixtures.Scale));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Expanded_pages_render_inside_the_shell(int page)
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = IslandTestFixtures.Expanded(page);

        using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
        Assert.Equal(1895, bitmap.Width);
        Assert.Equal(576, bitmap.Height);

        var shell = IslandMetrics.ShellRect(state);
        Assert.Equal(0, IslandTestFixtures.CountOpaqueOutside(bitmap, shell, IslandTestFixtures.Scale));
        Assert.True(IslandTestFixtures.CountOpaqueInside(bitmap, shell, IslandTestFixtures.Scale) > 2000,
            "the expanded shell should be painted");

        var center = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
        Assert.True(center.Alpha > 230, $"shell fill should be opaque, got alpha {center.Alpha}");
    }

    [Fact]
    public void Expanded_pages_have_no_black_band_above_or_below_the_content()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = IslandTestFixtures.Expanded(0);
        using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);

        // The shell fill is #08080A @96% everywhere; the content is not a separate opaque rect, so
        // sampling the title row, the body and the bottom margin must all be shell fill (no bands).
        var shell = IslandMetrics.ShellRect(state);
        var samples = new[]
        {
            new SKPoint(shell.Left + 8, shell.Top + 8),
            new SKPoint(shell.MidX, shell.Bottom - 6),
            new SKPoint(shell.Right - 8, shell.Top + 8),
        };
        foreach (var point in samples)
        {
            var pixel = bitmap.GetPixel((int)(point.X * IslandTestFixtures.Scale), (int)(point.Y * IslandTestFixtures.Scale));
            Assert.True(pixel.Alpha > 230, $"sample {point} should be shell fill, got alpha {pixel.Alpha}");
        }
    }

    [Fact]
    public void Transient_strip_renders_below_the_panel_and_only_when_visible()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var hidden = IslandTestFixtures.Expanded(2);
        var shown = IslandTestFixtures.Expanded(2, strip: true);

        var strip = renderer.MeasureStrip(shown);
        var shell = IslandMetrics.ShellRect(shown);
        Assert.Equal(shell.Bottom + 8f, strip.Strip.Top, 0.001f);

        using var hiddenBitmap = renderer.RenderToBitmap(hidden, IslandTestFixtures.Scale);
        using var shownBitmap = renderer.RenderToBitmap(shown, IslandTestFixtures.Scale);

        var band = new SKRect(strip.Strip.Left, strip.Strip.Top, strip.Strip.Right, strip.Strip.Bottom);
        Assert.Equal(0, IslandTestFixtures.CountOpaqueInside(hiddenBitmap, band, IslandTestFixtures.Scale, 200));
        Assert.True(IslandTestFixtures.CountOpaqueInside(shownBitmap, band, IslandTestFixtures.Scale, 200) > 200,
            "the revealed strip should paint mini-islands and dots");

        // The strip itself has no persistent background: gaps between pills stay transparent.
        var gapX = strip.Current.Right + 2;
        var gap = shownBitmap.GetPixel((int)(gapX * IslandTestFixtures.Scale), (int)(strip.Strip.MidY * IslandTestFixtures.Scale));
        Assert.True(gap.Alpha < 128, $"the strip gap should be transparent, got alpha {gap.Alpha}");
    }

    [Fact]
    public void Hidden_state_renders_a_fully_transparent_surface()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = IslandRenderState.Hidden(IslandMode.Float, IslandTestFixtures.Theme);
        using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);

        Assert.Equal(0, IslandTestFixtures.CountOpaqueInside(
            bitmap, new SKRect(0, 0, 1263, 384), IslandTestFixtures.Scale, 0));
    }

    [Fact]
    public void Page_transition_keeps_the_track_translation_and_current_enter_pose()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var settled = IslandTestFixtures.Expanded(0);
        var transition = IslandTestFixtures.Expanded(0, pagePosition: 0.4);

        using var settledBitmap = renderer.RenderToBitmap(settled, IslandTestFixtures.Scale);
        using var transitionBitmap = renderer.RenderToBitmap(transition, IslandTestFixtures.Scale);
        Assert.NotEqual(0, settledBitmap.Width);
        Assert.NotEqual(0, transitionBitmap.Width);

        var shell = IslandMetrics.ShellRect(settled);
        Assert.Equal(0, IslandTestFixtures.CountOpaqueOutside(transitionBitmap, shell, IslandTestFixtures.Scale));

        // Current contract: 6 DIP slide + 1.8% enter pose on top of the 400 DIP track translation.
        var settledPage = IslandMetrics.PageRect(settled, 0);
        var transitionPage = IslandMetrics.PageRect(transition, 0);
        Assert.Equal(0.4 * 400, settledPage.Left - transitionPage.Left, 0.01);
        var (slide, scale) = IslandRenderer.PageEnterPose(0.4, 0);
        Assert.Equal(-2.4, slide, 3);
        Assert.Equal(0.9928, scale, 3);
    }
    [Fact]
    public void Reduced_motion_removes_the_incoming_transform_but_still_renders()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = IslandTestFixtures.Expanded(1, pagePosition: 0.5) with { ReducedMotion = true };
        using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
        Assert.Equal(1895, bitmap.Width);
        Assert.True(IslandTestFixtures.CountOpaqueInside(bitmap, IslandMetrics.ShellRect(state), IslandTestFixtures.Scale) > 2000);
    }

    [Fact]
    public void Light_theme_and_simulated_glass_render_without_leaving_the_shell()
    {
        using var renderer = new IslandRenderer(IslandTheme.Light);
        var state = IslandTestFixtures.Expanded(
            0,
            theme: IslandTheme.Light with { Material = IslandMaterial.SimulatedGlass });
        using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
        Assert.Equal(0, IslandTestFixtures.CountOpaqueOutside(bitmap, IslandMetrics.ShellRect(state), IslandTestFixtures.Scale));
    }

    [Fact]
    public void Damage_rect_render_leaves_pixels_outside_the_damage_untouched()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = IslandTestFixtures.Expanded(1);
        using var bitmap = new SKBitmap(new SKImageInfo(1895, 576, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        renderer.Render(canvas, state, IslandTestFixtures.Scale);

        var damage = new SKRect(60, 150, 300, 210);
        canvas.Clear(new SKColor(0, 255, 0, 255));
        renderer.Render(canvas, state, IslandTestFixtures.Scale, damage);
        canvas.Flush();

        // Outside the damage the sentinel survives; inside it the island is repainted.
        var outside = bitmap.GetPixel(700, 500);
        Assert.Equal(new SKColor(0, 255, 0, 255), outside);
        var inside = bitmap.GetPixel((int)(120 * IslandTestFixtures.Scale), (int)(180 * IslandTestFixtures.Scale));
        Assert.NotEqual(new SKColor(0, 255, 0, 255), inside);
    }

    [Fact]
    public void Render_timings_are_tracked()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = IslandTestFixtures.Expanded(1);
        using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
        using var canvas = new SKCanvas(bitmap);
        renderer.Render(canvas, state, IslandTestFixtures.Scale);
        renderer.Render(canvas, state, IslandTestFixtures.Scale, new SKRect(60, 150, 300, 210));

        var stats = renderer.Stats;
        Assert.Equal(3, stats.Frames);
        Assert.Equal(1, stats.DirtyFrames);
        Assert.True(stats.LastMilliseconds >= 0);
        Assert.True(stats.MeanMilliseconds >= 0);
    }

    [Fact]
    public void Secondary_notification_pill_renders_below_the_primary_compact_pill()
    {
        var state = IslandTestFixtures.MediaCompact() with
        {
            SecondaryCompact = new CompactActivity(ActivityKind.Notification, new CompactModel
            {
                Title = "GitHub",
                Subtitle = "Run failed",
                AppName = "GitHub",
                UnreadCount = 2,
            }),
        };

        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var primary = IslandMetrics.ShellRect(state);
        var secondary = IslandMetrics.SecondaryShellRect(state);
        Assert.True(secondary.Top >= primary.Bottom, "the notification pill must sit below the primary pill");
        Assert.True(secondary.Bottom <= IslandMetrics.SurfaceSize(state.Mode).Height);

        var hit = renderer.HitTest(state, secondary.MidX, secondary.MidY);
        Assert.Equal(IslandHitKind.SecondaryCompact, hit.Kind);

        using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
        var pixel = bitmap.GetPixel(
            (int)(secondary.MidX * IslandTestFixtures.Scale),
            (int)(secondary.MidY * IslandTestFixtures.Scale));
        Assert.True(pixel.Alpha > 200, "the secondary pill must paint an opaque surface");
    }

    [Fact]
    public void Invalid_dpi_scale_is_rejected()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        using var bitmap = new SKBitmap(new SKImageInfo(10, 10, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.Render(canvas, IslandTestFixtures.Idle(), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.Render(canvas, IslandTestFixtures.Idle(), float.NaN));
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        renderer.Dispose();
        renderer.Dispose();
    }
}
