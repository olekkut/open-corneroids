using Godot;
using OpenCorneroids.Content;
using OpenCorneroids.Content.Discovery;
using OpenCorneroids.Game.Content;
using OpenCorneroids.Game.Persistence;
using OpenCorneroids.Game.World;
using OpenCorneroids.Simulation.Persistence;

namespace OpenCorneroids.Game.Ui;

/// <summary>
/// The main menu, as in the original: a small grey window over a flight through stars and space dust, with
/// Singleplayer (the "Choose world" window, and "Create new world" from it), Multiplayer (not there yet), Options and
/// Exit game. Ours: the damaged-save handling, seeds, texture packs and creative worlds of the earlier menu.
/// </summary>
public partial class MainMenu : Control
{
    public event Action<NewWorldSettings>? NewWorldRequested;
    /// <summary>The save file to load.</summary>
    public event Action<string>? LoadRequested;

    static readonly Color ErrorColor = new(1f, 0.55f, 0.5f);

    ContentLibrary _library = null!;
    IReadOnlyList<string> _roots = [];

    public void Setup(ContentLibrary library, IReadOnlyList<string> roots, string? error)
    {
        _library = library;
        _roots = roots;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        AddChild(new MenuBackground { Name = "Background" });

        // Top-left lines, where the original names its author and version.
        var corner = new VBoxContainer { Position = new Vector2(10, 8), MouseFilter = MouseFilterEnum.Ignore };
        corner.AddChild(ClassicUi.Label("Open Corneroids, a remake of Corneroids (2012) by Mikko Pulkki", 15));
        corner.AddChild(ClassicUi.Label($"Godot {Engine.GetVersionInfo()["string"]}  ·  data: {library.BaseInstall?.Id ?? "no vanilla install found"}", 13,
            new Color(1, 1, 1, 0.7f)));
        AddChild(corner);

        var menu = MenuOverlay.Create("Open Corneroids", new Vector2(300, 225), dim: 0);
        menu.Back = () => { };   // Esc does nothing on the main menu, as in the original
        menu.Body.AddChild(new Control { CustomMinimumSize = new Vector2(0, 2) });
        menu.AddButton("Singleplayer", ChooseWorld);
        var multiplayer = menu.AddButton("Multiplayer", () => { });
        multiplayer.Disabled = true;
        multiplayer.TooltipText = "Not in this version yet";
        menu.AddButton("Options", () => OptionsMenu.Open(this));
        menu.Body.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        menu.AddButton("Exit game", () => MenuOverlay.Ask(this, "Exit Open Corneroids?", () => GetTree().Quit()));
        AddChild(menu);

        if (library.Itemsets.Count == 0)
        {
            MenuOverlay.Tell(this,
                "No Corneroids content found. Copy your Corneroids install and any mods into:\n\n" +
                $"{ByoaPaths.UserRoot}\n\n(see README.txt there), then restart.\n\nFolders searched:\n" + string.Join("\n", roots),
                "No content");
        }
        else if (error is not null)
        {
            MenuOverlay.Tell(this, error, "Error");
        }
    }

    /// <summary>Opens a page at once (<c>--menu</c>, for screenshots).</summary>
    public void OpenPage(string page)
    {
        switch (page)
        {
            case "choose": ChooseWorld(); break;
            case "create": ChooseWorld(); CreateWorld(); break;
            case "options": OptionsMenu.Open(this); break;
            case "gameplay" or "graphics" or "controls": OptionsMenu.Open(this); OptionsMenu.OpenPage(this, page); break;
            default: GD.PushWarning($"--menu: unknown page '{page}' (choose, create, options, gameplay, graphics, controls)"); break;
        }
    }

    // ---- Choose world ----

