using Godot;
using OpenCorneroids.Content;
using OpenCorneroids.Content.Discovery;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Content;
using OpenCorneroids.Game.Persistence;
using OpenCorneroids.Game.Player;
using OpenCorneroids.Game.Ui;
using OpenCorneroids.Game.World;
using OpenCorneroids.Simulation.Persistence;

namespace OpenCorneroids.Game;

/// <summary>
/// Scans BYOA content, then switches between the menu and a world. Owns the save points that are not a key press:
/// leaving to the menu and closing the window both save the running world first.
/// </summary>
public partial class Main : Node
{
    GameArgs _args = null!;
    ContentLibrary _library = null!;
    IReadOnlyList<string> _roots = [];
    Node? _screen;
    int _worldFrames;
    /// <summary>Game time since the screen started, each frame counted up to <see cref="ShipPhysics.MaxFrame"/>.</summary>
    double _worldSeconds;
    /// <summary>The timed screenshot or save is done; quitting may take another frame, which must not do it again.</summary>
    bool _timedDone;
    bool _hooksApplied;
    bool _leavingWorld;
    ulong _measureStartMs;
    /// <summary>Parsed itemsets by entry, for matching a save's fingerprint; Def is null when the itemset does not parse.</summary>
    readonly Dictionary<ItemsetEntry, (ItemsetDef? Def, DiagnosticBag Diagnostics)> _parsed = new();

    public override void _Ready()
    {
        _args = GameArgs.Parse(OS.GetCmdlineUserArgs());
        GameInput.Register();
        Settings.GameSettings.Load();
        // Closing the window must save first; _Notification accepts the quit once that is done.
        GetTree().AutoAcceptQuit = false;
        // The UI is laid out for 1280x720 and scaled to the window (project.godot, stretch mode canvas_items), so a
        // much smaller window would shrink the text past reading.
        DisplayServer.WindowSetMinSize(new Vector2I(960, 540));

        _roots = ByoaPaths.FindRoots(_args);
        _library = ContentLibrary.Scan(_roots);
        foreach (var d in _library.Diagnostics.AtLeast(Severity.Warning)) GD.PushWarning(d.ToString());
        GD.Print($"BYOA roots: {string.Join("; ", _roots)}; {_library.Packs.Count} packs, {_library.Itemsets.Count} itemsets");
        GD.Print($"Worlds folder: {WorldSaves.Folder}");

        bool immediateQuit = _args.QuitAfterSave && _args.Frames is null && _args.Seconds is null;
        WorldView? world = null;
        if (_args.Load is { } name)
        {
            if (WorldSaves.FindFile(name) is { } file)
            {
                if (immediateQuit) world = LoadWorld(file);
                else _ = LoadWorldAsync(file);
            }
            else ShowMenu($"There is no saved world called '{name}'.");
        }
        else if ((_args.Itemset ?? (_args.NewWorld is not null ? "auto" : null)) is { } choice)
        {
            if (PickItemset(choice, out var problem) is { } entry)
            {
                var packs = _args.TexturePacks.Select(_library.FindPack).OfType<PackInfo>().ToList();
                var worldName = _args.NewWorld is { Length: > 0 } n ? n : WorldSaves.NextDefaultName();
                var settings = new NewWorldSettings(entry, packs, worldName, _args.Seed ?? WorldSaves.DefaultSeed, _args.Creative);
                if (immediateQuit) world = StartNewWorld(settings);
                else _ = StartNewWorldAsync(settings);
            }
            else
            {
                GD.Print($"--itemset={choice}: {problem}");
                ShowMenu($"--itemset={choice}: {problem}");
            }
        }
        else
        {
            var menu = ShowMenu(null);
            if (_args.Menu is { } page) menu.OpenPage(page);
        }

        if (_args.NewWorld is not null || _args.QuitAfterSave)
        {
            // With --frames or --seconds the world (started in the background) runs that long first (held controls, a
            // flying ship) and is saved then, in _Process; only an untimed run saves and quits here.
            if (immediateQuit)
            {
                var error = world is null ? "no world was started" : world.Save();
                GD.Print(error is not null ? $"Not saved ({error}); quitting"
                    : world!.HoldSaves ? "Not saved (the load is waiting for a confirmation); quitting"
                    : "Saved; quitting (--quit-after-save)");
                GetTree().Quit(error is null ? 0 : 1);
            }
        }
    }

