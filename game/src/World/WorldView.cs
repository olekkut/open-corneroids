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
using OpenCorneroids.Simulation.Ships;
using Num = System.Numerics;
using Orientation = OpenCorneroids.Simulation.Shapes.Orientation;

namespace OpenCorneroids.Game.World;

/// <summary>
/// One play session: space environment, asteroids (generated, or restored from a save), ships, the player camera, the
/// HUD and, in survival, the inventory and its windows. It can turn itself back into a <see cref="WorldSnapshot"/>.
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
    /// <summary>The static world and the ships.</summary>
    public GridSet Grids { get; private set; } = null!;
    public FlyCamera Player { get; private set; } = null!;
    /// <summary>The player's 36 slots. Exists in creative too (empty and unused) so callers never need a null check.</summary>
    public Inventory Inventory { get; private set; } = null!;
    /// <summary>The survival suit; null in creative.</summary>
    public Suit? Suit { get; private set; }
    public bool Creative => Info.Creative;
    /// <summary>Name, seed, rules and texture packs: what the save keeps besides the world itself.</summary>
    public WorldInfo Info { get; private set; } = null!;
    /// <summary>The itemset this world is played with now. Saves are bound to it, so a world loaded after a warning or
    /// a confirmation is bound to the new content from its next save on.</summary>
    public ItemsetBinding Binding { get; private set; } = null!;
    /// <summary>Solid cells (and objects) of the loaded save, world and ships, that became air because this palette
    /// lacks their block (or they lie outside this build's grid sizes).</summary>
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
    readonly ShipPhysics _shipPhysics = new();
    /// <summary>Where <see cref="DemoShip"/> put its console, for <c>--launch</c>.</summary>
    Vector3I? _demoConsole;

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
        Voxels = world;
        Grids = new GridSet(world, this);
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
            foreach (var savedShip in saved.Ships)
            {
                var ship = Grids.AddShip(ShipGrid.SizeFor(savedShip.Size));
                ship.RestoreMotion(savedShip);
                LostCells += ship.Import(savedShip.Palette, savedShip.Cells, savedShip.Size) + ship.ImportObjects(savedShip.Objects);
                // Everything it had is gone (counted above): an empty ship is not kept.
                if (ship.IsEmpty) Grids.RemoveShip(ship);
                else GD.Print(FormattableString.Invariant(
                    $"Loaded {ship.Name}: {ship.BlockCount} blocks, moving at {ship.LinearVelocity.Length():0.##} m/s, turning at {ship.AngularVelocity.Length():0.###} rad/s"));
            }
            eye = ToGodot(saved.Player.Position);
        }

        var hud = new Hud { Name = "Hud" };
        AddChild(hud);
        hud.Setup($"{info.Name}  -  {header.Name} by {header.Author}  [{loaded.View.PackId}]{(creative ? "  (creative)" : "")}", Icons);
        _hud = hud;
        var markers = new SpaceMarkers { Name = "Markers" };
        hud.AddChild(markers);
        hud.MoveChild(markers, 0);   // under the rest of the HUD
        markers.Setup(Grids);

        Inventory = Inventory.ForItemset(itemset);
        PlayMode mode;
        GameWindows? windows = null;
        if (creative)
        {
            mode = new CreativeMode(Grids, hud);
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
            Suit = new Suit(itemset);
            if (saved?.Suit is { } suitState && Suit.Restore(suitState) is { Count: > 0 } unworn)
            {
                // Equipment this itemset can't wear goes back to the inventory rather than vanishing.
                long lost = unworn.Sum(stack => (long)Inventory.Add(stack.ItemId, stack.Quantity));
                var note = $"{unworn.Count} worn items can't be worn with {header.Name} and were moved to the inventory" +
                           (lost > 0 ? $"; {lost} did not fit and were lost" : "");
                GD.PushWarning(note);
                hud.Flash(note, warning: true, seconds: 6);
            }
            mode = new SurvivalMode(Grids, hud, Inventory, itemset, Suit);
            windows = new GameWindows { Name = "Windows" };
            AddChild(windows);
            windows.Setup(Inventory, Suit, itemset, Icons);
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
            camera.Motor.Drift = ToGodot(saved.Player.Drift);
        }
        var shots = new Projectiles { Name = "Projectiles" };
        AddChild(shots);
        shots.Setup(Grids, itemset);
        shots.BlockDestroyed += name => hud.Log($"Shot apart: {name}");
        mode.Shots = shots;
        camera.Setup(Grids, hud, mode, windows);
        camera.ExitRequested += () => ExitRequested?.Invoke();
        camera.SaveRequested += () => Save();
        camera.MakeCurrent();
        camera.AddChild(SpaceDust.Create());
        var held = new HeldItem { Name = "HeldItem" };
        camera.AddChild(held);
        held.Setup(mode, Icons);
        Player = camera;
        _eye = eye;
        return true;
    }

    /// <summary>
    /// Ships move before the player does each frame (this node is processed before its children), so the camera
    /// follows a piloted ship where it is now and a standing player is pushed out of a ship that just moved into them.
    /// </summary>
    public override void _Process(double delta)
    {
        if (Player is null) return;
        _shipPhysics.Advance(delta, Grids, Player.PilotShip, Player.PilotControls((float)delta));
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
        // The pilot's seat is not saved: a world saved while flying loads with the player standing beside the console,
        // moving with the ship (see FlyCamera.StandingSpot), and looking at the ship's centre of mass, so the ship is the
        // first thing they see whichever view the save was made from.
        var (eye, velocity) = Player.StandingSpot();
        var rotation = Player.Rotation;
        if (Player.PilotShip is { } piloted)
        {
            var toShip = piloted.ToGlobal(piloted.Mass.CenterOfMass.ToGodot()) - eye;
            rotation = FlyCamera.LevelRotation(toShip.LengthSquared() > 0.01f ? toShip : -Player.GlobalTransform.Basis.Z);
        }
        // Standing up from a ship gives its whole velocity as drift (see FlyCamera.StandUp), so a save made seated does too.
        var drift = Player.PilotShip is null ? Player.Motor.Drift : velocity;
        var player = new PlayerState(ToNumerics(eye), ToNumerics(velocity), rotation.X, rotation.Y) { Drift = ToNumerics(drift) };
        IReadOnlyList<ItemStack?> slots = Creative ? [] : Inventory.Slots.ToArray();
        return new WorldSnapshot(Info.Name, DateTimeOffset.Now, Binding, Info.Seed, VoxelWorld.Size, palette, cells, player, slots)
        {
            Creative = Creative,
            TexturePacks = Info.TexturePackIds,
            Objects = Voxels.ExportObjects(),
            Ships = [.. Grids.Ships.Where(s => !s.IsEmpty).Select(s => s.ToSnapshot())],
            Suit = Suit?.Save(),
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
        int refused = 0;
        foreach (var grid in Grids.All)
        {
            grid.AutoCloseDoors = false;
            refused += grid.OpenAllDoors(cells => PlayerMotor.BodyOverlaps(grid, Player.GlobalPosition, cells));
        }
        if (refused > 0) GD.PushWarning($"--open-doors: {refused} door(s) left shut because the player is in the way");
    }

    /// <summary>Selects the hotbar slot that holds <paramref name="itemId"/> (<c>--select</c>).</summary>
    public void SelectItem(int itemId)
    {
        for (int i = 0; i < Inventory.HotbarSize; i++)
        {
            if (Inventory.Slots[i]?.ItemId != itemId) continue;
            Player.Mode.Select(i);
            return;
        }
        GD.PushWarning($"--select: item {itemId} is not in the hotbar");
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
            if (Player.Mode.PlanObject(new Vector3(-100, -100, -100), new Aim(Voxels, hit, before)) is not { Placeable: true }) continue;
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
        var aim = new Aim(Voxels, hit, before);
        if (Player.Mode.PlanObject(new Vector3(-100, -100, -100), aim) is { } plan)
        {
            size = plan.Shape.RotatedSize(plan.Orientation).ToVector3().ToGodot();
            min = plan.Origin.ToGodot();
        }
        else
        {
            (min, size) = (before, Vector3.One);
        }
        var view = BestView(min + size * 0.5f, size, (Vector3)(before - hit), [40, 60, 20],
            position => !requirePlaceable || Player.Mode.PlanObject(position, aim) is { Placeable: true });
        if (view is not { } chosen || (requirePlaceable && !chosen.MostlySeen)) return false;
        LookFrom(chosen.Position, min + size * 0.5f);
        Player.PinnedAim = aim;
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

    // ---- ship hooks ----

    /// <summary>
    /// <c>--demo-ship</c>: a small preset ship near the spawn, facing open space (<see cref="FindFreeBox"/>), built in
    /// the static world from the itemset's own blocks
    /// and touching nothing else, so <c>--launch</c> (or F on its console) can launch it. Picked by component, so any
    /// itemset gets what it has: a hull box of the first plain opaque single-cell block, the first console (embedded in
    /// the hull's top front, the front being −Z), the strongest power supply on top, and two thrusters on each of the
    /// six faces, each turned so its exhaust points away from the hull (<see cref="ThrusterMount"/>'s rule), so the
    /// ship can push and turn every way (on the top and bottom faces on opposite diagonals). Hull blocks don't conduct power, so the weakest single-cell power block that
    /// supplies nothing (a cable) replaces the hull cells on the shortest paths from the power block to the console
    /// and to every thruster (<see cref="WireDemoShip"/>). What the itemset lacks is skipped with a warning. The camera
    /// moves to look at it.
    /// </summary>
    /// <returns>Whether a ship was built.</returns>
    public bool DemoShip()
    {
        var palette = Voxels.Palette;
        ushort? First(Func<ushort, bool> where) => palette.Indices.Where(where).OrderBy(i => palette.Item(i).ItemId).Select(i => (ushort?)i).FirstOrDefault();
        bool Has<T>(ushort b) where T : class, IItemComponent => palette.Item(b).Get<T>() is not null;

        var hull = First(b => !palette.IsObject(b) && !palette.IsCutout(b) && palette.Item(b).Components.Count == 0 && palette.Item(b).Block!.Power is null);
        if (hull is not { } hullBlock)
        {
            GD.PushWarning("--demo-ship: the itemset has no plain opaque single-cell block to build a hull from");
            return false;
        }
        var console = palette.Indices.Where(Has<ConsoleComponent>).OrderBy(b => palette.IsObject(b)).ThenBy(b => palette.Item(b).ItemId)
            .Select(b => (ushort?)b).FirstOrDefault();
        var power = palette.Indices.Where(b => palette.Item(b).Block!.Power > 0).OrderByDescending(b => palette.Item(b).Block!.Power)
            .ThenBy(b => palette.Item(b).ItemId).Select(b => (ushort?)b).FirstOrDefault();
        var thruster = palette.ObjectIndices.Where(Has<ThrusterComponent>)
            .OrderBy(b => palette.Shape(b)!.Size.Volume).ThenBy(b => palette.Item(b).ItemId).Select(b => (ushort?)b).FirstOrDefault();
        if (console is null) GD.PushWarning("--demo-ship: the itemset has no console block; the ship can't be launched");
        if (power is null) GD.PushWarning("--demo-ship: the itemset has no power supply block");
        if (thruster is null) GD.PushWarning("--demo-ship: the itemset has no multi-block thruster");

        // Hull 5 wide (X), 3 high (Y), 7 long (Z). Thrusters and the power block stick out by at most their longest edge.
        var hullSize = new Vector3I(5, 3, 7);
        int reach = 1 + new[] { thruster, power }.OfType<ushort>().Select(b => palette.Shape(b) is { } shape ? Math.Max(shape.Size.X, Math.Max(shape.Size.Y, shape.Size.Z)) : 1)
            .DefaultIfEmpty(1).Max();
        if (FindFreeBox(hullSize, reach) is not { } min)
        {
            GD.PushWarning("--demo-ship: no free space near the spawn");
            return false;
        }

        var cells = new List<(Vector3I, ushort)>();
        for (int z = 0; z < hullSize.Z; z++)
        for (int y = 0; y < hullSize.Y; y++)
        for (int x = 0; x < hullSize.X; x++)
            cells.Add((min + new Vector3I(x, y, z), hullBlock));
        int cx = min.X + hullSize.X / 2, top = min.Y + hullSize.Y - 1, cy = min.Y + hullSize.Y / 2;
        var consoleCell = new Vector3I(cx, top, min.Z);
        if (console is { } c && !palette.IsObject(c)) cells[cells.FindIndex(e => e.Item1 == consoleCell)] = (consoleCell, c);
        if (power is { } pw && !palette.IsObject(pw))
        {
            var cell = new Vector3I(cx, top, min.Z + hullSize.Z / 2);
            cells[cells.FindIndex(e => e.Item1 == cell)] = (cell, pw);
        }
        Voxels.SetCells(cells);

        int placed = 0, wanted = 0;
        var placedObjects = new List<WorldObject>();
        void PlaceAgainst(ushort block, Vector3I placeCell, Vector3I normal, Orientation o)
        {
            wanted++;
            var origin = palette.Shape(block)!.AnchorFor(placeCell.ToInt3(), normal.ToInt3(), o);
            if (Voxels.ObjectProblem(block, origin, o) is { } problem)
                GD.PushWarning($"--demo-ship: {palette.Item(block).Name} at {origin}: {problem}");
            else if (Voxels.AddObject(block, origin, o) is { } added)
            {
                placed++;
                placedObjects.Add(added);
            }
        }
        if (console is { } co && palette.IsObject(co))
        {
            PlaceAgainst(co, consoleCell + Vector3I.Up, Vector3I.Up, Orientation.Default);
            consoleCell = Voxels.Objects.MaxBy(obj => obj.Id)?.ModelCells.First() ?? consoleCell;
        }
        if (power is { } p && palette.IsObject(p))
            PlaceAgainst(p, new Vector3I(cx, top + 1, min.Z + hullSize.Z / 2 - 1), Vector3I.Up, Orientation.Default);
        if (thruster is { } t)
        {
            var mounts = new (Vector3I Normal, Vector3I[] Cells)[]
            {
                (Vector3I.Right, [new(min.X + hullSize.X, cy, min.Z + 1), new(min.X + hullSize.X, cy, min.Z + hullSize.Z - 2)]),
                (Vector3I.Left, [new(min.X - 1, cy, min.Z + 1), new(min.X - 1, cy, min.Z + hullSize.Z - 2)]),
                // Top and bottom pairs sit on opposite diagonals, so the vertical thrusters can pitch and roll the ship
                // both ways (pairs on one end only could pitch it one way, and forward thrust below the centre of mass
                // could never be trimmed).
                (Vector3I.Up, [new(min.X, top + 1, min.Z + hullSize.Z - 2), new(min.X + hullSize.X - 1, top + 1, min.Z + 1)]),
                (Vector3I.Down, [new(min.X, min.Y - 1, min.Z + 1), new(min.X + hullSize.X - 1, min.Y - 1, min.Z + hullSize.Z - 2)]),
                (Vector3I.Back, [new(min.X + 1, cy, min.Z + hullSize.Z), new(min.X + hullSize.X - 2, cy, min.Z + hullSize.Z)]),
                (Vector3I.Forward, [new(min.X + 1, cy, min.Z - 1), new(min.X + hullSize.X - 2, cy, min.Z - 1)]),
            };
            var shape = palette.Shape(t)!;
            var component = palette.Item(t).Get<ThrusterComponent>()!;
            foreach (var (normal, at) in mounts)
            {
                // Exhaust outwards: the turn whose flame points along the face normal.
                var outward = normal.ToInt3().ToVector3();
                var turned = Orientation.All.FirstOrDefault(turn =>
                    System.Numerics.Vector3.Distance(ThrusterMount.FromShape(shape, Int3.Zero, turn, component).LocalFlameDirection, outward) < 1e-3f);
                foreach (var cell in at) PlaceAgainst(t, cell, normal, turned);
            }
        }
        _demoConsole = console is null ? null : consoleCell;
        int cables = WireDemoShip(cells.Where(c => c.Item2 == hullBlock).Select(c => c.Item1).ToHashSet(),
            cells.Where(c => c.Item2 != hullBlock).Select(c => c.Item1).Concat(placedObjects.SelectMany(o => o.ModelCells)));

        var center = (Vector3)min + (Vector3)hullSize * 0.5f;
        // From the front-right and a little above, far enough to see the thrusters around the hull.
        var eyeAt = center + new Vector3(0.55f, 0.45f, 1).Normalized() * 20;
        if (VoxelWorld.InBounds((Vector3I)eyeAt.Floor()) && !PlayerMotor.BodyOverlaps(eyeAt, Voxels.IsSolid))
            LookFrom(eyeAt, center);
        else if (BestView(center, (Vector3)hullSize + Vector3.One * 2, new Vector3(0.55f, 0.45f, 1).Normalized(), [65, 50, 80], _ => true) is { } view)
            LookFrom(view.Position, center);
        GD.Print($"--demo-ship: hull of {palette.Item(hullBlock).Name} at {min}, console {(console is { } k ? palette.Item(k).Name : "none")}, " +
                 $"power {(power is { } w ? palette.Item(w).Name : "none")}, {placed} of {wanted} multi-blocks placed, {cables} cable blocks");
        return true;
    }

    /// <summary>
    /// Connects the demo ship's power: hull blocks don't conduct (only blocks with a power value do), so cells of
    /// <paramref name="hull"/> on the shortest paths from the power supply to every other powered block among
    /// <paramref name="poweredCells"/> become the itemset's weakest non-supplying single-cell power block (a cable).
    /// Blocks are joined nearest first, each through whatever is already connected. Returns how many cables were laid.
    /// </summary>
    int WireDemoShip(HashSet<Vector3I> hull, IEnumerable<Vector3I> poweredCells)
    {
        var palette = Voxels.Palette;
        var cable = palette.GridIndices
            .Where(b => palette.Item(b).Block!.Power is <= 0 && palette.Item(b).Components.Count == 0 && !palette.IsCutout(b))
            .OrderBy(b => Math.Abs(palette.Item(b).Block!.Power!.Value)).ThenBy(b => palette.Item(b).ItemId)
            .Select(b => (ushort?)b).FirstOrDefault();
        if (cable is not { } cableBlock)
        {
            GD.PushWarning("--demo-ship: the itemset has no cable-like power block; the thrusters stay unpowered");
            return 0;
        }

        // Group the powered cells into blocks: grid cells alone, objects by their model.
        var groups = new List<(HashSet<Vector3I> Cells, int Power)>();
        foreach (var cell in poweredCells.Distinct())
        {
            if (Voxels.ObjectAt(cell) is { } obj)
            {
                if (palette.Item(obj.Block).Block!.Power is not { } objectPower || groups.Any(g => g.Cells.Contains(cell))) continue;
                groups.Add((obj.ModelCells.ToHashSet(), objectPower));
            }
            else if (palette.Item(Voxels.Get(cell)).Block!.Power is { } cellPower)
                groups.Add(([cell], cellPower));
        }
        int source = groups.FindIndex(g => g.Power > 0);
        if (source < 0) return 0;

        var reached = new HashSet<Vector3I>(groups[source].Cells);
        var laid = new List<Vector3I>();
        var rest = groups.Where((_, i) => i != source).Select(g => g.Cells)
            .OrderBy(g => g.Min(c => reached.Min(r => (c - r).Abs().X + (c - r).Abs().Y + (c - r).Abs().Z))).ToList();
        foreach (var target in rest)
        {
            if (Route(reached, hull, target) is not { } path)
            {
                GD.PushWarning($"--demo-ship: no hull path for power to the block at {target.First()}");
                continue;
            }
            laid.AddRange(path);
            reached.UnionWith(path);
            reached.UnionWith(target);
            hull.ExceptWith(path);
        }
        Voxels.SetCells(laid.Select(c => (c, cableBlock)).ToList());
        return laid.Count;
    }

    static readonly Vector3I[] FaceSteps = [Vector3I.Right, Vector3I.Left, Vector3I.Up, Vector3I.Down, Vector3I.Back, Vector3I.Forward];

    /// <summary>The fewest <paramref name="through"/> cells that join <paramref name="from"/> to a face of
    /// <paramref name="to"/> (breadth-first), empty when they already touch, or null when no path exists.</summary>
    static List<Vector3I>? Route(HashSet<Vector3I> from, HashSet<Vector3I> through, HashSet<Vector3I> to)
    {
        bool Touches(Vector3I c) => FaceSteps.Any(s => to.Contains(c + s));
        if (from.Any(Touches)) return [];
        var previous = new Dictionary<Vector3I, Vector3I?>();
        var queue = new Queue<Vector3I>();
        foreach (var start in from)
            foreach (var step in FaceSteps)
                if (through.Contains(start + step) && previous.TryAdd(start + step, null)) queue.Enqueue(start + step);
        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            if (Touches(cell))
            {
                var path = new List<Vector3I>();
                for (Vector3I? at = cell; at is { } c; at = previous[c]) path.Add(c);
                return path;
            }
            foreach (var step in FaceSteps)
                if (through.Contains(cell + step) && previous.TryAdd(cell + step, cell)) queue.Enqueue(cell + step);
        }
        return null;
    }

    /// <summary>How far ahead (−Z) of the demo ship <see cref="FindFreeBox"/> wants open space, in cells, so the first
    /// seat view is not a wall of rock and the ship can speed up before it reaches anything. Ours.</summary>
    const int DemoClearAhead = 40;

    /// <summary>
    /// The minimum corner of a box of <paramref name="size"/> near the spawn whose cells, and <paramref name="margin"/>
    /// around them, are empty and unreserved in the world: in front of the spawn view and to its left first, then
    /// further out. The nearest such box with <see cref="DemoClearAhead"/> cells of open space in front of it (−Z, the
    /// way the ship faces; the world's edge counts as open) wins; when there is none, the nearest free box.
    /// </summary>
    Vector3I? FindFreeBox(Vector3I size, int margin)
    {
        var basis = Player.GlobalTransform.Basis;
        var start = _eye - basis.Z * 14 - basis.X * 6 - (Vector3)size * 0.5f;
        bool Free(Vector3I p) => !Voxels.IsSolid(p) && !Voxels.IsReserved(p) && !PlayerMotor.BodyOccupies(Player.GlobalPosition, p);
        Vector3I? nearest = null;
        for (int ring = 0; ring <= 16; ring++)
        for (int dy = -ring; dy <= ring; dy++)
        for (int dx = -ring; dx <= ring; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring) continue;
            var min = (Vector3I)(start + basis.X * dx * 3 + basis.Y * dy * 3).Round();
            bool free = true;
            for (int z = -margin; z < size.Z + margin && free; z++)
            for (int y = -margin; y < size.Y + margin && free; y++)
            for (int x = -margin; x < size.X + margin && free; x++)
            {
                var p = min + new Vector3I(x, y, z);
                free = VoxelWorld.InBounds(p) && Free(p);
            }
            if (!free) continue;
            nearest ??= min;
            bool clear = true;
            for (int z = -margin - 1; z >= -DemoClearAhead && clear; z--)
            for (int y = -margin; y < size.Y + margin && clear; y++)
            for (int x = -margin; x < size.X + margin && clear; x++)
            {
                var p = min + new Vector3I(x, y, z);
                clear = !VoxelWorld.InBounds(p) || Free(p);
            }
            if (clear) return min;
        }
        return nearest;
    }

    /// <summary>
    /// <c>--launch</c>: F on a console of the static world, as the player would press it: <see cref="DemoShip"/>'s
    /// console, or else the world's console block nearest the camera.
    /// </summary>
    public void LaunchConsole()
    {
        var palette = Voxels.Palette;
        var cell = _demoConsole;
        if (cell is null)
        {
            var eye = Player.GlobalPosition;
            var consoles = Voxels.Blocks().Where(b => palette.Item(b.Block).Get<ConsoleComponent>() is not null).Select(b => b.Cell)
                .Concat(Voxels.Objects.Where(o => palette.Item(o.Block).Get<ConsoleComponent>() is not null).Select(o => o.ModelCells.First()));
            cell = consoles.Select(c => (Vector3I?)c).MinBy(c => ((Vector3)c!.Value).DistanceSquaredTo(eye));
        }
        if (cell is not { } at)
        {
            GD.PushWarning("--launch: there is no console in the static world");
            return;
        }
        int ships = Grids.Ships.Count;
        Player.PinnedAim = new Aim(Voxels, at, at + Vector3I.Up);
        Player.InteractAtAim();
        Player.PinnedAim = null;
        GD.Print(Grids.Ships.Count > ships ? $"--launch: launched {Grids.Ships[^1].Name}" : "--launch: nothing launched (see the HUD message)");
    }

    /// <summary>
    /// <c>--turn-ship</c>: turns every ship by <paramref name="degrees"/> about the world's up axis through its centre
    /// of mass, to see building and aiming follow a ship's own axes.
    /// </summary>
    public void TurnShips(float degrees)
    {
        foreach (var ship in Grids.Ships)
        {
            var pivot = ship.ToGlobal(ship.Mass.CenterOfMass.ToGodot());
            var turn = new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(degrees)), Vector3.Zero);
            var around = new Transform3D(Basis.Identity, pivot) * turn * new Transform3D(Basis.Identity, -pivot);
            ship.GlobalTransform = around * ship.GlobalTransform;
        }
    }

    /// <summary>
    /// <c>--pilot</c>: sits at the first ship's console (its first console grid block, else its first console object)
    /// with the given view, as F there would.
    /// </summary>
    public bool PilotFirstShip(FlyCamera.PilotView view)
    {
        if (Grids.Ships.Count == 0)
        {
            GD.PushWarning("--pilot: there is no ship (use --demo-ship --launch)");
            return false;
        }
        var ship = Grids.Ships[0];
        var palette = ship.Palette;
        Vector3I? console = ship.Blocks().Where(b => palette.Item(b.Block).Get<ConsoleComponent>() is not null).Select(b => (Vector3I?)b.Cell).FirstOrDefault()
                            ?? ship.Objects.Where(o => palette.Item(o.Block).Get<ConsoleComponent>() is not null).OrderBy(o => o.Id)
                                .Select(o => (Vector3I?)o.Origin.ToGodot()).FirstOrDefault();
        Player.View = view;
        if (console is not { } cell || !Player.Sit(ship, cell))
        {
            GD.PushWarning($"--pilot: {ship.Name} has no console");
            return false;
        }
        GD.Print($"--pilot: piloting {ship.Name} from its console at {cell}, {view} view");
        return true;
    }

    /// <summary>
    /// <c>--preview-ship</c> / <c>--place-on-ship</c>: selects the item's block (survival gets one if needed) and pins
    /// the aim to the first face of a ship, seen from the camera, where it fits, scanning rays that fan out from the
    /// camera towards the ship's centre; then, with <paramref name="place"/>, places it there by the normal rules.
    /// </summary>
    public bool AimAtShip(int itemId, bool place)
    {
        if (Grids.Ships.Count == 0)
        {
            GD.PushWarning("--preview-ship/--place-on-ship: there is no ship");
            return false;
        }
        if (Voxels.Palette.IndexOf(itemId) is not { } block)
        {
            GD.PushWarning($"--preview-ship/--place-on-ship: item {itemId} is not a placeable block in {Itemset.Header.Name}");
            return false;
        }
        if (!Player.Mode.TrySelectBlock(block))
        {
            Inventory.Add(itemId, 1);
            Player.Mode.TrySelectBlock(block);
        }
        var ship = Grids.Ships[^1];
        var eye = Player.GlobalPosition;
        var toShip = (ship.ToGlobal(ship.Mass.CenterOfMass.ToGodot()) - eye).Normalized();
        var side = toShip.Cross(Vector3.Up).Normalized();
        var up = side.Cross(toShip).Normalized();
        bool Fits(Aim aim) => Voxels.Palette.IsObject(block)
            ? Player.Mode.PlanObject(eye, aim) is { Placeable: true }
            : aim.Grid.ReservedProblem(aim.Before) is null && Grids.OverlapProblem(aim.Grid, [aim.Before]) is null &&
              !PlayerMotor.BodyOccupies(aim.Grid, eye, aim.Before);
        for (int ring = 0; ring <= 16; ring++)
        for (int step = 0; step < (ring == 0 ? 1 : 18); step++)
        {
            float radius = Mathf.DegToRad(ring * 1.5f), angle = Mathf.DegToRad(step * 20f);
            var direction = (toShip + side * Mathf.Tan(radius) * Mathf.Cos(angle) + up * Mathf.Tan(radius) * Mathf.Sin(angle)).Normalized();
            if (!Grids.Raycast(eye, direction, 4 * VoxelWorld.Size, out var aim) || aim.Grid != ship || aim.Hit == aim.Before || !Fits(aim)) continue;
            Player.PinnedAim = aim;
            GD.Print($"aiming at {ship.Name} cell {aim.Hit} towards {aim.Before}");
            if (!place) return true;
            int before = ship.Revision;
            Player.PlaceAtAim();
            Player.PinnedAim = null;
            GD.Print(ship.Revision != before ? $"--place-on-ship: placed {Voxels.Palette.Item(block).Name} on {ship.Name} at {aim.Before}"
                : "--place-on-ship: nothing placed (see the HUD message)");
            return ship.Revision != before;
        }
        GD.PushWarning($"--preview-ship/--place-on-ship: no face of {ship.Name} in view where {Voxels.Palette.Item(block).Name} fits");
        return false;
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
