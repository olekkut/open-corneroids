# Open Corneroids: guide

How to run, play and develop the game in detail. The [README](../README.md) is the short version.

## Requirements

- Godot Engine – .NET 4.7.2 (the standard build can't run C#)
- .NET SDK 8 or later

## Content

The repository and every release bundle the data of Corneroids 1.2.0, the CADNR v21 itemset (DarthNox and Chef Anon),
Nav's "Random Stuff" itemset and Sord's textures in `byoa/` (see [CREDITS](../CREDITS.md)). More installs, itemset mods
and texture packs can be added, unmodified, to either of:

- `byoa/` next to the game executable (or in this repository, during development)
- `%APPDATA%\OpenCorneroids\byoa\` (created on first run, with a README)

Folders are recognised by their contents. See [byoa-asset-loading.md](design/byoa-asset-loading.md).

## Run

On Windows, double-click `Play.cmd` in the repository root: it builds the game and starts it. It finds Godot .NET in a
`Godot_v*_mono_win64` folder in the repository, or at the path in the `OPEN_CORNEROIDS_GODOT` environment variable,
and passes its own arguments to the game (`Play.cmd --creative`).

Or open `game/project.godot` in Godot .NET and press Play, or from the repository root:

```
dotnet build game/OpenCorneroids.csproj
<godot-dotnet-exe> --path game
```

The window starts maximized. The interface is laid out for 1280x720 and scaled to fit the window, so it looks the
same on a laptop, at 1440p or at 4K; wider or taller screens get the extra room. The window can't be made smaller
than 960x540.

Arguments after `--`:

| Argument | Effect |
|---|---|
| `--itemset=auto` / `=<index>` / `=<pack>/<key>` / `=<pack>` / `=<key>` / `=<name>` | Skip the menu and load that itemset, tried in that order (ignoring case). A pack id means that pack's own itemset, not the copy of vanilla a repack ships beside it (`--itemset=v21` is CADNR v21, `--itemset=DarthNoxItemset` the DarthNoxItemset pack); a key several packs share means the base install's (`DefaultItemset`) or else is ambiguous, and the message lists the matches with their indexes |
| `--textures=<packId>[,<packId>]` | Enable texture packs (with `--itemset`) |
| `--byoa-dir=<path>` | Scan an extra content folder |
| `--creative` | Creative rules: instant mining, unlimited placing from every block, no inventory |
| `--showcase` | Place one of every block in front of the player (implies `--creative`): single-cell blocks in a grid, multi-blocks upright in rows below it. The player moves back towards the world's +Z edge to see it all, and rock in between is cleared |
| `--texture-mapping=stretch` / `=tile` | How multi-block faces use their texture region: once over the whole face (default), or repeated every region-size cells. Display only, not saved |
| `--orientation=<0..23>` | Initial orientation for placing multi-blocks (up axis × 4 + spin; 0 is upright) |
| `--preview=<itemId>` | Select that block (survival: given one if the hotbar has none) and aim at the asteroid so the placement ghost shows, preferring a spot where it fits. The camera stands back at an angle so the whole footprint is in view, and the aim stays on that spot until the mouse moves or a key is pressed; for screenshots |
| `--preview-space=<itemId>` | Select that block (survival: given one) and aim into empty space near a ship (so the ghost and the red attachment beam to its centre of mass show) or into open space beyond 16 m |
| `--place` | With `--preview` or `--preview-space`: place the previewed block there by the normal rules (survival spends the item), attaching within 16 m or starting a new floating ship beyond it |
| `--focus=<itemId>` | Move the camera close to the first placed multi-block of that item (from the side it already looks at), framing its footprint; for checking a showcase or loaded object by eye |
| `--select=<itemId>` | Select the hotbar slot holding that item (survival), e.g. a weapon for a firing test |
| `--drop=<itemId>:<count>[,...]` | Leave those items floating a metre in front of the player at the start (survival), to check picking up |
| `--record=<folder>` | Save a numbered PNG 15 times a second of game time until the `--screenshot`, to make short clips (the README's flight clip was made this way) |
| `--open-doors` | Open every door once the world is up, and keep doors from closing by themselves; a door that would open into the player stays shut (warning in the console); for screenshots |
| `--demo-ship` | In a new world, build a small ship near the spawn, at the nearest spot with 40 cells of open space in front of it (−Z, where it faces), from the itemset's own blocks, attached to nothing: a hull of the first plain opaque block, the first flight console in its top front, the strongest power supply on top and two thrusters on each face (exhaust outwards; on the top and bottom on opposite diagonals, so it can pitch and roll both ways), wired with cables and pre-bound to `W` `S` `A` `D` `Space` on its console so it flies in both console-binding and flight-assistant modes. The camera moves to look at it |
| `--launch` | Press E on a console of the static world: the `--demo-ship` console, or else the one nearest the camera, launching its structure as a ship |
| `--turn-ship=<degrees>` | Turn every ship about the world's up axis through its centre of mass (after `--launch`), to check that aiming, the ghost and building follow a ship's own axes |
| `--place-on-ship=<itemId>` | Select that block (survival: given one) and place it on the first face of a ship, seen from the camera, where it fits, by the normal rules |
| `--preview-ship=<itemId>` | As `--place-on-ship`, but only aim there, so the highlight and the ghost show on the ship; for screenshots |
| `--pilot` / `=seat` / `=chase` / `=fixed` | Sit at the first ship's console (after `--demo-ship --launch`, or in a loaded world) and pilot it, with the camera on the seat (or bound camera block), chasing the ship from behind, or (`fixed`, for before/after screenshots) left where it was. Applied after the hooks above |
| `--flight-assist` / `=on` / `=off` | Toggle the optional automatic flight assistant (default `off`: original console group bindings) |
| `--fixed-camera` / `=on` / `=off` | Toggle locking camera rotation to a boarded ship's rotation (default `off`) |
| `--dampeners=on` / `=off` | Inertial dampeners for the flight assistant (default `on`) |
| `--hold=<action>[+<action>...]:<seconds>` | Hold these input actions down for that many seconds of game time, as if the keys were pressed (the screenshot lock ignores only the real keyboard). Repeat the argument for a sequence. Actions: `move_forward`, `move_back`, `move_left`, `move_right`, `move_boost` (walking and piloting), `move_up`, `move_down` (walking only), `ship_up`, `ship_down`, `ship_pitch_up`, `ship_pitch_down`, `ship_yaw_left`, `ship_yaw_right` (piloting only), `ship_roll_left`, `ship_roll_right` (rolling the ship or the astronaut), and `primary` (the left mouse button: drill, shoot, or fire the ship's guns). Toggles (dampeners, flight assist, view) are not held: use `--flight-assist`, `--dampeners`, `--pilot=`. Game time counts each frame up to 0.25 s, as the ship physics does, so a loading hitch does not use up a hold |
| `--frames=<n>` | Take the `--screenshot` at frame n (default 240), or with `--quit-after-save` run that many frames before saving and quitting (so held controls and flying ships are in the save). The screenshot run's frame rate is uncapped, so the moment n frames is depends on the machine: prefer `--seconds` |
| `--seconds=<s>` | As `--frames`, but after s seconds of game time (the clock `--hold` counts in), the same moment on any machine. Wins over `--frames`. The log says the frame and time it fired at |
| `--open=inventory` / `=crafting` / `=console` / `=power` / `=help` / `=debug` | Open that window once the first world (new or loaded) is up. `crafting` shows the first crafting-table station; `console` opens the "Control block" window of the first console; `power` opens the "Power network" popup of the first powered block; `help` opens the `F1` controls & mechanics window; `debug` opens the `` ` `` debug console |
| `--console=<cmd>[;<cmd>...]` | Run one or more console commands after the first world is up (e.g. `--console="sv_ricochet 1;sv_cheats 1;speed 3;tp 64 80 100"`) |
| `--bind-console` / `=wide` / `=close` | Equip the first console's binding tool and frame either the whole ship (`wide`, default) or a close deck view within 3 m of a thruster (`close`); for screenshots |
| `--debug-hud` | Show the `F3` top-left technical readout (FPS, coordinates, ship telemetry) from startup |
| `--give=<itemId>:<count>[,<itemId>:<count>...]` | Add items to the survival inventory of the first *new* world, after the starting kit. Not applied to loaded worlds or to worlds picked later from the menu |
| `--seed=<n>` | Generator seed for a world started from the command line (default 2012) |
| `--new-world=<name>` | Create a world with this name (from `--itemset`, default `auto`, with `--textures`, `--creative`, `--seed`) and save it at once, replacing a save of the same name |
| `--load=<world name>` | Load a saved world instead of showing the menu. `--itemset`, `--textures`, `--creative`, `--showcase` and `--seed` are ignored: the save decides |
| `--quit-after-save` | Save the world started from the command line, then quit (no screenshot). Exit code 1 if nothing was saved. With `--frames` or `--seconds`, the world runs that long first |
| `--screenshot=<file.png>` | Save a screenshot after 240 frames (or `--frames` / `--seconds`), then quit (without saving the world). Mouse and keyboard are ignored meanwhile, so stray input can't change the picture. The log has the frame rate measured over the run |

The game starts in survival with the itemset's starting kit (`defaultItems`), as the original gives it: the first drill,
the crafting table and the ore furnace are endless (beside the inventory: click one to take another), and the rest
goes into the main inventory. The inventory has 40 slots, the first eight being the hotbar. Crafting uses only what is
in the main inventory, not the hotbar: craft one at a time, or half of what you can afford.

Your suit has **Health** and **Power** (the bars above the hotbar). Power recharges by itself (5 per second, more
with a power source worn); boosting with Shift uses it. Armor and helmets, power sources and the like are worn in the
**character** window beside the inventory, or put on from the hotbar. Nothing hurts you yet; that comes with combat.

With a weapon selected (the starting kit has a pistol), the left button fires instead of drilling. Each shot costs
suit power and pushes you back a little; shots break blocks once they've done enough damage (shot-apart blocks drop
nothing).

