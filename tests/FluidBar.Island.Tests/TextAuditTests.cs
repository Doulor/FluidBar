using FluidBar.Island;
using SkiaSharp;
using Xunit;

namespace FluidBar.Island.Tests;

/// <summary>
/// Guards for the UI text audit (task-33): contrast of tertiary copy, the home page's scroll path at
/// 1.6, and rail labels rendered at every font scale/language.
/// </summary>
public sealed class TextAuditTests
{
    private const float DpiScale = 1.5f;

    [Fact]
    public void Tertiary_text_meets_wcag_aa_on_shell_and_raised_cards()
    {
        var theme = IslandTestFixtures.Theme;
        var onShell = Contrast(theme.ForegroundTertiary, theme.Background);
        var onRaised = Contrast(theme.ForegroundTertiary, theme.BackgroundRaised);

        // The prototype's #6E6E78 was 3.97:1 on the shell and 3.64:1 on raised cards; the audit fixed
        // it to #80808A. 4.5:1 is WCAG AA for normal-size text.
        Assert.True(onShell >= 4.5, $"tertiary on shell was {onShell:F2}:1");
        Assert.True(onRaised >= 4.5, $"tertiary on raised was {onRaised:F2}:1");
    }

    [Fact]
    public void Home_scrolled_golden_reveals_the_battery_row_at_1_6()
    {
        var baseline = IslandTestFixtures.GoldenState("42-expanded-home-fs16");
        var scrolled = IslandTestFixtures.GoldenState("60-expanded-home-fs16-scrolled");
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        using var before = renderer.RenderToBitmap(baseline, DpiScale);
        using var after = renderer.RenderToBitmap(scrolled, DpiScale);

        var metrics = FontScaleMetrics.From(1.6);
        var page = IslandMetrics.PageRect(scrolled, 0);
        var scroll = PageMetricsLayout.MaxScroll(page, 0, metrics);
        Assert.True(scroll > 0, "the 1.6 home stack must overflow the 232 DIP body");

        // The scrolled state uses ScrollOffset=9999 (clamped by the renderer); if DrawHome ignored it
        // the two frames were identical and the battery row stayed off-screen.
        var lowerBody = PageMetricsLayout.Body(page);
        lowerBody = new SKRect(lowerBody.Left, lowerBody.Top + (lowerBody.Height * 0.55f), lowerBody.Right, lowerBody.Bottom);
        var changed = CountDifferentPixels(before, after, lowerBody);
        Assert.True(changed > 500, $"scrolled home changed only {changed} lower-body pixels");

        var battery = PageMetricsLayout.HomeBattery(page, metrics);
        battery.Offset(0, -Math.Min(9999f, scroll));
        var lights = CountBrightPixels(after, battery);
        Assert.True(lights > 10,
            $"battery lights={lights}; page={page}; scroll={scroll}; battery={battery}; changed={changed}");
    }

    [Fact]
    public void Audit_frames_render_every_page_at_1_6()
    {
        var dir = Path.Combine(IslandTestFixtures.RepoRoot, "build", "reports", "ui-audit", "final");
        Directory.CreateDirectory(dir);
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var names = new[] { "home", "media", "notifications", "clipboard", "agent", "widgets" };
        for (var page = 0; page < names.Length; page++)
        {
            var state = IslandTestFixtures.WithFontScale(IslandTestFixtures.Expanded(page), 1.6);
            if (page == 1)
            {
                state = state with
                {
                    Pages = state.Pages with
                    {
                        Media = state.Pages.Media with
                        {
                            OutputAvailable = false,
                            QueueAvailable = false,
                            OutputPlaceholder = "No output",
                            QueuePlaceholder = "No queue",
                        },
                    },
                };
            }

            using var bitmap = renderer.RenderToBitmap(state, DpiScale);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.Create(Path.Combine(dir, $"{names[page]}-fs16.png"));
            data.SaveTo(stream);
        }
    }

    [Theory]
    [InlineData(0.8, "en")]
    [InlineData(1.0, "en")]
    [InlineData(1.6, "en")]
    [InlineData(0.8, "zh")]
    [InlineData(1.0, "zh")]
    [InlineData(1.6, "zh")]
    public void Rail_labels_paint_inside_the_capsule_at_every_scale(double fontScale, string language)
    {
        var state = language == "zh"
            ? IslandTestFixtures.WithFontScale(IslandTestFixtures.RailState(2), fontScale)
            : IslandTestFixtures.WithFontScale(IslandTestFixtures.Expanded(2, strip: true), fontScale);
        var page = state.Archipelago.Rail.Active ? state.Archipelago.Rail.Page : 2;
        var label = language == "zh"
            ? IslandTestFixtures.ZhShortTitles[page]
            : state.Pages.ShortTitles[page];
        var capsule = MiniRailLayout.CapsuleWidth(label, fontScale);
        Assert.InRange(capsule, 64f, 104f);

        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        using var bitmap = renderer.RenderToBitmap(state, DpiScale);
        var dir = Path.Combine(IslandTestFixtures.RepoRoot, "build", "reports", "ui-audit", "rail");
        Directory.CreateDirectory(dir);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(Path.Combine(dir, $"rail-{language}-{fontScale.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}.png"));
        data.SaveTo(stream);
        Assert.True(new FileInfo(stream.Name).Length > 0);
    }

    private static int CountDifferentPixels(SKBitmap before, SKBitmap after, SKRect dipRect)
    {
        var left = Math.Max(0, (int)Math.Floor(dipRect.Left * DpiScale));
        var right = Math.Min(before.Width, (int)Math.Ceiling(dipRect.Right * DpiScale));
        var top = Math.Max(0, (int)Math.Floor(dipRect.Top * DpiScale));
        var bottom = Math.Min(before.Height, (int)Math.Ceiling(dipRect.Bottom * DpiScale));
        var count = 0;
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var a = before.GetPixel(x, y);
                var b = after.GetPixel(x, y);
                if (Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue) > 45)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int CountBrightPixels(SKBitmap bitmap, SKRect dipRect)
    {
        var left = Math.Max(0, (int)Math.Floor(dipRect.Left * DpiScale));
        var right = Math.Min(bitmap.Width, (int)Math.Ceiling(dipRect.Right * DpiScale));
        var top = Math.Max(0, (int)Math.Floor(dipRect.Top * DpiScale));
        var bottom = Math.Min(bitmap.Height, (int)Math.Ceiling(dipRect.Bottom * DpiScale));
        var count = 0;
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var c = bitmap.GetPixel(x, y);
                if (c.Red + c.Green + c.Blue > 300)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static double Contrast(SKColor foreground, SKColor background)
    {
        var a = Luminance(foreground);
        var b = Luminance(background);
        var hi = Math.Max(a, b);
        var lo = Math.Min(a, b);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static double Luminance(SKColor color)
    {
        static double Channel(byte value)
        {
            var v = value / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.Red)) + (0.7152 * Channel(color.Green)) + (0.0722 * Channel(color.Blue));
    }
}
