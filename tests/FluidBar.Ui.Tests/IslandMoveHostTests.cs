using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using FluidBar.App.Hosting.Skia;
using FluidBar.App.Settings;
using FluidBar.App.State;
using FluidBar.Core.Diagnostics;
using FluidBar.Core.Layout;
using FluidBar.Core.Motion;
using FluidBar.Core.Activities;
using FluidBar.App.Hosting.Pages;
using FluidBar.Island;
using FluidBar.Platform.Windows.Hosting;

namespace FluidBar.Ui.Tests;

public sealed class IslandMoveHostTests
{
    [Fact]
    public async Task PluginRegistrationChangesDoNotReenableTheSourceAsADropTargetDuringAnOutboundDrag()
    {
        await AvaloniaTest.Run(() =>
        {
            using var fixture = new MoveFixture();
            var active = typeof(SkiaIslandHost).GetField("_nativeFileDragActive", BindingFlags.Instance | BindingFlags.NonPublic)!;
            using var initial = fixture.Host.RegisterFileDropHandler(_ => { });
            Assert.True(AcceptsFiles(fixture.Window));
            active.SetValue(fixture.Host, true);
            fixture.Window.SetFileDropEnabled(false);
            using var extra = fixture.Host.RegisterFileDropHandler(_ => { });
            Assert.False(AcceptsFiles(fixture.Window));
            extra.Dispose();
            Assert.False(AcceptsFiles(fixture.Window));
            active.SetValue(fixture.Host, false);
            using var resumed = fixture.Host.RegisterFileDropHandler(_ => { });
            Assert.True(AcceptsFiles(fixture.Window));
        });
    }

    private static bool AcceptsFiles(LayeredWindowHost window) => (GetWindowLongW(window.Handle, -20) & 0x10) != 0;
    [DllImport("user32.dll")] private static extern int GetWindowLongW(IntPtr window, int index);

    [Fact]
    public async Task LegacySavedPlacementKeepsItsOriginalHostCoordinates()
    {
        await AvaloniaTest.Run(() =>
        {
            using var fixture = new MoveFixture();
            var legacy = AppSettings.TryParse("""
                {"schemaVersion":2,"placement":{"monitors":{"move-test":{"anchor":"bottom-right","offsetX":75,"offsetY":80}}}}
                """, out var error);
            Assert.NotNull(legacy);
            fixture.Settings.Replace(legacy! with { AutoCollapseMs = 0, AutoHideMs = 0 });
            var expected = IslandLayout.Calculate(IslandLayoutRequest.FromState(fixture.Controller.State,
                IslandMode.Float, IslandAnchor.BottomRight, 75, 80), fixture.Monitor).Placement.WindowPixel;
            Assert.Equal(expected.X, fixture.Window.Placement.WindowPixel.X);
            Assert.Equal(expected.Y, fixture.Window.Placement.WindowPixel.Y);
        });
    }

