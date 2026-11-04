using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Content;
using OpenCorneroids.Simulation.Items;

namespace OpenCorneroids.Game.Ui;

/// <summary>
/// The survival windows: the inventory on its own, or a crafting station's blueprints with the inventory beside it
/// so ingredients can be checked and shuffled without closing anything. While a window is open the mouse is free,
/// and a dimmed backdrop swallows clicks that miss the panels so they never reach the camera as mining or placing.
/// Opening and closing are driven by the camera, which owns the keys; this class only shows and hides.
/// </summary>
public partial class GameWindows : CanvasLayer
{
    Control _root = null!;
    InventoryPanel _inventory = null!;
    CraftingPanel _crafting = null!;
    CharacterPanel _character = null!;

    public bool IsOpen => _root.Visible;

    /// <summary>The station whose blueprints are on screen, or null when only the inventory is open.</summary>
    public CraftingStation? OpenStation { get; private set; }

    public void Setup(Inventory inventory, Suit suit, ItemsetDef itemset, ItemIcons icons)
    {
        Layer = 2;   // above the HUD
        _root = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        var backdrop = new ColorRect { Color = new Color(0, 0, 0, 0.45f), MouseFilter = Control.MouseFilterEnum.Ignore };
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(backdrop);

        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(center);

        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 12);
        center.AddChild(row);

        _crafting = new CraftingPanel { Name = "Crafting", Visible = false, SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        row.AddChild(_crafting);
        _crafting.Setup(inventory, itemset, icons);
        // The character window shares the row with the inventory, not with a station: three panels would not fit
        // the 1280-wide layout.
        _character = new CharacterPanel { Name = "Character", SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        row.AddChild(_character);
        _inventory = new InventoryPanel { Name = "Inventory", SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        row.AddChild(_inventory);
        _inventory.Setup(inventory, itemset, icons);
        _character.Setup(suit, inventory, _inventory, itemset, icons);
    }

    public void OpenInventory()
    {
        OpenStation = null;
        _crafting.Visible = false;
        _character.Visible = true;
        Reveal();
    }

    /// <param name="title">Shown at the top of the crafting panel, normally the station block's name.</param>
    public void OpenCrafting(CraftingStation station, string title)
    {
        OpenStation = station;
        _crafting.ShowStation(station, title);
        _crafting.Visible = true;
        _character.Visible = false;
        Reveal();
    }

    public void Close()
    {
        if (!IsOpen) return;
        _inventory.CancelPick();
        _root.Visible = false;
        OpenStation = null;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    void Reveal()
    {
        _root.Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    /// <summary>Shared look of every window panel. A new box per call: Godot resources in static fields are
    /// reported as leaks at exit.</summary>
    public static StyleBoxFlat PanelStyle()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.07f, 0.1f, 0.94f),
            BorderColor = new Color(1, 1, 1, 0.18f),
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(4);
        style.SetContentMarginAll(12);
        return style;
    }
}
