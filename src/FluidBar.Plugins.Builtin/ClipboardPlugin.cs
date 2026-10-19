using FluidBar.Platform.Windows.Clipboard;
using FluidBar.Platform.Windows.Threading;
using FluidBar.Sdk;
using FluidBar.Sdk.Contributions;

namespace FluidBar.Plugins.Builtin;

/// <summary>First-party clipboard plugin: maps bounded clipboard history snapshots to SDK events + commands.</summary>
public sealed class ClipboardPlugin : BuiltinPluginBase
{
    private readonly IClipboardSource _clipboard;
    private readonly object _stateGate = new();
    private uint _lastSequence;
    private string? _lastSignature;

    public ClipboardPlugin(IClipboardSource clipboard)
        => _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));

    public override string PluginId => BuiltinIds.ClipboardPlugin;

    public static PluginContributions Contributions => BuiltinContributions.ForClipboard();

    protected override void OnStarted()
    {
        TrackSubscription(() => _clipboard.SnapshotChanged += OnSnapshotChanged,
            () => _clipboard.SnapshotChanged -= OnSnapshotChanged);

        RegisterCommand(BuiltinIds.ClipboardCopyLatest, async (_, cancellationToken) =>
        {
            var latest = _clipboard.Current?.Latest;
            if (latest?.Text is { Length: > 0 } text)
            {
                await _clipboard.SetTextAsync(text, cancellationToken).ConfigureAwait(false);
            }
        });
        RegisterCommand(BuiltinIds.ClipboardPinLatest, (_, cancellationToken) =>
        {
            var latest = _clipboard.Current?.Latest;
            if (latest is not null)
            {
                Context.Settings.SetString("clipboard.pinnedText", latest.Text);
                return Context.Settings.SaveAsync(cancellationToken);
            }

            return ValueTask.CompletedTask;
        });
        RegisterCommand(BuiltinIds.ClipboardClearHistory, (_, _) =>
        {
            _clipboard.ClearHistory();
            Retract();
            return ValueTask.CompletedTask;
        });

        // No startup publish: the current clipboard history is old news, and publishing it made the
        // island pop a "Copied" pill on every launch. SnapshotChanged covers real copies.
    }

    private void OnSnapshotChanged(object? sender, SnapshotEventArgs<ClipboardSnapshot> args)
    {
        if (!IsStarted)
        {
            return;
        }

        PublishSnapshot(args.Snapshot);
    }

    internal void PublishSnapshot(ClipboardSnapshot? snapshot)
    {
        if (snapshot?.Latest is null)
        {
            lock (_stateGate)
            {
                if (_lastSignature is null)
                {
                    return;
                }

                _lastSignature = null;
            }

            Retract();
            return;
        }

        var latest = snapshot.Latest;
        lock (_stateGate)
        {
            if (latest.Sequence == _lastSequence)
            {
                return;
            }

            var signature = string.Join('|', latest.Sequence.ToString(
                System.Globalization.CultureInfo.InvariantCulture), latest.Kind.ToString(), latest.Text?.Length
                .ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0");
            if (string.Equals(signature, _lastSignature, StringComparison.Ordinal))
            {
                return;
            }

            _lastSequence = latest.Sequence;
            _lastSignature = signature;
        }

        var preview = BuildPreview(latest);
        var details = latest.FilePaths.Length > 0 ? latest.FilePaths.ToList() : null;
        var fields = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["icon"] = latest.Kind switch
            {
                ClipboardContentKind.Text => "clipboard_text",
                ClipboardContentKind.Image => "clipboard_image",
                ClipboardContentKind.Files => "clipboard_files",
                _ => "clipboard",
            },
            ["title"] = preview,
            ["preview"] = latest.Text,
            ["badge"] = $"{snapshot.History.Length}",
            ["kind"] = latest.Kind.ToString(),
            ["actions"] = string.Join(',', new[] { BuiltinIds.ClipboardCopyLatest, BuiltinIds.ClipboardPinLatest }),
        };

        Publish(new IslandEvent
        {
            Source = PluginId,
            Title = preview,
            Content = latest.Text,
            IconKind = "clipboard",
            Kind = IslandEventKind.Auto,
            LayoutId = BuiltinIds.ClipboardCompactLayout,
            Payload = new IslandEventPayload
            {
                Subtitle = $"{snapshot.History.Length}",
                Badge = $"{snapshot.History.Length}",
                DetailLines = details,
                Fields = fields,
            },
        });
    }

    private static string BuildPreview(ClipboardEntry latest)
    {
        if (!string.IsNullOrWhiteSpace(latest.Text))
        {
            var singleLine = latest.Text.ReplaceLineEndings(" ").Trim();
            return singleLine.Length <= 80 ? singleLine : string.Concat(singleLine.AsSpan(0, 80), "…");
        }

        return latest.Kind switch
        {
            ClipboardContentKind.Image => "image",
            ClipboardContentKind.Files => $"files ({latest.FilePaths.Length})",
            _ => "clipboard",
        };
    }

    protected override void OnStopped()
    {
        lock (_stateGate)
        {
            _lastSequence = 0;
            _lastSignature = null;
        }
    }
}
