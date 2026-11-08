using Godot;
using OpenCorneroids.Content;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Content;
using OpenCorneroids.Game.Persistence;
using OpenCorneroids.Game.Player;
using OpenCorneroids.Game.Settings;
using OpenCorneroids.Game.Ui;
using OpenCorneroids.Simulation.Debugging;
using OpenCorneroids.Simulation.Settings;
using OpenCorneroids.Simulation.Items;
using OpenCorneroids.Simulation.Persistence;
using OpenCorneroids.Simulation.Shapes;
using OpenCorneroids.Simulation.Ships;
using Num = System.Numerics;
using Orientation = OpenCorneroids.Simulation.Shapes.Orientation;

namespace OpenCorneroids.Game.World;

/// <summary>
/// One play session: space environment, asteroids (generated, or restored from a save), ships, the player camera, the
/// HUD and the in-world windows. It can turn itself back into a <see cref="WorldSnapshot"/>.
/// </summary>
public partial class WorldView : Node3D, IDebugConsoleHost
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
    GameWindows _windows = null!;
    Projectiles _shots = null!;
    HeldItem _held = null!;
    string _packId = "";
    Vector3 _eye;
    readonly ShipPhysics _shipPhysics = new();
    /// <summary>Where <see cref="DemoShip"/> put its console, for <c>--launch</c>.</summary>
    Vector3I? _demoConsole;

    /// <summary>In-game drop-down developer/debug console (<c>`</c>).</summary>
    public DebugConsoleOverlay DebugConsole { get; private set; } = null!;

    public delegate Task ProgressReporter(string step, double fraction, string? detail = null, string? completed = null);

    /// <summary>Builds the world from a loaded itemset. Must be called once the view is in the scene tree.</summary>
    /// <param name="saved">The save to restore, or null to generate asteroids from <see cref="WorldInfo.Seed"/>.</param>
    public bool Setup(LoadedItemset loaded, WorldInfo info, WorldSnapshot? saved = null) =>
        SetupAsync(loaded, info, saved, null).GetAwaiter().GetResult();

    /// <summary>Builds the world asynchronously, optionally reporting step-by-step progress.</summary>
    public async Task<bool> SetupAsync(LoadedItemset loaded, WorldInfo info, WorldSnapshot? saved = null, ProgressReporter? onProgress = null)
    {
        if (onProgress != null) await onProgress("Compiling block textures & materials...", 0.40);
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
        // This world's rules: the saved ones, or the original game's for a new world. Preferences stay as they are.
        foreach (var problem in Cvars.Import(saved?.Rules ?? new Dictionary<string, string>(), CvarFlags.World))
            GD.PushWarning($"World rules of '{info.Name}': {problem}");
        Cvars.CheatsUsed = saved?.CheatsUsed ?? false;

        if (onProgress != null) await onProgress("Initializing voxel environment...", 0.48, completed: "Textures & materials compiled");

        AddEnvironment();

        var world = new VoxelWorld { Name = "VoxelWorld" };
        world.Initialize(palette, textures, Mapping);
        AddChild(world);
        Voxels = world;
        Grids = new GridSet(world, this);
        Vector3 eye, target = default;
        if (saved is null)
        {
            if (onProgress != null) await onProgress("Generating asteroid field...", 0.55);
            (eye, target) = AsteroidGenerator.Generate(world, info.Seed);
            if (onProgress != null) await onProgress("Building asteroid meshes...", 0.70, completed: "Asteroids generated");
            world.RebuildAll();
        }
        else
        {
            if (onProgress != null) await onProgress("Importing voxel world...", 0.50);
            // Objects whose block is gone, or that no longer fit this world, are dropped and counted like lost cells.
            LostCells = world.Import(saved.Palette, saved.Cells, saved.Size) + world.ImportObjects(saved.Objects);
            if (world.ImportTurned(saved.Turned) is > 0 and var unturned)
                GD.PushWarning($"{unturned} turned blocks of the world could not be turned again and face the default way");
            world.ImportConsoles(saved.Consoles);

            if (onProgress != null) await onProgress("Importing ships...", 0.60, completed: "World voxels rebuilt");

            int shipCount = saved.Ships.Count;
            for (int i = 0; i < shipCount; i++)
            {
                var savedShip = saved.Ships[i];
                if (onProgress != null && (shipCount <= 10 || (i + 1) % 5 == 0 || i == shipCount - 1))
                {
                    double frac = 0.60 + 0.25 * ((double)(i + 1) / shipCount);
                    string kind = savedShip.IsAsteroid ? "asteroid" : "ship";
                    await onProgress($"Loading {kind}s ({i + 1} of {shipCount})...", frac, detail: $"Size {savedShip.Size}³ {kind}");
                }
                var ship = savedShip.IsAsteroid
                    ? Grids.AddAsteroid(ShipGrid.SizeFor(savedShip.Size))
                    : Grids.AddShip(ShipGrid.SizeFor(savedShip.Size));
                ship.RestoreMotion(savedShip);
                if (!savedShip.IsAsteroid && !string.IsNullOrEmpty(savedShip.Name))
                    ship.Name = savedShip.Name;

                LostCells += ship.Import(savedShip.Palette, savedShip.Cells, savedShip.Size) + ship.ImportObjects(savedShip.Objects);
                if (ship.ImportTurned(savedShip.Turned) is > 0 and var shipUnturned)
                    GD.PushWarning($"{shipUnturned} turned blocks of a ship could not be turned again and face the default way");
                ship.ImportConsoles(savedShip.Consoles);
                // Everything it had is gone (counted above): an empty ship is not kept.
                if (ship.IsEmpty) Grids.RemoveShip(ship);
                else GD.Print(FormattableString.Invariant(
                    $"Loaded {ship.Name}: {ship.BlockCount} blocks, moving at {ship.LinearVelocity.Length():0.##} m/s, turning at {ship.AngularVelocity.Length():0.###} rad/s"));
            }
            eye = ToGodot(saved.Player.Position);
        }

        if (onProgress != null) await onProgress("Configuring HUD & player systems...", 0.88, completed: saved != null ? $"{saved.Ships.Count} ships imported" : "Asteroid meshes built");

        var hud = new Hud { Name = "Hud" };
        AddChild(hud);
        _packId = loaded.View.PackId;
        hud.Setup($"{info.Name}  -  {header.Name} by {header.Author}  [{_packId}]{(creative ? "  (creative)" : "")}", Icons);
        _hud = hud;
        var markers = new SpaceMarkers { Name = "Markers" };
        hud.AddChild(markers);
        hud.MoveChild(markers, 0);   // under the rest of the HUD
        markers.Setup(Grids);

        Inventory = Inventory.ForItemset(itemset);
        PlayMode mode;
        var windows = new GameWindows { Name = "Windows" };
        AddChild(windows);
        _windows = windows;
        if (creative)
        {
            mode = new CreativeMode(Grids, hud);
            windows.Setup(null, null, itemset, Icons, []);
        }
        else
        {
            if (saved is null)
            {
                int leftOver = StartingKit.Give(Inventory, itemset, Crafting.MainInventory);
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
            windows.Setup(Inventory, Suit, itemset, Icons, StartingKit.Endless(itemset));
        }
        hud.SetControls(mode.Controls + "   F5 save");

        var consoleOverlay = new DebugConsoleOverlay { Name = "DebugConsole" };
        AddChild(consoleOverlay);
        consoleOverlay.Setup(this, () => windows.IsOpen);
        DebugConsole = consoleOverlay;

        var camera = new FlyCamera { Name = "Player", Position = eye };
        AddChild(camera);
        if (saved is null)
        {
            camera.LookAt(target, Vector3.Up);
        }
        else
        {
            camera.Rotation = new Vector3(saved.Player.Pitch, saved.Player.Yaw, saved.Player.Roll);
            camera.Motor.Velocity = ToGodot(saved.Player.Velocity);
            camera.Motor.Drift = ToGodot(saved.Player.Drift);
        }
        var shots = new Projectiles { Name = "Projectiles" };
        AddChild(shots);
        shots.Setup(Grids, itemset);
        shots.BlockDestroyed += name => hud.Log($"Shot apart: {name}");
        mode.Shots = shots;
        _shots = shots;
        if (mode is SurvivalMode survival)
        {
            var drops = new FloatingItems { Name = "Drops" };
            AddChild(drops);
            drops.Setup(Inventory, itemset, Icons, () => camera.GlobalPosition);
            drops.PickedUp += (name, count) => hud.Flash($"+{count} {name}");
            survival.Drops = drops;
            _drops = drops;
        }
        camera.Setup(Grids, hud, mode, windows, consoleOverlay);
        camera.ExitRequested += () => ExitRequested?.Invoke();
        camera.SaveRequested += () => Save();
        camera.MakeCurrent();
        camera.AddChild(SpaceDust.Create());
        var held = new HeldItem { Name = "HeldItem" };
        camera.AddChild(held);
        held.Setup(mode, Icons);
        _held = held;
        Player = camera;
        _eye = eye;
        camera.Fov = Cvars.Float(Simulation.Settings.Cvars.Fov);
        Cvars.Changed += OnSettingChanged;

        // The player's own commands for every world, e.g. their favourite rules.
        if (GameSettings.TryReadConfig(GameSettings.AutoexecFile, out _, out _))
            consoleOverlay.RunCommand($"exec {GameSettings.AutoexecFile}");

        if (onProgress != null) await onProgress("Ready to launch!", 1.0, completed: "Player & HUD ready");
        return true;
    }

    static CvarStore Cvars => GameSettings.Cvars;

    void OnSettingChanged(Cvar cvar, double value)
    {
        if (cvar == Simulation.Settings.Cvars.Fov && IsInstanceValid(Player)) Player.Fov = (float)value;
    }

    public override void _ExitTree()
    {
        // The store outlives this world; leaving the handler would keep the whole view alive.
        Cvars.Changed -= OnSettingChanged;
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
        var (eye, velocity) = Player.StandingSpot();
        var rotation = Player.Rotation;
        if (Player.PilotShip is { } piloted)
        {
            var toShip = piloted.ToGlobal(piloted.Mass.CenterOfMass.ToGodot()) - eye;
            rotation = FlyCamera.LevelRotation(toShip.LengthSquared() > 0.01f ? toShip : -Player.GlobalTransform.Basis.Z);
        }
        var player = new PlayerState(ToNumerics(eye), ToNumerics(velocity), rotation.X, rotation.Y) { Roll = rotation.Z };
        IReadOnlyList<ItemStack?> slots = Creative ? [] : Inventory.Slots.ToArray();
        return new WorldSnapshot(Info.Name, DateTimeOffset.Now, Binding, Info.Seed, VoxelWorld.Size, palette, cells, player, slots)
        {
            Creative = Creative,
            TexturePacks = Info.TexturePackIds,
            Objects = Voxels.ExportObjects(),
            Turned = Voxels.ExportTurned(),
            Consoles = Voxels.ExportConsoles(),
            Ships = [.. Grids.Ships.Where(s => !s.IsEmpty).Select(s => s.ToSnapshot())],
            Suit = Suit?.Save(),
            Rules = Cvars.Export(CvarFlags.World),
            CheatsUsed = Cvars.CheatsUsed,
        };
    }

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

    public void Flash(string text, bool warning = false, double seconds = 2) => _hud.Flash(text, warning, seconds);

    static Vector3 ToGodot(Num.Vector3 v) => new(v.X, v.Y, v.Z);
    static Num.Vector3 ToNumerics(Vector3 v) => new(v.X, v.Y, v.Z);

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

    /// <summary>Opens a window right after loading (<c>--open</c>), for screenshots: "inventory", "crafting", "console", or "power".</summary>
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
            case "console":
                foreach (var grid in Grids.Ships.Cast<BlockGrid>().Append(Voxels))
                {
                    foreach (var (cell, block) in grid.Blocks())
                        if (grid.Palette.Item(block).Get<ConsoleComponent>() is { } comp)
                        {
                            Player.OpenConsoleWindow(grid, cell, comp);
                            return;
                        }
                    foreach (var obj in grid.Objects)
                        if (grid.Palette.Item(obj.Block).Get<ConsoleComponent>() is { } comp)
                        {
                            Player.OpenConsoleWindow(grid, obj.Origin.ToGodot(), comp);
                            return;
                        }
                }
                GD.PushWarning("--open=console: no console block in the world or ships (try --demo-ship)");
                break;
            case "power":
                foreach (var grid in Grids.Ships.Cast<BlockGrid>().Append(Voxels))
                {
                    foreach (var (cell, block) in grid.PoweredBlocks())
                        if (grid.Systems.NetworkOf(new BlockTarget(grid, cell, null)) is { } net)
                        {
                            Player.OpenPowerWindow(net);
                            return;
                        }
                    foreach (var obj in grid.Objects)
                        if (grid.Palette.IsPowered(obj.Block) && grid.Systems.NetworkOf(new BlockTarget(grid, obj.Origin.ToGodot(), obj)) is { } net)
                        {
                            Player.OpenPowerWindow(net);
                            return;
                        }
                }
                GD.PushWarning("--open=power: no powered block in the world or ships (try --demo-ship)");
                break;
            case "help":
                Player.OpenHelp();
                break;
            case "debug" or "debug-console":
                DebugConsole.Open();
                break;
            default:
                GD.PushWarning($"--open: unknown window '{which}', expected inventory, crafting, console, power, help or debug");
                break;
        }
    }

    // ---- IDebugConsoleHost ----

    Num.Vector3 IDebugConsoleHost.Position => ToNumerics(Player.GlobalPosition);
    Num.Vector3 IDebugConsoleHost.Velocity => ToNumerics(Player.WorldVelocity);
    Num.Vector3 IDebugConsoleHost.SpawnPosition => ToNumerics(_eye);

    void IDebugConsoleHost.Teleport(Num.Vector3 position) => Player.TeleportTo(ToGodot(position));

    bool IDebugConsoleHost.Noclip
    {
        get => Player.Motor.Noclip;
        set => Player.Motor.Noclip = value;
    }

    CvarStore IDebugConsoleHost.Cvars => Cvars;

    bool IDebugConsoleHost.TryReadConfig(string name, out string text, out string error) =>
        GameSettings.TryReadConfig(name, out text, out error);

    string? IDebugConsoleHost.WriteUserConfig() => GameSettings.WriteUserConfig();

    public void SetCreative(bool creative)
    {
        Info = Info with { Creative = creative };
        var header = Itemset.Header;
        _hud.SetTitle($"{Info.Name}  -  {header.Name} by {header.Author}  [{_packId}]{(creative ? "  (creative)" : "")}");

        PlayMode mode;
        if (creative)
        {
            if (_windows.IsOpen) _windows.Close();
            mode = new CreativeMode(Grids, _hud) { Shots = _shots };
            _hud.HideSuit();
        }
        else
        {
            if (Suit is null)
            {
                if (Inventory.Slots.All(s => s is null))
                    StartingKit.Give(Inventory, Itemset, Crafting.MainInventory);
                Suit = new Suit(Itemset);
            }
            _windows.EnsureSurvival(Inventory, Suit, Itemset, Icons, StartingKit.Endless(Itemset));
            if (_drops is null)
            {
                var drops = new FloatingItems { Name = "Drops" };
                AddChild(drops);
                drops.Setup(Inventory, Itemset, Icons, () => Player.GlobalPosition);
                drops.PickedUp += (name, count) => _hud.Flash($"+{count} {name}");
                _drops = drops;
            }
            mode = new SurvivalMode(Grids, _hud, Inventory, Itemset, Suit) { Shots = _shots, Drops = _drops };
        }
        _hud.SetControls(mode.Controls + "   F5 save");
        Player.SetMode(mode);
        _held.SetMode(mode);
    }

    IReadOnlyList<DebugShipSummary> IDebugConsoleHost.Ships =>
        Grids.Ships
            .Select(s =>
            {
                var com = s.ToGlobal(s.Mass.CenterOfMass.ToGodot());
                return new DebugShipSummary(s.Name.ToString(), s.BlockCount, s.Mass.Mass, ToNumerics(com), s.LinearVelocity.Length());
            })
            .ToList();

    bool IDebugConsoleHost.TryTeleportToShip(string? selector, out string message)
    {
        if (Grids.Ships.Count == 0)
        {
            message = "No ships in the world. Use 'spawnship' to build and launch one.";
            return false;
        }

        ShipGrid? chosen = null;
        if (string.IsNullOrWhiteSpace(selector))
        {
            chosen = Grids.Ships
                .OrderBy(s => s.ToGlobal(s.Mass.CenterOfMass.ToGodot()).DistanceSquaredTo(Player.GlobalPosition))
                .First();
        }
        else if (int.TryParse(selector, out int idx) && idx >= 1 && idx <= Grids.Ships.Count)
        {
            chosen = Grids.Ships[idx - 1];
        }
        else
        {
            chosen = Grids.Ships.FirstOrDefault(s => s.Name.ToString().Equals(selector, StringComparison.OrdinalIgnoreCase))
                  ?? Grids.Ships.FirstOrDefault(s => s.Name.ToString().Contains(selector, StringComparison.OrdinalIgnoreCase));
        }

        if (chosen is null)
        {
            message = $"Ship '{selector}' not found (use 'ships' to list active ships).";
            return false;
        }

        var com = chosen.ToGlobal(chosen.Mass.CenterOfMass.ToGodot());
        float offset = Math.Max(4f, chosen.LocalExtent.Radius + 3f);
        var spot = com + new Vector3(0f, 2f, offset);
        Player.TeleportTo(spot);
        Player.LookAt(com, Vector3.Up);
        message = FormattableString.Invariant($"Teleported beside {chosen.Name} ({spot.X:0.#}, {spot.Y:0.#}, {spot.Z:0.#}).");
        return true;
    }

    string IDebugConsoleHost.SpawnDemoShip()
    {
        int before = Grids.Ships.Count;
        if (!DemoShip())
            return "Could not build demo ship (no free space or missing hull/thruster blocks in itemset).";
        LaunchConsole();
        return Grids.Ships.Count > before
            ? $"Built and launched {Grids.Ships[^1].Name} ({Grids.Ships[^1].BlockCount} blocks, mass {Grids.Ships[^1].Mass.Mass:0.#})."
            : "Built demo ship structure on the static grid.";
    }

    string IDebugConsoleHost.LaunchTargetedOrNearestShip()
    {
        int before = Grids.Ships.Count;
        LaunchConsole();
        return Grids.Ships.Count > before
            ? $"Launched {Grids.Ships[^1].Name} ({Grids.Ships[^1].BlockCount} blocks)."
            : "No unlaunched console found in the static world.";
    }

    int IDebugConsoleHost.ClearShips()
    {
        if (Player.PilotShip is not null)
            Player.TeleportTo(Player.GlobalPosition);
        var all = Grids.Ships.ToList();
        foreach (var ship in all)
            Grids.RemoveShip(ship);
        return all.Count;
    }

    string IDebugConsoleHost.ImportShip(string path)
    {
        try
        {
            var resolved = Path.GetFullPath(path);
            if (!File.Exists(resolved))
                return $"File not found: {path}";

            using var stream = File.OpenRead(resolved);
            var snapshot = SpaceboxStructureBridge.Import(stream, Path.GetFileNameWithoutExtension(resolved));

            var forward = -Player.GlobalTransform.Basis.Z;
            float distance = Math.Max(12f, snapshot.Size + 6f);
            var spawnPos = Player.GlobalPosition + forward * distance;

            var ship = Grids.AddShip(ShipGrid.SizeFor(snapshot.Size));
            ship.GlobalPosition = spawnPos;
            ship.RestoreMotion(snapshot with { Position = spawnPos.ToNumerics() });
            ship.Import(snapshot.Palette, snapshot.Cells, snapshot.Size);
            ship.ImportObjects(snapshot.Objects);
            ship.ImportTurned(snapshot.Turned);
            ship.ImportConsoles(snapshot.Consoles);

            if (ship.IsEmpty)
            {
                Grids.RemoveShip(ship);
                return "Imported structure was empty (no blocks loaded).";
            }

            return FormattableString.Invariant($"Imported {ship.Name} ({ship.BlockCount} blocks) from {Path.GetFileName(resolved)}.");
        }
        catch (Exception ex)
        {
            return $"Failed to import ship: {ex.Message}";
        }
    }

    string IDebugConsoleHost.ExportShip(string? selector, string path)
    {
        try
        {
            ShipGrid? chosen = null;
            if (string.IsNullOrWhiteSpace(selector))
            {
                chosen = Player.PilotShip ?? (Grids.Ships.Count > 0 ? Grids.Ships[0] : null);
            }
            else if (int.TryParse(selector.TrimStart('#'), System.Globalization.CultureInfo.InvariantCulture, out int index) && index >= 1 && index <= Grids.Ships.Count)
            {
                chosen = Grids.Ships[index - 1];
            }
            else
            {
                chosen = Grids.Ships.FirstOrDefault(s => s.Name.ToString().Equals(selector, StringComparison.OrdinalIgnoreCase))
                    ?? Grids.Ships.FirstOrDefault(s => s.Name.ToString().Contains(selector, StringComparison.OrdinalIgnoreCase));
            }

            if (chosen is null)
                return string.IsNullOrWhiteSpace(selector)
                    ? "No ship to export (pilot a ship or specify ship name / #index)."
                    : $"Ship '{selector}' not found. Use 'ships' to see active ships.";

            var resolved = Path.GetFullPath(path);
            if (!resolved.EndsWith(".sbs", StringComparison.OrdinalIgnoreCase))
                resolved += ".sbs";

            var dir = Path.GetDirectoryName(resolved);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var snapshot = chosen.ToSnapshot();
            using var stream = File.Create(resolved);
            SpaceboxStructureBridge.Export(snapshot, stream, "OpenCorneroids");

            return FormattableString.Invariant($"Exported {chosen.Name} ({chosen.BlockCount} blocks) to {Path.GetFileName(resolved)}.");
        }
        catch (Exception ex)
        {
            return $"Failed to export ship: {ex.Message}";
        }
    }

    string? IDebugConsoleHost.SaveWorld() => Save();

    public void Showcase()
    {
        var palette = Voxels.Palette;
        var singles = palette.GridIndices.ToList();
        var objects = palette.ObjectIndices
            .Select(b => (Block: b, Size: palette.Shape(b)!.RotatedSize(Orientation.Default)))
            .OrderByDescending(o => o.Size.Y).ThenBy(o => palette.Item(o.Block).ItemId).ToList();
        if (singles.Count == 0 && objects.Count == 0) return;

        int columns = Math.Max(10, (int)Math.Ceiling(Math.Sqrt(singles.Count * 16.0 / 9)));
        int rows = (singles.Count + columns - 1) / columns;
        int gridWidth = 2 * columns - 1, gridHeight = singles.Count == 0 ? 0 : 2 * rows - 1;
        const int Gap = 2;

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
        int left = Mathf.RoundToInt(_eye.X - totalWidth / 2f), top = Mathf.RoundToInt(_eye.Y + totalHeight / 2f);

        for (int cz = z + 1; cz <= cameraZ + 1; cz++)
        for (int cy = top - totalHeight - 1; cy <= top; cy++)
        for (int cx = left - 1; cx <= left + totalWidth; cx++)
            Voxels.SetRaw(new Vector3I(cx, cy, cz), BlockPalette.Air);
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
            var origin = new Int3(areaLeft + at.X, areaTop - at.Y, z + 1 - size.Z);
            if (Voxels.AddObject(block, origin, Orientation.Default, clearGrid: true) is null)
                GD.PushWarning($"Showcase: no room for {palette.Item(block).Name} at {origin}");
        }

        Player.Position = new Vector3(_eye.X, _eye.Y, cameraZ + 0.5f);
        Player.LookAt(new Vector3(left + totalWidth / 2f, top - totalHeight / 2f - totalHeight * 0.03f, z + 0.5f), Vector3.Up);
    }

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

    public void DropInFront(IEnumerable<(int ItemId, int Count)> items)
    {
        if (_drops is null) return;
        var at = Player.GlobalPosition - Player.GlobalTransform.Basis.Z;
        foreach (var (itemId, count) in items) _drops.Drop(itemId, count, at);
    }

    FloatingItems? _drops;

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
            if (Player.Mode.PlanObject(new Vector3(-100, -100, -100), new Aim(Voxels, hit, before)) is not { Placeable: true }) continue;
            if (Frame(hit, before, requirePlaceable: true)) return true;
        }
        if (fallback is { } f && Frame(f.Hit, f.Before, requirePlaceable: false)) return true;
        GD.PushWarning("--preview: the camera is not looking at any block");
        return false;
    }

    /// <summary>
    /// <c>--preview-space</c>: selects <paramref name="itemId"/> and positions the camera aiming into empty space near
    /// the first ship (if any, so the red attachment line to its centre of mass shows clearly) or into open space.
    /// </summary>
    public bool PreviewSpace(int itemId)
    {
        if (Voxels.Palette.IndexOf(itemId) is not { } block)
        {
            GD.PushWarning($"--preview-space: item {itemId} is not a placeable block in {Itemset.Header.Name}");
            return false;
        }
        if (!Player.Mode.TrySelectBlock(block))
        {
            Inventory.Add(itemId, 1);
            if (!Player.Mode.TrySelectBlock(block)) GD.PushWarning($"--preview-space: could not put item {itemId} in the hotbar");
        }
        Player.PinnedAim = null;
        if (Grids.Ships.Count > 0)
        {
            var ship = Grids.Ships[^1];
            var com = ship.GlobalTransform * ship.Mass.CenterOfMass.ToGodot();
            float rad = ship.LocalExtent.Radius;
            // Stand near the front-right of the ship and aim alongside its hull into empty space (~7 m off the hull,
            // well within the 16-unit attachment range, without hitting any block along the placement ray).
            var camPos = com + new Vector3(rad + 1.5f, 1.5f, rad + 2.5f);
            var aimPoint = com + new Vector3(rad + 1.2f, 0.5f, 0f);
            LookFrom(camPos, aimPoint);
            Player.Mode.View = (Player.GlobalPosition, -Player.GlobalTransform.Basis.Z, Vector3.Zero);
            return true;
        }
        // Without a ship, step into open space (> 16 units from rock) so --place starts a new floating ship.
        var openSpot = new Vector3(8f, 8f, 8f);
        if (!PlayerMotor.BodyOverlaps(openSpot, Voxels.IsSolid))
            LookFrom(openSpot, openSpot + new Vector3(0, 0, -4f));
        Player.Mode.View = (Player.GlobalPosition, -Player.GlobalTransform.Basis.Z, Vector3.Zero);
        return true;
    }

    public void PlaceSpacePreviewed()
    {
        int shipsBefore = Grids.Ships.Count;
        Player.Mode.View = (Player.GlobalPosition, -Player.GlobalTransform.Basis.Z, Vector3.Zero);
        Player.Mode.PlaceInSpace(Player.GlobalPosition);
        GD.Print(Grids.Ships.Count > shipsBefore
            ? $"--place: started floating ship {Grids.Ships[^1].Name} at {Grids.Ships[^1].Position}"
            : "--place: placed block in space attached to nearest grid");
    }

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

    (Vector3 Position, int Seen, bool MostlySeen)? BestView(Vector3 center, Vector3 size, Vector3 normal, float[] elevations, Func<Vector3, bool> accept)
    {
        float distance = Math.Max(4f, size.Length() * 0.5f / Mathf.Sin(Mathf.DegToRad(37.5f)) * 1.25f);
        var t1 = Mathf.Abs(normal.Y) > 0.5f ? Vector3.Right : Vector3.Up;
        var t2 = normal.Cross(t1);

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

    static string Coordinates(Vector3 p) => FormattableString.Invariant($"{p.X:0.#}, {p.Y:0.#}, {p.Z:0.#}");

    void LookFrom(Vector3 position, Vector3 target)
    {
        Player.Position = position;
        var view = (target - position).Normalized();
        Player.LookAt(target, Mathf.Abs(view.Y) > 0.99f ? Vector3.Forward : Vector3.Up);
    }

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
        var console = palette.Indices
            .Where(b => palette.Item(b).Get<ConsoleComponent>() is { GunsOnly: false })
            .OrderBy(b => palette.IsObject(b))
            .ThenByDescending(b => palette.Item(b).Get<ConsoleComponent>()!.Buttons)
            .ThenBy(b => palette.Item(b).ItemId)
            .Select(b => (ushort?)b).FirstOrDefault()
            ?? palette.Indices.Where(Has<ConsoleComponent>).OrderBy(b => palette.IsObject(b)).ThenBy(b => palette.Item(b).ItemId)
                .Select(b => (ushort?)b).FirstOrDefault();
        var power = palette.Indices.Where(b => palette.Item(b).Block!.Power > 0).OrderByDescending(b => palette.Item(b).Block!.Power)
            .ThenBy(b => palette.Item(b).ItemId).Select(b => (ushort?)b).FirstOrDefault();
        var thruster = palette.ObjectIndices.Where(Has<ThrusterComponent>)
            .OrderBy(b => palette.Shape(b)!.Size.Volume).ThenBy(b => palette.Item(b).ItemId).Select(b => (ushort?)b).FirstOrDefault();
        if (console is null) GD.PushWarning("--demo-ship: the itemset has no console block; the ship can't be launched");
        if (power is null) GD.PushWarning("--demo-ship: the itemset has no power supply block");
        if (thruster is null) GD.PushWarning("--demo-ship: the itemset has no multi-block thruster");

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
        var consoleOrigin = consoleCell;
        if (console is { } c && !palette.IsObject(c)) cells[cells.FindIndex(e => e.Item1 == consoleCell)] = (consoleCell, c);
        if (power is { } pw && !palette.IsObject(pw))
        {
            var cell = new Vector3I(cx, top, min.Z + hullSize.Z / 2);
            cells[cells.FindIndex(e => e.Item1 == cell)] = (cell, pw);
        }
        Voxels.SetCells(cells);

        int placed = 0, wanted = 0;
        var placedObjects = new List<WorldObject>();
        WorldObject? PlaceAgainst(ushort block, Vector3I placeCell, Vector3I normal, Orientation o)
        {
            wanted++;
            var origin = palette.Shape(block)!.AnchorFor(placeCell.ToInt3(), normal.ToInt3(), o);
            if (Voxels.ObjectProblem(block, origin, o) is { } problem)
            {
                GD.PushWarning($"--demo-ship: {palette.Item(block).Name} at {origin}: {problem}");
                return null;
            }
            if (Voxels.AddObject(block, origin, o) is { } added)
            {
                placed++;
                placedObjects.Add(added);
                return added;
            }
            return null;
        }
        if (console is { } co && palette.IsObject(co))
        {
            if (PlaceAgainst(co, consoleCell + Vector3I.Up, Vector3I.Up, Orientation.Default) is { } consoleObj)
            {
                consoleOrigin = consoleObj.Origin.ToGodot();
                consoleCell = consoleObj.ModelCells.First();
            }
        }
        if (power is { } p && palette.IsObject(p))
            PlaceAgainst(p, new Vector3I(cx, top + 1, min.Z + hullSize.Z / 2 - 1), Vector3I.Up, Orientation.Default);

        var mountedThrusters = new Dictionary<Vector3I, List<WorldObject>>();
        if (thruster is { } t)
        {
            var mounts = new (Vector3I Normal, Vector3I[] Cells)[]
            {
                (Vector3I.Right, [new(min.X + hullSize.X, cy, min.Z + 1), new(min.X + hullSize.X, cy, min.Z + hullSize.Z - 2)]),
                (Vector3I.Left, [new(min.X - 1, cy, min.Z + 1), new(min.X - 1, cy, min.Z + hullSize.Z - 2)]),
                (Vector3I.Up, [new(min.X, top + 1, min.Z + hullSize.Z - 2), new(min.X + hullSize.X - 1, top + 1, min.Z + 1)]),
                (Vector3I.Down, [new(min.X, min.Y - 1, min.Z + 1), new(min.X + hullSize.X - 1, min.Y - 1, min.Z + hullSize.Z - 2)]),
                (Vector3I.Back, [new(min.X + 1, cy, min.Z + hullSize.Z), new(min.X + hullSize.X - 2, cy, min.Z + hullSize.Z)]),
                (Vector3I.Forward, [new(min.X + 1, cy, min.Z - 1), new(min.X + hullSize.X - 2, cy, min.Z - 1)]),
            };
            var shape = palette.Shape(t)!;
            var component = palette.Item(t).Get<ThrusterComponent>()!;
            foreach (var (normal, at) in mounts)
            {
                var outward = normal.ToInt3().ToVector3();
                var turned = Orientation.All.FirstOrDefault(turn =>
                    System.Numerics.Vector3.Distance(ThrusterMount.FromShape(shape, Int3.Zero, turn, component).LocalFlameDirection, outward) < 1e-3f);
                var list = new List<WorldObject>();
                foreach (var cell in at)
                    if (PlaceAgainst(t, cell, normal, turned) is { } addedThruster)
                        list.Add(addedThruster);
                mountedThrusters[normal] = list;
            }
        }
        _demoConsole = console is null ? null : consoleCell;
        int cables = WireDemoShip(cells.Where(c => c.Item2 == hullBlock).Select(c => c.Item1).ToHashSet(),
            cells.Where(c => c.Item2 != hullBlock).Select(c => c.Item1).Concat(placedObjects.SelectMany(o => o.ModelCells)));

        // Pre-bind the demo ship's console colour groups so W/S/A/D/Space fly and yaw it immediately in original console mode.
        if (console is { } cIdx && palette.Item(cIdx).Get<ConsoleComponent>() is { GunsOnly: false } cComp)
        {
            var cid = consoleOrigin.ToInt3();
            int buttons = cComp.Buttons;
            void BindGroup(int groupIdx, string key, params WorldObject?[] targets)
            {
                if (groupIdx >= ConsoleBinding.ClampGroupCount(buttons)) return;
                Voxels.Consoles.AssignKey(cid, buttons, false, groupIdx, key);
                var state = Voxels.Consoles.GetOrCreate(cid, buttons, false);
                foreach (var targetObj in targets)
                {
                    if (targetObj is null) continue;
                    // Cycle until it lands in groupIdx.
                    while (state.GroupOf(targetObj.Origin) != groupIdx)
                        if (Voxels.Consoles.CycleTarget(cid, buttons, false, targetObj.Origin, isThruster: true, isGun: false) < 0)
                            break;
                }
            }
            if (mountedThrusters.TryGetValue(Vector3I.Back, out var back))
                BindGroup(0, "W", [.. back]);
            if (mountedThrusters.TryGetValue(Vector3I.Forward, out var front))
                BindGroup(1, "S", [.. front]);
            if (mountedThrusters.TryGetValue(Vector3I.Right, out var right) && mountedThrusters.TryGetValue(Vector3I.Left, out var left))
            {
                BindGroup(2, "A", right.ElementAtOrDefault(0), left.ElementAtOrDefault(1));
                BindGroup(3, "D", left.ElementAtOrDefault(0), right.ElementAtOrDefault(1));
            }
            if (mountedThrusters.TryGetValue(Vector3I.Down, out var down))
                BindGroup(4, "Space", [.. down]);
        }

        var center = (Vector3)min + (Vector3)hullSize * 0.5f;
        var eyeAt = center + new Vector3(0.55f, 0.45f, 1).Normalized() * 20;
        if (VoxelWorld.InBounds((Vector3I)eyeAt.Floor()) && !PlayerMotor.BodyOverlaps(eyeAt, Voxels.IsSolid))
            LookFrom(eyeAt, center);
        else if (BestView(center, (Vector3)hullSize + Vector3.One * 2, new Vector3(0.55f, 0.45f, 1).Normalized(), [65, 50, 80], _ => true) is { } view)
            LookFrom(view.Position, center);
        GD.Print($"--demo-ship: hull of {palette.Item(hullBlock).Name} at {min}, console {(console is { } k ? palette.Item(k).Name : "none")}, " +
                 $"power {(power is { } w ? palette.Item(w).Name : "none")}, {placed} of {wanted} multi-blocks placed, {cables} cable blocks");
        return true;
    }

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

    const int DemoClearAhead = 40;

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
            GD.PushWarning($"--pilot: {ship.Name} has no powered console");
            return false;
        }
        GD.Print($"--pilot: piloting {ship.Name} from its console at {cell}, {view} view");
        return true;
    }

    public bool BindFirstConsole(bool closeUp)
    {
        foreach (var grid in Grids.Ships.Cast<BlockGrid>().Append(Voxels))
        {
            Vector3I? consoleCell = null;
            ConsoleComponent? comp = null;
            foreach (var (cell, block) in grid.Blocks())
                if (grid.Palette.Item(block).Get<ConsoleComponent>() is { } c)
                {
                    (consoleCell, comp) = (cell, c);
                    break;
                }
            if (consoleCell is null)
            {
                foreach (var obj in grid.Objects)
                    if (grid.Palette.Item(obj.Block).Get<ConsoleComponent>() is { } c)
                    {
                        (consoleCell, comp) = (obj.Origin.ToGodot(), c);
                        break;
                    }
            }
            if (consoleCell is not { } foundCell || comp is null) continue;

            Player.EquipConsoleBindTool(grid, foundCell, comp);
            if (grid is ShipGrid ship)
            {
                var com = ship.ToGlobal(ship.Mass.CenterOfMass.ToGodot());
                if (closeUp)
                {
                    var consoleCenter = ship.ToGlobal((Vector3)foundCell + new Vector3(0.5f, 0.5f, 0.5f));
                    var deckEye = consoleCenter + new Vector3(0f, 1.15f, 1.0f);
                    var thrusterObj = ship.Objects
                        .Where(o => ship.Palette.Item(o.Block).Get<ThrusterComponent>() is not null)
                        .OrderBy(o => ship.ToGlobal(o.ModelBox.GetCenter()).DistanceSquaredTo(deckEye))
                        .FirstOrDefault();
                    if (thrusterObj is not null)
                    {
                        var targetWorld = ship.ToGlobal(thrusterObj.ModelBox.GetCenter());
                        LookFrom(deckEye, targetWorld);
                    }
                }
                else
                {
                    var eyePos = com + new Vector3(0.65f, 0.42f, 0.85f).Normalized() * (ship.LocalExtent.Radius * 1.45f + 2.2f);
                    LookFrom(eyePos, com);
                }
            }
            GD.Print($"--bind-console: equipped binding tool for console at {foundCell} ({(closeUp ? "close" : "wide")})");
            return true;
        }
        GD.PushWarning("--bind-console: no console block in the world or ships (try --demo-ship)");
        return false;
    }

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
