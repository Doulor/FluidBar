using System.Collections.Immutable;
using FluidBar.Platform.Windows.Clipboard;

namespace FluidBar.Platform.Tests;

public sealed class ClipboardQualityTests
{
    private static ClipboardEntry Text(uint sequence, string? text)
        => new(sequence, DateTimeOffset.UnixEpoch.AddSeconds(sequence), ClipboardContentKind.Text,
            text, text?.Length ?? 0, ImmutableArray<string>.Empty);

    [Fact]
    public void Repeated_payload_moves_to_front_with_new_capture_metadata()
    {
        var history = new ClipboardHistory();
        history.TryAdd(Text(1, "first"), out _);
        history.TryAdd(Text(2, "second"), out _);

        Assert.True(history.TryAdd(Text(3, "first"), out var snapshot));

        Assert.Equal(new uint[] { 3, 2 }, snapshot.History.Select(entry => entry.Sequence));
        Assert.Equal(0, history.Evictions);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \r\n\t")]
    public void Empty_text_never_enters_history(string? value)
    {
        var history = new ClipboardHistory();
        Assert.False(history.TryAdd(Text(1, value), out var snapshot));
        Assert.Empty(snapshot.History);
    }

    [Fact]
    public void Repeated_image_payload_is_deduplicated_across_history()
    {
        var history = new ClipboardHistory();
        var image = new ClipboardEntry(1, DateTimeOffset.UnixEpoch, ClipboardContentKind.Image,
            null, 0, ImmutableArray<string>.Empty, [1, 2, 3]);
        history.TryAdd(image, out _);
        history.TryAdd(Text(2, "middle"), out _);

        Assert.True(history.TryAdd(image with { Sequence = 3, ImageDib = [1, 2, 3] }, out var snapshot));
        Assert.Equal(new uint[] { 3, 2 }, snapshot.History.Select(entry => entry.Sequence));
    }

    [Fact]
    public void Paused_capture_does_not_read_payload_and_resume_skips_the_paused_sequence()
    {
        var type = typeof(ClipboardService).Assembly.GetType("FluidBar.Platform.Windows.Clipboard.ClipboardCaptureGate");
        Assert.NotNull(type);
        var gate = Activator.CreateInstance(type!);
        var setPaused = type!.GetMethod("SetPaused")!;
        var capture = type.GetMethod("TryRead")!;
        var reads = 0;
        Func<bool> read = () => { reads++; return true; };
        setPaused.Invoke(gate, [true, (Func<uint>)(() => 1)]);
        Assert.Equal(false, capture.Invoke(gate, [2u, false, read]));
        Assert.Equal(0, reads);

        setPaused.Invoke(gate, [false, (Func<uint>)(() => 2)]);
        Assert.Equal(false, capture.Invoke(gate, [2u, false, read]));
        Assert.Equal(true, capture.Invoke(gate, [3u, false, read]));
        Assert.Equal(1, reads);
    }

    [Fact]
    public void Failed_open_can_retry_the_same_sequence()
    {
        var type = typeof(ClipboardService).Assembly.GetType("FluidBar.Platform.Windows.Clipboard.ClipboardCaptureGate");
        Assert.NotNull(type);
        var gate = Activator.CreateInstance(type!);
        var capture = type!.GetMethod("TryRead")!;
        Assert.Equal(false, capture.Invoke(gate, [1u, false, (Func<bool>)(() => false)]));
        Assert.Equal(true, capture.Invoke(gate, [1u, false, (Func<bool>)(() => true)]));
    }
}