    void ChooseWorld()
    {
        var page = MenuOverlay.Create("Choose world", new Vector2(500, 655));
        page.Body.AddChild(ClassicUi.Label("Worlds:", 16));
        var list = ClassicWidgets.List(new Vector2(460, 300));
        page.Body.AddChild(list);

        var details = ClassicWindow.Create("Selected world", bevel: 4, padLeft: 16, padTop: 30, padRight: 16, padBottom: 12);
        details.CustomMinimumSize = new Vector2(460, 190);
        var detailBox = new VBoxContainer();
        detailBox.AddThemeConstantOverride("separation", 6);
        details.AddChild(detailBox);
        var name = ClassicUi.Label("", 17);
        var info = ClassicUi.Label("", 14);
        info.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        info.CustomMinimumSize = new Vector2(420, 0);
        var play = ClassicUi.Button("Play!", new Vector2(420, 26), 16);
        play.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        detailBox.AddChild(name);
        detailBox.AddChild(info);
        detailBox.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        detailBox.AddChild(play);
        page.Body.AddChild(details);

        var saved = new List<SavedWorld>();
        void Fill()
        {
            saved.Clear();
            saved.AddRange(WorldSaves.ListChecked());
            list.Clear();
            foreach (var (path, world, problem) in saved)
            {
                // Damaged saves are listed after the healthy ones, by file name, so the player sees the damage.
                var label = problem is null && world is not null ? world.Name : $"{Path.GetFileNameWithoutExtension(path)} (damaged)";
                int row = list.AddItem(label.Length > 50 ? label[..50] : label);
                if (problem is not null) list.SetItemCustomFgColor(row, ErrorColor);
            }
            int first = saved.FindIndex(w => !w.Damaged);
            if (first >= 0) list.Select(first);
            Show(first);
        }
        void Show(int index)
        {
            play.Visible = false;
            if (index < 0 || index >= saved.Count)
            {
                name.Text = saved.Count == 0 ? "No saved worlds yet" : "";
                info.Text = saved.Count == 0 ? "Create one with Create new." : "";
                return;
            }
            var (path, world, problem) = saved[index];
            name.Text = world?.Name ?? Path.GetFileNameWithoutExtension(path);
            if (problem is not null)
            {
                info.Text = $"Damaged save: {problem} Remove selected deletes the file.";
                return;
            }
            info.Text = $"Itemset: {world!.Itemset.Name} [{world.Itemset.PackId}]\n" +
                        $"Saved: {world.SavedAt.LocalDateTime:yyyy-MM-dd HH:mm}\n" +
                        $"Mode: {(world.Creative ? "creative" : "survival")}" +
                        (Path.GetFileName(path).Equals(WorldStore.FileNameFor(world.Name), StringComparison.OrdinalIgnoreCase)
                            ? "" : $"\nFile: {Path.GetFileName(path)}");
            play.Visible = true;
        }
        int Selected() => list.GetSelectedItems() is [var i, ..] ? i : -1;
        list.ItemSelected += i => Show((int)i);
        list.ItemActivated += i => { if (Selected() is var s and >= 0 && !saved[s].Damaged) LoadRequested?.Invoke(saved[s].FilePath); };
        play.Pressed += () => { if (Selected() is var s and >= 0) LoadRequested?.Invoke(saved[s].FilePath); };

        var create = ClassicUi.Button("Create new", new Vector2(280, 25), 16);
        create.Pressed += CreateWorld;
        var remove = ClassicUi.Button("Remove selected", new Vector2(280, 25), 16);
        remove.Pressed += () =>
        {
            if (Selected() is not (var s and >= 0)) return;
            var (path, world, _) = saved[s];
            var label = world?.Name ?? Path.GetFileNameWithoutExtension(path);
            MenuOverlay.Ask(this, $"Delete world {label}?", () =>
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    MenuOverlay.Tell(this, $"Could not delete '{label}': {e.Message}", "Error");
                }
                Fill();
            });
        };
        var back = ClassicUi.Button("Back", new Vector2(165, 25), 16);
        back.Pressed += page.Close;
        var bottom = new HBoxContainer();
        var left = new VBoxContainer();
        left.AddThemeConstantOverride("separation", 10);
        left.AddChild(create);
        left.AddChild(remove);
        bottom.AddChild(left);
        bottom.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        back.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        bottom.AddChild(back);
        page.Body.AddChild(bottom);
        AddChild(page);
        Fill();
    }

    // ---- Create new world ----

    void CreateWorld()
    {
        var page = MenuOverlay.Create("Create new world", new Vector2(460, 350));
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 10);
        page.Body.AddChild(grid);

        var nameBox = ClassicWidgets.TextBox(WorldSaves.NextDefaultName(), maxLength: 30);
        nameBox.CustomMinimumSize = new Vector2(320, 30);
        AddRow(grid, "Name:", nameBox);

        // The base install's itemset first (the default choice), then the rest in scan order.
        var order = _library.Itemsets.OrderBy(e => e.Pack == _library.BaseInstall ? 0 : 1).ToList();
        var itemsets = ClassicWidgets.DropDown(order.Select(e => $"{e.Header.Name} - {e.Header.Author} [{e.Pack.Id}]"), 0, new Vector2(320, 30));
        itemsets.ClipText = true;
        AddRow(grid, "Itemset:", itemsets);

        // Ours: the seed, texture packs and creative rules.
        var seed = ClassicWidgets.TextBox(((int)(GD.Randi() % 1_000_000)).ToString());
        seed.PlaceholderText = "number or any text; empty for random";
        AddRow(grid, "Seed:", seed);

        var packs = new List<(ClassicCheckBox Box, PackInfo Pack)>();
        foreach (var pack in _library.Packs.Where(p => p.Kind == PackKind.TexturePack))
        {
            var box = ClassicCheckBox.Create(pack.Id, false);
            packs.Add((box, pack));
            AddRow(grid, packs.Count == 1 ? "Textures:" : "", box);
        }
        var creative = ClassicCheckBox.Create("Creative (every block, instant mining)", false);
        AddRow(grid, "", creative);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 20);
        var ok = ClassicUi.Button("Ok", new Vector2(150, 25), 16);
        var cancel = ClassicUi.Button("Cancel", new Vector2(150, 25), 16);
        ok.Pressed += () =>
        {
            var name = nameBox.Text.Trim();
            if (name.Length == 0)
            {
                MenuOverlay.Tell(this, "Name must be set");
                return;
            }
            if (WorldSaves.Exists(name))
            {
                MenuOverlay.Tell(this, $"A world called '{name}' already exists. Pick another name, or remove it first.");
                return;
            }
            if (itemsets.Selected < 0 || order.Count == 0)
            {
                MenuOverlay.Tell(this, "Itemset must be chosen");
                return;
            }
            page.Close();
            NewWorldRequested?.Invoke(new NewWorldSettings(order[itemsets.Selected], packs.Where(p => p.Box.Checked).Select(p => p.Pack).ToList(),
                name, WorldSaves.ParseSeed(seed.Text), creative.Checked));
        };
        nameBox.TextSubmitted += _ => ok.EmitSignal(BaseButton.SignalName.Pressed);
        cancel.Pressed += page.Close;
        buttons.AddChild(ok);
        buttons.AddChild(cancel);
        page.Body.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        page.Body.AddChild(buttons);
        AddChild(page);
        nameBox.GrabFocus();
        nameBox.SelectAll();
    }

    static void AddRow(GridContainer grid, string label, Control control)
    {
        var text = ClassicUi.Label(label, 16);
        text.VerticalAlignment = VerticalAlignment.Center;
        grid.AddChild(text);
        grid.AddChild(control);
    }

    /// <summary>A modal yes/no question; with a null <paramref name="onOk"/> it is just a notice with one button.</summary>
    public void Confirm(string title, string text, Action? onOk, string okText = "OK") => Dialogs.Show(this, title, text, onOk, okText);
}

