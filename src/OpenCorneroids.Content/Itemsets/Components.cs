using System.Numerics;

namespace OpenCorneroids.Content.Itemsets;

/// <summary>Kind-specific data attached to an <see cref="ItemDef"/>.</summary>
public interface IItemComponent;

public sealed record LightDef(string Type, Rgb8 Color, float Radius);

/// <summary>Point-light blocks, drills, torches and some weapons emit light.</summary>
public sealed record LightComponent(LightDef Light) : IItemComponent;

public sealed record AsteroidComponent(AsteroidType AsteroidType) : IItemComponent;

public sealed record MineralComponent(AsteroidType AsteroidType, int Rarity) : IItemComponent;

public sealed record ConsoleComponent(int Buttons) : IItemComponent;

public sealed record CraftingStationComponent(CraftingStation Station) : IItemComponent;

public sealed record DoorComponent(Vector3 Direction, float Distance, float Speed, int TimeMs) : IItemComponent;

public sealed record GunComponent(float BarrelLength, int ReloadMs, int ProjectileId) : IItemComponent;

public sealed record ThrusterComponent(float Force, float FlameLength, Vector3 FlameOffset) : IItemComponent;

public sealed record ContainerComponent(int Width, int Height) : IItemComponent;

/// <summary>Drill strength, compared with a block's <c>powerToDrill</c>.</summary>
public sealed record DrillComponent(int Power) : IItemComponent;

public sealed record TorchComponent(int BurningTimeMs) : IItemComponent;

public sealed record WeaponComponent(int ReloadMs, int ProjectileId, float Pushback, float Spread) : IItemComponent;

public sealed record WearableComponent(WearableSlot Slot, int ArmorRating, int HealthRating, int PowerRechargeRate) : IItemComponent;

/// <param name="BuffSprite">Always (0,0) in known data; which atlas it points into is an open question.</param>
public sealed record ConsumableComponent(
    ConsumableKind Kind, int Armor, int Health, int Power, int DurationMs, CellRect BuffSprite) : IItemComponent;
