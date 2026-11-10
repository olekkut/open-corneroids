using System.Numerics;

namespace OpenCorneroids.Simulation.Combat;

/// <summary>
/// Ours: a uniform grid of spheres (creatures' hit spheres, 1.5 × their radius as in the original) that answers "which
/// spheres does this segment pass through, and where", the question every shot asks every frame. The answer is exactly
/// what testing every sphere with <see cref="Damage.SegmentHitsSphere"/> gives, in the same order; only the cost changes,
/// from every sphere per shot to the spheres in the few cells the shot's path crosses.
/// </summary>
/// <remarks>
/// Each sphere is listed in every cell its bounding box touches (a little enlarged, so float rounding at a cell edge can't
/// drop it). The point where a segment first meets a sphere lies in the sphere's box and on the segment, so the cell
/// holding it is both one the sphere is listed in and one the segment's walk visits: no hit is missed. Spheres too big
/// to list cell by cell, and segments too long or not finite to walk, fall back to testing every sphere. Built again
/// each frame (<see cref="Clear"/>, then <see cref="Add"/>); not thread-safe.
/// </remarks>
public sealed class SphereHash
{
    /// <summary>The default cell edge, metres: a few creatures across, and a frame's flight of most shots.</summary>
    public const float DefaultCellSize = 8;

    /// <summary>A sphere touching more cells than this is kept in a short list tested by every query instead.</summary>
    const int MaxCellsPerSphere = 64;

    /// <summary>A segment crossing more cells than this tests every sphere instead of walking them.</summary>
    const int MaxCellsPerQuery = 4096;

    /// <summary>The enlargement of each sphere's box, metres plus a share of its distance from the origin.</summary>
    const float Margin = 1e-3f, RelativeMargin = 1e-5f;

    readonly float _cellSize;
    readonly Dictionary<(int X, int Y, int Z), List<int>> _cells = new();
    readonly Stack<List<int>> _spare = new();
    readonly List<Vector3> _centers = new();
    readonly List<float> _radii = new();
    readonly List<int> _oversize = new();
    readonly List<int> _seen = new();
    int _query;

