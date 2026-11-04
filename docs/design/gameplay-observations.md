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

## 2. Decisions (2026-11-04)

- **Ship controls: both.** The original's console button binding, and our automatic flight assist, selectable.
- **Hotbar: 8 slots**, like the original.
- **Original art from the user's install:** reading `.xnb` textures (font, HUD sprites, crosshairs, models later) is
  allowed, at run time from BYOA, never committed, with our own drawing as the fallback.
- **Theme:** an original interface theme in the spirit of the original (light grey, bevelled, compact), not a copy.
- **Order:** suit stats and HUD/navigation first, then weapons and combat, single-block rotation, console binding,
  crafting categories, remaining block types, menus and options, then enemies, sound and multiplayer.

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
- **Held item:** the selected tool or weapon is drawn in the view, made from its icon pushed out `modelDepth` pixels.
- **Inventory:** the 8-slot hotbar plus four backpack rows of eight (40 slots). Older saves (36 slots) load unchanged;
  their ninth hotbar slot becomes the first backpack slot.
