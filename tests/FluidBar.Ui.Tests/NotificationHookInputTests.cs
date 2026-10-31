using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Threading;
using FluidBar.App.Hosting.Skia;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.State;
using FluidBar.Core.Activities;
using FluidBar.Core.Diagnostics;
using FluidBar.Core.Layout;
using FluidBar.Island;
using FluidBar.Platform.Windows.Hosting;

namespace FluidBar.Ui.Tests;

public sealed class NotificationHookInputTests
{
    [Fact]
    public void Production_hook_handlers_route_press_drag_outside_and_release_to_banner_gesture()
    {
        using var fixture = new HookFixture();
        var shell = IslandMetrics.ShellRect(fixture.Controller.RenderState);
        fixture.Call("OnHookPointerPressed", shell.MidX, shell.MidY);
        fixture.Call("OnHookPointerDragged", shell.MidX, shell.MidY - 90);
        fixture.Call("OnHookPointerReleased", shell.MidX, shell.MidY - 90);
        fixture.Step(90);
        Assert.NotEqual(ActivityKind.Notification, fixture.Controller.RenderState.Compact?.Kind);
        Assert.Empty(fixture.Actions.Calls);
    }

    [Fact]
    public void Production_hook_wheel_route_handles_two_notches_with_stationary_pointer()
    {
        using var fixture = new HookFixture();
        var shell = IslandMetrics.ShellRect(fixture.Controller.RenderState);
        fixture.Call("OnHookPointerMoved", shell.MidX, shell.MidY);
        fixture.Call("OnHookWheel", 1d, false);
        fixture.Step(12);
        Assert.True(fixture.Controller.RenderState.NotificationDragOffsetY < -4);
        fixture.Call("OnHookWheel", 1d, false);
        fixture.Step(90);
        Assert.NotEqual(ActivityKind.Notification, fixture.Controller.RenderState.Compact?.Kind);
        Assert.Empty(fixture.Actions.Calls);
    }

    [Fact]
    public void Queued_hook_press_move_release_keeps_origin_and_can_dismiss_at_screen_edge()
    {
        using var fixture = new HookFixture();
        var shell = IslandMetrics.ShellRect(fixture.Controller.RenderState);
        Task.Run(() =>
        {
            fixture.Call("OnHookPointerPressed", shell.MidX, shell.MidY);
            fixture.Call("OnHookPointerDragged", shell.MidX, 0f);
            fixture.Call("OnHookPointerReleased", shell.MidX, 0f);
        }).GetAwaiter().GetResult();
        fixture.Drain();
        fixture.Step(90);
        Assert.NotEqual(ActivityKind.Notification, fixture.Controller.RenderState.Compact?.Kind);
        Assert.Empty(fixture.Actions.Calls);
    }

    [Fact]
    public void Low_level_callback_regions_keep_stationary_pointer_for_second_wheel_notch()
    {
        using var fixture = new HookFixture();
        var shell = IslandMetrics.ShellRect(fixture.Controller.RenderState);
        var x = (int)shell.MidX;
        var y = (int)shell.MidY;
        fixture.SendHook(0x020A, x, y, 120);
        fixture.Step(12);
        Assert.True(fixture.Controller.RenderState.NotificationDragOffsetY < -4);
        fixture.SendHook(0x020A, x, y, 120);
        fixture.Step(90);
        Assert.NotEqual(ActivityKind.Notification, fixture.Controller.RenderState.Compact?.Kind);
        Assert.Empty(fixture.Actions.Calls);
    }

    [Fact]
    public void Low_level_callback_preserves_drag_after_pointer_leaves_region()
    {
        using var fixture = new HookFixture();
        var shell = IslandMetrics.ShellRect(fixture.Controller.RenderState);
        var x = (int)shell.MidX;
        var y = (int)shell.MidY;
        fixture.SendHook(0x0201, x, y);
        fixture.SendHook(0x0200, x, 0);
        fixture.SendHook(0x0202, x, 0);
        fixture.Step(90);
        Assert.NotEqual(ActivityKind.Notification, fixture.Controller.RenderState.Compact?.Kind);
        Assert.Empty(fixture.Actions.Calls);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HookData
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public IntPtr Extra;
    }

