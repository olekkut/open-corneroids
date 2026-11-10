using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Content;
using OpenCorneroids.Simulation.Items;

namespace OpenCorneroids.Game.Ui;

/// <summary>
/// The window of an open chest, beside the player's inventory as in the original: titled <c>"Container"</c> whatever
/// the block is called, with the chest's slots in its own grid (5 × 4 for the default container block), row by row.
/// Clicks move stacks through the cursor shared with the inventory (<see cref="ItemHand"/>), so the original's click
/// rules hold between hotbar, inventory and chest.
/// </summary>
public partial class ContainerPanel : ClassicWindow
{
    const float SlotSize = 48;

    ItemHand _hand = null!;
    ItemsetDef _itemset = null!;
    ItemIcons _icons = null!;
    GridContainer _grid = null!;
    ItemSlot[] _slots = [];

    /// <summary>The chest on show, or null while the window is closed.</summary>
    public Inventory? Container { get; private set; }

    /// <param name="hand">The cursor stack shared with the other item windows.</param>
    public void Setup(ItemHand hand, ItemsetDef itemset, ItemIcons icons)
    {
        (_hand, _itemset, _icons) = (hand, itemset, icons);
        Configure("Container", bevel: 5, padLeft: 18, padTop: 32, padRight: 18, padBottom: 16);
        _grid = new GridContainer();
        _grid.AddThemeConstantOverride("h_separation", 2);
        _grid.AddThemeConstantOverride("v_separation", 2);
        AddChild(_grid);
    }

    /// <summary>Shows <paramref name="container"/>'s slots, <paramref name="width"/> to a row.</summary>
    public void ShowContainer(Inventory container, int width)
    {
        Detach();
        Container = container;
        foreach (var child in _grid.GetChildren())
        {
            _grid.RemoveChild(child);
            child.QueueFree();
        }
        _grid.Columns = Math.Max(1, width);
        _slots = new ItemSlot[container.Slots.Count];
        for (int i = 0; i < _slots.Length; i++)
        {
            int index = i;
            var slot = ItemSlot.Create(SlotSize, MouseFilterEnum.Stop);
            slot.Clicked += button => OnClicked(index, button);
            _grid.AddChild(_slots[i] = slot);
        }
        container.Changed += Refresh;
        Refresh();
        // The window shrinks to the new grid.
        ResetSize();
    }

    /// <summary>Lets go of the chest on show (the window closed).</summary>
    public void Detach()
    {
        if (Container is { } container) container.Changed -= Refresh;
        Container = null;
    }

    public override void _ExitTree() => Detach();

    void OnClicked(int index, MouseButton button)
    {
        if (Container is not { } container || index >= container.Slots.Count) return;
        _hand.Click(container, index, button == MouseButton.Right ? HandButton.Right : HandButton.Left);
    }

    void Refresh()
    {
        if (Container is not { } container) return;
        for (int i = 0; i < _slots.Length && i < container.Slots.Count; i++)
        {
            var stack = container.Slots[i];
            var item = stack is not null && _itemset.ItemsById.TryGetValue(stack.ItemId, out var def) ? def : null;
            _slots[i].Show(item, stack?.Quantity ?? 0, _icons);
            if (stack is not null && item is null) _slots[i].TooltipText = $"Unknown item {stack.ItemId}";
            _slots[i].SetLook(ItemSlot.Look.Normal);
        }
    }
}
