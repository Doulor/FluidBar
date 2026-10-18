using FluidBar.Platform.Windows.Accessibility;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Interop;

namespace FluidBar.Platform.Windows.Hosting;

/// <summary>
/// UI Automation half of <see cref="LayeredWindowHost"/>: <c>WM_GETOBJECT</c> returns a managed raw
/// element provider for the island, keyboard focus messages are forwarded to the App's logical focus
/// sink, and the provider tree is rebuilt only when the App publishes a new element snapshot.
/// </summary>
public sealed partial class LayeredWindowHost
{
    private readonly object _uiaGeometryGate = new();
    private readonly SynchronizationContext? _uiaSynchronizationContext = SynchronizationContext.Current;

    private IUiaElementSource? _uiaSource;
    private IUiaKeyboardSink? _uiaKeyboard;
    private UiaElementSnapshot? _uiaSnapshot;
    private UiaElementProvider? _uiaRootProvider;
    private UiaWindowGeometry _uiaGeometry = new(0, 0, 1.0, 0, 0, false);
    private bool _uiaFocused;

    /// <summary>Whether an accessibility source is attached.</summary>
    public bool HasAccessibility => Volatile.Read(ref _uiaSource) is not null;

    /// <summary>The element snapshot currently exposed to UIA (null when not attached).</summary>
    public UiaElementSnapshot? UiaSnapshot => Volatile.Read(ref _uiaSnapshot);

    /// <summary>The cached provider root, if UIA has queried this window (tests/diagnostics).</summary>
    public UiaElementProvider? UiaRootProvider => Volatile.Read(ref _uiaRootProvider);

    /// <summary>Whether the island HWND currently owns keyboard focus.</summary>
    public bool HasUiaKeyboardFocus => Volatile.Read(ref _uiaFocused);

    /// <summary>
    /// Attaches the App accessibility seam. The source's snapshot is built on the UI thread; the
    /// source is responsible for raising <see cref="IUiaElementSource.Changed"/> when its tree changes.
    /// </summary>
    /// <param name="source">Element tree/action source.</param>
    /// <param name="keyboard">Optional keyboard focus sink.</param>
    public void AttachAccessibility(IUiaElementSource source, IUiaKeyboardSink? keyboard = null)
    {
        EnsureOwningThread();
        ArgumentNullException.ThrowIfNull(source);
        DetachAccessibility();
        _uiaSource = source;
        _uiaKeyboard = keyboard;
        source.Changed += OnAccessibilitySourceChanged;
        UpdateUiaGeometry();
        try
        {
            SetUiaSnapshot(source.BuildSnapshot());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(PlatformLogCategory.Platform, "layered-host.uia-attach-failed",
                "Building the initial island UIA snapshot failed; accessibility stays attached but empty.", ex);
        }

        _logger.LogDebug(PlatformLogCategory.Platform, "layered-host.uia-attached",
            "Attached the island UI Automation provider.");
    }

    /// <summary>Detaches the accessibility source and releases the UIA provider tree.</summary>
    public void DetachAccessibility()
    {
        EnsureOwningThread();
        DisposeAccessibility();
    }

    /// <summary>
    /// Publishes a new immutable element snapshot. Called on the UI thread from App state changes,
    /// never from the frame loop: the provider tree is rebuilt lazily on the next UIA query.
    /// </summary>
    /// <param name="snapshot">New snapshot, or null to expose no elements.</param>
    public void SetUiaSnapshot(UiaElementSnapshot? snapshot)
    {
        EnsureOwningThread();
        var previous = Interlocked.Exchange(ref _uiaRootProvider, null);
        Volatile.Write(ref _uiaSnapshot, snapshot);

        var listening = UiaNative.UiaClientsAreListening();

        // Notify clients that still hold the previous provider before it is disconnected; the
        // structure-changed event makes Narrator re-query (and then get the new tree via WM_GETOBJECT).
        if (previous is not null && listening)
        {
            RaiseStructureChanged(previous);
        }

        DisconnectRoot(previous);

        if (snapshot is null || !listening)
        {
            return;
        }

        var root = GetOrCreateRootProvider();
        if (root?.GetFocus() is IRawElementProviderSimple focused)
        {
            RaiseAutomationEvent(focused, UiaConstants.AutomationFocusChangedEventId);
        }
    }