    /// <summary>
    /// Closing the window saves first and quits only once that worked. A failed save (a file locked by a sync client or
    /// a virus scanner, a full disk) keeps the game open and asks, so an hour of building is not thrown away silently.
    /// </summary>
    public override void _Notification(int what)
    {
        if (what != NotificationWMCloseRequest) return;
        if (_screen is WorldView world && world.Save() is { } error)
        {
            AskInWorld(world, "Could not save", $"{error}\n\nQuit anyway? Everything since the last successful save is lost.",
                "Quit without saving", "Keep playing", Quit);
            return;
        }
        Quit();

        void Quit()
        {
            GetTree().AutoAcceptQuit = true;
            GetTree().Quit();
        }
    }

    double _nextRecord = 0.3;
    int _recorded;

    /// <summary>Frame of a <c>--screenshot</c> when <c>--frames</c> does not say.</summary>
    const int DefaultScreenshotFrame = 240;

    public override void _Process(double delta)
    {
        // --screenshot: let the screen (menu or world) render for a few seconds (--frames), save what's on screen, quit.
        // --quit-after-save with --frames: run the world that long, then save it and quit.
        bool timed = _args.Frames is not null || _args.Seconds is not null;
        bool deferredSave = _args.QuitAfterSave && timed;
        if (_timedDone || _screen is null || (_args.ScreenshotPath is null && !deferredSave) || (_args.QuitAfterSave && !deferredSave)) return;
        int target = _args.Frames ?? DefaultScreenshotFrame;
        // Skip the loading hitch: measure from frame 60, or from a quarter of the way for a short run.
        int measureFrom = _args.Seconds is null ? Math.Min(60, target / 4) : 60;
        if (++_worldFrames == measureFrom)
        {
            _measureStartMs = Time.GetTicksMsec();
            PerfMonitor.Timers.ResetRun();
        }
        _worldSeconds += Math.Min(Math.Max(0, delta), ShipPhysics.MaxFrame);
        if (_args.RecordDir is { } record && _worldSeconds >= _nextRecord)
        {
            System.IO.Directory.CreateDirectory(record);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(record, $"frame_{_recorded++:0000}.png"));
            _nextRecord = _worldSeconds + 1.0 / 15;
        }
        if (_args.Seconds is { } due ? _worldSeconds < due : _worldFrames != target) return;
        var seconds = (Time.GetTicksMsec() - _measureStartMs) / 1000.0;
        int measured = _worldFrames - measureFrom;
        if (seconds > 0 && measured > 0)
        {
            GD.Print($"Measured {measured / seconds:0} FPS over {measured} frames");
            GD.Print($"Frame timers (ms per frame, average/worst): {PerfMonitor.RunSummary()}");
        }
        var moment = FormattableString.Invariant($"frame {_worldFrames}, {_worldSeconds:0.###} s");
        _timedDone = true;
        if (deferredSave)
        {
            string? saveError = _screen is WorldView world
                ? world.HoldSaves ? "the load is waiting for a confirmation" : world.Save()
                : "no world is running";
            GD.Print(saveError is null ? $"Saved at {moment}; quitting (--quit-after-save)" : $"Not saved ({saveError}); quitting");
            GetTree().Quit(saveError is null ? 0 : 1);
            return;
        }
        GD.Print($"Taking the screenshot at {moment}");
        var error = GetViewport().GetTexture().GetImage().SavePng(_args.ScreenshotPath!);
        GD.Print(error == Error.Ok ? $"Screenshot saved to {_args.ScreenshotPath}" : $"Screenshot failed: {error}");
        GetTree().Quit();
    }

    ItemsetEntry? PickItemset(string choice, out string? problem)
    {
        problem = null;
        if (choice == "auto")
        {
            var entry = _library.Itemsets.FirstOrDefault(e => e.Pack == _library.BaseInstall) ?? _library.Itemsets.FirstOrDefault();
            if (entry is null) problem = "No itemsets were found.";
            return entry;
        }
        return _library.FindItemset(choice, out problem);
    }

    async Task<WorldView?> StartNewWorldAsync(NewWorldSettings settings)
    {
        var loading = new LoadingScreen();
        loading.Setup(settings.Name);
        Swap(loading);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        await loading.SetStep("Loading itemset & texture packs...", 0.15);

        var info = new WorldInfo(settings.Name, settings.Seed, settings.Creative, settings.TexturePacks.Select(p => p.Id).ToList(),
            WorldSaves.PathFor(settings.Name));

        var loaded = await Task.Run(() => _library.Load(settings.Itemset, settings.TexturePacks));
        foreach (var d in loaded.Diagnostics.AtLeast(Severity.Warning)) GD.PushWarning(d.ToString());
        if (loaded.Itemset is null || loaded.BlockAtlas is null)
        {
            ShowMenu($"Could not load {settings.Itemset.Header.Name}: " +
                     string.Join("; ", loaded.Diagnostics.AtLeast(Severity.Error).Take(3)));
            return null;
        }

        await loading.SetStep("Initializing voxel world...", 0.35, completedNotice: "Itemset & textures loaded");

        var world = new WorldView { Mapping = _args.TextureMapping };
        world.ExitRequested += () => LeaveWorld(world);

        bool ok = await world.SetupAsync(loaded, info, saved: null, async (step, frac, detail, completed) =>
        {
            await loading.SetStep(step, frac, detail, completed);
        });

        if (!ok)
        {
            ShowMenu($"{settings.Itemset.Header.Name} has no placeable single-cell blocks with textures.");
            return null;
        }

        await loading.SetStep("Finalizing world...", 0.98, completedNotice: "Asteroids generated");

        // Into the scene first, as a loaded world is: the hooks move and turn the player, which needs the tree.
        Swap(world);
        if (_args.Showcase) world.Showcase();
        if (_args.DemoShip) world.DemoShip();
        if (!_hooksApplied && !world.Creative) world.Give(_args.Give);
        if (!_hooksApplied && !world.Creative) world.DropInFront(_args.Drop);
        ApplyHooks(world);
        return world;
    }

    WorldView? StartNewWorld(NewWorldSettings settings)
    {
        var info = new WorldInfo(settings.Name, settings.Seed, settings.Creative, settings.TexturePacks.Select(p => p.Id).ToList(),
            WorldSaves.PathFor(settings.Name));
        var world = StartWorld(settings.Itemset, settings.TexturePacks, info, saved: null);
        if (world is null) return null;
        if (_args.Showcase) world.Showcase();
        if (_args.DemoShip) world.DemoShip();
        // --give describes the first world only; a world picked later from the menu starts plain
        // (item ids there may mean something else entirely).
        if (!_hooksApplied && !world.Creative) world.Give(_args.Give);
        if (!_hooksApplied && !world.Creative) world.DropInFront(_args.Drop);
        ApplyHooks(world);
        return world;
    }

    /// <summary>--open, --orientation, --launch, --turn-ship, --flight-assist, --fixed-camera, --dampeners, --pilot, --hold,
    /// --open-doors, --preview, --preview-space, --place, --place-on-ship, --preview-ship, --focus and --beacon apply to the first
    /// world, new or loaded, so a loaded world can be screenshotted the same way.</summary>
    void ApplyHooks(WorldView world)
    {
        if (_hooksApplied) return;
        _hooksApplied = true;
        // A screenshot run takes no input: stray mouse or wheel events reaching the window would change the selection
        // or the view, and two runs with the same arguments would differ.
        if (_args.ScreenshotPath is not null || (_args.QuitAfterSave && (_args.Frames is not null || _args.Seconds is not null))) world.Player.InputLocked = true;
        if (_args.DebugHud) world.Player.DebugHud = true;
        if (_args.Orientation is { } orientation) world.Player.Mode.Orientation = Simulation.Shapes.Orientation.FromIndex(orientation);
        if (_args.Launch) world.LaunchConsole();
        if (_args.TurnShip is { } degrees) world.TurnShips(degrees);
        if (_args.BindConsole is { } bindMode) world.BindFirstConsole(bindMode == "close");
        if (_args.PlaceOnShip is { } onShip) world.AimAtShip(onShip, place: true);
        if (_args.OpenDoors) world.OpenDoors();
        if (_args.Preview is { } item && world.Preview(item) && _args.Place) world.PlacePreviewed();
        else if (_args.PreviewSpace is { } spaceItem && world.PreviewSpace(spaceItem))
        {
            if (_args.Place) world.PlaceSpacePreviewed();
        }
        else if (_args.Place) GD.PushWarning("--place: needs a --preview or --preview-space");
        if (_args.PreviewShip is { } shipItem) world.AimAtShip(shipItem, place: false);
        if (_args.Focus is { } focus) world.Focus(focus);
        if (_args.Select is { } selected) world.SelectItem(selected);
        if (_args.Beacon is { } beacon)
        {
            world.Player.EquipBeaconTool();
            if (beacon.Length > 0 && !world.Player.PlaceBeacon(beacon)) GD.PushWarning($"--beacon: '{beacon}' is not a beacon name");
        }
        // Piloting last: the hooks above aim and place from where the player stands.
        if (_args.FlightAssist is { } flightAssist) world.Player.FlightAssist = flightAssist;
        if (_args.FixedCamera is { } fixedCam) world.Player.FixedCamera = fixedCam;
        if (_args.Dampeners is { } dampeners) world.Player.Dampeners = dampeners;
        // With no ship yet, piloting waits for the --console commands, which may build one ("stress ship 6000").
        var pilotView = _args.Pilot switch
        {
            null => (FlyCamera.PilotView?)null,
            "chase" => FlyCamera.PilotView.Chase,
            "fixed" => FlyCamera.PilotView.Fixed,
            _ => FlyCamera.PilotView.Seat,
        };
        bool pilotAfterCommands = pilotView is not null && world.Grids.Ships.Count == 0 && _args.ConsoleCommands.Count > 0;
        if (pilotView is { } view && !pilotAfterCommands) world.PilotFirstShip(view);
        if (_args.Holds.Count > 0) world.Player.Hold(_args.Holds);
        world.RunAutoexec();
        foreach (var cmd in _args.ConsoleCommands)
            world.DebugConsole.RunCommand(cmd);
        if (pilotAfterCommands) world.PilotFirstShip(pilotView!.Value);
        if (_args.Open is { } window) world.OpenWindow(window);
    }

    WorldView? StartWorld(ItemsetEntry entry, IReadOnlyList<PackInfo> texturePacks, WorldInfo info, WorldSnapshot? saved)
    {
        var loaded = _library.Load(entry, texturePacks);
        foreach (var d in loaded.Diagnostics.AtLeast(Severity.Warning)) GD.PushWarning(d.ToString());
        if (loaded.Itemset is null || loaded.BlockAtlas is null)
        {
            ShowMenu($"Could not load {entry.Header.Name}: " +
                     string.Join("; ", loaded.Diagnostics.AtLeast(Severity.Error).Take(3)));
            return null;
        }

        var world = new WorldView { Mapping = _args.TextureMapping };
        world.ExitRequested += () => LeaveWorld(world);
        Swap(world);
        if (!world.Setup(loaded, info, saved))
        {
            ShowMenu($"{entry.Header.Name} has no placeable single-cell blocks with textures.");
            return null;
        }
        return world;
    }

    /// <summary>Save and exit (the Game menu): save, then the menu. If saving fails the world stays until the player decides.</summary>
    async void LeaveWorld(WorldView world)
    {
        if (_leavingWorld) return;
        _leavingWorld = true;

        world.Player.InputLocked = true;
        world.Flash("Saving world...", warning: false, seconds: 5);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        string? error = null;
        try
        {
            error = world.Save();
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        if (error is null)
        {
            ShowMenu(null);
            _leavingWorld = false;
            return;
        }

        _leavingWorld = false;
        world.Player.InputLocked = false;
        AskInWorld(world, "Could not save", $"{error}\n\nLeave to the menu anyway? Everything since the last successful save is lost.",
            "Leave without saving", "Keep playing", () => ShowMenu($"'{world.Info.Name}' was not saved: {error}"));
    }

    /// <summary>
    /// A question over the running world, which is paused meanwhile (no input, no physics, no F5). Cancel resumes it;
    /// the mouse is released so the dialog can be clicked.
    /// </summary>
    void AskInWorld(WorldView world, string title, string text, string okText, string cancelText, Action onOk, Action? onCancel = null)
    {
        world.ProcessMode = ProcessModeEnum.Disabled;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Dialogs.Show(this, title, text, onOk, okText, cancelText, () =>
        {
            if (!IsInstanceValid(world)) return;
            world.ProcessMode = ProcessModeEnum.Inherit;
            // The F3 frame timer would otherwise count the whole pause as one frame.
            PerfMonitor.Timers.SkipFrame();
            onCancel?.Invoke();
        });
    }

    // ---- loading ----

    async Task<WorldView?> LoadWorldAsync(string path)
    {
        var loading = new LoadingScreen();
        loading.Setup(Path.GetFileNameWithoutExtension(path));
        Swap(loading);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        await loading.SetStep("Reading world save...", 0.10);

        WorldSnapshot saved;
        try
        {
            saved = await Task.Run(() => WorldStore.Load(path));
        }
        catch (Exception e) when (e is WorldSaveException or IOException or UnauthorizedAccessException)
        {
            ShowMenu($"Could not load {Path.GetFileName(path)}: {e.Message}");
            return null;
        }

        await loading.SetStep("Resolving itemset compatibility...", 0.20, completedNotice: $"World save loaded: {saved.Name}");

        var match = ResolveItemset(saved);
        if (match.Entry is not { } entry || match.Check.Status == BindingStatus.DifferentItemset)
        {
            ShowMenu(match.Check.Message);
            return null;
        }
        var check = match.Check;

        if (AskBeforeLoading(saved, entry, match) is not { } ask)
            return await OpenSavedAsync(saved, path, entry, check, acceptedCells: 0, acceptedItems: 0, loading);
        var menu = ShowMenu(null);
        menu.Confirm(ask.Title, ask.Text,
            () => _ = OpenSavedAsync(saved, path, entry, check, ask.Cells, ask.Items, null), okText: "Load anyway");
        return null;
    }

    /// <summary>
    /// Reads a save and finds the itemset it needs. An exact fingerprint match loads silently; the same itemset from
    /// the same pack with edited content loads with a warning, or after a confirmation when blocks the world uses are
    /// gone or the player's items no longer match; a same-key itemset from another pack always asks first; anything
    /// else is refused with the name of the itemset to install (see <see cref="AskBeforeLoading"/>). Never throws for
    /// a bad file.
    /// </summary>
    WorldView? LoadWorld(string path)
    {
        WorldSnapshot saved;
        try
        {
            saved = WorldStore.Load(path);
        }
        catch (Exception e) when (e is WorldSaveException or IOException or UnauthorizedAccessException)
        {
            ShowMenu($"Could not load {Path.GetFileName(path)}: {e.Message}");
            return null;
        }

        var match = ResolveItemset(saved);
        if (match.Entry is not { } entry || match.Check.Status == BindingStatus.DifferentItemset)
        {
            ShowMenu(match.Check.Message);
            return null;
        }
        var check = match.Check;

        if (AskBeforeLoading(saved, entry, match) is not { } ask)
            return OpenSaved(saved, path, entry, check, acceptedCells: 0, acceptedItems: 0);
        var menu = _screen as MainMenu ?? ShowMenu(null);
        menu.Confirm(ask.Title, ask.Text, () => OpenSaved(saved, path, entry, check, ask.Cells, ask.Items), okText: "Load anyway");
        return null;
    }

    /// <summary>A question to ask before loading a save, and the losses the player accepts by answering yes.</summary>
    /// <param name="Cells">Placed blocks that will become empty space.</param>
    /// <param name="Items">Inventory and suit items that will find no room.</param>
    sealed record LoadQuestion(string Title, string Text, int Cells, long Items);

    /// <summary>
    /// Whether loading needs a yes first, and the question: always for a same-key itemset from another pack, when blocks
    /// the world uses are gone, and when the player's items no longer match (<see cref="BindingResult.AsksAboutItems"/>),
    /// with the real counts. Null loads straight away.
    /// </summary>
    static LoadQuestion? AskBeforeLoading(WorldSnapshot saved, ItemsetEntry entry, ItemsetMatch match)
    {
        var check = match.Check;
        if (!match.OtherPack && check.Status != BindingStatus.Incompatible && !check.AsksAboutItems) return null;

        int cells = 0;
        var question = new List<string>();
        if (match.OtherPack)
        {
            question.Add($"'{saved.Name}' was made with {saved.Itemset.Name} from '{saved.Itemset.PackId}', which is not installed. " +
                         $"{entry.Header.Name} from '{entry.Pack.Id}' uses the same itemset key ({entry.Header.Key}) but is another " +
                         "pack: blocks and items keep their ids, but may behave differently or be different things.");
        }
        else if (check.Status != BindingStatus.Exact)
            question.Add($"'{saved.Name}' was saved with a different version of {saved.Itemset.Name} from '{saved.Itemset.PackId}'.");
        if (check.Status == BindingStatus.Incompatible)
        {
            var missing = check.MissingBlockIds.ToHashSet();
            cells = saved.Cells.Count(c => c != WorldSnapshot.Air && missing.Contains(saved.Palette[c])) +
                    saved.Objects.Count(o => missing.Contains(o.BlockId)) +
                    saved.Ships.Sum(ship => ship.Cells.Count(c => c != WorldSnapshot.Air && missing.Contains(ship.Palette[c])) +
                                            ship.Objects.Count(o => missing.Contains(o.BlockId)));
            question.Add($"{check.MissingBlockIds.Count} block type(s) the world uses do not exist in {entry.Header.Name} from " +
                         $"'{entry.Pack.Id}' (blockIds {string.Join(", ", check.MissingBlockIds)}). At least {cells} placed block(s) " +
                         "of those types will become empty space.");
        }
        long items = 0;
        if (check.AsksAboutItems && check.ItemProblems.Describe(entry.Header.Name) is { } itemNotes)
        {
            question.Add("Inventory and suit: " + itemNotes);
            items = check.ItemProblems.LostItems;
        }
        else if (match.OtherPack && saved.Inventory.Any(s => s is not null))
            question.Add("Inventory items are matched by item id, so some may now be different items.");

        bool rebinds = match.OtherPack || check.Status != BindingStatus.Exact;
        question.Add(rebinds ? "The world is bound to this itemset from its next save on. Load anyway?" : "Load anyway?");
        var title = match.OtherPack ? "Different pack"
            : check.Status == BindingStatus.Exact ? "Items can't all be restored"
            : "Itemset changed";
        return new LoadQuestion(title, string.Join("\n\n", question), cells, items);
    }

    async Task<WorldView?> OpenSavedAsync(WorldSnapshot saved, string path, ItemsetEntry entry, BindingResult check, int acceptedCells, long acceptedItems, LoadingScreen? loading)
    {
        if (loading is null)
        {
            loading = new LoadingScreen();
            loading.Setup(saved.Name);
            Swap(loading);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        await loading.SetStep("Finding texture packs...", 0.25, completedNotice: $"Itemset '{entry.Header.Name}' matched");

        var packs = new List<PackInfo>();
        foreach (var id in saved.TexturePacks)
        {
            if (_library.FindPack(id) is { } pack) packs.Add(pack);
            else GD.PushWarning($"World '{saved.Name}' uses texture pack '{id}', which is not installed; loading without it");
        }

        var savePath = path.EndsWith(".wrl", StringComparison.OrdinalIgnoreCase)
            ? Path.ChangeExtension(path, WorldStore.Extension)
            : path;
        var info = new WorldInfo(saved.Name, saved.Seed, saved.Creative, saved.TexturePacks, savePath);

        await loading.SetStep("Loading textures and content...", 0.32);

        var loaded = await Task.Run(() => _library.Load(entry, packs));
        foreach (var d in loaded.Diagnostics.AtLeast(Severity.Warning)) GD.PushWarning(d.ToString());
        if (loaded.Itemset is null || loaded.BlockAtlas is null)
        {
            ShowMenu($"Could not load {entry.Header.Name}: " +
                     string.Join("; ", loaded.Diagnostics.AtLeast(Severity.Error).Take(3)));
            return null;
        }

        await loading.SetStep("Creating world...", 0.38, completedNotice: "Textures & materials loaded");

        var world = new WorldView { Mapping = _args.TextureMapping };
        world.ExitRequested += () => LeaveWorld(world);

        bool ok = await world.SetupAsync(loaded, info, saved, async (step, frac, detail, completed) =>
        {
            await loading.SetStep(step, frac, detail, completed);
        });

        if (!ok)
        {
            ShowMenu($"{entry.Header.Name} has no placeable single-cell blocks with textures.");
            return null;
        }

        await loading.SetStep("Entering world...", 0.98, completedNotice: "World rebuilt");

        Swap(world);
        GD.Print($"Loaded world '{saved.Name}' from {path} ({check.Status}): {check.Message}");

        var notes = new List<string>();
        if (check.Status != BindingStatus.Exact) notes.Add(check.Message);
        notes.AddRange(world.LoadNotes);
        if (packs.Count < saved.TexturePacks.Count) notes.Add("Some of this world's texture packs are not installed.");
        if (notes.Count > 0)
        {
            var text = string.Join("  ", notes);
            GD.PushWarning(text);
            world.Flash(text, warning: true, seconds: 10);
        }
        ApplyHooks(world);

        GuardLosses(world, saved, acceptedCells, acceptedItems);
        return world;
    }

    /// <param name="acceptedCells">Lost cells the player already agreed to; any loss beyond it asks again.</param>
    /// <param name="acceptedItems">Lost items the player already agreed to; likewise.</param>
    WorldView? OpenSaved(WorldSnapshot saved, string path, ItemsetEntry entry, BindingResult check, int acceptedCells, long acceptedItems)
    {
        var packs = new List<PackInfo>();
        foreach (var id in saved.TexturePacks)
        {
            if (_library.FindPack(id) is { } pack) packs.Add(pack);
            else GD.PushWarning($"World '{saved.Name}' uses texture pack '{id}', which is not installed; loading without it");
        }

        var savePath = path.EndsWith(".wrl", StringComparison.OrdinalIgnoreCase)
            ? Path.ChangeExtension(path, WorldStore.Extension)
            : path;
        var info = new WorldInfo(saved.Name, saved.Seed, saved.Creative, saved.TexturePacks, savePath);
        var world = StartWorld(entry, packs, info, saved);
        if (world is null) return null;
        GD.Print($"Loaded world '{saved.Name}' from {path} ({check.Status}): {check.Message}");

        var notes = new List<string>();
        if (check.Status != BindingStatus.Exact) notes.Add(check.Message);
        notes.AddRange(world.LoadNotes);
        if (packs.Count < saved.TexturePacks.Count) notes.Add("Some of this world's texture packs are not installed.");
        if (notes.Count > 0)
        {
            var text = string.Join("  ", notes);
            GD.PushWarning(text);
            world.Flash(text, warning: true, seconds: 10);
        }
        ApplyHooks(world);

        GuardLosses(world, saved, acceptedCells, acceptedItems);
        return world;
    }

    /// <summary>
    /// Holds saves and asks when a load lost more than the player already agreed to: placed blocks (the palette also
    /// drops blocks without a usable texture, e.g. a texture pack that supplied it is gone, and a bigger save loses what
    /// lies outside this world; none of that shows in the binding check) or inventory and suit items that found no room.
    /// It is asked before anything can save over the file that still has them.
    /// </summary>
    void GuardLosses(WorldView world, WorldSnapshot saved, int acceptedCells, long acceptedItems)
    {
        bool cells = world.LostCells > acceptedCells, items = world.LostItems > acceptedItems;
        if (!cells && !items) return;
        var lost = new List<string>();
        if (cells)
        {
            GD.PushWarning($"{world.LostCells} placed block(s) of '{saved.Name}' could not be restored");
            lost.Add($"{world.LostCells} placed block(s) of '{saved.Name}' could not be restored and are empty space now: this itemset " +
                     "and its textures can't show their block type, or they lie outside the world.");
        }
        if (items)
        {
            GD.PushWarning($"{world.LostItems} item(s) of '{saved.Name}' could not be restored");
            lost.Add($"{world.LostItems} item(s) of the inventory and suit of '{saved.Name}' found no room in the inventory and are gone now.");
        }
        world.HoldSaves = true;
        var title = !items ? "Blocks could not be restored" : !cells ? "Items could not be restored" : "Blocks and items could not be restored";
        AskInWorld(world, title,
            string.Join("\n\n", lost) + "\n\n" +
            "The save file still has them. Keep playing, and the next save (F5, leaving or closing) drops them for good; " +
            "or go back to the menu without saving and restore the missing content first.",
            "Back to menu", "Keep playing",
            onOk: () => ShowMenu($"'{saved.Name}' was not changed."),
            onCancel: () => world.HoldSaves = false);
    }

    /// <summary>What <see cref="ResolveItemset"/> found.</summary>
    /// <param name="OtherPack">The itemset comes from a pack other than the one the world was saved with.</param>
    readonly record struct ItemsetMatch(ItemsetEntry? Entry, BindingResult Check, bool OtherPack);

    /// <summary>
    /// The installed itemset that best fits a save: one with the saved fingerprint if any (the saved pack first). Else,
    /// when the saved pack is installed, its own same-key itemset, even if blocks are missing: another pack that happens
    /// to define those blocks is not what the world was built with. Only without the saved pack does another same-key
    /// pack stand in (best check first), and that is flagged so loading asks.
    /// </summary>
    ItemsetMatch ResolveItemset(WorldSnapshot saved)
    {
        var binding = saved.Itemset;
        bool IsSavedPack(ItemsetEntry e) => e.Pack.Id.Equals(binding.PackId, StringComparison.OrdinalIgnoreCase);
        var sameKey = _library.Itemsets.Where(e => e.Header.Key.Equals(binding.Key, StringComparison.Ordinal)).ToList();
        var candidates = sameKey
            .OrderByDescending(IsSavedPack)
            .Select(e => (Entry: e, Def: Parse(e)))
            .Where(c => c.Def is not null)
            // The same inventory size and fill order the load will use (WorldView), so the prompt and the load agree.
            .Select(c => (c.Entry, Check: SaveBinding.Check(saved, c.Def!, c.Entry.Pack.Id, InventorySize.SlotCountFor(saved),
                InventorySize.FillOrderFor(saved))))
            .ToList();

        foreach (var c in candidates)
            if (c.Check.Status == BindingStatus.Exact) return new(c.Entry, c.Check, false);

        var own = candidates.Where(c => IsSavedPack(c.Entry)).ToList();
        var pool = own.Count > 0 ? own : candidates;
        if (pool.Count > 0)
        {
            var best = pool.MinBy(c => ((int)c.Check.Status, c.Check.MissingBlockIds.Count, c.Check.ItemProblems.LostItems));
            return new(best.Entry, best.Check, own.Count == 0);
        }

        // Nothing usable. Say whether the itemset is missing or present but broken: a pack whose itemset file no longer
        // parses (a hand edit gone wrong) is in the folder, and "put the pack back" would send the player looking for it.
        string message;
        if ((sameKey.FirstOrDefault(IsSavedPack) ?? sameKey.FirstOrDefault()) is { } broken)
            message = $"'{saved.Name}' needs the itemset '{binding.Name}' ({binding.Key}). '{broken.Header.Name}' from '{broken.Pack.Id}' " +
                      $"is installed, but its itemset file could not be read: {ParseError(broken)}";
        else
            message = $"'{saved.Name}' needs the itemset '{binding.Name}' ({binding.Key}) from '{binding.PackId}', which is not installed. " +
                      "Put that pack back in a BYOA folder and restart to play this world.";
        return new(null, new BindingResult(BindingStatus.DifferentItemset, [], message), false);
    }

    /// <summary>Parsing alone gives the fingerprint; atlases are only loaded for the itemset actually played.</summary>
    ItemsetDef? Parse(ItemsetEntry entry)
    {
        if (!_parsed.TryGetValue(entry, out var parsed))
        {
            var diagnostics = new DiagnosticBag();
            _parsed[entry] = parsed = (ItemsetParser.Parse(entry.Pack.Source, entry.Path, diagnostics), diagnostics);
        }
        return parsed.Def;
    }

    /// <summary>Why <see cref="Parse"/> returned null for this entry, for the player.</summary>
    string ParseError(ItemsetEntry entry)
    {
        Parse(entry);
        return _parsed[entry].Diagnostics.AtLeast(Severity.Error).FirstOrDefault()?.ToString() ?? "unknown error";
    }

    // ---- screens ----

    /// <param name="error">Shown as the menu's banner and also logged, so a failed load or save is in the log
    /// (and the console) and not only on screen.</param>
    MainMenu ShowMenu(string? error)
    {
        if (error is not null) GD.PushWarning(error);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        var menu = new MainMenu();
        menu.NewWorldRequested += settings => _ = StartNewWorldAsync(settings);
        menu.LoadRequested += path => _ = LoadWorldAsync(path);
        Swap(menu);
        menu.Setup(_library, _roots, error);
        return menu;
    }

    void Swap(Node screen)
    {
        _screen?.QueueFree();
        _screen = screen;
        _worldFrames = 0;
        _worldSeconds = 0;
        AddChild(screen);
    }
}
