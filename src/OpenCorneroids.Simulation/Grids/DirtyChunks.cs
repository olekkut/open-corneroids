namespace OpenCorneroids.Simulation.Grids;

/// <summary>
/// The chunks of one block grid whose meshes no longer match their cells. An edit only marks its chunks here; the
/// meshes are built once per frame, as in the original (a sector rebuild waits for the next frame, so many edits in one
/// frame cost one rebuild). A chunk marked twice is queued once; chunks come out in the order they were first marked,
/// or nearest first when only some may be built this frame (<see cref="Take"/>).
/// </summary>
public sealed class DirtyChunks
{
    readonly bool[] _dirty;
    readonly List<int> _queue = new();
    NearestChunk[] _picks = [];

    /// <param name="chunks">How many chunks the grid has; slots are 0..chunks-1.</param>
    public DirtyChunks(int chunks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(chunks);
        _dirty = new bool[chunks];
    }

    /// <summary>Chunks waiting to be built.</summary>
    public int Count => _queue.Count;

    /// <summary>Whether the chunk in <paramref name="slot"/> waits to be built; false outside the grid.</summary>
    public bool IsDirty(int slot) => (uint)slot < (uint)_dirty.Length && _dirty[slot];

    /// <summary>Queues a chunk; false when it was already queued or lies outside the grid.</summary>
    public bool Mark(int slot)
    {
        if ((uint)slot >= (uint)_dirty.Length || _dirty[slot]) return false;
        _dirty[slot] = true;
        _queue.Add(slot);
        return true;
    }

    /// <summary>Forgets every queued chunk (they were all just built, e.g. on load).</summary>
    public void Clear()
    {
        foreach (int slot in _queue) _dirty[slot] = false;
        _queue.Clear();
    }

    /// <summary>
    /// Takes up to <paramref name="max"/> chunks off the queue into <paramref name="into"/> (cleared first). When that
    /// is fewer than are waiting, the ones with the smallest <paramref name="distance"/> go first (equal distances in
    /// queue order) and the rest wait for a later frame; otherwise they come in the order they were marked. Reuses its
    /// buffers, so a long queue taken a little each frame makes no garbage.
    /// </summary>
    public void Take(int max, Func<int, float> distance, List<int> into)
    {
        into.Clear();
        int n = Math.Clamp(max, 0, _queue.Count);
        if (n == 0) return;
        if (n == _queue.Count)
        {
            into.AddRange(_queue);
            Clear();
            return;
        }
        NearestChunk.Grow(ref _picks, _queue.Count);
        for (int i = 0; i < _queue.Count; i++) _picks[i] = new NearestChunk(distance(_queue[i]), 0, i);
        Array.Sort(_picks, 0, _queue.Count);
        for (int k = 0; k < n; k++)
        {
            int slot = _queue[_picks[k].Position];
            into.Add(slot);
            _dirty[slot] = false;
        }
        Compact();
    }

    /// <summary>The chunk at <paramref name="position"/> in the queue (0 = marked first).</summary>
    internal int QueuedAt(int position) => _queue[position];

    /// <summary>Takes one queued chunk out of the waiting set; <see cref="Compact"/> then drops it from the queue.</summary>
    internal void Release(int slot) => _dirty[slot] = false;

    /// <summary>Drops released chunks from the queue in place, keeping the order of the rest.</summary>
    internal void Compact()
    {
        int kept = 0;
        for (int i = 0; i < _queue.Count; i++)
            if (_dirty[_queue[i]]) _queue[kept++] = _queue[i];
        _queue.RemoveRange(kept, _queue.Count - kept);
    }
}

/// <summary>A waiting chunk ranked for building: nearest first, then by grid, then by place in its queue. A NaN
/// distance counts as infinitely far.</summary>
readonly struct NearestChunk(float distance, int queue, int position) : IComparable<NearestChunk>
{
    public readonly float Distance = float.IsNaN(distance) ? float.PositiveInfinity : distance;
    public readonly int Queue = queue;
    public readonly int Position = position;

    public int CompareTo(NearestChunk other)
    {
        int c = Distance.CompareTo(other.Distance);
        if (c == 0) c = Queue.CompareTo(other.Queue);
        return c != 0 ? c : Position.CompareTo(other.Position);
    }

    /// <summary>Makes <paramref name="buffer"/> hold at least <paramref name="count"/>, doubling so it rarely grows.</summary>
    public static void Grow(ref NearestChunk[] buffer, int count)
    {
        if (buffer.Length < count) buffer = new NearestChunk[Math.Max(count, buffer.Length * 2)];
    }
}

/// <summary>
/// Picks which waiting chunk meshes of every grid are built this frame (<c>cl_remesh_per_frame</c>; 0 = no limit). The
/// limit is shared by all grids and spent on the chunks nearest the camera, whichever grid they belong to, so a busy
/// grid (the asteroid being drilled) cannot keep another (a ship just launched) waiting. The original builds one per
/// frame; ours builds every changed chunk at once by default.
/// </summary>
public sealed class RemeshScheduler
{
    long _frame = long.MinValue;
    NearestChunk[] _picks = [];

    /// <summary>True the first time it is asked in frame <paramref name="frame"/>, so the grids are flushed once per
    /// frame however many of them ask.</summary>
    public bool StartFrame(long frame)
    {
        if (frame == _frame) return false;
        _frame = frame;
        return true;
    }

    /// <summary>
    /// Takes up to <paramref name="max"/> chunks (0 or less: all) off <paramref name="queues"/> into
    /// <paramref name="into"/> (cleared first) as (queue index, chunk slot). When fewer may be built than are waiting,
    /// the smallest <paramref name="distance"/>(queue index, slot) go first, ties by queue then by queue order, and the
    /// rest wait; otherwise each queue comes whole, in the order its chunks were marked. Reuses its buffers.
    /// </summary>
    public void Take(IReadOnlyList<DirtyChunks> queues, int max, Func<int, int, float> distance, List<(int Queue, int Slot)> into)
    {
        into.Clear();
        int waiting = 0;
        for (int q = 0; q < queues.Count; q++) waiting += queues[q].Count;
        if (waiting == 0) return;
        if (max <= 0 || max >= waiting)
        {
            for (int q = 0; q < queues.Count; q++)
            {
                var queue = queues[q];
                for (int i = 0; i < queue.Count; i++) into.Add((q, queue.QueuedAt(i)));
                queue.Clear();
            }
            return;
        }
        NearestChunk.Grow(ref _picks, waiting);
        int n = 0;
        for (int q = 0; q < queues.Count; q++)
            for (int i = 0; i < queues[q].Count; i++)
                _picks[n++] = new NearestChunk(distance(q, queues[q].QueuedAt(i)), q, i);
        Array.Sort(_picks, 0, n);
        for (int k = 0; k < max; k++)
        {
            var pick = _picks[k];
            int slot = queues[pick.Queue].QueuedAt(pick.Position);
            into.Add((pick.Queue, slot));
            queues[pick.Queue].Release(slot);
        }
        for (int q = 0; q < queues.Count; q++) queues[q].Compact();
    }
}
