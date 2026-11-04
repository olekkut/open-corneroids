using System.Numerics;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Shapes;

namespace OpenCorneroids.Simulation.Persistence;

/// <summary>
/// Which itemset a world was built with. <see cref="Key"/> and the itemset's own version are not unique across mods,
/// so <see cref="Fingerprint"/> (see <see cref="ItemsetFingerprint"/>) is what decides whether the content is the same;
/// <see cref="Name"/> and <see cref="PackId"/> are kept so a mismatch can be explained to the player.
/// </summary>
public sealed record ItemsetBinding(string Key, string Name, string PackId, string Fingerprint);

/// <param name="Velocity">The whole velocity, including <see cref="Drift"/>.</param>
/// <param name="Pitch">Radians.</param>
/// <param name="Yaw">Radians.</param>
public sealed record PlayerState(Vector3 Position, Vector3 Velocity, float Pitch, float Yaw)
{
    /// <summary>
    /// The part of <see cref="Velocity"/> that is momentum the player was given (stepping off a moving ship) and that
    /// the game does not damp away. Optional in the file: left out when zero, so older files and worlds saved standing
    /// still read and write as before; an older build that ignores it only loses that momentum.
    /// </summary>
    public Vector3 Drift { get; init; }
}

/// <summary>
/// A block that is not stored in the cell grid: a multi-cell or turned block (doors, guns, thrusters...), kept whole so
/// its orientation and moving parts survive a save.
/// </summary>
/// <param name="BlockId">The ORIGINAL itemset blockId, as in the palette.</param>
/// <param name="Origin">Minimum cell of the footprint; see <see cref="BlockShape"/>.</param>
/// <param name="Orientation">An <see cref="Shapes.Orientation.Index"/>, 0..23.</param>
/// <param name="Open">Door open fraction 0..1; 0 for everything else.</param>
public sealed record PlacedObject(int BlockId, Int3 Origin, int Orientation, float Open)
{
    /// <summary>
    /// Why this object can't sit in a world of edge <paramref name="worldSize"/> with the given shape, or null if it fits.
    /// The save format can only check the origin, because footprints depend on the itemset; a loader that has the
    /// itemset calls this for the rest.
    /// </summary>
    public string? FootprintProblem(BlockShape shape, int worldSize)
    {
        ArgumentNullException.ThrowIfNull(shape);
        if (!Shapes.Orientation.IsValidIndex(Orientation)) return $"orientation {Orientation} is outside 0..{Shapes.Orientation.Count - 1}";
        var far = Origin + shape.RotatedSize(Shapes.Orientation.FromIndex(Orientation));
        if (Origin.X < 0 || Origin.Y < 0 || Origin.Z < 0 || far.X > worldSize || far.Y > worldSize || far.Z > worldSize)
            return $"block {BlockId} at {Origin} reaches outside the {worldSize}-cell world";
        return null;
    }
}

/// <summary>
/// One ship: a cubic block grid of its own with a transform, so it can sit anywhere in the world at any angle. Cells and
/// objects follow the same rules as the world's (<see cref="WorldSnapshot.Palette"/>, <see cref="WorldSnapshot.Cells"/>,
/// <see cref="PlacedObject"/>), in the ship's LOCAL cell coordinates 0..<see cref="Size"/>−1.
/// </summary>
/// <param name="Position">World position of the ship grid's local origin (the minimum corner of local cell 0,0,0).</param>
/// <param name="Rotation">Unit quaternion turning ship-local directions into world directions.</param>
/// <param name="LinearVelocity">World space, units per second.</param>
/// <param name="AngularVelocity">World axes, radians per second.</param>
/// <param name="Size">Edge length of the ship's cubic local grid.</param>
/// <param name="Palette">As <see cref="WorldSnapshot.Palette"/>: entry 0 is the air sentinel, the rest ORIGINAL blockIds.</param>
/// <param name="Cells">Size³ palette indices in <see cref="WorldSnapshot.CellIndex"/> order.</param>
public sealed record ShipSnapshot(
    Vector3 Position, Quaternion Rotation, Vector3 LinearVelocity, Vector3 AngularVelocity,
    int Size, IReadOnlyList<int> Palette, ushort[] Cells)
{
    /// <summary>The ship's multi-cell blocks, origins in ship-local cells.</summary>
    public IReadOnlyList<PlacedObject> Objects { get; init; } = [];
}

/// <summary>Everything a save file holds, as plain data so the simulation can be rebuilt without Godot.</summary>
/// <param name="SavedAt">Supplied by the caller; the library never reads the clock, which keeps saves reproducible in tests.</param>
/// <param name="Size">Edge length of the cubic cell grid.</param>
/// <param name="Palette">
/// Maps cell values to ORIGINAL itemset blockIds. Entry 0 is the air sentinel -1, because blockId 0 is a real block.
/// A palette keeps cells at 16 bits whatever the itemset's blockId range, and lets a binding check see exactly which
/// blockIds the world depends on.
/// </param>
/// <param name="Cells">Size³ palette indices at <c>(z * Size + y) * Size + x</c>; see <see cref="CellIndex"/>.</param>
/// <param name="Inventory">Slots in order; null is an empty slot, so slot positions survive a round trip.</param>
public sealed record WorldSnapshot(
    string Name, DateTimeOffset SavedAt, ItemsetBinding Itemset, int Seed, int Size,
    IReadOnlyList<int> Palette, ushort[] Cells, PlayerState Player, IReadOnlyList<ItemStack?> Inventory)
{
    /// <summary>
    /// Creative rules (free placing, instant mining, no inventory) rather than survival. Optional in the file: saves
    /// written before it existed have no such field and read as survival, which is what every world was then.
    /// </summary>
    public bool Creative { get; init; }

    /// <summary>
    /// Ids of the texture packs layered over the itemset when the world was played, top layer first, so a reload looks
    /// the same. Purely cosmetic (the binding decides compatibility); optional in the file, empty for older saves.
    /// </summary>
    public IReadOnlyList<string> TexturePacks { get; init; } = [];

    /// <summary>
    /// Blocks kept outside the cell grid (see <see cref="PlacedObject"/>). Optional in the file: saves written before
    /// it existed have none, which is what they held. An init property rather than a constructor parameter so existing
    /// callers keep compiling.
    /// </summary>
    public IReadOnlyList<PlacedObject> Objects { get; init; } = [];

    /// <summary>
    /// Ships: block grids of their own that are not part of the static world (see <see cref="ShipSnapshot"/>). Optional
    /// in the file: saves written before ships existed have none.
    /// </summary>
    public IReadOnlyList<ShipSnapshot> Ships { get; init; } = [];

    /// <summary>Palette index and cell value of air.</summary>
    public const ushort Air = 0;

    /// <summary>The palette entry that marks air; not a blockId.</summary>
    public const int AirBlockId = -1;

    public static int CellIndex(int size, int x, int y, int z) => (z * size + y) * size + x;
}

/// <summary>What a world list needs, read without decompressing the cells.</summary>
/// <param name="FilePath">Where the summary was read from; empty when it came from a stream that is not a file.</param>
public sealed record WorldSummary(string Name, DateTimeOffset SavedAt, ItemsetBinding Itemset, string FilePath)
{
    /// <summary>See <see cref="WorldSnapshot.Creative"/>; lets a world list label creative worlds.</summary>
    public bool Creative { get; init; }
}

/// <summary>A save file is damaged, truncated, hostile or from a newer build. The message is fit to show the player.</summary>
public sealed class WorldSaveException : Exception
{
    public WorldSaveException(string message) : base(message) { }
    public WorldSaveException(string message, Exception inner) : base(message, inner) { }
}
