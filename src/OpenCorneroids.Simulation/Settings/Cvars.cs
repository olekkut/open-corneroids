namespace OpenCorneroids.Simulation.Settings;

/// <summary>
/// Every console setting the game has. The defaults are the original game's behaviour (the "original" preset);
/// settings marked "Ours" control things the original does not have, and their defaults keep them as they were.
/// <c>sv_</c> settings are game rules saved with the world; <c>cl_</c> settings are the player's preferences.
/// </summary>
public static class Cvars
{
    const CvarFlags W = CvarFlags.World;
    const CvarFlags U = CvarFlags.User;
    const CvarFlags Cheat = CvarFlags.World | CvarFlags.Cheat;

    // ---- cheats ----

    public static readonly Cvar Cheats = new("sv_cheats", CvarKind.Bool, 0, W,
        "Allows cheat commands (noclip, god, give, kit, heal, health, power, tp) and cheat settings. Turning it off undoes them. A world remembers it was ever on.");

    public static readonly Cvar PlayerSpeedScale = new("sv_player_speed_scale", CvarKind.Float, 1, Cheat,
        "Multiplies all astronaut accelerations and speed limits (the 'speed' command).", 0.1, 50);

    // ---- astronaut movement (as in the original) ----

    public static readonly Cvar PlayerAccel = new("sv_player_accel", CvarKind.Float, 5, W,
        "Acceleration of each held movement key, m/s².", 0, 1000);

    public static readonly Cvar PlayerBoostAccel = new("sv_player_boost_accel", CvarKind.Float, 10, W,
        "Acceleration along the view while boosting, m/s².", 0, 1000);

    public static readonly Cvar PlayerSpeed = new("sv_player_speed", CvarKind.Float, 4, W,
        "Soft speed limit without boost, m/s.", 0, 1000);

    public static readonly Cvar PlayerBoostSpeed = new("sv_player_boost_speed", CvarKind.Float, 8, W,
        "Soft speed limit while boosting, m/s.", 0, 1000);

    public static readonly Cvar PlayerOverSpeedDecel = new("sv_player_over_speed_decel", CvarKind.Float, 8, W,
        "How fast speed above the soft limit bleeds off, m/s².", 0, 1000);

    public static readonly Cvar PlayerStopDecel = new("sv_player_stop_decel", CvarKind.Float, 6, W,
        "How fast the astronaut slows to a stop with no key held, m/s².", 0, 1000);

    public static readonly Cvar PlayerMaxSpeed = new("sv_player_max_speed", CvarKind.Float, 20, W,
        "Hard astronaut speed limit, m/s.", 0, 1000);

    public static readonly Cvar BoostCost = new("sv_boost_cost", CvarKind.Float, 20, W,
        "Suit power boosting costs per second.", 0, 1000);

    // ---- mining and drops (as in the original) ----

    public static readonly Cvar MineReach = new("sv_mine_reach", CvarKind.Float, 3, W,
        "How far the drill reaches, m.", 0.5, 64);

    public static readonly Cvar MineTimeMax = new("sv_mine_time_max", CvarKind.Float, 0.8, W,
        "Seconds to drill a block with no drill power to spare.", 0, 60);

    public static readonly Cvar MineTimeMin = new("sv_mine_time_min", CvarKind.Float, 0.3, W,
        "Fastest a block can be drilled, seconds.", 0, 60);

    public static readonly Cvar MineTimePerPower = new("sv_mine_time_per_power", CvarKind.Float, 0.1, W,
        "Seconds saved per point of drill power above the block's powerToDrill.", 0, 60);

    public static readonly Cvar ItemLifetime = new("sv_item_lifetime", CvarKind.Float, 30, W,
        "Seconds a mined block floats before it is gone.", 1, 3600);

    public static readonly Cvar ItemPickupReach = new("sv_item_pickup_reach", CvarKind.Float, 1.5, W,
        "Distance at which floating items fly to the player, m.", 0, 64);

    // ---- building (as in the original) ----

    public static readonly Cvar AttachDistance = new("sv_attach_distance", CvarKind.Float, 16, W,
        "A block placed in empty space attaches to the nearest object within this many metres; beyond it starts a new ship.", 0, 128);