    /// <param name="cellSize">Edge of a grid cell, metres; positive and finite.</param>
    public SphereHash(float cellSize = DefaultCellSize)
    {
        if (!(cellSize > 0) || !float.IsFinite(cellSize))
            throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "A cell edge must be positive and finite.");
        _cellSize = cellSize;
    }

    /// <summary>Spheres added since the last <see cref="Clear"/>.</summary>
    public int Count => _centers.Count;

    /// <summary>Forgets every sphere, keeping the memory for the next frame.</summary>
    public void Clear()
    {
        foreach (var list in _cells.Values)
        {
            list.Clear();
            _spare.Push(list);
        }
        _cells.Clear();
        _centers.Clear();
        _radii.Clear();
        _oversize.Clear();
        _seen.Clear();
    }

    /// <summary>Adds a sphere; returns its index, which <see cref="SegmentHits"/> reports. A sphere that can never be hit
    /// (radius not positive, or not finite) still gets an index, and is simply never reported.</summary>
    public int Add(Vector3 center, float radius)
    {
        int index = _centers.Count;
        _centers.Add(center);
        _radii.Add(radius);
        _seen.Add(0);
        if (!(radius > 0) || !float.IsFinite(radius) || !IsFinite(center)) return index;

        var reach = new Vector3(radius + Margin + RelativeMargin * MathF.Max(MathF.Abs(center.X), MathF.Max(MathF.Abs(center.Y), MathF.Abs(center.Z))));
        var lo = CellOf(center - reach);
        var hi = CellOf(center + reach);
        long cells = (long)(hi.X - lo.X + 1) * (hi.Y - lo.Y + 1) * (hi.Z - lo.Z + 1);
        if (cells > MaxCellsPerSphere)
        {
            _oversize.Add(index);
            return index;
        }
        for (int z = lo.Z; z <= hi.Z; z++)
        for (int y = lo.Y; y <= hi.Y; y++)
        for (int x = lo.X; x <= hi.X; x++)
        {
            if (!_cells.TryGetValue((x, y, z), out var list))
                _cells[(x, y, z)] = list = _spare.Count > 0 ? _spare.Pop() : new List<int>();
            list.Add(index);
        }
        return index;
    }

    /// <summary>
    /// The spheres the segment <paramref name="from"/>→<paramref name="to"/> comes within the radius of, with where along
    /// it (0..1, <see cref="Damage.SegmentHitsSphere"/>), nearest first (equal distances by index). Replaces the contents
    /// of <paramref name="hits"/>, so one list can serve every query of a frame without allocating.
    /// </summary>
    public void SegmentHits(Vector3 from, Vector3 to, List<(float At, int Index)> hits)
    {
        hits.Clear();
        if (_centers.Count == 0) return;
        if (++_query == int.MaxValue)
        {
            _query = 1;
            for (int i = 0; i < _seen.Count; i++) _seen[i] = 0;
        }

        if (!IsFinite(from) || !IsFinite(to) || !Walk(from, to, hits))
        {
            hits.Clear();
            for (int i = 0; i < _centers.Count; i++) Test(i, from, to, hits);
        }
        else
        {
            foreach (int i in _oversize) Test(i, from, to, hits);
        }
        hits.Sort(static (a, b) => a.At != b.At ? a.At.CompareTo(b.At) : a.Index.CompareTo(b.Index));
    }

    /// <summary>The same answer by testing every sphere, for checks and tests.</summary>
    public void SegmentHitsBruteForce(Vector3 from, Vector3 to, List<(float At, int Index)> hits)
    {
        hits.Clear();
        for (int i = 0; i < _centers.Count; i++)
            if (Damage.SegmentHitsSphere(from, to, _centers[i], _radii[i]) is { } t) hits.Add((t, i));
        hits.Sort(static (a, b) => a.At != b.At ? a.At.CompareTo(b.At) : a.Index.CompareTo(b.Index));
    }

    /// <summary>Visits every cell the segment passes through (Amanatides and Woo) and tests the spheres listed there;
    /// false, having tested nothing, when the walk would be longer than <see cref="MaxCellsPerQuery"/>.</summary>
    bool Walk(Vector3 from, Vector3 to, List<(float At, int Index)> hits)
    {
        var a = from / _cellSize;
        var b = to / _cellSize;
        var cell = CellOf(from);
        var end = CellOf(to);
        long steps = (long)Math.Abs(end.X - cell.X) + Math.Abs(end.Y - cell.Y) + Math.Abs(end.Z - cell.Z);
        if (steps >= MaxCellsPerQuery) return false;

        var d = b - a;
        int sx = Math.Sign(d.X), sy = Math.Sign(d.Y), sz = Math.Sign(d.Z);
        float dx = d.X != 0 ? MathF.Abs(1 / d.X) : float.PositiveInfinity;
        float dy = d.Y != 0 ? MathF.Abs(1 / d.Y) : float.PositiveInfinity;
        float dz = d.Z != 0 ? MathF.Abs(1 / d.Z) : float.PositiveInfinity;
        float tx = FirstBoundary(a.X, cell.X, d.X, dx);
        float ty = FirstBoundary(a.Y, cell.Y, d.Y, dy);
        float tz = FirstBoundary(a.Z, cell.Z, d.Z, dz);

        Visit(cell, from, to, hits);
        for (long i = 0; i < steps; i++)
        {
            // Only axes that have not yet reached the end cell may step, so the walk ends exactly there whatever the
            // rounding in the boundary distances.
            float bx = cell.X != end.X ? tx : float.PositiveInfinity;
            float by = cell.Y != end.Y ? ty : float.PositiveInfinity;
            float bz = cell.Z != end.Z ? tz : float.PositiveInfinity;
            if (bx <= by && bx <= bz) { cell.X += sx; tx += dx; }
            else if (by <= bz) { cell.Y += sy; ty += dy; }
            else { cell.Z += sz; tz += dz; }
            Visit(cell, from, to, hits);
        }
        return true;
    }

    void Visit((int X, int Y, int Z) cell, Vector3 from, Vector3 to, List<(float At, int Index)> hits)
    {
        if (!_cells.TryGetValue(cell, out var list)) return;
        foreach (int i in list) Test(i, from, to, hits);
    }

    void Test(int i, Vector3 from, Vector3 to, List<(float At, int Index)> hits)
    {
        if (_seen[i] == _query) return;
        _seen[i] = _query;
        if (Damage.SegmentHitsSphere(from, to, _centers[i], _radii[i]) is { } t) hits.Add((t, i));
    }

    (int X, int Y, int Z) CellOf(Vector3 p) => (Floor(p.X / _cellSize), Floor(p.Y / _cellSize), Floor(p.Z / _cellSize));

    /// <summary>Floor to a cell index, held well inside the int range (a cell that far out is only ever reached by
    /// coordinates nothing in the game uses).</summary>
    static int Floor(float v) => (int)Math.Clamp(MathF.Floor(v), -1_000_000_000f, 1_000_000_000f);

    static float FirstBoundary(float origin, int cell, float d, float delta) =>
        d > 0 ? (cell + 1 - origin) * delta : d < 0 ? (origin - cell) * delta : float.PositiveInfinity;

    static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
