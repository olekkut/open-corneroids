using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Content;
using OpenCorneroids.Simulation.Items;

namespace OpenCorneroids.Game.Ui;

/// <summary>
/// Crosshair with a mining progress bar under it, the hotbar with the suit's Health and Power bars above it (survival
/// only), status text, short messages and a log of recent ones at the left. It only draws: what the hotbar holds, which
/// slot is selected and the suit's numbers are decided by the play mode.
/// </summary>
public partial class Hud : CanvasLayer
{
    public const int HotbarSlots = Inventory.HotbarSize;
    const int LogLines = 8;
    const double LogSeconds = 12;
    const float SlotSize = 52;
    static readonly Color InfoColor = new(0.7f, 1f, 0.7f);
    static readonly Color WarningColor = new(1f, 0.6f, 0.45f);

    ItemIcons _icons = null!;
    readonly List<ItemSlot> _slots = new();
    Label _status = null!, _controls = null!, _selectedName = null!, _message = null!;
    ProgressBar _progress = null!;
    double _messageTime;
    HBoxContainer _suitBars = null!;
    (ProgressBar Bar, Label Value) _health, _power;
    VBoxContainer _log = null!;
    readonly List<(Label Line, double Age)> _logLines = new();

    public void Setup(string title, ItemIcons icons)
    {
        _icons = icons;

        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        // A dim backing plate: the outline alone is not enough over busy textures (the block showcase, lit walls).
        var plate = new PanelContainer { Position = new Vector2(6, 4), MouseFilter = Control.MouseFilterEnum.Ignore };
        var plateStyle = Flat(new Color(0, 0, 0, 0.5f));
        plateStyle.SetCornerRadiusAll(4);
        (plateStyle.ContentMarginLeft, plateStyle.ContentMarginRight, plateStyle.ContentMarginTop, plateStyle.ContentMarginBottom) = (8, 8, 3, 4);
        plate.AddThemeStyleboxOverride("panel", plateStyle);
        var top = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        top.AddThemeConstantOverride("separation", 0);
        top.AddChild(Text(title, 18));
        top.AddChild(_status = Text("", 14));
        top.AddChild(_controls = Text("", 13, new Color(1, 1, 1, 0.8f)));
        plate.AddChild(top);
        root.AddChild(plate);

        var crosshair = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        crosshair.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        crosshair.AddChild(Text("+", 26));
        root.AddChild(crosshair);

        // Just under the "+", so the eye never leaves the block being drilled.
        _progress = new ProgressBar
        {
            ShowPercentage = false, MinValue = 0, MaxValue = 1, Step = 0,
            Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _progress.AddThemeStyleboxOverride("background", Flat(new Color(0, 0, 0, 0.6f)));
        _progress.AddThemeStyleboxOverride("fill", Flat(new Color(1f, 0.8f, 0.3f)));
        _progress.SetAnchorsPreset(Control.LayoutPreset.Center);
        (_progress.OffsetLeft, _progress.OffsetTop, _progress.OffsetRight, _progress.OffsetBottom) = (-32, 20, 32, 26);
        root.AddChild(_progress);

        var bottom = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.End, MouseFilter = Control.MouseFilterEnum.Ignore };
        bottom.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        bottom.AddChild(_message = Text("", 16, InfoColor));
        bottom.AddChild(_selectedName = Text("", 18));
        _suitBars = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _suitBars.AddThemeConstantOverride("separation", 48);
        _health = SuitBar(_suitBars, "Health", new Color(0.86f, 0.12f, 0.1f));
        _power = SuitBar(_suitBars, "Power", new Color(0.55f, 0.15f, 0.6f));
        bottom.AddChild(_suitBars);
        var bar = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        for (int i = 0; i < HotbarSlots; i++)
        {
            var slot = ItemSlot.Create(SlotSize);
            bar.AddChild(slot);
            _slots.Add(slot);
        }
        bottom.AddChild(bar);
        bottom.AddChild(new Control { CustomMinimumSize = new Vector2(0, 14), MouseFilter = Control.MouseFilterEnum.Ignore });
        foreach (var label in new[] { _message, _selectedName }) label.HorizontalAlignment = HorizontalAlignment.Center;
        // Load warnings can be a sentence or two; wrap them instead of running off both screen edges.
        _message.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(bottom);

        _log = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Position = new Vector2(10, 150) };
        _log.AddThemeConstantOverride("separation", 0);
        root.AddChild(_log);
    }

    static (ProgressBar, Label) SuitBar(HBoxContainer row, string name, Color fill)
    {
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 2);
        column.AddChild(Text(name, 15));
        var bar = new ProgressBar
        {
            ShowPercentage = false, MinValue = 0, MaxValue = 100, Step = 0,
            CustomMinimumSize = new Vector2(180, 20), MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var back = Flat(new Color(0, 0, 0, 0.55f));
        back.BorderColor = fill.Lightened(0.3f);
        back.SetBorderWidthAll(1);
        bar.AddThemeStyleboxOverride("background", back);
        bar.AddThemeStyleboxOverride("fill", Flat(fill));
        var value = Text("", 14);
        value.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        value.OffsetLeft = 4;
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

    /// <summary>Adds a line to the message log at the left; old lines fade out. A line already in the log is not
    /// added again, only kept fresh, so refusals shown every frame ("You need a drill") are logged once.</summary>
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

    /// <summary>Redraws the hotbar. <paramref name="countText"/> overrides the corner text, e.g. "∞" in creative.</summary>
    public void ShowHotbar(IReadOnlyList<(ItemDef? Item, int Count)> slots, int selected, string caption, string? countText = null)
    {
        for (int i = 0; i < HotbarSlots; i++)
        {
            var (item, count) = i < slots.Count ? slots[i] : (null, 0);
            _slots[i].Show(item, count, _icons, item is null ? null : countText);
            _slots[i].SetLook(i == selected ? ItemSlot.Look.Selected : ItemSlot.Look.Normal);
        }
        _selectedName.Text = caption;
    }

    /// <summary>Whether the selected item's name shows above the hotbar: hidden while piloting, when nothing can be
    /// built or used from the seat.</summary>
    public bool SelectionVisible
    {
        get => _selectedName.Visible;
        set => _selectedName.Visible = value;
    }

    /// <summary>Mining progress from 0 to 1, or null to hide the bar.</summary>
    public void ShowProgress(double? fraction)
    {
        _progress.Visible = fraction is not null;
        if (fraction is { } f) _progress.Value = Math.Clamp(f, 0, 1);
    }

    public void SetStatus(string text) => _status.Text = text;

    /// <summary>The key hints line; set by the play mode, since survival has keys creative does not.</summary>
    public void SetControls(string text) => _controls.Text = text;

    /// <summary>A short message above the hotbar; <paramref name="warning"/> tints it for refusals ("Inventory full").
    /// Longer <paramref name="seconds"/> suit messages worth reading, such as how a save was loaded.</summary>
    public void Flash(string text, bool warning = false, double seconds = 2.0)
    {
        _message.Text = text;
        _message.AddThemeColorOverride("font_color", warning ? WarningColor : InfoColor);
        _messageTime = seconds;
        Log(text, warning);
    }

    public override void _Process(double delta)
    {
        if (_messageTime > 0 && (_messageTime -= delta) <= 0) _message.Text = "";
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
            // Full for most of its life, then fades over the last two seconds.
            line.Modulate = new Color(1, 1, 1, (float)Math.Clamp((LogSeconds - age) / 2, 0, 1));
        }
    }

    /// <summary>An outlined label that stays readable over bright rock and black space alike.</summary>
    public static Label Text(string text, int size, Color? color = null)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color ?? Colors.White);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 4);
        return label;
    }

    static StyleBoxFlat Flat(Color color) => new() { BgColor = color };
}
