using FluidBar.App.Hosting.Pages;
using SkiaSharp;

namespace FluidBar.Ui.Tests;

public sealed class FileVisualCacheTests
{
    [Fact]
    public async Task New_clipboard_revision_reloads_a_file_overwritten_at_the_same_path()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        try
        {
            await File.WriteAllTextAsync(path, "old");
            var cache = new FileVisualCache();
            var first = await cache.LoadAsync(path, "1");
            await File.WriteAllTextAsync(path, new string('x', 2048));
            var second = await cache.LoadAsync(path, "2");
            Assert.NotEqual(first.SizeText, second.SizeText);
            Assert.Equal("2 KB", second.SizeText);
            Assert.Same(first, await cache.LoadAsync(path, "1"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Metadata_and_image_preview_are_loaded_once_and_reused_without_file_access()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        try
        {
            using (var bitmap = new SKBitmap(32, 16))
            {
                bitmap.Erase(SKColors.Coral);
                using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(path, data.ToArray());
            }
            var cache = new FileVisualCache();
            var result = await cache.LoadAsync(path);
            Assert.Equal("PNG", result.TypeLabel);
            Assert.NotEmpty(result.SizeText!);
            Assert.Equal(Path.GetDirectoryName(path), result.Directory);
            Assert.NotEmpty(result.ThumbnailPng!);
            File.Delete(path);
            Assert.Same(result, await cache.LoadAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Missing_files_keep_filename_type_and_parent_without_fabricated_size_or_image()
    {
        var cache = new FileVisualCache();
        var result = await cache.LoadAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf"));
        Assert.Equal("PDF", result.TypeLabel);
        Assert.Null(result.SizeText);
        Assert.Null(result.ThumbnailPng);
        Assert.False(result.Exists);
    }
}
