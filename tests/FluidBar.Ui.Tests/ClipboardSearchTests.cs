using System.Collections.Immutable;
using FluidBar.App.Hosting.Skia;
using FluidBar.Platform.Windows.Clipboard;

namespace FluidBar.Ui.Tests;

/// <summary>Clipboard text search is pure and case-insensitive across text and file-name rows.</summary>
public sealed class ClipboardSearchTests
{
    [Fact]
    public void Matches_text_and_file_names_case_insensitively()
    {
        var text = new ClipboardEntry(
            1,
            DateTimeOffset.UtcNow,
            ClipboardContentKind.Text,
            "Hello FluidBar clipboard",
            24,
            ImmutableArray<string>.Empty);
        var files = new ClipboardEntry(
            2,
            DateTimeOffset.UtcNow,
            ClipboardContentKind.Files,
            null,
            0,
            ImmutableArray.Create(@"C:\Docs\Report.pdf"));

        Assert.True(SkiaPlatformActions.MatchesClipboardSearch(text, "fluidbar"));
        Assert.False(SkiaPlatformActions.MatchesClipboardSearch(text, "missing"));
        Assert.True(SkiaPlatformActions.MatchesClipboardSearch(files, "report"));
        Assert.False(SkiaPlatformActions.MatchesClipboardSearch(files, "fluidbar"));
    }

    [Fact]
    public void Empty_query_matches_every_row()
    {
        var image = new ClipboardEntry(
            3,
            DateTimeOffset.UtcNow,
            ClipboardContentKind.Image,
            null,
            0,
            ImmutableArray<string>.Empty);

        Assert.True(SkiaPlatformActions.MatchesClipboardSearch(image, null));
        Assert.True(SkiaPlatformActions.MatchesClipboardSearch(image, "  "));
    }
}
