using System.Diagnostics;
using FluidBar.Platform.Windows.Update;

namespace FluidBar.Platform.Tests;

/// <summary>The self-replace helper must quote single quotes and keep non-ASCII paths intact.</summary>
public sealed class UpdateSelfReplaceTests
{
    [Fact]
    public void Script_quotes_pid_and_escapes_powershell_literals()
    {
        var script = ProcessUpdateInstallerLauncher.BuildSelfReplaceScript(
            @"C:\下载\O'Brien\FluidBar.exe",
            @"C:\Program Files\FluidBar\FluidBar.exe",
            4321);

        Assert.Contains("Wait-Process -Id 4321", script);
        Assert.Contains(@"-LiteralPath 'C:\下载\O''Brien\FluidBar.exe'", script);
        Assert.Contains(@"-Destination 'C:\Program Files\FluidBar\FluidBar.exe'", script);
        Assert.Contains("Start-Process -FilePath 'C:\\Program Files\\FluidBar\\FluidBar.exe'", script);
        Assert.Contains("Remove-Item -LiteralPath $PSCommandPath -Force", script);
    }

    [Fact]
    public void Script_copies_the_download_to_the_target_without_restarting_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "FluidBarTests", "self-replace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "download.txt");
            var target = Path.Combine(root, "FluidBar.exe");
            var scriptPath = Path.Combine(root, "update.ps1");
            File.WriteAllText(source, "new-build");
            File.WriteAllText(target, "old-build");
            ProcessUpdateInstallerLauncher.WriteSelfReplaceScript(
                scriptPath,
                ProcessUpdateInstallerLauncher.BuildSelfReplaceScript(source, target, 999999, restart: false));

            var start = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            };
            using var process = Process.Start(start);
            Assert.NotNull(process);
            Assert.True(process.WaitForExit(10_000), "self-replace helper timed out");
            Assert.Equal(0, process.ExitCode);
            Assert.Equal("new-build", File.ReadAllText(target));
            Assert.False(File.Exists(scriptPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Script_waits_for_a_running_pid_before_replacing_the_exe()
    {
        var root = Path.Combine(Path.GetTempPath(), "FluidBarTests", "self-replace-running", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "download", "FluidBar.exe");
            var target = Path.Combine(root, "install", "FluidBar.exe");
            var scriptPath = Path.Combine(root, "update.ps1");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(source, "new-build");
            File.WriteAllText(target, "old-build");

            var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            using var running = Process.Start(new ProcessStartInfo(cmd, "/c ping -n 3 127.0.0.1 >nul")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            Assert.NotNull(running);
            ProcessUpdateInstallerLauncher.WriteSelfReplaceScript(
                scriptPath,
                ProcessUpdateInstallerLauncher.BuildSelfReplaceScript(source, target, running.Id, restart: false));

            var start = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            };
            using var helper = Process.Start(start);
            Assert.NotNull(helper);
            Assert.True(helper.WaitForExit(10_000), "self-replace helper timed out");
            Assert.Equal(0, helper.ExitCode);
            Assert.Equal("new-build", File.ReadAllText(target));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Script_is_written_as_utf8_with_bom()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fluidbar-update-test-{Guid.NewGuid():N}.ps1");
        try
        {
            ProcessUpdateInstallerLauncher.WriteSelfReplaceScript(path, "# 中文路径\n");

            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length >= 3);
            Assert.Equal(0xEF, bytes[0]);
            Assert.Equal(0xBB, bytes[1]);
            Assert.Equal(0xBF, bytes[2]);
            Assert.Contains("中文路径", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
