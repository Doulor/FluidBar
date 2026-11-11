using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace FluidBar.Platform.Windows.Update;

/// <summary>Hands portable updates to a helper before shutdown; setup packages use ShellExecute.</summary>
public sealed class ProcessUpdateInstallerLauncher : IUpdateInstallerLauncher
{
    private readonly string? _currentPath;
    private readonly int _processId;
    private readonly string _powerShellPath;

    /// <summary>Uses the actual FluidBar host path, including names assigned by a browser download.</summary>
    public ProcessUpdateInstallerLauncher()
        : this(Assembly.GetEntryAssembly()?.GetName().Name == "FluidBar" ? Environment.ProcessPath : null,
            Environment.ProcessId)
    {
    }

    internal ProcessUpdateInstallerLauncher(string? currentPath, int processId, string? powerShellPath = null)
    {
        _currentPath = currentPath;
        _processId = processId;
        _powerShellPath = powerShellPath ?? Path.Combine(Environment.SystemDirectory,
            "WindowsPowerShell", "v1.0", "powershell.exe");
    }

    /// <inheritdoc />
    public bool Launch(string installerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);
        if (string.Equals(Path.GetFileName(installerPath), "FluidBar.exe", StringComparison.OrdinalIgnoreCase))
        {
            // Never run a portable download alongside the old instance: the single-instance mutex
            // makes the new process exit, after which the caller would also shut the old one down.
            return CanSelfReplace(installerPath, _currentPath) && TryLaunchSelfReplace(installerPath);
        }

