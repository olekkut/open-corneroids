using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Content;
using OpenCorneroids.Simulation.Items;

namespace OpenCorneroids.Game.Ui;

/// <summary>
/// The blueprints of one crafting station, in the itemset's own order: what each makes, what it costs against what
/// the player has, and buttons to craft once or five times. The rules (counting, atomic crafting, room for the
/// result) are <see cref="Crafting"/>'s; this panel only shows them and redraws whenever the inventory changes.
/// </summary>
public partial class CraftingPanel : PanelContainer
{
    static readonly Color Enough = new(0.75f, 1f, 0.75f);
    static readonly Color Short = new(1f, 0.45f, 0.4f);

    sealed record Row(BlueprintDef Blueprint, Label Name, List<(ItemSlot Slot, Label Count, int ItemId, int Need)> Ingredients, Button One, Button Five);

    Inventory _inventory = null!;
    ItemsetDef _itemset = null!;
    ItemIcons _icons = null!;
    Label _title = null!, _status = null!;
    VBoxContainer _list = null!;
    ScrollContainer _scroll = null!;
    readonly List<Row> _rows = new();

    public void Setup(Inventory inventory, ItemsetDef itemset, ItemIcons icons)
    {
        _inventory = inventory;
        _itemset = itemset;
        _icons = icons;
        AddThemeStyleboxOverride("panel", GameWindows.PanelStyle());

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        AddChild(column);
        column.AddChild(_title = Hud.Text("", 20));
        _scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(560, 400),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        column.AddChild(_scroll);
        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 4);
        _scroll.AddChild(_list);
        column.AddChild(_status = Hud.Text("", 14));

        inventory.Changed += Refresh;
    }

    public override void _ExitTree() => _inventory.Changed -= Refresh;

    /// <summary>Fills the list with the station's blueprints. <paramref name="title"/> is the station block's name.</summary>
    public void ShowStation(CraftingStation station, string title)
    {
        _title.Text = title;
        _status.Text = "";
        foreach (var child in _list.GetChildren()) child.QueueFree();
        _rows.Clear();
        var blueprints = Crafting.For(_itemset, station);
        if (blueprints.Count == 0) _list.AddChild(Hud.Text("Nothing can be made here.", 15, new Color(1, 1, 1, 0.6f)));
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
        var name = Hud.Text(resultItem?.Name ?? blueprint.Name, 15);
        name.TooltipText = blueprint.Name;
        middle.AddChild(name);
        var needs = new HBoxContainer();
        needs.AddThemeConstantOverride("separation", 10);
        var ingredients = new List<(ItemSlot, Label, int, int)>();
        // Repeated ids are shown once with their total, matching how Crafting counts them.
        foreach (var group in blueprint.Ingredients.Where(i => i.Quantity > 0).GroupBy(i => i.ItemId))
        {
            // have/need sits beside the icon rather than in its corner: "120/15" would cover a small icon.
            var pair = new HBoxContainer();
            pair.AddThemeConstantOverride("separation", 3);
            var slot = ItemSlot.Create(34, MouseFilterEnum.Pass);
            var item = Lookup(group.Key);
            slot.Show(item, 0, _icons);
            if (item is null) slot.TooltipText = $"Unknown item {group.Key}";
            var count = Hud.Text("", 14);
            count.VerticalAlignment = VerticalAlignment.Center;
            count.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            pair.AddChild(slot);
            pair.AddChild(count);
            needs.AddChild(pair);
            ingredients.Add((slot, count, group.Key, group.Sum(i => i.Quantity)));
        }
        middle.AddChild(needs);
        line.AddChild(middle);

        // The original crafts one batch with a left click and half of what can be afforded with a right click
        //; here they are two buttons.
        var one = CraftButton("Craft 1", blueprint, half: false);
        var five = CraftButton("Craft half", blueprint, half: true);
        line.AddChild(one);
        line.AddChild(five);
        _rows.Add(new Row(blueprint, name, ingredients, one, five));
        return panel;
    }

    Button CraftButton(string text, BlueprintDef blueprint, bool half)
    {
        var button = new Button { Text = text, FocusMode = FocusModeEnum.None, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        // The default theme's button is almost the panel's colour; a filled green box makes it obvious which
        // recipes can be crafted right now, and a flat outline marks the ones that can't.
        button.AddThemeStyleboxOverride("normal", ButtonStyle(new Color(0.16f, 0.42f, 0.22f), new Color(0.5f, 0.9f, 0.55f)));
        button.AddThemeStyleboxOverride("hover", ButtonStyle(new Color(0.22f, 0.55f, 0.3f), new Color(0.7f, 1f, 0.7f)));
        button.AddThemeStyleboxOverride("pressed", ButtonStyle(new Color(0.1f, 0.3f, 0.15f), new Color(0.7f, 1f, 0.7f)));
        button.AddThemeStyleboxOverride("disabled", ButtonStyle(new Color(1, 1, 1, 0.03f), new Color(1, 1, 1, 0.15f)));
        button.AddThemeColorOverride("font_color", Colors.White);
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_pressed_color", Colors.White);
        button.AddThemeColorOverride("font_disabled_color", new Color(1, 1, 1, 0.3f));
        // A recipe with no ingredients can be afforded without end: half of that would never fit, so it is taken as a
        // stack's worth.
        button.Pressed += () => Craft(blueprint, half
            ? Crafting.HalfBatch(Math.Min(Crafting.MaxCraftable(_inventory, blueprint, Crafting.MainInventory), 2 * Inventory.UnknownItemMaxStack))
            : 1);
        return button;
    }

    static StyleBoxFlat ButtonStyle(Color background, Color border)
    {
        var style = new StyleBoxFlat { BgColor = background, BorderColor = border };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(3);
        style.ContentMarginLeft = style.ContentMarginRight = 10;
        style.ContentMarginTop = style.ContentMarginBottom = 4;
        return style;
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
            // Only the main inventory counts: what is on the hotbar can't be used.
            int max = Crafting.MaxCraftable(_inventory, row.Blueprint, Crafting.MainInventory);
            // An unknown id in the blueprint would make TryCraft refuse, so don't offer the button at all.
            bool valid = Lookup(row.Blueprint.Result.ItemId) is not null && row.Ingredients.All(i => Lookup(i.ItemId) is not null);
            row.One.Disabled = !valid || max < 1;
            row.Five.Disabled = !valid || max < 2;
            row.Name.AddThemeColorOverride("font_color", valid && max >= 1 ? Colors.White : new Color(1, 1, 1, 0.55f));
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
        var style = new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.04f) };
        style.SetContentMarginAll(4);
        style.SetCornerRadiusAll(3);
        return style;
    }
}
