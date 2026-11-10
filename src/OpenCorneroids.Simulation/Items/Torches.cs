using System.Numerics;
using OpenCorneroids.Content;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Physics;

namespace OpenCorneroids.Simulation.Items;

/// <summary>A torch in flight: where it is, how it moves, how long it still burns and the colour of its light.</summary>
/// <param name="ItemId">The torch item it was thrown from (its sprite).</param>
/// <param name="Position">World position, m.</param>
/// <param name="Velocity">World velocity, m/s.</param>
/// <param name="RemainingMs">Milliseconds it still burns; it goes out at 0.</param>
/// <param name="Color">Its light's colour: the item's <c>&lt;light&gt;</c> colour.</param>
public record struct ThrownTorch(int ItemId, Vector3 Position, Vector3 Velocity, double RemainingMs, Rgb8 Color);

/// <summary>
/// Thrown torches, as in the original. Releasing the left button with a torch in hand throws one, whatever is aimed
/// at, if the suit has the torch's <c>powerUsage</c> in power (25 for the bundled torches): one torch leaves the stack
/// and the power is spent. It flies from the eye along the view at 7 m/s, without the thrower's own velocity, with no
/// gravity, slowing by 5 m/s² until it stops about 4.9 m away. It is a small box (half size 0.2) that slides along
/// what it hits and never bounces or sticks; a ship moving into it pushes it out without carrying it. It burns for its
/// <c>burningTime</c> and its light's radius is ln(milliseconds left), flickering by up to 10%. Thrown torches can't be
/// picked up and are not saved, and there is no limit on how many burn at once.
/// </summary>
public static class Torches
{
    /// <summary>Speed a torch is thrown at, m/s, as in the original.</summary>
    public const float ThrowSpeed = 7f;

    /// <summary>How fast a thrown torch slows down, m/s², as in the original.</summary>
    public const float Deceleration = 5f;

    /// <summary>Half size of a thrown torch's collision box, m: the original's sphere of radius 0.2.</summary>
    public const float Radius = 0.2f;

    /// <summary>Largest share the light's radius flickers by, as in the original.</summary>
    public const double Flicker = 0.1;

    /// <summary>The rules a throw follows; <see cref="Original"/> or the <c>sv_torch_*</c> settings.</summary>
    /// <param name="ThrowSpeed">m/s along the view (<c>sv_torch_throw_speed</c>).</param>
    /// <param name="Deceleration">m/s² against the motion (<c>sv_torch_decel</c>).</param>
    /// <param name="Radius">Half size of the collision box, m (<c>sv_torch_radius</c>).</param>
    /// <param name="LifetimeScale">× the item's <c>burningTime</c> (<c>sv_torch_lifetime_scale</c>).</param>
    /// <param name="Flicker">Largest share the light radius drops by (<c>sv_torch_flicker</c>).</param>
    /// <param name="InheritVelocity">Ours: the thrower's velocity is added (<c>sv_torch_inherit_velocity</c>).</param>
    /// <param name="Limit">Most torches burning at once, the oldest going out first; 0 = no limit
    /// (<c>sv_torch_limit</c>).</param>
    public readonly record struct Tuning(float ThrowSpeed, float Deceleration, float Radius, double LifetimeScale,
        double Flicker, bool InheritVelocity, int Limit)
    {
        /// <summary>The original game's values.</summary>
        public static Tuning Original => new(Torches.ThrowSpeed, Torches.Deceleration, Torches.Radius, 1, Torches.Flicker, false, 0);

        public static Tuning From(Settings.CvarStore cvars)
        {
            ArgumentNullException.ThrowIfNull(cvars);
            return new(
                cvars.Float(Settings.Cvars.TorchThrowSpeed), cvars.Float(Settings.Cvars.TorchDecel),
                cvars.Float(Settings.Cvars.TorchRadius), cvars.Get(Settings.Cvars.TorchLifetimeScale),
                cvars.Get(Settings.Cvars.TorchFlicker), cvars.Bool(Settings.Cvars.TorchInheritVelocity),
                cvars.Int(Settings.Cvars.TorchLimit));
        }
    }

    /// <summary>Whether the item is a torch that can be thrown.</summary>
    public static bool IsTorch(ItemDef item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Get<TorchComponent>() is not null;
    }

    /// <summary>
    /// Suit power one throw uses, by the same rule as a weapon shot (<see cref="Combat.Weapons.ShotCost"/>): the item's
    /// <c>powerUsage</c>. As in the original, a negative one always throws and gives that much power back
    /// (<c>sv_weapon_negative_power 1</c>); ours, with <paramref name="negativeGivesPower"/> false, its size is the cost.
    /// </summary>
    public static int Cost(ItemDef torch, bool negativeGivesPower = true)
    {
        ArgumentNullException.ThrowIfNull(torch);
        return Combat.Weapons.ShotCost(torch, negativeGivesPower);
    }

    /// <summary>Why a throw did not happen.</summary>
    public enum Refusal
    {
        /// <summary>The torch was thrown.</summary>
        None,
        /// <summary>The slot holds no such torch.</summary>
        NoTorch,
        /// <summary>The suit has less power than the throw costs; as in the original nothing happens.</summary>
        NoPower,
    }

