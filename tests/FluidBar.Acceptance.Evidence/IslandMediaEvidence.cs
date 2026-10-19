using FluidBar.App.Localization;
using FluidBar.Core.Layout;
using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Acceptance.Evidence;

/// <summary>
/// Renders the production island media page in its empty-state combinations so the acceptance review can
/// check the output/queue placeholders and the no-lyrics band. Uses the real <see cref="IslandRenderer"/>
/// and the real catalogs; the track values are the same sample values as the committed island goldens
/// (this is a layout-state capture, never runtime measurement data).
/// </summary>
internal static class IslandMediaEvidence
{
    /// <summary>Captures the media empty-state combinations for one UI culture.</summary>
    public static object Capture(string output, string culture)
    {
        var localizer = LocalizationService.CreateDefault(culture, showMissingKeys: false, enablePseudoLocale: false);
        var screenshots = Path.Combine(output, "screenshots");
        Directory.CreateDirectory(screenshots);

        var pages = new ExpandedPageContent
        {
            PageTitles =
            [
                localizer.Get(LocKeys.Page.Home.Title),
                localizer.Get(LocKeys.Page.Media.Title),
                localizer.Get(LocKeys.Page.Notifications.Title),
                localizer.Get(LocKeys.Page.Clipboard.Title),
                localizer.Get(LocKeys.Page.Agent.Title),
                localizer.Get(LocKeys.Page.Widgets.Title),
            ],
        };
        var media = new MediaPageModel
        {
            HasSession = true,
            Eyebrow = localizer.Get(LocKeys.Media.NowPlaying),
            Title = "Midnight City",
            Artist = "M83",
            Album = "Hurry Up, We're Dreaming",
            LyricLine = "Waiting for a car to drive me home",
            NextLyricLine = "The city is my church",
            NextLyricLabel = localizer.Get(LocKeys.Common.Next),
            PositionText = "1:52",
            DurationText = "2:11",
            ProgressPercent = 46,
            IsPlaying = true,
            AccentHex = "#F06AB0",
            OutputName = "Speakers",
            QueueCount = 3,
            OutputPlaceholder = localizer.Get(LocKeys.Page.Media.OutputNone),
            QueuePlaceholder = localizer.Get(LocKeys.Page.Media.QueueNone),
            QueueLabel = localizer.Get(LocKeys.Page.Media.QueueLabel),
            NoLyricsText = localizer.Get(LocKeys.Page.Media.NoLyrics),
        };

        var variants = new (string Name, MediaPageModel Media)[]
        {
            ("with-data", media),
            ("no-output", media with { OutputName = null }),
            ("no-queue", media with { QueueCount = 0 }),
            ("no-lyrics", media with { LyricLine = null, NextLyricLine = null, NextLyricLabel = null }),
            ("no-artwork", media with { Artwork = null, ArtworkKey = null, AccentHex = null }),
            ("all-empty", media with
            {
                LyricLine = null,
                NextLyricLine = null,
                NextLyricLabel = null,
                OutputName = null,
                QueueCount = 0,
                Artwork = null,
                ArtworkKey = null,
                AccentHex = null,
            }),
        };

        using var renderer = new IslandRenderer(IslandTheme.Dark);
        var captured = new List<object>();
        foreach (var variant in variants)
        {
            var state = IslandRenderState.Expanded(
                IslandMode.Float,
                PagerRenderState.At(1),
                pages with { Media = variant.Media },
                IslandTheme.Dark);
            using var bitmap = renderer.RenderToBitmap(state, 1.5f);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            var path = Path.Combine(screenshots, $"island-media-{culture}-{variant.Name}.png");
            using (var stream = File.Create(path))
            {
                data.SaveTo(stream);
            }

            captured.Add(new { culture, variant = variant.Name, path, width = bitmap.Width, height = bitmap.Height, bytes = new FileInfo(path).Length });
        }

        return captured;
    }
}
