using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Content;
using OpenCorneroids.Game.World;
using OpenCorneroids.Simulation.Items;
using OpenCorneroids.Simulation.Shapes;
using OpenCorneroids.Simulation.Ships;

namespace OpenCorneroids.Game.Ui;

/// <summary>
/// The modal in-world windows, styled in the original game's raised light-grey bevelled GUI: the survival inventory and
/// character sheet, a crafting station's blueprints, the <c>"Control block"</c> colour-group key-binding window (with
/// its overlaid <c>"Press any key"</c> prompt), the <c>"Power network"</c> status popup, and the <c>F1</c> controls
/// help screen. While a window is open the mouse is free and clicks outside the panels are swallowed so they never reach
/// the camera as mining or placing. The item windows share one cursor stack (<see cref="Hand"/>), drawn under the mouse;
/// as in the original, a click that is not on a slot while holding it (outside the windows, or on a window's frame
/// between its slots) throws it along the view (<c>sv_item_toss_speed</c>), and so does closing the window
/// (<c>sv_item_close_toss_speed</c>). With <c>cl_item_toss_on_window 0</c> (ours) only a click outside the windows
/// throws it. Also as in the original, a press in the item windows counts only while the other mouse button is up
/// (<c>cl_item_click_needs_release</c>). An open chest shows its <c>"Container"</c> window beside the inventory, the two
/// 50 px apart as in the original.
/// </summary>
public partial class GameWindows : CanvasLayer
{
    Control _root = null!;
    ColorRect _backdrop = null!;
    HBoxContainer _row = null!;
    InventoryPanel? _inventory;
    CraftingPanel? _crafting;
    CharacterPanel? _character;
    ContainerPanel? _container;
    Func<bool>? _containerStillThere;
    HandCursor? _cursor;
    Inventory? _playerInventory;
    ClassicWindow _consolePanel = null!;
    GridContainer _consoleGroups = null!;
    CenterContainer _askKeyCenter = null!;
    ClassicWindow _powerPanel = null!;
    Label _powerBlocksLabel = null!;
    Label _powerBalanceLabel = null!;
    ClassicWindow _helpPanel = null!;

    BlockGrid? _consoleGrid;
    Int3 _consoleCell;
    int _consoleGroupCount;
    bool _consoleGunsOnly;
    int _waitingGroup = -1;
    readonly List<ColorGroupRow> _groupRows = [];
    Action? _onSetCamera;

    /// <summary>Space between windows shown side by side. Ours.</summary>
    const int WindowGap = 12;

    /// <summary>The player's inventory and an open chest are 50 px apart, as in the original.</summary>
    const int ContainerGap = 50;

    public bool IsOpen => _root is not null && _root.Visible;

    /// <summary>True while a chest's window is open; the Use key closes it again, as in the original.</summary>
    public bool IsContainerOpen => IsOpen && _container is { Visible: true };

    /// <summary>The stack held on the cursor, shared by every item window.</summary>
    public ItemHand Hand { get; } = new();

    /// <summary>
    /// Throws a stack out of the windows into space at the given speed along the view; set by the world once floating
    /// items exist. Without it a held stack goes back into the inventory instead.
    /// </summary>
    public Action<ItemStack, float>? Toss { get; set; }

    /// <summary>True while the overlaid <c>"Press any key"</c> prompt is waiting for a key for a colour group.</summary>
    public bool IsListeningForKey => IsOpen && _consolePanel.Visible && _waitingGroup >= 0;

    /// <summary>The station whose blueprints are on screen, or null when another window is open.</summary>
    public CraftingStation? OpenStation { get; private set; }

    /// <param name="endless">Items the player can always take another of (the starting drill and stations).</param>
    public void Setup(Inventory? inventory, Suit? suit, ItemsetDef itemset, ItemIcons icons, IReadOnlyList<int> endless)
    {
        Layer = 2;   // above the HUD
        _root = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.GuiInput += OnRootInput;
        AddChild(_root);

        _backdrop = new ColorRect { Color = new Color(0, 0, 0, 0.25f), Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_backdrop);

        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(center);

        _row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        _row.AddThemeConstantOverride("separation", WindowGap);
        center.AddChild(_row);

        if (inventory is not null && suit is not null)
            EnsureSurvival(inventory, suit, itemset, icons, endless);

        _consolePanel = BuildConsolePanel();
        _row.AddChild(_consolePanel);

        _powerPanel = BuildPowerPanel();
        _row.AddChild(_powerPanel);

        _helpPanel = BuildHelpPanel();
        _row.AddChild(_helpPanel);

        // Overlaid "Press any key" bevelled popup centred over "Control block", as in the original.
        _askKeyCenter = new CenterContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _askKeyCenter.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var askBox = ClassicWindow.Create(bevel: 5, padLeft: 18, padTop: 12, padRight: 18, padBottom: 12);
        askBox.CustomMinimumSize = new Vector2(210, 54);
        var askLabel = ClassicUi.Label("Press any key", 16);
        askLabel.HorizontalAlignment = HorizontalAlignment.Center;
        askLabel.VerticalAlignment = VerticalAlignment.Center;
        askLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        askLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        askBox.AddChild(askLabel);
        _askKeyCenter.AddChild(askBox);
        _root.AddChild(_askKeyCenter);
    }

