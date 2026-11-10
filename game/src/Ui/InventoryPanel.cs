using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Content;
using OpenCorneroids.Simulation.Items;

namespace OpenCorneroids.Game.Ui;

/// <summary>
/// The inventory grid inside a classic bevelled window: the main inventory on top, six slots wide as in the original,
/// and the hotbar row (slots 0..7) below it. Clicks move stacks through the cursor (<see cref="ItemHand"/>) by the
/// original's rules: left click takes or puts the whole stack, right click takes half or puts one. Ours: putting a
/// held stack on the trash slot throws it away (<see cref="Inventory.Discard(ItemStack)"/>), and clicking the trash
/// with an empty hand takes the last thrown stack back onto the cursor.
/// </summary>
public partial class InventoryPanel : ClassicWindow
{
    const float SlotSize = 48;

    /// <summary>The main inventory is six slots wide, as in the original (6 × 5 for the default 30 slots).</summary>
    const int MainColumns = 6;

    /// <summary>Ours: a main inventory larger than this many rows (<c>sv_inventory_slots</c>) grows wider instead, so the window fits the screen.</summary>
    const int MaxMainRows = 8;

    Inventory _inventory = null!;
    ItemsetDef _itemset = null!;
    ItemIcons _icons = null!;
    ItemSlot[] _all = [];
    ItemSlot _trash = null!;
    Label _trashHint = null!;
    ItemHand _hand = null!;

    /// <param name="hand">The cursor stack shared with the other item windows.</param>
    public void Setup(Inventory inventory, ItemHand hand, ItemsetDef itemset, ItemIcons icons, IReadOnlyList<int> endless)
    {
        _inventory = inventory;
        _hand = hand;
        _itemset = itemset;
        _icons = icons;
        Configure("Inventory", bevel: 5, padLeft: 18, padTop: 32, padRight: 18, padBottom: 16);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        AddChild(column);

        _all = new ItemSlot[inventory.Slots.Count];
        int mainSlots = Math.Max(0, _all.Length - Inventory.HotbarSize);
        var backpack = Grid(Math.Max(MainColumns, (mainSlots + MaxMainRows - 1) / MaxMainRows));
        for (int i = Inventory.HotbarSize; i < _all.Length; i++) backpack.AddChild(_all[i] = NewSlot(i));
        column.AddChild(backpack);

        column.AddChild(ClassicUi.Label($"Hotbar (1-{Inventory.HotbarSize})", 14, new Color(1f, 0.96f, 0.72f)));
        var hotbar = Grid(Inventory.HotbarSize);
        for (int i = 0; i < Math.Min(Inventory.HotbarSize, _all.Length); i++) hotbar.AddChild(_all[i] = NewSlot(i));
        column.AddChild(hotbar);

        var trashRow = new HBoxContainer();
        trashRow.AddThemeConstantOverride("separation", 8);
        _trash = ItemSlot.Create(SlotSize, MouseFilterEnum.Stop);
        _trash.Clicked += OnTrashClicked;
        trashRow.AddChild(_trash);
        _trashHint = ClassicUi.Label("", 13, new Color(1f, 0.9f, 0.88f));
        _trashHint.VerticalAlignment = VerticalAlignment.Center;
        _trashHint.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        trashRow.AddChild(_trashHint);
        column.AddChild(trashRow);

        // The starting drill and stations never run out, as in the original: a click takes one.
        if (endless.Count > 0)
        {
            var endlessRow = new HBoxContainer();
            endlessRow.AddThemeConstantOverride("separation", 6);
            foreach (var itemId in endless)
            {
                if (!itemset.ItemsById.TryGetValue(itemId, out var item)) continue;
                var slot = ItemSlot.Create(SlotSize, MouseFilterEnum.Stop);
                slot.Show(item, 0, icons, "∞");
                slot.TooltipText = $"{item.Name}: never runs out, click to take one";
                slot.Clicked += _ => TakeEndless(item);
                endlessRow.AddChild(slot);
            }
            var hint = ClassicUi.Label("Starter kit (click to take)", 13);
            hint.VerticalAlignment = VerticalAlignment.Center;
            hint.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            endlessRow.AddChild(hint);
            column.AddChild(endlessRow);
        }

        inventory.Changed += Refresh;
        Refresh();
    }

    /// <summary>An endless slot puts one more on the cursor, as in the original.</summary>
    void TakeEndless(ItemDef item)
    {
        if (_hand.TakeEndless(item.ItemId, _inventory.MaxStackOf(item.ItemId))) return;
        _trashHint.Text = _hand.Held?.ItemId == item.ItemId ? "Your hand is full" : "Put down what you hold first";
    }

    public override void _ExitTree() => _inventory.Changed -= Refresh;

    ItemSlot NewSlot(int index)
    {
        var slot = ItemSlot.Create(SlotSize, MouseFilterEnum.Stop);
        slot.Clicked += button => OnClicked(index, button);
        return slot;
    }

    void OnClicked(int index, MouseButton button) =>
        _hand.Click(_inventory, index, button == MouseButton.Right ? HandButton.Right : HandButton.Left);

    /// <summary>Ours: a held stack is thrown away; with an empty hand the trashed stack comes back onto the cursor.</summary>
    void OnTrashClicked(MouseButton button) => _hand.ClickTrash(_inventory);

    void Refresh()
    {
        for (int i = 0; i < _all.Length; i++)
        {
            var stack = _inventory.Slots[i];
            var item = stack is not null && _itemset.ItemsById.TryGetValue(stack.ItemId, out var def) ? def : null;
            _all[i].Show(item, stack?.Quantity ?? 0, _icons);
            if (stack is not null && item is null) _all[i].TooltipText = $"Unknown item {stack.ItemId}";
            _all[i].SetLook(i < Inventory.HotbarSize ? ItemSlot.Look.Hotbar : ItemSlot.Look.Normal);
        }

        var trash = _inventory.Trash;
        var trashed = trash is not null && _itemset.ItemsById.TryGetValue(trash.ItemId, out var trashedItem) ? trashedItem : null;
        _trash.Show(trashed, trash?.Quantity ?? 0, _icons);
        _trash.SetLook(ItemSlot.Look.Trash);
        _trash.TooltipText = trash is null ? "Trash" : $"Trash: {trashed?.Name ?? $"item {trash.ItemId}"} (click to take back)";
        _trashHint.Text = trash is null
            ? "Trash (put a stack here to discard)"
            : "Trash (click to restore)";
    }

    static GridContainer Grid(int columns)
    {
        var grid = new GridContainer { Columns = columns };
        grid.AddThemeConstantOverride("h_separation", 2);
        grid.AddThemeConstantOverride("v_separation", 2);
        return grid;
    }
}