        using var installer = Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
        return installer is not null;
    }

    internal static bool CanSelfReplace(string installerPath, string? current)
        => !string.IsNullOrWhiteSpace(current)
           && string.Equals(Path.GetExtension(current), ".exe", StringComparison.OrdinalIgnoreCase)
           && string.Equals(Path.GetFileName(installerPath), "FluidBar.exe", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(Path.GetFullPath(current), Path.GetFullPath(installerPath), StringComparison.OrdinalIgnoreCase);

    private bool TryLaunchSelfReplace(string installerPath)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"fluidbar-update-{Guid.NewGuid():N}.ps1");
        var readyPath = scriptPath + ".ready";
        var acknowledged = false;
        Process? helper = null;
        try
        {
            var logPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(installerPath))!, "install.log");
            WriteSelfReplaceScript(scriptPath, BuildSelfReplaceScript(installerPath, _currentPath!, _processId,
                logPath: logPath));
            helper = Process.Start(new ProcessStartInfo(_powerShellPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptPath },
            });
            if (helper is null)
                return false;

            // A created PowerShell process is not proof that its script parsed or that the target
            // directory is writable. Only the ready file authorizes the caller to exit.
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(15))
            {
                if (helper.HasExited)
                    return false;
                if (File.Exists(readyPath))
                {
                    acknowledged = true;
                    return true;
                }
                helper.WaitForExit(50);
            }

            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (!acknowledged)
            {
                // The app is staying alive. Do not leave a timed-out helper waiting for its exit.
                try { if (helper is { HasExited: false }) { helper.Kill(); helper.WaitForExit(5000); } }
                catch (Exception) { /* It may have exited between the check and Kill. */ }
                foreach (var path in new[] { scriptPath, readyPath, _currentPath + "." + Path.GetFileNameWithoutExtension(scriptPath) + ".update" })
                {
                    try { File.Delete(path); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            helper?.Dispose();
        }
    }

    /// <summary>Windows PowerShell 5.1 needs a BOM to read Unicode paths correctly.</summary>
    internal static void WriteSelfReplaceScript(string path, string script)
        => File.WriteAllText(path, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

    /// <summary>Stages beside the target, waits for shutdown, atomically replaces and restarts it.</summary>
    internal static string BuildSelfReplaceScript(string installerPath, string currentPath, int pid,
        bool restart = true, string? logPath = null, int retryTimeoutSeconds = 60)
    {
        return $$"""
            $ErrorActionPreference = 'Stop'
            $source = '{{EscapePowerShellLiteral(installerPath)}}'
            $target = '{{EscapePowerShellLiteral(currentPath)}}'
            $oldProcessId = {{pid}}
            $restart = ${{(restart ? "true" : "false")}}
            $logPath = {{(logPath is null ? "$PSCommandPath + '.log'" : "'" + EscapePowerShellLiteral(logPath) + "'")}}
            $readyPath = $PSCommandPath + '.ready'
            $stage = $target + '.' + [IO.Path]::GetFileNameWithoutExtension($PSCommandPath) + '.update'
            $backup = $stage + '.backup'
            $replaced = $false
            $succeeded = $false
            $exitCode = 1
            function Write-UpdateLog([string]$message) {
                try { Add-Content -LiteralPath $logPath -Value ((Get-Date -Format o) + ' ' + $message) -Encoding UTF8 }
                catch { }
            }
            function Get-UpdateHash([string]$path) {
                $stream = [IO.File]::OpenRead($path)
                $sha = [Security.Cryptography.SHA256]::Create()
                try { return [BitConverter]::ToString($sha.ComputeHash($stream)) }
                finally { $stream.Dispose(); $sha.Dispose() }
            }
            function Restart-Target {
                $started = Start-Process -FilePath $target -WorkingDirectory ([IO.Path]::GetDirectoryName($target)) -WindowStyle Hidden -PassThru
                if ($null -eq $started) { throw 'Restart did not create a process.' }
                Write-UpdateLog ('Restarted PID ' + $started.Id + ': ' + $target)
                $started.Dispose()
            }
            function Replace-UpdateFile([string]$from, [string]$to, [string]$save) {
                $deadline = [DateTime]::UtcNow.AddSeconds({{retryTimeoutSeconds}})
                while ($true) {
                    try { [IO.File]::Replace($from, $to, $save); return }
                    catch {
                        if ([DateTime]::UtcNow -ge $deadline) { throw }
                        Write-UpdateLog ('Replacement deferred: ' + $_.Exception.Message)
                        Start-Sleep -Milliseconds 500
                    }
                }
            }
            try {
                Write-UpdateLog ('Preparing update: ' + $source + ' -> ' + $target)
                $expectedHash = Get-UpdateHash $source
                Copy-Item -LiteralPath $source -Destination $stage -Force
                if ((Get-UpdateHash $stage) -ne $expectedHash) { throw 'Staged file hash mismatch.' }
                if ((Get-Item -LiteralPath $target).IsReadOnly) { throw 'The installed executable is read-only.' }
                [IO.File]::WriteAllText($readyPath, 'ready')
                Write-UpdateLog ('Ready; waiting for PID ' + $oldProcessId)
                Wait-Process -Id {{pid}} -Timeout 120 -ErrorAction SilentlyContinue
                if (Get-Process -Id $oldProcessId -ErrorAction SilentlyContinue) { throw 'The old process did not exit.' }

                Replace-UpdateFile $stage $target $backup
                $replaced = $true
                if ((Get-UpdateHash $target) -ne $expectedHash) { throw 'Installed file hash mismatch.' }
                if ($restart) { Restart-Target }
                $succeeded = $true
                $exitCode = 0
                Write-UpdateLog 'Update completed.'
            }
            catch {
                Write-UpdateLog ('Update failed: ' + $_.Exception.Message)
                if ($replaced -and (Test-Path -LiteralPath $backup)) {
                    try { Replace-UpdateFile $backup $target $stage; $replaced = $false; Write-UpdateLog 'Restored previous executable.' }
                    catch { Write-UpdateLog ('Rollback failed; backup retained at ' + $backup + ': ' + $_.Exception.Message) }
                }
                if ($restart -and -not $replaced -and (Test-Path -LiteralPath $readyPath) -and
                    -not (Get-Process -Id $oldProcessId -ErrorAction SilentlyContinue)) {
                    try { Restart-Target }
                    catch { Write-UpdateLog ('Recovery restart failed: ' + $_.Exception.Message) }
                }
            }
            finally {
                Remove-Item -LiteralPath $stage, $readyPath -Force -ErrorAction SilentlyContinue
                if ($succeeded) {
                    Remove-Item -LiteralPath $backup, $PSCommandPath -Force -ErrorAction SilentlyContinue
                }
            }
            exit $exitCode
            """;
    }

    private static string EscapePowerShellLiteral(string value) => value.Replace("'", "''");
}
