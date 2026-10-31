using FluidBar.Platform.Windows.Clipboard;

namespace FluidBar.Platform.Tests;

public sealed class ClipboardLongTextTests
{
    [Fact]
    public void Text_beyond_the_preview_limit_keeps_the_original_copy_payload()
    {
        var original = new string('a', 20_000) + "\ncomplete ending";
        var entry = ClipboardTextCapture.Create(1, DateTimeOffset.UnixEpoch, original, 16_384);
        Assert.Equal(16_384, entry.Text!.Length);
        Assert.Equal(original, entry.CompleteText);
        Assert.True(entry.IsTextTruncated);
        var samePrefix = ClipboardTextCapture.Create(2, DateTimeOffset.UnixEpoch, new string('a', 20_000) + "different ending", 16_384);
        Assert.False(ClipboardHistory.IsDuplicate(entry, samePrefix));
    }

    [Fact]
    public void Oversized_text_cannot_be_silently_copied_as_complete()
    {
        var original = new string('x', ClipboardTextCapture.MaxCompleteCharacters + 1);
        var entry = ClipboardTextCapture.Create(1, DateTimeOffset.UnixEpoch, original, 16_384);
        Assert.Null(entry.CompleteText);
        Assert.True(entry.IsTextTruncated);
    }

    [Fact]
    public void Retained_text_has_a_total_memory_budget()
    {
        var history = new ClipboardHistory(maxTextCharacters: 20);
        history.TryAdd(ClipboardTextCapture.Create(1, DateTimeOffset.UnixEpoch, new string('a', 15), 4), out _);
        history.TryAdd(ClipboardTextCapture.Create(2, DateTimeOffset.UnixEpoch, new string('b', 15), 4), out var snapshot);
        Assert.Equal(2u, Assert.Single(snapshot.History).Sequence);
    }

    [Fact]
    public void Blank_preview_does_not_discard_nonblank_content_after_the_preview_limit()
    {
        var original = new string(' ', 20_000) + "important ending";
        var entry = ClipboardTextCapture.Create(1, DateTimeOffset.UnixEpoch, original, 16_384);
        var history = new ClipboardHistory();
        Assert.True(history.TryAdd(entry, out var snapshot));
        Assert.Equal(original, Assert.Single(snapshot.History).CompleteText);
    }

    [Fact]
    public void Oversized_same_prefix_keeps_distinct_original_content_identities()
    {
        var prefix = new string('x', ClipboardTextCapture.MaxCompleteCharacters);
        var first = ClipboardTextCapture.Create(1, DateTimeOffset.UnixEpoch, prefix + "first", 16_384);
        var second = ClipboardTextCapture.Create(2, DateTimeOffset.UnixEpoch, prefix + "second", 16_384);
        Assert.NotNull(first.TextDigest);
        Assert.NotEqual(first.TextDigest, second.TextDigest);
        Assert.False(ClipboardHistory.IsDuplicate(first, second));
    }
}
