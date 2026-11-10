using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Ui;
using OpenCorneroids.Game.World;
using OpenCorneroids.Simulation.Combat;
using OpenCorneroids.Simulation.Items;

namespace OpenCorneroids.Game.Player;

/// <summary>
/// Survival rules on top of the Simulation library. The mouse works as in the original: the left
/// button acts with whatever is in hand (a drill drills and a weapon fires while it is held; a block is placed and a
/// consumable used when it is released), and letting go of the right button empties the hand. Mining numbers come from
/// <see cref="Mining"/>; this class only turns them into held-button timing and messages.
/// </summary>
public sealed class SurvivalMode : PlayMode
{
    public override string Controls =>
        "WASD move · Space/Ctrl up/down · Z/X roll · Shift boost · LMB use what you hold · RMB empty hand · 1-8/wheel select\n" +
        "R/middle turn block · E use · F edit · C inventory · B beacon · T light · L level · K fixed cam · N noclip · Esc game menu · F1 welcome";

    readonly Inventory _inventory;
    readonly ItemsetDef _itemset;
    readonly Suit _suit;
    readonly Dictionary<int, Trigger> _triggers = new();   // one reload per weapon
    readonly SpreadSequence _spread = new();                 // one spread index per weapon, as in the original
    int _selected;   // hotbar slot in hand, or -1 for an empty hand
    BlockTarget? _drillTarget;
    double _drillTime;

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

    ItemStack? InHand => _selected >= 0 ? _inventory.Slots[_selected] : null;

