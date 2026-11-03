using System.Numerics;
using OpenCorneroids.Content;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Items;

namespace OpenCorneroids.Simulation.Tests.Items.Support;

/// <summary>A hand-built itemset with made-up ids and numbers. No original game data is used.</summary>
static class TestItems
{
    public const int Rock = 1;       // block, health 5, powerToDrill 1, stack 64
    public const int Ore = 2;        // block, health 20, powerToDrill 3, stack 64
    public const int Glass = 3;      // block, health 0, powerToDrill 0, stack 64
    public const int Plate = 10;     // stack 10
    public const int Bar = 11;       // stack 64
    public const int Single = 12;    // maxStackSize 0 in the data, so 1 in practice
    public const int WeakDrill = 20; // power 2, stack 1
    public const int StrongDrill = 21; // power 5, stack 1
    public const int Unknown = 999;

    public static ItemsetDef Set(IReadOnlyList<BlueprintDef>? blueprints = null, IReadOnlyList<int>? defaultItems = null)
    {
        ItemDef[] items =
        [
            Item(Rock, "asteroid", 64, Block(Rock, health: 5, powerToDrill: 1)),
            Item(Ore, "mineral", 64, Block(Ore, health: 20, powerToDrill: 3)),
            Item(Glass, "window", 64, Block(Glass, health: 0, powerToDrill: 0)),
            Item(Plate, "ingredient", 10),
            Item(Bar, "ingredient", 64),
            Item(Single, "ingredient", 0),
            Item(WeakDrill, "drill", 1, components: [new DrillComponent(2)]),
            Item(StrongDrill, "drill", 1, components: [new DrillComponent(5)]),
        ];
        return new ItemsetDef
        {
            Path = "Blocksets/testItemset.xml",
            Header = new ItemsetHeader("Test", "Tests", "TestItemset", 1, null, null),
            Items = items,
            ItemsById = items.ToDictionary(i => i.ItemId),
            ItemsByBlockId = items.Where(i => i.Block is not null).ToDictionary(i => i.Block!.BlockId),
            Projectiles = new Dictionary<int, ProjectileDef>(),
            Blueprints = blueprints ?? Blueprints,
            DefaultItems = defaultItems ?? [WeakDrill, Rock],
            Fingerprint = "test",
        };
    }

    public static readonly BlueprintDef PlateFromRock = Bp("Plate", CraftingStation.CraftingTable, [S(Rock, 2)], S(Plate, 1));
    public static readonly BlueprintDef BarFromRockAndOre =
        Bp("Bar", CraftingStation.Smelter, [S(Rock, 1), S(Ore, 1), S(Rock, 2)], S(Bar, 2));
    public static readonly BlueprintDef DrillFromPlates = Bp("Drill", CraftingStation.CraftingTable, [S(Plate, 3)], S(WeakDrill, 1));
    public static readonly BlueprintDef GlassFromRock = Bp("Glass", CraftingStation.Smelter, [S(Rock, 1)], S(Glass, 1));
    public static readonly BlueprintDef PlatesFromBar = Bp("Plates", CraftingStation.Extractor, [S(Bar, 1)], S(Plate, 5));

    public static readonly IReadOnlyList<BlueprintDef> Blueprints =
        [PlateFromRock, BarFromRockAndOre, GlassFromRock, DrillFromPlates, PlatesFromBar];

    public static Inventory NewInventory(int slotCount = 36) => Inventory.ForItemset(Set(), slotCount);

    public static ItemStack S(int itemId, int quantity) => new(itemId, quantity);

    public static BlueprintDef Bp(string name, CraftingStation station, IReadOnlyList<ItemStack> ingredients, ItemStack result) =>
        new(name, station, ingredients, result, null);

    public static ItemDef Item(int id, string kind, int maxStack, BlockDef? block = null, IReadOnlyList<IItemComponent>? components = null) =>
        new(id, kind, kind + "s", $"Item {id}", "", new Rgb8(1, 2, 3), maxStack, 1, 0, new CellRect(0, 0, 1, 1),
            block, components ?? [], new Dictionary<string, string>(), null);

    public static BlockDef Block(int blockId, int health, int powerToDrill)
    {
        var faces = new FaceSet(new CellRect(0, 0, 1, 1), new CellRect(0, 0, 1, 1), new CellRect(0, 0, 1, 1));
        return new BlockDef(blockId, 1, health, powerToDrill, Vector3.One, Vector3.Zero, Vector3.Zero, faces, null, null);
    }

    /// <summary>Slot contents as compact strings, e.g. "1x5", "-" for empty, for readable assertions.</summary>
    public static string[] Layout(this Inventory inventory) =>
        inventory.Slots.Select(s => s is null ? "-" : $"{s.ItemId}x{s.Quantity}").ToArray();

}

/// <summary>Counts <see cref="Inventory.Changed"/> events.</summary>
sealed class ChangeCounter
{
    public ChangeCounter(Inventory inventory) => inventory.Changed += () => Count++;
    public int Count { get; private set; }
}
