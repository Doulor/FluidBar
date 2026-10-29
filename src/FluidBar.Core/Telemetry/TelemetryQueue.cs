using System.Globalization;

namespace FluidBar.Core.Telemetry;

/// <summary>
/// Durable, bounded local queue of finished sessions waiting to be uploaded.
/// </summary>
/// <remarks>
/// One tab-separated line per run keeps the file readable (so a user can inspect what is pending) and avoids a
/// serializer that could quietly grow a new field. The queue is capped: a permanently offline machine drops its
/// oldest runs instead of filling the disk.
/// </remarks>
public sealed class TelemetryQueue
{
    private const char Separator = '\t';
    private readonly string _path;
    private readonly int _capacity;

    /// <summary>Creates a queue backed by <paramref name="path"/>.</summary>
    /// <param name="path">Absolute path of the queue file (created on demand).</param>
    /// <param name="capacity">Maximum runs kept before the oldest are dropped.</param>
    public TelemetryQueue(string path, int capacity = 256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _path = path;
        _capacity = capacity;
    }

    /// <summary>Runs currently waiting for upload.</summary>
    public int Count => ReadAll().Count;

    /// <summary>Appends one finished session, dropping the oldest entries past the capacity.</summary>
    /// <param name="run">Session to enqueue.</param>
    public void Enqueue(TelemetryRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        EnqueueRange([run]);
    }

    /// <summary>Appends several sessions (used to put a failed batch back).</summary>
    /// <param name="runs">Sessions to enqueue, oldest first.</param>
    public void EnqueueRange(IEnumerable<TelemetryRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        var lines = ReadLines();
        lines.AddRange(runs.Where(static r => r is not null).Select(Format));
        if (lines.Count > _capacity)
        {
            lines.RemoveRange(0, lines.Count - _capacity);
        }

        Write(lines);
    }

    /// <summary>Removes and returns up to <paramref name="max"/> runs, oldest first.</summary>
    /// <param name="max">Batch size ceiling.</param>
    /// <returns>The runs to upload; empty when the queue is empty.</returns>
    public IReadOnlyList<TelemetryRun> TakeBatch(int max = 32)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(max, 1);
        var lines = ReadLines();
        if (lines.Count == 0)
        {
            return [];
        }

        var taken = lines.Take(max).Select(TryParse).Where(static r => r is not null).Select(static r => r!).ToList();
        var remaining = lines.Skip(max).ToList();
        Write(remaining);
        return taken;
    }

    /// <summary>Deletes every queued run (used when the user turns telemetry off).</summary>
    public void Clear()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        catch (IOException)
        {
            // A locked queue only delays the delete; the data is never sent while the setting is off.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string Format(TelemetryRun run) => string.Join(
        Separator,
        run.InstallId,
        run.StartedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        run.StoppedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        run.StartupMs.ToString(CultureInfo.InvariantCulture),
        run.FirstRun ? "1" : "0",
        run.Reason.ToString());

    private static TelemetryRun? TryParse(string line)
    {
        var parts = line.Split(Separator);
        if (parts.Length != 6)
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var started) ||
            !DateTimeOffset.TryParse(parts[2], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var stopped) ||
            !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var startupMs) ||
            !Enum.TryParse<TelemetryStopReason>(parts[5], ignoreCase: true, out var reason))
        {
            return null;
        }

        return new TelemetryRun
        {
            InstallId = parts[0],
            StartedAt = started,
            StoppedAt = stopped,
            StartupMs = Math.Max(0, startupMs),
            FirstRun = parts[4] == "1",
            Reason = reason,
        };
    }

    private List<string> ReadLines()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            return [.. File.ReadAllLines(_path).Where(static l => !string.IsNullOrWhiteSpace(l))];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private List<TelemetryRun> ReadAll() =>
        [.. ReadLines().Select(TryParse).Where(static r => r is not null).Select(static r => r!)];

    private void Write(List<string> lines)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (lines.Count == 0)
            {
                if (File.Exists(_path))
                {
                    File.Delete(_path);
                }

                return;
            }

            File.WriteAllLines(_path, lines);
        }
        catch (IOException)
        {
            // An unwritable queue degrades to "no telemetry", never to a crash.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
