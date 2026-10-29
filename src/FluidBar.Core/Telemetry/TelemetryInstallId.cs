namespace FluidBar.Core.Telemetry;

/// <summary>
/// The anonymous install identifier: a random GUID generated on this machine, stored next to the queue and
/// resettable from the settings window. It is never derived from hardware, the user name or the machine name.
/// </summary>
public static class TelemetryInstallId
{
    /// <summary>Reads the stored identifier, creating one when missing or unreadable.</summary>
    /// <param name="path">Absolute path of the identifier file.</param>
    /// <returns>A GUID in <c>N</c> format.</returns>
    public static string GetOrCreate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            if (File.Exists(path))
            {
                var existing = File.ReadAllText(path).Trim();
                if (Guid.TryParse(existing, out var parsed))
                {
                    return parsed.ToString("N");
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        var created = Guid.NewGuid().ToString("N");
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, created);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return created;
    }

    /// <summary>Forgets the current identifier; the next session starts a fresh one.</summary>
    /// <param name="path">Absolute path of the identifier file.</param>
    public static void Reset(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // Best effort: writing an empty file still invalidates the old identifier.
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            try
            {
                File.WriteAllText(path, string.Empty);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
