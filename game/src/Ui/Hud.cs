using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Content;
using OpenCorneroids.Simulation.Items;

namespace OpenCorneroids.Game.Ui;

/// <summary>
/// Classic Corneroids HUD: crisp centre crosshair (switching to the binding reticle when holding a console tool) with
/// contextual <c>"F to edit / E to use"</c> and binding prompts underneath, mining progress bar, light-grey bevelled
/// 8-slot hotbar with the magnetic-boot aboard indicator to its right, suit Health and Power bars above the hotbar, and
/// when seated at a console, a 3D velocity-vector cone and <c>"Speed: X.XX Units/s"</c> readout in place of the hotbar.
/// Technical telemetry (FPS, coordinates, ship numbers) stays tucked behind <c>F3</c> so normal play is uncluttered.
/// </summary>
public partial class Hud : CanvasLayer
{
    public enum CrosshairKind { Default, BindBlocks, BindCamera }

    public const int HotbarSlots = Inventory.HotbarSize;
    const int LogLines = 5;
    const double LogSeconds = 6;
    const float SlotSize = 56;
    const float HotbarPad = 6;
    const float HotbarWidth = HotbarSlots * SlotSize + HotbarPad * 2;   // 460 px
    static readonly Color InfoColor = new(0.75f, 1f, 0.75f);
    static readonly Color WarningColor = new(1f, 0.62f, 0.48f);
    static readonly Color SpeedGreen = Color.Color8(46, 196, 36);

    ItemIcons _icons = null!;
    readonly List<ItemSlot> _slots = new();
    PanelContainer _debugPlate = null!;
    Label _title = null!, _status = null!, _controls = null!, _selectedName = null!, _message = null!, _centerPrompt = null!;
    Label _speedLabel = null!, _flightModeLabel = null!;
    ProgressBar _progress = null!;
    CrosshairView _crosshair = null!;
    BoardedIconView _boardedIcon = null!;
    SpeedConeView _speedCone = null!;
    ClassicWindow _hotbarFrame = null!;
    Control _seatedHud = null!;
    double _messageTime;
    double _selectedNameTime;
    string _lastSelectedCaption = "";
    bool _selectionAllowed = true;
    HBoxContainer _suitBars = null!;
    (ProgressBar Bar, Label Value) _health, _power;
    VBoxContainer _log = null!;
    readonly List<(Label Line, double Age)> _logLines = new();

    /// <summary>Whether the top-left F3 technical/debug readout is shown (off by default for a clean screen).</summary>
    public bool DebugVisible
    {
        get => _debugPlate.Visible;
        set
        {
            _debugPlate.Visible = value;
            _log.Position = new Vector2(12, value ? 96 : 12);
        }
    }

    /// <summary>Whether the magnetic-boot aboard indicator shows to the right of the hotbar.</summary>
    public bool BoardedVisible
    {
        get => _boardedIcon.Visible;
        set => _boardedIcon.Visible = value;
    }

    public void Setup(string title, ItemIcons icons)
    {
        _icons = icons;

        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        // Top-left F3 debug readout (hidden by default so normal play has a clean sky).
        _debugPlate = new PanelContainer
        {
            Position = new Vector2(8, 6),
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var plateStyle = Flat(new Color(0, 0, 0, 0.58f));
        plateStyle.SetCornerRadiusAll(3);
        (plateStyle.ContentMarginLeft, plateStyle.ContentMarginRight, plateStyle.ContentMarginTop, plateStyle.ContentMarginBottom) = (8, 8, 4, 5);
        _debugPlate.AddThemeStyleboxOverride("panel", plateStyle);
        var top = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        top.AddThemeConstantOverride("separation", 1);
        top.AddChild(_title = Text(title, 15, new Color(0.85f, 0.92f, 1f)));
        top.AddChild(_status = Text("", 13));
        top.AddChild(_controls = Text("", 12, new Color(1, 1, 1, 0.75f)));
        _debugPlate.AddChild(top);
        root.AddChild(_debugPlate);

        _log = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Position = new Vector2(12, 12) };
        _log.AddThemeConstantOverride("separation", 1);
        root.AddChild(_log);

        // Crisp custom crosshair in the exact centre of the viewport.
        _crosshair = new CrosshairView { MouseFilter = Control.MouseFilterEnum.Ignore };
        _crosshair.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_crosshair);