    /// <summary>
    /// A consumable in hand is used; something wearable is put on (ours: the original wears things only in the character
    /// window). Anything else is not "used".
    /// </summary>
    public override bool UseSelected()
    {
        if (InHand is not { } stack || !_itemset.ItemsById.TryGetValue(stack.ItemId, out var item)) return false;
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

    static Simulation.Settings.CvarStore Cvars => Settings.GameSettings.Cvars;

    /// <summary>The boost costs 20 suit power per second (<c>sv_boost_cost</c>) and needs this frame's share of it.</summary>
    public override bool TryBoost(double delta)
    {
        if (_suit.TrySpend(Cvars.Get(Simulation.Settings.Cvars.BoostCost) * delta)) return true;
        // Said every couple of seconds while Shift stays down, not every frame.
        double now = Time.GetTicksMsec() / 1000.0;
        if (now - _boostWarned > 2)
        {
            Hud.Flash("Not enough power to boost", warning: true);
            _boostWarned = now;
        }
        return false;
    }

    double _boostWarned = double.NegativeInfinity;

    public override bool Vulnerable => !_suit.IsDead && Cvars.Bool(Simulation.Settings.Cvars.PlayerDamage);

    public override double HealthLeft => _suit.Health;

    public override bool IsDead => _suit.IsDead;

    /// <summary>Armor first (<c>sv_armor_cap</c>, <c>sv_armor_scale</c>), as in the original, then the suit.</summary>
    public override double TakeDamage(double damage)
    {
        if (!Vulnerable) return 0;
        int after = Damage.AfterArmor(damage, _suit.Armor, Cvars.Int(Simulation.Settings.Cvars.ArmorCap),
            Cvars.Get(Simulation.Settings.Cvars.ArmorScale));
        return _suit.Hurt(after);
    }

    public override void OnDeath(Vector3 at)
    {
        base.OnDeath(at);
        if (!_suit.IsDead) _suit.Die();
        if (!Cvars.Bool(Simulation.Settings.Cvars.DeathDropItems)) return;
        // Ours (sv_death_drop_items): everything carried floats where the player died; the endless slots stay.
        for (int i = 0; i < _inventory.Slots.Count; i++)
        {
            if (_inventory.Slots[i] is not { } stack) continue;
            _inventory.SetSlot(i, null);
            if (Drops is { } drops) drops.Drop(stack.ItemId, stack.Quantity, at);
        }
    }

    public override void OnRespawn() => _suit.Respawn();

    /// <summary>Releasing the left button: a torch in hand is thrown, whatever is aimed at; a block in hand is placed
    /// (against an aimed face, or in empty space to attach within 16 units / start a new ship), or a consumable is used,
    /// as in the original.</summary>
    public override void PrimaryReleased(Vector3 eye, Aim? aim)
    {
        if (InHand is not { } stack || !_itemset.ItemsById.TryGetValue(stack.ItemId, out var item)) return;
        if (Torches.IsTorch(item))
        {
            ThrowTorch(item);
            return;
        }
        if (item.Block is not null)
        {
            if (aim is { } a) Place(eye, a);
            else PlaceInSpace(eye);
        }
        else UseSelected();
    }

    /// <summary>
    /// One torch thrown from the eye along the view, as in the original: it needs the torch's <c>powerUsage</c> in suit
    /// power (25), takes one torch from the stack and spends the power (<see cref="Torches.TryTake"/>). Ours: without the
    /// power the HUD says so; the original does nothing.
    /// </summary>
    void ThrowTorch(ItemDef torch)
    {
        if (Torchlight is not { } torches || _suit.IsDead) return;
        switch (Torches.TryTake(torch, _inventory, _selected, _suit, Cvars.Bool(Simulation.Settings.Cvars.WeaponNegativePower)))
        {
            case Torches.Refusal.None:
                var (eye, forward, velocity) = View;
                torches.Throw(torch, eye, forward, velocity);
                break;
            case Torches.Refusal.NoPower:
                Hud.Flash($"Not enough power to throw the {torch.Name}", warning: true);
                break;
        }
    }

    /// <summary>The right button does nothing on its way down; see <see cref="SecondaryReleased"/>.</summary>
    public override void SecondaryPressed(Vector3 eye, Aim? aim) { }

    /// <summary>Releasing the right button empties the hand, as in the original.</summary>
    public override void SecondaryReleased()
    {
        if (_selected < 0) return;
        _selected = -1;
        StopDrilling();
        ShowHotbar();
    }

    public override void Deselect() => SecondaryReleased();

    public override void Detach()
    {
        StopDrilling();
        _inventory.Changed -= ShowHotbar;
        _suit.Changed -= ShowSuit;
    }

    /// <summary>The drill in hand, if any: mining needs one held, as in the original.</summary>
    int HeldDrillPower => HeldItem?.Get<DrillComponent>()?.Power ?? 0;

    void ShowSuit() => Hud.ShowSuit(_suit.Health, _suit.MaxHealth, _suit.Power, _suit.MaxPower);

    public override bool HasInventory => true;

    public override string Status => HeldDrillPower is > 0 and var power ? $"   drill power {power}" : "";

    public override ushort SelectedBlock =>
        InHand is { } stack && Palette.IndexOf(stack.ItemId) is { } block ? block : BlockPalette.Air;

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

    /// <summary>The wheel steps through the slots and stops at the first and last, as in the original; from an empty hand it
    /// takes the first slot.</summary>
    public override void Cycle(int delta)
    {
        _selected = _selected < 0 ? 0 : Math.Clamp(_selected + delta, 0, Inventory.HotbarSize - 1);
        ShowHotbar();
    }

    public override ItemDef? HeldItem =>
        InHand is { } stack && _itemset.ItemsById.TryGetValue(stack.ItemId, out var item) && item.Block is null ? item : null;

    /// <summary>Raised when the held weapon fires, with its <c>pushback</c>, for the first-person kick.</summary>
    public event Action<float>? Fired;

    /// <summary>
    /// Drilling as in the original: with a drill in hand and the button held on a block within
    /// <see cref="DrillReach"/> m, progress builds for the time <see cref="Mining.SecondsToMine"/> gives and starts over
    /// when the aim moves or the button is let go. The block then breaks and leaves one of itself floating where it was
    /// (<see cref="FloatingItems"/>), whatever room the inventory has. Grey sparks fly while the drill bites, yellow ones
    /// when it is too weak, and then the block never breaks. A multi-block is one target however the aim moves over it.
    /// </summary>
    public override void Update(double delta, BlockTarget? target, bool drilling)
    {
        _suit.HealthRegenPerSecond = Cvars.Get(Simulation.Settings.Cvars.HealthRegen);
        _suit.Tick(delta);
        if (_suit.IsDead) return;
        // A weapon in hand: the primary button shoots instead of drilling.
        if (HeldItem is { } held && held.Get<WeaponComponent>() is { } weapon)
        {
            StopDrilling();
            if (drilling) Shoot(held, weapon);
            return;
        }
        int power = HeldDrillPower;
        if (!drilling || power == 0 || target is not { } cell || !InReach(cell))
        {
            StopDrilling();
            return;
        }
        if (_drillTarget != cell)
        {
            _drillTarget = cell;
            _drillTime = 0;
        }

        var item = Palette.Item(cell.Grid.BlockOf(cell));
        var block = item.Block!;
        bool bites = Mining.CanMine(power, block);
        Sparks(cell, bites, delta);
        if (!bites)
        {
            Hud.ShowProgress(null);
            return;
        }

        double seconds = Mining.SecondsToMine(power, block, Cvars.Get(Simulation.Settings.Cvars.MineTimeMax),
            Cvars.Get(Simulation.Settings.Cvars.MineTimeMin), Cvars.Get(Simulation.Settings.Cvars.MineTimePerPower));
        _drillTime = Math.Min(_drillTime + delta, seconds);
        Hud.ShowProgress(_drillTime / seconds);
        if (_drillTime < seconds) return;

        var drop = Mining.Drop(item);
        var at = TargetCenter(cell);
        Grids.Break(cell);
        if (Drops is { } drops) drops.Drop(drop.ItemId, drop.Quantity, at);
        else _inventory.Add(drop.ItemId, drop.Quantity);
        ResetDrill();
    }

    /// <summary>The original reaches 3 m with a drill, from the eye to the block (<c>sv_mine_reach</c>).</summary>
    public static float DrillReach => Cvars.Float(Simulation.Settings.Cvars.MineReach);

    /// <summary>Where mined items float and how they reach the player; set by the world.</summary>
    public FloatingItems? Drops { get; set; }

    double _sinceSparks;

    /// <summary>Measured along the view to where the aim enters the block, as the original does.</summary>
    bool InReach(BlockTarget cell) => AimDistance <= DrillReach;

    /// <summary>Where the aim meets the block: where the sparks fly.</summary>
    Vector3 AimPoint => View.Eye + View.Forward * AimDistance;

    static Vector3 TargetCenter(BlockTarget cell) => cell.Object is { } obj
        ? cell.Grid.ToGlobal(obj.ModelBox.GetCenter())
        : cell.Grid.ToGlobal((Vector3)cell.Cell + new Vector3(0.5f, 0.5f, 0.5f));

    void Sparks(BlockTarget cell, bool bites, double delta)
    {
        if ((_sinceSparks += delta) < 0.1 || Shots is null) return;
        _sinceSparks = 0;
        Shots.Spark(AimPoint - View.Forward * 0.05f, bites ? new Color(0.75f, 0.75f, 0.75f) : new Color(1f, 0.9f, 0.1f));
    }

    /// <summary>
    /// One shot if the weapon has reloaded and the suit has the power for it (the weapon's <c>powerUsage</c>; a negative
    /// one gives power, as in the original, <c>sv_weapon_negative_power</c>), along the view with the weapon's spread
    /// (<c>sv_weapon_spread_mode</c>), from the gun 0.3 m right of and 0.2 m below the eye
    /// (<see cref="Weapons.ShotOrigin"/>). The held weapon kicks on screen; as in the original the player is not moved,
    /// unless <c>sv_recoil_push</c> is on (<see cref="Weapons.Recoil"/>).
    /// </summary>
    void Shoot(ItemDef held, WeaponComponent weapon)
    {
        if (Shots is null || !_itemset.Projectiles.TryGetValue(weapon.ProjectileId, out var projectile)) return;
        double now = Time.GetTicksMsec() / 1000.0;
        if (!_triggers.TryGetValue(held.ItemId, out var trigger)) _triggers[held.ItemId] = trigger = new Trigger();
        if (trigger.Wait(now) > 0) return;
        if (!_suit.TryUse(Weapons.ShotCost(held, Cvars.Bool(Simulation.Settings.Cvars.WeaponNegativePower))))
        {
            Hud.Flash($"Not enough power to fire the {held.Name}", warning: true);
            return;
        }
        trigger.TryFire(now, weapon.ReloadMs);
        var (eye, forward, velocity) = View;
        var aim = forward.ToNumerics();
        var direction = Cvars.Int(Simulation.Settings.Cvars.WeaponSpreadMode) == 0
            ? _spread.Next(held.ItemId, aim, weapon.Spread)
            : Weapons.Spread(aim, weapon.Spread, Random.Shared.NextDouble(), Random.Shared.NextDouble());
        var up = ViewUp.Normalized();
        var right = forward.Cross(up).Normalized();
        var from = Weapons.ShotOrigin(eye.ToNumerics(), aim, right.ToNumerics(), up.ToNumerics(),
            Cvars.Float(Simulation.Settings.Cvars.ShotForwardOffset)).ToGodot();
        Shots.Fire(projectile, from, direction.ToGodot(), velocity, fromPlayer: true, speedScale: direction.Length());
        if (Cvars.Bool(Simulation.Settings.Cvars.RecoilPush))
            Push?.Invoke(Weapons.Recoil(direction, weapon.Pushback, projectile,
                Cvars.Float(Simulation.Settings.Cvars.RecoilScale)).ToGodot());
        Fired?.Invoke(weapon.Pushback);
    }

    public override void Place(Vector3 eye, Aim aim)
    {
        if (!TryGetHeldPlaceable(out var block)) return;
        TryPlace(eye, aim, block, () => _inventory.TryTakeFromSlot(_selected, 1));
    }

    public override void PlaceInSpace(Vector3 eye)
    {
        if (!TryGetHeldPlaceable(out var block)) return;
        TryPlaceInSpace(eye, View.Forward, block, () => _inventory.TryTakeFromSlot(_selected, 1));
    }

    bool TryGetHeldPlaceable(out ushort block)
    {
        block = BlockPalette.Air;
        if (InHand is not { } stack)
        {
            Hud.Flash("Nothing in hand to place", warning: true);
            return false;
        }
        if (!_itemset.ItemsById.TryGetValue(stack.ItemId, out var item) || item.Block is null)
        {
            Hud.Flash($"{NameOf(stack.ItemId)} can't be placed", warning: true);
            return false;
        }
        if (Palette.IndexOf(item.ItemId) is not { } idx)
        {
            // The palette leaves out only blocks whose face textures are missing.
            Hud.Flash($"{item.Name} has no usable textures in this pack, so it can't be placed", warning: true);
            return false;
        }
        block = idx;
        return true;
    }

    /// <summary>The button is up or the aim is off every block: the drilling starts over.</summary>
    void StopDrilling() => ResetDrill();

    void ResetDrill()
    {
        _drillTarget = null;
        _drillTime = 0;
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
        var selected = InHand;
        var caption = _selected < 0 || selected is null ? "" : NameOf(selected.ItemId);
        Hud.ShowHotbar(slots, _selected, caption);
    }
}
