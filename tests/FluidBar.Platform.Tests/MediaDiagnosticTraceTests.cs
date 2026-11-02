using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Media;

namespace FluidBar.Platform.Tests;

public sealed class MediaDiagnosticTraceTests
{
    [Fact]
    public async Task Enabled_diagnostics_redact_text_and_use_a_bounded_background_rolling_sink()
    {
        var root = Path.Combine(Path.GetTempPath(), "FluidBarTests", "rolling-diag", Guid.NewGuid().ToString("N"));
        try
        {
            await using (var trace = new DiagnosticTrace(new RollingLogOptions
            { DirectoryPath = root, FilePrefix = "trace", MaxFileBytes = 2048, MaxFiles = 2,
                ChannelCapacity = 256, RateLimitPerCategoryPerSecond = 1000, MaxEntryBytes = 1024 }, () => true))
            {
                var privateText = "do not retain: OTP 847291 ' private\\folder";
                trace.Log($"title='{privateText}' position={12.3:0.0} playing={true}");
                trace.Log(privateText);
                await trace.FlushAsync();
                var messages = Directory.GetFiles(root).SelectMany(ReadSharedLines).Select(line =>
                {
                    using var json = System.Text.Json.JsonDocument.Parse(line);
                    return json.RootElement.GetProperty("msg").GetString()!;
                }).ToArray();
                Assert.Contains(messages, message => message.Contains("12.3"));
                Assert.Contains(messages, message => message.Contains("<redacted>"));
                Assert.All(messages, message => { Assert.DoesNotContain("847291", message); Assert.DoesNotContain("private", message); });
                for (var index = 0; index < 160; index++) trace.Log($"sequence={index} position={index * 1.1:0.000} sampleCount={123456789}");
                await trace.FlushAsync();
                var files = Directory.GetFiles(root).Select(path => new FileInfo(path)).ToArray();
                Assert.InRange(files.Length, 1, 2);
                Assert.All(files, file => Assert.True(file.Length <= 3072));
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static string[] ReadSharedLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public async Task Disabled_writer_does_not_create_directories_or_evaluate_interpolated_arguments()
    {
        var root = Path.Combine(Path.GetTempPath(), "FluidBarTests", "unused-diag-" + Guid.NewGuid().ToString("N"));
        await using var trace = new DiagnosticTrace(new RollingLogOptions { DirectoryPath = root }, () => false);
        var evaluations = 0;
        string Sensitive() { evaluations++; return "private"; }
        trace.Log($"title={Sensitive()}");
        await trace.FlushAsync();
        Assert.Equal(0, evaluations);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void Disabled_diagnostics_neither_format_sensitive_values_nor_write_the_legacy_log()
    {
        var root = Path.Combine(Path.GetTempPath(), "FluidBarTests", "disabled-diag", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previousFlag = Environment.GetEnvironmentVariable("FLUIDBAR_PIN_DIAG");
        var previousTemp = Environment.GetEnvironmentVariable("TEMP");
        var previousTmp = Environment.GetEnvironmentVariable("TMP");
        try
        {
            Environment.SetEnvironmentVariable("FLUIDBAR_PIN_DIAG", "0");
            Environment.SetEnvironmentVariable("TEMP", root);
            Environment.SetEnvironmentVariable("TMP", root);
            Assert.Equal(Path.TrimEndingDirectorySeparator(root), Path.TrimEndingDirectorySeparator(Path.GetTempPath()));
            var formats = 0;
            string Sensitive() { formats++; return "private fixture content"; }
            MediaDiagnostics.LogAlways($"title='{Sensitive()}'");
            Assert.Equal(0, formats);
            Assert.Empty(Directory.GetFiles(root));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FLUIDBAR_PIN_DIAG", previousFlag);
            Environment.SetEnvironmentVariable("TEMP", previousTemp);
            Environment.SetEnvironmentVariable("TMP", previousTmp);
            Directory.Delete(root, recursive: true);
        }
    }
}
