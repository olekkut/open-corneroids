using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Content;
using OpenCorneroids.Simulation.Items;

namespace OpenCorneroids.Game.Ui;

/// <summary>
/// The inventory grid inside a classic bevelled window: the backpack rows on top and the hotbar row (slots 0..7) below
/// them. Click a stack to pick it up, click another slot to put it there; putting a stack on the trash slot throws it
/// away (<see cref="Inventory.Discard"/>), and clicking the trash with nothing picked takes the last thrown stack back.
/// </summary>
public partial class InventoryPanel : ClassicWindow
{
    const float SlotSize = 48;

    Inventory _inventory = null!;
    ItemsetDef _itemset = null!;
    ItemIcons _icons = null!;
    ItemSlot[] _all = [];
    ItemSlot _trash = null!;
    Label _trashHint = null!;
    int? _picked;
    int _pickedItemId;

    public void Setup(Inventory inventory, ItemsetDef itemset, ItemIcons icons, IReadOnlyList<int> endless)
    {
        _inventory = inventory;
        _itemset = itemset;
        _icons = icons;
        Configure("Inventory", bevel: 5, padLeft: 18, padTop: 32, padRight: 18, padBottom: 16);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        AddChild(column);

        _all = new ItemSlot[inventory.Slots.Count];
        var backpack = Grid();
        for (int i = Inventory.HotbarSize; i < _all.Length; i++) backpack.AddChild(_all[i] = NewSlot(i));
        column.AddChild(backpack);

        column.AddChild(ClassicUi.Label($"Hotbar (1-{Inventory.HotbarSize})", 14, new Color(1f, 0.96f, 0.72f)));
        var hotbar = Grid();
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

    void TakeEndless(ItemDef item)
    {
        CancelPick();
        _trashHint.Text = _inventory.Add(item.ItemId, 1) == 0 ? $"Took a {item.Name}" : "No room for it";
    }

    /// <summary>The slot whose stack is picked up, waiting for a second click; null when nothing is.</summary>
    public int? PickedSlot => _picked;

    /// <summary>Forgets a half-finished move, e.g. when the window closes.</summary>
    public void CancelPick()
    {
        _picked = null;
        Refresh();
    }

    public override void _ExitTree() => _inventory.Changed -= Refresh;

    ItemSlot NewSlot(int index)
    {
        var slot = ItemSlot.Create(SlotSize, MouseFilterEnum.Stop);
        slot.Clicked += button => OnClicked(index, button);
        return slot;
    }

    void OnClicked(int index, MouseButton button)
    {
        if (button == MouseButton.Right || _picked == index)
        {
            CancelPick();
            return;
        }
        if (_picked is { } from)
        {
            _picked = null;
            _inventory.MoveOrMerge(from, index);
            Refresh();
        }
        else if (_inventory.Slots[index] is { } stack)
        {
            _picked = index;
            _pickedItemId = stack.ItemId;
            Refresh();
        }
    }

    /// <summary>With a stack picked, throws it away; with nothing picked, takes the trashed stack back.</summary>
    void OnTrashClicked(MouseButton button)
    {
        if (button == MouseButton.Right)
        {
            CancelPick();
            return;
        }
        if (_picked is { } from)
        {
            _picked = null;
            _inventory.Discard(from);
        }
        else if (_inventory.Trash is not null && _inventory.RestoreTrash() == 0)
        {
            _trashHint.Text = "No room to take it back";
            return;
        }
        Refresh();
    }

    void Refresh()
    {
        if (_picked is { } picked && _inventory.Slots[picked]?.ItemId != _pickedItemId) _picked = null;
        for (int i = 0; i < _all.Length; i++)
        {
            var stack = _inventory.Slots[i];
            var item = stack is not null && _itemset.ItemsById.TryGetValue(stack.ItemId, out var def) ? def : null;
            _all[i].Show(item, stack?.Quantity ?? 0, _icons);
            if (stack is not null && item is null) _all[i].TooltipText = $"Unknown item {stack.ItemId}";
            _all[i].SetLook(_picked == i ? ItemSlot.Look.Picked : i < Inventory.HotbarSize ? ItemSlot.Look.Hotbar : ItemSlot.Look.Normal);
        }

        var trash = _inventory.Trash;
        var trashed = trash is not null && _itemset.ItemsById.TryGetValue(trash.ItemId, out var trashedItem) ? trashedItem : null;
        _trash.Show(trashed, trash?.Quantity ?? 0, _icons);
        _trash.SetLook(ItemSlot.Look.Trash);
        _trash.TooltipText = trash is null ? "Trash" : $"Trash: {trashed?.Name ?? $"item {trash.ItemId}"} (click to take back)";
        _trashHint.Text = trash is null
            ? "Trash (drop item to discard)"
            : "Trash (click to restore)";
    }

    static GridContainer Grid()
    {
        var grid = new GridContainer { Columns = Inventory.HotbarSize };
        grid.AddThemeConstantOverride("h_separation", 2);
        grid.AddThemeConstantOverride("v_separation", 2);
        return grid;
    }
}