    [Fact]
    public async Task SavedPositionDoesNotCancelNotificationDragAnimation()
    {
        await AvaloniaTest.Run(() =>
        {
            using var fixture = new MoveFixture();
            fixture.Controller.PublishActivity(ActivityKind.Notification, new ActivityPayload
                { Title = "Notice", SourceName = "Test", Ttl = TimeSpan.FromMinutes(5) }, 1, "saved-position-notice");
            fixture.Controller.SnapMotion();
            fixture.Settings.Update(s => s.WithMonitorPlacement("move-test", IslandAnchor.TopCenter, 0, 250));
            fixture.ApplySavedPlacement();
            var before = fixture.VisibleShell();
            var local = IslandMetrics.ShellRect(fixture.Controller.RenderState);
            fixture.Controller.HandlePointerPressed(local.MidX, local.MidY);
            fixture.Controller.HandleDragStarted(local.MidX, local.MidY, 0);
            Assert.True(fixture.Controller.BeginNotificationDrag());
            fixture.Controller.HandleNotificationDragMoved(local.MidY - 90, 100);
            fixture.AdvanceFrames(1);
            Assert.True(fixture.Controller.RenderState.NotificationDragOffsetY < -5);
            Assert.True(fixture.VisibleShell().Y < before.Y - 5,
                "Moving the native window must not compensate for the notification's local drag offset.");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeFileDragRestoresASuspendedPositionReturn(bool copied)
    {
        await AvaloniaTest.Run(() =>
        {
            var invoked = false;
            var registry = FileShelfOutboundDragTests.Registry([Path.Combine(Path.GetTempPath(), "return-file.txt")]);
            using var fixture = new MoveFixture(pageRegistry: registry, fileDrag: (_, _) => { invoked = true; return copied; });
            var point = fixture.PressPoint();
            fixture.Call("OnHookPointerPressed", point.X, point.Y);
            fixture.Call("OnHookPointerDragged", point.X + 180, point.Y + 120);
            fixture.Call("OnHookPointerReleased", point.X + 180, point.Y + 120);
            fixture.Controller.ApplyInput(IslandInput.PrimaryClick());
            fixture.Controller.HandlePageRequest(new PageRef("fluidbar-file-shelf.page"));
            fixture.Controller.SnapMotion();
            for (var frame = 0; frame < 100; frame++) fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));
            var state = fixture.Controller.RenderState;
            var row = PageMetricsLayout.NotificationRow(IslandMetrics.PageRect(state, 6), 0, 0, FontScaleMetrics.From(state.FontScale));
            var window = fixture.Window.Placement.WindowPixel;
            var pointer = new LayeredPointerEvent((int)row.MidX, (int)row.MidY, row.MidX, row.MidY,
                window.X + (int)row.MidX, window.Y + (int)row.MidY, LayeredPointerButtons.Left, LayeredKeyModifiers.None, 0);
            fixture.Raise("PointerPressed", pointer);
            Assert.True(fixture.PositionReturn.IsSuspended);
            Assert.True(fixture.Controller.HasPendingFileDrag);
            fixture.Call("StartNativeFileDrag", fixture.Entry, false);
            Assert.True(invoked);
            Assert.False(fixture.PositionReturn.IsSuspended);
            fixture.AdvanceFrames(240);
            Assert.False(fixture.PositionReturn.IsActive);
        });
    }

    [Fact]
    public async Task ControlClickDuringTemporaryDisplacementCancelsReturnEvenWhenSettingsAreAlreadyDefault()
    {
        await AvaloniaTest.Run(() =>
        {
            using var fixture = new MoveFixture();
            var home = fixture.Window.Placement.WindowPixel;
            var point = fixture.PressPoint();
            fixture.Call("OnHookPointerPressed", point.X, point.Y);
            fixture.Call("OnHookPointerDragged", point.X + 180, point.Y + 120);
            fixture.Call("OnHookPointerReleased", point.X + 180, point.Y + 120);
            point = fixture.PressPoint();
            fixture.Call("OnHookPointerPressedWithModifiers", point.X, point.Y, LayeredKeyModifiers.Control);
            fixture.Call("OnHookPointerReleased", point.X, point.Y);
            Assert.Equal(home, fixture.Window.Placement.WindowPixel);
            fixture.AdvanceFrames(240);
            Assert.Equal(home, fixture.Window.Placement.WindowPixel);
        });
    }

    [Fact]
    public async Task RegrabbingTheReturningIslandContinuesFromItsCurrentPosition()
    {
        await AvaloniaTest.Run(() =>
        {
            using var fixture = new MoveFixture();
            var home = fixture.Window.Placement.WindowPixel;
            var point = fixture.PressPoint();
            fixture.Call("OnHookPointerPressed", point.X, point.Y);
            fixture.Call("OnHookPointerDragged", point.X + 180, point.Y + 120);
            fixture.Call("OnHookPointerReleased", point.X + 180, point.Y + 120);
            fixture.AdvanceFrames(85);
            var returning = fixture.Window.Placement.WindowPixel;
            point = fixture.PressPoint();
            fixture.Call("OnHookPointerPressed", point.X, point.Y);
            fixture.AdvanceFrames(20);
            Assert.Equal(returning, fixture.Window.Placement.WindowPixel);
            fixture.Call("OnHookPointerDragged", point.X + 60, point.Y + 30);
            var redropped = fixture.Window.Placement.WindowPixel;
            Assert.Equal(returning.X + 60, redropped.X);
            fixture.Call("OnHookPointerReleased", point.X + 60, point.Y + 30);
            fixture.AdvanceFrames(60);
            Assert.Equal(redropped, fixture.Window.Placement.WindowPixel);
            fixture.AdvanceFrames(180);
            Assert.Equal(home, fixture.Window.Placement.WindowPixel);
            Assert.Equal(0, fixture.Store.SaveCount);
        });
    }

    [Fact]
    public async Task FrameClockKeepsRunningDuringThePauseAndStopsAfterReturnSettles()
    {
        await AvaloniaTest.Run(() =>
        {
            using var fixture = new MoveFixture();
            var home = fixture.Window.Placement.WindowPixel;
            var point = fixture.PressPoint();
            fixture.Call("OnHookPointerPressed", point.X, point.Y);
            fixture.Call("OnHookPointerDragged", point.X + 180, point.Y + 120);
            fixture.Call("OnHookPointerReleased", point.X + 180, point.Y + 120);
            fixture.EnableFrameLoop();
            Assert.True(fixture.Clock.IsRunning);
            fixture.Clock.Pump(60, TimeSpan.FromMilliseconds(16));
            Assert.True(fixture.Clock.IsRunning);
            fixture.Clock.Pump(240, TimeSpan.FromMilliseconds(16));
            Assert.Equal(home, fixture.Window.Placement.WindowPixel);
            Assert.False(fixture.Clock.IsRunning);
        });
    }

    [Theory]
    [InlineData(96u, -1, 0)]
    [InlineData(96u, 1, 0)]
    [InlineData(96u, 0, 1)]
    [InlineData(144u, -1, 1)]
    [InlineData(144u, 1, 1)]
    [InlineData(144u, 0, 1)]
    [InlineData(192u, -1, 0)]
    [InlineData(192u, 1, 0)]
    [InlineData(192u, 0, 1)]
    public async Task VisibleIslandReachesScreenEdgesAndSavedDropStaysThere(uint dpi, int horizontal, int vertical)
    {
        await AvaloniaTest.Run(() =>
        {
            using var fixture = new MoveFixture(dpi);
            var point = fixture.PressPoint();
            fixture.Call("OnHookPointerPressedWithModifiers", point.X, point.Y, LayeredKeyModifiers.Control);
            var x = point.X + horizontal * 10000;
            var y = point.Y + vertical * 10000;
            fixture.Call("OnHookPointerDragged", x, y);
            var shell = fixture.VisibleShell();
            if (horizontal < 0) Assert.InRange(Math.Abs(shell.X - fixture.WorkArea.X), 0, 1);
            if (horizontal > 0) Assert.InRange(Math.Abs(shell.Right - fixture.WorkArea.Right), 0, 1);
            if (vertical > 0) Assert.InRange(Math.Abs(shell.Bottom - fixture.WorkArea.Bottom), 0, 1);
            fixture.Call("OnHookPointerReleased", x, y);
            var saved = fixture.VisibleShell();
            Assert.InRange(Math.Abs(saved.X - shell.X), 0, 1);
            Assert.InRange(Math.Abs(saved.Y - shell.Y), 0, 1);
            // Reapplying the saved placement must not reintroduce the transparent-window clamp.
            fixture.ApplySavedPlacement();
            Assert.Equal(saved, fixture.VisibleShell());
            var core = CoreSettingsStoreAdapter.ApplyToCore(new FluidBar.Core.Settings.FluidBarSettings(), fixture.Settings.Current);
            var json = FluidBar.Core.Settings.SettingsJson.Serialize(core);
            Assert.True(FluidBar.Core.Settings.SettingsJson.TryDeserialize(json, out var reloaded, out _));
            fixture.Settings.Replace(CoreSettingsStoreAdapter.ToAppSettings(reloaded!));
            Assert.Equal(saved, fixture.VisibleShell());
            var imported = AppSettings.TryParse(fixture.Settings.Current.ToJson(), out _);
            fixture.Settings.Replace(imported!);
            Assert.Equal(saved, fixture.VisibleShell());
        });
    }

    [Fact]
    public async Task ControlHookDragSavesPositionAndControlClickResetsIt()
    {
        await AvaloniaTest.Run(() =>
        {
            using var fixture = new MoveFixture();
            var start = fixture.Window.Placement.WindowPixel;
            var point = fixture.PressPoint();
            fixture.Call("OnHookPointerPressedWithModifiers", point.X, point.Y, LayeredKeyModifiers.Control);
            fixture.Call("OnHookPointerDragged", point.X + 120, point.Y + 80);
            var dropped = fixture.Window.Placement.WindowPixel;
            Assert.NotEqual(start, dropped);
            fixture.Call("OnHookPointerReleased", point.X + 120, point.Y + 80);
            Assert.Equal(dropped, fixture.Window.Placement.WindowPixel);
            Assert.NotEmpty(fixture.Settings.Current.Placement);
            Assert.True(fixture.Store.SaveCount > 0);

            var saveCount = fixture.Store.SaveCount;
            point = fixture.PressPoint();
            fixture.Call("OnHookPointerPressed", point.X, point.Y);
            fixture.Call("OnHookPointerDragged", point.X - 70, point.Y + 60);
            fixture.Call("OnHookPointerReleased", point.X - 70, point.Y + 60);
            fixture.AdvanceFrames(240);
            Assert.Equal(dropped, fixture.Window.Placement.WindowPixel);
            Assert.Equal(saveCount, fixture.Store.SaveCount);

            point = fixture.PressPoint();
            fixture.Call("OnHookPointerPressedWithModifiers", point.X, point.Y, LayeredKeyModifiers.Control);
            fixture.Call("OnHookPointerReleased", point.X, point.Y);
            Assert.Equal(start, fixture.Window.Placement.WindowPixel);
            Assert.Empty(fixture.Settings.Current.Placement);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task NativeWindowDragHonorsControlAndCancellation(bool control, bool cancelled)
    {
        await AvaloniaTest.Run(() =>
        {
            using var fixture = new MoveFixture();
            var start = fixture.Window.Placement.WindowPixel;
            var point = fixture.PressPoint();
            var shell = IslandMetrics.ShellRect(fixture.Controller.RenderState);
            var pointer = new LayeredPointerEvent((int)shell.MidX, (int)shell.MidY, shell.MidX, shell.MidY,
                (int)point.X, (int)point.Y, LayeredPointerButtons.Left,
                control ? LayeredKeyModifiers.Control : LayeredKeyModifiers.None, 0);
            fixture.Raise("PointerPressed", pointer);
            fixture.Raise("Dragged", new LayeredDragEvent(LayeredDragPhase.Started, pointer, 0, 0, 0, 0));
            var moved = pointer with { ScreenX = pointer.ScreenX + 120, ScreenY = pointer.ScreenY + 80 };
            fixture.Raise("Dragged", new LayeredDragEvent(LayeredDragPhase.Moved, moved, 120, 80, 120, 80));
            var dropped = fixture.Window.Placement.WindowPixel;
            Assert.NotEqual(start, dropped);
            if (!cancelled) fixture.Raise("PointerReleased", moved);
            fixture.Raise("Dragged", new LayeredDragEvent(cancelled ? LayeredDragPhase.Cancelled : LayeredDragPhase.Ended, moved, 120, 80, 0, 0));
            if (!control && !cancelled)
            {
                Assert.Equal(dropped, fixture.Window.Placement.WindowPixel);
                fixture.AdvanceFrames(240);
            }
            Assert.Equal(control && !cancelled ? dropped : start, fixture.Window.Placement.WindowPixel);
            Assert.Equal(control && !cancelled, fixture.Store.SaveCount > 0);
            Assert.False(fixture.Controller.IsWindowDragging);
        });
    }

    [Fact]
    public async Task PlainHookDragMovesWindowThenReturnsWithoutSaving()
    {
        await AvaloniaTest.Run(() =>
        {
            using var fixture = new MoveFixture();
            var start = fixture.Window.Placement.WindowPixel;
            var point = fixture.PressPoint();
            fixture.Call("OnHookPointerPressed", point.X, point.Y);
            fixture.Call("OnHookPointerDragged", point.X + 120, point.Y + 80);
            var dropped = fixture.Window.Placement.WindowPixel;
            Assert.NotEqual(start, dropped);
            fixture.Call("OnHookPointerReleased", point.X + 120, point.Y + 80);
            Assert.Equal(dropped, fixture.Window.Placement.WindowPixel);
            fixture.AdvanceFrames(60); // 960 ms: visibly pause before returning.
            Assert.Equal(dropped, fixture.Window.Placement.WindowPixel);
            var crossedHome = false;
            var positions = new HashSet<int>();
            for (var frame = 0; frame < 180; frame++)
            {
                fixture.AdvanceFrames(1);
                positions.Add(fixture.Window.Placement.WindowPixel.X);
                crossedHome |= fixture.Window.Placement.WindowPixel.X < start.X;
            }
            Assert.Equal(start, fixture.Window.Placement.WindowPixel);
            Assert.True(crossedHome, "Return should overshoot slightly before settling, rather than snap or ease linearly.");
            Assert.True(positions.Count > 15, "Return should move through intermediate positions.");
            Assert.Equal(0, fixture.Store.SaveCount);
            Assert.False(fixture.Controller.IsWindowDragging);
        });
    }

    private sealed class MoveFixture : IDisposable
    {
        public readonly InMemoryUiSettingsStore Store = new(AppSettings.Default with { AutoCollapseMs = 0, AutoHideMs = 0 });
        public readonly SettingsController Settings;
        public readonly LayeredWindowHost Window;
        public readonly SkiaIslandController Controller;
        public readonly ManualFrameClock Clock = new();
        private readonly SkiaIslandHost _host;
        private readonly DesignIslandDataSource _data;
        private readonly object _entry;
        public PixelRect WorkArea { get; }
        public MonitorInfo Monitor { get; }
        public object Entry => _entry;
        public IslandPositionReturn PositionReturn => (IslandPositionReturn)_entry.GetType().GetProperty("PositionReturn")!.GetValue(_entry)!;
        public SkiaIslandHost Host => _host;

        public MoveFixture(uint dpi = 96, PageRegistry? pageRegistry = null,
            Func<IReadOnlyList<string>, Func<bool>?, bool>? fileDrag = null)
        {
            Settings = new SettingsController(Store.LastSaved, Store);
            var localizer = new FakeLocalizer();
            _data = new DesignIslandDataSource(localizer, uiPost: action => action());
            _host = new SkiaIslandHost(new SkiaIslandHostOptions
                { Settings = Settings, Localizer = localizer, Data = _data, FrameClockFactory = () => Clock,
                    PageRegistry = pageRegistry, FileDrag = fileDrag });
            Controller = (SkiaIslandController)Field(_host, "_controller").GetValue(_host)!;
            Controller.Start();
            Controller.SnapMotion();
            var monitor = MonitorInfo.Create("move-test", new PixelRect(-1920, 40, 1920, 1080), dpi);
            Monitor = monitor;
            WorkArea = monitor.WorkArea;
            var entry = typeof(SkiaIslandHost).GetMethod("CreateWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(_host, [monitor, IslandMode.Float])!;
            _entry = entry;
            Window = (LayeredWindowHost)entry.GetType().GetProperty("Host")!.GetValue(entry)!;
            ((IList)Field(_host, "_windows").GetValue(_host)!).Add(entry);
            Call("ApplyPlacement", entry);
        }

        public (float X, float Y) PressPoint()
        {
            var shell = IslandMetrics.ShellRect(Controller.RenderState);
            var origin = Window.Placement.WindowPixel;
            return ((float)(origin.X + shell.MidX * Window.Scale), (float)(origin.Y + shell.MidY * Window.Scale));
        }

        public void Call(string name, params object[] args)
            => typeof(SkiaIslandHost).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_host, args);

        public void Raise<T>(string name, T value)
            => ((Action<T>)Field(Window, name).GetValue(Window)!).Invoke(value);

        public void AdvanceFrames(int count)
        {
            for (var frame = 0; frame < count; frame++) Call("OnFrame", TimeSpan.FromMilliseconds(16));
        }

        public void ApplySavedPlacement() => Call("ApplyPlacement", _entry);

        public void EnableFrameLoop()
        {
            Field(_host, "_started").SetValue(_host, true);
            Call("EnsureFrameLoop");
        }

        public VisualRect VisibleShell()
        {
            var shell = IslandMetrics.ShellRect(Controller.RenderState);
            var window = Window.Placement.WindowPixel;
            return new VisualRect(window.X + shell.Left * Window.Scale, window.Y + shell.Top * Window.Scale,
                shell.Width * Window.Scale, shell.Height * Window.Scale);
        }

        private static FieldInfo Field(object target, string name)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;

        public void Dispose() { _host.Dispose(); _data.Dispose(); Settings.Dispose(); }
    }
}
