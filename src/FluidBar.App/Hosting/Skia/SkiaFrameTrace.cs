using System.Diagnostics;
using System.Globalization;
using FluidBar.Island;

namespace FluidBar.App.Hosting.Skia;

/// <summary>
/// Opt-in, env-gated per-frame trace for the layered island host. Set
/// <c>FLUIDBAR_FRAME_TRACE=1</c> (or a file path) before launch; every rendered frame appends one CSV
/// line with shell/motion geometry and the invalidation kind, so a jump can be attributed to a frame
/// instead of inferred from screenshots.
/// </summary>
internal static class SkiaFrameTrace
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly StreamWriter? Writer = Create();

    public static bool Enabled => Writer is not null;

    public static void Frame(string kind, in IslandRenderState state, SkiaIslandMotion motion, bool agentAnimating, in SkiaIslandRenderChange change)
    {
        if (Writer is null)
        {
            return;
        }

        Writer.Write(Math.Round(Clock.Elapsed.TotalMilliseconds, 3).ToString(CultureInfo.InvariantCulture));
        Writer.Write(',');
        Writer.Write(kind);
        Writer.Write(',');
        Writer.Write(state.ShellWidth.ToString("0.###", CultureInfo.InvariantCulture));
        Writer.Write(',');
        Writer.Write(state.ShellHeight.ToString("0.###", CultureInfo.InvariantCulture));
        Writer.Write(',');
        Writer.Write(state.ShellOrigin.X.ToString("0.###", CultureInfo.InvariantCulture));
        Writer.Write(',');
        Writer.Write(state.ShellOrigin.Y.ToString("0.###", CultureInfo.InvariantCulture));
        Writer.Write(',');
        Writer.Write(state.NudgeOffsetY.ToString("0.###", CultureInfo.InvariantCulture));
        Writer.Write(',');
        Writer.Write(motion.Width.ToString("0.###", CultureInfo.InvariantCulture));
        Writer.Write(',');
        Writer.Write(motion.Height.ToString("0.###", CultureInfo.InvariantCulture));
        Writer.Write(',');
        Writer.Write(motion.IsAtRest ? '1' : '0');
        Writer.Write(',');
        Writer.Write((int)state.Form);
        Writer.Write(',');
        Writer.Write(state.PageIndex);
        Writer.Write(',');
        Writer.Write(agentAnimating ? '1' : '0');
        Writer.Write(',');
        Writer.Write(change.FullFrame ? '1' : '0');
        Writer.Write(',');
        Writer.Write(change.DamageDip.IsEmpty ? "0;0;0;0" : string.Create(CultureInfo.InvariantCulture,
            $"{change.DamageDip.X:0.###};{change.DamageDip.Y:0.###};{change.DamageDip.Width:0.###};{change.DamageDip.Height:0.###}"));
        Writer.WriteLine();
    }

    private static StreamWriter? Create()
    {
        var value = Environment.GetEnvironmentVariable("FLUIDBAR_FRAME_TRACE");
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var path = value is "1" or "true"
            ? Path.Combine(Path.GetTempPath(), "fluidbar-frame-trace.csv")
            : value;
        var writer = new StreamWriter(path, append: false);
        writer.WriteLine("ms,kind,shellW,shellH,originX,originY,nudgeY,motionW,motionH,motionRest,form,page,agent,full,damage");
        writer.Flush();
        return writer;
    }
}