/// <summary>
/// Behind the main menu, as in the original: flying forward at 10 units a second through a field of stars and drifting
/// dust. The stars wrap around a cube that travels with the camera, so the flight never ends.
/// </summary>
public partial class MenuBackground : Node3D
{
    const float Speed = 10, Half = 300;
    Camera3D _camera = null!;
    MultiMeshInstance3D _stars = null!;
    readonly List<Vector3> _points = new();

    public override void _Ready()
    {
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.005f, 0.005f, 0.012f) },
        });
        _camera = new Camera3D { Position = new Vector3(128, 128, 128), Fov = 90, Far = 2000 };
        AddChild(_camera);
        _camera.MakeCurrent();
        _camera.AddChild(SpaceDust.Create());

        var rng = new RandomNumberGenerator { Seed = 2012 };
        const int count = 1500;
        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            InstanceCount = count,
            Mesh = new QuadMesh
            {
                Size = new Vector2(1.2f, 1.2f),
                Material = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                    VertexColorUseAsAlbedo = true,
                },
            },
        };
        for (int i = 0; i < count; i++)
        {
            _points.Add(new Vector3(rng.RandfRange(-Half, Half), rng.RandfRange(-Half, Half), rng.RandfRange(-Half, Half)));
            float b = rng.RandfRange(0.4f, 1f);
            multimesh.SetInstanceColor(i, new Color(b, b, Math.Min(1, b * 1.1f)));
        }
        _stars = new MultiMeshInstance3D { Multimesh = multimesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_stars);
    }

    public override void _Process(double delta)
    {
        _camera.Position += Vector3.Forward * Speed * (float)delta;
        var mm = _stars.Multimesh;
        var center = _camera.Position;
        for (int i = 0; i < _points.Count; i++)
        {
            // Wrap each star into the cube around the camera.
            var p = _points[i] - center;
            p = new Vector3(Wrap(p.X), Wrap(p.Y), Wrap(p.Z));
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity, center + p));
        }
    }

    static float Wrap(float v) => Mathf.PosMod(v + Half, Half * 2) - Half;
}
