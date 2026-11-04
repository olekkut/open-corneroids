# Gameplay observations and decisions

What the original game does, as seen from the outside, and the decisions taken from it. Clean-room rules apply:
only public gameplay footage, the itemset data and the names of files in a user's own install are used. Never the
original's code or binaries (see `byoa-asset-loading.md` §9). Footage stills are not stored in the repository.

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

## 2. Decisions (2026-11-04)

- **Ship controls: both.** The original's console button binding, and our automatic flight assist, selectable.
- **Hotbar: 8 slots**, like the original.
- **Original art from the user's install:** reading `.xnb` textures (font, HUD sprites, crosshairs, models later) is
  allowed, at run time from BYOA, never committed, with our own drawing as the fallback.
- **Theme:** an original interface theme in the spirit of the original (light grey, bevelled, compact), not a copy.
- **Order:** suit stats and HUD/navigation first, then weapons and combat, single-block rotation, console binding,
  crafting categories, remaining block types, menus and options, then enemies, sound and multiplayer.

## 2a. Rules matched to the original

Where the original's behaviour is known, it replaces the guesses below. Done so far (2026-11-04): controls and
movement (spec 1.1–1.2). The boost costs 20 power a second, not 10; drilling needs a drill in hand; the left button
acts with what is held and releasing the right one empties the hand; the player rides along with a ship they are near.
Mining, drops, crafting and the starting kit (spec 1.5, 1.6, 1.10) too: drilling takes 0.8 s less 0.1 s per point of
power to spare (never under 0.3 s), within 3 m, with grey sparks (yellow when the drill is too weak); a mined block
floats where it was for 30 s and flies to a player who comes within 1.5 m with room for it; crafting uses only the
main inventory (craft one, or half of what can be afforded); the starting drill, crafting table and ore furnace are
endless slots and the rest of the kit goes into the main inventory. One deviation: the spec has 1.2.0 ignore an
extractor in the kit, but nothing crafts one, so it is given as an item, as the v1.0.5 footage shows.
Still to follow the spec: health and armor, weapon recoil and spread, ship consoles and power networks, consumables.

## 3. Our rules where the footage is silent

Tunable, and marked so in code.

- **Suit power** (100 max) recharges at the shown `Power recharge` rate, read as per second: 5 with nothing worn, plus
  the `powerRechargeRate` of what is worn and of running buffs. **[data]** Vanilla drills have `powerUsage` 0, so
  drilling is free; weapons have 1–3 (per shot, presumably) and come with combat. Until then the drain the footage
  shows (`68 / 100`) is given to **boosting** (Shift): 10 power per second while thrusting. With too little power the
  boost stops.
- **Health** is 100 plus the `healthRating` of worn items; **armor** is the sum of `armorRating`. Nothing damages
  the player yet; that comes with combat.
- **Consumables** (RMB with one selected): instant ones (`type` 1) add their health and power at once and are refused
  when they would do nothing; timed ones (`type` 0) add their armor and power (read as recharge) for `duration` ms, and
  using the same one again restarts it rather than stacking. Wearables are put on with RMB too, or in the character
  window.
- **Asteroid markers:** one per face-connected lump of at least 64 blocks, at its centre, with distance and mass (the
  sum of its blocks' `mass`, shown as tonnes). Found again a few seconds after the world changes. Ships get a white
  marker with their name and mass. A marker hides while the player is inside what it marks.
- **Weapons:** with a weapon selected the primary button fires instead of drilling: one shot per `reload` ms, costing
  the weapon's `powerUsage` in suit power (its size: the Heavy Plasma Launcher's is -25), along the view turned by up
  to `spread` radians. Each shot pushes the shooter back by `pushback` × projectile speed × 0.05 m/s.
- **Projectiles** fly at their `speed` for their `lifetime`. A shot meeting a surface at less than `ricochetAngle`
  radians glances off (at most three times); otherwise it hits, and a block breaks once the damage it took reaches its
  `health` (health 0: the first hit). Shot-apart blocks drop nothing. Damage is not saved. Shots carry the shooter's
  velocity.
- **Ship guns** fire along their long axis (local +Y, turned with the block) from `barrelLength` beyond the model's
  centre, each on its own `reload`. Until console button binding exists, the pilot's primary button fires all of a
  ship's guns.
- **Turning single blocks:** a single-cell block can be turned when its top or bottom texture differs from its walls,
  or it is a gun; plain cubes can't (they would look the same). The turn is kept per cell beside the block grid, saved
  with the world and its ships (format version 5 only when there are any), carried along by a launch, and dropped
  when the cell changes. A turned block's faces show the textures the turn brings round, kept upright with it. The
  preview draws an arrow along the block's up axis, like the original's placement arrows. Single-cell guns fire along
  that axis too.
- **Held item:** the selected tool or weapon is drawn in the view, made from its icon pushed out `modelDepth` pixels.
- **Inventory:** the 8-slot hotbar plus four backpack rows of eight (40 slots). Older saves (36 slots) load unchanged;
  their ninth hotbar slot becomes the first backpack slot.