Green markers show each asteroid's distance and mass, white ones each ship's. The log at the left keeps the last
messages for a few seconds.

Controls (survival) follow the original's defaults:

- **Looking:** the mouse turns you freely in every direction (there is no fixed up); Z/X roll; L levels the view again
  (ours).
- **Moving:** WASD, Space up and Ctrl down along your own up. Each key pushes at 5 m/s² and you cruise at 4 m/s; Shift
  boosts along the view at 10 m/s² up to 8 m/s for 20 suit power a second. Let go and you coast to a stop. N is noclip
  (ours; a cheat, so it needs `sv_cheats 1`).
- **Hands:** the left button acts with what you hold. A drill drills (within 3 m: grey sparks when it bites, yellow when
  it is too weak) and a weapon fires while it is held down; a block is placed and a consumable used (or a wearable put
  on) when you let go. With a block in hand and no block face aimed at, the preview floats ahead of you in empty space:
  within 16 m of an existing ship or asteroid a red beam shows what it will attach to, and beyond 16 m releasing the
  left button starts a new floating ship, as in the original. A mined block floats where it was for 30 seconds; come
  within 1.5 m and it flies to you. Letting go of the right button empties your hand. 1–8 or the wheel pick a hotbar
  slot; R or the middle button turns the block you are about to place.
