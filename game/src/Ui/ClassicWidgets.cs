using Godot;

namespace OpenCorneroids.Game.Ui;

/// <summary>
/// The original's menu widgets on Godot controls: a 30×30 checkbox showing an X, a slider with &lt; and &gt; buttons
/// and a value box, drop-down lists, text boxes and lists in sunken fields, all in the classic grey palette.
/// </summary>
public static class ClassicWidgets
{
    static StyleBoxFlat Field(int margin = 6) => new()
    {
        BgColor = ClassicUi.FieldFace,
        BorderColor = ClassicUi.BevelDark,
        BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
        ContentMarginLeft = margin, ContentMarginRight = margin, ContentMarginTop = 3, ContentMarginBottom = 3,
    };

    /// <summary>A text box in a sunken field.</summary>
    public static LineEdit TextBox(string text = "", int maxLength = 0)
    {
        var box = new LineEdit { Text = text, MaxLength = maxLength, CustomMinimumSize = new Vector2(0, 30) };
        box.AddThemeStyleboxOverride("normal", Field());
        box.AddThemeStyleboxOverride("focus", Field());
        box.AddThemeFontSizeOverride("font_size", 15);
        box.AddThemeColorOverride("font_color", Colors.White);
        box.AddThemeColorOverride("caret_color", Colors.White);
        box.AddThemeColorOverride("font_placeholder_color", new Color(1, 1, 1, 0.55f));
        return box;
    }

    /// <summary>A drop-down list (the original's combobox).</summary>
    public static OptionButton DropDown(IEnumerable<string> items, int selected, Vector2 size)
    {
        var drop = new OptionButton { CustomMinimumSize = size, FocusMode = Control.FocusModeEnum.None };
        foreach (var item in items) drop.AddItem(item);
        if (drop.ItemCount > 0) drop.Select(Math.Clamp(selected, 0, drop.ItemCount - 1));
        foreach (var state in new[] { "normal", "hover", "pressed", "focus" }) drop.AddThemeStyleboxOverride(state, Field(8));
        drop.AddThemeFontSizeOverride("font_size", 15);
        drop.AddThemeColorOverride("font_color", Colors.White);
        drop.AddThemeColorOverride("font_hover_color", Colors.White);
        return drop;
    }

    /// <summary>A list in a sunken field, one row per item.</summary>
    public static ItemList List(Vector2 size)
    {
        var list = new ItemList { CustomMinimumSize = size, FocusMode = Control.FocusModeEnum.None };
        list.AddThemeStyleboxOverride("panel", Field(4));
        list.AddThemeFontSizeOverride("font_size", 16);
        list.AddThemeColorOverride("font_color", Colors.White);
        list.AddThemeColorOverride("font_selected_color", Colors.White);
        list.AddThemeConstantOverride("v_separation", 10);
        // The selected row gets a white outline, as in the original.
        list.AddThemeStyleboxOverride("selected", new StyleBoxFlat
        {
            BgColor = new Color(1, 1, 1, 0.08f), BorderColor = Colors.White,
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
        });
        list.AddThemeStyleboxOverride("selected_focus", list.GetThemeStylebox("selected"));
        return list;
    }
}

/// <summary>The original's checkbox: a 30×30 sunken box with an X when checked, and its label to the right.</summary>
public partial class ClassicCheckBox : HBoxContainer
{
    bool _checked;
    Control _box = null!;

    public event Action<bool>? Toggled;

    public bool Checked
    {
        get => _checked;
        set
        {
            _checked = value;
            _box?.QueueRedraw();
        }
    }

