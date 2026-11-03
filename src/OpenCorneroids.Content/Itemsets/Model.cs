using System.Numerics;

namespace OpenCorneroids.Content.Itemsets;

/// <summary>Blueprint <c>usage</c>; matches the crafting block <c>type</c> attribute (craftingtable/extractor/smelter).</summary>
public enum CraftingStation { CraftingTable = 0, Extractor = 1, Smelter = 2 }

public enum AsteroidType { Light, Medium, Heavy }

public enum WearableSlot { Helmet = 0, Shoulders = 1, Chest = 2, Legs = 3, Arms = 4, Feet = 5, PowerSource = 6 }

/// <summary>Consumable <c>type</c>: 1 = applied at once (duration 0), 0 = timed buff.</summary>
public enum ConsumableKind { TimedBuff = 0, Instant = 1 }

public sealed record FaceSet(CellRect Walls, CellRect Top, CellRect Bottom)
{
    public IEnumerable<(string Face, CellRect Rect)> Faces => [("walls", Walls), ("top", Top), ("bottom", Bottom)];
}

public sealed record BlockDef(
    int BlockId, float Mass, int Health, int PowerToDrill,
    Vector3 Size, Vector3 ModelSize, Vector3 ModelPlacement,
    FaceSet Active, FaceSet? Inactive, int? Power)
{
    /// <summary>Texture regions span the visible model, not the placement bounds (thruster exhaust, door travel).</summary>
    public Vector3 VisualSize => ModelSize == Vector3.Zero ? Size : ModelSize;
}

/// <param name="Kind">The element name: armor, door, drill, wearable…</param>
/// <param name="Category">The container element: armors, doors, drills, wearables…</param>
/// <param name="Sprite">Icon cell for items. Blocks carry a raw value here that is not an icon; keep it, don't draw it.</param>
/// <param name="Extras">Fields this loader doesn't know yet, kept so newer mods still load.</param>
public sealed record ItemDef(
    int ItemId, string Kind, string Category, string Name, string Description, Rgb8 Color,
    int MaxStackSize, int ModelDepth, int PowerUsage, CellRect Sprite,
    BlockDef? Block, IReadOnlyList<IItemComponent> Components,
    IReadOnlyDictionary<string, string> Extras, int? Line)
{
    public T? Get<T>() where T : class, IItemComponent => Components.OfType<T>().FirstOrDefault();
}

public sealed record ItemStack(int ItemId, int Quantity);

public sealed record BlueprintDef(string Name, CraftingStation Usage, IReadOnlyList<ItemStack> Ingredients, ItemStack Result, int? Line);

public sealed record ProjectileDef(
    int ProjectileId, Rgb8 Color, int Damage, int LifetimeMs, float RicochetAngle, float Speed, Vector3 Size, int? Line);

/// <param name="Version">The root <c>version</c> attribute. Its meaning is unknown (1 for vanilla, 5 for DarthNox); opaque.</param>
public sealed record ItemsetHeader(
    string Name, string Author, string Key, int? Version, string? BlockTextureKey, string? SpriteTextureKey);

public sealed class ItemsetDef
{
    /// <summary>Content path of the itemset XML, e.g. <c>Blocksets/defaultItemset.xml</c>.</summary>
    public required string Path { get; init; }
    public required ItemsetHeader Header { get; init; }
    /// <summary>Items in file order. IDs are the original ones: sparse, and blockId 0 is a real block.</summary>
    public required IReadOnlyList<ItemDef> Items { get; init; }
    public required IReadOnlyDictionary<int, ItemDef> ItemsById { get; init; }
    public required IReadOnlyDictionary<int, ItemDef> ItemsByBlockId { get; init; }
    public required IReadOnlyDictionary<int, ProjectileDef> Projectiles { get; init; }
    public required IReadOnlyList<BlueprintDef> Blueprints { get; init; }
    public required IReadOnlyList<int> DefaultItems { get; init; }
    /// <summary>Content identity; <c>key</c> and <c>version</c> are not unique across mods. See <see cref="ItemsetFingerprint"/>.</summary>
    public required string Fingerprint { get; init; }

    public IEnumerable<ItemDef> Blocks => Items.Where(i => i.Block is not null);
}
