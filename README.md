# Open Corneroids

A clean-room reimplementation of Corneroids in Godot 4 (C#). It ships no original content:
players bring their own copy of the game and any community mods (BYOA).

## Requirements

- Godot Engine – .NET 4.7.2 (the standard build can't run C#)
- .NET SDK 8 or later

## Content

Put Corneroids installs, itemset mods and texture packs, unmodified, in one of:

- `byoa/` in this repository (gitignored; used during development)
- `%APPDATA%\OpenCorneroids\byoa\` (created on first run, with a README)
- `byoa/` next to the game executable

Folders are recognised by their contents. See `docs/design/byoa-asset-loading.md`.

## Run

Open `game/project.godot` in Godot .NET and press Play, or from the repository root:

```
dotnet build game/OpenCorneroids.csproj
<godot-dotnet-exe> --path game
```

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
| `--place` | With `--preview`: place the previewed block there by the normal rules (survival spends the item), e.g. with `--quit-after-save` to save a placed object without a mouse |
| `--focus=<itemId>` | Move the camera close to the first placed multi-block of that item (from the side it already looks at), framing its footprint; for checking a showcase or loaded object by eye |
| `--open-doors` | Open every door once the world is up, and keep doors from closing by themselves; a door that would open into the player stays shut (warning in the console); for screenshots |
| `--demo-ship` | In a new world, build a small ship near the spawn, at the nearest spot with 40 cells of open space in front of it (−Z, where it faces), from the itemset's own blocks, attached to nothing: a hull of the first plain opaque block, the first console in its top front, the strongest power supply on top and two thrusters on each face (exhaust outwards; on the top and bottom on opposite diagonals, so it can pitch and roll both ways), skipping what the itemset lacks. Hull blocks don't conduct power, so the weakest non-supplying single-cell power block (a cable) replaces the hull on the shortest paths from the power block to the console and every thruster. The camera moves to look at it |
| `--launch` | Press F on a console of the static world: the `--demo-ship` console, or else the one nearest the camera, launching its structure as a ship |
| `--turn-ship=<degrees>` | Turn every ship about the world's up axis through its centre of mass (after `--launch`), to check that aiming, the ghost and building follow a ship's own axes |
| `--place-on-ship=<itemId>` | Select that block (survival: given one) and place it on the first face of a ship, seen from the camera, where it fits, by the normal rules |
| `--preview-ship=<itemId>` | As `--place-on-ship`, but only aim there, so the highlight and the ghost show on the ship; for screenshots |
| `--pilot` / `=seat` / `=chase` / `=fixed` | Sit at the first ship's console (after `--demo-ship --launch`, or in a loaded world) and pilot it, with the camera on the seat, chasing the ship from behind, or (`fixed`, for before/after screenshots) left where it was. Applied after the hooks above |
| `--dampeners=on` / `=off` | Inertial dampeners for the pilot (default on) |
| `--hold=<action>[+<action>...]:<seconds>` | Hold these input actions down for that many seconds of game time, as if the keys were pressed (the screenshot lock ignores only the real keyboard). Repeat the argument for a sequence. Actions: `move_forward`, `move_back`, `move_left`, `move_right`, `move_boost` (walking and piloting), `move_up`, `move_down` (walking only), `ship_up`, `ship_down`, `ship_pitch_up`, `ship_pitch_down`, `ship_yaw_left`, `ship_yaw_right`, `ship_roll_left`, `ship_roll_right`. Toggles (dampeners, view) are not held: use `--dampeners`, `--pilot=`. `move_up`/`move_down` held while piloting log a warning (they do nothing there). Game time counts each frame up to 0.25 s, as the ship physics does, so a loading hitch does not use up a hold |
| `--frames=<n>` | Take the `--screenshot` at frame n (default 240), or with `--quit-after-save` run that many frames before saving and quitting (so held controls and flying ships are in the save). The screenshot run's frame rate is uncapped, so the moment n frames is depends on the machine: prefer `--seconds` |
| `--seconds=<s>` | As `--frames`, but after s seconds of game time (the clock `--hold` counts in), the same moment on any machine. Wins over `--frames`. The log says the frame and time it fired at |
| `--open=inventory` / `=crafting` | Open that window once the first world (new or loaded) is up (survival). `crafting` shows the first crafting-table station without aiming at one |
| `--give=<itemId>:<count>[,<itemId>:<count>...]` | Add items to the survival inventory of the first *new* world, after the starting kit. Not applied to loaded worlds or to worlds picked later from the menu |
| `--seed=<n>` | Generator seed for a world started from the command line (default 2012) |
| `--new-world=<name>` | Create a world with this name (from `--itemset`, default `auto`, with `--textures`, `--creative`, `--seed`) and save it at once, replacing a save of the same name |
| `--load=<world name>` | Load a saved world instead of showing the menu. `--itemset`, `--textures`, `--creative`, `--showcase` and `--seed` are ignored: the save decides |
| `--quit-after-save` | Save the world started from the command line, then quit (no screenshot). Exit code 1 if nothing was saved. With `--frames`, the world runs that long first |
| `--screenshot=<file.png>` | Save a screenshot after 240 frames (or `--frames`), then quit (without saving the world). Mouse and keyboard are ignored meanwhile, so stray input can't change the picture. The log has the frame rate measured over the run |

The game starts in survival: you get the itemset's starting kit (`defaultItems`) in a 36-slot inventory whose
first nine slots are the hotbar.

Controls (survival): mouse look, WASD move, Space/E up, Ctrl/Q down, Shift boost, N noclip,
hold left click to drill the targeted block (needs a drill strong enough for it; the bar under the crosshair shows
progress), right click places the selected hotbar block, mouse wheel or 1–9 selects a hotbar slot,
F uses the targeted crafting station, opens/closes the targeted door or uses a console (see Ships), I or Tab opens the inventory (click a stack, then click where it goes; put it on the red trash slot to throw it away,
click the trash to take the last thrown stack back; the next throw destroys it and saves don't keep it),
Esc closes a window, frees the mouse, and again returns to the menu. Movement stops while a window is open.
F5 saves the world.

Multi-block blocks (doors, thrusters, guns, power blocks, big windows) are placed whole. While one is selected, the
aimed face shows a ghost of it: a wire box for its footprint and its textured model, see-through and tinted green when it
fits and red when it is blocked (outside the world, overlapping blocks or another multi-block's footprint, or in your body).
Rock in front of the ghost hides it, as it would the placed block.
R spins it a quarter turn about its up axis and T points its up axis the next way; the HUD shows the orientation. With a single-cell block selected R and T do nothing (the grid has no orientation) and say so.
Only the model is solid: thruster exhaust and door travel space can be flown through but not built in. Aiming at any part
of a multi-block's model targets all of it, and breaking it drops the item. Doors slide along their own up axis and close
again after their time; a door never closes on the player (it stays open and says so). Door speed is read as cells per
second and time as milliseconds open (ours; original behaviour unknown).

## Ships

A ship (our rules; original behaviour unknown) is the structure attached to a ship console (any block with a console component, the Standard console block in
vanilla): every block face-connected to it, multi-blocks included, except asteroid rock and minerals, where it stops.
F on a console that is part of the static world **launches** it: the structure leaves the world and becomes a ship with
its own block grid, at the same world position and world-aligned at first, and the HUD says how many blocks it has and
its mass (the sum of the blocks' `mass`; a multi-block counts its item's mass once). A console with nothing attached, or
a structure over 48 cells along any axis (or over 20 000 cells), is refused with a message, and so is a
65th ship, since a save holds at most 64. A ship's grid has 64 cells a
side with the structure in the middle, so there is room to build on every side.

Ships are built on like the world: aiming, mining, placing, the ghost, doors and stations all work on them, and a block
placed against a ship's face joins that ship, turned in the ship's own axes. Nothing can be placed where another grid already
has a block or a multi-block's reserved space (a ship's thruster can't be built into the rock beside it). Mining a ship down to nothing removes it.
The player collides with ships too (in a turned ship's frame the body is approximated by the axis-aligned box around
it).

### Flying

F on a ship's console sits the player down at it (Esc or F again stands up). The camera sits just above the console
(or, when the ship has a block right there, behind it, so an enclosed cockpit does not look out through its own roof),
looking along its forward: a console block has no orientation, so its forward is the ship's −Z (the front of the demo
ship); a console that is a multi-block faces its own turned −Z. Controls while seated:

- W/S forward/back, A/D strafe, Space/Ctrl up/down, along the console's axes; Shift asks for twice the speed
- mouse (or the arrow keys) pitches and yaws, Q/E roll (instead of moving up and down)
- Z toggles the inertial dampeners, V switches between the seat and a chase camera behind the ship
- the HUD shows the position of the centre of mass, speed, heading (compass degrees, 0 along the world's −Z, counting up
  to the right), pitch and roll, the turn rates about the console's axes (°/s; pitch up, yaw right and roll right are
  positive), mass, power supply/demand and the worst network's satisfaction, how many thrusters fire, the dampeners and
  the view. Building and mining are off while seated, and the hotbar's selection name is hidden

Standing up puts the player at the nearest free spot beside the console (above, behind, to the sides...) moving with the
ship's velocity there. That velocity is drift, which the astronaut's damping leaves alone, so the player keeps moving
with a coasting ship; thrusting against it spends the thrust on it first, and hitting something takes away the part
into it. The seat is not saved: a world saved while flying loads with the player standing there, drifting with the ship
(the drift is saved) and looking at the ship's centre of mass.

All flight rules are ours (tunable; original behaviour unknown):

- **Thrust.** A thruster's placement bounds leave room for its exhaust beside the model ("thruster wash" in the DarthNox
  v21 readme); the flame points from the model's centre towards the bounds' centre (snapped to an axis) and the ship is
  pushed the other way, at the model's centre. Force is the itemset's `force` × 30, so the demo ship (mass about 265)
  gets about 4.5 m/s² per axis.
- **Flight assistant.** The controls ask for a velocity (up to 30 m/s) and a turn rate (0.9 rad/s per key, the mouse up
  to 1.8). The assistant turns the difference into a force and torque, and an allocator picks a throttle 0..1 for every
  real thruster to come as close as it can, turning first: a ship moves and turns only where it has thrusters, and
  pushing forward on a lopsided ship flies straight at reduced thrust rather than spinning. Only when that would leave
  less than half the push the ship could give (all its rear thrust off the centre of mass, with nothing to trim the
  turn it makes) does the allocator lean towards pushing anyway: such a ship accelerates and pitches. There are no
  gyroscopes. With dampeners on, letting go brings the ship to a stop and sideways drift is cancelled; with them off
  the assistant only speeds up towards what is asked and the ship keeps its momentum. Either way it never pushes a ship
  past 60 m/s (the boosted request) and brakes back to that, so legs flown in different directions without dampeners
  do not add up. Rotation always stops when the stick is let go.
  An unpiloted ship drifts (with a very small damping, so it eventually stops).
- **Power.** Blocks with a `power` value form networks through touching faces; other blocks don't conduct. Supply is the
  sum of positive values, demand the sum of the consumers' (thrusters only while firing), and a network's satisfaction
  is supply / demand, at most 1. Thrusters give that fraction of their force. Every thruster that pushes is firing
  (draws power, shows a flame scaled by throttle and the thruster's `flameLength`, or a short default when it is 0);
  one stays lit for 0.25 s after its throttle drops to zero, so the demand does not flicker. Blocks on a network that
  delivers something (satisfaction above 0) show their active textures, idle thrusters included: they are ready to
  fire. The others (a lone cable, a thruster with no supply on its network) show their `deactiveTextures`. The static
  world follows the same rule, so launching a structure does not change how it looks. Only the changed chunks and
  objects are remeshed.
- **Physics.** Ships are integrated in fixed steps of 1/120 s and drawn (and followed by the camera) at the blend of
  their last two steps that the frame's leftover time gives, so they move smoothly at any refresh rate, at most a step
  behind. Mass, centre of mass and inertia are recomputed whenever blocks are added or removed, also while flying;
  building on a ship never moves it, and on a turning ship the hull keeps its motion (the centre of mass velocity is
  corrected for the shift).
- **Collisions.** After each step the ship's surface (each exposed cell's centre and a point just inside each open face)
  is tested against the static world's solid cells, and against nearby ships' (bounding spheres first). On contact the
  step is undone back to the last free moment, the velocity into the contact is reversed at 20% (sliding along it is
  kept) and the spin is halved; the contact normal is read from which faces of the hit cells are open. Limits: no
  swept test (a ship faster than a cell per step, 120 m/s, could skip a one-cell wall; the assistant's 60 m/s limit
  keeps piloted ships well below that), sample points a cell apart (a sharp rock corner can poke half a cell in
  between them), no torque or friction from contacts, and a ship that starts a step already overlapping (built into
  rock, or loaded so) is pushed out a little each step; a resting ship is checked again after any edit, door move or
  pose change. Ship against ship shares the impulse by mass. A standing player is pushed out of a moving ship's cells,
  along the ship's motion if there is room (else to another side), never into rock or another ship, and drifts at
  least as fast as the ship that way; with no free spot at all the player stays in the ship's cells and can walk out
  (the ship is not stopped by the player). A player standing inside a moving ship is not carried with it, only pushed
  by its walls.
- **Saves.** Position, rotation and both velocities of every ship are saved, so a ship saved while moving keeps moving
  after loading. Doors on ships keep working and move with the ship.

On this machine the demo ship flying in a new vanilla world runs at about 1 000 FPS (Godot's screenshot runs are
uncapped), and the DarthNox showcase (every block placed, in view behind the ship) plus the demo ship flying up and
backwards at 16 m/s in front of it (`--itemset=DarthNoxItemset --showcase --demo-ship --launch --pilot=chase
--hold=ship_up+move_back:4 --seconds=3.5`) at about 955 FPS.

Controls (creative): as above, but a click mines at once, right click places from the whole block palette
(the wheel pages through it), and there is no inventory or crafting (doors still work).

## Worlds

Worlds are saved to `user://worlds` (`%APPDATA%\OpenCorneroids\worlds` on Windows), one `.ocworld` file per world:
on F5, when returning to the menu, and when the window is closed. If that save fails (a locked file, a full disk), the
game stays open and asks before leaving or quitting without saving. A world always saves back to the file it was loaded
from, so a copied or renamed `.ocworld` file is a separate world and never overwrites the original. The menu's
**Continue** list loads or deletes them (saves whose header can't be read are listed too, so they can be deleted);
**New world** picks an itemset, texture packs, a name, a seed and creative or survival rules, all of which the save
remembers. Multi-blocks are saved whole (block, footprint origin, orientation and how far a door is open); a door saved
part-open finishes closing on load. Ships are saved with their position, rotation, velocities, cells and multi-blocks;
saves without ships keep the older format version, so older builds still read them.

A world is bound to the exact itemset it was made with (key, pack and content fingerprint). On load:

- an itemset with the same fingerprint loads silently (the saved pack is preferred);
- the same itemset from the saved pack with edited content loads with a warning if every block the world uses still exists;
- if some of those blocks are gone, the game asks first and turns them into empty space;
- if the saved pack is not installed, a same-key itemset from another pack is offered, but only after asking;
- if blocks still can't be restored once loaded (no texture for them, or outside the world), the game asks again and
  saves nothing until the player chooses to keep playing;
- a world whose itemset is not installed (or no longer parses) is refused, naming the itemset it needs. Damaged files
  show an error.

Round-trip check (creative showcase, then survival inventory):

```
<godot> --path game -- --itemset=auto --showcase --new-world=RoundTrip --quit-after-save
<godot> --path game -- --load=RoundTrip --screenshot=roundtrip.png
<godot> --path game -- --new-world=SurvivalTrip --give=10:5,20:3 --quit-after-save
<godot> --path game -- --load=SurvivalTrip --open=inventory --screenshot=inventory.png
<godot> --path game -- --new-world=ObjectTrip --give=56:1 --preview=56 --place --quit-after-save
<godot> --path game -- --load=ObjectTrip --focus=56 --screenshot=object.png
<godot> --path game -- --new-world=ShipTrip --demo-ship --launch --turn-ship=30 --place-on-ship=29 --quit-after-save
<godot> --path game -- --load=ShipTrip --screenshot=ship.png
<godot> --path game -- --new-world=FlightTrip --demo-ship --launch --pilot=fixed --dampeners=off --hold=move_forward+ship_yaw_left:0.8 --seconds=1.2 --quit-after-save
<godot> --path game -- --load=FlightTrip --dampeners=off --pilot=chase --seconds=0.5 --screenshot=flight.png
```

Flight checks:

```
<godot> --path game -- --itemset=auto --demo-ship --launch --pilot=fixed --screenshot=rest.png
<godot> --path game -- --itemset=auto --demo-ship --launch --pilot=fixed --hold=move_forward:2 --seconds=2.5 --screenshot=forward.png
<godot> --path game -- --itemset=auto --demo-ship --launch --pilot=fixed --hold=ship_yaw_left:1.5 --seconds=2.5 --screenshot=yaw.png
<godot> --path game -- --itemset=auto --demo-ship --launch --pilot=chase --hold=move_forward+move_boost:12 --seconds=11.5 --screenshot=boost.png
<godot> --path game -- --itemset=auto --demo-ship --launch --pilot=chase --hold=ship_yaw_right+ship_pitch_up:0.7 --hold=move_forward:20 --seconds=12 --screenshot=bump.png
```

## Tests and tools

```
dotnet test OpenCorneroids.sln
dotnet run --project tools/OpenCorneroids.ContentDump
```

The BYOA golden tests skip when `byoa/` is empty.
