using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Content;
using OpenCorneroids.Simulation.Items;

namespace OpenCorneroids.Game.Ui;

/// <summary>
/// The character equipment window beside the inventory: the seven equipment slots laid out roughly like a body, and the
/// suit's Health, Armor and Power recharge. Clicks go through the cursor stack shared with the inventory
/// (<see cref="ItemHand.ClickWorn"/>): an empty hand takes off what is worn, a held piece goes on.
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
    ItemHand _hand = null!;
    ItemsetDef _itemset = null!;
    ItemIcons _icons = null!;
    readonly Dictionary<WearableSlot, ItemSlot> _slots = new();
    Label _stats = null!, _status = null!;

    public void Setup(Suit suit, Inventory inventory, ItemHand hand, ItemsetDef itemset, ItemIcons icons)
    {
        (_suit, _inventory, _hand, _itemset, _icons) = (suit, inventory, hand, itemset, icons);
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
        var held = _hand.Held;
        _status.Text = _hand.ClickWorn(_suit, slot, _inventory) switch
        {
            WornClick.NotWearable => "That can't be worn.",
            WornClick.WrongSlot when held is not null && _suit.WearableOf(held.ItemId) is { } wearable =>
                $"That is worn as {Names[wearable.Slot].Name.ToLowerInvariant()}.",
            WornClick.NoRoom => "No room for what you wear there now.",
            _ => "",
        };
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
