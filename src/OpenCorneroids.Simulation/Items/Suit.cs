using OpenCorneroids.Content.Itemsets;

namespace OpenCorneroids.Simulation.Items;

/// <summary>A timed consumable still in effect.</summary>
/// <param name="ItemId">The consumable that gave it; using the same one again restarts it instead of stacking.</param>
public sealed record SuitBuff(int ItemId, double SecondsLeft, int Armor, int Power)
{
    /// <summary>Added to maximum health while it lasts (not to current health), as in the original.</summary>
    public int Health { get; init; }
}

/// <summary>
/// The astronaut's suit: health, suit power, the seven equipment slots and timed buffs, as in the original: 100 health
/// regenerating half a point a second, 100 power recharging 5 a second plus what is worn, worn and timed bonuses to
/// maximum health, armor and recharge, death at 0 health and a respawn with 100 of each (see
/// <see cref="Combat.Damage"/> for how damage is cut by armor).
/// </summary>
/// <remarks>
/// Equipment slots are indexed by <see cref="WearableSlot"/>. Every public call that changes anything visible raises
/// <see cref="Changed"/> once; <see cref="Tick"/> raises it only when a whole point of health or power, or a buff,
/// changed, so a HUD can redraw on it without redrawing every frame.
/// </remarks>
public sealed class Suit
{
    public const int BaseMaxHealth = 100;
    public const int BaseMaxPower = 100;
    /// <summary>Power per second with nothing worn: the footage's "Power recharge: 5". Per second is ours.</summary>
    public const int BasePowerRecharge = 5;
    public const int SlotCount = 7;

    readonly ItemsetDef itemset;
    readonly ItemStack?[] equipment = new ItemStack?[SlotCount];
    readonly List<SuitBuff> buffs = [];

    public Suit(ItemsetDef itemset)
    {
        ArgumentNullException.ThrowIfNull(itemset);
        this.itemset = itemset;
        Health = BaseMaxHealth;
        Power = BaseMaxPower;
    }

    public event Action? Changed;

    public double Health { get; private set; }
    public double Power { get; private set; }
    /// <summary>When enabled via the debug console, suit power is never spent and health/power stay full.</summary>
    public bool GodMode { get; set; }
    public IReadOnlyList<ItemStack?> Equipment => equipment;
    public IReadOnlyList<SuitBuff> Buffs => buffs;

    /// <summary>Health regained per second while alive (<c>sv_health_regen</c>; 0.5 as in the original).</summary>
    public double HealthRegenPerSecond { get; set; } = Combat.Damage.HealthRegenPerSecond;

    /// <summary>Health ran out: no regeneration and no further damage until <see cref="Respawn"/>.</summary>
    public bool IsDead { get; private set; }

    public int MaxHealth => BaseMaxHealth + Worn().Sum(w => w.HealthRating) + buffs.Sum(b => Math.Max(0, b.Health));
    public int MaxPower => BaseMaxPower;
    public int Armor => Worn().Sum(w => w.ArmorRating) + buffs.Sum(b => b.Armor);
    public int PowerRecharge => BasePowerRecharge + Worn().Sum(w => w.PowerRechargeRate) + buffs.Sum(b => b.Power);

    /// <summary>The wearable data of an item, or null when it can't be worn.</summary>
    public WearableComponent? WearableOf(int itemId) =>
        itemset.ItemsById.TryGetValue(itemId, out var item) ? item.Get<WearableComponent>() : null;

