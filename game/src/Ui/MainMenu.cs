using Godot;
using OpenCorneroids.Content;
using OpenCorneroids.Content.Discovery;
using OpenCorneroids.Game.Content;
using OpenCorneroids.Game.Persistence;
using OpenCorneroids.Simulation.Persistence;

namespace OpenCorneroids.Game.Ui;

/// <summary>
/// Two halves: "Continue" lists saved worlds (load or delete), "New world" picks an itemset, texture packs, a name,
/// a seed and the rules for a fresh one.
/// </summary>
public partial class MainMenu : Control
{
    public event Action<NewWorldSettings>? NewWorldRequested;
    /// <summary>The save file to load.</summary>
    public event Action<string>? LoadRequested;

    static readonly Color Dim = new(1, 1, 1, 0.6f);
    static readonly Color ErrorColor = new(1f, 0.5f, 0.45f);

    ContentLibrary _library = null!;
    ItemList _itemsets = null!;
    List<ItemsetEntry> _itemsetOrder = [];
    readonly List<(CheckBox Box, PackInfo Pack)> _texturePacks = new();
    LineEdit _name = null!, _seed = null!;
    CheckBox _creative = null!;
    Label _newWorldError = null!;
    VBoxContainer _continue = null!;
    /// <summary>The Continue rows' backgrounds, recoloured when the selection moves.</summary>
    readonly List<StyleBoxFlat> _rowStyles = new();
    int _selectedWorld;
    /// <summary>One per Continue row: the file, and the summary when its header reads (null for a damaged save).</summary>
    readonly List<SavedWorld> _saved = new();
    Button _load = null!, _delete = null!;

    public void Setup(ContentLibrary library, IReadOnlyList<string> roots, string? error)
    {
        _library = library;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var background = new ColorRect { Color = new Color(0.03f, 0.035f, 0.06f) };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(background);

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride($"margin_{side}", 28);
        AddChild(margin);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        margin.AddChild(column);

        column.AddChild(Text("Open Corneroids", 36));
        column.AddChild(Text("Bring Your Own Assets", 16, Dim));
        if (error is not null) column.AddChild(Text(error, 15, ErrorColor, wrap: true));

        if (library.Itemsets.Count == 0)
        {
            column.AddChild(Text(
                "No Corneroids content found. Copy your Corneroids install and any mods into:\n\n" +
                $"    {ByoaPaths.UserRoot}\n\n" +
                "(see README.txt there), then restart.\n\nFolders searched:\n    " + string.Join("\n    ", roots),
                16, wrap: true));
            return;
        }

        var halves = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        halves.AddThemeConstantOverride("separation", 32);
        column.AddChild(halves);
        _continue = Half(halves);
        var create = Half(halves);
        BuildContinue();
        BuildNewWorld(create);

        var baseText = library.BaseInstall is { } b ? $"Base install: {b.Id} (vanilla {b.VanillaVersion})" : "No vanilla install found";
        column.AddChild(Text($"{baseText}.  {library.Packs.Count} packs in: {string.Join(", ", roots)}.  Worlds: {WorldSaves.Folder}",
            12, new Color(1, 1, 1, 0.45f), wrap: true));
    }

