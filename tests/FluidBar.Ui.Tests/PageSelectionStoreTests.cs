using System.Text.Json;
using FluidBar.App.Hosting.Skia;

namespace FluidBar.Ui.Tests;

/// <summary>Page-selection writes stay inside the configured settings root and land asynchronously.</summary>
public sealed class PageSelectionStoreTests
{
    [Fact]
    public async Task Save_uses_the_isolated_settings_root()
    {
        var previous = Environment.GetEnvironmentVariable("FLUIDBAR_SETTINGS_DIR");
        var root = Path.Combine(Path.GetTempPath(), "FluidBarTests", "page-selection", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Environment.SetEnvironmentVariable("FLUIDBAR_SETTINGS_DIR", root);
            PageSelectionStore.Save(3);

            var path = Path.Combine(root, "ui-page.json");
            for (var i = 0; i < 100 && !File.Exists(path); i++)
            {
                await Task.Delay(10);
            }

            Assert.True(File.Exists(path));
            var json = await File.ReadAllTextAsync(path);
            using var document = JsonDocument.Parse(json);
            Assert.Equal(3, document.RootElement.GetProperty("index").GetInt32());
        }
        finally
        {
            Environment.SetEnvironmentVariable("FLUIDBAR_SETTINGS_DIR", previous);
            Directory.Delete(root, recursive: true);
        }
    }
}
