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