- **Blocks:** E uses the block you look at (a console's seat, a crafting station, a door); F edits it: on a console it
  equips that console's binding tool (left-click thrusters or cannons within 3 m to cycle their colour group, right-click
  to put the tool away, or press F again to open the "Control block" window for key assignment and "Set camera"); on any
  other powered block F opens the "Power network" popup showing block count and power balance. Contextual `"F to edit"` /
  `"E to use"` prompts appear beneath the crosshair when aiming at an interactive block within reach. T switches the
  flashlight.
- **Windows & HUD:** C (or I) opens the inventory and character window (click a stack, then click where it goes, or onto
  its equipment slot to wear it; put it on the red trash slot to throw it away, click the trash to take the last
  thrown stack back; the next throw destroys it and saves don't keep it). `` ` `` opens the in-game **Console** (for
  changing any game setting, see [Settings](#settings-console-variables) below, switching between `creative` and
  `survival` modes, spawning ships, and, with `sv_cheats 1`, cheats such as `speed`, `tp`, `give`, `noclip` and `god`;
  `Up`/`Down` browse history and `Tab` completes commands and setting names). F1 opens the Help & Controls window, F3 toggles the top-left technical readout (FPS,
  coordinates, flight telemetry), F6 hides or shows the HUD, and F5 saves the world. Esc closes a window, frees the
  mouse, and again returns to the menu. Movement stops while a window is open.
- **Ships:** come within about a metre of a ship's blocks and you are aboard (indicated by the magnetic-boot icon to the
  right of the hotbar): you move along with it, and when K ("Fixed camera when aboard") is on your view turns with the
  ship too, until you are about 2.5 m clear of it again.

