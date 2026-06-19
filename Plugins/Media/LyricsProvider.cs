namespace FluidBar;

public interface ILyricsProvider
{
    string? TryGetCurrentLine(MediaSnapshot snapshot, TimeSpan position);
}

public sealed class NullLyricsProvider : ILyricsProvider
{
    public string? TryGetCurrentLine(MediaSnapshot snapshot, TimeSpan position) => null;
}

