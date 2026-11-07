using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Content;
using OpenCorneroids.Simulation.Items;

namespace OpenCorneroids.Game.Ui;

/// <summary>
/// The character equipment window beside the inventory: the seven equipment slots laid out roughly like a body, and the
/// suit's Health, Armor and Power recharge.
/// </summary>
public partial class CharacterPanel : ClassicWindow
{
    const float SlotSize = 48;

    static readonly WearableSlot?[] Layout =
    [
        null, WearableSlot.Helmet, null,
        WearableSlot.Arms, WearableSlot.Chest, WearableSlot.Shoulders,
        null, WearableSlot.Legs, WearableSlot.PowerSource,
        null, WearableSlot.Feet, null,
    ];

    static readonly Dictionary<WearableSlot, (string Short, string Name)> Names = new()
    {
        [WearableSlot.Helmet] = ("Head", "Helmet"),
        [WearableSlot.Shoulders] = ("Shld", "Shoulders"),
        [WearableSlot.Chest] = ("Chest", "Chest"),
        [WearableSlot.Legs] = ("Legs", "Legs"),
        [WearableSlot.Arms] = ("Arms", "Arms"),
        [WearableSlot.Feet] = ("Feet", "Feet"),
        [WearableSlot.PowerSource] = ("Pwr", "Power source"),
    };

    Suit _suit = null!;
    Inventory _inventory = null!;
    InventoryPanel _inventoryPanel = null!;
    ItemsetDef _itemset = null!;
    ItemIcons _icons = null!;
    readonly Dictionary<WearableSlot, ItemSlot> _slots = new();
    Label _stats = null!, _status = null!;

    public void Setup(Suit suit, Inventory inventory, InventoryPanel inventoryPanel, ItemsetDef itemset, ItemIcons icons)
    {
        (_suit, _inventory, _inventoryPanel, _itemset, _icons) = (suit, inventory, inventoryPanel, itemset, icons);
        Configure("Character", bevel: 5, padLeft: 18, padTop: 32, padRight: 18, padBottom: 16);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        AddChild(column);

        var grid = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        foreach (var cell in Layout)
        {
            if (cell is not { } slot)
            {
                grid.AddChild(new Control { CustomMinimumSize = new Vector2(SlotSize, SlotSize), MouseFilter = MouseFilterEnum.Ignore });
                continue;
            }
            var view = ItemSlot.Create(SlotSize, MouseFilterEnum.Stop);
            view.Placeholder = Names[slot].Short;
            view.Clicked += _ => OnClicked(slot);
            grid.AddChild(view);
            _slots[slot] = view;
        }
        column.AddChild(grid);
        column.AddChild(_stats = ClassicUi.Label("", 14));
        column.AddChild(_status = ClassicUi.Label("", 13, new Color(1f, 0.85f, 0.8f)));
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.CustomMinimumSize = new Vector2(3 * SlotSize + 16, 0);

        suit.Changed += Refresh;
        Refresh();
    }

    public override void _ExitTree() => _suit.Changed -= Refresh;

    void OnClicked(WearableSlot slot)
    {
        _status.Text = "";
        if (_inventoryPanel.PickedSlot is { } picked)
        {
            var stack = _inventory.Slots[picked];
            var wearable = stack is null ? null : _suit.WearableOf(stack.ItemId);
            _inventoryPanel.CancelPick();
            if (wearable is null) _status.Text = "That can't be worn.";
            else if (wearable.Slot != slot) _status.Text = $"That is worn as {Names[wearable.Slot].Name.ToLowerInvariant()}.";
            else if (!_suit.TryEquip(_inventory, picked)) _status.Text = "No room for what you wear there now.";
            return;
        }
        if (_suit.Equipment[(int)slot] is not null && !_suit.TryUnequip(slot, _inventory))
            _status.Text = "The inventory is full.";
    }

    void Refresh()
    {
        foreach (var (slot, view) in _slots)
        {
            var worn = _suit.Equipment[(int)slot];
            var item = worn is null ? null : _itemset.ItemsById.GetValueOrDefault(worn.ItemId);
            view.Show(item, 0, _icons);
            view.TooltipText = item is null ? Names[slot].Name : $"{item.Name} (click to take off)";
            view.SetLook(ItemSlot.Look.Normal);
        }
        var buffs = _suit.Buffs.Select(b => $"{_itemset.ItemsById.GetValueOrDefault(b.ItemId)?.Name ?? $"item {b.ItemId}"} {Math.Ceiling(b.SecondsLeft):0} s");
        _stats.Text = $"Health: {_suit.MaxHealth}\nArmor: {_suit.Armor}\nPower recharge: {_suit.PowerRecharge}" +
                      string.Concat(buffs.Select(b => "\n" + b));
    }
}
