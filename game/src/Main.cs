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
    ulong _measureStartMs;
    /// <summary>Parsed itemsets by entry, for matching a save's fingerprint; Def is null when the itemset does not parse.</summary>
    readonly Dictionary<ItemsetEntry, (ItemsetDef? Def, DiagnosticBag Diagnostics)> _parsed = new();

    public override void _Ready()
    {
        _args = GameArgs.Parse(OS.GetCmdlineUserArgs());
        GameInput.Register();
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

        WorldView? world = null;
        if (_args.Load is { } name)
        {
            if (WorldSaves.FindFile(name) is { } file) world = LoadWorld(file);
            else ShowMenu($"There is no saved world called '{name}'.");
        }
        else if ((_args.Itemset ?? (_args.NewWorld is not null ? "auto" : null)) is { } choice)
        {
            if (PickItemset(choice, out var problem) is { } entry)
            {
                var packs = _args.TexturePacks.Select(_library.FindPack).OfType<PackInfo>().ToList();
                var worldName = _args.NewWorld is { Length: > 0 } n ? n : WorldSaves.NextDefaultName();
                world = StartNewWorld(new NewWorldSettings(entry, packs, worldName, _args.Seed ?? WorldSaves.DefaultSeed, _args.Creative));
            }
            else
            {
                GD.Print($"--itemset={choice}: {problem}");
                ShowMenu($"--itemset={choice}: {problem}");
            }
        }
        else
        {
            ShowMenu(null);
        }

        if (_args.NewWorld is not null || _args.QuitAfterSave)
        {
            var error = world is null ? "no world was started" : world.Save();
            // With --frames the world runs that long first (held controls, a flying ship) and is saved then.
            if (_args.QuitAfterSave && ((_args.Frames is null && _args.Seconds is null) || world is null))
            {
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
        if (++_worldFrames == measureFrom) _measureStartMs = Time.GetTicksMsec();
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
        if (seconds > 0 && measured > 0) GD.Print($"Measured {measured / seconds:0} FPS over {measured} frames");
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
    /// --open-doors, --preview, --preview-space, --place, --place-on-ship, --preview-ship and --focus apply to the first
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
        // Piloting last: the hooks above aim and place from where the player stands.
        if (_args.FlightAssist is { } flightAssist) world.Player.FlightAssist = flightAssist;
        if (_args.FixedCamera is { } fixedCam) world.Player.FixedCamera = fixedCam;
        if (_args.Dampeners is { } dampeners) world.Player.Dampeners = dampeners;
        if (_args.Pilot is { } view)
            world.PilotFirstShip(view switch { "chase" => FlyCamera.PilotView.Chase, "fixed" => FlyCamera.PilotView.Fixed, _ => FlyCamera.PilotView.Seat });
        if (_args.Holds.Count > 0) world.Player.Hold(_args.Holds);
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

    /// <summary>Esc twice: save, then the menu. If saving fails the world stays until the player decides.</summary>
    void LeaveWorld(WorldView world)
    {
        if (world.Save() is not { } error)
        {
            ShowMenu(null);
            return;
        }
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
            onCancel?.Invoke();
        });
    }

    // ---- loading ----

    /// <summary>
    /// Reads a save and finds the itemset it needs. An exact fingerprint match loads silently; the same itemset from
    /// the same pack with edited content loads with a warning, or after a confirmation when blocks the world uses are
    /// gone; a same-key itemset from another pack always asks first; anything else is refused with the name of the
    /// itemset to install. Never throws for a bad file.
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
        if (!match.OtherPack && check.Status != BindingStatus.Incompatible) return OpenSaved(saved, path, entry, check, acceptedLoss: 0);

        if (!saved.Creative && saved.Inventory.Any(s => s is not null))
            question.Add("Inventory items are matched by item id, so some may now be different items.");
        question.Add("The world is bound to this itemset from its next save on. Load anyway?");
        var menu = _screen as MainMenu ?? ShowMenu(null);
        menu.Confirm(match.OtherPack ? "Different pack" : "Itemset changed", string.Join("\n\n", question),
            () => OpenSaved(saved, path, entry, check, acceptedLoss: cells), okText: "Load anyway");
        return null;
    }

    /// <param name="acceptedLoss">Lost cells the player already agreed to; any loss beyond it asks again.</param>
    WorldView? OpenSaved(WorldSnapshot saved, string path, ItemsetEntry entry, BindingResult check, int acceptedLoss)
    {
        var packs = new List<PackInfo>();
        foreach (var id in saved.TexturePacks)
        {
            if (_library.FindPack(id) is { } pack) packs.Add(pack);
            else GD.PushWarning($"World '{saved.Name}' uses texture pack '{id}', which is not installed; loading without it");
        }

        var info = new WorldInfo(saved.Name, saved.Seed, saved.Creative, saved.TexturePacks, path);
        var world = StartWorld(entry, packs, info, saved);
        if (world is null) return null;
        GD.Print($"Loaded world '{saved.Name}' from {path} ({check.Status}): {check.Message}");

        var notes = new List<string>();
        if (check.Status != BindingStatus.Exact) notes.Add(check.Message);
        if (world.InventoryNote is { } inventory) notes.Add(inventory);
        if (packs.Count < saved.TexturePacks.Count) notes.Add("Some of this world's texture packs are not installed.");
        if (notes.Count > 0)
        {
            var text = string.Join("  ", notes);
            GD.PushWarning(text);
            world.Flash(text, warning: true, seconds: 10);
        }
        ApplyHooks(world);

        // The palette also drops blocks without a usable texture (e.g. a texture pack that supplied it is gone), and a
        // bigger save loses what lies outside this world. None of that shows in the binding check, so it is asked here,
        // before anything can save over the file that still has those blocks.
        if (world.LostCells > acceptedLoss)
        {
            GD.PushWarning($"{world.LostCells} placed block(s) of '{saved.Name}' could not be restored");
            world.HoldSaves = true;
            AskInWorld(world, "Blocks could not be restored",
                $"{world.LostCells} placed block(s) of '{saved.Name}' could not be restored and are empty space now: this itemset " +
                "and its textures can't show their block type, or they lie outside the world.\n\n" +
                "The save file still has them. Keep playing, and the next save (F5, leaving or closing) drops them for good; " +
                "or go back to the menu without saving and restore the missing content first.",
                "Back to menu", "Keep playing",
                onOk: () => ShowMenu($"'{saved.Name}' was not changed."),
                onCancel: () => world.HoldSaves = false);
        }
        return world;
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
            .Select(c => (c.Entry, Check: SaveBinding.Check(saved, c.Def!, c.Entry.Pack.Id)))
            .ToList();

        foreach (var c in candidates)
            if (c.Check.Status == BindingStatus.Exact) return new(c.Entry, c.Check, false);

        var own = candidates.Where(c => IsSavedPack(c.Entry)).ToList();
        var pool = own.Count > 0 ? own : candidates;
        if (pool.Count > 0)
        {
            var best = pool.MinBy(c => ((int)c.Check.Status, c.Check.MissingBlockIds.Count));
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
        menu.NewWorldRequested += settings => StartNewWorld(settings);
        menu.LoadRequested += path => LoadWorld(path);
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