Multi-block blocks (doors, thrusters, guns, power blocks, big windows) are placed whole. While any block is selected,
the preview shows its natural block faces wrapped in white 8-corner selector brackets covering its full footprint
(including thruster exhaust space), or tinted red with red brackets when blocked (outside the world, overlapping blocks
or another multi-block's footprint, or in your body). Rock or hull in front of the preview hides it, as it would the
placed block.
R or the middle mouse button turns multi-blocks and turnable single blocks: the block's top points to the next of the
six directions, and after all six the spin moves on, so all 24 orientations come round.
Single-cell blocks whose facing matters turn the same way: consoles, crafting stations, torches, cables, containers,
cameras and the single-cell Anti-Personnel Cannon (anything whose top or bottom differs from its sides, and every gun).
A depth-tested red and white arrow in the preview points along the block's top (and straight out of a thruster's nozzle
through its exhaust box). Plain cubes (rock, armor, windows) look the same any way round, so R says so instead.
Only the model is solid: thruster exhaust and door travel space can be flown through but not built in. Aiming at any part
of a multi-block's model targets all of it, and breaking it drops the item. Doors slide along their own up axis and close
again after their time; a door never closes on the player (it stays open and says so). Door speed is read as cells per
second and time as milliseconds open (ours; original behaviour unknown).

## Ships

Ships can be started straight in empty space (place a block more than 16 m from any rock or ship) or launched from a
structure built on an asteroid: E on a console that is part of the static world **launches** its face-connected
structure (stopping at asteroid rock and minerals) as a ship with its own 64³ block grid. Console bindings made before
launching carry over onto the ship.

Ships are built on like the world: aiming, mining, placing (against a face or within 16 m in space, with the red
attachment beam pointing to the ship's centre of mass), the ghost, doors, consoles and stations all work on them. Mining
a ship down to nothing removes it.

### Console binding and power networks

- **Power networks (as in the original).** Face-touching blocks that have a `power` value form a network. Its balance is
  the signed sum of every member block's `power` (supply positive, consumers and cables negative or zero, counted
  continuously). When `balance >= 0` every block on the network is active and shows its `activeTextures`; when
  `balance < 0` the whole network goes inactive (`deactiveTextures`), thrusters and cannons on it cannot fire, and a
  console on it refuses seating (or immediately unmounts a seated pilot if power is lost). Pressing `F` on a non-console
  powered block opens the **Power network** popup with its block count and power balance.
- **Console binding (as in the original).** Pressing `F` on a console equips its binding tool (deselecting the hotbar)
  and highlights the console in blue, bound blocks in their colour group, any bound camera in magenta, and a valid aimed
  block within 3 m in white. Releasing the left mouse button on a thruster or cannon on the same grid cycles it through
  the console's colour groups (up to `min(buttons, 10)`: Red, Green, LightBlue, Yellow, Violet, Orange, Brown,
  Turquoise, Pink, DarkBlue) and unbinds after the last group; gun consoles bind cannons only. Pressing `F` a second
  time on the same console opens the **Control block** window to bind a key to each colour group (Esc cancels) or click
  **Set camera** to bind a camera block on the same grid.

### Flying

