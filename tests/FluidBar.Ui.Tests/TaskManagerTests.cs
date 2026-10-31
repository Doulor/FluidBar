using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FluidBar.App.State;
using FluidBar.App.Views;
using FluidBar.App.Hosting.Skia;
using FluidBar.Island;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Tasks;
using FluidBar.Platform.Windows.Accessibility;
using SkiaSharp;

namespace FluidBar.Ui.Tests;

public sealed class TaskManagerTests
{
    [Fact]
    public void EmptyCard_HasManagementHitTargetWithoutAnyTasks()
    {
        var page = new SKRect(0, 0, 432, 280);
        var metrics = FontScaleMetrics.From(1);
        var button = PageMetricsLayout.WidgetsTaskManage(page, metrics);
        var hit = PageMetricsLayout.HitTest(PageSlotKind.Widgets, page, button.MidX, button.MidY, metrics, 0, 0, out _);
        Assert.Equal(IslandPageControl.TaskManage, hit);
    }

    [Fact]
    public void EmptyCard_DoesNotExposeTheUsersFilePath()
    {
        var mapped = WidgetContentMapper.MapTasks(new TaskListSnapshot([], TaskListStatus.Missing,
            @"C:\Users\private\tasks-secret.json", null));
        Assert.DoesNotContain("tasks-secret", mapped.EmptyHint);
        Assert.Contains("Add", mapped.EmptyHint);
    }

    [Fact]
    public void TaskManagement_IsReachableThroughAccessibilityAndActionRouting()
    {
        var actions = new RecordingIslandPlatformActions();
        using var fixture = ControllerFixture.Create(actions: actions);
        using var source = new SkiaIslandUiaSource(fixture.Controller, new FakeLocalizer(), new SkiaHostDispatcher());
        fixture.ExpandAndSettle();
        fixture.GoToPage(5);
        Assert.NotNull(source.BuildSnapshot().Find("island.page.5.taskmanage"));
        source.Perform(new UiaElementActionRequest(UiaElementAction.Invoke, "island.page.5.taskmanage"));
        Assert.Equal(IslandPlatformActionKind.TaskManage, Assert.Single(actions.Calls).Kind);
    }

    [Fact]
    public async Task Window_AddEditToggleDelete_UsesTheServiceAndShowsEmptyState()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fluidbar-task-ui-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "tasks.json");
        await using var service = new TaskListService(NullPlatformLogger.Instance, new PlatformCounters(),
            new TaskListOptions { FilePath = path });
        try
        {
            await AvaloniaTest.RunAsync(async () =>
            {
                var window = new TaskManagerWindow(service, TestFixtures.CreateLocalizer("en-US"));
                try
                {
                    window.Show();
                    await WaitUntil(() => Buttons(window, "task-add").Any());
                    var input = window.GetVisualDescendants().OfType<TextBox>().Single(control => control.Name == "task-title");
                    input.Text = "  preserve me  ";
                    Click(Buttons(window, "task-add").Single());
                    await WaitUntil(() => Buttons(window, "task-edit-0").Any());
                    Assert.Equal("  preserve me  ", service.Load().Items[0].Title);
                    Click(Buttons(window, "task-edit-0").Single());
                    input.Text = "renamed";
                    Click(Buttons(window, "task-add").Single());
                    await WaitUntil(() => service.Current?.Items[0].Title == "renamed");
                    var check = window.GetVisualDescendants().OfType<CheckBox>().Single();
                    check.IsChecked = true;
                    await WaitUntil(() => service.Current?.Items[0].Done == true);
                    Click(Buttons(window, "task-delete-0").Single());
                    await WaitUntil(() => service.Current?.Count == 0);
                    Assert.Empty(service.Load().Items);
                }
                finally { window.Close(); }
            });
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task InvalidFile_ShowsFriendlyErrorAndPreventsEdits()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fluidbar-task-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "private-tasks.json");
        await File.WriteAllTextAsync(path, "{ broken");
        await using var service = new TaskListService(NullPlatformLogger.Instance, new PlatformCounters(),
            new TaskListOptions { FilePath = path });
        try
        {
            await AvaloniaTest.RunAsync(async () =>
            {
                var localizer = TestFixtures.CreateLocalizer("en-US");
                var window = new TaskManagerWindow(service, localizer);
                try
                {
                    window.Show();
                    await WaitUntil(() => window.GetVisualDescendants().OfType<TextBlock>()
                        .Any(text => text.Text == localizer.Get("Tasks.Manage.Invalid")));
                    Assert.Empty(Buttons(window, "task-add"));
                    Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(),
                        text => text.Text?.Contains("private-tasks", StringComparison.Ordinal) == true);
                }
                finally { window.Close(); }
            });
            Assert.Equal("{ broken", await File.ReadAllTextAsync(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static IEnumerable<Button> Buttons(Window window, string name)
        => window.GetVisualDescendants().OfType<Button>().Where(button => button.Name == name && button.IsEffectivelyEnabled);

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(20);
        Assert.True(condition(), "The task manager did not reach the expected state.");
    }
}
