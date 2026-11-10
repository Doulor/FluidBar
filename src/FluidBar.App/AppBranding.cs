using System.Security.Cryptography;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;

namespace FluidBar.App;

/// <summary>The executable, tray, windows and settings marks share assets derived from Assets/fluidbar.svg.</summary>
internal static class AppBranding
{
    private static readonly Lazy<WindowIcon> SharedWindowIcon = new(() =>
    {
        using var stream = OpenAsset("ico");
        return new WindowIcon(stream);
    });
    private static readonly Lazy<Bitmap> SharedLogo = new(() =>
    {
        using var stream = OpenAsset("png");
        return new Bitmap(stream);
    });
    private static readonly Lazy<string> CachedIconPath = new(CacheIcon);
    private static bool _windowDefaultsConfigured;

    public static void ConfigureWindowIcons()
    {
        if (_windowDefaultsConfigured) return;
        var application = Application.Current ?? throw new InvalidOperationException("The application is not initialized.");
        application.Styles.Add(new Style(selector => selector.Is<Window>())
        {
            Setters = { new Setter(Window.IconProperty, SharedWindowIcon.Value) },
        });
        _windowDefaultsConfigured = true;
    }

    public static Image CreateLogo(double size)
    {
        var image = new Image
        {
            Name = "fluidbar-brand-logo",
            Source = SharedLogo.Value,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        AutomationProperties.SetName(image, "FluidBar");
        return image;
    }

    /// <summary>Materializes the same multi-size ICO embedded into the EXE; the digest avoids stale tray caches.</summary>
    public static string GetIconFilePath() => CachedIconPath.Value;

    private static Stream OpenAsset(string extension)
        => typeof(AppBranding).Assembly.GetManifestResourceStream("FluidBar.AppIcon." + extension)
            ?? throw new InvalidOperationException("The FluidBar brand asset is missing: " + extension);

    private static string CacheIcon()
    {
        using var input = OpenAsset("ico");
        using var buffer = new MemoryStream();
        input.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var digest = Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant();
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluidBar", "icons");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "fluidbar-" + digest + ".ico");
        if (!File.Exists(path) || new FileInfo(path).Length != bytes.Length) File.WriteAllBytes(path, bytes);
        return path;
    }
}
