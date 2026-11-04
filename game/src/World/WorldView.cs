using Godot;
using OpenCorneroids.Content;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Content;
using OpenCorneroids.Game.Persistence;
using OpenCorneroids.Game.Player;
using OpenCorneroids.Game.Ui;
using OpenCorneroids.Simulation.Items;
using OpenCorneroids.Simulation.Persistence;
using OpenCorneroids.Simulation.Shapes;
using Num = System.Numerics;
using Orientation = OpenCorneroids.Simulation.Shapes.Orientation;

namespace OpenCorneroids.Game.World;

/// <summary>
/// One play session: space environment, asteroids (generated, or restored from a save), the player camera, the HUD
/// and, in survival, the inventory and its windows. It can turn itself back into a <see cref="WorldSnapshot"/>.
/// </summary>
public partial class WorldView : Node3D
{
    public event Action? ExitRequested;

    public ItemsetDef Itemset { get; private set; } = null!;
    public BlockTextures Textures { get; private set; } = null!;
    /// <summary>Null when the itemset's sprite atlas could not be resolved; non-block icons then fall back to badges.</summary>
    public SpriteTextures? Sprites { get; private set; }
    public ItemIcons Icons { get; private set; } = null!;
    public VoxelWorld Voxels { get; private set; } = null!;
    public FlyCamera Player { get; private set; } = null!;
    /// <summary>The player's 36 slots. Exists in creative too (empty and unused) so callers never need a null check.</summary>
    public Inventory Inventory { get; private set; } = null!;
    public bool Creative => Info.Creative;
    /// <summary>Name, seed, rules and texture packs: what the save keeps besides the world itself.</summary>
    public WorldInfo Info { get; private set; } = null!;
    /// <summary>The itemset this world is played with now. Saves are bound to it, so a world loaded after a warning or
    /// a confirmation is bound to the new content from its next save on.</summary>
    public ItemsetBinding Binding { get; private set; } = null!;
    /// <summary>Solid cells of the loaded save that became air because this palette lacks their block (or they lie
    /// outside this build's world size).</summary>
    public int LostCells { get; private set; }
    /// <summary>What restoring the saved inventory could not keep as it was, for the player; null when nothing.</summary>
    public string? InventoryNote { get; private set; }
    /// <summary>
    /// While true, <see cref="Save"/> writes nothing: the player has not yet agreed that this load may lose content,
    /// so the file on disk, which still has it, must not be replaced by an F5, leaving or closing the window.
    /// </summary>
    public bool HoldSaves { get; set; }

    /// <summary>How multi-block faces lay out their texture regions (<c>--texture-mapping</c>). A display choice, not
    /// saved with the world. Set before <see cref="Setup"/>.</summary>
    public TextureMapping Mapping { get; init; } = TextureMapping.Stretch;

    Hud _hud = null!;
    Vector3 _eye;

