using FluidBar.Platform.Windows.Interop;

namespace FluidBar.Platform.Windows.Policies;

/// <summary>
/// Shell window classes that must never be covered by the island. When one of these is in the
/// foreground (taskbar, Start menu, search, task view, overflow flyout, tooltips, menus) the island
/// must not re-assert topmost — fighting the shell produces flicker, swallowed clicks and, on some
/// Windows builds, a shell that never closes.
/// </summary>
public static class ShellWindowClasses
{
    /// <summary>Classes from the design contract (spec §5.6). Matching is case-insensitive and exact.</summary>
    public static readonly IReadOnlySet<string> Blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow",
        "Xaml_WindowedPopupClass",
        "MultitaskingViewFrame",
        "TopLevelWindowForOverflowXamlIsland",
        "Progman",
        "WorkerW",
        "#32768",
        "tooltips_class32",
    };

    /// <summary>Returns true when <paramref name="className"/> belongs to the Windows shell/UI surface.</summary>
    public static bool IsShellWindowClass(string? className)
        => !string.IsNullOrWhiteSpace(className) && Blocked.Contains(className);
}

/// <summary>Action the island host should take after evaluating the foreground window.</summary>
public enum TopmostDecision
{
    /// <summary>Re-assert <c>HWND_TOPMOST</c> (throttled, <c>SWP_NOACTIVATE</c>).</summary>
    Reassert,

    /// <summary>Island is hidden; nothing to do.</summary>
    SkipHidden,

    /// <summary>Shell window owns the foreground (Start menu, taskbar, task view…); never raise above it.</summary>
    SkipShellForeground,

    /// <summary>Full-screen/busy/presentation mode is active; the island should be hidden, not raised.</summary>
    SkipSuppressed,

    /// <summary>Already asserted recently; throttle to ≤1 Hz.</summary>
    SkipThrottled,
}

/// <summary>Inputs for <see cref="TopmostPolicy.Evaluate"/>.</summary>
/// <param name="ForegroundWindowClass">Class name of the current foreground window, if any.</param>
/// <param name="IslandVisible">Whether the island host window is currently visible.</param>
/// <param name="UserNotificationState">Result of <c>SHQueryUserNotificationState</c>, if queried.</param>
/// <param name="TimeSinceLastAssert">Time since the last successful topmost assertion.</param>
public readonly record struct TopmostContext(
    string? ForegroundWindowClass,
    bool IslandVisible,
    int? UserNotificationState,
    TimeSpan TimeSinceLastAssert);

/// <summary>Pure topmost re-assertion policy. Win32 calls are the caller's job; the decision is testable.</summary>
public static class TopmostPolicy
{
    /// <summary>Re-assert at most once per second (design contract: low-frequency, not per event).</summary>
    public static readonly TimeSpan MinimumAssertInterval = TimeSpan.FromSeconds(1);

    /// <summary>Decides whether the island may raise itself above the foreground window.</summary>
    public static TopmostDecision Evaluate(in TopmostContext context)
    {
        if (!context.IslandVisible)
        {
            return TopmostDecision.SkipHidden;
        }

        if (IsSuppressingUserNotificationState(context.UserNotificationState))
        {
            return TopmostDecision.SkipSuppressed;
        }

        if (ShellWindowClasses.IsShellWindowClass(context.ForegroundWindowClass))
        {
            return TopmostDecision.SkipShellForeground;
        }

        if (context.TimeSinceLastAssert >= TimeSpan.Zero && context.TimeSinceLastAssert < MinimumAssertInterval)
        {
            return TopmostDecision.SkipThrottled;
        }

        return TopmostDecision.Reassert;
    }

    /// <summary>
    /// True for states in which every island activity must be suppressed: exclusive full-screen,
    /// presentation mode and "busy" (e.g. UAC/secure desktop transitions).
    /// </summary>
    public static bool IsSuppressingUserNotificationState(int? state) => state is
        NativeMethods.QUNS_RUNNING_D3D_FULL_SCREEN or
        NativeMethods.QUNS_PRESENTATION_MODE or
        NativeMethods.QUNS_BUSY or
        NativeMethods.QUNS_NOT_PRESENT;

    /// <summary>True for the states that indicate a full-screen app for the "hide the island" rule.</summary>
    public static bool IsFullScreenState(int? state) => state is
        NativeMethods.QUNS_RUNNING_D3D_FULL_SCREEN or
        NativeMethods.QUNS_PRESENTATION_MODE;
}