    public static ClassicCheckBox Create(string label, bool value)
    {
        var check = new ClassicCheckBox { _checked = value };
        check.AddThemeConstantOverride("separation", 20);
        check._box = new Control { CustomMinimumSize = new Vector2(30, 30), MouseFilter = MouseFilterEnum.Stop };
        check._box.Draw += check.DrawBox;
        check._box.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) return;
            check.Checked = !check.Checked;
            check.Toggled?.Invoke(check.Checked);
        };
        check.AddChild(check._box);
        var text = ClassicUi.Label(label, 16);
        text.VerticalAlignment = VerticalAlignment.Center;
        check.AddChild(text);
        return check;
    }

    void DrawBox()
    {
        var size = _box.Size;
        _box.DrawRect(new Rect2(Vector2.Zero, size), ClassicUi.FieldFace);
        _box.DrawRect(new Rect2(0, 0, size.X, 2), ClassicUi.BevelDark);
        _box.DrawRect(new Rect2(0, 0, 2, size.Y), ClassicUi.BevelDark);
        _box.DrawRect(new Rect2(0, size.Y - 2, size.X, 2), ClassicUi.BevelLight);
        _box.DrawRect(new Rect2(size.X - 2, 0, 2, size.Y), ClassicUi.BevelLight);
        if (!_checked) return;
        var font = ThemeDB.FallbackFont;
        var at = new Vector2(size.X / 2 - 7, size.Y / 2 + 8);
        _box.DrawString(font, at + Vector2.One, "X", HorizontalAlignment.Left, -1, 22, new Color(0, 0, 0, 0.5f));
        _box.DrawString(font, at, "X", HorizontalAlignment.Left, -1, 22, Colors.White);
    }
}

/// <summary>The original's slider: &lt; and &gt; buttons around a track with a thumb, and the value in a box beside it.</summary>
public partial class ClassicSlider : HBoxContainer
{
    HSlider _slider = null!;
    Label _value = null!;
    Func<double, string> _format = v => v.ToString("0.##");

    public event Action<double>? ValueChanged;

    public double Value
    {
        get => _slider.Value;
        set => _slider.Value = value;
    }

    public static ClassicSlider Create(double min, double max, double step, double value, float width, Func<double, string>? format = null)
    {
        var s = new ClassicSlider();
        if (format is not null) s._format = format;
        s.AddThemeConstantOverride("separation", 4);
        var less = ClassicUi.Button("<", new Vector2(30, 30), 15);
        var more = ClassicUi.Button(">", new Vector2(30, 30), 15);
        s._slider = new HSlider
        {
            MinValue = min, MaxValue = max, Step = step, Value = value,
            CustomMinimumSize = new Vector2(width, 30), SizeFlagsVertical = SizeFlags.ShrinkCenter,
            FocusMode = FocusModeEnum.None,
        };
        s._slider.AddThemeStyleboxOverride("slider", new StyleBoxFlat
        {
            BgColor = ClassicUi.FieldFace, BorderColor = ClassicUi.BevelDark, BorderWidthTop = 2, BorderWidthLeft = 2,
            ContentMarginTop = 6, ContentMarginBottom = 6,
        });
        var grabber = Image.CreateEmpty(14, 24, false, Image.Format.Rgba8);
        grabber.Fill(ClassicUi.PanelFace);
        for (int y = 0; y < 24; y++) { grabber.SetPixel(0, y, ClassicUi.BevelLight); grabber.SetPixel(13, y, ClassicUi.BevelDark); }
        for (int x = 0; x < 14; x++) { grabber.SetPixel(x, 0, ClassicUi.BevelLight); grabber.SetPixel(x, 23, ClassicUi.BevelDark); }
        var grabberTexture = ImageTexture.CreateFromImage(grabber);
        s._slider.AddThemeIconOverride("grabber", grabberTexture);
        s._slider.AddThemeIconOverride("grabber_highlight", grabberTexture);
        s._slider.AddThemeStyleboxOverride("grabber_area", new StyleBoxEmpty());
        s._slider.AddThemeStyleboxOverride("grabber_area_highlight", new StyleBoxEmpty());
        var field = ClassicField.Create(2);
        field.CustomMinimumSize = new Vector2(70, 30);
        s._value = ClassicUi.Label(s._format(value), 15);
        s._value.HorizontalAlignment = HorizontalAlignment.Center;
        field.AddChild(s._value);
        less.Pressed += () => s._slider.Value -= Math.Max(step, (max - min) / 50);
        more.Pressed += () => s._slider.Value += Math.Max(step, (max - min) / 50);
        s._slider.ValueChanged += v =>
        {
            s._value.Text = s._format(v);
            s.ValueChanged?.Invoke(v);
        };
        s.AddChild(less);
        s.AddChild(s._slider);
        s.AddChild(more);
        s.AddChild(field);
        return s;
    }
}