    /// <summary>Builds the world from a loaded itemset. Must be called once the view is in the scene tree.</summary>
    /// <param name="saved">The save to restore, or null to generate asteroids from <see cref="WorldInfo.Seed"/>.</param>
    public bool Setup(LoadedItemset loaded, WorldInfo info, WorldSnapshot? saved = null)
    {
        var itemset = loaded.Itemset!;
        var textures = BlockTextures.Create(loaded);
        var palette = BlockPalette.Build(itemset, textures);
        // Asteroids are built from grid blocks, so an itemset needs at least one single-cell block to make a world.
        if (!palette.GridIndices.Any()) return false;
        Itemset = itemset;
        Textures = textures;
        Sprites = SpriteTextures.Create(loaded);
        Icons = new ItemIcons(textures, Sprites);
        Info = info;
        var header = loaded.Entry.Header;
        Binding = new ItemsetBinding(header.Key, header.Name, loaded.Entry.Pack.Id, itemset.Fingerprint);
        bool creative = info.Creative;

        AddEnvironment();

        var world = new VoxelWorld { Name = "VoxelWorld" };
        world.Initialize(palette, textures, Mapping);
        AddChild(world);
        Vector3 eye, target = default;
        if (saved is null)
        {
            (eye, target) = AsteroidGenerator.Generate(world, info.Seed);
            world.RebuildAll();
        }
        else
        {
            // Objects whose block is gone, or that no longer fit this world, are dropped and counted like lost cells.
            LostCells = world.Import(saved.Palette, saved.Cells, saved.Size) + world.ImportObjects(saved.Objects);
            eye = ToGodot(saved.Player.Position);
        }
        Voxels = world;

        var hud = new Hud { Name = "Hud" };
        AddChild(hud);
        hud.Setup($"{info.Name}  -  {header.Name} by {header.Author}  [{loaded.View.PackId}]{(creative ? "  (creative)" : "")}", Icons);
        _hud = hud;

        Inventory = Inventory.ForItemset(itemset, 36);
        PlayMode mode;
        GameWindows? windows = null;
        if (creative)
        {
            mode = new CreativeMode(world, hud);
        }
        else
        {
            if (saved is null)
            {
                int leftOver = StartingKit.Give(Inventory, itemset);
                if (leftOver > 0) GD.PushWarning($"Starting kit: {leftOver} items did not fit the inventory");
            }
            else
            {
                RestoreInventory(saved.Inventory);
            }
            mode = new SurvivalMode(world, hud, Inventory, itemset);
            windows = new GameWindows { Name = "Windows" };
            AddChild(windows);
            windows.Setup(Inventory, itemset, Icons);
        }
        hud.SetControls(mode.Controls + "   F5 save");

        var camera = new FlyCamera { Name = "Player", Position = eye };
        AddChild(camera);
        if (saved is null)
        {
            camera.LookAt(target, Vector3.Up);
        }
        else
        {
            camera.Rotation = new Vector3(saved.Player.Pitch, saved.Player.Yaw, 0);
            camera.Motor.Velocity = ToGodot(saved.Player.Velocity);
        }
        camera.Setup(world, hud, mode, windows);
        camera.ExitRequested += () => ExitRequested?.Invoke();
        camera.SaveRequested += () => Save();
        camera.MakeCurrent();
        Player = camera;
        _eye = eye;
        return true;
    }

