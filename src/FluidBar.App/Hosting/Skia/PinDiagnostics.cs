using System.Runtime.CompilerServices;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Media;

namespace FluidBar.App.Hosting.Skia;

internal static class PinDiagnostics
{
    public static void Log(ref DiagnosticMessage message, [CallerMemberName] string caller = "")
        => MediaDiagnostics.Log(ref message, caller);
    public static void Log(string message, [CallerMemberName] string caller = "")
        => MediaDiagnostics.Log(message, caller);
}
