using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Content;
using OpenCorneroids.Simulation.Items;

namespace OpenCorneroids.Game.Ui;

/// <summary>
/// The inventory grid: the backpack rows on top and the hotbar row (slots 0..8) below them, set apart and tinted
/// so it is clear which items are in hand. Click a stack to pick it up, click another slot to put it there; the
/// move itself is <see cref="Inventory.MoveOrMerge"/>, so the same item merges and anything else swaps. Putting a
/// stack on the trash slot throws it away (<see cref="Inventory.Discard"/>); clicking the trash with nothing picked
/// takes the last thrown stack back.
/// </summary>
public partial class InventoryPanel : PanelContainer
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

    public void Setup(Inventory inventory, ItemsetDef itemset, ItemIcons icons)
    {
        _inventory = inventory;
        _itemset = itemset;
        _icons = icons;
        AddThemeStyleboxOverride("panel", GameWindows.PanelStyle());

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        AddChild(column);
        column.AddChild(Hud.Text("Inventory", 20));

        _all = new ItemSlot[inventory.Slots.Count];
        var backpack = Grid();
        for (int i = Inventory.HotbarSize; i < _all.Length; i++) backpack.AddChild(_all[i] = NewSlot(i));
        column.AddChild(backpack);

        column.AddChild(new HSeparator());
        column.AddChild(Hud.Text($"Hotbar (1-{Inventory.HotbarSize})", 14, new Color(0.6f, 0.75f, 1f)));
        var hotbar = Grid();
        for (int i = 0; i < Math.Min(Inventory.HotbarSize, _all.Length); i++) hotbar.AddChild(_all[i] = NewSlot(i));
        column.AddChild(hotbar);

        var trashRow = new HBoxContainer();
        trashRow.AddThemeConstantOverride("separation", 8);
        _trash = ItemSlot.Create(SlotSize, MouseFilterEnum.Stop);
        _trash.Clicked += OnTrashClicked;
        trashRow.AddChild(_trash);
        _trashHint = Hud.Text("", 13, new Color(1f, 0.75f, 0.7f, 0.8f));
        _trashHint.VerticalAlignment = VerticalAlignment.Center;
        _trashHint.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        trashRow.AddChild(_trashHint);
        column.AddChild(trashRow);

        column.AddChild(Hud.Text("Click an item, then click where it goes. Right click cancels.", 13, new Color(1, 1, 1, 0.55f)));
        column.AddChild(Hud.Text("Esc or I/Tab closes.", 13, new Color(1, 1, 1, 0.55f)));

        inventory.Changed += Refresh;
        Refresh();
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
            Refresh();   // MoveOrMerge raises no event when nothing changed, but the pick highlight must still go
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
        // A craft can empty the picked slot or refill it with something else. Keeping the pick then would make the
        // next click move a stack the player never picked (MoveOrMerge swaps from an empty source), so drop it.
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
            ? "Trash: put an item here to throw it away."
            : "Trash: click to take it back. The next item thrown away destroys it.";
    }

    static GridContainer Grid()
    {
        var grid = new GridContainer { Columns = Inventory.HotbarSize };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        return grid;
    }
}
