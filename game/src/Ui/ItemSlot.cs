using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Content;

namespace OpenCorneroids.Game.Ui;

/// <summary>
/// One square showing an item: its icon (or a text badge when it has none), a count in the top-left corner as in the
/// original, and the name as a tooltip. Used by the hotbar, the inventory grid and the crafting window.
/// </summary>
public partial class ItemSlot : PanelContainer
{
    public enum Look { Normal, Hotbar, Selected, Picked, Trash }

    // Per instance rather than static: Godot resources held in static fields outlive the engine and are reported
    // as leaks at exit.
    readonly StyleBox _normalStyle = ClassicUi.BevelStyle(ClassicUi.SlotFace, ClassicUi.BevelDark, ClassicUi.BevelLight, bevel: 2, marginH: 4, marginV: 4);
    readonly StyleBox _hotbarStyle = ClassicUi.BevelStyle(Color.Color8(186, 186, 186), ClassicUi.BevelDark, ClassicUi.BevelLight, bevel: 2, marginH: 4, marginV: 4);
    readonly StyleBox _selectedStyle = FrameStyle(Color.Color8(196, 196, 196), Colors.White, 3);
    readonly StyleBox _pickedStyle = FrameStyle(Color.Color8(170, 198, 204), new Color(0.25f, 0.95f, 1f), 3);
    readonly StyleBox _trashStyle = ClassicUi.BevelStyle(Color.Color8(168, 136, 136), ClassicUi.BevelDark, ClassicUi.BevelLight, bevel: 2, marginH: 4, marginV: 4);

    TextureRect _icon = null!;
    Label _badge = null!, _count = null!;

    /// <summary>Dim text shown while the slot is empty, e.g. which equipment goes there.</summary>
    public string Placeholder { get; set; } = "";

    /// <summary>Raised with the mouse button when the slot is clicked; slots that are only for display ignore clicks.</summary>
    public event Action<MouseButton>? Clicked;

    /// <param name="mouse">Ignore for the HUD (a click there must reach the camera to recapture the mouse), Pass for
    /// display slots in windows (tooltip only), Stop for slots that take clicks.</param>
    public static ItemSlot Create(float size, MouseFilterEnum mouse = MouseFilterEnum.Ignore)
    {
        var slot = new ItemSlot
        {
            CustomMinimumSize = new Vector2(size, size),
            MouseFilter = mouse,
            FocusMode = FocusModeEnum.None,
        };
        slot._icon = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = TextureFilterEnum.Nearest,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        slot._badge = ClassicUi.Label("", (int)(size * 0.28f), new Color(1, 1, 1, 0.9f));
        slot._badge.HorizontalAlignment = HorizontalAlignment.Center;
        slot._badge.VerticalAlignment = VerticalAlignment.Center;
        slot._count = ClassicUi.Label("", Math.Max(12, (int)(size * 0.25f)));
        slot._count.HorizontalAlignment = HorizontalAlignment.Left;
        slot._count.VerticalAlignment = VerticalAlignment.Top;
        slot._count.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        slot._count.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        slot.AddChild(slot._icon);
        slot.AddChild(slot._badge);
        slot.AddChild(slot._count);
        slot.SetLook(Look.Normal);
        return slot;
    }

    /// <summary>Shows an item, or clears the slot when <paramref name="item"/> is null. Stack count sits in the
    /// top-left corner, as in the original.</summary>
    public void Show(ItemDef? item, int count, ItemIcons icons, string? countText = null)
    {
        var icon = item is null ? null : icons.Of(item);
        _icon.Texture = icon;
        _badge.Text = item is null ? Placeholder : icon is null ? ItemIcons.Badge(item) : "";
        _badge.Modulate = new Color(1, 1, 1, item is null ? 0.55f : 1f);
        _count.Text = countText ?? (item is not null && count >= 1 ? count.ToString() : "");
        TooltipText = item?.Name ?? "";
    }

    public void SetLook(Look look) => AddThemeStyleboxOverride("panel", look switch
    {
        Look.Hotbar => _hotbarStyle,
        Look.Selected => _selectedStyle,
        Look.Picked => _pickedStyle,
        Look.Trash => _trashStyle,
        _ => _normalStyle,
    });

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true } button && button.ButtonIndex is MouseButton.Left or MouseButton.Right)
        {
            Clicked?.Invoke(button.ButtonIndex);
            AcceptEvent();
        }
    }

    static StyleBoxFlat FrameStyle(Color background, Color border, int borderWidth)
    {
        var style = new StyleBoxFlat { BgColor = background, BorderColor = border };
        style.SetBorderWidthAll(borderWidth);
        style.SetContentMarginAll(4);
        return style;
    }
}