    /// <summary>
    /// The cost of one throw, as in the original: with at least <see cref="Cost"/> suit power, one torch comes off the
    /// stack in <paramref name="slot"/> (the last one empties it) and the power is used (<see cref="Suit.TryUse"/>:
    /// a negative cost gives power back). With less, nothing changes.
    /// </summary>
    public static Refusal TryTake(ItemDef torch, Inventory inventory, int slot, Suit suit, bool negativeGivesPower = true)
    {
        ArgumentNullException.ThrowIfNull(torch);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(suit);
        if (!IsTorch(torch) || slot < 0 || slot >= inventory.Slots.Count ||
            inventory.Slots[slot] is not { Quantity: > 0 } stack || stack.ItemId != torch.ItemId)
            return Refusal.NoTorch;
        int cost = Cost(torch, negativeGivesPower);
        if (!suit.GodMode && suit.Power < cost) return Refusal.NoPower;
        if (!inventory.TryTakeFromSlot(slot, 1)) return Refusal.NoTorch;
        suit.TryUse(cost);
        return Refusal.None;
    }

    /// <summary>
    /// A torch leaving the hand: at <paramref name="eye"/>, moving along <paramref name="forward"/> at the throw speed
    /// (plus <paramref name="carrierVelocity"/> only with <see cref="Tuning.InheritVelocity"/>), burning for its
    /// <c>burningTime</c> × the lifetime scale, with its light's colour (black when it has no light, as in the
    /// original). Null for an item that is not a torch.
    /// </summary>
    public static ThrownTorch? Throw(ItemDef torch, Vector3 eye, Vector3 forward, Vector3 carrierVelocity, Tuning tuning)
    {
        ArgumentNullException.ThrowIfNull(torch);
        if (torch.Get<TorchComponent>() is not { } burning) return null;
        var direction = forward.LengthSquared() > 0 ? Vector3.Normalize(forward) : Vector3.Zero;
        var velocity = direction * Finite(tuning.ThrowSpeed);
        if (tuning.InheritVelocity && IsFinite(carrierVelocity)) velocity += carrierVelocity;
        double scale = double.IsFinite(tuning.LifetimeScale) ? Math.Max(0, tuning.LifetimeScale) : 1;
        var color = torch.Get<LightComponent>()?.Light.Color ?? default;
        return new ThrownTorch(torch.ItemId, eye, velocity, Math.Max(0, burning.BurningTimeMs) * scale, color);
    }

    /// <summary>
    /// One frame of a torch in free flight: it burns for <paramref name="dt"/> seconds and slows by the deceleration
    /// against its motion, stopping rather than turning back (the original's speed never quite reaches zero; the
    /// difference is under deceleration × one frame, which looks the same). Returns the move this frame (the exact
    /// distance covered while slowing, so the path is the same at any frame rate) and whether it still burns.
    /// </summary>
    public static (Vector3 Delta, bool Burning) Step(ref ThrownTorch torch, double dt, Tuning tuning)
    {
        if (!(dt > 0)) return (Vector3.Zero, torch.RemainingMs > 0);
        torch.RemainingMs -= dt * 1000;
        float seconds = (float)dt;
        float decel = Math.Max(0, Finite(tuning.Deceleration));
        var velocity = torch.Velocity;
        float speed = velocity.Length();
        Vector3 delta;
        if (speed == 0 || !float.IsFinite(speed))
        {
            delta = Vector3.Zero;
            velocity = Vector3.Zero;
        }
        else if (decel * seconds >= speed)
        {
            // Stops within this frame, after speed² / 2a.
            delta = velocity * (speed / decel / 2);
            velocity = Vector3.Zero;
        }
        else
        {
            var next = velocity * ((speed - decel * seconds) / speed);
            delta = (velocity + next) * (seconds / 2);
            velocity = next;
        }
        torch.Velocity = velocity;
        return (delta, torch.RemainingMs > 0);
    }

    /// <summary>
    /// Moves the torch by <paramref name="delta"/> through the world and every ship (<see cref="GridSweep.Move"/>):
    /// pushed out of a grid that has come to overlap it, then stopped at what it hits, the velocity into the surface
    /// removed so it slides along and never bounces.
    /// </summary>
    public static void Collide(ref ThrownTorch torch, Vector3 delta, IGridShape world, IReadOnlyList<IGridShape> ships, float radius)
    {
        float r = float.IsFinite(radius) ? Math.Clamp(radius, 0.01f, 2f) : Radius;
        var velocity = torch.Velocity;
        torch.Position = GridSweep.Move(torch.Position, new Vector3(r), delta, ref velocity, world, ships, maxPush: 1 + 2 * r);
        torch.Velocity = velocity;
    }

    /// <summary>
    /// The radius of a thrown torch's light, as in the original: ln(milliseconds left) × (1 − flicker × u) for a
    /// random <paramref name="u"/> in 0..1, so the item's own light radius no longer counts once it is thrown (about
    /// 9.9 with 20 s left, 6.9 with 1 s). Never negative and never NaN: 0 under one millisecond.
    /// </summary>
    public static float LightRadius(double remainingMs, double u, double flicker)
    {
        if (!(remainingMs >= 1) || double.IsInfinity(remainingMs)) return 0;
        double share = double.IsFinite(flicker) ? Math.Clamp(flicker, 0, 1) : 0;
        double random = double.IsFinite(u) ? Math.Clamp(u, 0, 1) : 0;
        return (float)(Math.Log(remainingMs) * (1 - share * random));
    }

    /// <summary>How many of the oldest torches go out so that at most <paramref name="limit"/> burn; 0 for no limit
    /// (<paramref name="limit"/> 0 or less, as in the original).</summary>
    public static int OverLimit(int count, int limit) => limit <= 0 ? 0 : Math.Max(0, count - limit);

    static float Finite(float value) => float.IsFinite(value) ? value : 0;

    static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
