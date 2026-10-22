using System.Text.Json;

namespace FluidBar.App.Hosting.Skia;

/// <summary>
/// Remembers which island page the user scrolled to, across restarts.
/// </summary>
/// <remarks>
/// Deliberately a side file instead of a settings field: the settings schema carries migrations and a bad
/// write there can take the island down, while this file is read defensively and ignored when missing.
/// </remarks>
internal static class PageSelectionStore
{
    private static readonly object Gate = new();
    private static string FilePath
    {
        get
        {
            var settingsRoot = Environment.GetEnvironmentVariable("FLUIDBAR_SETTINGS_DIR");
            var root = string.IsNullOrWhiteSpace(settingsRoot)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluidBar")
                : settingsRoot;
            return Path.Combine(root, "ui-page.json");
        }
    }

    /// <summary>Last selected page index, or 0 when nothing was stored (or the file is unreadable).</summary>
    public static int Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return 0;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(FilePath));
            return document.RootElement.TryGetProperty("index", out var index) && index.TryGetInt32(out var value)
                ? Math.Max(0, value)
                : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>Stores the selected page index off the UI thread; failures are swallowed on purpose.</summary>
    public static void Save(int index)
    {
        var value = Math.Max(0, index);
        _ = Task.Run(() => SaveCore(value));
    }

    private static void SaveCore(int index)
    {
        lock (Gate)
        {
            try
            {
                var directory = Path.GetDirectoryName(FilePath)!;
                Directory.CreateDirectory(directory);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(new { index }));
            }
            catch (Exception)
            {
                // A missing preference is a cosmetic loss, never a reason to fail a frame.
            }
        }
    }
}
