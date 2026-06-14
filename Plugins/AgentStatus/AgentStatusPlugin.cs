using System.IO;
using System.Windows.Threading;

namespace FluidBar;

public sealed class AgentStatusPlugin : IIslandPlugin
{
    private static readonly string BaseDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FluidBar",
        "agent-events");

    private readonly DispatcherTimer _timer;

    public string Id => "agent-status";
    public string Name => "Agent 状态";
    public string Description => "监听 Claude Code / Codex 本地 hook 事件，任务完成或失败时显示提醒";
    public string Icon => "\uE8F2";
    public bool Enabled { get; set; } = true;
    public IPluginConfig? Config => null;
    public event Action<IslandEvent>? EventTriggered;

    public AgentStatusPlugin()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _timer.Tick += (_, _) => PollInbox();
    }

    public void Initialize()
    {
        Directory.CreateDirectory(InboxDir);
        Directory.CreateDirectory(ProcessedDir);
        Directory.CreateDirectory(FailedDir);
    }

    public void Start()
    {
        Initialize();
        if (!_timer.IsEnabled)
            _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
    }

    public void Dispose() => Stop();

    private static string InboxDir => Path.Combine(BaseDir, "inbox");
    private static string ProcessedDir => Path.Combine(BaseDir, "processed");
    private static string FailedDir => Path.Combine(BaseDir, "failed");

    private void PollInbox()
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(InboxDir, "*.json").OrderBy(File.GetCreationTimeUtc))
                ConsumeFile(path);
        }
        catch
        {
        }
    }

    private void ConsumeFile(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var hook = AgentHookEvent.Parse(json);
            EventTriggered?.Invoke(AgentStatusIslandEventFactory.FromHook(hook));
            MoveTo(path, ProcessedDir);
        }
        catch
        {
            MoveTo(path, FailedDir);
        }
    }

    private static void MoveTo(string path, string directory)
    {
        Directory.CreateDirectory(directory);
        var target = Path.Combine(
            directory,
            $"{Path.GetFileNameWithoutExtension(path)}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.json");
        try
        {
            File.Move(path, target, overwrite: true);
        }
        catch
        {
            try { File.Delete(path); }
            catch { }
        }
    }
}

