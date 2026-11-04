using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Ui;
using OpenCorneroids.Game.World;
using OpenCorneroids.Simulation.Items;

namespace OpenCorneroids.Game.Player;

/// <summary>
/// Survival rules on top of the Simulation library: blocks take time to drill and need a strong enough drill, what
/// is mined goes into the inventory, and placing spends the selected hotbar stack. All numbers come from
/// <see cref="Mining"/>; this class only turns them into held-button timing and messages.
/// </summary>
public sealed class SurvivalMode : PlayMode
{
    public override string Controls =>
        "WASD move · Space/E up · Ctrl/Q down · Shift boost · N noclip · hold LMB drill · RMB place/use · " +
        "R/T turn · wheel/1-8 select · F use station/door/console · I/Tab inventory · Esc mouse, Esc again menu";

    readonly Inventory _inventory;
    readonly ItemsetDef _itemset;
    readonly Suit _suit;
    bool _boostSpent;   // power ran out while boosting; no boost until it is back to BoostResume
    int _selected;
    BlockTarget? _drillTarget;
    double _drillTime;
    bool _drillBlocked;   // the target is fully drilled but its drop did not fit

    public SurvivalMode(GridSet grids, Hud hud, Inventory inventory, ItemsetDef itemset, Suit suit) : base(grids, hud)
    {
        _inventory = inventory;
        _itemset = itemset;
        _suit = suit;
        _inventory.Changed += ShowHotbar;
        _suit.Changed += ShowSuit;
        ShowHotbar();
        ShowSuit();
    }

    /// <summary>
    /// RMB with a consumable selected uses it; with something wearable it is put on. Anything else (a block, a tool)
    /// is left to placing.
    /// </summary>
    public override bool UseSelected()
    {
        if (_inventory.Slots[_selected] is not { } stack || !_itemset.ItemsById.TryGetValue(stack.ItemId, out var item)) return false;
        if (item.Get<ConsumableComponent>() is not null)
        {
            if (_suit.TryConsume(_inventory, _selected) is { } refusal) Hud.Flash(refusal, warning: true);
            else Hud.Flash($"Used {item.Name}");
            return true;
        }
        if (item.Get<WearableComponent>() is not null)
        {
            if (_suit.TryEquip(_inventory, _selected)) Hud.Flash($"Wearing {item.Name}");
            else Hud.Flash("No room to take off what you wear there", warning: true);
            return true;
        }
        return false;
    }

    /// <summary>Power needed before boosting works again once it ran out, so the recharge can't make the boost
    /// stutter on and off every other frame. Ours, tunable.</summary>
    const double BoostResume = 20;

    public override bool TryBoost(double delta)
    {
        if (_boostSpent)
        {
            if (_suit.Power < BoostResume) return false;
            _boostSpent = false;
        }
        if (_suit.TrySpend(Suit.BoostPowerPerSecond * delta)) return true;
        _boostSpent = true;
        Hud.Flash("Out of power: no boost until it recharges", warning: true);
        return false;
    }

    void ShowSuit() => Hud.ShowSuit(_suit.Health, _suit.MaxHealth, _suit.Power, _suit.MaxPower);

    public override bool HasInventory => true;

    public override string Status => $"   drill power {Mining.DrillPower(_inventory, _itemset)}";

    public override ushort SelectedBlock =>
        _inventory.Slots[_selected] is { } stack && Palette.IndexOf(stack.ItemId) is { } block ? block : BlockPalette.Air;

    public override bool TrySelectBlock(ushort block)
    {
        var itemId = Palette.Item(block).ItemId;
        for (int i = 0; i < Inventory.HotbarSize; i++)
        {
            if (_inventory.Slots[i]?.ItemId != itemId) continue;
            Select(i);
            return true;
        }
        return false;
    }

    public override void Select(int slot)
    {
        if (slot is < 0 or >= Inventory.HotbarSize) return;
        _selected = slot;
        ShowHotbar();
    }

    public override void Cycle(int delta)
    {
        _selected = ((_selected + delta) % Inventory.HotbarSize + Inventory.HotbarSize) % Inventory.HotbarSize;
        ShowHotbar();
    }

