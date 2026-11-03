using Godot;
using OpenCorneroids.Content;
using OpenCorneroids.Content.Discovery;
using OpenCorneroids.Game.Content;
using OpenCorneroids.Game.Ui;
using OpenCorneroids.Game.World;

namespace OpenCorneroids.Game;

/// <summary>Scans BYOA content, then switches between the itemset menu and the world.</summary>
public partial class Main : Node
{
    GameArgs _args = null!;
    ContentLibrary _library = null!;
    IReadOnlyList<string> _roots = [];
    Node? _screen;
    int _worldFrames;
    ulong _measureStartMs;

    public override void _Ready()
    {
        _args = GameArgs.Parse(OS.GetCmdlineUserArgs());
        GameInput.Register();

        _roots = ByoaPaths.FindRoots(_args);
        _library = ContentLibrary.Scan(_roots);
        foreach (var d in _library.Diagnostics.AtLeast(Severity.Warning)) GD.PushWarning(d.ToString());
        GD.Print($"BYOA roots: {string.Join("; ", _roots)}; {_library.Packs.Count} packs, {_library.Itemsets.Count} itemsets");

        if (_args.Itemset is { } choice && PickItemset(choice) is { } entry)
            StartWorld(entry, _args.TexturePacks.Select(_library.FindPack).OfType<PackInfo>().ToList());
        else
            ShowMenu(null);
    }

    public override void _Process(double delta)
    {
        // --screenshot: let the screen (menu or world) render for a few seconds, save what's on screen, quit.
        if (_args.ScreenshotPath is not { } path || _screen is null) return;
        if (++_worldFrames == 60) _measureStartMs = Time.GetTicksMsec();   // skip the loading hitch
        if (_worldFrames != 240) return;
        var seconds = (Time.GetTicksMsec() - _measureStartMs) / 1000.0;
        GD.Print($"Measured {180 / seconds:0} FPS over 180 frames; engine counter {Engine.GetFramesPerSecond()}");
        var error = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print(error == Error.Ok ? $"Screenshot saved to {path}" : $"Screenshot failed: {error}");
        GetTree().Quit();
    }

    ItemsetEntry? PickItemset(string choice)
    {
        if (choice == "auto")
            return _library.Itemsets.FirstOrDefault(e => e.Pack == _library.BaseInstall) ?? _library.Itemsets.FirstOrDefault();
        if (int.TryParse(choice, out var index) && index >= 0 && index < _library.Itemsets.Count)
            return _library.Itemsets[index];
        return _library.Itemsets.FirstOrDefault(e => e.Header.Key.Equals(choice, StringComparison.OrdinalIgnoreCase));
    }

    void StartWorld(ItemsetEntry entry, IReadOnlyList<PackInfo> texturePacks)
    {
        var loaded = _library.Load(entry, texturePacks);
        foreach (var d in loaded.Diagnostics.AtLeast(Severity.Warning)) GD.PushWarning(d.ToString());
        if (loaded.Itemset is null || loaded.BlockAtlas is null)
        {
            ShowMenu($"Could not load {entry.Header.Name}: " +
                     string.Join("; ", loaded.Diagnostics.AtLeast(Severity.Error).Take(3)));
            return;
        }

        var world = new WorldView();
        world.ExitRequested += () => ShowMenu(null);
        Swap(world);
        if (!world.Setup(loaded))
            ShowMenu($"{entry.Header.Name} has no placeable 1x1x1 blocks with textures.");
        else if (_args.Showcase)
            world.Showcase();
    }

    void ShowMenu(string? error)
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        var menu = new MainMenu();
        menu.PlayRequested += StartWorld;
        Swap(menu);
        menu.Setup(_library, _roots, error);
    }

    void Swap(Node screen)
    {
        _screen?.QueueFree();
        _screen = screen;
        _worldFrames = 0;
        AddChild(screen);
    }
}
