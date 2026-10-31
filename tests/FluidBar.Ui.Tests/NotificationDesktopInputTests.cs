using System.Reflection;
using System.Runtime.InteropServices;
using FluidBar.App.Hosting.Skia;
using FluidBar.App.Settings;
using FluidBar.App.State;
using FluidBar.Core.Activities;
using FluidBar.Core.Diagnostics;
using FluidBar.Island;

namespace FluidBar.Ui.Tests;

// Explicit opt-in: installs the production mouse hook and moves the real desktop cursor.
public sealed class NotificationDesktopInputTests
{
    [Fact]
    public async Task Installed_hook_accepts_real_wheel_and_drag_input()
    {
        if (Environment.GetEnvironmentVariable("FLUIDBAR_NOTIFICATION_DESKTOP_TEST") != "1") return;
        await AvaloniaTest.RunAsync(async () =>
        {
            var localizer = new FakeLocalizer();
            var store = new InMemoryUiSettingsStore(AppSettings.Default with { AutoCollapseMs = 0, AutoHideMs = 0 });
            using var settings = new SettingsController(store.LastSaved, store, NullAppLogger.Instance);
            using var data = new DesignIslandDataSource(localizer, uiPost: a => Avalonia.Threading.Dispatcher.UIThread.Post(a));
            using var host = new SkiaIslandHost(new SkiaIslandHostOptions
            { Localizer = localizer, Settings = settings, Data = data, PlatformActions = new RecordingIslandPlatformActions() });
            await host.StartAsync();
            var controller = (SkiaIslandController)typeof(SkiaIslandHost)
                .GetField("_controller", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host)!;
            GetCursorPos(out var previous);
            try
            {
                for (var gesture = 0; gesture < 2; gesture++)
                {
                    host.PublishActivity(ActivityKind.Notification, new ActivityPayload
                    { Title = "Input smoke " + gesture, SourceName = "FluidBar test", Ttl = TimeSpan.FromMinutes(1) }, gesture + 1, "desktop-smoke");
                    await Task.Delay(700);
                    var shell = IslandMetrics.ShellRect(controller.RenderState);
                    var window = host.PrimaryWindow!;
                    var placement = window.Placement.WindowPixel;
                    var scale = window.Scale;
                    var x = (int)(placement.X + shell.MidX * scale);
                    var y = (int)(placement.Y + shell.MidY * scale);
                    SetCursorPos(x, y);
                    await Task.Delay(100);
                    if (gesture == 0)
                    {
                        mouse_event(0x0800, 0, 0, 120, UIntPtr.Zero);
                        await Task.Delay(180);
                        var hook = typeof(SkiaIslandHost).GetField("_mouseHook", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
                        string HookField(string name) => hook.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(hook)?.ToString() ?? "null";
                        GetCursorPos(out var actual);
                        Assert.True(controller.RenderState.NotificationDragOffsetY < -4,
                            $"first real wheel notch: actual=({actual.X},{actual.Y}) hook=({HookField("_lastHookX")},{HookField("_lastHookY")}) pass={hook.GetType().GetProperty("PassThrough")!.GetValue(hook)} target=({x},{y}) scale={scale} state={controller.RenderState.Form} kind={controller.RenderState.Compact?.Kind} offset={controller.RenderState.NotificationDragOffsetY} pointer={controller.IsPointerInside} windows={host.DescribeWindows()}");
                        mouse_event(0x0800, 0, 0, 120, UIntPtr.Zero);
                    }
                    else
                    {
                        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
                        await Task.Delay(40);
                        for (var i = 1; i <= 10; i++)
                        {
                            SetCursorPos(x, Math.Max(0, y - (int)(i * 10 * scale)));
                            await Task.Delay(16);
                        }
                        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
                    }
                    await Task.Delay(850);
                    string Read(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target)?.ToString() ?? "null";
                    Assert.True(controller.RenderState.Compact?.Kind != ActivityKind.Notification,
                        $"gesture={gesture} at=({x},{y}) scale={scale} offset={controller.RenderState.NotificationDragOffsetY} hookPressed={Read(host, "_hookPressed")} hookKind={Read(host, "_hookDragKind")} active={Read(controller, "_notificationDragActive")} dismissing={Read(controller, "_notificationDismissing")} startY={Read(controller, "_notificationDragStartY")} pointerY={Read(controller, "_pointerY")} monitor={window.Monitor} host={host.DescribeWindows()}");
                    Assert.Equal(0, controller.RenderState.NotificationDragOffsetY, 3);
                }
            }
            finally
            {
                mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
                SetCursorPos(previous.X, previous.Y);
            }
        });
    }

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll", EntryPoint = "SetPhysicalCursorPos")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", EntryPoint = "GetPhysicalCursorPos")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
}
