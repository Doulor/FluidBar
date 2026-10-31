using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Tasks;

namespace FluidBar.Platform.Tests;

public sealed class TaskManagementTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "fluidbar-task-management-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "nested", "tasks.json");
    private TaskListService Create(int maxTasks = 100) => new(NullPlatformLogger.Instance, new PlatformCounters(),
        new TaskListOptions { FilePath = FilePath, MaxTasks = maxTasks });

    [Fact]
    public async Task Add_CreatesMissingFile_ThenRenameToggleRemovePersist()
    {
        await using var service = Create();
        var added = await service.AddAsync("  原样保留  ");
        Assert.True(added.Succeeded);
        Assert.Equal("  原样保留  ", added.Snapshot.Items[0].Title);
        Assert.True((await service.RenameAsync(0, "new title")).Succeeded);
        Assert.True((await service.ToggleAsync(0)).Snapshot.Items[0].Done);
        Assert.True((await service.RemoveAsync(0)).Succeeded);
        Assert.Empty(service.Load().Items);
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("{\"title\":\"wrong root\"}")]
    public async Task Mutations_RefuseInvalidDocumentWithoutChangingBytes(string json)
    {
        Write(json);
        await using var service = Create();
        Assert.False((await service.AddAsync("new")).Succeeded);
        Assert.False((await service.RenameAsync(0, "new")).Succeeded);
        Assert.False((await service.RemoveAsync(0)).Succeeded);
        Assert.Equal(json, File.ReadAllText(FilePath));
    }

    [Fact]
    public async Task Editing_PreservesOtherRowsUnknownFieldsAndWhitespace()
    {
        Write("""[{"title":"  one  ","done":false,"extra":"keep"},{"legacy":42},{"title":"two","done":true}]""");
        await using var service = Create();
        Assert.True((await service.RenameAsync(1, "renamed")).Succeeded);
        var json = File.ReadAllText(FilePath);
        Assert.Contains("  one  ", json);
        Assert.Contains("\"extra\": \"keep\"", json);
        Assert.Contains("\"legacy\": 42", json);
        Assert.Equal("  one  ", service.Load().Items[0].Title);
    }

    [Fact]
    public async Task ValidationAndStaleIndices_DoNotWrite()
    {
        await using var service = Create(maxTasks: 1);
        Assert.False((await service.AddAsync(" ")).Succeeded);
        Assert.False((await service.AddAsync(new string('x', 501))).Succeeded);
        var first = await service.AddAsync("one");
        Assert.False((await service.AddAsync("two")).Succeeded);
        Assert.False((await service.RenameAsync(-1, "other")).Succeeded);
        Assert.False((await service.RemoveAsync(9)).Succeeded);
        Assert.True((await service.RenameAsync(0, "changed")).Succeeded);
        Assert.False((await service.RemoveAsync(0, expectedSnapshot: first.Snapshot)).Succeeded);
        Assert.Equal("changed", service.Load().Items[0].Title);
    }

    [Fact]
    public async Task ConcurrentAdds_AreSerializedWithoutLosingRows()
    {
        await using var service = Create();
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => service.AddAsync("task " + i)));
        Assert.All(results, result => Assert.True(result.Succeeded));
        Assert.Equal(20, service.Load().Count);
        Assert.Equal(20, service.Load().Items.Select(item => item.Title).Distinct().Count());
    }

    [Fact]
    public async Task EditingACappedList_PreservesTasksBeyondTheDisplayLimit()
    {
        Write("""[{"title":"one","done":false},{"title":"two","done":true},{"title":"  third  ","done":false}]""");
        await using var service = Create(maxTasks: 1);
        Assert.True((await service.ToggleAsync(0)).Succeeded);
        var complete = TaskListService.Parse(File.ReadAllText(FilePath), true, FilePath);
        Assert.Equal(3, complete.Count);
        Assert.Equal("two", complete.Items[1].Title);
        Assert.True(complete.Items[1].Done);
        Assert.Equal("  third  ", complete.Items[2].Title);
        Assert.False((await service.AddAsync("fourth")).Succeeded);
    }

    private void Write(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, json);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
