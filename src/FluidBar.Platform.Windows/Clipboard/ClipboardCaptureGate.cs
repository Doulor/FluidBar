namespace FluidBar.Platform.Windows.Clipboard;

/// <summary>Serializes capture with privacy transitions without retaining paused payloads.</summary>
internal sealed class ClipboardCaptureGate
{
    private readonly object _gate = new();
    private bool _paused;
    private uint _lastSequence;

    public bool IsPaused { get { lock (_gate) return _paused; } }

    public void SetPaused(bool paused, Func<uint> currentSequence)
    {
        lock (_gate)
        {
            if (_paused == paused) return;
            // Read only the sequence number on resume; the paused clipboard payload stays unread.
            if (!paused) _lastSequence = currentSequence();
            _paused = paused;
        }
    }

    public bool TryRead(uint sequence, bool initial, Func<bool> read)
    {
        lock (_gate)
        {
            if (_paused || (!initial && sequence == _lastSequence)) return false;
            if (!read()) return false;
            _lastSequence = sequence;
            return true;
        }
    }
}
