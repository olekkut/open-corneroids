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
        "WASD move · Space up · Ctrl down · Q/E roll · L level · Shift boost · N noclip · LMB mine · RMB place · R/T turn · F door/console · " +
        "wheel/1-9 select · Esc mouse, Esc again menu";

    readonly ushort[] _blocks;
    int _selected;

    public CreativeMode(GridSet grids, Hud hud) : base(grids, hud)
    {
        _blocks = Palette.Indices.ToArray();
        ShowHotbar();
    }

    public override bool HasInventory => false;

    public override ushort SelectedBlock => _blocks.Length == 0 ? BlockPalette.Air : _blocks[_selected];

    public override bool TrySelectBlock(ushort block)
    {
        int index = Array.IndexOf(_blocks, block);
        if (index < 0) return false;
        _selected = index;
        ShowHotbar();
        return true;
    }

    /// <summary>Number keys pick within the current page (the hotbar's width).</summary>
    public override void Select(int slot)
    {
        if (slot is < 0 or >= Hud.HotbarSlots) return;
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

    public override void PrimaryPressed(BlockTarget? target)
    {
        if (target is not { } t) return;
        var name = Palette.Item(t.Grid.BlockOf(t)).Name;
        Grids.Break(t);
        Hud.Flash($"Mined {name}");
    }

    public override void Place(Vector3 eye, Aim aim)
    {
        var block = SelectedBlock;
        if (block == BlockPalette.Air) return;
        if (TryPlace(eye, aim, block, () => true)) Hud.Flash($"Placed {Palette.Item(block).Name}");
    }

    void ShowHotbar()
    {
        int page = _selected / Hud.HotbarSlots * Hud.HotbarSlots;
        var slots = Enumerable.Range(page, Hud.HotbarSlots)
            .Select(i => i < _blocks.Length ? (Palette.Item(_blocks[i]), 1) : (null, 0))
            .ToList();
        var caption = _blocks.Length == 0 ? "" :
            $"{Palette.Item(SelectedBlock).Name}   ({_selected + 1}/{_blocks.Length}, page {page / Hud.HotbarSlots + 1})";
        Hud.ShowHotbar(slots, _selected - page, caption);
    }
}
