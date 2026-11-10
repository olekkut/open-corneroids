using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Content;
using OpenCorneroids.Simulation.Items;

namespace OpenCorneroids.Game.Ui;

/// <summary>
/// Draws the stack held on the cursor (<see cref="ItemHand"/>) under the mouse, as in the original: the item's icon
/// with its count in the top-left corner. It never takes clicks.
/// </summary>
public partial class HandCursor : Control
{
    const float IconSize = 40;

    ItemHand _hand = null!;
    ItemsetDef _itemset = null!;
    ItemIcons _icons = null!;

    public void Setup(ItemHand hand, ItemsetDef itemset, ItemIcons icons)
    {
        (_hand, _itemset, _icons) = (hand, itemset, icons);
        MouseFilter = MouseFilterEnum.Ignore;
        TopLevel = true;
        ZIndex = 100;
        TextureFilter = TextureFilterEnum.Nearest;
        hand.Changed += QueueRedraw;
    }

    public override void _ExitTree()
    {
        if (_hand is not null) _hand.Changed -= QueueRedraw;
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree() || _hand is null || _hand.IsEmpty) return;
        // Centred on the mouse, so the stack sits where it will be put.
        Position = GetViewport().GetMousePosition() - new Vector2(IconSize, IconSize) / 2;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_hand?.Held is not { } held) return;
        var rect = new Rect2(Vector2.Zero, new Vector2(IconSize, IconSize));
        var item = _itemset.ItemsById.GetValueOrDefault(held.ItemId);
        var font = ThemeDB.FallbackFont;
        if (item is not null && _icons.Of(item) is { } icon)
        {
            DrawTextureRect(icon, rect, false);
        }
        else
        {
            DrawRect(rect, new Color(0.62f, 0.62f, 0.66f, 0.9f));
            var badge = item is null ? "?" : ItemIcons.Badge(item);
            DrawString(font, new Vector2(2, IconSize * 0.62f), badge, HorizontalAlignment.Center, IconSize - 4, 12, Colors.White);
        }
        var count = held.Quantity.ToString();
        DrawString(font, new Vector2(3, 15), count, HorizontalAlignment.Left, -1, 13, new Color(0, 0, 0, 0.7f));
        DrawString(font, new Vector2(2, 14), count, HorizontalAlignment.Left, -1, 13, Colors.White);
    }
}
