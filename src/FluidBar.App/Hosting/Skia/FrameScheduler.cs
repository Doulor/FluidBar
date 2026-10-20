using FluidBar.Core.Layout;

namespace FluidBar.App.Hosting.Skia;

/// <summary>
/// Collects frame invalidations and coalesces dirty rectangles. Rectangles merge only when they
/// intersect or their edge gap is below <see cref="MergeGapDip"/>; otherwise they stay as separate
/// damage regions so the renderer can clip each one independently.
/// </summary>
public sealed class FrameScheduler
{
    /// <summary>Maximum edge gap in DIP at which two dirty rectangles may be coalesced.</summary>
    public const double MergeGapDip = 8;

    private readonly List<VisualRect> _dirty = [];
    private bool _full;
    private string _fullReason = "startup";
    private string? _lastFullReason;

    /// <summary>Whether a full frame is currently pending.</summary>
    public bool HasFullFrame => _full;

    /// <summary>Number of pending dirty rectangles (0 when a full frame wins).</summary>
    public int DirtyCount => _full ? 0 : _dirty.Count;

    /// <summary>The reason retained for the most recent full frame.</summary>
    public string? LastFullReason => _lastFullReason;

    /// <summary>Requests a full-surface repaint and records the caller's reason label.</summary>
    public void InvalidateFull(string reason)
    {
        _full = true;
        _fullReason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason;
    }

    /// <summary>Adds one dirty rectangle, merging only with nearby/intersecting regions.</summary>
    public void InvalidateDirty(VisualRect damage)
    {
        if (_full || damage.IsEmpty)
        {
            return;
        }

        var merged = damage;
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var i = 0; i < _dirty.Count; i++)
            {
                if (!CanMerge(merged, _dirty[i]))
                {
                    continue;
                }

                merged = Union(merged, _dirty[i]);
                _dirty.RemoveAt(i);
                changed = true;
                break;
            }
        }

        _dirty.Add(merged);
    }

    /// <summary>Takes the pending invalidation, or returns false when nothing changed.</summary>
    public bool TryTake(out SkiaIslandRenderChange change)
    {
        if (_full)
        {
            _full = false;
            _dirty.Clear();
            _lastFullReason = _fullReason;
            change = new SkiaIslandRenderChange(true, VisualRect.Empty)
            {
                FullReason = _fullReason,
            };
            return true;
        }

        if (_dirty.Count == 0)
        {
            change = default;
            return false;
        }

        var rects = _dirty.ToArray();
        var union = rects[0];
        for (var i = 1; i < rects.Length; i++)
        {
            union = Union(union, rects[i]);
        }

        _dirty.Clear();
        change = new SkiaIslandRenderChange(false, union)
        {
            DamageRects = rects,
        };
        return true;
    }

    /// <summary>Clears pending state (used when a host forces a full render).</summary>
    public void Clear()
    {
        _full = false;
        _dirty.Clear();
    }

    /// <summary>Whether two dirty regions are close enough to merge under the 8 DIP contract.</summary>
    public static bool CanMerge(VisualRect left, VisualRect right)
    {
        if (left.IntersectsWith(right))
        {
            return true;
        }

        var dx = Math.Max(0, Math.Max(left.X - right.Right, right.X - left.Right));
        var dy = Math.Max(0, Math.Max(left.Y - right.Bottom, right.Y - left.Bottom));
        return dx < MergeGapDip && dy < MergeGapDip;
    }

    private static VisualRect Union(VisualRect left, VisualRect right)
    {
        var x = Math.Min(left.X, right.X);
        var y = Math.Min(left.Y, right.Y);
        var rightEdge = Math.Max(left.Right, right.Right);
        var bottom = Math.Max(left.Bottom, right.Bottom);
        return new VisualRect(x, y, rightEdge - x, bottom - y);
    }
}

