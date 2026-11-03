using Godot;
using OpenCorneroids.Content;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Content;
using OpenCorneroids.Game.Persistence;
using OpenCorneroids.Game.Player;
using OpenCorneroids.Game.Ui;
using OpenCorneroids.Simulation.Items;
using OpenCorneroids.Simulation.Persistence;
using Num = System.Numerics;

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

    Hud _hud = null!;
    Vector3 _eye;

    /// <summary>Builds the world from a loaded itemset. Must be called once the view is in the scene tree.</summary>
    /// <param name="saved">The save to restore, or null to generate asteroids from <see cref="WorldInfo.Seed"/>.</param>
    public bool Setup(LoadedItemset loaded, WorldInfo info, WorldSnapshot? saved = null)
    {
        var itemset = loaded.Itemset!;
        var textures = BlockTextures.Create(loaded);
        var palette = BlockPalette.Build(itemset, textures);
        if (palette.Count == 0) return false;
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
        world.Initialize(palette, textures.Opaque, textures.Cutout);
        AddChild(world);
        Vector3 eye, target = default;
        if (saved is null)
        {
            (eye, target) = AsteroidGenerator.Generate(world, info.Seed);
            world.RebuildAll();
        }
        else
        {
            LostCells = world.Import(saved.Palette, saved.Cells, saved.Size);
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

    /// <summary>Places every placeable block in a spaced grid in front of the spawn point, using the same
    /// <see cref="VoxelWorld.Set"/> path as the player, so meshing, cutouts and block lights are exercised. The grid is
    /// centred on eye height, far enough away to fit between the HUD's top text and the hotbar, and the camera turns to
    /// face its middle, so no row hides behind the HUD or past the screen edge.</summary>
    public void Showcase()
    {
        var blocks = Voxels.Palette.Indices.ToList();
        if (blocks.Count == 0) return;
        // Roughly screen-shaped (16:9), at least ten across.
        int columns = Math.Max(10, (int)Math.Ceiling(Math.Sqrt(blocks.Count * 16.0 / 9)));
        int rows = (blocks.Count + columns - 1) / columns;
        // Blocks sit two apart, so the grid is 2 * rows - 1 tall. The default 75 degree vertical FOV sees about 0.77 up
        // per unit away; holding the half height to about 0.43 per unit leaves the HUD's top text and the hotbar clear.
        // Sideways a 16:9 screen sees about 1.36 per unit, so 1.0 keeps a margin at the edges.
        int distance = Math.Max(10, (int)Math.Ceiling(Math.Max((2 * rows - 1) * 1.15, columns - 0.5)));
        int x0 = Mathf.RoundToInt(_eye.X) - columns + 1, y0 = Mathf.RoundToInt(_eye.Y) + rows - 1, z = Mathf.RoundToInt(_eye.Z) - distance;
        for (int i = 0; i < blocks.Count; i++)
            Voxels.Set(new Vector3I(x0 + i % columns * 2, y0 - i / columns * 2, z), blocks[i]);
        // Aimed a little below the middle: the hotbar and its caption take more of the screen than the text above.
        Player.LookAt(new Vector3(x0 + columns - 0.5f, y0 - rows + 1f, z + 0.5f), Vector3.Up);
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
