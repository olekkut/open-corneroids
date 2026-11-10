# Gameplay observations and decisions

What the original game does, as seen from gameplay and data, and the design decisions taken from it. Footage stills are
not stored in the repository.

Each observation says where it comes from: **[footage]** for gameplay video stills (YouTube, Alpha v1.2.0 title
screen, shared by the project owner on 2026-11-04), **[data]** for the itemset XML, **[files]** for asset file names.
Anything without a source is our own rule.

## 1. Observations

### 1.1 HUD
- **[footage]** Two bars above the hotbar: **Health** (red, `100 / 100`) and **Power** (purple, `100 / 100`). Power
  drops while the player uses equipment (seen at `68 / 100` while drilling) and comes back over time.
- **[footage]** The hotbar has **8** slots, the stack count drawn in the slot's top-left corner.
- **[footage]** The held item is drawn in first person (drill, pistol).
- **[footage]** Asteroids far away carry a green marker: a diamond bracket with the distance and the asteroid's mass,
  e.g. `2260 m / 8456 tn`. Other players carry a white one with distance and name. Markers fade with distance.
- **[footage]** A message log at the top left (server messages in green, chat in white).
- **[footage]** Drifting dust specks fill space, also behind the menus. **[files]** `dust`.
- **[files]** `radar`, `target`, `damageIndicator`, `fontImage`, `litterbin`, `rotation`, `gridCell`,
  `characterBackground`, and crosshairs `default`, `bindBlocks`, `bindCamera`, `gunConsole`.

### 1.2 Character and inventory
- **[footage]** A **Character** window beside the inventory: seven equipment slots (helmet, shoulders, chest, legs,
  arms, feet, power source; **[data]** the `slot` values 0–6 of wearables) and three stats: `Health: 100`,
  `Armor: 0`, `Power recharge: 5`, with nothing worn.
- **[footage]** The inventory has a trash can, item names as tooltips, and counts in the top-left corner.

### 1.3 Crafting
- **[footage]** A station first shows categories. Blocks: Plating, Power, Thruster, Console, Cannons, Lights, Doors,
  Chests, Windows, Crafting, Cameras, Gun Con. Player items: Armors, Drills, Weapon, Buffs, Material. These match the
  item kinds in the itemset XML. Then an icon grid; hovering shows the item's stats (drill power, colour radius, mass,
  health, size, power use, buttons) and the recipe with have/need, short ingredients in red.
- **[footage]** One recipe in the video (Triple Iron Drill: 3 Powerful Iron Drill + 15 Titanium ingot) differs from
  vanilla 1.2.0's data (8 Iron ingot): the video used another itemset or version. The data wins.

### 1.4 Building and ships
- **[footage]** Single-cell blocks are rotated when placed; the preview shows the facing with arrows.
- **[footage]** Consoles are set up by selecting blocks and binding them to the console's buttons ("Choose blocks you
  wish to control! Press Edit again to assign keys!"). **[data]** consoles have a `buttons` count.

### 1.5 Menus and enemies
- **[footage]** Main menu: Singleplayer, Multiplayer, Options, Exit game. World picker: list, selected-world details,
  Create new, Remove selected, Back. Grey bevelled panels and a pixel font.
- **[files]** Enemy models and textures: `scavenger`, `needle`, `mineralParasite`, `swarmEgg`; player model
  `astronaut`.

### 1.6 "How To Play Corneroids - HD REDUX" (YouTube `_8CWw365hUU`, v1.0.5 with DarthNox's itemset)

Watched through its narration and stills taken from it (stored outside the repository). **[footage]** unless noted.

