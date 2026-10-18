using FluidBar.Integration.Tests.TestSupport;
using FluidBar.Platform.Windows.Agent;
using FluidBar.Plugins.Builtin;
using FluidBar.Sdk;

namespace FluidBar.Integration.Tests;

/// <summary>
/// Seam 3: agent hook installation is consent-gated. Starting the plugin can never auto-install hooks;
/// only a settings-surface invocation carrying explicit <c>consent=true</c> reaches the platform installer.
/// </summary>
public class AgentConsentTests
{
    private static BuiltinPlatformServices AgentOnlyServices(FakeAgentInboxSource inbox, FakeHookInstaller installer)
        => new()
        {
            AgentInbox = inbox,
            AgentHookInstaller = installer,
        };

    [Fact]
    public async Task Agent_plugin_never_installs_hooks_on_start()
    {
        var inbox = new FakeAgentInboxSource();
        var installer = new FakeHookInstaller();
        await using var host = await BuiltinPluginHost.StartAsync(
            AgentOnlyServices(inbox, installer), TestTimeProvider.StartingAtEpoch());

        Assert.Single(host.Plugins);
        Assert.Equal(BuiltinIds.AgentPlugin, host.Plugins[0].Descriptor.Id);
        Assert.Equal(0, installer.InstallCalls);
        Assert.Equal(0, installer.UninstallCalls);
        Assert.False(await installer.IsInstalledAsync());
    }

    [Fact]
    public async Task Install_command_without_consent_is_ignored_and_warns()
    {
        var inbox = new FakeAgentInboxSource();
        var installer = new FakeHookInstaller();
        await using var host = await BuiltinPluginHost.StartAsync(
            AgentOnlyServices(inbox, installer), TestTimeProvider.StartingAtEpoch());
        var context = host.Get(BuiltinIds.AgentPlugin).Context;

        var deniedContext = new PluginCommandContext
        {
            PluginId = BuiltinIds.AgentPlugin,
            CommandId = BuiltinIds.AgentInstallHooks,
            Source = CommandInvocationSource.Settings,
        };
        await context.CommandRegistry.InvokeAsync(BuiltinIds.AgentInstallHooks, deniedContext);

        Assert.Equal(0, installer.InstallCalls);
        Assert.Contains(context.NotificationSink.Notifications, notification =>
            notification.Title == "agent-hooks-consent-required");

        // Non-settings surfaces are also rejected even if they try to pass consent.
        var wrongSurface = new PluginCommandContext
        {
            PluginId = BuiltinIds.AgentPlugin,
            CommandId = BuiltinIds.AgentRepairHooks,
            Source = CommandInvocationSource.ContextMenu,
            Arguments = new Dictionary<string, string?>(StringComparer.Ordinal) { ["consent"] = "true" },
        };
        await context.CommandRegistry.InvokeAsync(BuiltinIds.AgentRepairHooks, wrongSurface);
        Assert.Equal(0, installer.InstallCalls);
    }

    [Fact]
    public async Task Install_repair_uninstall_with_explicit_settings_consent_reach_platform_installer()
    {
        var inbox = new FakeAgentInboxSource();
        var installer = new FakeHookInstaller();
        await using var host = await BuiltinPluginHost.StartAsync(
            AgentOnlyServices(inbox, installer), TestTimeProvider.StartingAtEpoch());
        var context = host.Get(BuiltinIds.AgentPlugin).Context;

        PluginCommandContext Consent(string commandId) => new()
        {
            PluginId = BuiltinIds.AgentPlugin,
            CommandId = commandId,
            Source = CommandInvocationSource.Settings,
            Arguments = new Dictionary<string, string?>(StringComparer.Ordinal) { ["consent"] = "true" },
        };

        await context.CommandRegistry.InvokeAsync(BuiltinIds.AgentInstallHooks, Consent(BuiltinIds.AgentInstallHooks));
        Assert.Equal(1, installer.InstallCalls);
        Assert.True(await installer.IsInstalledAsync());

        await context.CommandRegistry.InvokeAsync(BuiltinIds.AgentRepairHooks, Consent(BuiltinIds.AgentRepairHooks));
        Assert.Equal(2, installer.InstallCalls);

        await context.CommandRegistry.InvokeAsync(BuiltinIds.AgentUninstallHooks, Consent(BuiltinIds.AgentUninstallHooks));
        Assert.Equal(1, installer.UninstallCalls);
        Assert.False(await installer.IsInstalledAsync());
        Assert.Contains(context.NotificationSink.Notifications, notification => notification.Title == "agent-hooks-install-ok");
    }

    [Fact]
    public void Consent_rule_is_pure_and_documented()
    {
        Assert.True(AgentStatusPlugin.IsConsentGranted(new PluginCommandContext
        {
            PluginId = BuiltinIds.AgentPlugin,
            CommandId = BuiltinIds.AgentInstallHooks,
            Source = CommandInvocationSource.Settings,
            Arguments = new Dictionary<string, string?>(StringComparer.Ordinal) { ["consent"] = "TRUE" },
        }));

        Assert.False(AgentStatusPlugin.IsConsentGranted(new PluginCommandContext
        {
            PluginId = BuiltinIds.AgentPlugin,
            CommandId = BuiltinIds.AgentInstallHooks,
            Source = CommandInvocationSource.Settings,
        }));

        Assert.False(AgentStatusPlugin.IsConsentGranted(new PluginCommandContext
        {
            PluginId = BuiltinIds.AgentPlugin,
            CommandId = BuiltinIds.AgentInstallHooks,
            Source = CommandInvocationSource.Global,
            Arguments = new Dictionary<string, string?>(StringComparer.Ordinal) { ["consent"] = "true" },
        }));
    }
}