    /// <summary>
    /// Puts on the item in <paramref name="inventorySlot"/>. One item is taken from the stack; whatever was worn in that
    /// equipment slot goes back to the inventory (into the emptied slot when the stack was a single item). Fails
    /// without changing anything when the item can't be worn or the old piece has nowhere to go.
    /// </summary>
    public bool TryEquip(Inventory inventory, int inventorySlot)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (inventory.Slots[inventorySlot] is not { } stack || WearableOf(stack.ItemId) is not { } wearable) return false;
        int slot = (int)wearable.Slot;
        if (slot is < 0 or >= SlotCount) return false;
        var old = equipment[slot];
        if (stack.Quantity == 1)
        {
            inventory.SetSlot(inventorySlot, old);
        }
        else
        {
            if (old is not null && !inventory.CanAdd([old])) return false;
            inventory.TryTakeFromSlot(inventorySlot, 1);
            if (old is not null) inventory.Add(old.ItemId, old.Quantity);
        }
        equipment[slot] = stack with { Quantity = 1 };
        ClampHealth();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Takes off what is worn in <paramref name="slot"/> into the inventory; false when nothing is worn or there is no room.</summary>
    public bool TryUnequip(WearableSlot slot, Inventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        int i = (int)slot;
        if (i is < 0 or >= SlotCount || equipment[i] is not { } worn || !inventory.CanAdd([worn])) return false;
        inventory.Add(worn.ItemId, worn.Quantity);
        equipment[i] = null;
        ClampHealth();
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Uses one consumable from <paramref name="inventorySlot"/>. Instant ones add health, power and nothing more (armor
    /// has no meaning for an instant); timed ones start a buff, restarting it if the same item is already running.
    /// Returns why it was refused, or null when it was used. A potion is refused at full health so it isn't wasted.
    /// </summary>
    public string? TryConsume(Inventory inventory, int inventorySlot)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (inventory.Slots[inventorySlot] is not { } stack || !itemset.ItemsById.TryGetValue(stack.ItemId, out var item)
            || item.Get<ConsumableComponent>() is not { } effect)
            return "That can't be used";
        if (effect.Kind == ConsumableKind.Instant)
        {
            bool helps = (effect.Health > 0 && Health < MaxHealth) || (effect.Power > 0 && Power < MaxPower);
            if (!helps) return effect.Health > 0 ? "Health is already full" : "Power is already full";
            Health = Math.Min(MaxHealth, Health + effect.Health);
            Power = Math.Min(MaxPower, Power + effect.Power);
        }
        else
        {
            buffs.RemoveAll(b => b.ItemId == item.ItemId);
            buffs.Add(new SuitBuff(item.ItemId, Math.Max(0, effect.DurationMs) / 1000.0, Math.Max(0, effect.Armor), effect.Power)
            {
                Health = Math.Max(0, effect.Health),
            });
        }
        inventory.TryTakeFromSlot(inventorySlot, 1);
        Changed?.Invoke();
        return null;
    }

    /// <summary>Spends <paramref name="amount"/> power if it is all there; otherwise spends nothing.</summary>
    public bool TrySpend(double amount)
    {
        if (GodMode || amount <= 0) return true;
        if (Power < amount) return false;
        SetPower(Power - amount);
        return true;
    }

    /// <summary>Sets suit health (clamped to <c>0..MaxHealth</c>) and notifies listeners. 0 kills.</summary>
    public void SetHealth(double value)
    {
        if (!double.IsFinite(value)) return;
        Health = Math.Clamp(value, 0, MaxHealth);
        if (Health <= 0)
        {
            Die();
            return;
        }
        IsDead = false;
        Changed?.Invoke();
    }

    /// <summary>Sets suit power (clamped to <c>0..MaxPower</c>) and notifies listeners.</summary>
    public void SetPowerLevel(double value)
    {
        if (!double.IsFinite(value)) return;
        Power = Math.Clamp(value, 0, MaxPower);
        Changed?.Invoke();
    }

    /// <summary>Restores both health and power to their maxima.</summary>
    public void RestoreFull()
    {
        Health = MaxHealth;
        Power = MaxPower;
        IsDead = false;
        Changed?.Invoke();
    }

    /// <summary>
    /// Takes <paramref name="amount"/> health (already reduced by armor; see <see cref="Combat.Damage.AfterArmor"/>).
    /// Returns what was taken: nothing while dead or in god mode. Reaching 0 makes the suit <see cref="IsDead"/>.
    /// </summary>
    public double Hurt(double amount)
    {
        if (IsDead || GodMode || !(amount > 0) || !double.IsFinite(amount)) return 0;
        double taken = Math.Min(Health, amount);
        Health -= taken;
        if (Health <= 0)
        {
            Health = 0;
            Die();
            return taken;
        }
        Changed?.Invoke();
        return taken;
    }

    /// <summary>Death, as in the original: health 0 and every running consumable effect cleared.</summary>
    public void Die()
    {
        Health = 0;
        IsDead = true;
        buffs.Clear();
        Changed?.Invoke();
    }

    /// <summary>
    /// Back to life, as in the original: health to the base 100 (worn bonuses are not filled) and suit power to 100.
    /// </summary>
    public void Respawn()
    {
        IsDead = false;
        Health = Math.Min(BaseMaxHealth, MaxHealth);
        Power = MaxPower;
        Changed?.Invoke();
    }

    /// <summary>Recharges power, regenerates health and runs buffs down by <paramref name="seconds"/> of game time.</summary>
    public void Tick(double seconds)
    {
        if (seconds <= 0 || IsDead) return;
        bool changed = false;
        for (int i = buffs.Count - 1; i >= 0; i--)
        {
            var left = buffs[i].SecondsLeft - seconds;
            if (left <= 0) { buffs.RemoveAt(i); changed = true; }
            else
            {
                if (Math.Ceiling(left) != Math.Ceiling(buffs[i].SecondsLeft)) changed = true;
                buffs[i] = buffs[i] with { SecondsLeft = left };
            }
        }
        if (changed) ClampHealth();
        var before = (int)Power;
        Power = Math.Min(MaxPower, Power + PowerRecharge * seconds);
        var healthBefore = (int)Health;
        if (HealthRegenPerSecond > 0 && double.IsFinite(HealthRegenPerSecond) && Health < MaxHealth)
            Health = Math.Min(MaxHealth, Health + HealthRegenPerSecond * seconds);
        if (changed || (int)Power != before || (int)Health != healthBefore) Changed?.Invoke();
    }

    /// <summary>
    /// Loads saved values: health and power are clamped to their maxima. Saved equipment that this itemset can't wear in
    /// that slot (an unknown id, a changed slot, an eighth slot) is not worn but returned, for the caller to put in the
    /// inventory, so a changed itemset never silently deletes items.
    /// </summary>
    /// <returns>The saved equipment that could not be worn.</returns>
    public IReadOnlyList<ItemStack> Restore(SuitState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var rejected = new List<ItemStack>();
        Array.Clear(equipment);
        for (int i = 0; i < state.Equipment.Count; i++)
        {
            if (state.Equipment[i] is not { Quantity: > 0 } stack) continue;
            if (i < SlotCount && WearableOf(stack.ItemId) is { } w && (int)w.Slot == i) equipment[i] = stack with { Quantity = 1 };
            else rejected.Add(stack);
        }
        buffs.Clear();
        foreach (var b in state.Buffs)
            if (b.SecondsLeft > 0 && double.IsFinite(b.SecondsLeft)) buffs.Add(b);
        Health = Math.Clamp(double.IsFinite(state.Health) ? state.Health : MaxHealth, 0, MaxHealth);
        Power = Math.Clamp(double.IsFinite(state.Power) ? state.Power : MaxPower, 0, MaxPower);
        // Ours: a world saved by a dead player loads with them respawned where they lie.
        IsDead = false;
        if (Health <= 0) Health = Math.Min(BaseMaxHealth, MaxHealth);
        Changed?.Invoke();
        return rejected;
    }

    public SuitState Save() => new(Health, Power, [.. equipment], [.. buffs]);

    IEnumerable<WearableComponent> Worn() =>
        equipment.Select(s => s is null ? null : WearableOf(s.ItemId)).OfType<WearableComponent>();

    void SetPower(double value)
    {
        var before = (int)Power;
        Power = Math.Clamp(value, 0, MaxPower);
        if ((int)Power != before) Changed?.Invoke();
    }

    void ClampHealth() => Health = Math.Min(Health, MaxHealth);
}

/// <summary>The suit as saved. Equipment is indexed by <see cref="WearableSlot"/>.</summary>
public sealed record SuitState(double Health, double Power, IReadOnlyList<ItemStack?> Equipment, IReadOnlyList<SuitBuff> Buffs);