    /// <summary>Creates the survival inventory, character and crafting panels if the world started in creative mode.</summary>
    public void EnsureSurvival(Inventory inventory, Suit suit, ItemsetDef itemset, ItemIcons icons, IReadOnlyList<int> endless)
    {
        if (_inventory is not null && _character is not null) return;
        _crafting = new CraftingPanel { Name = "Crafting", Visible = false, SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        _row.AddChild(_crafting);
        _row.MoveChild(_crafting, 0);
        _crafting.Setup(inventory, itemset, icons);
        _character = new CharacterPanel { Name = "Character", Visible = false, SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        _row.AddChild(_character);
        _row.MoveChild(_character, 1);
        _inventory = new InventoryPanel { Name = "Inventory", Visible = false, SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        _row.AddChild(_inventory);
        _row.MoveChild(_inventory, 2);
        _inventory.Setup(inventory, Hand, itemset, icons, endless);
        _character.Setup(suit, inventory, Hand, itemset, icons);
        _container = new ContainerPanel { Name = "Container", Visible = false, SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        _row.AddChild(_container);
        _row.MoveChild(_container, 3);
        _container.Setup(Hand, itemset, icons);
        _playerInventory = inventory;
        _cursor = new HandCursor { Name = "HandCursor" };
        _root.AddChild(_cursor);
        _cursor.Setup(Hand, itemset, icons);
    }

    /// <summary>
    /// A press that no slot took (slots accept their clicks, window frames pass them on): with a stack on the cursor it
    /// is thrown, as in the original. With <c>cl_item_toss_on_window 0</c> (ours) a press on a window's frame does nothing.
    /// </summary>
    void OnRootInput(InputEvent @event)
    {
        if (Hand.IsEmpty || @event is not InputEventMouseButton { Pressed: true } press
            || press.ButtonIndex is not (MouseButton.Left or MouseButton.Right)) return;
        if (!Settings.GameSettings.Cvars.Bool(Simulation.Settings.Cvars.ItemTossOnWindow) && IsOnAWindow(press.GlobalPosition)) return;
        TossHeld(Settings.GameSettings.Cvars.Float(Simulation.Settings.Cvars.ItemTossSpeed));
        _root.AcceptEvent();
    }

    bool IsOnAWindow(Vector2 at)
    {
        foreach (var child in _row.GetChildren())
            if (child is Control { Visible: true } panel && panel.GetGlobalRect().HasPoint(at)) return true;
        return false;
    }

    /// <summary>
    /// As in the original, a press in the item windows counts only while the other mouse button is up, so pressing the
    /// second button while holding the first does nothing (<c>cl_item_click_needs_release</c>; 0, ours, lets it act).
    /// </summary>
    bool IsChordPress(InputEvent @event) =>
        ItemWindowShown
        && @event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left or MouseButton.Right } press
        && Input.IsMouseButtonPressed(press.ButtonIndex == MouseButton.Left ? MouseButton.Right : MouseButton.Left)
        && Settings.GameSettings.Cvars.Bool(Simulation.Settings.Cvars.ItemClickNeedsRelease);

    bool ItemWindowShown => _inventory is { Visible: true } || _character is { Visible: true } || _crafting is { Visible: true }
        || _container is { Visible: true };

    /// <summary>Throws the cursor stack into space, or puts it back into the inventory when nothing can float.</summary>
    void TossHeld(float speed)
    {
        if (Hand.IsEmpty) return;
        if (Toss is { } toss)
        {
            if (Hand.Release() is { } stack) toss(stack, speed);
        }
        else if (_playerInventory is not null)
        {
            Hand.ReturnTo(_playerInventory);
        }
    }

    ClassicWindow BuildConsolePanel()
    {
        var panel = ClassicWindow.Create("Control block", bevel: 5, padLeft: 32, padTop: 42, padRight: 28, padBottom: 22);
        panel.Name = "ControlBlock";
        panel.Visible = false;
        panel.CustomMinimumSize = new Vector2(460, 400);
        panel.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;

        var vbox = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        vbox.AddThemeConstantOverride("separation", 12);
        panel.AddChild(vbox);

        var groupsWrap = new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        vbox.AddChild(groupsWrap);

        _consoleGroups = new GridContainer { Columns = 2, Position = new Vector2(6, 12) };
        _consoleGroups.AddThemeConstantOverride("h_separation", 42);
        _consoleGroups.AddThemeConstantOverride("v_separation", 16);
        groupsWrap.AddChild(_consoleGroups);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 20);
        vbox.AddChild(buttons);

        var setCamera = ClassicUi.Button("Set camera", new Vector2(150, 32), 16);
        setCamera.Pressed += () =>
        {
            var cb = _onSetCamera;
            Close();
            cb?.Invoke();
        };
        buttons.AddChild(setCamera);

        var back = ClassicUi.Button("Back", new Vector2(100, 32), 16);
        back.Pressed += Close;
        buttons.AddChild(back);

        return panel;
    }

    ClassicWindow BuildPowerPanel()
    {
        var panel = ClassicWindow.Create("Power network", bevel: 5, padLeft: 22, padTop: 36, padRight: 22, padBottom: 20);
        panel.Name = "PowerNetwork";
        panel.Visible = false;
        panel.CustomMinimumSize = new Vector2(300, 135);
        panel.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;

        var vbox = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        vbox.AddThemeConstantOverride("separation", 14);
        panel.AddChild(vbox);

        _powerBlocksLabel = ClassicUi.Label("", 16);
        vbox.AddChild(_powerBlocksLabel);

        _powerBalanceLabel = ClassicUi.Label("", 16);
        vbox.AddChild(_powerBalanceLabel);

        return panel;
    }

    ClassicWindow BuildHelpPanel()
    {
        var panel = ClassicWindow.Create("Help & Controls", bevel: 5, padLeft: 18, padTop: 32, padRight: 18, padBottom: 18);
        panel.Name = "Help";
        panel.Visible = false;
        panel.CustomMinimumSize = new Vector2(640, 460);
        panel.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;

        var vbox = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        vbox.AddThemeConstantOverride("separation", 10);
        panel.AddChild(vbox);

        var field = ClassicField.Create(12);
        field.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        vbox.AddChild(field);

        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 8);
        field.AddChild(text);

        text.AddChild(ClassicUi.Label("Welcome to Open Corneroids!", 17));
        text.AddChild(HelpSection("Movement & Suit",
            "WASD move · Space / Ctrl up / down · Z / X roll · Shift boost · L level view\n" +
            "K lock camera to ship deck when aboard · T flashlight · C or I inventory"));
        text.AddChild(HelpSection("Mining & Building",
            "Hold LMB to mine with a drill or fire a weapon; release LMB to place a block or use an item.\n" +
            "Release RMB to empty hand (RMB places in creative) · 1-8 / wheel select slot · R / MMB rotate block.\n" +
            "Aim into open space to attach within 16 m (red beam) or start a new floating ship beyond 16 m."));
        text.AddChild(HelpSection("Ships, Consoles & Power",
            "E on an unlaunched console launches the ship; E on a ship console sits down (or uses doors, stations, chests).\n" +
            "F on a console equips its binding tool (LMB thrusters/cannons within 3 m to cycle colour group;\n" +
            "press F on the console again to assign group keys or set a camera). F on power blocks shows network balance."));
        text.AddChild(HelpSection("Piloting & Shortcuts",
            "Seated: bound colour-group keys fire thrusters & cannons · H toggles automatic Flight Assistant (G dampeners)\n" +
            "V switches seat / chase view · ` Console · Esc Game menu · F1 Welcome · F3 Debug telemetry · F5 Quicksave · F6 Hide HUD"));

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        vbox.AddChild(buttons);
        var close = ClassicUi.Button("Start!", new Vector2(180, 32), 16);
        close.Pressed += Close;
        buttons.AddChild(close);

        return panel;
    }

    static Control HelpSection(string heading, string body)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        box.AddChild(ClassicUi.Label(heading, 15, new Color(1f, 0.95f, 0.65f)));
        var bodyLabel = ClassicUi.Label(body, 13);
        bodyLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(bodyLabel);
        return box;
    }