E on a powered ship console sits the player down at it (Esc or E again stands up, aboard the ship). In `seat` view, if a
powered camera block is bound to that console the view comes from that camera block; otherwise it sits at the console. V
switches between seat/camera view and a chase camera behind the ship.

Two control schemes are available while seated (`H` toggles between them on flight consoles):

- **Original console bindings (default).** Holding a colour group's bound key fires its powered thrusters continuously
  (each pushing with its itemset `force`, turning the ship when mounted off the centre of mass; speed capped at 20 m/s,
  spin at 1.5 rad/s, and coasting slows by 0.25 m/s² and 0.03 rad/s² when no thruster pushes) and fires its bound
  cannons whenever reloaded. On a gun console, bound cannons aim where the seated player looks.
- **Flight assistant (optional, `H` or `--flight-assist=on`).** W/S forward/back, A/D strafe, Space/Ctrl up/down, Shift
  boost, mouse/arrows pitch and yaw, Z/X roll, G inertial dampeners, and left mouse button to fire all guns. The
  allocator picks throttles 0..1 across powered thrusters (scaled by 30× itemset `force`) to match the requested
  velocity and turn rate without spinning lopsided hulls.

Standing up puts the player at the nearest free spot beside the console moving with the ship's velocity as drift.
Console colour-group keys, bound targets and bound cameras are saved with both the static world and every ship.

## Health, damage and death

As in the original:

- **Health** is 100 plus what worn armor adds (and timed consumables while they last), and comes back at half a point a
  second.
- **Armor** (the worn pieces and timed consumables added up, at most 60) takes off ln(armor + 1) / 6 of each hit: about
  30% at 5, 40% at 10, 51% at 20 and 69% at 60.
- **What hurts:** creatures (see below), shots (ship cannons, scavenger bolts) and hitting a surface hard: 7 damage for every m/s above
  8 m/s straight into it, so boosting into rock at full speed hurts badly. Your own hand weapon never hits you.
- **Any hit** flashes red markers around the crosshair, pointing where it came from, and throws you out of a console
  seat.
- **At 0 health you die:** your hand is emptied (nothing is lost from the inventory), consumable effects end, and a
  "You have died" window waits over a heartbeat until you press **Respawn**. You come back 64 to 95 m from where you
  died, facing it, with 100 health and full suit power. The console has `kill` and `respawn` too.
- **Shots** lose the damage they deal: a block soaks up its remaining hit points, a player their remaining health, and a
  shot with damage left flies on through, so a strong shot can break several blocks in a row. Shot-up blocks drop
  nothing.

Every number here is a setting (see below), e.g. `sv_player_damage 0` for a world where nobody gets hurt, or
`sv_death_drop_items 1` to drop everything you carry where you die.

## Creatures

