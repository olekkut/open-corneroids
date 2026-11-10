using System.Numerics;

namespace OpenCorneroids.Content.Itemsets;

/// <summary>Kind-specific data attached to an <see cref="ItemDef"/>.</summary>
public interface IItemComponent;

public sealed record LightDef(string Type, Rgb8 Color, float Radius)
{
    /// <summary>A <c>&lt;light&gt;</c> without a <c>radius</c> has radius 1, as in the original.</summary>
    public const float DefaultRadius = 1;
}

/// <summary>Point-light blocks, drills, torches and some weapons emit light.</summary>
public sealed record LightComponent(LightDef Light) : IItemComponent;

public sealed record AsteroidComponent(AsteroidType AsteroidType) : IItemComponent;

public sealed record MineralComponent(AsteroidType AsteroidType, int Rarity) : IItemComponent;

public sealed record ConsoleComponent(int Buttons, bool GunsOnly = false) : IItemComponent;

public sealed record CraftingStationComponent(CraftingStation Station) : IItemComponent;

public sealed record DoorComponent(Vector3 Direction, float Distance, float Speed, int TimeMs) : IItemComponent;

/// <summary>A ship gun. <c>reloadTime</c> follows the hand weapons' rules (<see cref="WeaponComponent"/>).</summary>
public sealed record GunComponent(float BarrelLength, int ReloadMs, int ProjectileId) : IItemComponent;

public sealed record ThrusterComponent(float Force, float FlameLength, Vector3 FlameOffset) : IItemComponent;

/// <summary>
/// A container's grid of slots. As in the original, a missing <c>width</c> or <c>height</c> is 1 and the grid is held
/// within 1..<see cref="MaxWidth"/> by 1..<see cref="MaxHeight"/>, so no container has more than 35 slots.
/// </summary>
public sealed record ContainerComponent(int Width, int Height) : IItemComponent
{
    public const int MaxWidth = 7;
    public const int MaxHeight = 5;
}

/// <summary>Drill strength, compared with a block's <c>powerToDrill</c>.</summary>
public sealed record DrillComponent(int Power) : IItemComponent;

/// <summary>How long a thrown torch burns. As in the original, a missing <c>burningTime</c> is
/// <see cref="DefaultBurningTimeMs"/> and a negative one is 0.</summary>
public sealed record TorchComponent(int BurningTimeMs) : IItemComponent
{
    public const int DefaultBurningTimeMs = 10_000;
}

/// <summary>
/// A hand weapon. As in the original: a missing <c>reloadTime</c> is <see cref="DefaultReloadMs"/> and none is shorter
/// than <see cref="MinReloadMs"/> (ship guns too); <c>spread</c> is held within 0..<see cref="MaxSpread"/> and
/// <c>pushback</c> within 0..<see cref="MaxPushback"/>.
/// </summary>
public sealed record WeaponComponent(int ReloadMs, int ProjectileId, float Pushback, float Spread) : IItemComponent
{
    public const int DefaultReloadMs = 1000;
    public const int MinReloadMs = 50;
    public const float MaxSpread = 0.5f;
    public const float MaxPushback = 1f;
}

public sealed record WearableComponent(WearableSlot Slot, int ArmorRating, int HealthRating, int PowerRechargeRate) : IItemComponent;

/// <param name="BuffSprite">Always (0,0) in known data; which atlas it points into is an open question.</param>
public sealed record ConsumableComponent(
    ConsumableKind Kind, int Armor, int Health, int Power, int DurationMs, CellRect BuffSprite) : IItemComponent;
