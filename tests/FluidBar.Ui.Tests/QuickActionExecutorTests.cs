using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FluidBar.App.Hosting.Skia;
using FluidBar.App.Settings;
using FluidBar.App.Views.Settings;
using FluidBar.Core.Diagnostics;
using FluidBar.Core.Settings;

namespace FluidBar.Ui.Tests;

public sealed class QuickActionExecutorTests
{
    [Fact]
    public async Task Builtin_action_waits_for_the_real_result_and_rejects_failure()
    {
        using var settings = Settings();
        var completion = new TaskCompletionSource<IslandActionFeedback>();
        var executor = new QuickActionExecutor(settings, (_, _) => completion.Task,
            (_, _) => Task.CompletedTask, (_, _, _) => Task.CompletedTask, () => { });
        var running = executor.ExecuteAsync(new() { Id = "theme", Name = "Theme", Kind = "theme" });
        Assert.False(running.IsCompleted);
        completion.SetResult(new(1, default, IslandActionFeedbackStatus.Failed));
        await Assert.ThrowsAsync<InvalidOperationException>(() => running);
    }

    [Fact]
    public async Task Missing_plugin_command_propagates_failure_without_installing_anything()
    {
        using var settings = Settings();
        var executor = new QuickActionExecutor(settings, (_, _) => throw new Xunit.Sdk.XunitException("Wrong branch"),
            (_, _) => Task.CompletedTask,
            (id, command, _) => Task.FromException(new InvalidOperationException(id + "/" + command)), () => { });

        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(
            new() { Id = "timer", Name = "Timer", Kind = "plugin", Target = "fluidbar-timer/fluidbar-timer.start" }));
        Assert.DoesNotContain("fluidbar-timer", settings.Current.Plugins.Enabled);
    }

    [Fact]
    public async Task Pausing_clipboard_persists_only_after_an_explicit_invocation()
    {
        using var settings = Settings();
        var executor = new QuickActionExecutor(settings, (_, _) => throw new Xunit.Sdk.XunitException("Wrong branch"),
            (_, _) => Task.CompletedTask, (_, _, _) => Task.CompletedTask, () => { });
        Assert.False(settings.Current.ClipboardPaused);
        await executor.ExecuteAsync(new() { Id = "pause", Name = "Pause", Kind = "clipboard-pause" });
        Assert.True(settings.Current.ClipboardPaused);
    }

    [Fact]
    public void Normalization_preserves_a_noop_update()
    {
        var store = new InMemoryUiSettingsStore();
        using var settings = new SettingsController(store.LastSaved, store, NullAppLogger.Instance);
        settings.Update(current => current with { ClipboardPaused = current.ClipboardPaused });
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public Task Settings_editor_saves_edits_runs_and_removes_actions() => AvaloniaTest.Run(() =>
    {
        using var settings = Settings();
        var localizer = TestFixtures.CreateLocalizer("en-US");
        var runs = 0;
        var window = new SettingsWindow(settings, localizer, SettingsWindowSection.General,
            executeQuickAction: (_, _) => { runs++; return Task.CompletedTask; });
        try
        {
            window.Show();
            TextBox NameBox() => window.GetVisualDescendants().OfType<TextBox>()
                .Single(box => AutomationProperties.GetAutomationId(box) == "cards.action.name");
            Button Button(string key) => window.GetVisualDescendants().OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == localizer.Get("Settings.Cards." + key));
            NameBox().Text = "My theme";
            Button("Save").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Assert.Equal("My theme", Assert.Single(settings.Current.QuickActions).Name);
            Button("Edit").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            NameBox().Text = "Renamed";
            Button("Save").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Assert.Equal("Renamed", Assert.Single(settings.Current.QuickActions).Name);
            Button("Run").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Assert.Equal(1, runs);
            Button("Remove").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Assert.Empty(settings.Current.QuickActions);
        }
        finally { window.Close(); }
    });

    private static SettingsController Settings()
    {
        var store = new InMemoryUiSettingsStore();
        return new SettingsController(store.LastSaved, store, NullAppLogger.Instance);
    }
}
