using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace FluidBar.Platform.Windows.Clipboard;

internal static class ClipboardTextCapture
{
    internal const int MaxCompleteCharacters = 1_048_576;

    internal static ClipboardEntry Create(uint sequence, DateTimeOffset timestamp, string text, int maxPreviewCharacters)
    {
        var previewLength = Math.Min(text.Length, Math.Max(1, maxPreviewCharacters));
        var preview = text.Length > previewLength ? text[..previewLength] : text;
        var full = text.Length > previewLength && text.Length <= MaxCompleteCharacters ? text : null;
        var digest = text.Length > MaxCompleteCharacters && !string.IsNullOrWhiteSpace(text)
            ? Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(text.AsSpan()))) : null;
        return new ClipboardEntry(sequence, timestamp, ClipboardContentKind.Text, preview, text.Length,
            ImmutableArray<string>.Empty, FullText: full, TextDigest: digest);
    }
}
