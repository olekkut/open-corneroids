# BYOA asset loading: itemsets, atlases, directory layout

Status: proposal · Scope: loading original Corneroids content (vanilla 1.0.1.2 / 1.2.0) and community
itemset/texture mods into open-corneroids (Godot 4, C#) without shipping or modifying any of it.

> **Bundled data (2026-11-04).** The project now ships the content folders of four packs in `byoa/` (vanilla 1.2.0,
> CADNR v21, Nav's itemset, Sord's textures; never a program) so it runs out of the box; see `CREDITS.md`. Everything
> below still applies: they load as BYOA packs like any other, and more can be added.

Toolchain: **Godot Engine – .NET 4.7.2** (x86_64) plus a .NET SDK. The standard (non-.NET) Godot
build can't run C#.

> **Clean-room provenance.** Everything below comes only from the *data* in the drop folders: itemset
> and atlas XML, PNG pixels, readme files, and XNB header bytes. No `.exe`/`.dll` was opened.
> Behaviour the data can't settle is listed under [Open questions](#8-open-questions). Answer those by
> watching the original game run, never by reading its binaries.

---

## 1. What the drop folders contain

| Folder | What it is | Content root | Itemsets (key · items · blueprints) | Atlas keys it defines |
|---|---|---|---|---|
| `Corneroids-Alpha-1.2.0` | Vanilla 1.2.0, already unpacked: `Corneroids.exe` next to `Content/`. **The default base install.** | `Corneroids/Content` | `DefaultItemset` · 113 · 82 | `defaultblocks`, `defaultsprites` |
| `Corneroids-Alpha-1.0.1.2` | Vanilla 1.0.1.2, distributed as a ClickOnce installer package; **every file has a `.deploy` suffix**. We read it in place without running `setup.exe`. It's the base only when 1.2.0 is absent, and it's useful as an older-schema test case. | `Corneroids_installer/Application Files/Corneroids_1_0_1_2/Content` | `DefaultItemset` · 112 · 73 (no containers yet) | same |
| `v21` | Chef Anon repack (1.2.0 binaries) | `Corneroids/Content` | `DefaultItemset` (vanilla + an empty `<spotLights/>`), `DarthNoxItemset` "CADNR v21" · 196 · 170 | default (**repainted**) + `darthnoxblocks`, `darthnoxsprites` |
| `Chef Anons … v15` | Older repack (binary 1.0.5) | `Corneroids/Content` | same default, `DarthNoxItemset` · 162 · 138 | same as v21, older pixels |
| `DarthNoxItemset` | Standalone itemset mod (1.0.x era) | `Content` | `DarthNoxItemset` · 140 · 113 | default (**repainted**) + darthnox |
| `navItemset` | Itemset mod | `Content` | `navItemset` "Random Stuff" · 116 · 87 | `navblocks`, `navsprites`. Lists `defaultblocks` but **doesn't ship its PNG** |
| `SordsTextures` | Loose texture pack, no XML | none (readme says "drop into `Content/Textures/Blocks`") | none | replaces `defaultblocks` texture and light map |

### 1.1 Itemset XML facts

- **One schema everywhere.** All six itemset files use the same element vocabulary. The only
  differences are which categories exist (`containers` arrived in 1.2.0, `spotLights` is always empty
  and sometimes missing), one optional field (`<light>` is missing on one Nav weapon), and comments
  (DarthNox files carry about 90 of them).
- **Encoding.** UTF-8 with or without a BOM (Nav and the repack DarthNox files have none), CRLF line
  endings, pure ASCII.
- **Tag names are reused in different roles**, so a global tag→type map won't work:
  - `<crafting>` is the block category, the block element *and* the top-level blueprint section.
  - `<power>` is the block category, the block element, a "power produced or consumed" field on
    powered blocks, and "drill strength" on `<drill>`.
  - `<ingredient>` is an item definition under `items/ingredients` but a `{itemId, quantity}`
    reference inside a blueprint.
  - `<container>`, `<door>`, `<thruster>` and `<light>` each also appear as sub-blocks of the
    same-named element.
- **`key` is not unique.** DarthNox standalone, v15 and v21 all use `key="DarthNoxItemset"` and
  `version="5"` (the v21 readme says so: "Changed blockset name and author but not key"), yet they
  hold 140, 162 and 196 items. Identify itemsets by a **content fingerprint**, not by key or version.
- **IDs are sparse, and 0 is a real block.** `itemId` runs 0–198 and `blockId` 0–103, with gaps
  (itemId 6, 58, 130; blockId 6, 30, 53). `blockId 0` is *Light rock*, so "air" can't be 0 in
  original IDs.
- **Decimals use `.`**, as in `mass 0.2`, `modelPlacement y -2.5`, `ricochetAngle 0.7854` and
  projectile `size w 0.05`. Parse with `CultureInfo.InvariantCulture`. On uk-UA, pl-PL and similar
  locales the default parse fails or gives wrong numbers.
- **Enums, inferred from the data:**
  - Blueprint `usage`: 0 = `craftingtable`, 1 = `extractor`, 2 = `smelter`. Every "Smelt …" recipe
    is 2 and every "Extract …" recipe is 1; this matches `<crafting type=…>`.
  - Wearable `slot`: 0 helmet, 1 shoulders, 2 chest, 3 legs, 4 arms, 5 feet, 6 power source.
  - Consumable `type`: 1 = instant (duration 0, Health Potion), 0 = timed buff.
  - `light@type` is always `point`.
- **Units:** times are milliseconds (`reloadTime`, `lifetime`, door `time`, `burningTime`,
  `duration`). Colours are 0–255 bytes. On blocks, a negative `power` means consumption and a
  positive one means generation.
- **Health goes up to 255** in DarthNox repacks, and the readmes mention "overflow problems". Store
  it as `int` and don't emulate any overflow.
- **No dangling references** in any shipped file: blueprints, `defaultItems` and `projectileId` all
  resolve. One Nav blueprint name is duplicated, so blueprint names aren't keys.

### 1.2 Texture facts

- Every atlas PNG is **512×512 and `blockSize` is 32**, giving a 16×16 cell grid. XML coordinates are
  in **cells, with the origin at the top left and +y pointing down.**
- **The mapping is verified against pixels.** Every face and sprite reference in every itemset lands
  on a non-empty cell of its own atlas:

  | Itemset | Block-face cells | Item-sprite references |
  |---|---|---|
  | Vanilla | 269 | 66 |
  | DarthNox v21 | 662 | 95 |
  | DarthNox standalone | 360 | 80 |
  | Nav | 269 | 69 |

  As a control, checking DarthNox's XML against the *vanilla* atlas hits 81 empty cells, so the check
  does tell layouts apart.
- **`w`/`h` give the region size in cells and match the visible model face, not the placement
  bounds.** Use `modelSize` when it is non-zero, otherwise `size`:

  | Block | `size` | `modelSize` | `walls` region |
  |---|---|---|---|
  | Iron blast door | 2×4×1 | 2×2×1 | 2×2 |
  | Ion thruster | 2×7×2 | 2×2×2 | 2×2 |
  | Anti-Frigate Cannon | 2×4×2 | 0 | 2×4 |

  `size` also covers non-visual space, such as exhaust or the door's travel. `modelPlacement` shifts
  the model inside it. A door sits at y −1 inside 4-tall bounds and slides +y by `distance` 2, which
  fits a Y-up frame.
- **Faces:** `walls` (all four sides share one region), `top` and `bottom`. Powered blocks have
  `activeTextures` plus `deactiveTextures`.
- **`lightTextureAtlas.png` is an emissive mask on the same grid as `blockTextures.png`.** Its RGB is
  always white and its alpha is 0/255 (Sord's pack adds a few soft alpha values). Sample it at the
  same UV as the albedo.
- **Transparency is in the data, and it's all cutout.** The window region (14,6)–(15,8) is an opaque
  frame around fully transparent pixels. The vanilla, DarthNox v21 and Nav block atlases contain no
  partial alpha at all (only 0 and 255).
- **Sprite atlas:** `<sprite x y>` is an icon cell for *items*.
  - Nearly every *block* has `sprite (0,0)`, which is the Iron-drill icon cell, so block inventory
    icons must come from the block faces.
  - Asteroids and minerals have non-zero sprites that are clearly *not* icons (Iron ore → (0,6) is
    the Aluminium ingot cell). Keep the raw value and don't draw it.
- **The engine owns some sprite cells.** Cells (11–14, 0–2) hold UI glyphs that no XML references
  (key hint, build/hammer icons, crosshair, joystick and arrows). Every sprite atlas checked keeps
  them in place: vanilla, Nav, and v21 DarthNox.
- **Layouts only grow.** From 1.0.1.2 to 1.2.0 the block atlas changed in only 2 cells, (12,4) and
  (13,4) for the new container; the sprite atlas is pixel-identical. Mods paint into empty cells or
  repaint existing ones.
- **The same path doesn't mean the same pixels.** DarthNox, v15 and v21 ship *repainted* copies of
  the default atlases at the vanilla paths: 50–52 block cells and 17 sprite cells visibly differ.
  In 181 more sprite cells only the RGB under alpha 0 differs, which bleeds into colour once you
  mipmap, so phase B runs `FixAlphaEdges()`. Sord's pack repaints 19 block cells and 9 light cells.
  It is **pixel-identical to Nav's `NavTextures` block and light atlases**.
- **The registry files conflict.** Each mod overwrites `Textures/Blocks/DefaultBlocks.xml` and
  `Textures/Sprites/SpriteTextureAtlases.xml` with "default + mine". In the original game, installing
  DarthNox and Nav together drops one of them. We merge these files by key instead.
- **PNG flavours:** RGBA8, plus 8-bit indexed with `tRNS` (vanilla `spriteImages.png`). Ancillary
  `iCCP`/`gAMA`/`cHRM` chunks are present. Normalise everything to RGBA8 and ignore colour profiles.
- **XNB:** 60 files in format v4 (XNA 3.x), 58 of them LZX-compressed: models, shaders, crosshairs,
  font and creature textures. **Out of scope for v1.** The shaders are useless in Godot anyway, so
  those assets get original replacements.

---

## 2. Pack model and discovery

A **pack** is one dropped folder. Discovery is content-based; subfolder names are only hints.

| Signal (case-insensitive) | Pack kind | Role |
|---|---|---|
| `pack.json` at the pack root | as declared | Explicit; overrides every heuristic below |
| `Content/Blocksets/*.xml` and `Corneroids.exe[.deploy]` beside `Content` | `GameInstall` | Itemsets, atlases, and a base-install candidate |
| `Content/Blocksets/*.xml`, no exe | `ItemsetMod` | Itemsets and atlases |
| `Content/Textures/**` only | `TexturePack` (mirrored) | Mounted at `Content/` |
| No `Content`, loose `blockTextures.png` / `lightTextureAtlas.png` / `spriteImages.png` | `TexturePack` (loose) | Block files → `Textures/Blocks/`, sprite file → `Textures/Sprites/`, which is where the mod readmes say to put them |

How discovery works:

- **Finding the content root.** Search breadth-first, at most 5 levels deep, for a directory named
  `Content` that contains `Blocksets` or `Textures`. That handles `v21/Corneroids/Content` and the
  ClickOnce tree alike.
- **Choosing the base install.** A built-in table of known vanilla fingerprints (SHA-256 of
  `defaultItemset.xml`) decides which `GameInstall` packs are vanilla:
  - 1.2.0: `603db3ec811b6776baf17af13fed07df72d3104c0148378b7689e0160c455244`
  - 1.0.1.2: `1cf146df6733e591c167cb85ced03987c74ff2cfd150ccc17487bc84aca8946e`

  The newest vanilla install becomes the default base. Repacks still contribute their itemsets and
  atlases, but they're only used as a base if the player picks one.
- **`pack.json` is optional and our own format.** Existing community folders must work without it.
  New packs use it to declare an id, kind, `mountAt`, or retargeted overrides, for example
  `{"overrides":{"navblocks":{"texture":"blockTextures.png","light":"lightTextureAtlas.png"}}}`.

## 3. Virtual file system

```csharp
public interface IContentSource
{
    string PackId { get; }
    bool TryOpen(string contentPath, out Stream stream);       // "Textures/Blocks/blockTextures.png"
    IEnumerable<string> List(string directory, string extension);
}

public sealed class FolderSource : IContentSource
{
    // Mods are authored on Windows, so lookups must be case-insensitive even on Linux.
    readonly Dictionary<string, string> _index = new(StringComparer.OrdinalIgnoreCase);

    public FolderSource(string packId, string contentRoot, string mountAt = "")
    {
        PackId = packId;
        foreach (var file in Directory.EnumerateFiles(contentRoot, "*", SearchOption.AllDirectories))
        {
            var rel = mountAt + Path.GetRelativePath(contentRoot, file).Replace('\\', '/');
            if (rel.EndsWith(".deploy", StringComparison.OrdinalIgnoreCase))
                rel = rel[..^".deploy".Length];                    // ClickOnce layout (1.0.1.2)
            _index[rel] = file;
        }
    }
    // TryOpen / List read from _index and open files with FileShare.Read, never for writing.
}
```

**Each itemset resolves through its own layered view**, where the first hit wins:

```
1. user-enabled texture packs, in the user's order    e.g. SordsTextures → Textures/Blocks/*
2. the itemset's own pack                             e.g. navItemset, v21
3. the base install                                   e.g. vanilla 1.2.0
```

This reproduces how each mod looked in its own distribution. v21's Default itemset gets v21's
repainted atlas, and Nav's missing default PNG falls through to vanilla. Mods also stop clobbering
each other, and texture packs remain an explicit opt-in. Later, a `ZipSource` can implement the same
interface so players can drop archives as well.

## 4. Itemset and atlas loader (the same code path for vanilla and mods)

Nothing is special-cased for vanilla: `defaultItemset.xml` is just an itemset in the base pack.

### 4.1 Atlas catalogue

For every view, parse `Textures/Blocks/DefaultBlocks.xml` (`<blockTextureAtlases>`) and
`Textures/Sprites/SpriteTextureAtlases.xml` (`<spriteTextureAtlases>`) from **every layer**. Merge
the entries by `<key>`; the upper layer wins for each key.

```csharp
public sealed record AtlasDef(string Key, string Name, AtlasKind Kind,
                              string TexturePath, string? LightTexturePath, int BlockSize,
                              string DeclaringPack);
```

An itemset's `<textures><blockTexture><key>` and `<spriteTexture><key>` then resolve through that
merged catalogue. The paths are relative to `Content/`, and the file lookup goes through the same
layered view.

### 4.2 Parsing

Parsing happens in two phases:

1. A **header scan** with a streaming `XmlReader` reads only `name`, `author`, `key`, `@version` and
   the texture keys. It's fast enough to list every itemset on the new-world screen.
2. A **full parse** runs when a world is created or loaded.

```csharp
static readonly XmlReaderSettings Settings = new()
{
    DtdProcessing = DtdProcessing.Prohibit,   // user-supplied files: no DTD/XXE/entity bombs
    XmlResolver = null,
    IgnoreComments = true,
    IgnoreWhitespace = true,
    MaxCharactersInDocument = 16 * 1024 * 1024,
};

public static XDocument Load(Stream s)
{
    using var reader = XmlReader.Create(s, Settings);           // copes with BOM and no-BOM UTF-8
    return XDocument.Load(reader, LoadOptions.SetLineInfo);     // line numbers for diagnostics
}
```

Rules:

- **Dispatch by path, not tag.** Use a table keyed by `"<category>/<element>"`. A category that isn't
  in the table produces a warning and is skipped, which keeps the loader compatible with future mods.

  ```csharp
  static readonly Dictionary<string, ComponentParser> Blocks = new()
  {
      ["armors/armor"] = None,                 ["asteroids/asteroid"] = Asteroid,
      ["cameras/camera"] = None,               ["consoles/console"] = Console,
      ["crafting/crafting"] = CraftingStation, ["doors/door"] = Door,
      ["gunConsoles/gunConsole"] = Console,    ["guns/gun"] = Gun,
      ["pointLights/pointLight"] = PointLight, ["spotLights/spotLight"] = SpotLight,
      ["minerals/mineral"] = Mineral,          ["power/power"] = None,
      ["thrusters/thruster"] = Thruster,       ["containers/container"] = Container,
      ["windows/window"] = None,
  };
  static readonly Dictionary<string, ComponentParser> Items = new()
  {
      ["consumables/consumable"] = Consumable, ["ingredients/ingredient"] = None,
      ["drills/drill"] = Drill,                ["torches/torch"] = Torch,
      ["weapons/weapon"] = Weapon,             ["wearables/wearable"] = Wearable,
  };
  ```
- **Don't depend on element order.** Read fields by name (`e.Element("health")`). Optional fields get
  defaults: `deactiveTextures`, block `power`, weapon `light`.
- **Every number goes through one helper** that uses `CultureInfo.InvariantCulture`. Integer fields
  also accept `"2.0"`, with a warning.
- **Keep unknown fields.** Store them in `Extras` and log a diagnostic instead of failing. Keep raw
  values we don't use yet, such as `sprite` on blocks and `buffSpriteCoords`.
- **Errors are per item.** A bad item is skipped and logged with `file(line)`. Only an unreadable root
  fails the whole itemset.

### 4.3 Model

The library is Godot-free: it uses `System.Numerics.Vector3` and its own small structs, so it can be
unit-tested with `dotnet test`.

```csharp
public readonly record struct CellRect(int X, int Y, int W, int H);
public readonly record struct Rgb8(byte R, byte G, byte B);
public sealed record FaceSet(CellRect Walls, CellRect Top, CellRect Bottom);

public sealed record BlockDef(int BlockId, float Mass, int Health, int PowerToDrill,
                              Vector3 Size, Vector3 ModelSize, Vector3 ModelPlacement,
                              FaceSet Active, FaceSet? Inactive, int? Power)
{
    public Vector3 VisualSize => ModelSize == Vector3.Zero ? Size : ModelSize;   // see 1.2
}

public sealed record ItemDef(int ItemId, string Kind, string Name, string Description, Rgb8 Color,
                             int MaxStackSize, int ModelDepth, int PowerUsage, CellRect Sprite,
                             BlockDef? Block, IReadOnlyList<IItemComponent> Components,
                             IReadOnlyDictionary<string, string> Extras);
```

Components are plain records:

| Component | Fields |
|---|---|
| `Door` | dir, distance, speed, timeMs |
| `Thruster` | force, flameLength, flameOffset |
| `Gun` | barrelLength, reloadMs, projectileId |
| `Console` | buttons |
| `PointLight` | rgb, radius |
| `Container` | w, h |
| `CraftingStation` | type |
| `Mineral` | asteroidType, rarity |
| `Asteroid` | asteroidType |
| `Drill` | power, light |
| `Torch` | light, burnMs |
| `Weapon` | reloadMs, projectileId, pushback, spread, light? |
| `Wearable` | slot, armor, health, recharge |
| `Consumable` | kind, armor, health, power, durationMs, buffSprite |

Alongside items, an itemset holds `Projectiles` (keyed by `projectileId`), `Blueprints` (usage,
ingredients, result) and `DefaultItems`.

### 4.4 Validation, identity, runtime IDs

- **Validation runs over the resolved view:**
  - itemId, blockId and projectileId are unique;
  - blueprint, `defaultItems` and `projectileId` references resolve;
  - every face and sprite region is inside the grid and **not empty in the atlas it resolves to**
    (this catches a texture pack that doesn't match its itemset);
  - each region's `w×h` matches the model face.
- **Fingerprint** each itemset with SHA-256 of a canonical form of its XML, which ignores comments,
  whitespace, line endings, BOMs, attribute order and empty elements. v21's Default then deduplicates
  against vanilla 1.2.0.
  If its resolved atlases differ, it is listed as "Default (v21 textures)".
- **Worlds store** `{key, name, packId, fingerprint}`. On load:
  - an exact fingerprint match loads silently;
  - the same key with a different fingerprint gets a warning with a summary, such as "140 → 196
    items";
  - in both cases, check that every blockId the world uses still exists.
- **Runtime block index.** Assign a dense `ushort` at load, with 0 = air, and map it to the original
  `blockId`, because original ID 0 is a real block. Saves store original IDs through a palette.

---

## 5. Mapping XNA atlas coordinates to Godot 4

### 5.1 The math

- **Grid size.** `GridCols = referenceWidth / blockSize`, which is 16 for every known atlas.
  `referenceWidth` is the width of the PNG shipped by the pack that *declared* the atlas.
- **UV rectangle** (normalised, so resolution-independent):
  `uv = Rect2(x, y, w, h) / GridCols`
- **Pixel rectangle.** `cellPx = actualImageWidth / GridCols`, so
  `px = Rect2I(x·cellPx, y·cellPx, w·cellPx, h·cellPx)`. A 1024² HD drop-in replacement therefore
  works without touching any XML.
- **No flips.** XNA and Godot both put the texture origin at the top left with +v down.
- **No axis swaps.** Both are right-handed, Y-up and −Z-forward, so `modelPlacement`, `flameOffset`
  and door `direction` carry over 1:1. The door data in §1.2 is consistent with this.
- **Face convention (ours, to confirm visually):** a wall texture's top edge sits on the face's +Y edge.

### 5.2 Loading pages (Godot side)

```csharp
public sealed class AtlasPage
{
    public Image Albedo { get; }        // CPU copy: alpha classes, icon renders, sprite extrusion
    public Image? Light { get; }
    public ImageTexture AlbedoTex { get; }
    public ImageTexture? LightTex { get; }
    public int GridCols { get; }
    readonly Dictionary<CellRect, AtlasTexture> _sprites = new();

    public Rect2 Uv(CellRect c) => new((float)c.X / GridCols, (float)c.Y / GridCols,
                                       (float)c.W / GridCols, (float)c.H / GridCols);

    public Rect2I PixelRect(CellRect c)
    {
        int px = Albedo.GetWidth() / GridCols;
        return new Rect2I(c.X * px, c.Y * px, c.W * px, c.H * px);
    }

    public AtlasTexture Sprite(CellRect c)
    {
        if (_sprites.TryGetValue(c, out var t)) return t;
        var r = PixelRect(c);
        return _sprites[c] = new AtlasTexture { Atlas = AlbedoTex, Region = new Rect2(r.Position, r.Size),
                                                FilterClip = true };
    }
}

static Image LoadPng(IContentSource view, string path)
{
    if (!view.TryOpen(path, out var s)) throw new ContentException($"missing {path}");
    using var ms = new MemoryStream();
    using (s) s.CopyTo(ms);
    var img = new Image();
    if (img.LoadPngFromBuffer(ms.ToArray()) != Error.Ok) throw new ContentException($"bad PNG {path}");
    img.Convert(Image.Format.Rgba8);    // vanilla spriteImages.png is 8-bit indexed + tRNS
    return img;
}
```

Load through the VFS with `LoadPngFromBuffer`. **Never import BYOA files through `res://`.** They
aren't part of the export, and the editor would write `.import` files into the players' folders.

### 5.3 Blocks, in two phases

**Phase A, ship first: atlas as-is.**

- Use one `ImageTexture` per page with no mipmaps and nearest filtering.
- The mesher writes the region UVs, inset by about 0.01 texel so floating-point error at a cell edge
  can't sample the neighbouring cell.
- Each multi-cell region is emitted as one quad spanning `VisualSize`.
- Use one shared material per (atlas, alpha class):

```glsl
shader_type spatial;
uniform sampler2D albedo_atlas : source_color, filter_nearest, repeat_disable;
uniform sampler2D light_atlas  : hint_default_transparent, filter_nearest, repeat_disable;
uniform float emission_energy = 1.0;
void fragment() {
    vec4 c = texture(albedo_atlas, UV);
    ALBEDO = c.rgb;
    EMISSION = c.rgb * texture(light_atlas, UV).a * emission_energy;  // mask is white; tint by albedo
    // translucent variant: ALPHA = c.a;   cutout variant: ALPHA = c.a; ALPHA_SCISSOR_THRESHOLD = 0.5;
}
```

**Phase B, later: `Texture2DArray`.**

- Slice every referenced cell into its own 32×32 layer, with a parallel layer array for the light mask.
- Run `FixAlphaEdges()` and generate mipmaps per layer. That gives mipmaps without bleeding.
- Multi-cell faces become w×h quads, one layer each. When face size equals region size, that looks
  the same as phase A.
- 1×1 faces can be greedy-meshed with `fract()` tiling.

The `CellRect`-based API stays the same, so the mesher doesn't change.

**Alpha classes come from the data.** At load, scan each referenced region:

| Alpha found | Class | Surface |
|---|---|---|
| All 255 | Opaque | Opaque |
| Only 0 or 255 | Cutout | Cutout (alpha scissor) |
| Any value between | Translucent | Alpha-blended |

The mesher routes faces to the matching surface, so windows work without any "window" special case.
Every known block atlas is opaque or cutout. The translucent path exists for mods that add tinted glass;
`AtlasGrid.Classify` in the content library implements this table.

### 5.4 Sprites and icons

- **UI icons** use `page.Sprite(cell)`, an `AtlasTexture` with `FilterClip`. Set
  `rendering/textures/canvas_textures/default_texture_filter` to Nearest.
- **Held items:** extrude the sprite's opaque pixels `modelDepth` deep (1, 2, 3 or 7 in the data),
  using `Albedo.GetRegion(PixelRect(cell))`. This is why each page keeps its CPU `Image`.
- **Block inventory icons:** at load, render each block into a `SubViewport` from its faces and cache
  the result under `cache/byoa/<fingerprint>/icons.png`.
- **Engine-owned cells:** keep `game/data/engine_sprites.json`, *our* names mapped to cells, for
  example `"ui.hint.key": [11,0,1,1]`. Resolve it against whichever `defaultsprites` is active, so
  texture packs reskin the UI as well.

---

## 6. BYOA directory layout

Set `application/config/use_custom_user_dir = true` and `custom_user_dir_name = "OpenCorneroids"`.
`user://` then resolves to:

| OS | Path |
|---|---|
| Windows | `%APPDATA%\OpenCorneroids` |
| Linux | `~/.local/share/OpenCorneroids` |
| macOS | `~/Library/Application Support/OpenCorneroids` |

The name is chosen so it doesn't collide with the original game's `%APPDATA%\Corneroids`.

```
%APPDATA%\OpenCorneroids\
├─ byoa\                                   ← players drop folders here exactly as downloaded
│  ├─ README.txt                           (written on first run: what goes where)
│  ├─ games\
│  │  ├─ Corneroids-Alpha-1.2.0\           (Corneroids\Content\…)
│  │  ├─ Corneroids-Alpha-1.0.1.2\         (ClickOnce tree, .deploy files are fine)
│  │  ├─ v21\                              (repack: full install incl. repainted defaults)
│  │  └─ Chef Anons DarthNox Rebalance Repack v15\
│  ├─ mods\
│  │  ├─ DarthNoxItemset\                  (Content\Blocksets\…, Content\Textures\…)
│  │  └─ navItemset\
│  └─ texturepacks\
│     └─ SordsTextures\                    (loose blockTextures.png + lightTextureAtlas.png)
├─ cache\byoa\<fingerprint>\               ← derived only (icons, texture arrays, parse cache); safe to delete
├─ saves\
└─ byoa.cfg                                ← enabled packs, texture-pack order, chosen base install
```

Rules:

- **The BYOA folders are read-only to us.** We never write, rename or create `.import` files inside
  them; everything derived goes to `cache/`.
- **The `games/`, `mods/` and `texturepacks/` names are only hints.** Detection is by content (§2), so
  a misplaced folder still loads, and the content screen shows what it was detected as.
- **Portable mode:** if `<exe dir>/byoa/` exists, it's scanned first. A `-- --byoa-dir=<path>` user
  argument (read with `OS.GetCmdlineUserArgs()`) adds a root for development.
- **The content screen** lists every pack with its kind, any itemsets, resolved atlas keys and
  validation diagnostics.

### Repository layout (development)

```
open-corneroids/
├─ .gitignore                    byoa/  *.exe  .godot/  bin/  obj/
├─ game/                         Godot project: project.godot, OpenCorneroids.csproj, shaders, data/
├─ src/OpenCorneroids.Content/   Godot-free class library: VFS, discovery, parser, validator, fingerprints
├─ tests/OpenCorneroids.Content.Tests/
│     synthetic fixtures (hand-written mini itemset + generated PNGs) always run;
│     BYOA golden tests (counts in §1) run only if a local byoa dir exists, so CI never needs the originals
├─ docs/design/byoa-asset-loading.md
└─ byoa/                         gitignored + .gdignore: move the 8 top-level folders here
```

---

## 7. Milestones

1. **VFS, discovery and the diagnostics dump.** Headless: list every pack and itemset in `byoa/`
   with no errors.
2. **Parser, validator and fingerprints.** Golden counts:

   | Itemset | Items | Blocks | Blueprints | Projectiles |
   |---|---|---|---|---|
   | Vanilla 1.0.1.2 | 112 | 46 | 73 | 6 |
   | Vanilla 1.2.0 | 113 | 47 | 82 | 6 |
   | DarthNox standalone | 140 | 60 | 113 | 6 |
   | DarthNox v15 | 162 | 81 | 138 | 8 |
   | DarthNox v21 | 196 | 101 | 170 | 9 |
   | Nav | 116 | 47 | 87 | 8 |

   Every itemset should have zero empty-cell hits on its own view.
3. **Atlas pages, sprite `AtlasTexture`s and the phase-A mesher, with emission and alpha classes.**
4. **World binding** (fingerprints and the runtime block palette).
5. **Phase B:** texture arrays, icon renders and held-item extrusion.

## 8. Open questions

Answer these by observing the original game running.

1. How a region maps onto faces whose size differs from it, for example the 1-deep sides of a 2×3×1
   cannon with a 2×3 `walls` region. Stretch or crop? *Implemented:* stretch by default,
   `--texture-mapping=tile` repeats the region instead (`Simulation/Shapes/ShapeQuads.cs`); still to be
   compared with the original. DarthNox also has four ion thrusters whose model (3 cells, placement −2.5)
   hangs half a cell outside their 7-cell bounds; `BlockShape.Build` pulls it back inside and records a
   Problem.
2. The UV orientation of `top`/`bottom` relative to ±Z, and of `walls` on rotated blocks.
3. What `sprite` on asteroid and mineral blocks drives, and which atlas `buffSpriteCoords` points into
   (it is always (0,0)).
4. How block inventory icons look (an isometric render?).
5. What the itemset `@version` attribute means (1 for vanilla and Nav, 5 for DarthNox). For now it
   is stored and treated as opaque.

## 9. Related projects: do not consult

These public repos were reviewed on 2026-11-03 using only their metadata, READMEs, file and
directory names, and (for PasterLak) two PNGs compared against vanilla. **No source code was read.**
None has a license, so none of their code could be reused anyway. Contributors to this project
shouldn't read their source.

| Repo | What it is | Why we stay away |
|---|---|---|
| `DylanBarber/corneroids` | README for "371 decompiled C# source files" of 1.2.0 (only the README is pushed) | Decompiled original |
| `DylanBarber/Corneroids-Reloaded` | Godot 4.7.2 game alongside a "recovered" classic runtime (`src/Corneroids`, `lib/xna31`, original `Content/`) | Decompiled code and original assets sit next to the Godot code |
| `Wissotsky/Corneroids` | Decompiled game "crudely ported from XNA 3.1 to MonoGame" (2024) | Decompiled original |
| `PasterLak/Corneroids_Monogame` | 2020 MonoGame remake attempt | `blockTextures.png` is pixel-identical to vanilla; several type names match the decompiled code's file names exactly |
| `ivan-tester/corneroids.by-migration` | PHP (DataLife Engine) source for the corneroids.by fan site; no game code | Not relevant as code. The site itself may be a source of more community mods to test against |
| Spacebox v0.1.5.1 (PasterLak, itch.io; closed source, own .NET 8 + OpenGL engine) | A Corneroids-"inspired" game whose default content follows Corneroids item-for-item | Its `Mods/GameSets/Default` contains 92 PNGs pixel-identical to vanilla 1.2.0 block-atlas cells, 41 more that are near-identical, and a near-copy of a DarthNox sprite. The itch.io page states no credits or permission. Use none of its assets, and don't decompile its binary. Its JSON mod layout (per-object folders, string IDs, an `Extends` chain, per-texture emission maps) can serve as an *idea* reference for a future native pack format |
