using FluidBar.Platform.Windows.Accessibility;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Display;
using FluidBar.Platform.Windows.Hosting;
using FluidBar.Platform.Windows.Interop;

namespace FluidBar.Platform.Tests;

/// <summary>
/// Guarded real-HWND test for the island UIA seam: <c>WM_GETOBJECT</c> must answer with a provider,
/// keyboard messages must reach the sink, and the snapshot must be published on attach. Skipped on
/// non-interactive agents; no Narrator or UIA client is required.
/// </summary>
[Trait("Category", "Integration")]
public sealed class UiaWindowIntegrationTests
{
    [Fact]
    public void LayeredWindowHost_AnswersGetObjectWithTheIslandProvider()
    {
        if (!Environment.UserInteractive)
        {
            return;
        }

        var monitor = new MonitorInfo("uia-test", 0, new PixelRect(0, 0, 1920, 1080),
            new PixelRect(0, 0, 1920, 1040), 96, 96, true);
        using var host = new LayeredWindowHost(new LayeredHostOptions
        {
            Pacing = LayeredPacingMode.None,
            ReassertTopmost = false,
        }, logger: new CapturingLogger(), counters: new PlatformCounters());

        host.Create(monitor, LayeredHostMode.Float);
        host.Show();

        var snapshot = Snapshot();
        var source = new FakeUiaSource(snapshot);
        var keyboard = new FakeKeyboardSink();
        host.AttachAccessibility(source, keyboard);

        Assert.True(host.HasAccessibility);
        Assert.Same(snapshot, host.UiaSnapshot);

        // WM_GETOBJECT(UiaRootObjectId) must reach UiaReturnRawElementProvider and build the root.
        var result = NativeMethods.User32.SendMessageW(host.Handle, NativeMethods.WM_GETOBJECT, IntPtr.Zero,
            new IntPtr(-25));

        Assert.NotEqual(IntPtr.Zero, result);
        Assert.NotNull(host.UiaRootProvider);
        Assert.Equal("FluidBar", host.UiaRootProvider!.GetPropertyValue(30005));
        Assert.NotNull(host.UiaRootProvider.GetPatternProvider(10000));

        // Keyboard focus messages reach the sink; WM_GETDLGCODE asks for Tab/arrow keys.
        _ = NativeMethods.User32.SendMessageW(host.Handle, NativeMethods.WM_SETFOCUS, IntPtr.Zero, IntPtr.Zero);
        Assert.True(host.HasUiaKeyboardFocus);
        var dlgCode = NativeMethods.User32.SendMessageW(host.Handle, NativeMethods.WM_GETDLGCODE, IntPtr.Zero, IntPtr.Zero);
        Assert.Equal(7, dlgCode.ToInt32());

        _ = NativeMethods.User32.SendMessageW(host.Handle, NativeMethods.WM_KEYDOWN,
            new IntPtr(NativeMethods.VK_TAB), IntPtr.Zero);
        Assert.Equal(UiaKeyCommand.Tab, keyboard.LastCommand);

        _ = NativeMethods.User32.SendMessageW(host.Handle, NativeMethods.WM_KEYDOWN,
            new IntPtr(NativeMethods.VK_ESCAPE), IntPtr.Zero);
        Assert.Equal(UiaKeyCommand.Escape, keyboard.LastCommand);

        _ = NativeMethods.User32.SendMessageW(host.Handle, NativeMethods.WM_KILLFOCUS, IntPtr.Zero, IntPtr.Zero);
        Assert.False(host.HasUiaKeyboardFocus);

        // A changed snapshot is published through the source event.
        source.Publish(snapshot with { Name = "FluidBar updated" });
        Assert.Equal("FluidBar updated", host.UiaSnapshot!.Name);
    }

    [Fact]
    public void LayeredWindowHost_WithoutAccessibility_DoesNotAnswerGetObject()
    {
        if (!Environment.UserInteractive)
        {
            return;
        }

        var monitor = new MonitorInfo("uia-test", 0, new PixelRect(0, 0, 1920, 1080),
            new PixelRect(0, 0, 1920, 1040), 96, 96, true);
        using var host = new LayeredWindowHost(new LayeredHostOptions
        {
            Pacing = LayeredPacingMode.None,
            ReassertTopmost = false,
        }, logger: new CapturingLogger(), counters: new PlatformCounters());

        host.Create(monitor, LayeredHostMode.Float);
        host.Show();

        var result = NativeMethods.User32.SendMessageW(host.Handle, NativeMethods.WM_GETOBJECT, IntPtr.Zero,
            new IntPtr(-25));

        Assert.Equal(IntPtr.Zero, result);
        Assert.Null(host.UiaRootProvider);
        Assert.False(host.HasAccessibility);
    }

    private static UiaElementSnapshot Snapshot() => new()
    {
        Id = "island.root",
        Role = UiaElementRole.Group,
        Name = "FluidBar",
        BoundsDip = new UiaRect(24, 24, 440, 284),
        Patterns = UiaPatternKind.Invoke | UiaPatternKind.ExpandCollapse,
        State = new UiaElementState
        {
            IsFocusable = true,
            ExpandCollapse = UiaExpandCollapseState.Collapsed,
        },
        Children =
        [
            new UiaElementSnapshot
            {
                Id = "island.compact.media.play",
                Role = UiaElementRole.Button,
                Name = "Play",
                BoundsDip = new UiaRect(430, 31, 26, 26),
                Patterns = UiaPatternKind.Invoke,
                State = new UiaElementState { IsFocusable = true },
            },
        ],
    };

    private sealed class FakeUiaSource : IUiaElementSource
    {
        public FakeUiaSource(UiaElementSnapshot snapshot) => Snapshot = snapshot;

        public UiaElementSnapshot Snapshot { get; private set; }

        public UiaElementActionRequest? LastRequest { get; private set; }

        public event Action? Changed;

        public UiaElementSnapshot BuildSnapshot() => Snapshot;

        public void Perform(in UiaElementActionRequest request) => LastRequest = request;

        public void Publish(UiaElementSnapshot snapshot)
        {
            Snapshot = snapshot;
            Changed?.Invoke();
        }
    }

    private sealed class FakeKeyboardSink : IUiaKeyboardSink
    {
        public UiaKeyCommand? LastCommand { get; private set; }

        public bool HandleKey(UiaKeyCommand command)
        {
            LastCommand = command;
            return true;
        }

        public void NotifyWindowFocus(bool focused)
        {
        }
    }
}
