using Godot;
using OpenCorneroids.Game.Content;
using OpenCorneroids.Game.World;

namespace OpenCorneroids.Game.Ui;

/// <summary>Crosshair, a nine-slot hotbar paging through every placeable block, and status text.</summary>
public partial class Hud : CanvasLayer
{
    const int Slots = 9;
    const float SlotSize = 52;

    BlockPalette _palette = null!;
    BlockTextures _textures = null!;
    ushort[] _blocks = [];
    int _selected;
    readonly List<(PanelContainer Panel, TextureRect Icon)> _slots = new();
    readonly StyleBoxFlat _slotStyle = Style(new Color(0, 0, 0, 0.55f), new Color(1, 1, 1, 0.25f), 1);
    readonly StyleBoxFlat _selectedStyle = Style(new Color(0.1f, 0.1f, 0.1f, 0.75f), new Color(1f, 0.85f, 0.3f), 3);
    Label _status = null!, _selectedName = null!, _message = null!;
    double _messageTime;

    public ushort Selected => _blocks.Length == 0 ? BlockPalette.Air : _blocks[_selected];

    public void Setup(BlockPalette palette, BlockTextures textures, string title)
    {
        _palette = palette;
        _textures = textures;
        _blocks = palette.Indices.ToArray();

        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        var top = new VBoxContainer { Position = new Vector2(12, 8), MouseFilter = Control.MouseFilterEnum.Ignore };
        top.AddChild(Text(title, 18));
        top.AddChild(_status = Text("", 14));
        top.AddChild(Text("WASD move · Space/E up · Ctrl/Q down · Shift boost · LMB mine · RMB place · wheel/1-9 select · Esc mouse, Esc again menu", 13,
            new Color(1, 1, 1, 0.6f)));
        root.AddChild(top);

        var crosshair = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        crosshair.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        crosshair.AddChild(Text("+", 26));
        root.AddChild(crosshair);

        var bottom = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.End, MouseFilter = Control.MouseFilterEnum.Ignore };
        bottom.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        bottom.AddChild(_message = Text("", 16, new Color(0.7f, 1f, 0.7f)));
        bottom.AddChild(_selectedName = Text("", 18));
        var bar = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        for (int i = 0; i < Slots; i++)
        {
            var panel = new PanelContainer { CustomMinimumSize = new Vector2(SlotSize, SlotSize) };
            var icon = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                CustomMinimumSize = new Vector2(SlotSize - 12, SlotSize - 12),
            };
            panel.AddChild(icon);
            bar.AddChild(panel);
            _slots.Add((panel, icon));
        }
        bottom.AddChild(bar);
        bottom.AddChild(new Control { CustomMinimumSize = new Vector2(0, 14) });
        foreach (var label in new[] { _message, _selectedName }) label.HorizontalAlignment = HorizontalAlignment.Center;
        root.AddChild(bottom);

        Refresh();
    }

    public void SelectSlot(int slot)
    {
        int page = _selected / Slots * Slots;
        if (page + slot < _blocks.Length) _selected = page + slot;
        Refresh();
    }

    public void Cycle(int delta)
    {
        if (_blocks.Length == 0) return;
        _selected = ((_selected + delta) % _blocks.Length + _blocks.Length) % _blocks.Length;
        Refresh();
    }

    public void SetStatus(string text) => _status.Text = text;

    public void Flash(string text)
    {
        _message.Text = text;
        _messageTime = 2.0;
    }

    public override void _Process(double delta)
    {
        if (_messageTime > 0 && (_messageTime -= delta) <= 0) _message.Text = "";
    }

    void Refresh()
    {
        int page = _selected / Slots * Slots;
        for (int i = 0; i < Slots; i++)
        {
            var (panel, icon) = _slots[i];
            int index = page + i;
            icon.Texture = index < _blocks.Length ? _textures.Icon(_palette.IconCell(_blocks[index])) : null;
            panel.AddThemeStyleboxOverride("panel", index == _selected ? _selectedStyle : _slotStyle);
        }
        _selectedName.Text = _blocks.Length == 0 ? "" :
            $"{_palette.Item(Selected).Name}   ({_selected + 1}/{_blocks.Length}, page {page / Slots + 1})";
    }

    static Label Text(string text, int size, Color? color = null)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color ?? Colors.White);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 4);
        return label;
    }

    static StyleBoxFlat Style(Color background, Color border, int borderWidth)
    {
        var style = new StyleBoxFlat { BgColor = background, BorderColor = border };
        style.SetBorderWidthAll(borderWidth);
        style.SetContentMarginAll(6);
        return style;
    }
}