    private void RaiseStructureChanged(UiaElementProvider root)
    {
        try
        {
            _ = UiaNative.UiaRaiseStructureChangedEvent(
                root, UiaConstants.StructureChangeTypeChildrenInvalidated, IntPtr.Zero, 0);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(PlatformLogCategory.Platform, "layered-host.uia-event-failed",
                "Raising a UIA structure event failed. " + ex.Message);
        }
    }

    private void RaiseAutomationEvent(IRawElementProviderSimple provider, int eventId)
    {
        try
        {
            _ = UiaNative.UiaRaiseAutomationEvent(provider, eventId);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(PlatformLogCategory.Platform, "layered-host.uia-event-failed",
                "Raising a UIA automation event failed. " + ex.Message);
        }
    }

    /// <summary>Replaces the live window geometry used to translate DIP bounds to screen pixels.</summary>
    private void UpdateUiaGeometry()
    {
        var placement = _placement;
        var geometry = new UiaWindowGeometry(
            placement.WindowPixel.X,
            placement.WindowPixel.Y,
            _scale,
            placement.WindowPixel.Width,
            placement.WindowPixel.Height,
            _visible);
        lock (_uiaGeometryGate)
        {
            _uiaGeometry = geometry;
        }
    }

    private UiaWindowGeometry CurrentUiaGeometry()
    {
        lock (_uiaGeometryGate)
        {
            return _uiaGeometry;
        }
    }

