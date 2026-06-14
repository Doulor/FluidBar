using System.IO;

namespace FluidBar;

public interface ILyricsProvider
{
    string? TryGetCurrentLine(MediaSnapshot snapshot, TimeSpan position);
}

public sealed class NullLyricsProvider : ILyricsProvider
{
    public string? TryGetCurrentLine(MediaSnapshot snapshot, TimeSpan position) => null;
}

public sealed class LocalLrcLyricsProvider : ILyricsProvider
{
    private readonly string _lyricsDirectory;

    public LocalLrcLyricsProvider(string lyricsDirectory)
    {
        _lyricsDirectory = lyricsDirectory;
    }

    public string? TryGetCurrentLine(MediaSnapshot snapshot, TimeSpan position)
    {
        if (string.IsNullOrWhiteSpace(snapshot.Title) || !Directory.Exists(_lyricsDirectory))
            return null;

        var safeTitle = string.Join("_", snapshot.Title.Split(Path.GetInvalidFileNameChars()));
        var path = Path.Combine(_lyricsDirectory, safeTitle + ".lrc");
        if (!File.Exists(path))
            return null;

        string? current = null;
        foreach (var line in File.ReadLines(path))
        {
            var parsed = TryParseLine(line);
            if (parsed is null)
                continue;
            if (parsed.Value.Time <= position)
                current = parsed.Value.Text;
            else
                break;
        }

        return current;
    }

    private static (TimeSpan Time, string Text)? TryParseLine(string line)
    {
        if (line.Length < 10 || line[0] != '[')
            return null;

        var close = line.IndexOf(']', StringComparison.Ordinal);
        if (close <= 0)
            return null;

        var timestamp = line[1..close];
        var parts = timestamp.Split(':');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out var minutes) ||
            !double.TryParse(parts[1], out var seconds))
        {
            return null;
        }

        return (TimeSpan.FromSeconds(minutes * 60 + seconds), line[(close + 1)..].Trim());
    }
}