    void HideAllPanels()
    {
        // Whatever the window closes for, a stack still on the cursor leaves the hand, as in the original.
        TossHeld(Settings.GameSettings.Cvars.Float(Simulation.Settings.Cvars.ItemCloseTossSpeed));
        if (_crafting is not null) _crafting.Visible = false;
        if (_character is not null) _character.Visible = false;
        if (_inventory is not null) _inventory.Visible = false;
        if (_container is not null)
        {
            _container.Visible = false;
            _container.Detach();
        }
        _containerStillThere = null;
        _row.AddThemeConstantOverride("separation", WindowGap);
        _consolePanel.Visible = false;
        _powerPanel.Visible = false;
        _helpPanel.Visible = false;
        _askKeyCenter.Visible = false;
        _backdrop.Visible = false;
        _waitingGroup = -1;
        _consoleGrid = null;
        _onSetCamera = null;
    }

    public bool HasSurvivalInventory => _inventory is not null && _character is not null;

    public bool IsHelpOpen => IsOpen && _helpPanel.Visible;

    public void OpenInventory()
    {
        if (_inventory is null || _character is null) return;
        OpenStation = null;
        HideAllPanels();
        _backdrop.Visible = true;
        _character.Visible = true;
        _inventory.Visible = true;
        Reveal();
    }