    public static readonly Cvar BindDistance = new("sv_bind_distance", CvarKind.Float, 3, W,
        "Reach of the console binding tool, m.", 0.5, 64);

    // ---- ships (as in the original) ----

    public static readonly Cvar ShipMaxSpeed = new("sv_ship_max_speed", CvarKind.Float, 20, W,
        "Ship speed limit, units/s.", 0.1, 1000);

    public static readonly Cvar ShipMaxSpin = new("sv_ship_max_spin", CvarKind.Float, 1.5, W,
        "Ship turn rate limit, rad/s.", 0.01, 100);

    public static readonly Cvar ShipCoastDecel = new("sv_ship_coast_decel", CvarKind.Float, 0.25, W,
        "How fast a ship slows down when no thruster pushes it, units/s².", 0, 100);

    public static readonly Cvar ShipCoastSpinDecel = new("sv_ship_coast_spin_decel", CvarKind.Float, 0.03, W,
        "How fast a ship stops turning when no thruster turns it, rad/s².", 0, 100);

    public static readonly Cvar AssistForce = new("sv_assist_force", CvarKind.Float, 30, W,
        "Ours: thruster force multiplier for the automatic flight assistant.", 0.1, 1000);

    // ---- weapons ----

    public static readonly Cvar Ricochet = new("sv_ricochet", CvarKind.Bool, 0, W,
        "Ours: shots glance off surfaces they meet at less than the projectile's ricochetAngle. The original has no ricochets.");

    public static readonly Cvar RicochetBounces = new("sv_ricochet_bounces", CvarKind.Int, 3, W,
        "Ours: how many times one shot may glance off before it hits (with sv_ricochet 1).", 0, 16);

    public static readonly Cvar RecoilPush = new("sv_recoil_push", CvarKind.Bool, 0, W,
        "Ours: hand weapons push the shooter back by the weapon's pushback. In the original recoil does not move the player.");

    public static readonly Cvar RecoilScale = new("sv_recoil_scale", CvarKind.Float, 0.05, W,
        "Ours: m/s of push per unit of pushback × projectile speed (with sv_recoil_push 1).", 0, 10);

    public static readonly Cvar ProjectileInheritVelocity = new("sv_projectile_inherit_velocity", CvarKind.Bool, 0, W,
        "Ours: shots keep the shooter's own velocity. In the original they do not.");

    public static readonly Cvar ProjectileLimit = new("sv_projectile_limit", CvarKind.Int, 512, W,
        "Most shots in flight at once; past it no new shot is fired.", 1, 10000);

    public static readonly Cvar ProjectilePassThrough = new("sv_projectile_pass_through", CvarKind.Bool, 1, W,
        "A shot loses the damage it does and flies on through what it breaks while it has damage left. 0 = ours from before: it stops at the first block it hits.");

    public static readonly Cvar ProjectileHitRadius = new("sv_projectile_hit_radius", CvarKind.Float, 0.735, W,
        "A shot hits a player when it passes this close to their centre, m.", 0, 10);

    // ---- health, damage, death (as in the original) ----

    public static readonly Cvar PlayerDamage = new("sv_player_damage", CvarKind.Bool, 1, W,
        "Players take damage from shots and hard impacts. 0 = ours: nobody gets hurt.");

    public static readonly Cvar HealthRegen = new("sv_health_regen", CvarKind.Float, 0.5, W,
        "Health regained per second (one point every two seconds).", 0, 1000);

    public static readonly Cvar ArmorCap = new("sv_armor_cap", CvarKind.Int, 60, W,
        "Armor rating above this counts as this.", 0, 100_000);

    public static readonly Cvar ArmorScale = new("sv_armor_scale", CvarKind.Float, 6, W,
        "Armor takes off ln(armor + 1) / this of the damage; smaller makes armor stronger.", 0.1, 1000);

    public static readonly Cvar ImpactSpeed = new("sv_impact_speed", CvarKind.Float, 8, W,
        "Speed into a surface, m/s, from which hitting it hurts.", 0, 1000);

    public static readonly Cvar ImpactDamage = new("sv_impact_damage", CvarKind.Float, 7, W,
        "Damage per m/s of speed into a surface above sv_impact_speed (0 = impacts never hurt).", 0, 1000);