    static VBoxContainer Half(HBoxContainer parent)
    {
        var half = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1 };
        half.AddThemeConstantOverride("separation", 8);
        parent.AddChild(half);
        return half;
    }

    // ---- continue ----

    /// <summary>(Re)builds the saved-world list; called again after a delete.</summary>
    void BuildContinue()
    {
        foreach (var child in _continue.GetChildren()) child.QueueFree();
        _continue.AddChild(Text("Continue", 22));
        _saved.Clear();
        _saved.AddRange(WorldSaves.ListChecked());
        if (_saved.Count == 0)
        {
            _continue.AddChild(Text("No saved worlds yet. Create one on the right; F5 saves while playing, and leaving to the menu or " +
                                    "closing the window saves too.", 15, Dim, wrap: true));
            return;
        }

        // Hand-made rows rather than an ItemList, whose rows are a single line: the itemset, pack, mode and date go on
        // a second line that wraps, so a long world, itemset or pack name never pushes the date out of sight.
        var rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", 2);
        _rowStyles.Clear();
        for (int i = 0; i < _saved.Count; i++)
        {
            var (path, world, problem) = _saved[i];
            // A damaged save is listed anyway (after the healthy ones), so the player sees the damage before picking it,
            // can delete the file, and learns why its name is taken.
            var details = problem is not null
                ? $"Damaged save{(world is null ? "" : $" of '{world.Name}'")}: {problem} Delete removes the file."
                : world is null ? "" : $"{world.Itemset.Name} [{world.Itemset.PackId}], {(world.Creative ? "creative" : "survival")}, " +
                  $"saved {world.SavedAt.LocalDateTime:yyyy-MM-dd HH:mm}" +
                  // A copied or renamed file keeps the name inside it; the file tells two such rows apart.
                  (Path.GetFileName(path).Equals(WorldStore.FileNameFor(world.Name), StringComparison.OrdinalIgnoreCase)
                      ? "" : $", file '{Path.GetFileName(path)}'");
            // Damaged rows go by their file name: the name inside may be another, healthy world's.
            rows.AddChild(WorldRow(i, problem is null && world is not null ? world.Name : Path.GetFileNameWithoutExtension(path),
                details, problem is not null));
        }
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 200),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        scroll.AddChild(rows);
        _continue.AddChild(scroll);
        // The newest healthy world, so Load (or Enter) never starts with a broken one picked.
        SelectWorld(Math.Max(0, _saved.FindIndex(w => !w.Damaged)));

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        _load = BigButton("Load");
        _load.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _load.Pressed += () => Load(_selectedWorld);
        _delete = BigButton("Delete");
        _delete.Pressed += () => AskDelete(_selectedWorld);
        buttons.AddChild(_load);
        buttons.AddChild(_delete);
        _continue.AddChild(buttons);
    }

    /// <summary>One Continue entry: the name, and below it a wrapping line of details. Click selects, double click loads.</summary>
    Control WorldRow(int index, string name, string details, bool damaged)
    {
        var style = new StyleBoxFlat { ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 4, ContentMarginBottom = 5 };
        _rowStyles.Add(style);
        var row = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Stop };
        row.AddThemeStyleboxOverride("panel", style);
        var lines = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        lines.AddThemeConstantOverride("separation", 0);
        var title = Text(name, 16, damaged ? ErrorColor : null);
        var info = Text(details, 13, damaged ? ErrorColor : Dim, wrap: true);
        title.MouseFilter = info.MouseFilter = MouseFilterEnum.Ignore;
        title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        title.ClipText = true;
        lines.AddChild(title);
        lines.AddChild(info);
        row.AddChild(lines);
        row.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click) return;
            SelectWorld(index);
            if (click.DoubleClick) Load(index);
        };
        return row;
    }

    void SelectWorld(int index)
    {
        _selectedWorld = index;
        for (int i = 0; i < _rowStyles.Count; i++)
            _rowStyles[i].BgColor = i == index ? new Color(1, 1, 1, 0.16f) : new Color(1, 1, 1, 0.04f);
    }

    void Load(int index)
    {
        if (index >= 0 && index < _saved.Count) LoadRequested?.Invoke(_saved[index].FilePath);
    }

    void AskDelete(int index)
    {
        if (index < 0 || index >= _saved.Count) return;
        var (path, summary, _) = _saved[index];
        var name = summary?.Name ?? Path.GetFileNameWithoutExtension(path);
        Confirm("Delete world", $"Delete '{name}' for good?\n\n{path}\n\nThis cannot be undone.", () =>
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Confirm("Delete failed", $"Could not delete '{name}': {e.Message}", null);
            }
            BuildContinue();
        }, okText: "Delete");
    }

    // ---- new world ----

    void BuildNewWorld(VBoxContainer parent)
    {
        parent.AddChild(Text("New world", 22));

        _itemsets = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 150) };
        _itemsets.AddThemeFontSizeOverride("font_size", 15);
        // The base install's itemset first (it is the default choice), then the rest in scan order.
        _itemsetOrder = _library.Itemsets.OrderBy(e => e.Pack == _library.BaseInstall ? 0 : 1).ToList();
        foreach (var entry in _itemsetOrder)
        {
            var h = entry.Header;
            var tag = entry.Pack == _library.BaseInstall ? "  (base)" : "";
            _itemsets.AddItem($"{h.Name} - {h.Author}    [{entry.Pack.Id}]{tag}");
        }
        _itemsets.Select(0);
        _itemsets.ItemActivated += _ => Create();
        parent.AddChild(_itemsets);

        var packs = _library.Packs.Where(p => p.Kind == PackKind.TexturePack).ToList();
        if (packs.Count > 0)
        {
            var row = new HFlowContainer();
            row.AddChild(Text("Texture packs:", 15));
            foreach (var pack in packs)
            {
                var box = new CheckBox { Text = pack.Id };
                row.AddChild(box);
                _texturePacks.Add((box, pack));
            }
            parent.AddChild(row);
        }

        var fields = new GridContainer { Columns = 2 };
        fields.AddThemeConstantOverride("h_separation", 10);
        fields.AddChild(Text("Name", 15));
        fields.AddChild(_name = new LineEdit { Text = WorldSaves.NextDefaultName(), SizeFlagsHorizontal = SizeFlags.ExpandFill });
        fields.AddChild(Text("Seed", 15));
        fields.AddChild(_seed = new LineEdit
        {
            Text = ((int)(GD.Randi() % 1_000_000)).ToString(),
            PlaceholderText = "number or any text; empty for random",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        });
        parent.AddChild(fields);
        _name.TextSubmitted += _ => Create();
        _seed.TextSubmitted += _ => Create();

        parent.AddChild(_creative = new CheckBox { Text = "Creative (instant mining, every block, no inventory)" });
        parent.AddChild(_newWorldError = Text("", 14, ErrorColor, wrap: true));

        var play = BigButton("Create world");
        play.Pressed += Create;
        parent.AddChild(play);
        play.GrabFocus();
    }

    void Create()
    {
        var name = _name.Text.Trim();
        if (name.Length == 0)
        {
            _newWorldError.Text = "Give the world a name.";
            return;
        }
        if (WorldSaves.Exists(name))
        {
            _newWorldError.Text = $"A saved world already uses the name '{name}' (or one that maps to the same file). Pick another name, or load or delete it on the left.";
            return;
        }
        var entry = _itemsetOrder[Selected(_itemsets) is var i and >= 0 ? i : 0];
        var packs = _texturePacks.Where(t => t.Box.ButtonPressed).Select(t => t.Pack).ToList();
        NewWorldRequested?.Invoke(new NewWorldSettings(entry, packs, name, WorldSaves.ParseSeed(_seed.Text), _creative.ButtonPressed));
    }

    // ---- helpers ----

    /// <summary>A modal yes/no question; with a null <paramref name="onOk"/> it is just a notice with one button.</summary>
    public void Confirm(string title, string text, Action? onOk, string okText = "OK") => Dialogs.Show(this, title, text, onOk, okText);

    static int Selected(ItemList list) => list.GetSelectedItems() is [var selected, ..] ? selected : -1;

    static Button BigButton(string text)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 40) };
        button.AddThemeFontSizeOverride("font_size", 18);
        return button;
    }

    static Label Text(string text, int size, Color? color = null, bool wrap = false)
    {
        var label = new Label { Text = text, AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off };
        label.AddThemeFontSizeOverride("font_size", size);
        if (color is { } c) label.AddThemeColorOverride("font_color", c);
        return label;
    }
}
