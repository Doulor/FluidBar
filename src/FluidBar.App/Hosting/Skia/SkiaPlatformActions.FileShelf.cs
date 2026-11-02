namespace FluidBar.App.Hosting.Skia;

public sealed partial class SkiaPlatformActions
{
    /// <summary>Validates references off the UI thread, then writes a CF_HDROP copy payload.</summary>
    public Task<bool> CopyShelfFilesAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        var snapshot = paths.ToArray();
        return Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (snapshot.Length is 0 or > 32 || snapshot.Any(path =>
                    !Path.IsPathFullyQualified(path) || (!File.Exists(path) && !Directory.Exists(path))))
                return false;
            return await _clipboard.SetFilesAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }
}
