using FluidBar.Core.Layout;
using FluidBar.Island;

namespace FluidBar.Island.Tests;

/// <summary>
/// The lyric form of the media pill widens so a whole line fits. Its ceiling used to be the expanded shell width
/// (440 DIP), which still cut long lyric lines off; the requested ceiling is that plus one half (660 DIP).
/// </summary>
public sealed class MediaPillWidthTests
{
    private const string LongLine =
        "You've been running round, running round, running round throwing that dirt all on my name";

    [Fact]
    public void The_media_ceiling_is_the_expanded_width_plus_one_half()
    {
        Assert.Equal(LayoutTokens.ExpandedWidth * 1.5, LayoutTokens.MediaMaxWidth);
        Assert.Equal(708, LayoutTokens.MediaMaxWidth);
        Assert.True(LayoutTokens.MediaMaxWidth < LayoutTokens.NotificationMaxWidth,
            "the notification card keeps its own wider ceiling");
    }

    [Fact]
    public void A_long_lyric_line_gets_the_whole_new_budget()
    {
        Assert.Equal(708f, CompactMediaLayout.AdaptiveWidth(
            textWidth: 5000f,
            minWidth: CompactMediaLayout.MinContentWidth,
            maxWidth: (float)LayoutTokens.MediaMaxWidth));

        // The previous ceiling, for comparison: the same line stopped 220 DIP earlier.
        Assert.Equal(472f, CompactMediaLayout.AdaptiveWidth(
            textWidth: 5000f,
            minWidth: CompactMediaLayout.MinContentWidth,
            maxWidth: (float)LayoutTokens.ExpandedWidth));
    }

    [Fact]
    public void A_short_line_still_keeps_the_pill_compact()
    {
        const float Short = 10f;
        var width = CompactMediaLayout.AdaptiveWidth(
            textWidth: Short,
            minWidth: CompactMediaLayout.MinContentWidth,
            maxWidth: (float)LayoutTokens.MediaMaxWidth);

        // WidthForText adds the text width on top of the fixed slots, so a short line stays just above the floor.
        Assert.Equal(CompactMediaLayout.MinContentWidth + Short, width, 0.5f);
        Assert.True(width < LayoutTokens.MediaMaxWidth / 4, "a short line must not widen the pill");
    }

    [Fact]
    public void More_of_a_long_lyric_line_is_shown_than_before()
    {
        using var text = new TextEngine();
        var style = new TextStyle(FontRole.Ui, 13.5f);

        // Grow the fixture until it overruns the new ceiling too, so the comparison is meaningful on any font.
        var line = LongLine;
        while (text.Measure(line, style) <= LayoutTokens.MediaMaxWidth)
        {
            line += " " + LongLine;
        }

        var before = text.GetLayout(line, style, (float)LayoutTokens.ExpandedWidth);
        var after = text.GetLayout(line, style, (float)LayoutTokens.MediaMaxWidth);

        Assert.True(before.Trimmed);
        Assert.True(after.Trimmed);
        Assert.True(after.Text.Length > before.Text.Length + 10,
            $"expected noticeably more text at the new ceiling ('{after.Text}' vs '{before.Text}')");
        Assert.True(after.Width <= LayoutTokens.MediaMaxWidth + 0.5);
    }
}