    /// <param name="title">Shown at the top of the crafting panel, normally the station block's name.</param>
    public void OpenCrafting(CraftingStation station, string title)
    {
        if (_crafting is null || _inventory is null) return;
        OpenStation = station;
        HideAllPanels();
        _backdrop.Visible = true;
        _crafting.ShowStation(station, title);
        _crafting.Visible = true;
        _inventory.Visible = true;
        Reveal();
    }

    /// <summary>
    /// Opens a chest: the player's inventory on the left and the chest's <c>"Container"</c> window on the right, 50 px
    /// apart, as in the original. The window closes by itself once <paramref name="stillThere"/> says the chest is gone
    /// (its block was broken meanwhile), so nothing is put into a chest that no longer exists.
    /// </summary>
    /// <param name="width">Slots per row of the chest.</param>
    public void OpenContainer(Inventory container, int width, Func<bool>? stillThere = null)
    {
        if (_container is null || _inventory is null) return;
        OpenStation = null;
        HideAllPanels();
        _backdrop.Visible = true;
        _row.AddThemeConstantOverride("separation", ContainerGap);
        _inventory.Visible = true;
        _container.ShowContainer(container, width);
        _container.Visible = true;
        _containerStillThere = stillThere;
        Reveal();
    }

    public override void _Process(double delta)
    {
        if (IsContainerOpen && _containerStillThere is { } stillThere && !stillThere()) Close();
    }

    /// <summary>Opens the <c>"Control block"</c> window for the console at <paramref name="consoleCell"/> in <paramref name="grid"/>,
    /// as in the original when pressing F a second time on a console while holding its binding tool.</summary>
    public void OpenConsoleWindow(BlockGrid grid, Vector3I consoleCell, ConsoleComponent component, Action? onSetCamera = null)
    {
        OpenStation = null;
        HideAllPanels();
        _consoleGrid = grid;
        _consoleCell = consoleCell.ToInt3();
        _consoleGroupCount = ConsoleBinding.ClampGroupCount(component.Buttons);
        _consoleGunsOnly = component.GunsOnly;
        _onSetCamera = onSetCamera;
        RebuildConsoleGroupRows();
        _consolePanel.Visible = true;
        Reveal();
    }

    /// <summary>Opens the <c>"Power network"</c> status popup for <paramref name="network"/>, as in the original.</summary>
    public void OpenPowerWindow(PowerNetworkInfo network)
    {
        OpenStation = null;
        HideAllPanels();
        _powerBlocksLabel.Text = $"Block count:  {network.BlockCount}";
        _powerBalanceLabel.Text = FormattableString.Invariant($"Power balance:  {network.Balance:0.#}");
        _powerBalanceLabel.AddThemeColorOverride("font_color",
            network.IsActive ? Colors.White : new Color(1f, 0.42f, 0.38f));
        _powerPanel.Visible = true;
        Reveal();
    }

    /// <summary>Opens the <c>F1</c> controls &amp; gameplay help window.</summary>
    public void OpenHelpWindow()
    {
        OpenStation = null;
        HideAllPanels();
        _backdrop.Visible = true;
        _helpPanel.Visible = true;
        Reveal();
    }

    void RebuildConsoleGroupRows()
    {
        foreach (var child in _consoleGroups.GetChildren()) child.QueueFree();
        _groupRows.Clear();
        if (_consoleGrid is null) return;

        var state = _consoleGrid.Consoles.GetOrCreate(_consoleCell, _consoleGroupCount, _consoleGunsOnly);
        var rowsByIndex = new ColorGroupRow[_consoleGroupCount];
        int rows = Math.Min(5, _consoleGroupCount);
        for (int row = 0; row < rows; row++)
        {
            rowsByIndex[row] = AddConsoleGroupCell(state, row);
            if (row + 5 < _consoleGroupCount)
                rowsByIndex[row + 5] = AddConsoleGroupCell(state, row + 5);
            else if (_consoleGroupCount > 5)
                _consoleGroups.AddChild(new Control { CustomMinimumSize = new Vector2(175, 36) });
        }
        _groupRows.AddRange(rowsByIndex);
    }

