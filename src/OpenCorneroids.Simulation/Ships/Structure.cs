using OpenCorneroids.Simulation.Shapes;

namespace OpenCorneroids.Simulation.Ships;

/// <summary>The cells <see cref="Structure.Collect"/> found.</summary>
public sealed class StructureScan(IReadOnlyList<Int3> cells, bool truncated)
{
    HashSet<Int3>? _set;

    /// <summary>Every collected cell, in breadth-first order from the start (the start first).</summary>
    public IReadOnlyList<Int3> Cells { get; } = cells;

    /// <summary>True when the search stopped at the cell limit with more connected cells left.</summary>
    public bool Truncated { get; } = truncated;

    /// <summary>Whether <paramref name="cell"/> was collected.</summary>
    public bool Contains(Int3 cell) => (_set ??= [.. Cells]).Contains(cell);
}

/// <summary>
/// Finds a ship's extent: the cells face-connected to a start cell (the console) through blocks that belong to a ship.
/// Ours; original behaviour unknown.
/// </summary>
public static class Structure
{
    /// <summary>Default cap on the cells one search collects, so launching from a console welded to a huge station
    /// cannot stall the game. Tunable.</summary>
    public const int DefaultMaxCells = 32_768;

    static readonly Int3[] Neighbours = [Int3.UnitX, -Int3.UnitX, Int3.UnitY, -Int3.UnitY, Int3.UnitZ, -Int3.UnitZ];

    /// <summary>
    /// Breadth-first search from <paramref name="start"/> over face neighbours (+X, −X, +Y, −Y, +Z, −Z order), visiting
    /// a cell when <paramref name="blockAt"/> gives a block id there and <paramref name="conducts"/> accepts it. For a
    /// launch the predicate rejects asteroid rock and minerals, so the ship stops at them. Multi-cell objects are passed
    /// in as their cells (each reporting the object's block id), so they join through whichever cell touches.
    /// </summary>
    /// <returns>Empty when the start cell itself is empty or rejected.</returns>
    /// <param name="maxCells">At least 1; the search stops once it has this many cells and reports
    /// <see cref="StructureScan.Truncated"/> if any accepted neighbour was left out.</param>
    public static StructureScan Collect(Int3 start, Func<Int3, int?> blockAt, Func<int, bool> conducts, int maxCells = DefaultMaxCells)
    {
        ArgumentNullException.ThrowIfNull(blockAt);
        ArgumentNullException.ThrowIfNull(conducts);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCells, 1);
        bool Accepts(Int3 cell) => blockAt(cell) is { } id && conducts(id);
        if (!Accepts(start)) return new StructureScan([], false);

        var cells = new List<Int3> { start };
        var seen = new HashSet<Int3> { start };
        bool truncated = false;
        for (int head = 0; head < cells.Count && !truncated; head++)
        {
            foreach (var step in Neighbours)
            {
                var next = cells[head] + step;
                if (seen.Contains(next) || !Accepts(next)) continue;
                if (cells.Count >= maxCells)
                {
                    truncated = true;
                    break;
                }
                seen.Add(next);
                cells.Add(next);
            }
        }
        return new StructureScan(cells, truncated);
    }
}
