using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Persistence;
using OpenCorneroids.Simulation.Shapes;

namespace OpenCorneroids.Simulation.Ships;

/// <summary>
/// The rules for turning a structure of the static world into a ship (F on its console). All ours (tunable; original
/// behaviour unknown).
/// </summary>
public static class Launch
{
    /// <summary>Edge of a new ship's local cell grid. Tunable; must be a multiple of the game's chunk size (16).</summary>
    public const int ShipGridSize = 64;

    /// <summary>
    /// Largest structure extent (per axis, in cells, multi-block footprints included) that can launch, so a new ship
    /// keeps <c>(ShipGridSize − MaxExtent) / 2</c> = 8 free cells on every side of its grid to be built on. Tunable.
    /// </summary>
    public const int MaxExtent = 48;

    /// <summary>
    /// Most cells one structure search collects (<see cref="Structure.Collect"/>): a console welded to a huge station
    /// is refused quickly instead of stalling the game. A full 48³ box would be far more, but no ship that fits the
    /// extent needs that many solid cells. Tunable.
    /// </summary>
    public const int MaxCells = 20_000;

    /// <summary>
    /// Whether a block can be part of a ship: everything except asteroid rock and minerals, so a ship built against an
    /// asteroid stops at the rock and leaves it behind.
    /// </summary>
    public static bool BelongsToShip(ItemDef item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Get<AsteroidComponent>() is null && item.Get<MineralComponent>() is null;
    }

    /// <summary>Why the structure can't launch, or null when it can.</summary>
    /// <param name="blocks">Grid blocks plus multi-block objects found, the console included.</param>
    /// <param name="min">Minimum cell of the structure's bounds (multi-block footprints included).</param>
    /// <param name="max">Maximum cell of those bounds (inclusive).</param>
    /// <param name="truncated">The search stopped at <see cref="MaxCells"/> (<see cref="StructureScan.Truncated"/>).</param>
    public static string? Problem(int blocks, Int3 min, Int3 max, bool truncated)
    {
        if (truncated) return $"Can't launch: the structure is too big (over {MaxCells} cells)";
        if (blocks < 1) return "Can't launch: there is no console there";
        if (blocks == 1) return "Can't launch: the console is not attached to any other block";
        var extent = max - min + Int3.One;
        int largest = Math.Max(extent.X, Math.Max(extent.Y, extent.Z));
        if (largest > MaxExtent)
            return $"Can't launch: the structure is {extent.X}×{extent.Y}×{extent.Z} cells, over the {MaxExtent}-cell limit";
        return null;
    }

    /// <summary>
    /// Why one more ship can't be launched into a world that already has <paramref name="ships"/> ships holding
    /// <paramref name="shipCells"/> grid cells together (each ship's edge cubed), or null when it can. The limits are the
    /// save format's (<see cref="WorldSaveFormat.MaxShips"/>, <see cref="WorldSaveFormat.MaxTotalShipCells"/>), so a
    /// launch never leaves a world that can no longer be saved.
    /// </summary>
    public static string? FleetProblem(int ships, long shipCells, int gridSize = ShipGridSize)
    {
        if (ships >= WorldSaveFormat.MaxShips)
            return $"Can't launch: there are already {ships} ships, the most a world can save is {WorldSaveFormat.MaxShips}";
        if (shipCells + (long)gridSize * gridSize * gridSize > WorldSaveFormat.MaxTotalShipCells)
            return "Can't launch: the ships already fill the space a world can save";
        return null;
    }

    /// <summary>
    /// Where the structure's minimum cell goes in the new ship's local grid: the bounds centred in it (an odd leftover
    /// cell goes to the positive side). The ship's grid origin then sits at world <c>min − LocalMin</c>, so every block
    /// keeps its world position.
    /// </summary>
    /// <exception cref="ArgumentException">The bounds don't fit a grid of <paramref name="gridSize"/>.</exception>
    public static Int3 LocalMin(Int3 min, Int3 max, int gridSize = ShipGridSize)
    {
        var extent = max - min + Int3.One;
        if (extent.X < 1 || extent.Y < 1 || extent.Z < 1 || extent.X > gridSize || extent.Y > gridSize || extent.Z > gridSize)
            throw new ArgumentException($"Bounds {min}..{max} don't fit a {gridSize}-cell grid.");
        return new Int3((gridSize - extent.X) / 2, (gridSize - extent.Y) / 2, (gridSize - extent.Z) / 2);
    }
}
