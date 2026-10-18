namespace FluidBar.Plugins.SampleRich.Core;

/// <summary>
/// A private helper library for the rich sample plugin. It is not part of any contract: it is copied into the
/// plugin package under <c>plugin/</c> and resolved by the plugin's AssemblyLoadContext (never shared with the
/// host). This exists to prove local dependency packaging and per-plugin isolation.
/// </summary>
public static class TrackFormatter
{
    public const string LibraryVersion = "1.0.0";

    public static string Format(string title, string artist)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(artist);
        return $"{title} \u2014 {artist}";
    }

    public static string Badge(string title) => title.Length <= 12 ? "TRACK" : "NOW PLAYING";
}
