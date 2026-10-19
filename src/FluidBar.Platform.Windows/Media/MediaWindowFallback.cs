using System.Diagnostics;
using FluidBar.Platform.Windows.Interop;

namespace FluidBar.Platform.Windows.Media;

/// <summary>One audio session that is currently producing sound.</summary>
/// <param name="ProcessId">Owning process id.</param>
/// <param name="Peak">Instantaneous peak level 0..1 (0 when the meter is unavailable).</param>
internal readonly record struct AudioSessionInfo(int ProcessId, float Peak);

/// <summary>
/// Enumerates the per-process audio sessions of the default render endpoint (WASAPI). Used only by the
/// non-SMTC media fallback: it answers "which player is actually making sound right now".
/// </summary>
internal static class AudioSessionProbe
{
    private const uint ClsCtxAll = 23;

    /// <summary>
    /// Collects the active render sessions. Must be called from an MTA thread (the fallback poller is
    /// MTA by construction). Returns false when the endpoint/session manager is unavailable.
    /// </summary>
    public static bool TryGetActiveRenderSessions(out List<AudioSessionInfo> sessions)
    {
        sessions = [];
        object? managerObject = null;
        try
        {
            var enumerator = (AudioInterop.IMMDeviceEnumerator)(object)new AudioInterop.MMDeviceEnumeratorComObject();
            if (enumerator.GetDefaultAudioEndpoint(AudioInterop.EDataFlowRender, AudioInterop.ERoleConsole,
                    out var device) != 0 || device is null)
            {
                return false;
            }

            var iid = AudioInterop.IIdAudioSessionManager2;
            if (device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out managerObject) != 0
                || managerObject is not AudioInterop.IAudioSessionManager2 manager)
            {
                return false;
            }

            if (manager.GetSessionEnumerator(out var sessionEnumerator) != 0 || sessionEnumerator is null)
            {
                return false;
            }

            if (sessionEnumerator.GetCount(out var count) != 0)
            {
                return false;
            }

            for (var i = 0; i < count; i++)
            {
                if (sessionEnumerator.GetSession(i, out var control) != 0 || control is null)
                {
                    continue;
                }

                if (control.GetState(out var state) != 0 || state != AudioInterop.AudioSessionStateActive)
                {
                    continue;
                }

                if (control is not AudioInterop.IAudioSessionControl2 control2
                    || control2.GetProcessId(out var processId) != 0
                    || processId == 0
                    || processId == Environment.ProcessId)
                {
                    continue;
                }

                var peak = 0f;
                if (control is AudioInterop.IAudioMeterInformation meter && meter.GetPeakValue(out var value) == 0)
                {
                    peak = value;
                }

                sessions.Add(new AudioSessionInfo((int)processId, peak));
            }

            return true;
        }
        catch (Exception)
        {
            // The fallback is best-effort: a COM hiccup must never affect the GSMTC path.
            return false;
        }
        finally
        {
            if (managerObject is not null && System.Runtime.InteropServices.Marshal.IsComObject(managerObject))
            {
                try
                {
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(managerObject);
                }
                catch (Exception)
                {
                    // Already released.
                }
            }
        }
    }
}

/// <summary>Media snapshot derived from a player window title (players without SMTC/GSMTC support).</summary>
/// <param name="ProcessName">Player process name (lower-case, for diagnostics).</param>
/// <param name="Title">Track title parsed from the window title.</param>
/// <param name="Artist">Artist/playlist line parsed from the window title (may be empty).</param>
/// <param name="TrackKey">Stable key for caches.</param>
internal sealed record WindowMediaCandidate(string ProcessName, string Title, string Artist, string TrackKey);

/// <summary>
/// Fallback media source for players that never register with Windows SMTC (e.g. KuGou). It finds the
/// process that is actively producing sound, reads its main window title and parses the usual
/// "playlist - song - app" / "song - artist - app" formats. No artwork or timeline is available on
/// this path, so the island must render it as "unknown progress" instead of inventing values.
/// </summary>
internal static class MediaWindowFallback
{
    private sealed record PlayerRule(string Process, string[] AppSuffixes, bool PlaylistFirst);

    private static readonly PlayerRule[] Rules =
    [
        new("kugou", ["酷狗音乐", "酷狗"], PlaylistFirst: true),
        new("kgmusic", ["酷狗音乐", "酷狗"], PlaylistFirst: true),
        new("qqmusic", ["QQ音乐", "QQ 音乐"], PlaylistFirst: false),
        new("cloudmusic", ["网易云音乐", "网易云"], PlaylistFirst: false),
        new("netease", ["网易云音乐"], PlaylistFirst: false),
        new("kwmusic", ["酷我音乐", "酷我"], PlaylistFirst: false),
        new("kuwo", ["酷我音乐", "酷我"], PlaylistFirst: false),
        new("spotify", ["Spotify"], PlaylistFirst: false),
        new("foobar2000", ["foobar2000"], PlaylistFirst: false),
        new("aimp", ["AIMP"], PlaylistFirst: false),
        new("musicbee", ["MusicBee"], PlaylistFirst: false),
    ];

    /// <summary>Finds the loudest active known player and parses its window title, or null.</summary>
    public static WindowMediaCandidate? FindCandidate()
    {
        var ok = AudioSessionProbe.TryGetActiveRenderSessions(out var sessions);
        if (!ok || sessions.Count == 0)
        {
            return null;
        }

        foreach (var session in sessions.OrderByDescending(static s => s.Peak))
        {
            Process process;
            try
            {
                process = Process.GetProcessById(session.ProcessId);
            }
            catch (Exception)
            {
                continue;
            }

            var processName = process.ProcessName;
            if (FindRule(processName) is not { } rule)
            {
                continue;
            }

            string windowTitle;
            try
            {
                windowTitle = process.MainWindowTitle;
            }
            catch (Exception)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(windowTitle))
            {
                continue;
            }

            var candidate = Parse(processName, windowTitle);
            if (candidate is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Parses a player window title into title/artist (exposed for tests).</summary>
    public static WindowMediaCandidate? Parse(string processName, string windowTitle)
    {
        if (string.IsNullOrWhiteSpace(processName) || string.IsNullOrWhiteSpace(windowTitle))
        {
            return null;
        }

        var rule = FindRule(processName);
        var parts = windowTitle.Split(" - ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        if (rule is not null && parts.Length > 1
            && rule.AppSuffixes.Any(suffix => parts[^1].Contains(suffix, StringComparison.OrdinalIgnoreCase)))
        {
            parts = parts[..^1];
        }

        string title;
        string artist;
        if (parts.Length >= 2 && rule is { PlaylistFirst: true })
        {
            artist = parts[0];
            title = string.Join(" - ", parts[1..]);
        }
        else if (parts.Length >= 2)
        {
            title = parts[0];
            artist = string.Join(" - ", parts[1..]);
        }
        else
        {
            title = parts[0];
            artist = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        // A title that is only the player name carries no track information.
        if (rule is not null
            && rule.AppSuffixes.Any(suffix => title.Equals(suffix, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return new WindowMediaCandidate(
            processName.ToLowerInvariant(),
            title,
            artist,
            "win|" + processName.ToLowerInvariant() + "|" + title + "|" + artist);
    }

    private static PlayerRule? FindRule(string processName)
    {
        foreach (var rule in Rules)
        {
            if (processName.Contains(rule.Process, StringComparison.OrdinalIgnoreCase))
            {
                return rule;
            }
        }

        return null;
    }
}