    /// <summary>
    /// Drilling needs continuous aim: progress builds while the button is held on one block and starts over when
    /// the aim moves to another block or the button is let go. The exception is a block that is fully drilled but
    /// whose drop has nowhere to go: it stays intact and remembers it is done, even across letting go and opening
    /// the inventory, so the player can make room and then finish it with a click instead of drilling it again.
    /// Aiming the drill at any other block forgets it. A multi-block is one target however the aim moves over it,
    /// and takes the drill power and time of its own BlockDef, like any block.
    /// </summary>
    public override void Update(double delta, BlockTarget? target, bool drilling)
    {
        _suit.Tick(delta);
        if (!drilling || target is not { } cell)
        {
            StopDrilling();
            return;
        }
        if (_drillTarget != cell)
        {
            _drillTarget = cell;
            _drillTime = 0;
            _drillBlocked = false;
        }

        var item = Palette.Item(cell.Grid.BlockOf(cell));
        var block = item.Block!;
        int power = Mining.DrillPower(_inventory, _itemset);
        if (!Mining.CanMine(power, block))
        {
            Hud.ShowProgress(null);
            Hud.Flash(power == 0
                ? $"You need a drill to mine {item.Name}"
                : $"{item.Name} needs drill power {block.PowerToDrill} (your best drill: {power})", warning: true);
            return;
        }

        double seconds = Mining.SecondsToMine(power, block);
        _drillTime = Math.Min(_drillTime + delta, seconds);
        Hud.ShowProgress(_drillTime / seconds);
        if (_drillTime < seconds) return;

        var drop = Mining.Drop(item);
        if (!_inventory.CanAdd([drop]))
        {
            _drillBlocked = true;
            Hud.Flash("Inventory full", warning: true);
            return;
        }
        Grids.Break(cell);
        _inventory.Add(drop.ItemId, drop.Quantity);
        Hud.Flash($"+{drop.Quantity} {NameOf(drop.ItemId)}");
        ResetDrill();
    }

    public override void Place(Vector3 eye, Aim aim)
    {
        if (_inventory.Slots[_selected] is not { } stack)
        {
            Hud.Flash("Nothing selected to place", warning: true);
            return;
        }
        if (!_itemset.ItemsById.TryGetValue(stack.ItemId, out var item) || item.Block is null)
        {
            Hud.Flash($"{NameOf(stack.ItemId)} can't be placed", warning: true);
            return;
        }
        if (Palette.IndexOf(item.ItemId) is not { } block)
        {
            // The palette leaves out only blocks whose face textures are missing.
            Hud.Flash($"{item.Name} has no usable textures in this pack, so it can't be placed", warning: true);
            return;
        }
        TryPlace(eye, aim, block, () => _inventory.TryTakeFromSlot(_selected, 1));
    }

    /// <summary>The button is up or the aim is off every block. The bar always goes (it may still show a block
    /// that was just broken); a blocked, fully drilled block keeps its progress, anything else starts over.</summary>
    void StopDrilling()
    {
        Hud.ShowProgress(null);
        if (!_drillBlocked) ResetDrill();
    }

    void ResetDrill()
    {
        _drillTarget = null;
        _drillTime = 0;
        _drillBlocked = false;
        Hud.ShowProgress(null);
    }

    string NameOf(int itemId) => _itemset.ItemsById.TryGetValue(itemId, out var item) ? item.Name : $"item {itemId}";

    void ShowHotbar()
    {
        var slots = new List<(ItemDef?, int)>(Inventory.HotbarSize);
        for (int i = 0; i < Inventory.HotbarSize; i++)
        {
            var stack = _inventory.Slots[i];
            slots.Add(stack is not null && _itemset.ItemsById.TryGetValue(stack.ItemId, out var item) ? (item, stack.Quantity) : (null, 0));
        }
        var selected = _inventory.Slots[_selected];
        var caption = selected is null ? "Empty slot" : NameOf(selected.ItemId);
        Hud.ShowHotbar(slots, _selected, caption);
    }
}