    ColorGroupRow AddConsoleGroupCell(ConsoleBindingState state, int groupIndex)
    {
        var group = state.Groups[groupIndex];
        var rgb = group.ColorGroup.Color;
        var swatchColor = Color.Color8(rgb.R, rgb.G, rgb.B);
        var cell = new ColorGroupRow(swatchColor, group.Key) { Name = $"GroupKey_{groupIndex}" };
        int captured = groupIndex;
        cell.Clicked += () => BeginKeyCapture(captured);
        _consoleGroups.AddChild(cell);
        return cell;
    }

    void BeginKeyCapture(int groupIndex)
    {
        if (_consoleGrid is null || groupIndex < 0 || groupIndex >= _consoleGroupCount) return;
        _waitingGroup = groupIndex;
        _askKeyCenter.Visible = true;
    }

    void RefreshGroupLabels()
    {
        if (_consoleGrid is null) return;
        var state = _consoleGrid.Consoles.GetOrCreate(_consoleCell, _consoleGroupCount, _consoleGunsOnly);
        for (int i = 0; i < state.Groups.Count && i < _groupRows.Count; i++)
            _groupRows[i]?.SetKeyText(state.Groups[i].Key);
    }

    public override void _Input(InputEvent @event)
    {
        if (!IsOpen) return;

        if (IsListeningForKey && @event is InputEventKey { Pressed: true, Echo: false } keyEvent)
        {
            var key = keyEvent.Keycode != Key.None ? keyEvent.Keycode : keyEvent.PhysicalKeycode;
            if (key != Key.Escape && _consoleGrid is not null)
            {
                var keyName = GameInput.ConsoleKeyName(key);
                if (!string.IsNullOrEmpty(keyName))
                    _consoleGrid.Consoles.AssignKey(_consoleCell, _consoleGroupCount, _consoleGunsOnly, _waitingGroup, keyName);
            }
            _waitingGroup = -1;
            _askKeyCenter.Visible = false;
            RefreshGroupLabels();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (IsChordPress(@event))
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        // Right-clicking closes the Power network popup, as in the original.
        if (_powerPanel.Visible && @event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    public void Close()
    {
        if (!IsOpen) return;
        HideAllPanels();
        _root.Visible = false;
        OpenStation = null;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    void Reveal()
    {
        _root.Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    /// <summary>Draws one colour-group swatch with a +5,+5 dark-grey drop shadow and <c>"= Key"</c>, as in the original.</summary>
    sealed partial class ColorGroupRow : Control
    {
        readonly Color _color;
        string _key;
        bool _hovered;

        public event Action? Clicked;

        public ColorGroupRow(Color color, string key)
        {
            _color = color;
            _key = key;
            CustomMinimumSize = new Vector2(175, 36);
            MouseFilter = MouseFilterEnum.Stop;
            MouseDefaultCursorShape = CursorShape.PointingHand;
            MouseEntered += () => { _hovered = true; QueueRedraw(); };
            MouseExited += () => { _hovered = false; QueueRedraw(); };
        }

        public void SetKeyText(string key)
        {
            _key = key;
            QueueRedraw();
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                Clicked?.Invoke();
                AcceptEvent();
            }
        }

        public override void _Draw()
        {
            if (_hovered) DrawRect(new Rect2(-4, -2, Size.X + 4, Size.Y + 2), new Color(1, 1, 1, 0.18f));
            DrawRect(new Rect2(5, 5, 30, 30), ClassicUi.SwatchShadow);
            DrawRect(new Rect2(0, 0, 30, 30), _color);

            var font = ThemeDB.FallbackFont;
            const int fontSize = 16;
            var shadow = new Color(0, 0, 0, 0.55f);
            DrawString(font, new Vector2(45, 22), "=", HorizontalAlignment.Left, -1, fontSize, shadow);
            DrawString(font, new Vector2(44, 21), "=", HorizontalAlignment.Left, -1, fontSize, Colors.White);
            DrawString(font, new Vector2(65, 22), _key, HorizontalAlignment.Left, -1, fontSize, shadow);
            DrawString(font, new Vector2(64, 21), _key, HorizontalAlignment.Left, -1, fontSize, Colors.White);
        }
    }
}
