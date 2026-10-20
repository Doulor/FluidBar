using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.Views.Settings;

namespace FluidBar.Ui.Tests;

/// <summary>
/// The "Agent support" settings page: static, read-only status for every agent (native/partial/
/// not detected/not possible), no install actions, bilingual.
/// </summary>
public sealed class AgentSupportSectionTests
{
    private static async Task<SettingsWindow> ShowSupportAsync(string culture)
    {
        var localizer = TestFixtures.CreateLocalizer(culture);
        var (controller, _) = TestFixtures.CreateSettings(AppSettings.Default);
        var window = new SettingsWindow(controller, localizer, SettingsWindowSection.AgentSupport);
        window.Show();
        await Task.Delay(120);
        return window;
    }

    [Fact]
    public async Task SupportPage_ListsEveryAgentWithoutInstallActions()
    {
        await AvaloniaTest.Run(async () =>
        {
            var window = await ShowSupportAsync("en-US");
            try
            {
                var texts = window.GetVisualDescendants().OfType<TextBlock>()
                    .Select(text => text.Text ?? string.Empty).ToArray();
                Assert.Contains("Agent support", texts);
                foreach (var name in new[] { "DeepSeek Harness", "Claude Code", "Codex", "GitHub Copilot",
                             "Aider", "Cursor", "Windsurf", "Qoder", "zcode", "WorkBuddy" })
                {
                    Assert.Contains(name, texts);
                }

                var buttons = window.GetVisualDescendants().OfType<Button>().ToList();
                Assert.DoesNotContain(buttons, button => Equals(button.Content, "Install"));
                Assert.DoesNotContain(buttons, button => Equals(button.Content, "Remove"));
                Assert.Contains("Read-only native detection", texts);
                Assert.Contains("No readable local state", texts);
                Assert.Contains(texts, text => text.Contains("SQLite", StringComparison.Ordinal));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task SupportPage_RendersInChinese()
    {
        await AvaloniaTest.Run(async () =>
        {
            var window = await ShowSupportAsync("zh-Hans");
            try
            {
                var texts = window.GetVisualDescendants().OfType<TextBlock>()
                    .Select(text => text.Text ?? string.Empty).ToArray();
                Assert.Contains("代理支持", texts);
                Assert.Contains("原生支持", texts);
                Assert.Contains("未检测到", texts);
                Assert.Contains("不可行", texts);
                Assert.Contains("数据来源", texts);
                Assert.Contains(texts, text => text.Contains("SQLite", StringComparison.Ordinal));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task SupportPage_ExplainsInfeasibleAgents()
    {
        await AvaloniaTest.Run(async () =>
        {
            var window = await ShowSupportAsync("en-US");
            try
            {
                var texts = window.GetVisualDescendants().OfType<TextBlock>()
                    .Select(text => text.Text ?? string.Empty).ToArray();
                Assert.Contains(texts, text => text.Contains("Electron desktop client", StringComparison.Ordinal));
                Assert.Contains(texts, text => text.Contains("Pure IDE agent", StringComparison.Ordinal));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task SupportSection_IsReachableFromTheNavigation()
    {
        await AvaloniaTest.Run(async () =>
        {
            var window = await ShowSupportAsync("en-US");
            try
            {
                Assert.Equal(SettingsWindowSection.AgentSupport, window.CurrentSection);
                Assert.Contains("Agent support", window.GetVisualDescendants().OfType<TextBlock>()
                    .Select(text => text.Text ?? string.Empty));
            }
            finally
            {
                window.Close();
            }
        });
    }
}
