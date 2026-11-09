using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Content;

namespace OpenCorneroids.Game.Ui;

/// <summary>The windows of a running world, as in the original: the Esc "Game menu" and the welcome window (F1).</summary>
public static class InGameMenus
{
    /// <summary>
    /// "Game menu": Options, Save and exit (asking "Save game and exit to main menu?" first), Back to game. Esc closes
    /// it, as in the original.
    /// </summary>
    public static MenuOverlay Pause(Node parent, Action saveAndExit)
    {
        var menu = MenuOverlay.Create("Game menu", new Vector2(300, 160));
        menu.Body.AddChild(new Control { CustomMinimumSize = new Vector2(0, 2) });
        menu.AddButton("Options", () => OptionsMenu.Open(parent));
        menu.AddButton("Save and exit", () => MenuOverlay.Ask(parent, "Save game and exit to main menu?", () =>
        {
            menu.Close();
            saveAndExit();
        }));
        menu.AddButton("Back to game", menu.Close);
        parent.AddChild(menu);
        return menu;
    }

    /// <summary>
    /// The welcome window the original shows whenever a world loads (and on F1): a word on the game and three special
    /// blocks, with this itemset's own icons. Ours: a Controls button for the full key list.
    /// </summary>
    public static MenuOverlay Welcome(Node parent, ItemsetDef itemset, ItemIcons icons, Action? openControls)
    {
        var window = MenuOverlay.Create("", new Vector2(620, 520));
        var field = ClassicField.Create(14);
        field.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        window.Body.AddChild(field);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        field.AddChild(column);

        var title = ClassicUi.Label("Welcome to Open Corneroids!", 24);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(title);
        column.AddChild(Text("Mine the asteroids, craft parts from what you dig up, build a ship block by block and fly " +
                             "it out into the field. Watch out for what lives on the rocks."));
        column.AddChild(ClassicUi.Label("Some special blocks:", 18));

        Special(column, icons, itemset.Items.FirstOrDefault(i => i.Block?.Power is > 0),
            "Power generator: thrusters and cannons need power, so build generators into your ship.");
        Special(column, icons, itemset.Items.FirstOrDefault(i => i.Get<ThrusterComponent>() is not null),
            "Thruster: it moves your ship. Bind thrusters to a console block to steer.");
        Special(column, icons, itemset.Items.FirstOrDefault(i => i.Get<GunComponent>() is not null),
            "Cannon: arms your ship against whatever comes for you.");

        column.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        var footer = ClassicUi.Label("You can reopen this screen by pressing F1", 14, new Color(1, 1, 1, 0.85f));
        footer.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(footer);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 16);
        var start = ClassicUi.Button("Start!", new Vector2(200, 30), 17);
        start.Pressed += window.Close;
        buttons.AddChild(start);
        if (openControls is not null)
        {
            var controls = ClassicUi.Button("Controls", new Vector2(160, 30), 16);
            controls.Pressed += () =>
            {
                window.Close();
                openControls();
            };
            buttons.AddChild(controls);
        }
        window.Body.AddChild(buttons);
        parent.AddChild(window);
        return window;
    }

    static Label Text(string text)
    {
        var label = ClassicUi.Label(text, 15);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(540, 0);
        return label;
    }

    static void Special(VBoxContainer column, ItemIcons icons, ItemDef? item, string text)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        var icon = new TextureRect
        {
            CustomMinimumSize = new Vector2(64, 64),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            Texture = item is null ? null : icons.Of(item),
        };
        row.AddChild(icon);
        var label = ClassicUi.Label(text, 15);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(460, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(label);
        column.AddChild(row);
    }
}
