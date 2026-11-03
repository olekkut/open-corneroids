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
| `--showcase` | Place one of every block in front of the player |
| `--screenshot=<file.png>` | Save a screenshot after a few seconds, then quit |

Controls: mouse look, WASD move, Space/E up, Ctrl/Q down, Shift boost, left click mine,
right click place, mouse wheel or 1–9 select a block, Esc frees the mouse, Esc again returns to the menu.

## Tests and tools

```
dotnet test OpenCorneroids.sln
dotnet run --project tools/OpenCorneroids.ContentDump
```

The BYOA golden tests skip when `byoa/` is empty.