    public static readonly Cvar DamageSeatEject = new("sv_damage_seat_eject", CvarKind.Bool, 1, W,
        "Taking damage throws the player out of a console seat.");

    public static readonly Cvar RespawnMin = new("sv_respawn_min", CvarKind.Float, 64, W,
        "Nearest a respawn point is to where the player died, m.", 0, 10_000);

    public static readonly Cvar RespawnMax = new("sv_respawn_max", CvarKind.Float, 95, W,
        "Furthest a respawn point is from where the player died, m.", 0, 10_000);

    public static readonly Cvar DeathDropItems = new("sv_death_drop_items", CvarKind.Bool, 0, W,
        "Ours: dying drops the whole inventory as floating items where the player died. The original keeps it.");

    // ---- creatures (as in the original) ----

    public static readonly Cvar Creatures = new("sv_creatures", CvarKind.Bool, 1, W,
        "Creatures (mineral parasites, swarm eggs and their needles) appear on asteroids near the player.");

    public static readonly Cvar CreatureSpawnInterval = new("sv_creature_spawn_interval", CvarKind.Float, 1, W,
        "Seconds between creature spawn attempts.", 0.1, 3600);

    public static readonly Cvar ParasitesPerSector = new("sv_parasites_per_sector", CvarKind.Float, 1, W,
        "Parasites an asteroid may hold per 16³ sector of its blocks.", 0, 100);

    public static readonly Cvar ParasiteEatTime = new("sv_parasite_eat_time", CvarKind.Float, 20, W,
        "Seconds a mineral parasite takes to eat a block.", 0.1, 3600);

    public static readonly Cvar CreatureDamageScale = new("sv_creature_damage_scale", CvarKind.Float, 1, W,
        "Ours: multiplies what creatures' touches and bolts do to the player.", 0, 100);

    public static readonly Cvar Scavengers = new("sv_scavengers", CvarKind.Bool, 0, W,
        "Ours: scavenger flyers appear away from asteroids. The original has them but never lets one appear.");

    // ---- player preferences ----

    public static readonly Cvar FlightAssist = new("cl_flight_assist", CvarKind.Bool, 0, U,
        "Ours: fly ships with the automatic flight assistant instead of the console bindings (H).");

    public static readonly Cvar FixedCamera = new("cl_fixed_camera", CvarKind.Bool, 0, U,
        "Keep the view level with the deck while aboard a ship (K).");

    public static readonly Cvar Fov = new("cl_fov", CvarKind.Float, 75, U,
        "Field of view, degrees.", 30, 130);

    public static readonly Cvar Sensitivity = new("cl_sensitivity", CvarKind.Float, 1, U,
        "Mouse look sensitivity multiplier.", 0.05, 20);

    public static readonly Cvar MarkerMinBlocks = new("cl_marker_min_blocks", CvarKind.Int, 64, U,
        "Ours: asteroids smaller than this many blocks get no distance and mass marker.", 1, 1_000_000);

    /// <summary>Every setting, in the order <c>cvarlist</c> shows them.</summary>
    public static readonly IReadOnlyList<Cvar> All =
    [
        Cheats, PlayerSpeedScale,
        PlayerAccel, PlayerBoostAccel, PlayerSpeed, PlayerBoostSpeed, PlayerOverSpeedDecel, PlayerStopDecel, PlayerMaxSpeed, BoostCost,
        MineReach, MineTimeMax, MineTimeMin, MineTimePerPower, ItemLifetime, ItemPickupReach,
        AttachDistance, BindDistance,
        ShipMaxSpeed, ShipMaxSpin, ShipCoastDecel, ShipCoastSpinDecel, AssistForce,
        Ricochet, RicochetBounces, RecoilPush, RecoilScale, ProjectileInheritVelocity, ProjectileLimit,
        ProjectilePassThrough, ProjectileHitRadius,
        PlayerDamage, HealthRegen, ArmorCap, ArmorScale, ImpactSpeed, ImpactDamage, DamageSeatEject,
        RespawnMin, RespawnMax, DeathDropItems,
        Creatures, CreatureSpawnInterval, ParasitesPerSector, ParasiteEatTime, CreatureDamageScale, Scavengers,
        FlightAssist, FixedCamera, Fov, Sensitivity, MarkerMinBlocks,
    ];
}