- **Menus.** Main menu: Singleplayer, Options, Exit game (1.2.0 adds Multiplayer). Options: Gameplay, Graphics,
  Controls, Network, Sounds, Back. *Gameplay*: Draw target squares, View bobbing, Fixed camera when aboard, Show hints
  (checkboxes), Gamma (slider, 0-100, maxed by default; the narrator suggests 3-5). *Graphics*: Resolution (list),
  Fullscreen, V-Sync, Lighting (checkboxes), SSAO (list: High...), View distance (list: "Far, 150"...), Field of view
  (slider, 90). Singleplayer: world list, Create new (Name, Author, Itemset list; Ok, Cancel), Remove selected, Back.
  A new world shows a welcome window ("Welcome to Corneroids!", three special blocks explained, "You can reopen this
  screen by pressing F1", Start!).
- **HUD.** A white boot icon right of the hotbar shows the player is "connected" to an asteroid or ship and moves with
  it. Asteroid markers show distance and mass; ships get an orange marker labelled "ship".
- **Asteroids move** like ships; the bigger the asteroid, the rarer its minerals (the narrator: look for big ones).
- **Inventory.** Character window: power sources and armor. The starting drill, crafting table and ore furnace sit in
  endless slots on the right of the inventory; a trash can above them. Stacks of up to 64, some to 256.
- **Flashlight** is on at the start; T toggles it.
- **Mining.** Grey sparks when the drill can mine the block, yellow when it is too weak.
- **Placing.** The preview shows the block itself with white corner marks, a red arrow with a white head along its up,
  and a red line from its bottom to the object it will be attached to. Blocks can be placed in empty space: attached to
  the nearest object, or, with no red line, as a new object floating on its own: that is how a ship is started. Left
  click places; right click empties the hand ("so you can get a better view").
- **Crafting.** E on a station; categories, then recipes with a mouse-over of what they need; items on the hotbar
  can't be used; left click crafts one, right click half.
- **Power.** F on a power block shows its network ("power balance of 40"). Cables carry power between touching blocks
  and look grey when unpowered; powered blocks light up.
- **Thrusters** blow out of their widest opening; R orients them.
- **Consoles.** E sits you down (WASD stop working, you can only look); F on the console gives the binding tool, F
  again opens the "Control block" window: coloured groups, each "= None" until a key is set, plus "Set camera" and
  Back. Clicking a block with the tool cycles it through the colours; right click puts the tool away.
- **Crashes** do no harm "at this moment": things bounce off in a random direction.
- **Enemies.** A purple parasite eats asteroids and the player and chases you once shot; a spore spawner fires three
  homing projectiles and dies.

## 2. Decisions

- **Ship controls: both.** The original's console colour-group key binding is the default, and our automatic flight
  assistant is an opt-in toggle (`H` or `--flight-assist=on`).
- **Hotbar: 8 slots**, like the original.
- **Original art from the bundled data:** reading `.xnb` textures (font, HUD sprites, crosshairs, models later) is
  supported, with our own drawing as the fallback.
- **Theme:** an interface theme faithful to the classic light-grey bevelled look.
- **Original by default, everything tunable.** Rules and our additions are console settings (`sv_` world rules saved
  with each world, `cl_` preferences in `config.cfg`) whose defaults are the original's behaviour. New mechanics, and
  our departures from the original, land behind a setting rather than a fixed number; cheats need `sv_cheats 1`.

## 2a. Rules matched to the original

- **Controls, movement and boarding:** boost costs 20 power/s; drilling needs a drill in hand; LMB acts with what is
  held and releasing RMB empties the hand; the player rides along with a nearby ship, with optional fixed camera when
  aboard (`K`). A block grid that comes to overlap the player (a ship closing on them, rock their ship carries them
  into, the ship they ride) pushes them out, and only the part of their velocity going into it is removed: no bounce.
  Their velocity counts relative to the ship they ride, so rock does not hand them the ship's speed; a ship they do
  not ride shoves them at its speed.
- **Mining, drops, crafting and starting kit:** drilling takes 0.8 s minus 0.1 s per spare power point (down to 0.3 s)
  within 3 m, with grey sparks (yellow when too weak); mined blocks float for 30 s and fly to a player within 1.5 m;
  the inventory is the 8-slot hotbar plus a 6 × 5 main inventory (`sv_inventory_slots` 30), and pickups go into the
  first slot that takes them, hotbar first (`sv_inventory_fill_hotbar_first`), with what the hotbar can't take going
  into the main inventory only if all of it fits there (`sv_inventory_pickup_whole`); crafting uses only the main inventory (left click crafts one, right click half of what can be afforded); the starting
  drill, crafting table and ore furnace sit in endless slots (each click puts one more on the cursor). Item windows move stacks
  through one stack held on the cursor and drawn under the mouse: with an empty hand left click takes the whole stack
  and right click half (rounded down, a single item whole); holding a stack, left click puts it into an empty slot,
  tops up the same item and keeps the rest, or swaps; right click puts one into an empty slot or onto the same item.
  A press counts only while the other mouse button is up (`cl_item_click_needs_release`). A click that is not on a
  slot, outside the windows or on a window's frame (`cl_item_toss_on_window`), throws the held stack along the view
  at 3 m/s (`sv_item_toss_speed`), closing the
  window throws it at 2 m/s (`sv_item_close_toss_speed`); thrown stacks slow by ×0.98 per 1/60 s (`sv_item_drag`) and
  can't be picked up for 2 s (`sv_item_toss_pickup_delay`). The original's right click on a different item destroys
  one held item; that is not reproduced (it does nothing). One intentional deviation: 1.2.0 ignores an extractor in
  the starting kit, but nothing crafts one, so it is given as an item, as the v1.0.5 footage shows.
- **Using blocks and chests:** `E` (use) and `F` (edit) reach 1.7 m from the eye to the block (`sv_use_reach`), for
  doors, consoles, crafting stations, chests and power blocks alike; the `"E to use"` and `"F to edit"` prompts show
  only within that reach. `E` on a container block opens the player's inventory and the chest's `"Container"` window
  (5 × 4 for the default container block), 50 px apart; `E` or `Esc` closes them, and the item-window click rules above
  move stacks between hotbar, inventory and chest. Anyone can open any chest. A broken chest (mined, shot or eaten)
  throws every stack out from the block's centre in a random direction at 1–4 m/s (`sv_container_spill`,
  `sv_container_spill_speed_min`/`_max`), out of reach of pickup for 1 s (`sv_container_spill_pickup_delay`). Chests
  holding something are saved with the world and follow a launched structure onto its ship. The original restores only
  one chest per ship or asteroid after a reload; that is not reproduced (every chest comes back). Ours: a chest that
  became smaller after an itemset change (or whose block is gone) keeps what fits; the rest goes into the survival
  inventory, and only what does not fit there floats where the chest was (like any floating item, for
  `sv_item_lifetime`). The original empties such a chest. Ours: chests can't be opened in creative mode; breaking one
  there still throws its contents out, to be picked up into the survival inventory. Ours: the left window beside a chest
  is the normal inventory panel with its hotbar row; the original shows a 6 × 5 window captioned `"Player"` there.
