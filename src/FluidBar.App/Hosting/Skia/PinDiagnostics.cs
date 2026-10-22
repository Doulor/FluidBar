using System.Globalization;
namespace FluidBar.App.Hosting.Skia;
internal static class PinDiagnostics
{
    public static void Log(string message)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("FLUIDBAR_PIN_DIAG"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var frames = Environment.StackTrace.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(3);
        var line = string.Create(CultureInfo.InvariantCulture, $"{DateTimeOffset.Now:O} | {message} | {string.Join(" <- ", frames)}\n");
        try
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "fluidbar-pin-diag.log"), line);
        }
        catch (IOException)
        {
            // Diagnostics must never affect island input.
        }
    }
}
