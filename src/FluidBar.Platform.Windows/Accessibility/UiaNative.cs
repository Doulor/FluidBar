using System.Runtime.InteropServices;

namespace FluidBar.Platform.Windows.Accessibility;

/// <summary>
/// UIAutomationCore entry points used by the island host. Kept next to the provider because the
/// COM interface parameters are part of the accessibility contract; the host-facing P/Invokes live
/// in <see cref="Interop.NativeMethods"/>.
/// </summary>
internal static class UiaNative
{
    private const string Lib = "UIAutomationCore.dll";

    /// <summary>
    /// Answers <c>WM_GETOBJECT</c> with a raw element provider. The return value is the window
    /// procedure's LRESULT.
    /// </summary>
    [DllImport(Lib)]
    internal static extern IntPtr UiaReturnRawElementProvider(IntPtr hwnd, IntPtr wParam, IntPtr lParam,
        [MarshalAs(UnmanagedType.Interface)] IRawElementProviderSimple provider);

    /// <summary>Whether any UIA client is listening (used to skip event work when idle).</summary>
    [DllImport(Lib)]
    internal static extern bool UiaClientsAreListening();

    /// <summary>Releases UIA's references to a provider that is being replaced/disposed.</summary>
    [DllImport(Lib)]
    internal static extern int UiaDisconnectProvider([MarshalAs(UnmanagedType.Interface)] IRawElementProviderSimple provider);

    /// <summary>Raises a structure-changed event so clients re-query the tree.</summary>
    [DllImport(Lib)]
    internal static extern int UiaRaiseStructureChangedEvent(
        [MarshalAs(UnmanagedType.Interface)] IRawElementProviderSimple provider,
        int structureChangeType,
        IntPtr runtimeId,
        int runtimeIdLength);

    /// <summary>Raises a simple automation event (focus changed).</summary>
    [DllImport(Lib)]
    internal static extern int UiaRaiseAutomationEvent(
        [MarshalAs(UnmanagedType.Interface)] IRawElementProviderSimple provider,
        int eventId);
}