- **Empty-space placement and ship building:** aiming into empty space previews the block at `sqrt(0.25 * size²) + 2` m,
  attaching to the nearest object within 16 m (drawing a `0.1 × 0.1` red beam to its centre of mass) or starting a new
  floating ship beyond 16 m.
- **Power networks:** face-adjacent powered blocks form a network whose balance is the signed sum of their `power`
  values; the network is active all-or-nothing when `balance >= 0` (`F` opens the "Power network" popup showing block
  count and power balance). Losing power immediately unmounts any pilot seated on that network.
- **Consoles, gun consoles, cameras and ship physics:** `F` on a console equips its binding tool (cycling thrusters and
  cannons through up to 10 colour groups within 3 m, drawing depth-tested 8-corner selector brackets around each bound
  block's full footprint including thruster exhaust space; gun consoles bind cannons only and aim them along the seated
  player's view), and `F` again opens the classic bevelled "Control block" window for key assignment and camera binding.
  By default, seated flight uses those console bindings with raw itemset thruster `force`, off-centre torque, speed cap
  20 m/s, spin cap 1.5 rad/s, and coasting deceleration (0.25 m/s² linear, 0.03 rad/s² angular) when no thruster pushes,
  replacing the hotbar with the 3D green velocity cone and `Speed: X Units/s` readout.
- **Camera blocks:** "Set camera" in the "Control block" window puts the camera tool in hand; `F` on the same console
  with either tool opens the window again. The tool draws the bound camera in magenta (scale 1.03), only while it is in
  hand. A seated pilot whose console has a powered camera sees from the camera block's centre; the camera block is
  see-through, so the hull blocks around it keep their faces and the view is not walled in. The mouse turns the camera
  itself (yaw wraps round, pitch stops at `cl_camera_pitch_limit`, 90°), not the seat's look, which waits unturned and is
  what the speed cone follows and what the pilot faces on standing up. Each camera keeps its own look while the world
  is running, also across standing up and sitting down again; looks are not saved, so after a reload every camera looks
  straight out of its front, as in the original. Ours: `cl_camera_eye_forward` (0) moves the eye that far past the front face when the cell there is free,
  `cl_speed_cone_follows_view` (0) turns the cone with the camera, and only cameras on the console's own ship or
  structure can be bound (the original takes any camera block in reach, even on another object).
- **HUD and in-world windows:** classic raised light-grey bevelled panels (`#BEBEBE` face, 3D bevels, broken caption
  groove), 8-slot hotbar with top-left stack counts and magnetic-boot aboard indicator, contextual crosshair reticles and
  prompts (`"F to edit"`, `"E to use"`), `F1` Help & Controls window, `F3` toggleable debug telemetry, and `` ` ``
  console (ours: every setting by name, `find`, `cvarlist`, `differences`, `reset`, `preset`, `exec`, `mode`, `ships`,
  `spawnship`, `help`; cheats behind `sv_cheats 1`: `speed`, `tp`, `give`, `kit`, `noclip`, `god`, `heal`).
- **Weapons:** no ricochets, recoil that only moves the held weapon (back by its `pushback`, sliding forward at 0.75
  units/s), and shots that do not take the shooter's velocity; at most 512 shots in flight (no new shot past it). A
  shot loses the damage it deals (a block's remaining hit points, a player's remaining health) and flies on with the
  rest. A hand weapon's shot starts 0.3 m right of and 0.2 m below the eye and 1 m along the aim
  (`sv_shot_forward_offset`); its spread adds `spread` (0 to 0.5) times the next of a fixed seeded list of 256
  directions, one place in the list per weapon, without normalising, so shot speed varies by up to ±spread
  (`sv_weapon_spread_mode 0`). A missing `reloadTime` is 1000 ms and none is under 50 ms (ship guns too); `pushback`
  is held within 0 to 1. A weapon with a negative `powerUsage` (the Heavy Plasma Launcher) always fires and gives that
  much suit power per shot (`sv_weapon_negative_power 1`). Each of our former behaviours is still there behind a
  setting (`sv_ricochet`, `sv_recoil_push`, `sv_projectile_inherit_velocity`, `sv_projectile_pass_through 0`,
  `sv_weapon_spread_mode 1`, `sv_weapon_negative_power 0`).
- **Ship contacts:** a ship that hits rock or another ship does not bounce (`sv_ship_restitution 0`), and contacts do
  no damage to blocks.
- **Itemset defaults:** a torch without `burningTime` burns for 10 s (never less than 0), a light without `radius` has
  radius 1, and a container's grid is 1 by 1 when its size is missing, at most 7 by 5.
- **Torches:** releasing the left button with a torch in hand throws one, whatever is aimed at, if the suit has the
  torch's `powerUsage` (25) in power; one torch leaves the stack and the power is spent (by the weapon rule: a
  negative `powerUsage` always throws and gives power, `sv_weapon_negative_power`). It flies from the eye along
  the view at 7 m/s without the thrower's velocity, slows by 5 m/s² to a stop about 4.9 m away, slides along what it
  hits without bouncing (a box of half size 0.2), and is pushed but not carried by a moving ship. Its light has the
  torch's colour and a radius of ln(milliseconds left), flickering by up to 10%; it burns for its `burningTime`, can't
  be picked up, is not saved, and any number burn at once. Its sprite is drawn within 32 m. A torch in hand lights the
  area around the player with its own light (`sv_torch_held_light`).
- **Beacons:** B on foot puts the beacon tool in hand (the hotbar selection empties; a slot key, the wheel or the right
  button put it away). The nearest beacon within 6 m gets a red box and "Click to remove the beacon!"; a click removes
  it at once. With none that near, "Click to create a new beacon!": a click opens "Beacon" / "Beacon name:" (20
  characters, Ok / Cancel, the game keeps running) and Ok with a name puts a beacon 2 m ahead along the view. Beacons
  are orange markers with distance and name, drawn whether or not target squares are on, saved with the world (up to
  65536 per world), and imported from original `.wrl` worlds. Settings: `sv_beacon_reach`, `sv_beacon_place_distance`,
  `sv_beacon_name_max`.
- **Health, damage and death:** health 100 plus worn and timed bonuses, regenerating 0.5/s; armor (capped at 60)
  takes off ln(armor + 1) / 6; shots hit within 0.735 m of the player's centre; hitting a surface above 8 m/s costs 7
  per m/s; any hit shows red markers for 0.4 s and throws the player out of a seat; at 0 health the hand empties,
  consumable effects end and "You have died" / Respawn shows over a heartbeat; respawn 64–95 m away up every axis,
  facing the death point, with 100 health and power, inventory kept.

- **Creatures:** single player only; a spawn cycle every second near asteroids puts mineral parasites (up to 4 tries)
  or swarm eggs (2) on asteroid and mineral surfaces 16 or more units from the player, at most one parasite per 16³
  sector of blocks, none within 8 of another; creatures beyond 32 go. Parasites eat a block per 20 s (no drop), charge a
  player within 4 (or one who shoots them from within 16) and bite for 10; eggs hatch three homing needles (sting 5,
  4 s life) when a player comes within 5 or shoots them; touch damage every 0.7 s with a 5 u/s knockback; shots hit
  within 1.5 × a creature's radius; creatures regenerate a point per 2 s, shrink away in 0.2 s and burst in red, and
  drop nothing. Models and textures are the original's `.xnb` files. The scavenger flyer is coded but never spawns in
  the original; ours behind `sv_scavengers`.

- **Menus and options:** the main menu ("Singleplayer", "Multiplayer", "Options", "Exit game" over a forward flight
  through stars and dust), "Choose world" and "Create new world", "Options" with Gameplay (Draw target squares, View
  bobbing, Fixed camera when aboard, Show hints, Gamma 0–50 applied on Back), Graphics (Resolution, Fullscreen, V-Sync,
  Lighting, SSAO Off/Low/High, View distance Short/Medium/Far, Field of view 45°–135° default 90°, applied after "Save
  changes?") and Controls (Mouse sensitivity 0–3 default 0.5; key bindings with "Press any key", a duplicate key moves);
  the in-game "Game menu" (Options, Save and exit, Back to game); the welcome window on every load and on F1; the death
  popup "Oh noes!". The original has no audio; its Network and Sounds buttons did nothing and are left out.

Still to match the original: consumables details, the spin a contact gives ships, and classic menu/UI polish.

## 3. Our additive rules and remaining placeholders

Tunable, and marked so in code.

- **Flight assistant (opt-in, `H`):** solves throttles 0..1 across powered thrusters (with a 30× force multiplier) to
  reach requested linear and angular velocities with optional inertial dampeners (`G`).
- **Menus, ours:** settings save as they change (the original saves on exit); Lighting is
  on by default (the original's is off) and Fullscreen too; View distance sets how far blocks are drawn (8 × the
  original's band); gamma 0–50 maps to brightness 0.6–1; the Gameplay page adds Flight assistant and Welcome window on
  load; Controls adds our own actions and a Defaults button; Create new world adds seed, texture packs and creative and
  drops the original's unused Author field; Multiplayer is greyed out.
- **Creatures, ours:** they are not saved; `sv_creature_damage_scale` scales their damage to the player; the console's
  `creatures`, `spawn` and `tp aim` are ours.
- **Death extras:** `sv_death_drop_items` drops the inventory where the player died (off; the original keeps it), and a
  world saved by a dead player loads with them alive where they lay. The console's `kill` and `respawn` are ours.
- **Weapons and projectiles (opt-in):** physical kickback (`sv_recoil_push`, scaled by `sv_recoil_scale`),
  `ricochetAngle` bounces (`sv_ricochet`, up to `sv_ricochet_bounces`), inherited shooter velocity
  (`sv_projectile_inherit_velocity`), an even spread cone at full speed (`sv_weapon_spread_mode 1`) and weapons whose
  negative `powerUsage` costs its size (`sv_weapon_negative_power 0`), all off by default.
- **Player push-out:** the push moves the body along one of the grid's axes, the shortest way to a spot free of every
  grid (the original averages a sphere's contact normals), and at most `sv_player_pushout_max` (2.5 m) per frame; 0
  turns pushing off. The player is pushed out of a ship before boarding it, along the ship's motion when that way out
  is within the limit, so a fast ship shoves them ahead of it instead of swallowing them (one that closes on them by
  more than the limit in one frame can still pass over them); a move that would end inside a grid it started outside
  goes back to the last free spot; a player still inside rock after the push is moved to the nearest free spot.
- **Ship contacts:** each contact multiplies a ship's spin by 0.5 (`sv_ship_contact_spin_damping`) instead of turning
  it by contact torque as the original does; a bounce can be set with `sv_ship_restitution`.
- **Asteroid and ship markers:** one green marker per face-connected asteroid lump of at least 64 blocks
  (`cl_marker_min_blocks`) with distance and mass, and white markers for ships.
- **Turning single blocks:** turnable single-cell blocks store their orientation per cell (format version 5 when any
  exist) and draw the facing arrow in the preview.
- **Inventory:** `sv_inventory_slots` sets the main inventory's size (read when the world loads); a world saved before
  the setting existed keeps its 40 slots (8 + 32) as its own rule, so nothing moves. `sv_inventory_pickup_whole 0` lets
  the main inventory keep the part of a pickup that fits; `sv_inventory_fill_hotbar_first 0` tops up matching stacks
  anywhere before using an empty slot. Items added while loading (a stack past the end of a smaller inventory, worn
  items that can't be worn) keep the part that fits either way.
- **Torches, ours:** a throw without enough power says so on the HUD (the original does nothing);
  `sv_torch_inherit_velocity` adds the thrower's velocity, `sv_torch_limit` caps how many burn (the oldest go out), and
  only the `cl_torch_light_limit` (64) torches nearest the camera cast light. Lights use the same range as light
  blocks (1.5 × the radius, at least 2 m).
- **Beacons, ours:** `cl_beacon_markers 0` hides the beacon markers; their distance label uses the same metres as the
  other markers. Names are trimmed, so a name of only spaces puts no beacon (in the original only an empty name
  does).
- **Item windows:** our trash slot stays beside the original's click rules: a held stack put on it is thrown away, and
  clicking it with an empty hand takes the last thrown stack back onto the cursor. `cl_item_toss_on_window 0` makes a
  click on a window's own frame do nothing instead of throwing the stack, and `cl_item_click_needs_release 0` lets a
  press act while the other mouse button is held. Thrown items pass through blocks instead of sliding along them. A
  stack still on the cursor when the world is saved (with a window open, or quitting from one) is saved as if put back
  into the inventory.
