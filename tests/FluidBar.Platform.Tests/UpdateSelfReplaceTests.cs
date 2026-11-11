using System.Diagnostics;
using FluidBar.Platform.Windows.Update;

namespace FluidBar.Platform.Tests;

/// <summary>The self-replace helper must quote single quotes and keep non-ASCII paths intact.</summary>
public sealed class UpdateSelfReplaceTests
{
    [Theory]
    [InlineData(@"C:\Users\朋友\Downloads\FluidBar (1).exe")]
    [InlineData(@"C:\下载\小岛.exe")]
    public void Portable_update_replaces_the_actual_host_even_when_the_browser_renamed_it(string current)
        => Assert.True(ProcessUpdateInstallerLauncher.CanSelfReplace(@"C:\更新\FluidBar.exe", current));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Launcher_refuses_shutdown_when_the_helper_cannot_prepare(bool missingPowerShell)
    {
        var root = Path.Combine(Path.GetTempPath(), "FluidBarTests", "self-replace-preflight", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "FluidBar (1).exe");
        try
        {
            var source = Path.Combine(root, "FluidBar.exe");
            File.WriteAllText(source, "new-build");
            File.WriteAllText(target, "old-build");
            if (!missingPowerShell)
                File.SetAttributes(target, FileAttributes.ReadOnly);
            var launcher = new ProcessUpdateInstallerLauncher(target, Environment.ProcessId,
                missingPowerShell ? Path.Combine(root, "missing-powershell.exe") : null);

            Assert.False(launcher.Launch(source));
            Assert.Equal("old-build", File.ReadAllText(target));
            Assert.Empty(Directory.GetFiles(root, "*.update"));
            if (!missingPowerShell)
                Assert.Contains("read-only", File.ReadAllText(Path.Combine(root, "install.log")));
        }
        finally
        {
            File.SetAttributes(target, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Script_restarts_the_updated_program_or_restores_the_old_one_when_launch_fails(bool brokenDownload)
    {
        var root = Path.Combine(Path.GetTempPath(), "FluidBarTests", "下载 O'Brien", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "FluidBar.exe");
            var target = Path.Combine(root, "FluidBar (1).exe");
            var marker = Path.Combine(root, "restarted.txt");
            var scriptPath = Path.Combine(root, "update.ps1");
            var compilePath = Path.Combine(root, "compile.ps1");
            var probePath = brokenDownload ? target : source;
            var code = "class UpdateProbe { static void Main() { System.IO.File.WriteAllText(System.Environment.GetEnvironmentVariable(\"FLUIDBAR_UPDATE_PROBE_MARKER\"), \"started\"); } }";
            ProcessUpdateInstallerLauncher.WriteSelfReplaceScript(compilePath,
                $"$ErrorActionPreference = 'Stop'\nAdd-Type -TypeDefinition '{code}' -OutputAssembly '{probePath.Replace("'", "''")}' -OutputType WindowsApplication");
            using (var compiler = Process.Start(PowerShell(compilePath)))
            {
                Assert.NotNull(compiler);
                Assert.True(compiler.WaitForExit(20_000));
                Assert.Equal(0, compiler.ExitCode);
            }
            var expected = File.ReadAllBytes(probePath);
            if (brokenDownload)
                File.WriteAllText(source, "broken executable");
            else
                File.WriteAllText(target, "old-build");
            ProcessUpdateInstallerLauncher.WriteSelfReplaceScript(scriptPath,
                ProcessUpdateInstallerLauncher.BuildSelfReplaceScript(source, target, 999999));
            var start = PowerShell(scriptPath);
            start.Environment["FLUIDBAR_UPDATE_PROBE_MARKER"] = marker;
            using var helper = Process.Start(start);
            Assert.NotNull(helper);
            Assert.True(helper.WaitForExit(20_000));
            Assert.True(helper.ExitCode == (brokenDownload ? 1 : 0), File.ReadAllText(scriptPath + ".log"));
            Assert.True(SpinWait.SpinUntil(() => File.Exists(marker) && new FileInfo(marker).Length > 0, 10_000),
                "updated executable did not restart: " + File.ReadAllText(scriptPath + ".log"));
            Assert.Equal("started", File.ReadAllText(marker));
            Assert.Equal(expected, File.ReadAllBytes(target));
            if (brokenDownload)
                Assert.Contains("Restored previous executable", File.ReadAllText(scriptPath + ".log"));
            Assert.Empty(Directory.GetFiles(root, "*.backup"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ProcessStartInfo PowerShell(string scriptPath) => new("powershell.exe")
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptPath },
    };

    [Fact]
    public void Script_preserves_the_old_file_and_records_a_missing_download()
    {
        var root = Path.Combine(Path.GetTempPath(), "FluidBarTests", "self-replace-failure", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var target = Path.Combine(root, "FluidBar.exe");
            var scriptPath = Path.Combine(root, "update.ps1");
            File.WriteAllText(target, "old-build");
            ProcessUpdateInstallerLauncher.WriteSelfReplaceScript(scriptPath,
                ProcessUpdateInstallerLauncher.BuildSelfReplaceScript(Path.Combine(root, "missing.exe"), target, 999999, restart: false));
            using var helper = Process.Start(new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList = { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath },
            });
            Assert.NotNull(helper);
            Assert.True(helper.WaitForExit(10_000));
            Assert.NotEqual(0, helper.ExitCode);
            Assert.Equal("old-build", File.ReadAllText(target));
            Assert.True(File.Exists(scriptPath + ".log"), "failed update must leave a diagnostic log");
            Assert.Contains("failed", File.ReadAllText(scriptPath + ".log"), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Script_retries_a_target_lock_that_outlives_the_old_process()
    {
        var root = Path.Combine(Path.GetTempPath(), "FluidBarTests", "self-replace-lock", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "download.exe");
            var target = Path.Combine(root, "FluidBar.exe");
            var ready = Path.Combine(root, "started.txt");
            var scriptPath = Path.Combine(root, "update.ps1");
            File.WriteAllText(source, "new-build");
            File.WriteAllText(target, "old-build");
            ProcessUpdateInstallerLauncher.WriteSelfReplaceScript(scriptPath,
                $"[IO.File]::WriteAllText('{ready.Replace("'", "''")}', 'started')\n" +
                ProcessUpdateInstallerLauncher.BuildSelfReplaceScript(source, target, 999999, restart: false));

            using var targetLock = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var helper = Process.Start(new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList = { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath },
            });
            Assert.NotNull(helper);
            try
            {
                var deadline = Stopwatch.StartNew();
                while (!File.Exists(ready) && !helper.HasExited && deadline.Elapsed < TimeSpan.FromSeconds(10))
                    await Task.Delay(20);
                Assert.True(File.Exists(ready), "helper did not start");
                var exitedWhileLocked = helper.WaitForExit(1800);
                targetLock.Dispose();
                Assert.True(helper.WaitForExit(10_000), "self-replace helper timed out after releasing the lock");
                Assert.False(exitedWhileLocked, "helper abandoned the update on a temporary file lock: " + File.ReadAllText(scriptPath + ".log"));
                Assert.True(helper.ExitCode == 0, File.ReadAllText(scriptPath + ".log"));
                Assert.Equal("new-build", File.ReadAllText(target));
            }
            finally
            {
                targetLock.Dispose();
                if (!helper.HasExited)
                {
                    helper.Kill(entireProcessTree: true);
                    helper.WaitForExit();
                }
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Script_quotes_pid_and_escapes_powershell_literals()
    {
        var script = ProcessUpdateInstallerLauncher.BuildSelfReplaceScript(
            @"C:\下载\O'Brien\FluidBar.exe",
            @"C:\Program Files\FluidBar\FluidBar.exe",
            4321);

        Assert.Contains("Wait-Process -Id 4321", script);
        Assert.Contains(@"$source = 'C:\下载\O''Brien\FluidBar.exe'", script);
        Assert.Contains(@"$target = 'C:\Program Files\FluidBar\FluidBar.exe'", script);
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
            Assert.True(process.ExitCode == 0, File.ReadAllText(scriptPath + ".log"));
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
            Assert.True(helper.ExitCode == 0, File.ReadAllText(scriptPath + ".log"));
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
