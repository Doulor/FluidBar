using System.Diagnostics;
using System.Text.Json;
using FluidBar.Core.Activities;
using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.App.Hosting.Skia;

/// <summary>
/// End-to-end verification dump: when <c>FLUIDBAR_VERIFY_DUMP</c> points at a directory, every rendered
/// frame (throttled to ~10 Hz) is written as a PNG plus a JSONL record, so what the island actually paints
/// can be inspected without capturing the layered window (which fails on a locked desktop).
/// </summary>
/// <remarks>Diagnostics-only: disabled unless the environment variable is set; failures are swallowed.</remarks>
internal static class IslandVerifyDump
{
    private static readonly string? DirectoryPath = Environment.GetEnvironmentVariable("FLUIDBAR_VERIFY_DUMP");
    private static readonly object Gate = new();
    private static long _lastAtTicks;
    private static int _index;

    /// <summary>Whether the dump is enabled.</summary>
    public static bool Enabled => !string.IsNullOrEmpty(DirectoryPath);

    /// <summary>Renders the frame's state to a PNG and appends its summary to frames.jsonl.</summary>
    /// <param name="renderer">Host renderer (same instance that paints the layered surface).</param>
    /// <param name="state">Render state of this frame.</param>
    /// <param name="scale">Render scale.</param>
    public static void Capture(IslandRenderer renderer, in IslandRenderState state, float scale)
    {
        if (!Enabled)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var last = Interlocked.Read(ref _lastAtTicks);
        if (last != 0 && now - last < Stopwatch.Frequency / 10)
        {
            return;
        }

        Interlocked.Exchange(ref _lastAtTicks, now);
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(DirectoryPath!);
                using var bitmap = renderer.RenderToBitmap(state, scale);
                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 90);
                var index = ++_index;
                var name = "frame-" + index.ToString("00000") + ".png";
                using (var stream = File.Create(Path.Combine(DirectoryPath!, name)))
                {
                    data.SaveTo(stream);
                }

                var compactMedia = state.Compact is { } compact && compact.Kind == FluidBar.Core.Activities.ActivityKind.Media
                ? compact.Model
                : null;
                var record = new
                {
                    at = DateTimeOffset.UtcNow,
                    file = name,
                    form = state.Form.ToString(),
                    page = state.PageIndex,
                    title = compactMedia?.Title ?? state.Pages.Media.Title,
                    compactPlaying = compactMedia?.IsActive,
                    pagePlaying = state.Pages.Media.IsPlaying,
                    pageProgress = state.Pages.Media.ProgressPercent,
                    lyricHighlight = state.Pages.Media.LyricHighlight,
                    compactLyricHighlight = compactMedia?.LyricHighlight,
                    hasCompactLyric = compactMedia is { ShowLyrics: true },
                };
                File.AppendAllText(Path.Combine(DirectoryPath!, "frames.jsonl"),
                    JsonSerializer.Serialize(record) + "\n");
            }
        }
        catch (Exception)
        {
            // Diagnostics must never break a frame.
        }
    }
}
