using System.Globalization;
namespace FluidBar.Platform.Windows.Media;
public static class MediaDiagnostics
{
    public static bool Enabled => string.Equals(Environment.GetEnvironmentVariable("FLUIDBAR_PIN_DIAG"), "1", StringComparison.Ordinal);

    /// <summary>Writes regardless of <see cref="Enabled"/>; use only for rare transition lines.</summary>
    public static void LogAlways(string message)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "fluidbar-pin-diag.log"),
                $"{DateTimeOffset.Now:O} | media-state {message}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }

    public static void Log(string message)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "fluidbar-pin-diag.log"),
                $"{DateTimeOffset.Now:O} | media-provider {message}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }
}
