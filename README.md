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
| `--showcase` | Place one of every block in front of the player (implies `--creative`) |
| `--open=inventory` / `=crafting` | Open that window once the first world (new or loaded) is up (survival). `crafting` shows the first crafting-table station without aiming at one |
| `--give=<itemId>:<count>[,<itemId>:<count>...]` | Add items to the survival inventory of the first *new* world, after the starting kit. Not applied to loaded worlds or to worlds picked later from the menu |
| `--seed=<n>` | Generator seed for a world started from the command line (default 2012) |
| `--new-world=<name>` | Create a world with this name (from `--itemset`, default `auto`, with `--textures`, `--creative`, `--seed`) and save it at once, replacing a save of the same name |
| `--load=<world name>` | Load a saved world instead of showing the menu. `--itemset`, `--textures`, `--creative`, `--showcase` and `--seed` are ignored: the save decides |
| `--quit-after-save` | Save the world started from the command line, then quit (no screenshot). Exit code 1 if nothing was saved |
| `--screenshot=<file.png>` | Save a screenshot after a few seconds, then quit (without saving the world) |

The game starts in survival: you get the itemset's starting kit (`defaultItems`) in a 36-slot inventory whose
first nine slots are the hotbar.

Controls (survival): mouse look, WASD move, Space/E up, Ctrl/Q down, Shift boost, N noclip,
hold left click to drill the targeted block (needs a drill strong enough for it; the bar under the crosshair shows
progress), right click places the selected hotbar block, mouse wheel or 1–9 selects a hotbar slot,
F uses the targeted crafting station, I or Tab opens the inventory (click a stack, then click where it goes),
Esc closes a window, frees the mouse, and again returns to the menu. Movement stops while a window is open.
F5 saves the world.

Controls (creative): as above, but a click mines at once, right click places from the whole block palette
(the wheel pages through it), and there is no inventory or crafting.

## Worlds

Worlds are saved to `user://worlds` (`%APPDATA%\OpenCorneroids\worlds` on Windows), one `.ocworld` file per world:
on F5, when returning to the menu, and when the window is closed. If that save fails (a locked file, a full disk), the
game stays open and asks before leaving or quitting without saving. A world always saves back to the file it was loaded
from, so a copied or renamed `.ocworld` file is a separate world and never overwrites the original. The menu's
**Continue** list loads or deletes them (saves whose header can't be read are listed too, so they can be deleted);
**New world** picks an itemset, texture packs, a name, a seed and creative or survival rules, all of which the save
remembers.

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
```

## Tests and tools

```
dotnet test OpenCorneroids.sln
dotnet run --project tools/OpenCorneroids.ContentDump
```

The BYOA golden tests skip when `byoa/` is empty.
