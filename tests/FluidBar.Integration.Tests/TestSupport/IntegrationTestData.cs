using System.Runtime.CompilerServices;
using FluidBar.Platform.Windows.Agent;
using FluidBar.Platform.Windows.Clipboard;
using FluidBar.Platform.Windows.Devices;
using FluidBar.Platform.Windows.Media;
using FluidBar.Platform.Windows.Notifications;
using FluidBar.Plugins.Builtin;
using FluidBar.Sdk;

namespace FluidBar.Integration.Tests.TestSupport;

/// <summary>Repository/sample-output discovery for the marketplace seam (build-time dependency, no network).</summary>
public static class IntegrationPaths
{
    /// <summary>Repository root discovered by walking up from this source file.</summary>
    public static string RepoRoot([CallerFilePath] string callerFilePath = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(callerFilePath)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FluidBar.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    /// <summary>Newest <c>net10.0</c> build output for a sample plugin project.</summary>
    public static string SamplePluginOutput(string projectName)
    {
        var bin = Path.Combine(RepoRoot(), "plugins", "samples", projectName, "bin");
        if (!Directory.Exists(bin))
        {
            throw new DirectoryNotFoundException(
                $"Sample plugin '{projectName}' is not built ('{bin}' missing); the integration project references it as a build dependency.");
        }

        return Directory.EnumerateDirectories(bin, "net10.0", SearchOption.AllDirectories)
            .OrderByDescending(static directory => directory, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?? throw new DirectoryNotFoundException($"No net10.0 output found under '{bin}'.");
    }
}

/// <summary>Disposable temp directory.</summary>
public sealed class TempDirectory : IDisposable
{
    private TempDirectory(string path) => Path = path;

    public string Path { get; }

    public static TempDirectory Create()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fluidbar-integration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new TempDirectory(path);
    }

    public string Combine(params string[] segments) => System.IO.Path.Combine([Path, .. segments]);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort temp cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort temp cleanup.
        }
    }
}

/// <summary>Canonical fake platform snapshots for the integration suite.</summary>
public static class FakeSnapshots
{
    public static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    public static MediaSnapshot MediaPlaying(
        string title = "Midnight City",
        string artist = "M83",
        double positionSeconds = 30,
        double durationSeconds = 240,
        bool playing = true)
        => new(
            SourceAppUserModelId: "Spotify.exe",
            SourceDisplayName: "Spotify",
            Title: title,
            Artist: artist,
            Album: "Hurry Up, We're Dreaming",
            PlaybackState: playing ? MediaPlaybackState.Playing : MediaPlaybackState.Paused,
            IsPlaying: playing,
            Position: TimeSpan.FromSeconds(positionSeconds),
            PositionUpdatedAtUtc: Now,
            Duration: TimeSpan.FromSeconds(durationSeconds),
            CanSeek: true,
            CanGoNext: true,
            CanGoPrevious: true,
            TrackKey: "spotify|midnight-city|m83|album");

    public static ClipboardEntry ClipboardText(string text, uint sequence = 1)
        => new(sequence, Now, ClipboardContentKind.Text, text, text.Length, []);

    public static NotificationEntry NotificationWithCode(string title = "Sign-in code", string body = "Your verification code is 482913")
        => new(7, "Windows", title, body, Now, VerificationCodeDetector.Detect($"{title}\n{body}"));

    public static AgentEventEntry AgentCompleted(string agent = "claude")
        => new(
            Id: agent + ":completed:event-1",
            Agent: AgentEventParser.MapAgent(agent),
            Kind: AgentEventKind.Completed,
            Title: "Agent finished",
            Message: "Task completed in 2m 14s",
            Timestamp: Now,
            SourceFile: "event-1.json");

    public static DeviceEventEntry UsbConnected(string name = "USB 046D:C52B")
        => new("\\\\?\\USB#VID_046D&PID_C52B#serial", DeviceKind.Usb, DeviceChangeKind.Connected, name, Now);

    public static DeviceEventEntry BluetoothConnected(string name = "Headphones")
        => new("bt:headphones", DeviceKind.Bluetooth, DeviceChangeKind.Connected, name, Now);

    /// <summary>Assembles the fake platform service graph consumed by <see cref="BuiltinPluginCatalog"/>.</summary>
    public static BuiltinPlatformServices Services(
        FakeMediaSource media,
        FakeClipboardSource clipboard,
        FakeNotificationSource notifications,
        FakeAgentInboxSource agentInbox,
        FakeHookInstaller hookInstaller,
        FakeDeviceEventSource usb,
        FakeDeviceEventSource bluetooth)
        => new()
        {
            Media = media,
            Lyrics = null,
            ArtworkTempStore = null,
            Clipboard = clipboard,
            Notifications = notifications,
            AgentInbox = agentInbox,
            AgentHookInstaller = hookInstaller,
            UsbDevices = usb,
            BluetoothDevices = bluetooth,
        };
}

/// <summary>Handcrafted SDK events for pipeline/priority assertions independent of a specific plugin.</summary>
public static class SdkEvents
{
    public static IslandEvent Media(string title = "Midnight City", string artist = "M83", double progress = 0.125)
        => new()
        {
            Source = BuiltinIds.MediaPlugin,
            Title = title,
            Content = artist,
            IconKind = "media",
            Kind = IslandEventKind.Media,
            Payload = new IslandEventPayload
            {
                Subtitle = artist,
                Progress = progress,
                IsActive = true,
                AudioWave = [0.1f, 0.4f, 0.8f],
                PlaybackTicks = (long)(progress * TimeSpan.FromMinutes(4).Ticks),
            },
        };

    public static IslandEvent Notification(string title = "Build finished", string body = "CI is green", string? code = null)
        => new()
        {
            Source = BuiltinIds.NotificationsPlugin,
            Title = title,
            Content = body,
            IconKind = "notification",
            Kind = IslandEventKind.Notification,
            Payload = new IslandEventPayload
            {
                Subtitle = "Windows",
                Badge = code,
                VerificationCode = code,
            },
        };

    public static IslandEvent Clipboard(string preview = "npm run build")
        => new()
        {
            Source = BuiltinIds.ClipboardPlugin,
            Title = preview,
            Content = preview,
            IconKind = "clipboard",
            Kind = IslandEventKind.Auto,
            Payload = new IslandEventPayload { Badge = "1", Subtitle = "1" },
        };

    public static IslandEvent Agent(string title = "Agent finished", string agent = "claude")
        => new()
        {
            Source = BuiltinIds.AgentPlugin,
            Title = title,
            Content = "Task completed",
            IconKind = "agent",
            Kind = IslandEventKind.Agent,
            Payload = new IslandEventPayload
            {
                Subtitle = agent,
                Badge = "completed",
                SourceName = agent,
                Fields = new Dictionary<string, string?>(StringComparer.Ordinal) { ["agent"] = agent },
            },
        };
}
