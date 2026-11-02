using System.Text.Json;
using FluidBar.App.Settings;
using FluidBar.Platform.Windows.Diagnostics;

namespace FluidBar.Ui.Tests;

public sealed class PlatformLogFileTests
{
    [Fact]
    public async Task Serialized_platform_exception_reaches_the_real_JSONL_file_with_its_stack()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FluidBarTests", "platform-logs", Guid.NewGuid().ToString("N"));
        const string original = "System.Runtime.InteropServices.COMException (0x80070490): Element not found.\n   at NotificationVisual.GetBinding(String name)";
        try
        {
            string path;
            await using (var logger = new SettingsAwareLogger(() => "warn"))
            {
                await logger.StartFileSinkAsync(directory);
                path = logger.LogFilePath!;
                var adapter = new AppLoggerPlatformAdapter(logger);
                adapter.Log(new PlatformLogEntry(DateTimeOffset.UtcNow, PlatformLogLevel.Warning,
                    PlatformLogCategory.Notifications, "test.exception", "Parse failed.", Exception: original));
            }
            var lines = await File.ReadAllLinesAsync(path);
            using var json = JsonDocument.Parse(Assert.Single(lines));
            Assert.Equal(original, json.RootElement.GetProperty("exception").GetString());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}
