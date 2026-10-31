using FluidBar.Plugins.FileShelf;

namespace FluidBar.OptionalPlugins.Tests;

public sealed class FileReferenceShelfTests
{
    [Fact]
    public void Removes_duplicates_and_relative_paths_without_reading_original_files()
    {
        var directory = Path.Combine(Path.GetTempPath(), "shelf-" + Guid.NewGuid().ToString("N"));
        var original = Path.Combine(directory, "uncreated.txt");
        var normalized = FileReferenceShelf.Normalize([original, original, "relative.txt", "", null!]);
        Assert.Equal(original, Assert.Single(normalized));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void Caps_references_at_32()
    {
        var entries = Enumerable.Range(0, 100).Select(index => Path.Combine(Path.GetTempPath(), "shelf-" + index));
        Assert.Equal(32, FileReferenceShelf.Normalize(entries).Length);
    }
}
