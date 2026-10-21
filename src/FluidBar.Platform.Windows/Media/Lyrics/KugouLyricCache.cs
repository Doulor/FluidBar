using System.Diagnostics;
using System.Text;
using Microsoft.Win32;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FluidBar.Platform.Windows.Media.Lyrics;

/// <summary>
/// Finds the lyric file KuGou itself uses for a track, in the player's own <c>Lyric</c> folder.
/// </summary>
/// <remarks>
/// <para>The folder holds <c>&lt;artist&gt; - &lt;title&gt;-&lt;32 hex&gt;-&lt;songId&gt;-00000000.krc</c> files, i.e. exactly the
/// lyric the player shows for that song. Matching by file name avoids the fuzzy catalogue search that
/// returned other songs' lyrics, and the KRC carries per-word timings, so the island's sweep is the
/// player's own timeline instead of an interpolation.</para>
/// <para>Read-only. A missing folder, an unreadable file or an undecodable blob simply returns null and
/// the caller falls back to the online providers.</para>
/// </remarks>
public static partial class KugouLyricCache
{
    private static readonly object Gate = new();
    private static Dictionary<string, string> _index = new(StringComparer.Ordinal);
    private static DateTime _stamp = DateTime.MinValue;
    private static string? _root;

    /// <summary>Looks up and decodes the player's lyric file for a track.</summary>
    /// <param name="title">Track title as the player reports it.</param>
    /// <param name="artist">Track artist as the player reports it.</param>
    public static LyricsDocument? TryFind(string? title, string? artist)
    {
        // The window scrape often carries the player's own chrome ("正在播放: 歌手 - 歌名"), which would
        // otherwise be part of the key and never match a file name. Reuse the same cleanup the cover
        // lookup uses (it also splits "artist - title" when the artist field is empty).
        var (cleanTitle, cleanArtist) = ArtworkSearchTerms.Split(title, artist);
        var wantedTitle = Normalize(cleanTitle);
        if (wantedTitle.Length == 0)
        {
            return null;
        }

        var wantedArtist = Normalize(cleanArtist);
        string? path = null;
        lock (Gate)
        {
            Refresh();
            // KuGou names its files "artist - title", but the scrape often yields the two halves in the
            // other order ("… - 爱如枯槁" where the last part is the TITLE). Try both concatenations and
            // then a contains-both fallback (user report: 爱如枯槁 existed locally yet was not found).
            string? hit = null;
            if (wantedArtist.Length > 0)
            {
                _index.TryGetValue(wantedArtist + wantedTitle, out hit);
                hit ??= _index.TryGetValue(wantedTitle + wantedArtist, out var swapped) ? swapped : null;
            }

            if (hit is null)
            {
                foreach (var pair in _index)
                {
                    var both = wantedArtist.Length == 0
                        ? pair.Key.EndsWith(wantedTitle, StringComparison.Ordinal)
                        : pair.Key.Contains(wantedTitle, StringComparison.Ordinal)
                          && pair.Key.Contains(wantedArtist, StringComparison.Ordinal);
                    if (both)
                    {
                        hit = pair.Value;
                        break;
                    }
                }
            }

            path = hit;
        }

        if (path is null)
        {
            return null;
        }

        try
        {
            var decoded = KrcDecoder.TryDecode(File.ReadAllBytes(path));
            return decoded?.Document;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void Refresh()
    {
        var root = _root ??= ResolveRoot();
        if (root is null)
        {
            _index = new Dictionary<string, string>(StringComparer.Ordinal);
            return;
        }

        DateTime stamp;
        try
        {
            stamp = Directory.GetLastWriteTimeUtc(root);
        }
        catch (IOException)
        {
            return;
        }

        if (stamp == _stamp && _index.Count > 0)
        {
            return;
        }

        _stamp = stamp;
        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.krc"))
            {
                var key = Normalize(FileNameRegex().Replace(Path.GetFileNameWithoutExtension(file), string.Empty));
                if (key.Length > 0)
                {
                    index[key] = file;
                }
            }
        }
        catch (IOException)
        {
            return;
        }

        _index = index;
    }

    /// <summary>Locates KuGou's lyric folder: the player's own install directory, then the usual roots.</summary>
    private static string? ResolveRoot()
    {
        var configured = Environment.GetEnvironmentVariable("FLUIDBAR_KUGOU_LYRIC_DIR");
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
        {
            return configured;
        }

        var candidates = new List<string>();
        try
        {
            foreach (var process in Process.GetProcessesByName("KuGou"))
            {
                var directory = Path.GetDirectoryName(process.MainModule?.FileName);
                if (!string.IsNullOrEmpty(directory))
                {
                    candidates.Add(Path.Combine(directory, "Lyric"));
                }
            }
        }
        catch (Exception)
        {
            // MainModule throws for a 32/64-bit mismatch or a protected process: fall through to the roots.
        }

        // The install location differs on every machine ("D:\KuGou", "C:\Program Files\KuGou", portable
        // copies…), so ask the uninstall registry keys next.
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var path in new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\KuGou",
            })
            {
                try
                {
                    using var key = hive.OpenSubKey(path);
                    if (key is null) continue;
                    foreach (var name in key.GetSubKeyNames())
                    {
                        using var sub = key.OpenSubKey(name);
                        var location = sub?.GetValue("InstallLocation") as string
                                       ?? sub?.GetValue("InstallPath") as string
                                       ?? sub?.GetValue("Path") as string;
                        if (!string.IsNullOrWhiteSpace(location))
                        {
                            candidates.Add(Path.Combine(location, "Lyric"));
                        }
                    }
                }
                catch (Exception)
                {
                    // Unreadable hive / missing value: keep looking.
                }
            }
        }

        // Last resort: the usual top-level folders on every fixed drive.
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
            var top = drive.RootDirectory.FullName;
            candidates.Add(Path.Combine(top, "KuGou", "Lyric"));
            candidates.Add(Path.Combine(top, "Program Files", "KuGou", "Lyric"));
            candidates.Add(Path.Combine(top, "Program Files (x86)", "KuGou", "Lyric"));
            try
            {
                foreach (var directory in Directory.EnumerateDirectories(top, "KuGou*", SearchOption.TopDirectoryOnly))
                {
                    candidates.Add(Path.Combine(directory, "Lyric"));
                }
            }
            catch (Exception)
            {
                // Drive not enumerable: skip it.
            }
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        candidates.Add(Path.Combine(local, "KuGou8", "Lyric"));
        candidates.Add(Path.Combine(local, "KuGou", "Lyric"));

        return candidates.FirstOrDefault(Directory.Exists);
    }

    /// <summary>Lower-case, punctuation-free form used for both the file names and the track.</summary>
    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    /// <summary>Strips the <c>-&lt;hash&gt;-&lt;songId&gt;-&lt;flag&gt;</c> suffix the player appends.</summary>
    [GeneratedRegex(@"\s*-[0-9a-fA-F]{32}-\d+-\d+$")]
    private static partial Regex FileNameRegex();
}
