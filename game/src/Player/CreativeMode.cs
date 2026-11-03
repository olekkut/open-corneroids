using Godot;
using OpenCorneroids.Game.Ui;
using OpenCorneroids.Game.World;

namespace OpenCorneroids.Game.Player;

/// <summary>
/// The building sandbox: blocks break on the first click, and the hotbar pages through every placeable block with
/// no cost. Kept for building tests and the block showcase (<c>--creative</c>, <c>--showcase</c>).
/// </summary>
public sealed class CreativeMode : PlayMode
{
    public override string Controls =>
        "WASD move · Space/E up · Ctrl/Q down · Shift boost · N noclip · LMB mine · RMB place · wheel/1-9 select · Esc mouse, Esc again menu";

    readonly ushort[] _blocks;
    int _selected;

    public CreativeMode(VoxelWorld world, Hud hud) : base(world, hud)
    {
        _blocks = Palette.Indices.ToArray();
        ShowHotbar();
    }

    public override bool HasInventory => false;

    ushort Selected => _blocks.Length == 0 ? BlockPalette.Air : _blocks[_selected];

    /// <summary>Number keys pick within the current page of nine.</summary>
    public override void Select(int slot)
    {
        int page = _selected / Hud.HotbarSlots * Hud.HotbarSlots;
        if (page + slot < _blocks.Length) _selected = page + slot;
        ShowHotbar();
    }

    /// <summary>The wheel runs through the whole palette, turning pages as it goes.</summary>
    public override void Cycle(int delta)
    {
        if (_blocks.Length == 0) return;
        _selected = ((_selected + delta) % _blocks.Length + _blocks.Length) % _blocks.Length;
        ShowHotbar();
    }

    public override void PrimaryPressed(Vector3I? target)
    {
        if (target is not { } cell) return;
        var name = Palette.Item(World.Get(cell)).Name;
        World.Set(cell, BlockPalette.Air);
        Hud.Flash($"Mined {name}");
    }

    public override void Place(Vector3 eye, Vector3I cell)
    {
        if (Selected == BlockPalette.Air || !CanPlaceAt(eye, cell)) return;
        if (World.Set(cell, Selected)) Hud.Flash($"Placed {Palette.Item(Selected).Name}");
    }

    void ShowHotbar()
    {
        int page = _selected / Hud.HotbarSlots * Hud.HotbarSlots;
        var slots = Enumerable.Range(page, Hud.HotbarSlots)
            .Select(i => i < _blocks.Length ? (Palette.Item(_blocks[i]), 1) : (null, 0))
            .ToList();
        var caption = _blocks.Length == 0 ? "" :
            $"{Palette.Item(Selected).Name}   ({_selected + 1}/{_blocks.Length}, page {page / Hud.HotbarSlots + 1})";
        Hud.ShowHotbar(slots, _selected - page, caption);
    }
}
