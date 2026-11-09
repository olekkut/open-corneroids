using System.Globalization;
using Godot;
using OpenCorneroids.Game.Settings;
using OpenCorneroids.Simulation.Settings;
using Rules = OpenCorneroids.Simulation.Settings.Cvars;

namespace OpenCorneroids.Game.Ui;

/// <summary>
/// The Options windows, as in the original: Gameplay, Graphics and Controls (its Network and Sounds buttons did nothing
/// and the original has no sound, so they are left out). Every control reads and writes a console setting, so the
/// console and the windows always agree; preferences are saved to config.cfg as they change.
/// </summary>
public static partial class OptionsMenu
{
    static CvarStore Cvars => GameSettings.Cvars;

    /// <summary>Opens "Options" over <paramref name="parent"/> (the main menu or the in-game pause menu).</summary>
    public static MenuOverlay Open(Node parent)
    {
        var options = MenuOverlay.Create("Options", new Vector2(300, 220));
        options.Body.AddChild(new Control { CustomMinimumSize = new Vector2(0, 2) });
        options.AddButton("Gameplay", () => Gameplay(parent));
        options.AddButton("Graphics", () => Graphics(parent));
        options.AddButton("Controls", () => Controls(parent));
        options.Body.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });
        options.AddButton("Back", options.Close);
        parent.AddChild(options);
        return options;
    }

    /// <summary>Opens one page directly ("gameplay", "graphics" or "controls").</summary>
    public static void OpenPage(Node parent, string page)
    {
        switch (page)
        {
            case "gameplay": Gameplay(parent); break;
            case "graphics": Graphics(parent); break;
            case "controls": Controls(parent); break;
        }
    }

    // ---- Gameplay ----

    static void Gameplay(Node parent)
    {
        var page = MenuOverlay.Create("Gameplay", new Vector2(500, 420));
        page.Body.AddThemeConstantOverride("separation", 22);
        page.Body.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });
        Check(page, "Draw target squares", Rules.DrawTargets);
        Check(page, "View bobbing", Rules.ViewBobbing);
        Check(page, "Fixed camera when aboard", Rules.FixedCamera);
        Check(page, "Show hints", Rules.ShowHints);
        Check(page, "Flight assistant (ours)", Rules.FlightAssist);
        Check(page, "Welcome window on load", Rules.Welcome);

        // Gamma applies on Back; Esc throws the change away, as in the original.
        int gamma = Cvars.Int(Rules.Gamma);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 20);
        row.AddChild(ClassicUi.Label("Gamma", 16));
        var slider = ClassicSlider.Create(0, 50, 1, gamma, 250, v => ((int)v).ToString(CultureInfo.InvariantCulture));
        slider.ValueChanged += v => gamma = (int)v;
        row.AddChild(slider);
        page.Body.AddChild(row);

        var back = ClassicUi.Button("Back", new Vector2(160, 25), 16);
        back.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        back.Pressed += () =>
        {
            Cvars.Set(Rules.Gamma, gamma);
            page.Close();
        };
        page.Body.AddChild(back);
        parent.AddChild(page);
    }

    static void Check(MenuOverlay page, string label, Cvar cvar)
    {
        var check = ClassicCheckBox.Create(label, Cvars.Bool(cvar));
        check.Toggled += on => Cvars.Set(cvar, on);
        page.Body.AddChild(check);
    }

    // ---- Graphics ----

    static readonly (int W, int H)[] Resolutions =
    [
        (1024, 768), (1152, 864), (1280, 720), (1280, 800), (1280, 1024), (1366, 768), (1440, 900), (1600, 900),
        (1680, 1050), (1920, 1080), (1920, 1200), (2560, 1080), (2560, 1440), (3440, 1440), (3840, 2160),
    ];

    static readonly string[] SsaoNames = ["Off", "Low", "High"];
    static readonly (string Name, int Value)[] ViewDistances = [("Short", 50), ("Medium", 100), ("Far", 150)];

    static void Graphics(Node parent)
    {
        var page = MenuOverlay.Create("Graphics", new Vector2(600, 450));
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 30);
        grid.AddThemeConstantOverride("v_separation", 14);
        page.Body.AddChild(grid);

        // As in the original: modes of at least 800×700 that fit the screen, smallest first; ours adds "Desktop".
        var screen = DisplayServer.ScreenGetSize();
        var modes = new List<(int W, int H)> { (0, 0) };
        modes.AddRange(Resolutions.Where(r => r.W <= screen.X && r.H <= screen.Y));
        if (!modes.Contains((screen.X, screen.Y))) modes.Add((screen.X, screen.Y));
        modes = [.. modes.Take(1), .. modes.Skip(1).Distinct().OrderBy(m => m.W * m.H)];
        int currentMode = Math.Max(0, modes.IndexOf((Cvars.Int(Rules.ResolutionWidth), Cvars.Int(Rules.ResolutionHeight))));
        var resolution = ClassicWidgets.DropDown(modes.Select(m => m.W == 0 ? "Desktop" : $"{m.W} x {m.H}"), currentMode, new Vector2(350, 30));
        Row(grid, "Resolution:", resolution);

        var fullscreen = ClassicCheckBox.Create("", Cvars.Bool(Rules.Fullscreen));
        Row(grid, "Fullscreen:", fullscreen);
        var vsync = ClassicCheckBox.Create("", Cvars.Bool(Rules.VSync));
        Row(grid, "V-Sync:", vsync);
        var lighting = ClassicCheckBox.Create("", Cvars.Bool(Rules.Lighting));
        Row(grid, "Lighting:", lighting);
        var ssao = ClassicWidgets.DropDown(SsaoNames, Cvars.Int(Rules.Ssao), new Vector2(200, 30));
        Row(grid, "SSAO:", ssao);
        int distance = Array.FindIndex(ViewDistances, d => d.Value == Cvars.Int(Rules.ViewDistance));
        var view = ClassicWidgets.DropDown(ViewDistances.Select(d => d.Name), distance < 0 ? 1 : distance, new Vector2(200, 30));
        Row(grid, "View distance:", view);
        var fov = ClassicSlider.Create(45, 135, 1, Cvars.Float(Rules.Fov), 200, v => $"{v:0}°");
        Row(grid, "Field of view:", fov);

        // Nothing applies until Ok, which asks first, as in the original; Cancel or Esc throws the changes away.
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 12);
        var ok = ClassicUi.Button("Ok", new Vector2(150, 25), 16);
        var cancel = ClassicUi.Button("Cancel", new Vector2(150, 25), 16);
        ok.Pressed += () => MenuOverlay.Ask(parent, "Save changes?", () =>
        {
            var mode = modes[Math.Max(0, resolution.Selected)];
            Cvars.Set(Rules.ResolutionWidth, mode.W);
            Cvars.Set(Rules.ResolutionHeight, mode.H);
            Cvars.Set(Rules.Fullscreen, fullscreen.Checked);
            Cvars.Set(Rules.VSync, vsync.Checked);
            Cvars.Set(Rules.Lighting, lighting.Checked);
            Cvars.Set(Rules.Ssao, Math.Max(0, ssao.Selected));
            Cvars.Set(Rules.ViewDistance, ViewDistances[Math.Max(0, view.Selected)].Value);
            Cvars.Set(Rules.Fov, Math.Round(fov.Value));
            page.Close();
        }, onNo: page.Close);
        cancel.Pressed += page.Close;
        buttons.AddChild(ok);
        buttons.AddChild(cancel);
        page.Body.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        page.Body.AddChild(buttons);
        parent.AddChild(page);
    }

    static void Row(GridContainer grid, string label, Control control)
    {
        var text = ClassicUi.Label(label, 16);
        text.CustomMinimumSize = new Vector2(150, 30);
        text.VerticalAlignment = VerticalAlignment.Center;
        grid.AddChild(text);
        grid.AddChild(control);
    }

    // ---- Controls ----

    static void Controls(Node parent)
    {
        var page = MenuOverlay.Create("Controls", new Vector2(500, 605));
        var sensitivity = new HBoxContainer();
        sensitivity.AddThemeConstantOverride("separation", 16);
        sensitivity.AddChild(ClassicUi.Label("Mouse sensitivity:", 16));
        var slider = ClassicSlider.Create(0, 3, 0.01, Cvars.Float(Rules.Sensitivity), 180, v => v.ToString("0.00", CultureInfo.InvariantCulture));
        slider.ValueChanged += v => Cvars.Set(Rules.Sensitivity, Math.Truncate(v * 100) / 100);
        sensitivity.AddChild(slider);
        page.Body.AddChild(sensitivity);

        page.Body.AddChild(ClassicUi.Label("Key bindings:", 16));
        var field = ClassicField.Create(6);
        field.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 400), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        field.AddChild(scroll);
        var rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", 2);
        scroll.AddChild(rows);
        page.Body.AddChild(field);

        var keyLabels = new Dictionary<GameInput.Binding, Label>();
        void Refresh()
        {
            foreach (var (binding, label) in keyLabels) label.Text = GameInput.KeyName(GameInput.KeyOf(binding));
        }
        foreach (var binding in GameInput.Bindings)
        {
            var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Stop, CustomMinimumSize = new Vector2(0, 25) };
            var name = ClassicUi.Label(binding.Ours ? $"{binding.Label} (ours)" : binding.Label, 15);
            name.CustomMinimumSize = new Vector2(220, 0);
            var key = ClassicUi.Label("", 15);
            keyLabels[binding] = key;
            row.AddChild(name);
            row.AddChild(key);
            row.GuiInput += e =>
            {
                if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
                    PressAnyKey(parent, binding, Refresh);
            };
            rows.AddChild(row);
        }
        Refresh();

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 12);
        var defaults = ClassicUi.Button("Defaults", new Vector2(160, 25), 16);
        defaults.Pressed += () => MenuOverlay.Ask(parent, "Put every key back to its default?", () =>
        {
            GameInput.ResetBindings();
            GameSettings.WriteUserConfig();
            Refresh();
        });
        var back = ClassicUi.Button("Back", new Vector2(160, 25), 16);
        back.Pressed += page.Close;
        buttons.AddChild(defaults);
        buttons.AddChild(back);
        page.Body.AddChild(buttons);
        parent.AddChild(page);
    }

    /// <summary>"Press any key": the next key binds the row (taken off any other row); Esc cancels.</summary>
    static void PressAnyKey(Node parent, GameInput.Binding binding, Action done)
    {
        var popup = KeyCatcher.Create();
        popup.Caught += key =>
        {
            if (key is { } k && GameInput.Rebindable(k))
            {
                GameInput.Rebind(binding, k);
                GameSettings.WriteUserConfig();
            }
            done();
        };
        parent.AddChild(popup);
    }

    /// <summary>The "Press any key" popup: catches the next key press before anything else sees it.</summary>
    sealed partial class KeyCatcher : MenuOverlay
    {
        public event Action<Key?>? Caught;

        public static KeyCatcher Create()
        {
            var catcher = new KeyCatcher { MouseFilter = MouseFilterEnum.Stop };
            catcher.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
            center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            catcher.AddChild(center);
            var window = ClassicWindow.Create("", bevel: 5, padLeft: 20, padTop: 14, padRight: 20, padBottom: 14);
            window.CustomMinimumSize = new Vector2(200, 50);
            var text = ClassicUi.Label("Press any key", 16);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            window.AddChild(text);
            center.AddChild(window);
            return catcher;
        }

        public override void _Input(InputEvent @event)
        {
            if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
            GetViewport().SetInputAsHandled();
            var code = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
            QueueFree();
            Caught?.Invoke(code == Key.Escape ? null : code);
        }
    }
}
