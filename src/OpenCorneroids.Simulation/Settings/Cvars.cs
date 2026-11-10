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

    public static readonly Cvar PlayerPushoutMax = new("sv_player_pushout_max", CvarKind.Float, 2.5, W,
        "Ours: furthest a block grid that has come to overlap the astronaut (a ship closing on them, rock their ship carried them into) pushes them out in one frame, metres. Deeper in a ship they are left to climb out; deeper in rock they are moved to the nearest free spot. 0 turns pushing off.", 0, 16);

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

    public static readonly Cvar ItemTossSpeed = new("sv_item_toss_speed", CvarKind.Float, 3, W,
        "Speed of a stack thrown out of an item window by clicking outside it, along the view, m/s.", 0, 100);

    public static readonly Cvar ItemCloseTossSpeed = new("sv_item_close_toss_speed", CvarKind.Float, 2, W,
        "Speed of a stack still held on the cursor when an item window closes, thrown along the view, m/s.", 0, 100);

    public static readonly Cvar ItemTossPickupDelay = new("sv_item_toss_pickup_delay", CvarKind.Float, 2, W,
        "Seconds before a thrown stack can be picked up again.", 0, 60);

    public static readonly Cvar ItemDrag = new("sv_item_drag", CvarKind.Float, 0.98, W,
        "Speed a moving floating item keeps per 1/60 s (the original applies it once per update; its update rate is uncertain). 1 = never slows down.", 0, 1);

    // ---- using blocks and chests (as in the original) ----

    public static readonly Cvar UseReach = new("sv_use_reach", CvarKind.Float, 1.7, W,
        "How far E (use) and F (edit) reach from the eye to the block, m: doors, consoles, crafting stations, chests and power blocks. 1.7 as in the original.", 0.2, 64);

    public static readonly Cvar ContainerSpill = new("sv_container_spill", CvarKind.Bool, 1, W,
        "A broken chest throws out what it held as floating items, as in the original. 0 = the contents are lost with the block.");

    public static readonly Cvar ContainerSpillSpeedMin = new("sv_container_spill_speed_min", CvarKind.Float, 1, W,
        "Slowest speed a broken chest's stacks fly out at, m/s.", 0, 100);

    public static readonly Cvar ContainerSpillSpeedMax = new("sv_container_spill_speed_max", CvarKind.Float, 4, W,
        "Fastest speed a broken chest's stacks fly out at, m/s.", 0, 100);

    public static readonly Cvar ContainerSpillPickupDelay = new("sv_container_spill_pickup_delay", CvarKind.Float, 1, W,
        "Seconds before a stack thrown out of a broken chest can be picked up.", 0, 60);

    // ---- thrown torches (as in the original) ----

    public static readonly Cvar TorchThrowSpeed = new("sv_torch_throw_speed", CvarKind.Float, 7, W,
        "Speed a torch is thrown at along the view, m/s.", 0, 100);

    public static readonly Cvar TorchDecel = new("sv_torch_decel", CvarKind.Float, 5, W,
        "How fast a thrown torch slows down, m/s² (from 7 m/s it stops about 4.9 m away).", 0, 100);

    public static readonly Cvar TorchRadius = new("sv_torch_radius", CvarKind.Float, 0.2, W,
        "Size of a thrown torch against blocks: half the side of its collision box, m.", 0.01, 2);

    public static readonly Cvar TorchLifetimeScale = new("sv_torch_lifetime_scale", CvarKind.Float, 1, W,
        "How long a thrown torch burns, × the item's burningTime.", 0, 100);

    public static readonly Cvar TorchFlicker = new("sv_torch_flicker", CvarKind.Float, 0.1, W,
        "Largest share a thrown torch's light radius drops by each frame (its radius is ln of the milliseconds left).", 0, 1);

    public static readonly Cvar TorchHeldLight = new("sv_torch_held_light", CvarKind.Bool, 1, W,
        "A torch in hand lights the area around the player with its own light.");

    public static readonly Cvar TorchLimit = new("sv_torch_limit", CvarKind.Int, 0, W,
        "Most thrown torches burning at once, the oldest going out first; 0 = no limit, as in the original.", 0, 4096);

    public static readonly Cvar TorchInheritVelocity = new("sv_torch_inherit_velocity", CvarKind.Bool, 0, W,
        "Ours: a thrown torch keeps the thrower's own velocity. In the original it does not.");

    // ---- beacons (as in the original) ----

    public static readonly Cvar BeaconReach = new("sv_beacon_reach", CvarKind.Float, 6, W,
        "How near a beacon must be for the beacon tool (B) to pick it, and a click to remove it, m.", 0.5, 64);

    public static readonly Cvar BeaconPlaceDistance = new("sv_beacon_place_distance", CvarKind.Float, 2, W,
        "How far ahead along the view the beacon tool puts a new beacon, m.", 0, 64);

    public static readonly Cvar BeaconNameMax = new("sv_beacon_name_max", CvarKind.Int, 20, W,
        "Longest beacon name, characters; longer names are cut.", 1, 64);

    // ---- inventory (as in the original) ----

    public static readonly Cvar InventorySlots = new("sv_inventory_slots", CvarKind.Int, 30, W,
        "Main inventory slots beside the 8-slot hotbar: 30 (6 × 5) as in the original. Takes effect when the world is loaded again; a world saved with a larger inventory keeps its size.", 6, 96)
    {
        AppliesOnLoad = true,
    };

    public static readonly Cvar InventoryFillHotbarFirst = new("sv_inventory_fill_hotbar_first", CvarKind.Bool, 1, W,
        "Picked-up items go into the first slot that takes them, hotbar first and then the main inventory, as in the original. 0 = ours: matching stacks anywhere are topped up before an empty slot is used.");

    public static readonly Cvar InventoryPickupWhole = new("sv_inventory_pickup_whole", CvarKind.Bool, 1, W,
        "With sv_inventory_fill_hotbar_first 1, the part of a picked-up stack the hotbar can't take goes into the main inventory only if all of it fits there; otherwise it stays floating, as in the original. 0 = ours: the main inventory takes what fits.");

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

    public static readonly Cvar ShipRestitution = new("sv_ship_restitution", CvarKind.Float, 0, W,
        "Fraction of the closing speed a ship bounces back with after a hit. 0 as in the original: no bounce.", 0, 1);

    public static readonly Cvar ShipContactSpinDamping = new("sv_ship_contact_spin_damping", CvarKind.Float, 0.5, W,
        "Ours: each contact multiplies a ship's spin by this, so scraping along rock settles it (1 keeps the spin). The original turns ships by contact torque instead.", 0, 1);

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

    public static readonly Cvar WeaponSpreadMode = new("sv_weapon_spread_mode", CvarKind.Int, 0, W,
        "Hand weapon spread. 0 as in the original: aim plus spread × a fixed list of 256 directions, so shot speed varies by up to ±spread. 1 = ours: an even cone of spread radians at full speed.", 0, 1);

    public static readonly Cvar ShotForwardOffset = new("sv_shot_forward_offset", CvarKind.Float, 1, W,
        "How far along the aim a hand weapon's shot starts from the gun (0.3 m right of and 0.2 m below the eye), m.", 0, 10);

    public static readonly Cvar WeaponNegativePower = new("sv_weapon_negative_power", CvarKind.Bool, 1, W,
        "A weapon with a negative powerUsage always fires and gives that much suit power per shot, as in the original. 0 = ours: it costs the size of its powerUsage.");

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

    public static readonly Cvar Fov = new("cl_fov", CvarKind.Float, 90, U,
        "Field of view, degrees (Graphics: Field of view).", 45, 135);

    public static readonly Cvar Sensitivity = new("cl_sensitivity", CvarKind.Float, 0.5, U,
        "Mouse sensitivity: the view turns 0.01 × this radians per pixel (Controls: Mouse sensitivity).", 0, 3);

    public static readonly Cvar CameraPitchLimit = new("cl_camera_pitch_limit", CvarKind.Float, 90, U,
        "Seated at a console with a camera block, how far the mouse turns the camera up or down, degrees; it turns all the way round sideways. As in the original.", 0, 90);

    public static readonly Cvar CameraEyeForward = new("cl_camera_eye_forward", CvarKind.Float, 0, U,
        "Ours: moves a camera block's view out of the block to this far past its front face, m, when the cell there is free. 0 = the block's centre, as in the original.", 0, 0.6);

    public static readonly Cvar SpeedConeFollowsView = new("cl_speed_cone_follows_view", CvarKind.Bool, 0, U,
        "Ours: seated with a camera block in use, the speed cone turns with the camera's view. 0 = it keeps to the seat's look, as in the original.");

    public static readonly Cvar DrawTargets = new("cl_draw_targets", CvarKind.Bool, 1, U,
        "Distance and mass markers on asteroids and ships (Gameplay: Draw target squares).");

    public static readonly Cvar ViewBobbing = new("cl_view_bobbing", CvarKind.Bool, 1, U,
        "The view shakes above 4 m/s, up to 0.02 rad at 8 m/s (Gameplay: View bobbing).");

    public static readonly Cvar ShowHints = new("cl_show_hints", CvarKind.Bool, 1, U,
        "On-screen hints for what you hold and the block you look at (Gameplay: Show hints).");

    public static readonly Cvar ItemTossOnWindow = new("cl_item_toss_on_window", CvarKind.Bool, 1, U,
        "Holding a stack in an item window, a click on the window that is not on a slot throws it, as in the original. 0 (ours): only a click outside the windows throws it.");

    public static readonly Cvar ItemClickNeedsRelease = new("cl_item_click_needs_release", CvarKind.Bool, 1, U,
        "In item windows a press counts only while the other mouse button is up, as in the original. 0 (ours): every press acts.");

    public static readonly Cvar Gamma = new("cl_gamma", CvarKind.Int, 50, U,
        "Brightness, 0 to 50 (Gameplay: Gamma).", 0, 50);

    public static readonly Cvar Fullscreen = new("cl_fullscreen", CvarKind.Bool, 1, U,
        "Full screen rather than a window (Graphics: Fullscreen). Ours on by default; the original starts windowed.");

    public static readonly Cvar ResolutionWidth = new("cl_resolution_width", CvarKind.Int, 0, U,
        "Window width in pixels; 0 = the desktop's (Graphics: Resolution).", 0, 16384);

    public static readonly Cvar ResolutionHeight = new("cl_resolution_height", CvarKind.Int, 0, U,
        "Window height in pixels; 0 = the desktop's (Graphics: Resolution).", 0, 16384);

    public static readonly Cvar VSync = new("cl_vsync", CvarKind.Bool, 1, U, "Wait for the screen's refresh (Graphics: V-Sync).");

    public static readonly Cvar Lighting = new("cl_lighting", CvarKind.Bool, 1, U,
        "Dynamic lights: the flashlight and light blocks (Graphics: Lighting). Ours on by default; the original's is off.");

    public static readonly Cvar Ssao = new("cl_ssao", CvarKind.Int, 2, U,
        "Ambient occlusion: 0 off, 1 low, 2 high (Graphics: SSAO).", 0, 2);

    public static readonly Cvar ViewDistance = new("cl_view_distance", CvarKind.Int, 100, U,
        "50 short, 100 medium, 150 far (Graphics: View distance). Ours: sets how far the world is drawn, 8 × this metres.", 50, 150);

    public static readonly Cvar Welcome = new("cl_welcome", CvarKind.Bool, 1, U,
        "Show the welcome window whenever a world loads, as the original does (F1 reopens it).");

    public static readonly Cvar MarkerMinBlocks = new("cl_marker_min_blocks", CvarKind.Int, 64, U,
        "Ours: asteroids smaller than this many blocks get no distance and mass marker.", 1, 1_000_000);

    public static readonly Cvar RemeshPerFrame = new("cl_remesh_per_frame", CvarKind.Int, 0, U,
        "Ours: how many chunk meshes are rebuilt per frame after blocks change, nearest the camera first; 0 = every changed chunk in the same frame. The original rebuilds one per frame.", 0, 4096);

    public static readonly Cvar BatchObjects = new("cl_batch_objects", CvarKind.Bool, 1, U,
        "Ours: draw the multi-blocks of each chunk (all but doors) as one merged mesh instead of one mesh each; looks the same, far fewer draw calls. The original also merges them into its sector meshes.");

    public static readonly Cvar TorchDrawDistance = new("cl_torch_draw_distance", CvarKind.Float, 32, U,
        "How far away a thrown torch's sprite is drawn, m (its light shines at any distance), as in the original.", 1, 1000);

    public static readonly Cvar TorchLightLimit = new("cl_torch_light_limit", CvarKind.Int, 64, U,
        "Ours: only this many thrown torches nearest the camera cast light, to spare the renderer; the original lights every one.", 0, 512);

    public static readonly Cvar BeaconMarkers = new("cl_beacon_markers", CvarKind.Bool, 1, U,
        "Beacon markers (orange, with distance and name), drawn whether or not target squares are on, as in the original. 0 = ours: hidden.");

    /// <summary>Every setting, in the order <c>cvarlist</c> shows them.</summary>
    public static readonly IReadOnlyList<Cvar> All =
    [
        Cheats, PlayerSpeedScale,
        PlayerAccel, PlayerBoostAccel, PlayerSpeed, PlayerBoostSpeed, PlayerOverSpeedDecel, PlayerStopDecel, PlayerMaxSpeed, BoostCost, PlayerPushoutMax,
        MineReach, MineTimeMax, MineTimeMin, MineTimePerPower, ItemLifetime, ItemPickupReach,
        ItemTossSpeed, ItemCloseTossSpeed, ItemTossPickupDelay, ItemDrag,
        UseReach, ContainerSpill, ContainerSpillSpeedMin, ContainerSpillSpeedMax, ContainerSpillPickupDelay,
        TorchThrowSpeed, TorchDecel, TorchRadius, TorchLifetimeScale, TorchFlicker, TorchHeldLight, TorchLimit, TorchInheritVelocity,
        BeaconReach, BeaconPlaceDistance, BeaconNameMax,
        InventorySlots, InventoryFillHotbarFirst, InventoryPickupWhole,
        AttachDistance, BindDistance,
        ShipMaxSpeed, ShipMaxSpin, ShipCoastDecel, ShipCoastSpinDecel, AssistForce, ShipRestitution, ShipContactSpinDamping,
        Ricochet, RicochetBounces, RecoilPush, RecoilScale, ProjectileInheritVelocity, ProjectileLimit,
        ProjectilePassThrough, ProjectileHitRadius, WeaponSpreadMode, ShotForwardOffset, WeaponNegativePower,
        PlayerDamage, HealthRegen, ArmorCap, ArmorScale, ImpactSpeed, ImpactDamage, DamageSeatEject,
        RespawnMin, RespawnMax, DeathDropItems,
        Creatures, CreatureSpawnInterval, ParasitesPerSector, ParasiteEatTime, CreatureDamageScale, Scavengers,
        FlightAssist, FixedCamera, Fov, Sensitivity, CameraPitchLimit, CameraEyeForward, SpeedConeFollowsView, MarkerMinBlocks,
        DrawTargets, ViewBobbing, ShowHints, ItemTossOnWindow, ItemClickNeedsRelease, Gamma,
        Fullscreen, ResolutionWidth, ResolutionHeight, VSync, Lighting, Ssao, ViewDistance, Welcome, RemeshPerFrame, BatchObjects,
        TorchDrawDistance, TorchLightLimit, BeaconMarkers,
    ];
}
