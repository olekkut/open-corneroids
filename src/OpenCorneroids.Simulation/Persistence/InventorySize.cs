using OpenCorneroids.Simulation.Items;
using OpenCorneroids.Simulation.Settings;

namespace OpenCorneroids.Simulation.Persistence;

/// <summary>
/// How big a world's survival inventory is: the 8-slot hotbar plus <c>sv_inventory_slots</c> main slots, 30 (6 × 5) as
/// in the original. The size is a world rule read once, when the world loads.
/// </summary>
/// <remarks>
/// Ours, for saves from before the rule: those worlds had a 40-slot inventory (the hotbar and 32 main slots) and saved
/// every slot. When a world's rules do not set the size and its saved inventory has more slots than the default, the
/// rule becomes the saved count minus the hotbar for that world, so old worlds keep their size and nothing moves.
/// </remarks>
public static class InventorySize
{
    static Cvar Rule => Cvars.InventorySlots;

    /// <summary>The main slots (beside the hotbar) the load of <paramref name="saved"/> gives; the default for a new world.</summary>
    public static int MainSlotsFor(WorldSnapshot? saved)
    {
        if (saved is null) return Default;
        if (SavedRule(saved.Rules) is { } ruled) return ruled;
        return LegacyMainSlots(saved.Inventory.Count) ?? Default;
    }

    /// <summary>The whole inventory, hotbar included, that the load of <paramref name="saved"/> creates.</summary>
    public static int SlotCountFor(WorldSnapshot? saved) => Inventory.HotbarSize + MainSlotsFor(saved);

    /// <summary>
    /// Where items added during the load of <paramref name="saved"/> go: its <c>sv_inventory_fill_hotbar_first</c> rule,
    /// or the original's slot order when the rules do not set it (or set it to something import ignores). The check
    /// before loading and the load itself both use this, as they both use <see cref="SlotCountFor"/>: over several
    /// additions the two orders can fit different amounts.
    /// </summary>
    public static FillOrder FillOrderFor(WorldSnapshot? saved) =>
        FillOrderOf(saved is not null && Find(saved.Rules, Cvars.InventoryFillHotbarFirst) is { } text &&
                    Cvars.InventoryFillHotbarFirst.TryParse(text, out var value, out _)
            ? value != 0
            : Cvars.InventoryFillHotbarFirst.Default != 0);

    /// <summary>The fill order <c>sv_inventory_fill_hotbar_first</c> stands for.</summary>
    public static FillOrder FillOrderOf(bool hotbarFirst) => hotbarFirst ? FillOrder.SlotOrder : FillOrder.TopUpFirst;

    /// <summary>
    /// Sets <c>sv_inventory_slots</c> in <paramref name="cvars"/> to what this world uses, after its saved rules were
    /// imported, so a pre-rule world keeps its larger inventory as its own rule. Returns the inventory's slot count.
    /// </summary>
    public static int Apply(CvarStore cvars, WorldSnapshot? saved)
    {
        ArgumentNullException.ThrowIfNull(cvars);
        int main = MainSlotsFor(saved);
        if (cvars.Int(Rule) != main) cvars.Set(Rule, main);
        return Inventory.HotbarSize + main;
    }

    /// <summary>
    /// The world rules to save. Rules at their default are normally left out, but a world whose inventory still has more
    /// slots than the default (its size changes only on the next load) must say it wants the default, or loading it
    /// would read the slot count as a pre-rule world's and keep the old size.
    /// </summary>
    /// <param name="rules">The changed world rules (<see cref="CvarStore.Export"/>).</param>
    /// <param name="mainSlots">The rule's current value.</param>
    /// <param name="savedSlotCount">How many inventory slots the save holds.</param>
    public static IReadOnlyDictionary<string, string> RulesToSave(IReadOnlyDictionary<string, string> rules, int mainSlots,
        int savedSlotCount)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (Find(rules, Rule) is not null || LegacyMainSlots(savedSlotCount) is null) return rules;
        return new Dictionary<string, string>(rules, StringComparer.Ordinal) { [Rule.Name] = Rule.Format(mainSlots) };
    }

    /// <summary>The main slots a save without the rule had, when it is more than the default; null otherwise.</summary>
    public static int? LegacyMainSlots(int savedSlotCount) =>
        savedSlotCount > Inventory.HotbarSize + Default
            ? (int)Math.Clamp(savedSlotCount - Inventory.HotbarSize, Rule.Min, Rule.Max)
            : null;

    static int Default => (int)Rule.Default;

    /// <summary>The saved value of the rule, when it is there and valid (an invalid one is ignored on import too).</summary>
    static int? SavedRule(IReadOnlyDictionary<string, string> rules) =>
        Find(rules, Rule) is { } text && Rule.TryParse(text, out var value, out _) ? (int)value : null;

    /// <summary>Rule names are matched without case, as <see cref="CvarStore.Import"/> does.</summary>
    static string? Find(IReadOnlyDictionary<string, string> rules, Cvar cvar) =>
        rules.FirstOrDefault(p => p.Key.Trim().Equals(cvar.Name, StringComparison.OrdinalIgnoreCase)).Value;
}
