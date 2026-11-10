using FluidBar.App.Hosting.Skia;
using FluidBar.Core.Activities;
using FluidBar.Core.Layout;
using FluidBar.Island;
using FluidBar.Platform.Windows.Hosting;
using SkiaSharp;

namespace FluidBar.Ui.Tests;

public sealed class GdiGlassIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Real_gdi_rgb_stays_opaque_through_blur_and_layered_commit(bool reservedAlpha)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FLUIDBAR_HWND_TESTS") != "1") return;
        Exception? failure = null;
        using var completed = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try { RunFrame(reservedAlpha); }
            catch (Exception ex) { failure = ex; }
            finally { completed.Set(); }
        }) { IsBackground = true, Name = "FluidBar.GdiGlassTest" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(completed.Wait(TimeSpan.FromSeconds(20)), "Native glass verification timed out.");
        Assert.Null(failure);
    }

    private static void RunFrame(bool reservedAlpha)
    {
        var monitor = FluidBar.Platform.Windows.Display.MonitorInfo.Create("glass-test", 0,
            new FluidBar.Platform.Windows.Display.PixelRect(0, 0, 1920, 1080),
            new FluidBar.Platform.Windows.Display.PixelRect(0, 0, 1920, 1040), 96, true);
        using var host = new LayeredWindowHost(new LayeredHostOptions { Pacing = LayeredPacingMode.None, ReassertTopmost = false });
        host.Create(monitor, LayeredHostMode.Float);
        var window = host.Placement.WindowPixel;
        var rect = (X: window.X, Y: window.Y, Width: window.Width, Height: window.Height);
        // Exercise the actual Windows fallback, including its reserved (typically zero) alpha bytes.
        var raw = ScreenBackdropCapture.TryCapture(rect.X, rect.Y, rect.Width, rect.Height, allowComposedCapture: false);
        Assert.NotNull(raw);
        var rawAlphaMin = 255;
        var rawAlphaMax = 0;
        for (var i = 3; i < raw.Value.Pixels.Length; i += 4)
        {
            rawAlphaMin = Math.Min(rawAlphaMin, raw.Value.Pixels[i]);
            rawAlphaMax = Math.Max(rawAlphaMax, raw.Value.Pixels[i]);
            // Replay the reserved-alpha capture variant while retaining the real desktop RGB.
            if (reservedAlpha) raw.Value.Pixels[i] = 0;
        }
        using var provider = new IslandBackdropProvider { CaptureOverride = _ => raw };
        using var captured = new ManualResetEventSlim();
        provider.Updated += captured.Set;
        provider.Get(rect, force: true);
        Assert.True(captured.Wait(TimeSpan.FromSeconds(5)));
        var backdrop = provider.Get(rect);
        Assert.NotNull(backdrop);
        using var baked = SKBitmap.FromImage(backdrop);
        var bakedAlpha = baked.GetPixel(baked.Width / 2, baked.Height / 2).Alpha;
        Assert.Equal(255, bakedAlpha);

        var state = IslandRenderState.ForCompact(IslandMode.Float,
            new CompactActivity(ActivityKind.Idle, new CompactModel { ClockText = "16:00", DateText = "Glass" }),
            IslandTheme.Light.WithMaterial(IslandMaterial.LiquidGlass)) with { Backdrop = backdrop, BackdropLuminance = provider.LuminanceFor(rect) };
        using var renderer = new IslandRenderer();
        var frame = host.BeginFrame();
        using (var surface = SKSurface.Create(new SKImageInfo(frame.WidthPx, frame.HeightPx, SKColorType.Bgra8888, SKAlphaType.Premul), frame.Bits, frame.Stride))
        {
            Assert.NotNull(surface);
            renderer.Render(surface.Canvas, state, (float)frame.Scale);
            surface.Canvas.Flush();
        }
        var shell = IslandMetrics.ShellRect(state);
        var x = (int)((shell.Left + 12) * frame.Scale);
        var y = (int)(shell.MidY * frame.Scale);
        var committedAlpha = frame.Pixels[y * frame.Stride + x * 4 + 3];
        Assert.True(committedAlpha >= 250, $"Blurred desktop disappeared before native commit: {committedAlpha}.");
        var commit = host.CommitFrame();
        Assert.True(commit.Updated, $"UpdateLayeredWindow failed: {commit.Win32Error}.");
        var report = Path.Combine(FindRoot(), "build", "reports", "glass-native-diagnosis", $"gdi-contract-{reservedAlpha}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        File.WriteAllText(report, System.Text.Json.JsonSerializer.Serialize(new { rawAlphaMin, rawAlphaMax, reservedAlpha, bakedAlpha, committedAlpha, nativeCommit = commit.Updated }));
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "FluidBar.sln"))) return directory.FullName;
        throw new InvalidOperationException("Repository root missing.");
    }
}
