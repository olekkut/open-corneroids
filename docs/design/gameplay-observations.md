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

## 2a. Rules matched to the original

- **Controls, movement and boarding:** boost costs 20 power/s; drilling needs a drill in hand; LMB acts with what is
  held and releasing RMB empties the hand; the player rides along with a nearby ship, with optional fixed camera when
  aboard (`K`).
- **Mining, drops, crafting and starting kit:** drilling takes 0.8 s minus 0.1 s per spare power point (down to 0.3 s)
  within 3 m, with grey sparks (yellow when too weak); mined blocks float for 30 s and fly to a player within 1.5 m;
  crafting uses only the main inventory (left click crafts one, right click half of what can be afforded); the starting
  drill, crafting table and ore furnace sit in endless slots. One intentional deviation: 1.2.0 ignores an extractor in
  the starting kit, but nothing crafts one, so it is given as an item, as the v1.0.5 footage shows.
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
- **HUD and in-world windows:** classic raised light-grey bevelled panels (`#BEBEBE` face, 3D bevels, broken caption
  groove), 8-slot hotbar with top-left stack counts and magnetic-boot aboard indicator, contextual crosshair reticles and
  prompts (`"F to edit"`, `"E to use"`), `F1` Help & Controls window, `F3` toggleable debug telemetry, and `` ` ``
  developer/debug console (ours: live `mode creative|survival`, `speed`, `tp`, `give`, `noclip`, `god`, `heal`, `ships`,
  `spawnship`, `help`).

Still to match the original: health and armor damage/respawn, weapon visual-only recoil and pass-through damage,
consumables details, enemies, and classic menu/UI polish.

## 3. Our additive rules and remaining placeholders

Tunable, and marked so in code.

- **Flight assistant (opt-in, `H`):** solves throttles 0..1 across powered thrusters (with a 30× force multiplier) to
  reach requested linear and angular velocities with optional inertial dampeners (`G`).
- **Suit health and armor:** health is 100 plus worn `healthRating`; armor is the sum of `armorRating`. Damage, death
  and respawn come in the next milestone.
- **Weapons and projectiles (placeholder parts still to align):** hand weapons currently apply physical kickback,
  `ricochetAngle` bounce, and inherited shooter velocity; those will be replaced by visual-only recoil and pass-through
  block damage in the combat milestone.
- **Asteroid and ship markers:** one green marker per face-connected asteroid lump of at least 64 blocks with distance
  and mass, and white markers for ships.
- **Turning single blocks:** turnable single-cell blocks store their orientation per cell (format version 5 when any
  exist) and draw the facing arrow in the preview.
- **Inventory:** the 8-slot hotbar plus four backpack rows of eight (40 slots).