    private sealed class QueueContext : SynchronizationContext
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
        public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));
        public void Drain() { while (_queue.TryDequeue(out var work)) work.Callback(work.State); }
    }

    private sealed class HookFixture : IDisposable
    {
        private readonly SkiaIslandHost _host;
        private readonly QueueContext _context = new();
        public void Drain() => _context.Drain();
        private readonly SettingsController _settings;
        private readonly DesignIslandDataSource _data;
        private readonly FakeTimeProvider _time = new();
        private readonly PlatformMessageHost _messageHost;
        private readonly IslandMouseHook _hook;
        public RecordingIslandPlatformActions Actions { get; } = new();
        public SkiaIslandController Controller { get; }

        public HookFixture()
        {
            var localizer = new FakeLocalizer();
            var store = new InMemoryUiSettingsStore(AppSettings.Default with { AutoCollapseMs = 0, AutoHideMs = 0 });
            _settings = new SettingsController(store.LastSaved, store, NullAppLogger.Instance);
            _data = new DesignIslandDataSource(localizer, _time, a => a());
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(_context);
            try
            {
                _host = new SkiaIslandHost(new SkiaIslandHostOptions
                { Localizer = localizer, Settings = _settings, Data = _data, PlatformActions = Actions, TimeProvider = _time });
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
            Controller = (SkiaIslandController)Field(_host, "_controller").GetValue(_host)!;
            Controller.Start();
            Controller.PublishActivity(ActivityKind.Notification, new ActivityPayload
            { Title = "Hook toast", SourceName = "App", Ttl = TimeSpan.FromMinutes(5) }, 1, "hook-test");
            Controller.SnapMotion();
            var window = new LayeredWindowHost();
            Field(window, "_placement").SetValue(window, new LayeredHostPlacement(
                new LayeredRect(0, 0, 1263, 384), new LayeredPixelSize(1263, 384),
                LayeredSurfaceSize.ForMode(LayeredHostMode.Float), 1, new LayeredPixelSize(24, 24)));
            var entryType = typeof(SkiaIslandHost).GetNestedType("WindowEntry", BindingFlags.NonPublic)!;
            var monitor = MonitorInfo.Create("test", new PixelRect(0, 0, 1920, 1080));
            var entry = Activator.CreateInstance(entryType, window, monitor)!;
            ((IList)Field(_host, "_windows").GetValue(_host)!).Add(entry);
            _messageHost = new PlatformMessageHost(NullPlatformLogger.Instance, new PlatformCounters());
            _hook = new IslandMouseHook(_messageHost, NullPlatformLogger.Instance, new PlatformCounters());
            foreach (var pair in new[]
            {
                ("PointerPressed", "OnHookPointerPressed"), ("PointerReleased", "OnHookPointerReleased"),
                ("PointerDragged", "OnHookPointerDragged"), ("PointerMoved", "OnHookPointerMoved")
            })
            {
                var method = typeof(SkiaIslandHost).GetMethod(pair.Item2, BindingFlags.Instance | BindingFlags.NonPublic)!;
                typeof(IslandMouseHook).GetEvent(pair.Item1)!.AddEventHandler(_hook,
                    Delegate.CreateDelegate(typeof(Action<float, float>), _host, method));
            }
            _hook.PointerLeft += () => Call("OnHookPointerLeft");
            _hook.WheelWithModifiers += (notches, shift) => Call("OnHookWheel", notches, shift);
        }

        private static FieldInfo Field(object instance, string name)
            => instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;

        public void Call(string name, params object[] args)
            => typeof(SkiaIslandHost).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_host, args);

        public void SendHook(int message, int x, int y, short wheel = 0)
        {
            Call("RebuildHookRects", Controller.RenderState);
            var rects = (List<IslandMouseHook.Rect>)Field(_host, "_hookRects").GetValue(_host)!;
            _hook.SetRegions(rects);
            var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<HookData>());
            try
            {
                Marshal.StructureToPtr(new HookData { X = x, Y = y, MouseData = unchecked((uint)(ushort)wheel << 16) }, pointer, false);
                typeof(IslandMouseHook).GetMethod("HookCallback", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(_hook, new object[] { 0, new IntPtr(message), pointer });
            }
            finally { Marshal.FreeHGlobal(pointer); }
        }

        public void Step(int frames)
        {
            for (var i = 0; i < frames; i++)
            {
                _time.Advance(TimeSpan.FromMilliseconds(16));
                Controller.Advance(TimeSpan.FromMilliseconds(16));
            }
        }

        public void Dispose() { _hook.Dispose(); _messageHost.DisposeAsync().AsTask().GetAwaiter().GetResult(); _host.Dispose(); _data.Dispose(); _settings.Dispose(); }
    }
}
