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
        "WASD move · Space/Ctrl up/down · Z/X roll · Shift boost · LMB mine · RMB place · wheel/1-8 select · R/middle turn block\n" +
        "E door/console · F edit · B beacon · T light · L level · K fixed cam · N noclip · Esc game menu · F1 welcome";

    readonly ushort[] _blocks;
    int _selected;

    public CreativeMode(GridSet grids, Hud hud) : base(grids, hud)
    {
        _blocks = Palette.Indices.ToArray();
        ShowHotbar();
    }

    public override bool HasInventory => false;

    public override ushort SelectedBlock => _selected < 0 || _blocks.Length == 0 ? BlockPalette.Air : _blocks[_selected];

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
        int baseIndex = Math.Max(0, _selected);
        int page = baseIndex / Hud.HotbarSlots * Hud.HotbarSlots;
        if (page + slot < _blocks.Length) _selected = page + slot;
        ShowHotbar();
    }

    /// <summary>The wheel runs through the whole palette, turning pages as it goes.</summary>
    public override void Cycle(int delta)
    {
        if (_blocks.Length == 0) return;
        if (_selected < 0) _selected = 0;
        else _selected = ((_selected + delta) % _blocks.Length + _blocks.Length) % _blocks.Length;
        ShowHotbar();
    }

    public override void Deselect()
    {
        if (_selected < 0) return;
        _selected = -1;
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

    public override void PlaceInSpace(Vector3 eye)
    {
        var block = SelectedBlock;
        if (block == BlockPalette.Air) return;
        if (TryPlaceInSpace(eye, View.Forward, block, () => true)) Hud.Flash($"Placed {Palette.Item(block).Name}");
    }

    void ShowHotbar()
    {
        int baseIndex = Math.Max(0, _selected);
        int page = baseIndex / Hud.HotbarSlots * Hud.HotbarSlots;
        var slots = Enumerable.Range(page, Hud.HotbarSlots)
            .Select(i => i < _blocks.Length ? (Palette.Item(_blocks[i]), 1) : (null, 0))
            .ToList();
        var caption = _selected < 0 ? ""
            : _blocks.Length == 0 ? ""
            : $"{Palette.Item(SelectedBlock).Name}  ({_selected + 1}/{_blocks.Length})";
        Hud.ShowHotbar(slots, _selected < 0 ? -1 : _selected - page, caption, countText: "");
    }
}