    private void OnAccessibilitySourceChanged()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            PostToUi(OnAccessibilitySourceChanged);
            return;
        }

        if (_uiaSource is { } source)
        {
            try
            {
                SetUiaSnapshot(source.BuildSnapshot());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(PlatformLogCategory.Platform, "layered-host.uia-refresh-failed",
                    "Refreshing the island UIA snapshot failed; the previous tree stays published.", ex);
            }
        }
    }

    private UiaElementProvider? GetOrCreateRootProvider()
    {
        var snapshot = Volatile.Read(ref _uiaSnapshot);
        var source = Volatile.Read(ref _uiaSource);
        if (snapshot is null || source is null)
        {
            return null;
        }

        var existing = Volatile.Read(ref _uiaRootProvider);
        if (existing is not null)
        {
            return existing;
        }

        var context = new UiaProviderContext(
            snapshot,
            local => CurrentUiaGeometry().ToScreen(local),
            request => PostToUi(() => InvokeSource(() => source.Perform(request), "layered-host.uia-action-failed")),
            id => PostToUi(() =>
            {
                InvokeSource(() => source.Perform(new UiaElementActionRequest(UiaElementAction.SetFocus, id)),
                    "layered-host.uia-focus-request-failed");
                FocusForAccessibility();
            }),
            () => CurrentUiaGeometry().IsVisible);

        var created = UiaElementProvider.CreateRoot(snapshot, context);
        var prior = Interlocked.CompareExchange(ref _uiaRootProvider, created, null);
        return prior ?? created;
    }

    private IntPtr OnGetObject(IntPtr hwnd, IntPtr wParam, IntPtr lParam)
    {
        if (lParam.ToInt64() != UiaConstants.RootObjectId)
        {
            return NativeMethods.User32.DefWindowProcW(hwnd, NativeMethods.WM_GETOBJECT, wParam, lParam);
        }

        UiaElementProvider? provider;
        try
        {
            provider = GetOrCreateRootProvider();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(PlatformLogCategory.Platform, "layered-host.uia-provider-failed",
                "Building the island UIA provider failed. " + ex.Message);
            return NativeMethods.User32.DefWindowProcW(hwnd, NativeMethods.WM_GETOBJECT, wParam, lParam);
        }

        if (provider is null)
        {
            return NativeMethods.User32.DefWindowProcW(hwnd, NativeMethods.WM_GETOBJECT, wParam, lParam);
        }

        try
        {
            return UiaNative.UiaReturnRawElementProvider(hwnd, wParam, lParam, provider);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(PlatformLogCategory.Platform, "layered-host.uia-provider-failed",
                "UiaReturnRawElementProvider failed. " + ex.Message);
            return NativeMethods.User32.DefWindowProcW(hwnd, NativeMethods.WM_GETOBJECT, wParam, lParam);
        }
    }

    private IntPtr OnKeyDown(IntPtr wParam, IntPtr lParam)
    {
        var keyboard = Volatile.Read(ref _uiaKeyboard);
        if (keyboard is null)
        {
            return NativeMethods.User32.DefWindowProcW(_hwnd, NativeMethods.WM_KEYDOWN, wParam, lParam);
        }

        var shift = (NativeMethods.User32.GetKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0;
        var command = wParam.ToInt32() switch
        {
            NativeMethods.VK_TAB => shift ? UiaKeyCommand.ShiftTab : UiaKeyCommand.Tab,
            NativeMethods.VK_LEFT => UiaKeyCommand.Left,
            NativeMethods.VK_RIGHT => UiaKeyCommand.Right,
            NativeMethods.VK_UP => UiaKeyCommand.Up,
            NativeMethods.VK_DOWN => UiaKeyCommand.Down,
            NativeMethods.VK_HOME => UiaKeyCommand.Home,
            NativeMethods.VK_END => UiaKeyCommand.End,
            NativeMethods.VK_RETURN => UiaKeyCommand.Enter,
            NativeMethods.VK_SPACE => UiaKeyCommand.Space,
            NativeMethods.VK_ESCAPE => UiaKeyCommand.Escape,
            _ => (UiaKeyCommand?)null,
        };

        if (command is null)
        {
            return NativeMethods.User32.DefWindowProcW(_hwnd, NativeMethods.WM_KEYDOWN, wParam, lParam);
        }

        try
        {
            return keyboard.HandleKey(command.Value)
                ? IntPtr.Zero
                : NativeMethods.User32.DefWindowProcW(_hwnd, NativeMethods.WM_KEYDOWN, wParam, lParam);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(PlatformLogCategory.Platform, "layered-host.uia-key-failed",
                "The island keyboard handler threw; the key was ignored.", ex);
            return NativeMethods.User32.DefWindowProcW(_hwnd, NativeMethods.WM_KEYDOWN, wParam, lParam);
        }
    }

    private void OnWindowFocusChanged(bool focused)
    {
        Volatile.Write(ref _uiaFocused, focused);
        UpdateUiaGeometry();
        try
        {
            Volatile.Read(ref _uiaKeyboard)?.NotifyWindowFocus(focused);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(PlatformLogCategory.Platform, "layered-host.uia-focus-failed",
                "The island focus sink threw; keyboard focus state was still updated.", ex);
        }
    }

    /// <summary>
    /// Gives the island keyboard focus on explicit assistive-technology request. Pointer clicks still
    /// return <c>MA_NOACTIVATE</c>; only UIA <c>SetFocus</c>/keyboard navigation takes focus.
    /// </summary>
    private void FocusForAccessibility()
    {
        if (!_created || !_visible)
        {
            return;
        }

        var foreground = NativeMethods.User32.GetForegroundWindow();
        var foregroundThread = foreground != IntPtr.Zero
            ? NativeMethods.User32.GetWindowThreadProcessId(foreground, out _)
            : 0;
        var currentThread = NativeMethods.Kernel32.GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != currentThread
            && NativeMethods.User32.AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            _ = NativeMethods.User32.SetForegroundWindow(_hwnd);
            _ = NativeMethods.User32.SetFocus(_hwnd);
        }
        finally
        {
            if (attached)
            {
                _ = NativeMethods.User32.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }

    private void InvokeSource(Action action, string eventId)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(PlatformLogCategory.Platform, eventId,
                "The island accessibility source threw; the request was ignored.", ex);
        }
    }

    private void PostToUi(Action action)
    {
        if (Environment.CurrentManagedThreadId == _ownerThreadId)
        {
            action();
            return;
        }

        var context = _uiaSynchronizationContext;
        if (context is null)
        {
            _logger.LogDebug(PlatformLogCategory.Platform, "layered-host.uia-thread-dropped",
                "A UIA request arrived off the UI thread and no synchronization context is available.");
            return;
        }

        context.Post(static state => ((Action)state!)(), action);
    }

    private void DisconnectRoot(UiaElementProvider? root)
    {
        if (root is null)
        {
            return;
        }

        try
        {
            _ = UiaNative.UiaDisconnectProvider(root);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(PlatformLogCategory.Platform, "layered-host.uia-disconnect-failed",
                "UiaDisconnectProvider failed. " + ex.Message);
        }
    }

    private void DisposeAccessibility()
    {
        if (_uiaSource is { } source)
        {
            source.Changed -= OnAccessibilitySourceChanged;
        }

        _uiaSource = null;
        _uiaKeyboard = null;
        Volatile.Write(ref _uiaSnapshot, null);
        var root = Interlocked.Exchange(ref _uiaRootProvider, null);
        DisconnectRoot(root);
    }
}
