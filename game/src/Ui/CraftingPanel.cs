using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Content;
using OpenCorneroids.Simulation.Items;

namespace OpenCorneroids.Game.Ui;

/// <summary>
/// The blueprints of one crafting station inside a classic bevelled window, in the itemset's own order: what each makes,
/// what it costs against what the player has, and buttons to craft once or half of what can be afforded.
/// </summary>
public partial class CraftingPanel : ClassicWindow
{
    static readonly Color Enough = new(0.65f, 1f, 0.65f);
    static readonly Color Short = new(1f, 0.48f, 0.42f);

    sealed record Row(BlueprintDef Blueprint, Label Name, List<(ItemSlot Slot, Label Count, int ItemId, int Need)> Ingredients, Button One, Button Five);

    Inventory _inventory = null!;
    ItemsetDef _itemset = null!;
    ItemIcons _icons = null!;
    Label _status = null!;
    VBoxContainer _list = null!;
    ScrollContainer _scroll = null!;
    readonly List<Row> _rows = new();

    public void Setup(Inventory inventory, ItemsetDef itemset, ItemIcons icons)
    {
        _inventory = inventory;
        _itemset = itemset;
        _icons = icons;
        Configure("Crafting", bevel: 5, padLeft: 18, padTop: 32, padRight: 18, padBottom: 16);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        AddChild(column);

        var field = ClassicField.Create(6);
        column.AddChild(field);

        _scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(540, 380),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        field.AddChild(_scroll);
        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 4);
        _scroll.AddChild(_list);
        column.AddChild(_status = ClassicUi.Label("", 14));

        inventory.Changed += Refresh;
    }

    public override void _ExitTree() => _inventory.Changed -= Refresh;

    /// <summary>Fills the list with the station's blueprints. <paramref name="title"/> is the station block's name.</summary>
    public void ShowStation(CraftingStation station, string title)
    {
        Caption = title;
        _status.Text = "";
        foreach (var child in _list.GetChildren()) child.QueueFree();
        _rows.Clear();
        var blueprints = Crafting.For(_itemset, station);
        if (blueprints.Count == 0) _list.AddChild(ClassicUi.Label("Nothing can be made here.", 15));
        foreach (var blueprint in blueprints) _list.AddChild(BuildRow(blueprint));
        _scroll.ScrollVertical = 0;
        Refresh();
    }

    Control BuildRow(BlueprintDef blueprint)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", RowStyle());
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 8);
        panel.AddChild(line);

        var result = ItemSlot.Create(46, MouseFilterEnum.Pass);
        var resultItem = Lookup(blueprint.Result.ItemId);
        result.Show(resultItem, blueprint.Result.Quantity, _icons, blueprint.Result.Quantity > 1 ? $"×{blueprint.Result.Quantity}" : "");
        line.AddChild(result);

        var middle = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        middle.AddThemeConstantOverride("separation", 2);
        var name = ClassicUi.Label(resultItem?.Name ?? blueprint.Name, 15);
        name.TooltipText = blueprint.Name;
        middle.AddChild(name);
        var needs = new HBoxContainer();
        needs.AddThemeConstantOverride("separation", 10);
        var ingredients = new List<(ItemSlot, Label, int, int)>();
        foreach (var group in blueprint.Ingredients.Where(i => i.Quantity > 0).GroupBy(i => i.ItemId))
        {
            var pair = new HBoxContainer();
            pair.AddThemeConstantOverride("separation", 4);
            var slot = ItemSlot.Create(34, MouseFilterEnum.Pass);
            var item = Lookup(group.Key);
            slot.Show(item, 0, _icons, "");
            if (item is null) slot.TooltipText = $"Unknown item {group.Key}";
            var count = ClassicUi.Label("", 14);
            count.VerticalAlignment = VerticalAlignment.Center;
            count.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            pair.AddChild(slot);
            pair.AddChild(count);
            needs.AddChild(pair);
            ingredients.Add((slot, count, group.Key, group.Sum(i => i.Quantity)));
        }
        middle.AddChild(needs);
        line.AddChild(middle);

        var one = CraftButton("Craft 1", blueprint, half: false);
        var five = CraftButton("Craft half", blueprint, half: true);
        line.AddChild(one);
        line.AddChild(five);
        _rows.Add(new Row(blueprint, name, ingredients, one, five));
        return panel;
    }

    Button CraftButton(string text, BlueprintDef blueprint, bool half)
    {
        var button = ClassicUi.Button(text, new Vector2(88, 30), 14);
        button.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        button.Pressed += () => Craft(blueprint, half
            ? Crafting.HalfBatch(Math.Min(Crafting.MaxCraftable(_inventory, blueprint, Crafting.MainInventory), 2 * Inventory.UnknownItemMaxStack))
            : 1);
        return button;
    }

    void Craft(BlueprintDef blueprint, int times)
    {
        var name = Lookup(blueprint.Result.ItemId)?.Name ?? blueprint.Name;
        _status.Text = Crafting.TryCraft(_inventory, _itemset, blueprint, times, Crafting.MainInventory)
            ? $"Crafted {blueprint.Result.Quantity * times} {name}"
            : "Inventory is full!";
        _status.AddThemeColorOverride("font_color", _status.Text.StartsWith("Crafted") ? Enough : Short);
    }

    void Refresh()
    {
        foreach (var row in _rows)
        {
            int max = Crafting.MaxCraftable(_inventory, row.Blueprint, Crafting.MainInventory);
            bool valid = Lookup(row.Blueprint.Result.ItemId) is not null && row.Ingredients.All(i => Lookup(i.ItemId) is not null);
            row.One.Disabled = !valid || max < 1;
            row.Five.Disabled = !valid || max < 2;
            row.Name.AddThemeColorOverride("font_color", valid && max >= 1 ? Colors.White : new Color(1, 1, 1, 0.6f));
            foreach (var (_, count, itemId, need) in row.Ingredients)
            {
                int have = (int)Math.Min(int.MaxValue, _inventory.CountFrom(itemId, Crafting.MainInventory));
                count.Text = $"{have}/{need}";
                count.AddThemeColorOverride("font_color", have >= need ? Enough : Short);
            }
        }
    }

    ItemDef? Lookup(int itemId) => _itemset.ItemsById.GetValueOrDefault(itemId);

    static StyleBoxFlat RowStyle()
    {
        var style = new StyleBoxFlat { BgColor = Color.Color8(182, 182, 182), BorderColor = ClassicUi.Groove };
        style.SetBorderWidthAll(1);
        style.SetContentMarginAll(5);
        return style;
    }
}
