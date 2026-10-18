using FluidBar.Platform.Windows.Hosting;

namespace FluidBar.Spikes.LayeredHost;

/// <summary>
/// App-side drag wiring: the host never moves itself, so the spike applies each drag step to the
/// window placement (exactly what the product host will do). Also counts the input events used as
/// evidence that press/move/release reached the layered window.
/// </summary>
internal sealed class DragSupport
{
    private readonly LayeredWindowHost _host;
    private readonly Action<string>? _log;

    private DragSupport(LayeredWindowHost host, Action<string>? log)
    {
        _host = host;
        _log = log;
    }

    /// <summary>Pointer-move messages observed.</summary>
    public int Moves { get; private set; }

    /// <summary>Left-button presses observed.</summary>
    public int Presses { get; private set; }

    /// <summary>Drag gestures started.</summary>
    public int Started { get; private set; }

    /// <summary>Drag steps applied to the window placement.</summary>
    public int Applied { get; private set; }

    /// <summary>Drag gestures cancelled (capture lost).</summary>
    public int Cancelled { get; private set; }

    /// <summary>Buttons reported by the last pointer move.</summary>
    public LayeredPointerButtons LastButtons { get; private set; }

    /// <summary>Logs every input/drag event (diagnostics only).</summary>
    public bool Verbose { get; set; }

    /// <summary>Subscribes the drag behaviour to a host.</summary>
    public static DragSupport Attach(LayeredWindowHost host, Action<string>? log = null)
    {
        var support = new DragSupport(host, log);
        host.PointerMoved += support.OnMoved;
        host.PointerPressed += support.OnPressed;
        host.PointerReleased += support.OnReleased;
        host.PointerCaptureLost += support.OnCaptureLost;
        host.Dragged += support.OnDragged;
        return support;
    }

    private void OnMoved(LayeredPointerEvent pointer)
    {
        Moves++;
        LastButtons = pointer.Buttons;
    }

    private void OnPressed(LayeredPointerEvent pointer)
    {
        Presses++;
        if (Verbose)
        {
            _log?.Invoke($"  input: pressed client=({pointer.ClientX},{pointer.ClientY}) buttons={pointer.Buttons}");
        }
    }

    private void OnReleased(LayeredPointerEvent pointer)
    {
        if (Verbose)
        {
            _log?.Invoke($"  input: released client=({pointer.ClientX},{pointer.ClientY}) buttons={pointer.Buttons}");
        }
    }

    private void OnCaptureLost()
    {
        if (Verbose)
        {
            _log?.Invoke("  input: capture lost");
        }
    }

    private void OnDragged(LayeredDragEvent drag)
    {
        if (Verbose)
        {
            _log?.Invoke($"  drag: {drag.Phase} step=({drag.StepX},{drag.StepY}) total=({drag.DeltaX},{drag.DeltaY}) " +
                $"buttons={drag.Pointer.Buttons}");
        }

        switch (drag.Phase)
        {
            case LayeredDragPhase.Started:
                Started++;
                return;

            case LayeredDragPhase.Cancelled:
                Cancelled++;
                return;

            case LayeredDragPhase.Moved:
            case LayeredDragPhase.Ended:
                if (drag.StepX == 0 && drag.StepY == 0)
                {
                    return;
                }

                var window = _host.Placement.WindowPixel;
                _host.PlacePhysical(window.X + drag.StepX, window.Y + drag.StepY);
                Applied++;
                return;
        }
    }
}