    /// <summary>
    /// Slots go back where they were. Items this itemset no longer defines are kept, as the inventory allows, so loading
    /// with the right itemset later shows them again. A stack above this itemset's max stack size is split rather than
    /// cut down (<see cref="Inventory.SetSlot"/> would clamp it): the rest goes to other slots. Whatever still does not
    /// fit, or is unknown, ends up in <see cref="InventoryNote"/> for the player, not just the log.
    /// </summary>
    void RestoreInventory(IReadOnlyList<ItemStack?> slots)
    {
        int unknown = 0, split = 0;
        var overflow = new List<ItemStack>();
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] is not { } stack || stack.Quantity <= 0) continue;
            if (i >= Inventory.Slots.Count) { overflow.Add(stack); continue; }
            if (!Itemset.ItemsById.ContainsKey(stack.ItemId)) unknown++;
            int max = Inventory.MaxStackOf(stack.ItemId);
            if (stack.Quantity > max)
            {
                split++;
                overflow.Add(stack with { Quantity = stack.Quantity - max });
            }
            Inventory.SetSlot(i, stack);
        }
        long lost = 0;
        foreach (var stack in overflow) lost += Inventory.Add(stack.ItemId, stack.Quantity);

        var notes = new List<string>();
        if (unknown > 0) notes.Add($"{unknown} inventory stack(s) are items {Itemset.Header.Name} does not define (kept, without an icon)");
        if (split > 0) notes.Add($"{split} inventory stack(s) were above this itemset's stack size and were split");
        if (lost > 0) notes.Add($"{lost} item(s) did not fit the inventory and were lost");
        if (notes.Count == 0) return;
        InventoryNote = string.Join("; ", notes) + ".";
        GD.PushWarning(InventoryNote);
    }

    /// <summary>The world as it is right now, ready for <see cref="WorldStore.Save"/>.</summary>
    public WorldSnapshot ToSnapshot()
    {
        var (palette, cells) = Voxels.Export();
        var rotation = Player.Rotation;
        var player = new PlayerState(ToNumerics(Player.GlobalPosition), ToNumerics(Player.Motor.Velocity), rotation.X, rotation.Y);
        IReadOnlyList<ItemStack?> slots = Creative ? [] : Inventory.Slots.ToArray();
        return new WorldSnapshot(Info.Name, DateTimeOffset.Now, Binding, Info.Seed, VoxelWorld.Size, palette, cells, player, slots)
        {
            Creative = Creative,
            TexturePacks = Info.TexturePackIds,
            Objects = Voxels.ExportObjects(),
        };
    }

    /// <summary>Writes the world to <see cref="WorldInfo.FilePath"/>. Never throws: a failure is shown on the HUD and
    /// returned, so a full disk or a locked file can't crash the game on its way to the menu.</summary>
    /// <returns>Null on success, otherwise what went wrong.</returns>
    public string? Save()
    {
        if (HoldSaves)
        {
            GD.Print($"Not saving '{Info.Name}': the load has not been confirmed yet, so the file keeps what it had");
            return null;
        }
        try
        {
            WorldStore.SaveFile(Info.FilePath, ToSnapshot());
            GD.Print($"Saved world '{Info.Name}' to {Info.FilePath}");
            _hud.Flash("Saved");
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            var error = $"Could not save '{Info.Name}': {e.Message}";
            GD.PushError(error);
            _hud.Flash(error, warning: true, seconds: 6);
            return error;
        }
    }

    /// <summary>Shows a message on the HUD, e.g. a warning about how the save was loaded.</summary>
    public void Flash(string text, bool warning = false, double seconds = 2) => _hud.Flash(text, warning, seconds);

    static Vector3 ToGodot(Num.Vector3 v) => new(v.X, v.Y, v.Z);
    static Num.Vector3 ToNumerics(Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>Adds items to the inventory (<c>--give</c>). Unknown ids are skipped with a warning.</summary>
    public void Give(IEnumerable<(int ItemId, int Count)> items)
    {
        foreach (var (itemId, count) in items)
        {
            if (!Itemset.ItemsById.ContainsKey(itemId))
            {
                GD.PushWarning($"--give: item {itemId} is not in {Itemset.Header.Name}");
                continue;
            }
            int leftOver = Inventory.Add(itemId, count);
            if (leftOver > 0) GD.PushWarning($"--give: {leftOver} of item {itemId} did not fit");
        }
    }

    /// <summary>Opens a window right after loading (<c>--open</c>), for screenshots. "crafting" shows the first
    /// crafting-table station without the player having to aim at one.</summary>
    public void OpenWindow(string which)
    {
        switch (which)
        {
            case "inventory":
                Player.OpenInventory();
                break;
            case "crafting":
                var table = Itemset.Items.FirstOrDefault(i => i.Get<CraftingStationComponent>()?.Station == CraftingStation.CraftingTable);
                Player.OpenCrafting(CraftingStation.CraftingTable, table?.Name ?? "Crafting table");
                break;
            default:
                GD.PushWarning($"--open: unknown window '{which}', expected inventory or crafting");
                break;
        }
    }

    /// <summary>
    /// Lays out every placeable block in front of the player: the single-cell blocks in a spaced grid (through the same
    /// <see cref="VoxelWorld.Set"/> path as the player, so meshing, cutouts and block lights are exercised), and below it
    /// every multi-block, upright, in shelves spaced by its size so nothing overlaps. The shelf width is picked so the
    /// whole layout needs the shortest camera distance, and the player moves back (towards the world's +Z edge) to see
    /// all of it at once; rock between the camera and the layout is cleared so the asteroid can't hide anything.
    /// </summary>
    public void Showcase()
    {
        var palette = Voxels.Palette;
        var singles = palette.GridIndices.ToList();
        var objects = palette.ObjectIndices
            .Select(b => (Block: b, Size: palette.Shape(b)!.RotatedSize(Orientation.Default)))
            .OrderByDescending(o => o.Size.Y).ThenBy(o => palette.Item(o.Block).ItemId).ToList();
        if (singles.Count == 0 && objects.Count == 0) return;

        // Roughly screen-shaped (16:9), at least ten across, blocks two apart.
        int columns = Math.Max(10, (int)Math.Ceiling(Math.Sqrt(singles.Count * 16.0 / 9)));
        int rows = (singles.Count + columns - 1) / columns;
        int gridWidth = 2 * columns - 1, gridHeight = singles.Count == 0 ? 0 : 2 * rows - 1;
        const int Gap = 2;

        // The default 75 degree vertical FOV sees about 0.77 up per unit away; holding the half height to about 0.43 per
        // unit leaves the HUD's top text and the hotbar clear. Sideways a 16:9 screen sees about 1.36 per unit, so 1.0
        // keeps a margin at the edges.
        static float Distance(int width, int height) => Math.Max(10, Math.Max(height * 1.15f, width * 0.5f + 0.5f));
        var shelves = Shelves(objects.Select(o => o.Size).ToList(), Math.Max(gridWidth, objects.Count == 0 ? 0 : objects.Max(o => o.Size.X)));
        for (int width = shelves.Width + 1; objects.Count > 0 && width <= gridWidth * 4 + 64; width++)
        {
            var candidate = Shelves(objects.Select(o => o.Size).ToList(), width);
            if (Distance(Total(candidate).W, Total(candidate).H) < Distance(Total(shelves).W, Total(shelves).H)) shelves = candidate;
        }
        (int W, int H) Total((List<Int3> At, int Width, int Height) s) =>
            (Math.Max(gridWidth, s.Width), gridHeight + (objects.Count > 0 ? (gridHeight > 0 ? Gap : 0) + s.Height : 0));

        var (totalWidth, totalHeight) = Total(shelves);
        int distance = (int)Math.Ceiling(Distance(totalWidth, totalHeight));
        int cameraZ = VoxelWorld.Size - 2;
        int z = Math.Max(2, cameraZ - distance);
        int left = Mathf.RoundToInt(_eye.X - totalWidth / 2f), top = Mathf.RoundToInt(_eye.Y + totalHeight / 2f);   // top is exclusive

        // Clear the view: everything between the layout's plane and the camera, a cell wider than the layout.
        for (int cz = z + 1; cz <= cameraZ + 1; cz++)
        for (int cy = top - totalHeight - 1; cy <= top; cy++)
        for (int cx = left - 1; cx <= left + totalWidth; cx++)
            Voxels.SetRaw(new Vector3I(cx, cy, cz), BlockPalette.Air);
        // And behind the multi-block shelves, as deep as the deepest footprint: each object clears its own footprint,
        // which left the parts above its model (door travel space, empty bounds) as narrow pockets in the rock behind
        // the layout, seen as dark slits. One even backdrop instead.
        int depth = objects.Count == 0 ? 0 : objects.Max(o => o.Size.Z);
        int shelvesTop = top - gridHeight - (gridHeight > 0 ? Gap : 0);
        for (int cz = z + 1 - depth; cz <= z; cz++)
        for (int cy = shelvesTop - shelves.Height - 1; cy <= shelvesTop; cy++)
        for (int cx = left - 1; cx <= left + totalWidth; cx++)
            Voxels.SetRaw(new Vector3I(cx, cy, cz), BlockPalette.Air);
        Voxels.RebuildAll();

        int x0 = left + (totalWidth - gridWidth) / 2;
        for (int i = 0; i < singles.Count; i++)
            Voxels.Set(new Vector3I(x0 + i % columns * 2, top - 1 - i / columns * 2, z), singles[i]);

        int areaLeft = left + (totalWidth - shelves.Width) / 2, areaTop = shelvesTop;
        for (int i = 0; i < objects.Count; i++)
        {
            var (block, size) = objects[i];
            var at = shelves.At[i];
            // Front faces line up with the single blocks' (z + 1), so the footprint reaches back into the clearing's far side.
            var origin = new Int3(areaLeft + at.X, areaTop - at.Y, z + 1 - size.Z);
            if (Voxels.AddObject(block, origin, Orientation.Default, clearGrid: true) is null)
                GD.PushWarning($"Showcase: no room for {palette.Item(block).Name} at {origin}");
        }

        Player.Position = new Vector3(_eye.X, _eye.Y, cameraZ + 0.5f);
        // Aimed a little below the middle: the hotbar and its caption take more of the screen than the text above.
        Player.LookAt(new Vector3(left + totalWidth / 2f, top - totalHeight / 2f - totalHeight * 0.03f, z + 0.5f), Vector3.Up);
    }

    /// <summary>
    /// Shelf packing: boxes left to right one cell apart, a new shelf one cell below the tallest box of the last one when
    /// the next box would pass <paramref name="width"/>. Boxes stand on their shelf's floor, so models that sit at the
    /// bottom of their bounds (thrusters, closed doors) line up. Returns each box's x and the depth of its shelf's floor
    /// below the top, and the bounds used.
    /// </summary>
    static (List<Int3> At, int Width, int Height) Shelves(List<Int3> sizes, int width)
    {
        var at = new List<Int3>(sizes.Count);
        int x = 0, shelfTop = 0, shelfHeight = 0, used = 0, shelfStart = 0;
        void CloseShelf()
        {
            for (int i = shelfStart; i < at.Count; i++) at[i] = at[i] with { Y = shelfTop + shelfHeight };
            shelfStart = at.Count;
        }
        foreach (var size in sizes)
        {
            if (x > 0 && x + size.X > width)
            {
                CloseShelf();
                shelfTop += shelfHeight + 1;
                (x, shelfHeight) = (0, 0);
            }
            at.Add(new Int3(x, 0, 0));
            used = Math.Max(used, x + size.X);
            x += size.X + 1;
            shelfHeight = Math.Max(shelfHeight, size.Y);
        }
        CloseShelf();
        return (at, used, shelfTop + shelfHeight);
    }

    /// <summary><c>--open-doors</c>: every door fully open, and kept open so a later screenshot still shows it. A door
    /// that would open into the player stays shut (the rule doors follow in play) and is reported.</summary>
    public void OpenDoors()
    {
        Voxels.AutoCloseDoors = false;
        int refused = Voxels.OpenAllDoors(cells => PlayerMotor.BodyOverlaps(Player.GlobalPosition, cells));
        if (refused > 0) GD.PushWarning($"--open-doors: {refused} door(s) left shut because the player is in the way");
    }

    /// <summary>
    /// <c>--preview</c>: selects the item's block (survival gets one if no hotbar slot holds it), finds a face of the
    /// asteroid where it fits, pins the aim to that face (<see cref="FlyCamera.PinnedAim"/>) and stands the camera back
    /// at an angle so the ghost's whole footprint is in the picture. A single-cell block gets the same treatment as a
    /// 1×1×1 footprint (it has no ghost, only the highlight).
    /// </summary>
    /// <returns>Whether a face was found to aim at.</returns>
    public bool Preview(int itemId)
    {
        if (Voxels.Palette.IndexOf(itemId) is not { } block)
        {
            GD.PushWarning($"--preview: item {itemId} is not a placeable block in {Itemset.Header.Name}");
            return false;
        }
        if (!Player.Mode.TrySelectBlock(block))
        {
            Inventory.Add(itemId, 1);
            if (!Player.Mode.TrySelectBlock(block)) GD.PushWarning($"--preview: could not put item {itemId} in the hotbar");
        }
        // Rays fanning out from the view direction, nearest first; the first face where the block fits wins, so the
        // ghost shows green when the asteroid allows it. Otherwise the first face found, shown red.
        var eye = Player.GlobalPosition;
        var basis = Player.GlobalTransform.Basis;
        (Vector3I Hit, Vector3I Before)? fallback = null;
        for (int ring = 0; ring <= 16; ring++)
        for (int step = 0; step < (ring == 0 ? 1 : 18); step++)
        {
            float radius = Mathf.DegToRad(ring * 1.5f), angle = Mathf.DegToRad(step * 20f);
            var direction = (-basis.Z + basis.X * Mathf.Tan(radius) * Mathf.Cos(angle) + basis.Y * Mathf.Tan(radius) * Mathf.Sin(angle)).Normalized();
            if (!VoxelRaycast.Cast(Voxels, eye, direction, 4 * VoxelWorld.Size, out var hit, out var before) || hit == before) continue;
            fallback ??= (hit, before);
            // Placeable from somewhere: the body check is redone from where the camera ends up.
            if (Player.Mode.PlanObject(new Vector3(-100, -100, -100), hit, before) is not { Placeable: true }) continue;
            if (Frame(hit, before, requirePlaceable: true)) return true;
        }
        if (fallback is { } f && Frame(f.Hit, f.Before, requirePlaceable: false)) return true;
        GD.PushWarning("--preview: the camera is not looking at any block");
        return false;
    }

    /// <summary>
    /// Pins the aim to the face between <paramref name="hit"/> and <paramref name="before"/> and puts the camera where it
    /// sees the selected block's whole footprint (see <see cref="BestView"/>). A viewpoint where the player's body would
    /// block the placement is skipped.
    /// </summary>
    bool Frame(Vector3I hit, Vector3I before, bool requirePlaceable)
    {
        Vector3 min, size;
        if (Player.Mode.PlanObject(new Vector3(-100, -100, -100), hit, before) is { } plan)
        {
            size = plan.Shape.RotatedSize(plan.Orientation).ToVector3().ToGodot();
            min = plan.Origin.ToGodot();
        }
        else
        {
            (min, size) = (before, Vector3.One);
        }
        var view = BestView(min + size * 0.5f, size, (Vector3)(before - hit), [40, 60, 20],
            position => !requirePlaceable || Player.Mode.PlanObject(position, hit, before) is { Placeable: true });
        if (view is not { } chosen || (requirePlaceable && !chosen.MostlySeen)) return false;
        LookFrom(chosen.Position, min + size * 0.5f);
        Player.PinnedAim = (hit, before);
        GD.Print($"--preview: aiming at the face of {hit} towards {before}, camera at {Coordinates(chosen.Position)} " +
                 $"({chosen.Seen} footprint points in sight)");
        return true;
    }

    /// <summary>
    /// <c>--focus</c>: moves the camera close to the first placed multi-block of that item, on the side the camera is
    /// already on (in front, for the showcase), looking at it almost square on so model-box offsets and face
    /// textures can be checked by eye. Its footprint is framed, so travel space and exhaust room are in the picture.
    /// </summary>
    public bool Focus(int itemId)
    {
        var block = Voxels.Palette.IndexOf(itemId);
        var obj = block is { } b ? Voxels.Objects.Where(o => o.Block == b).MinBy(o => o.Id) : null;
        if (obj is null)
        {
            GD.PushWarning($"--focus: no placed multi-block of item {itemId} in this world");
            return false;
        }
        var cells = obj.Footprint.ToList();
        var min = cells.Aggregate((a, c) => new Vector3I(Math.Min(a.X, c.X), Math.Min(a.Y, c.Y), Math.Min(a.Z, c.Z)));
        var max = cells.Aggregate((a, c) => new Vector3I(Math.Max(a.X, c.X), Math.Max(a.Y, c.Y), Math.Max(a.Z, c.Z)));
        var size = (Vector3)(max - min + Vector3I.One);
        var center = (Vector3)min + size * 0.5f;
        // The axis towards the camera's current side: the face it was already looking at.
        var toCamera = Player.GlobalPosition - center;
        var axis = toCamera.Abs().MaxAxisIndex();
        var normal = Vector3I.Zero;
        normal[(int)axis] = toCamera[(int)axis] >= 0 ? 1 : -1;
        if (BestView(center, size, (Vector3)normal, [70, 55, 40], _ => true) is not { } chosen)
        {
            GD.PushWarning($"--focus: no open viewpoint near {Voxels.Palette.Item(obj.Block).Name} at {obj.Origin}");
            return false;
        }
        LookFrom(chosen.Position, center);
        GD.Print($"--focus: {Voxels.Palette.Item(obj.Block).Name} at {obj.Origin}, camera at {Coordinates(chosen.Position)}");
        return true;
    }

    /// <summary>
    /// A camera position that sees the box at <paramref name="center"/>: out along <paramref name="normal"/> at each of
    /// <paramref name="elevations"/> (degrees off the surface, first preferred) and a few azimuths around it, far enough
    /// for the box's bounding sphere to fit the view. The viewpoint that sees most of the box past the rock wins; one
    /// outside the world, inside rock or refused by <paramref name="accept"/> is skipped.
    /// </summary>
    (Vector3 Position, int Seen, bool MostlySeen)? BestView(Vector3 center, Vector3 size, Vector3 normal, float[] elevations, Func<Vector3, bool> accept)
    {
        // The default 75 degree vertical FOV: a sphere of radius r fits at r / sin(37.5°); a quarter more leaves the HUD
        // text and the hotbar clear.
        float distance = Math.Max(4f, size.Length() * 0.5f / Mathf.Sin(Mathf.DegToRad(37.5f)) * 1.25f);
        var t1 = Mathf.Abs(normal.Y) > 0.5f ? Vector3.Right : Vector3.Up;
        var t2 = normal.Cross(t1);

        // Points of the box to keep in sight: its centre and its corners pulled a little inwards (the corners
        // themselves touch the surface the block stands on).
        var points = new List<Vector3> { center };
        for (int i = 0; i < 8; i++)
            points.Add(center + new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1) * (size * 0.5f - Vector3.One * 0.3f));

        (Vector3 Position, int Seen)? best = null;
        foreach (var elevation in elevations)
        for (int a = 0; a < 8; a++)
        {
            float e = Mathf.DegToRad(elevation), azimuth = Mathf.DegToRad(30 + a * 45);
            var direction = normal * Mathf.Sin(e) + (t1 * Mathf.Cos(azimuth) + t2 * Mathf.Sin(azimuth)) * Mathf.Cos(e);
            var position = center + direction * distance;
            if (!VoxelWorld.InBounds(new Vector3I(Mathf.FloorToInt(position.X), Mathf.FloorToInt(position.Y), Mathf.FloorToInt(position.Z))) ||
                PlayerMotor.BodyOverlaps(position, Voxels.IsSolid) || !accept(position))
                continue;
            int seen = points.Count(p => !VoxelRaycast.Cast(Voxels, position, p - position, position.DistanceTo(p) - 0.05f, out _, out _));
            if (best is null || seen > best.Value.Seen) best = (position, seen);
            if (seen == points.Count) break;
        }
        return best is { } b ? (b.Position, b.Seen, b.Seen > points.Count / 2) : null;
    }

    /// <summary>"61.2, 71, 83.4" whatever the system locale: a decimal comma would run the numbers together.</summary>
    static string Coordinates(Vector3 p) => FormattableString.Invariant($"{p.X:0.#}, {p.Y:0.#}, {p.Z:0.#}");

    void LookFrom(Vector3 position, Vector3 target)
    {
        Player.Position = position;
        var view = (target - position).Normalized();
        Player.LookAt(target, Mathf.Abs(view.Y) > 0.99f ? Vector3.Forward : Vector3.Up);
    }

    /// <summary><c>--place</c>: a right click at the previewed spot, through the normal placing rules (survival spends
    /// the item). The aim is let go afterwards, so the screenshot shows the placed block rather than a red ghost over it.</summary>
    public void PlacePreviewed()
    {
        int before = Voxels.Objects.Count;
        Player.PlaceAtAim();
        Player.PinnedAim = null;
        GD.Print(Voxels.Objects.Count > before
            ? $"--place: placed {Voxels.Palette.Item(Voxels.Objects.MaxBy(o => o.Id)!.Block).Name} at {Voxels.Objects.MaxBy(o => o.Id)!.Origin}"
            : "--place: nothing placed (see the HUD message)");
    }

    void AddEnvironment()
    {
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.01f, 0.01f, 0.02f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.55f, 0.6f, 0.75f),
            AmbientLightEnergy = 0.35f,
            GlowEnabled = true,
            GlowIntensity = 0.6f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
        };
        AddChild(new WorldEnvironment { Environment = environment });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-35, -40, 0), LightEnergy = 1.1f, LightColor = new Color(1f, 0.96f, 0.9f) });
        AddChild(Stars(900, seed: 7));
    }

    static MultiMeshInstance3D Stars(int count, int seed)
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)seed };
        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new SphereMesh { Radius = 0.6f, Height = 1.2f, RadialSegments = 4, Rings = 2 },
            InstanceCount = count,
        };
        for (int i = 0; i < count; i++)
        {
            var direction = new Vector3(rng.Randfn(), rng.Randfn(), rng.Randfn()).Normalized();
            var scale = rng.RandfRange(0.4f, 1.6f);
            multimesh.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.One * scale), new Vector3(64, 64, 64) + direction * 600f));
        }
        return new MultiMeshInstance3D
        {
            Name = "Stars",
            Multimesh = multimesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(0.9f, 0.92f, 1f) },
        };
    }
}
