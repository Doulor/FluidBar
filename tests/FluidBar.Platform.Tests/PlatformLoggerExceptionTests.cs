using FluidBar.Core.Diagnostics;
using FluidBar.Platform.Windows.Diagnostics;

namespace FluidBar.Platform.Tests;

public sealed class PlatformLoggerExceptionTests
{
    [Fact]
    public void Adapter_keeps_serialized_exception_type_message_and_stack_without_overwriting_data()
    {
        var logger = new RecordingAppLogger();
        var adapter = new AppLoggerPlatformAdapter(logger);
        const string original = "System.Runtime.InteropServices.COMException (0x80070490): Element not found.\n   at Windows.UI.Notifications.NotificationVisual.GetBinding(String name)";
        var data = new Dictionary<string, object?> { ["exception"] = "business-value", ["notificationId"] = 42u };
        adapter.Log(new PlatformLogEntry(DateTimeOffset.UnixEpoch, PlatformLogLevel.Warning,
            PlatformLogCategory.Notifications, "notifications.parse-failed", "Parse failed.", data, original));
        var entry = Assert.Single(logger.Snapshot());
        Assert.NotNull(entry.Exception);
        Assert.Equal(original, entry.Exception.ToString());
        Assert.Same(data, entry.Data);
        Assert.Equal("business-value", entry.Data!["exception"]);
    }
}
