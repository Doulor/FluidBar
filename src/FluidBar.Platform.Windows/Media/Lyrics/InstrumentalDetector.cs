using System.Text;

namespace FluidBar.Platform.Windows.Media.Lyrics;

/// <summary>
/// Recognises instrumental tracks, so the island shows the title and artist instead of a lyric.
/// </summary>
/// <remarks>
/// <para>An instrumental has nothing to sing, but the catalogue search happily matches its title to some
/// other song and the island then scrolls a stranger's words (user report 2026-10-22: "纯音乐会随机取一个
/// 别的歌的歌词"). Two independent signals are used:</para>
/// <list type="bullet">
/// <item>the title carries an instrumental marker ("纯音乐", "(Instrumental)", "伴奏", "Off Vocal"…);</item>
/// <item>the lyric the player itself downloaded is a placeholder ("纯音乐，请欣赏" / "此歌曲为没有填词的纯音乐"),
/// which is what KuGou ships for instrumental tracks.</item>
/// </list>
/// <para>Deliberately conservative: an unknown track without either signal keeps its lyrics. A false
/// positive hides real lyrics, which is worse than showing a placeholder line.</para>
/// </remarks>
public static class InstrumentalDetector
{
    /// <summary>Markers that only appear on instrumental releases.</summary>
    private static readonly string[] TitleMarkers =
    [
        "纯音乐", "純音樂", "纯音", "伴奏", "无人声", "無人聲", "演奏版", "音乐盒", "音樂盒",
        "instrumental", "off vocal", "offvocal", "backing track", "karaoke", "カラオケ", "inst.", "inst版",
    ];

    /// <summary>Credit lines ("作词：…", "Lyrics by …"): metadata, not something anybody sings.</summary>
    private static readonly string[] CreditMarkers =
    [
        "作词", "作曲", "编曲", "制作人", "监制", "出品", "录音", "混音", "母带", "发行", "和声", "演奏", "吉他",
        "贝斯", "鼓手", "弦乐", "钢琴", "词：", "曲：", "lyrics by", "composed by", "arranged by", "produced by",
        "written by", "music by", "composer", "lyricist", "remix", "mixing", "mastering", "vocal", "instrumental",
    ];

    /// <summary>Fragments of the placeholder line KuGou writes for tracks without words.</summary>
    private static readonly string[] PlaceholderFragments =
    [
        "纯音乐", "純音樂", "请欣赏", "請欣賞", "没有填词", "沒有填詞", "没有歌词", "沒有歌詞",
        "instrumental", "no lyrics", "lyrics not available", "暂无歌词", "暫無歌詞",
    ];

    /// <summary>True when the track title marks an instrumental release.</summary>
    public static bool LooksInstrumental(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        var normalized = title.ToLowerInvariant();
        foreach (var marker in TitleMarkers)
        {
            if (normalized.Contains(marker, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when the document is only a placeholder ("纯音乐，请欣赏"). Such a document carries no lyrics to
    /// scroll, and its single timed line would otherwise be painted as if it were the song.
    /// </summary>
    public static bool IsPlaceholder(LyricsDocument? document)
    {
        if (document is null || document.IsEmpty)
        {
            return false;
        }

        // Every line must be non-singable (placeholder or credit). One real line is enough to keep the
        // document: a placeholder has one or two lines, a credit-only file has a handful.
        foreach (var line in document.Lines)
        {
            if (!IsPlaceholderLine(line.Text) && !IsCreditLine(line.Text))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True when the line is a credit ("作词：张三"), not a lyric.</summary>
    private static bool IsCreditLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var normalized = text.Trim().ToLowerInvariant();
        foreach (var marker in CreditMarkers)
        {
            if (normalized.Contains(marker, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPlaceholderLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var normalized = text.Trim().ToLowerInvariant();
        if (normalized.Length > 40)
        {
            return false;
        }

        foreach (var fragment in PlaceholderFragments)
        {
            if (normalized.Contains(fragment, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