As in the original, creatures live on asteroids in single player. Every second, while you are near one, a mineral
parasite or a swarm egg may appear on an asteroid or mineral surface 16 m or more from you (at most one parasite per
16³ sector of an asteroid's blocks), and creatures more than 32 m away go.

- **Mineral parasite:** eats the asteroid block under it, one every 20 seconds (nothing drops), then crawls to the next
  asteroid or mineral block. Come within 4 m, or shoot it from within 16 m, and it charges at you; its bite does 10.
  20 health.
- **Swarm egg:** hatches when you come within 5 m or shoot it, oozing green goo and letting out three needles, one a
  second, then vanishes. 5 health.
- **Needle:** homes in on you at 7 m/s and stings for 5, trailing purple; gone after 4 seconds or on hitting rock.
- **Scavenger flyer:** the original has it but never lets one appear; `sv_scavengers 1` lets it, away from asteroids. It
  circles about 16 m off and fires yellow-green bolts (5 damage) when you are on foot, or at anyone who shoots it.

A creature's touch hurts once every 0.7 seconds and knocks you back at 5 m/s. Shots hit creatures within 1.5 times their
size and fly on with any damage left. Creatures heal a point every two seconds, drop nothing and are not saved. The
console counts them with `creatures` (and removes them with `creatures clear`); with `sv_cheats 1`, `spawn parasite`,
`spawn egg`, `spawn needle` or `spawn scavenger` puts one where you aim, and `tp aim` takes you close to what you aim at.

The creatures are drawn with the original's own models and textures, read from the bundled `.xnb` files.

## Settings (console variables)

The game plays like the original out of the box, but nearly every rule can be changed from the console (`` ` ``), the
way Source engine games do it. Settings starting with `sv_` are **world rules**: they are saved with the world, and a new
world starts with the original game's. Settings starting with `cl_` are **your preferences**: they are kept in
`config.cfg` and apply to every world.

| Command | What it does |
|---|---|
| `<setting>` | Show the setting's value, default, range and what it does, e.g. `sv_ricochet` |
| `<setting> <value>` | Set it, e.g. `sv_ricochet 1`, `sv_ship_max_speed 35`, `cl_fov 90` (`<setting> default` resets it) |
| `find <text>` | Find settings by name or description, e.g. `find ship` |
| `cvarlist [prefix]` | List every setting, or those starting with a prefix such as `sv_player` |
| `differences` | List what differs from the original game (and whether cheats were ever used in this world) |
| `toggle <setting>` | Flip an on/off setting |
| `reset <setting\|rules\|prefs\|all>` | Back to defaults: one setting, all world rules, all preferences or everything |
| `preset [name]` | `preset original` restores the original game's rules and controls; `preset assisted` turns on our flight assistant and the level camera aboard ships |
| `exec <file>` | Run the commands in a `.cfg` file from the config folder (`%APPDATA%\OpenCorneroids` on Windows) |
| `writeconfig` | Save your preferences to `config.cfg` now (they are also saved whenever one changes) |

Some of the things that can be changed:

- **Movement:** `sv_player_accel`, `sv_player_speed`, `sv_player_boost_speed`, `sv_player_max_speed`,
  `sv_boost_cost` and more (`cvarlist sv_player`).
- **Mining and drops:** `sv_mine_reach`, `sv_mine_time_max`, `sv_mine_time_min`, `sv_mine_time_per_power`,
  `sv_item_lifetime`, `sv_item_pickup_reach`.
- **Building and ships:** `sv_attach_distance`, `sv_bind_distance`, `sv_ship_max_speed`, `sv_ship_max_spin`,
  `sv_ship_coast_decel`, `sv_assist_force`.
- **Weapons:** `sv_ricochet` (off, as in the original), `sv_recoil_push` (off: recoil only kicks the weapon on screen),
  `sv_projectile_inherit_velocity` (off: shots fly at their own speed), `sv_projectile_limit` (512),
  `sv_projectile_pass_through` (on), `sv_projectile_hit_radius`.
- **Health and death:** `sv_player_damage`, `sv_health_regen`, `sv_armor_cap`, `sv_armor_scale`, `sv_impact_speed`,
  `sv_impact_damage`, `sv_damage_seat_eject`, `sv_respawn_min`, `sv_respawn_max`, `sv_death_drop_items` (ours, off).
- **Creatures:** `sv_creatures`, `sv_creature_spawn_interval`, `sv_parasites_per_sector`, `sv_parasite_eat_time`,
  `sv_creature_damage_scale` (ours), `sv_scavengers` (ours, off).
- **Preferences:** `cl_flight_assist` (`H`), `cl_fixed_camera` (`K`), `cl_fov`, `cl_sensitivity`, `cl_marker_min_blocks`.

**Cheats.** `noclip` (and the `N` key), `god`, `give`, `kit`, `heal`, `health`, `power`, `tp` and `speed` only work after
`sv_cheats 1`. Setting `sv_cheats 0` again ends noclip and god mode and resets the cheat settings. A world remembers that
cheats were used in it, and `differences` says so.

**Your own presets.** Put any commands in a `.cfg` file in the config folder and run it with `exec <name>`; lines may end
in `// comments`. A file called `autoexec.cfg` runs by itself each time a world starts, which is the place for rules you
want in every world. For example, an `arcade.cfg`:

```
// Faster ships, bouncing shots
sv_ship_max_speed 40
sv_ricochet 1
sv_recoil_push 1
```

## Worlds and Backward Compatibility

Worlds are saved to `user://worlds` (`%APPDATA%\OpenCorneroids\worlds` on Windows), one `.ocworld` file per world:
on F5, when returning to the menu, and when the window is closed. If that save fails (a locked file, a full disk), the
game stays open and asks before leaving or quitting without saving. A world always saves back to the file it was loaded
from, so a copied or renamed `.ocworld` file is a separate world and never overwrites the original.

The **Continue** menu lists both native `.ocworld` saves and legacy 2012 Corneroids `.wrl` SQLite 3 maps.
When loading a `.wrl` file:
- Player coordinates, Euler angles and boarded entity status are restored.
- 16×16×16 voxel chunks with 512-byte MSB-first bitmasks and chiral quarter-turn rotations are loaded into entity grids.
- Control consoles, 10-color group key mappings and cameras are bound.
- Saving a loaded `.wrl` map writes to `.ocworld`, preserving the native high-performance format.

In addition, mod packs can be placed as raw `.zip` archives directly in BYOA folders (e.g. `%APPDATA%\OpenCorneroids\byoa`
or the game's `byoa/` folder) without manual extraction. Uncompressed folders shadow same-named `.zip` archives.

For interoperability with Spacebox, structures can be imported and exported via the console (`` ` ``):
- `importship <path.sbs>`: reads a GZIP-compressed NBT `.sbs` structure file and spawns it in front of the astronaut.
- `exportship [ship#|name] <path.sbs>`: serializes the targeted or piloted ship to a Spacebox `.sbs` structure file.

A world is bound to the exact itemset it was made with (key, pack and content fingerprint). On load:

- an itemset with the same fingerprint loads silently (the saved pack is preferred);
- the same itemset from the saved pack with edited content loads with a warning if every block the world uses still exists;
- if some of those blocks are gone, the game asks first and turns them into empty space;
- if the saved pack is not installed, a same-key itemset from another pack is offered, but only after asking;
- if blocks still can't be restored once loaded (no texture for them, or outside the world), the game asks again and
  saves nothing until the player chooses to keep playing;
- a world whose itemset is not installed (or no longer parses) is refused, naming the itemset it needs. Damaged files
  show an error.

Round-trip checks:

```
<godot> --path game -- --itemset=auto --showcase --new-world=RoundTrip --quit-after-save
<godot> --path game -- --load=RoundTrip --screenshot=roundtrip.png
<godot> --path game -- --new-world=SurvivalTrip --give=10:5,20:3 --quit-after-save
<godot> --path game -- --load=SurvivalTrip --open=inventory --screenshot=inventory.png
<godot> --path game -- --new-world=ObjectTrip --give=56:1 --preview=56 --place --quit-after-save
<godot> --path game -- --load=ObjectTrip --focus=56 --screenshot=object.png
<godot> --path game -- --new-world=ShipTrip --demo-ship --launch --turn-ship=30 --place-on-ship=29 --quit-after-save
<godot> --path game -- --load=ShipTrip --screenshot=ship.png
<godot> --path game -- --new-world=ConsoleTrip --demo-ship --launch --quit-after-save
<godot> --path game -- --load=ConsoleTrip --open=console --seconds=0.5 --screenshot=console.png
```

Flight and space-placement checks:

```
<godot> --path game -- --itemset=auto --demo-ship --launch --pilot=chase --hold=move_forward+move_left:2.5 --seconds=2.0 --screenshot=console_flight.png
<godot> --path game -- --itemset=auto --demo-ship --launch --flight-assist=on --pilot=chase --hold=move_forward+ship_yaw_left:2 --seconds=1.8 --screenshot=assist_flight.png
<godot> --path game -- --itemset=auto --demo-ship --launch --preview-space=28 --seconds=0.5 --screenshot=space_attach.png
<godot> --path game -- --itemset=auto --preview-space=28 --place --seconds=0.5 --screenshot=space_new_ship.png
```

## Tests and tools

```
dotnet test OpenCorneroids.sln
dotnet run --project tools/OpenCorneroids.ContentDump
```

The BYOA golden tests skip when `byoa/` is empty.
