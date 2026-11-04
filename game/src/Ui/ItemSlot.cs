using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Content;

namespace OpenCorneroids.Game.Ui;

/// <summary>
/// One square showing an item: its icon (or a text badge when it has none), a count in the corner and the name as a
/// tooltip. Used by the hotbar, the inventory grid and the crafting window, so an item looks the same everywhere.
/// </summary>
public partial class ItemSlot : PanelContainer
{
    public enum Look { Normal, Hotbar, Selected, Picked, Trash }

    // Per instance rather than static: Godot resources held in static fields outlive the engine and are reported
    // as leaks at exit.
    readonly StyleBoxFlat _normalStyle = Style(new Color(0, 0, 0, 0.55f), new Color(1, 1, 1, 0.25f), 1);
    readonly StyleBoxFlat _hotbarStyle = Style(new Color(0.05f, 0.08f, 0.14f, 0.7f), new Color(0.45f, 0.65f, 1f, 0.6f), 1);
    readonly StyleBoxFlat _selectedStyle = Style(new Color(0.1f, 0.1f, 0.1f, 0.75f), new Color(1f, 0.85f, 0.3f), 3);
    readonly StyleBoxFlat _pickedStyle = Style(new Color(0.1f, 0.25f, 0.3f, 0.85f), new Color(0.4f, 1f, 1f), 3);
    readonly StyleBoxFlat _trashStyle = Style(new Color(0.25f, 0.05f, 0.05f, 0.7f), new Color(1f, 0.4f, 0.35f, 0.7f), 1);

    TextureRect _icon = null!;
    Label _badge = null!, _count = null!;

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
        slot._badge = Hud.Text("", (int)(size * 0.32f), new Color(1, 1, 1, 0.85f));
        slot._badge.HorizontalAlignment = HorizontalAlignment.Center;
        slot._badge.VerticalAlignment = VerticalAlignment.Center;
        slot._count = Hud.Text("", Math.Max(11, (int)(size * 0.26f)));
        slot._count.HorizontalAlignment = HorizontalAlignment.Right;
        slot._count.VerticalAlignment = VerticalAlignment.Bottom;
        slot.AddChild(slot._icon);
        slot.AddChild(slot._badge);
        slot.AddChild(slot._count);
        slot.SetLook(Look.Normal);
        return slot;
    }

    /// <summary>Shows an item, or clears the slot when <paramref name="item"/> is null. A count of 1 or less is not
    /// drawn, the way a single tool needs no number.</summary>
    public void Show(ItemDef? item, int count, ItemIcons icons, string? countText = null)
    {
        var icon = item is null ? null : icons.Of(item);
        _icon.Texture = icon;
        _badge.Text = item is not null && icon is null ? ItemIcons.Badge(item) : "";
        _count.Text = countText ?? (item is not null && count > 1 ? count.ToString() : "");
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

    static StyleBoxFlat Style(Color background, Color border, int borderWidth)
    {
        var style = new StyleBoxFlat { BgColor = background, BorderColor = border };
        style.SetBorderWidthAll(borderWidth);
        style.SetContentMarginAll(5);
        return style;
    }
}