        // Red damage markers around the crosshair, and the death window with its heartbeat.
        _damage = new DamageView { MouseFilter = Control.MouseFilterEnum.Ignore };
        _damage.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_damage);
        _death = BuildDeathScreen();
        AddChild(_death);

        // Contextual prompt below the crosshair ("F to edit / E to use", or console binding instructions).
        _centerPrompt = Text("", 16);
        _centerPrompt.HorizontalAlignment = HorizontalAlignment.Center;
        _centerPrompt.SetAnchorsPreset(Control.LayoutPreset.Center);
        (_centerPrompt.OffsetLeft, _centerPrompt.OffsetTop, _centerPrompt.OffsetRight, _centerPrompt.OffsetBottom) = (-260, 34, 260, 120);
        root.AddChild(_centerPrompt);

        // Mining progress bar right under the crosshair.
        _progress = new ProgressBar
        {
            ShowPercentage = false, MinValue = 0, MaxValue = 1, Step = 0,
            Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _progress.AddThemeStyleboxOverride("background", Flat(new Color(0, 0, 0, 0.65f)));
        _progress.AddThemeStyleboxOverride("fill", Flat(new Color(1f, 0.8f, 0.3f)));
        _progress.SetAnchorsPreset(Control.LayoutPreset.Center);
        (_progress.OffsetLeft, _progress.OffsetTop, _progress.OffsetRight, _progress.OffsetBottom) = (-32, 20, 32, 26);
        root.AddChild(_progress);

        // Bottom stack: transient flash message, brief item-switch label, Health & Power bars, and the 8-slot hotbar.
        var bottom = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.End, MouseFilter = Control.MouseFilterEnum.Ignore };
        bottom.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        bottom.AddThemeConstantOverride("separation", 4);

        bottom.AddChild(_message = Text("", 15, InfoColor));
        bottom.AddChild(_selectedName = Text("", 16));
        foreach (var label in new[] { _message, _selectedName }) label.HorizontalAlignment = HorizontalAlignment.Center;
        _message.AutowrapMode = TextServer.AutowrapMode.WordSmart;

        _suitBars = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _suitBars.AddThemeConstantOverride("separation", 48);
        _health = SuitBar(_suitBars, "Health", Color.Color8(255, 0, 0), Color.Color8(28, 0, 0, 215));
        _power = SuitBar(_suitBars, "Power", Color.Color8(138, 0, 138), Color.Color8(24, 0, 24, 215));
        bottom.AddChild(_suitBars);

        var hotbarCenter = new CenterContainer
        {
            CustomMinimumSize = new Vector2(HotbarWidth, SlotSize + HotbarPad * 2),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _hotbarFrame = ClassicWindow.Create(bevel: 4, padLeft: (int)HotbarPad, padTop: (int)HotbarPad, padRight: (int)HotbarPad, padBottom: (int)HotbarPad);
        _hotbarFrame.MouseFilter = Control.MouseFilterEnum.Ignore;
        var bar = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        bar.AddThemeConstantOverride("separation", 0);
        for (int i = 0; i < HotbarSlots; i++)
        {
            var slot = ItemSlot.Create(SlotSize);
            bar.AddChild(slot);
            _slots.Add(slot);
        }
        _hotbarFrame.AddChild(bar);
        hotbarCenter.AddChild(_hotbarFrame);
        bottom.AddChild(hotbarCenter);

        bottom.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10), MouseFilter = Control.MouseFilterEnum.Ignore });
        root.AddChild(bottom);

        // Magnetic-boot "aboard" icon anchored to the right of the centered hotbar so toggling it never shifts the bar.
        _boardedIcon = new BoardedIconView { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _boardedIcon.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        float halfBar = HotbarWidth * 0.5f;
        (_boardedIcon.OffsetLeft, _boardedIcon.OffsetTop, _boardedIcon.OffsetRight, _boardedIcon.OffsetBottom) =
            (halfBar + 22, -74, halfBar + 22 + 58, -16);
        root.AddChild(_boardedIcon);

        // Seated piloting HUD (replaces the hotbar when mounted to a console, as in the original).
        _seatedHud = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _seatedHud.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_seatedHud);

        _speedCone = new SpeedConeView { MouseFilter = Control.MouseFilterEnum.Ignore };
        _speedCone.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        (_speedCone.OffsetLeft, _speedCone.OffsetTop, _speedCone.OffsetRight, _speedCone.OffsetBottom) = (-90, -138, 90, -4);
        _seatedHud.AddChild(_speedCone);

        var speedBox = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        speedBox.AddThemeConstantOverride("separation", 1);
        speedBox.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        // Aligned directly under the Power bar (whose left edge sits at +24 px right of screen centre).
        (speedBox.OffsetLeft, speedBox.OffsetTop, speedBox.OffsetRight, speedBox.OffsetBottom) = (24, -74, 300, -14);
        _speedLabel = Text("Speed: 0 Units/s", 16, SpeedGreen);
        _flightModeLabel = Text("", 13, new Color(0.65f, 0.92f, 1f));
        speedBox.AddChild(_speedLabel);
        speedBox.AddChild(_flightModeLabel);
        _seatedHud.AddChild(speedBox);
    }

    static (ProgressBar, Label) SuitBar(HBoxContainer row, string name, Color fill, Color backFill)
    {
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 2);
        column.AddChild(Text(name, 15));
        var bar = new ProgressBar
        {
            ShowPercentage = false, MinValue = 0, MaxValue = 100, Step = 0,
            CustomMinimumSize = new Vector2((HotbarWidth - 48) * 0.5f, 24),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var back = Flat(backFill);
        back.BorderColor = fill;
        back.SetBorderWidthAll(2);
        bar.AddThemeStyleboxOverride("background", back);
        bar.AddThemeStyleboxOverride("fill", Flat(fill));
        var value = Text("", 14);
        value.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        value.OffsetLeft = 6;
        value.VerticalAlignment = VerticalAlignment.Center;
        bar.AddChild(value);
        column.AddChild(bar);
        row.AddChild(column);
        return (bar, value);
    }

    /// <summary>Shows the suit bars (they stay hidden in creative, which never calls this).</summary>
    public void ShowSuit(double health, int maxHealth, double power, int maxPower)
    {
        _suitBars.Visible = true;
        Fill(_health, health, maxHealth);
        Fill(_power, power, maxPower);

        static void Fill((ProgressBar Bar, Label Value) bar, double value, int max)
        {
            bar.Bar.MaxValue = Math.Max(1, max);
            bar.Bar.Value = Math.Clamp(value, 0, max);
            bar.Value.Text = $"{(int)value} / {max}";
        }
    }

    /// <summary>Hides the suit bars when switching to creative mode on foot.</summary>
    public void HideSuit() => _suitBars.Visible = false;

    /// <summary>Updates the F3 debug title banner when switching between creative and survival.</summary>
    public void SetTitle(string title) => _title.Text = title;

    /// <summary>Switches the centre crosshair sprite.</summary>
    public void SetCrosshair(CrosshairKind kind) => _crosshair.Kind = kind;

    /// <summary>Sets the contextual interaction/tool prompt right below the crosshair (empty to hide).</summary>
    public void SetCenterPrompt(string text)
    {
        if (_centerPrompt.Text != text) _centerPrompt.Text = text;
    }

    /// <summary>Updates the seated piloting HUD (hiding the hotbar while seated, showing speed and 3D velocity cone).</summary>
    public void SetPiloting(bool seated, Vector3 worldVelocity = default, Basis cameraBasis = default, bool flightAssist = false, bool dampeners = true)
    {
        _hotbarFrame.Visible = !seated;
        _seatedHud.Visible = seated;
        if (!seated) return;

        float speed = MathF.Round(worldVelocity.Length(), 2);
        _speedLabel.Text = FormattableString.Invariant($"Speed: {speed:0.##} Units/s");
        _flightModeLabel.Text = flightAssist ? $"Flight Assist: ON  (Dampeners {(dampeners ? "ON" : "OFF")})" : "";
        _flightModeLabel.Visible = flightAssist;
        _speedCone.SetVelocity(cameraBasis.Orthonormalized().Inverse() * worldVelocity);
    }

    /// <summary>Adds a line to the message log at the top-left; old lines fade out.</summary>
    public void Log(string text, bool warning = false)
    {
        if (string.IsNullOrEmpty(text)) return;
        int same = _logLines.FindIndex(l => l.Line.Text == text);
        if (same >= 0)
        {
            _logLines[same] = (_logLines[same].Line, 0);
            return;
        }
        var line = Text(text, 14, warning ? WarningColor : InfoColor);
        _log.AddChild(line);
        _logLines.Add((line, 0));
        while (_logLines.Count > LogLines)
        {
            _logLines[0].Line.QueueFree();
            _logLines.RemoveAt(0);
        }
    }

    /// <summary>Redraws the hotbar. <paramref name="countText"/> overrides the corner text.</summary>
    public void ShowHotbar(IReadOnlyList<(ItemDef? Item, int Count)> slots, int selected, string caption, string? countText = null)
    {
        for (int i = 0; i < HotbarSlots; i++)
        {
            var (item, count) = i < slots.Count ? slots[i] : (null, 0);
            _slots[i].Show(item, count, _icons, item is null ? null : countText);
            _slots[i].SetLook(i == selected ? ItemSlot.Look.Selected : ItemSlot.Look.Normal);
        }
        if (caption != _lastSelectedCaption)
        {
            _lastSelectedCaption = caption;
            _selectedName.Text = caption;
            _selectedNameTime = string.IsNullOrEmpty(caption) ? 0 : 2.2;
            _selectedName.Modulate = Colors.White;
        }
    }

    /// <summary>Whether the selected item's name may show above the hotbar (hidden while piloting).</summary>
    public bool SelectionVisible
    {
        get => _selectionAllowed;
        set
        {
            _selectionAllowed = value;
            _selectedName.Visible = value;
        }
    }

    /// <summary>Mining progress from 0 to 1, or null to hide the bar.</summary>
    public void ShowProgress(double? fraction)
    {
        _progress.Visible = fraction is not null;
        if (fraction is { } f) _progress.Value = Math.Clamp(f, 0, 1);
    }

    public void SetStatus(string text) => _status.Text = text;

    public void SetControls(string text) => _controls.Text = text;

    /// <summary>Shows a short transient notice above the hotbar.</summary>
    public void Flash(string text, bool warning = false, double seconds = 2.0)
    {
        _message.Text = text;
        _message.AddThemeColorOverride("font_color", warning ? WarningColor : InfoColor);
        _messageTime = seconds;
    }

    public override void _Process(double delta)
    {
        BeatHeart(delta);
        if (_messageTime > 0 && (_messageTime -= delta) <= 0) _message.Text = "";
        if (_selectedNameTime > 0)
        {
            _selectedNameTime -= delta;
            if (_selectedNameTime <= 0) _selectedName.Text = "";
            else _selectedName.Modulate = new Color(1, 1, 1, (float)Math.Clamp(_selectedNameTime / 0.5, 0, 1));
        }
        for (int i = _logLines.Count - 1; i >= 0; i--)
        {
            var (line, age) = _logLines[i];
            age += delta;
            if (age >= LogSeconds)
            {
                line.QueueFree();
                _logLines.RemoveAt(i);
                continue;
            }
            _logLines[i] = (line, age);
            line.Modulate = new Color(1, 1, 1, (float)Math.Clamp((LogSeconds - age) / 1.5, 0, 1));
        }
    }

    /// <summary>An outlined label that stays readable over bright rock and black space alike.</summary>
    public static Label Text(string text, int size, Color? color = null)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color ?? Colors.White);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        label.AddThemeConstantOverride("outline_size", 3);
        return label;
    }

    static StyleBoxFlat Flat(Color color) => new() { BgColor = color };

    // ---- damage and death ----

    DamageView _damage = null!;
    Control _death = null!;
    ColorRect _heartbeat = null!;
    Action? _onRespawn;
    double _deadFor;

    /// <summary>
    /// Red markers around the crosshair for 0.4 seconds, as in the original, pointing toward where the hurt came from:
    /// <paramref name="screenAngle"/> in radians clockwise from straight up on screen, or null for all round.
    /// </summary>
    public void ShowDamage(float? screenAngle) => _damage.Show(screenAngle);

    /// <summary>The death window, as in the original: "You have died" and a Respawn button, over a heartbeat pulse.</summary>
    public void ShowDeath(Action onRespawn)
    {
        _onRespawn = onRespawn;
        _deadFor = 0;
        _death.Visible = true;
    }

    public void HideDeath()
    {
        _onRespawn = null;
        _death.Visible = false;
    }

    public bool DeathShown => _death.Visible;

    Control BuildDeathScreen()
    {
        var screen = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        screen.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _heartbeat = new ColorRect { Color = new Color(0.6f, 0, 0, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        _heartbeat.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        screen.AddChild(_heartbeat);

        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        screen.AddChild(center);
        // The original's caption.
        var window = ClassicWindow.Create("Oh noes!", bevel: 5, padLeft: 28, padTop: 36, padRight: 28, padBottom: 20);
        center.AddChild(window);
        var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        column.AddThemeConstantOverride("separation", 16);
        window.AddChild(column);
        var text = ClassicUi.Label("You have died", 22, Colors.Black);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(text);
        var respawn = ClassicUi.Button("Respawn", new Vector2(160, 38), 17);
        respawn.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        respawn.Pressed += () => _onRespawn?.Invoke();
        column.AddChild(respawn);
        return screen;
    }

    void BeatHeart(double delta)
    {
        if (!_death.Visible) return;
        // A heartbeat once a second: a quick double throb of red, then rest.
        _deadFor += delta;
        double beat = _deadFor % 1.0;
        float pulse = (float)(Math.Exp(-Math.Pow((beat - 0.08) / 0.05, 2)) + 0.7 * Math.Exp(-Math.Pow((beat - 0.28) / 0.06, 2)));
        _heartbeat.Color = new Color(0.55f, 0, 0, 0.22f + 0.3f * pulse);
    }

    /// <summary>Red wedges around the crosshair while the player is being hurt.</summary>
    sealed partial class DamageView : Control
    {
        /// <summary>How long the markers show, as in the original.</summary>
        const double ShowSeconds = 0.4;
        double _left;
        float? _angle;

        public void Show(float? angle)
        {
            _angle = angle;
            _left = ShowSeconds;
            QueueRedraw();
        }

        public override void _Process(double delta)
        {
            if (_left <= 0) return;
            _left -= delta;
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (_left <= 0) return;
            var c = (Size * 0.5f).Floor();
            float alpha = (float)Math.Clamp(_left / ShowSeconds, 0, 1);
            var red = new Color(0.95f, 0.08f, 0.05f, 0.9f * alpha);
            var shadow = new Color(0, 0, 0, 0.5f * alpha);
            if (_angle is { } a) Wedge(c, a, red, shadow);
            else for (int i = 0; i < 4; i++) Wedge(c, i * MathF.PI / 2, red, shadow);
        }

        void Wedge(Vector2 c, float angle, Color color, Color shadow)
        {
            // An arrowhead 34 px out from the centre, pointing outward toward the source.
            var dir = new Vector2(MathF.Sin(angle), -MathF.Cos(angle));
            var side = new Vector2(-dir.Y, dir.X);
            var tip = c + dir * 48;
            Vector2[] points = [tip, c + dir * 34 + side * 12, c + dir * 38, c + dir * 34 - side * 12];
            var outline = new Vector2[points.Length + 1];
            points.CopyTo(outline, 0);
            outline[^1] = points[0];
            DrawColoredPolygon(points, color);
            DrawPolyline(outline, shadow, 1.5f, antialiased: true);
        }
    }

    /// <summary>Draws the classic 4-tick crosshair, the <c>[k]</c> block-binding reticle, or the camera-binding reticle.</summary>
    sealed partial class CrosshairView : Control
    {
        CrosshairKind _kind;
        public CrosshairKind Kind
        {
            get => _kind;
            set { if (_kind != value) { _kind = value; QueueRedraw(); } }
        }

        public override void _Draw()
        {
            var c = (Size * 0.5f).Floor();
            var shadow = new Color(0, 0, 0, 0.75f);
            switch (_kind)
            {
                case CrosshairKind.Default:
                    // Four crisp white ticks around an open 6 px centre gap, as in the original.
                    Span<Rect2> ticks =
                    [
                        new(c.X - 11, c.Y - 1, 8, 3),
                        new(c.X + 4,  c.Y - 1, 8, 3),
                        new(c.X - 1,  c.Y - 11, 3, 8),
                        new(c.X - 1,  c.Y + 4,  3, 8),
                    ];
                    foreach (var r in ticks) DrawRect(r.Grow(1), shadow);
                    foreach (var r in ticks) DrawRect(r, Colors.White);
                    break;

                case CrosshairKind.BindBlocks:
                case CrosshairKind.BindCamera:
                    DrawRect(new Rect2(c.X - 14, c.Y - 14, 28, 28), shadow, filled: false, width: 3);
                    DrawRect(new Rect2(c.X - 14, c.Y - 14, 28, 28), Colors.White, filled: false, width: 2);
                    DrawRect(new Rect2(c.X - 9, c.Y - 9, 18, 18), shadow, filled: false, width: 3);
                    DrawRect(new Rect2(c.X - 9, c.Y - 9, 18, 18), Colors.White, filled: false, width: 2);
                    var glyph = _kind == CrosshairKind.BindBlocks ? "k" : "c";
                    var font = ThemeDB.FallbackFont;
                    DrawString(font, c + new Vector2(-3, 5), glyph, HorizontalAlignment.Left, -1, 13, shadow);
                    DrawString(font, c + new Vector2(-4, 4), glyph, HorizontalAlignment.Left, -1, 13, Colors.White);
                    break;
            }
        }
    }

    /// <summary>Draws the white magnetic-boot "aboard" icon to the right of the hotbar, as in the original.</summary>
    sealed partial class BoardedIconView : Control
    {
        public override void _Draw()
        {
            var shadow = new Color(0, 0, 0, 0.7f);
            // Hatched deck plate at the bottom.
            var plate = new Rect2(4, 40, 50, 12);
            DrawRect(plate.Grow(1), shadow);
            DrawRect(plate, Colors.White, filled: false, width: 2);
            for (float x = plate.Position.X + 4; x < plate.End.X - 2; x += 6)
                DrawLine(new Vector2(x, plate.End.Y - 1), new Vector2(Math.Min(x + 6, plate.End.X - 1), plate.Position.Y + 1), Colors.White, 1.5f);

            // Boot silhouette in the middle (shaft + instep + sole).
            Rect2 shaft = new(27, 10, 10, 25);
            Rect2 foot = new(17, 26, 20, 9);
            DrawRect(shaft.Grow(1), shadow);
            DrawRect(foot.Grow(1), shadow);
            DrawRect(shaft, Colors.White);
            DrawRect(foot, Colors.White);
            DrawRect(new Rect2(29, 12, 6, 21), new Color(0, 0, 0, 0.85f));
            DrawRect(new Rect2(19, 28, 16, 5), new Color(0, 0, 0, 0.85f));

            // Downward magnetic arrows flanking the boot.
            DrawDownArrow(new Vector2(9, 12), shadow);
            DrawDownArrow(new Vector2(45, 12), shadow);
        }

        void DrawDownArrow(Vector2 topCenter, Color shadow)
        {
            var stem = new Rect2(topCenter.X - 2, topCenter.Y, 4, 15);
            Vector2[] head =
            [
                new(topCenter.X - 6, topCenter.Y + 14),
                new(topCenter.X + 6, topCenter.Y + 14),
                new(topCenter.X, topCenter.Y + 23),
            ];
            DrawRect(stem.Grow(1), shadow);
            DrawRect(stem, Colors.White);
            DrawColoredPolygon(head, Colors.White);
        }
    }

    /// <summary>3D perspective-projected green velocity cone between the suit bars when piloting, as in the original.</summary>
    sealed partial class SpeedConeView : Control
    {
        const int Segments = 16;
        Vector3 _viewVelocity;
        readonly (float Depth, Vector2 A, Vector2 B, Vector2 C, Color Color)[] _faces = new (float, Vector2, Vector2, Vector2, Color)[Segments * 2];
        readonly Vector2[] _tri = new Vector2[3];

        public void SetVelocity(Vector3 viewVelocity)
        {
            if (_viewVelocity.DistanceSquaredTo(viewVelocity) < 1e-4f) return;
            _viewVelocity = viewVelocity;
            QueueRedraw();
        }

        public override void _Draw()
        {
            float speed = _viewVelocity.Length();
            if (speed < 0.02f) return;

            var dir = _viewVelocity / speed;
            const float radius = 0.11f;

            var upRef = MathF.Abs(dir.Y) < 0.95f ? Vector3.Up : Vector3.Right;
            var u = dir.Cross(upRef).Normalized();
            var v = u.Cross(dir).Normalized();

            // Scaled by speed as in the original: the length stretches with speed up to 10 units/s
            // (yScale = min(speed, 10) * 0.25) while the base radius stays the same.
            float yScale = Math.Min(speed, 10f) * 0.25f;
            float coneLength = Math.Max(0.04f, radius * 3.8f * yScale);

            // Viewed from slightly above, as in the original's mini-viewport, sized to stay inside the gap between Health and Power.
            var center3D = new Vector3(0, -0.72f, -0.1f);
            var apex = center3D + dir * (coneLength * 0.55f);
            var baseCenter = center3D - dir * (coneLength * 0.45f);

            Span<Vector3> ring = stackalloc Vector3[Segments];
            for (int i = 0; i < Segments; i++)
            {
                float a = i * MathF.Tau / Segments;
                ring[i] = baseCenter + (u * MathF.Cos(a) + v * MathF.Sin(a)) * radius;
            }

            var light = new Vector3(0.35f, 0.75f, 0.55f).Normalized();
            var screenCenter = new Vector2(Size.X * 0.5f, -2f);
            const float camZ = 2.35f, focal = 155f;

            Vector2 Project(Vector3 p)
            {
                float d = Math.Max(0.4f, camZ - p.Z);
                return new Vector2(screenCenter.X + p.X / d * focal, screenCenter.Y - p.Y / d * focal);
            }

            var pApex = Project(apex);
            var pBase = Project(baseCenter);
            int count = 0;
            for (int i = 0; i < Segments; i++)
            {
                int next = (i + 1) % Segments;
                var r0 = ring[i];
                var r1 = ring[next];
                var sideNormal = (r1 - apex).Cross(r0 - apex).Normalized();
                float shade = 0.42f + 0.58f * Math.Max(0f, sideNormal.Dot(light));
                var sideColor = new Color(0.1f * shade, 0.68f * shade, 0.08f * shade, 0.88f);
                float sideDepth = (apex.Z + r0.Z + r1.Z) / 3f;
                _faces[count++] = (sideDepth, pApex, Project(r0), Project(r1), sideColor);

                float baseShade = 0.28f + 0.35f * Math.Max(0f, (-dir).Dot(light));
                var baseColor = new Color(0.06f * baseShade, 0.48f * baseShade, 0.05f * baseShade, 0.9f);
                float baseDepth = (baseCenter.Z + r0.Z + r1.Z) / 3f;
                _faces[count++] = (baseDepth, pBase, Project(r1), Project(r0), baseColor);
            }

            Array.Sort(_faces, 0, count, Comparer<(float Depth, Vector2 A, Vector2 B, Vector2 C, Color Color)>.Create((x, y) => x.Depth.CompareTo(y.Depth)));
            for (int i = 0; i < count; i++)
            {
                var f = _faces[i];
                _tri[0] = f.A;
                _tri[1] = f.B;
                _tri[2] = f.C;
                DrawColoredPolygon(_tri, f.Color);
            }
        }
    }
}
