using System.Numerics;
using OpenCorneroids.Content.Itemsets;

namespace OpenCorneroids.Simulation.Persistence;

/// <summary>
/// Which itemset a world was built with. <see cref="Key"/> and the itemset's own version are not unique across mods,
/// so <see cref="Fingerprint"/> (see <see cref="ItemsetFingerprint"/>) is what decides whether the content is the same;
/// <see cref="Name"/> and <see cref="PackId"/> are kept so a mismatch can be explained to the player.
/// </summary>
public sealed record ItemsetBinding(string Key, string Name, string PackId, string Fingerprint);

/// <param name="Pitch">Radians.</param>
/// <param name="Yaw">Radians.</param>
public sealed record PlayerState(Vector3 Position, Vector3 Velocity, float Pitch, float Yaw);

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