/// <summary>
/// A full-screen layer holding one centred classic window over a dim backdrop, for menus and popups. Esc closes it
/// (calls <see cref="Back"/>), as in the original.
/// </summary>
public partial class MenuOverlay : Control
{
    public ClassicWindow Window { get; private set; } = null!;
    public VBoxContainer Body { get; private set; } = null!;

    /// <summary>What Esc does; closes the overlay by default.</summary>
    public Action? Back { get; set; }

    /// <summary>Raised once the overlay is gone.</summary>
    public event Action? Closed;

    public static MenuOverlay Create(string caption, Vector2 size, float dim = 0.35f)
    {
        var overlay = new MenuOverlay { MouseFilter = MouseFilterEnum.Stop };
        overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var backdrop = new ColorRect { Color = new Color(0, 0, 0, dim), MouseFilter = MouseFilterEnum.Ignore };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        overlay.AddChild(backdrop);
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        overlay.AddChild(center);
        overlay.Window = ClassicWindow.Create(caption, bevel: 5, padLeft: 20, padTop: string.IsNullOrEmpty(caption) ? 14 : 32, padRight: 20, padBottom: 16);
        overlay.Window.CustomMinimumSize = size;
        center.AddChild(overlay.Window);
        overlay.Body = new VBoxContainer();
        overlay.Body.AddThemeConstantOverride("separation", 12);
        overlay.Window.AddChild(overlay.Body);
        return overlay;
    }

    public override void _Input(InputEvent @event)
    {
        if (!IsVisibleInTree() || @event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        // Only the topmost overlay answers.
        var parent = GetParent();
        if (parent is not null && parent.GetChildren().OfType<MenuOverlay>().LastOrDefault(o => o.Visible) != this) return;
        GetViewport().SetInputAsHandled();
        if (Back is { } back) back();
        else Close();
    }

    public void Close()
    {
        if (!IsInstanceValid(this) || IsQueuedForDeletion()) return;
        QueueFree();
        Closed?.Invoke();
    }

    /// <summary>A full-width classic button in the body.</summary>
    public Button AddButton(string text, Action onPressed, float height = 25)
    {
        var button = ClassicUi.Button(text, new Vector2(0, height), 16);
        button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        button.Pressed += onPressed;
        Body.AddChild(button);
        return button;
    }

    /// <summary>A Yes/No question over everything, as in the original (Esc answers No).</summary>
    public static MenuOverlay Ask(Node parent, string text, Action onYes, Action? onNo = null, string caption = "")
    {
        var font = ThemeDB.FallbackFont;
        float width = Math.Max(240, font.GetStringSize(text, HorizontalAlignment.Left, -1, 16).X + 60);
        var ask = Create(caption, new Vector2(width, 100), dim: 0.2f);
        var label = ClassicUi.Label(text, 16);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        ask.Body.AddChild(label);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 16);
        var yes = ClassicUi.Button("Yes", new Vector2(80, 30), 16);
        var no = ClassicUi.Button("No", new Vector2(80, 30), 16);
        yes.Pressed += () => { ask.Close(); onYes(); };
        no.Pressed += () => { ask.Close(); onNo?.Invoke(); };
        ask.Back = () => { ask.Close(); onNo?.Invoke(); };
        row.AddChild(yes);
        row.AddChild(no);
        ask.Body.AddChild(row);
        parent.AddChild(ask);
        return ask;
    }

    /// <summary>A message with one Ok button, as in the original's message box.</summary>
    public static MenuOverlay Tell(Node parent, string text, string caption = "", Action? onOk = null)
    {
        var font = ThemeDB.FallbackFont;
        float width = Math.Max(240, font.GetStringSize(text, HorizontalAlignment.Left, -1, 16).X + 60);
        var box = Create(caption, new Vector2(Math.Min(width, 700), 100), dim: 0.2f);
        var label = ClassicUi.Label(text, 16);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(Math.Min(width, 700) - 40, 0);
        box.Body.AddChild(label);
        var ok = ClassicUi.Button("Ok", new Vector2(100, 30), 16);
        ok.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        ok.Pressed += () => { box.Close(); onOk?.Invoke(); };
        box.Back = () => { box.Close(); onOk?.Invoke(); };
        box.Body.AddChild(ok);
        parent.AddChild(box);
        return box;
    }
}
