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
| `--itemset=auto` / `=<key>` / `=<index>` | Skip the menu and load that itemset |
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
| `--open=inventory` / `=crafting` | Open that window once the first world (new or loaded) is up (survival). `crafting` shows the first crafting-table station without aiming at one |
| `--give=<itemId>:<count>[,<itemId>:<count>...]` | Add items to the survival inventory of the first *new* world, after the starting kit. Not applied to loaded worlds or to worlds picked later from the menu |
| `--seed=<n>` | Generator seed for a world started from the command line (default 2012) |
| `--new-world=<name>` | Create a world with this name (from `--itemset`, default `auto`, with `--textures`, `--creative`, `--seed`) and save it at once, replacing a save of the same name |
| `--load=<world name>` | Load a saved world instead of showing the menu. `--itemset`, `--textures`, `--creative`, `--showcase` and `--seed` are ignored: the save decides |
| `--quit-after-save` | Save the world started from the command line, then quit (no screenshot). Exit code 1 if nothing was saved |
| `--screenshot=<file.png>` | Save a screenshot after a few seconds, then quit (without saving the world). Mouse and keyboard are ignored meanwhile, so stray input can't change the picture |

The game starts in survival: you get the itemset's starting kit (`defaultItems`) in a 36-slot inventory whose
first nine slots are the hotbar.

Controls (survival): mouse look, WASD move, Space/E up, Ctrl/Q down, Shift boost, N noclip,
hold left click to drill the targeted block (needs a drill strong enough for it; the bar under the crosshair shows
progress), right click places the selected hotbar block, mouse wheel or 1–9 selects a hotbar slot,
F uses the targeted crafting station or opens/closes the targeted door, I or Tab opens the inventory (click a stack, then click where it goes),
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
part-open finishes closing on load.

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
```

## Tests and tools

```
dotnet test OpenCorneroids.sln
dotnet run --project tools/OpenCorneroids.ContentDump
```

The BYOA golden tests skip when `byoa/` is empty.
