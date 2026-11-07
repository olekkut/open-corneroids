using Godot;

namespace OpenCorneroids.Game.Ui;

/// <summary>
/// Classic light-grey bevelled window, field and button widgets matching the original game's GUI palette
/// (<c>#BEBEBE</c> panel face, <c>#E4E4E4</c> top/left highlight, <c>#7F7F7F</c> bottom/right shadow, and an inset
/// <c>#A5A5A5</c> groove interrupted at the top-left by a white caption). Every Godot resource is created per widget
/// rather than cached in static fields so nothing outlives the scene tree at exit.
/// </summary>
public static class ClassicUi
{
    public static readonly Color PanelFace = Color.Color8(190, 190, 190);
    public static readonly Color PanelHover = Color.Color8(204, 204, 204);
    public static readonly Color FieldFace = Color.Color8(170, 170, 170);
    public static readonly Color SlotFace = Color.Color8(176, 176, 176);
    public static readonly Color BevelLight = Color.Color8(228, 228, 228);
    public static readonly Color BevelDark = Color.Color8(127, 127, 127);
    public static readonly Color Groove = Color.Color8(162, 162, 162);
    public static readonly Color SwatchShadow = Color.Color8(120, 120, 120);

    /// <summary>Builds a 9-patch two-tone bevelled <see cref="StyleBoxTexture"/> for buttons and item slots.</summary>
    public static StyleBoxTexture BevelStyle(Color face, Color topLeft, Color bottomRight, int bevel = 3, int marginH = 6, int marginV = 4)
    {
        const int s = 16;
        int b = Math.Clamp(bevel, 1, 6);
        var image = Image.CreateEmpty(s, s, false, Image.Format.Rgba8);
        image.Fill(face);
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            bool tl = y < b || x < b;
            bool br = y >= s - b || x >= s - b;
            if (tl && br)
            {
                // Diagonal mitre at the top-right and bottom-left corners.
                if (x >= s - b && y < b) image.SetPixel(x, y, x - (s - b) >= b - 1 - y ? bottomRight : topLeft);
                else if (x < b && y >= s - b) image.SetPixel(x, y, y - (s - b) >= b - 1 - x ? bottomRight : topLeft);
                else if (x < b && y < b) image.SetPixel(x, y, topLeft);
                else image.SetPixel(x, y, bottomRight);
            }
            else if (tl) image.SetPixel(x, y, topLeft);
            else if (br) image.SetPixel(x, y, bottomRight);
        }

        var texture = ImageTexture.CreateFromImage(image);
        return new StyleBoxTexture
        {
            Texture = texture,
            TextureMarginLeft = b,
            TextureMarginTop = b,
            TextureMarginRight = b,
            TextureMarginBottom = b,
            ContentMarginLeft = marginH,
            ContentMarginRight = marginH,
            ContentMarginTop = marginV,
            ContentMarginBottom = marginV,
        };
    }

    /// <summary>A raised light-grey bevelled button with crisp white shadowed text, as in the original windows.</summary>
    public static Button Button(string text, Vector2 minSize, int fontSize = 15)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = minSize,
            FocusMode = Control.FocusModeEnum.None,
        };
        button.AddThemeFontSizeOverride("font_size", fontSize);
        button.AddThemeStyleboxOverride("normal", BevelStyle(PanelFace, BevelLight, BevelDark, bevel: 3, marginH: 10, marginV: 4));
        button.AddThemeStyleboxOverride("hover", BevelStyle(PanelHover, Colors.White, BevelDark, bevel: 3, marginH: 10, marginV: 4));
        button.AddThemeStyleboxOverride("pressed", BevelStyle(FieldFace, BevelDark, BevelLight, bevel: 3, marginH: 10, marginV: 4));
        button.AddThemeStyleboxOverride("disabled", BevelStyle(Color.Color8(178, 178, 178), Color.Color8(196, 196, 196), Color.Color8(150, 150, 150), bevel: 2, marginH: 10, marginV: 4));
        button.AddThemeColorOverride("font_color", Colors.White);
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_pressed_color", Color.Color8(235, 235, 235));
        button.AddThemeColorOverride("font_disabled_color", Color.Color8(215, 215, 215, 130));
        button.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.55f));
        button.AddThemeConstantOverride("shadow_offset_x", 1);
        button.AddThemeConstantOverride("shadow_offset_y", 1);
        button.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.45f));
        button.AddThemeConstantOverride("outline_size", 2);
        return button;
    }

    /// <summary>Readable white label with a crisp subtle shadow for light-grey window interiors.</summary>
    public static Label Label(string text, int size = 15, Color? color = null)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color ?? Colors.White);
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.6f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.45f));
        label.AddThemeConstantOverride("outline_size", 2);
        return label;
    }
}

/// <summary>
/// Classic raised light-grey window frame, optionally carrying an inset groove broken at the top-left by a white
/// caption (<c>"Control block"</c>, <c>"Power network"</c>, <c>"Inventory"</c>, etc.), as in the original.
/// </summary>
public partial class ClassicWindow : MarginContainer
{
    string _caption = "";
    int _bevel = 5;

    public string Caption
    {
        get => _caption;
        set
        {
            _caption = value;
            QueueRedraw();
        }
    }

    public static ClassicWindow Create(string caption = "", int bevel = 5, int padLeft = -1, int padTop = -1, int padRight = -1, int padBottom = -1)
    {
        var win = new ClassicWindow();
        win.Configure(caption, bevel, padLeft, padTop, padRight, padBottom);
        return win;
    }

    public void Configure(string caption = "", int bevel = 5, int padLeft = -1, int padTop = -1, int padRight = -1, int padBottom = -1)
    {
        _caption = caption;
        _bevel = bevel;
        bool hasCaption = !string.IsNullOrEmpty(caption);
        int defSide = hasCaption ? 18 : bevel + 4;
        int defTop = hasCaption ? 30 : bevel + 4;
        AddThemeConstantOverride("margin_left", padLeft >= 0 ? padLeft : defSide);
        AddThemeConstantOverride("margin_right", padRight >= 0 ? padRight : defSide);
        AddThemeConstantOverride("margin_top", padTop >= 0 ? padTop : defTop);
        AddThemeConstantOverride("margin_bottom", padBottom >= 0 ? padBottom : defSide);
        Resized += QueueRedraw;
    }

    public override void _Draw()
    {
        float w = Size.X, h = Size.Y, b = _bevel;
        if (w <= 0 || h <= 0) return;

        DrawRect(new Rect2(0, 0, w, h), ClassicUi.PanelFace);
        // Raised outer bevel: light top & left, dark bottom & right.
        DrawRect(new Rect2(0, 0, w, b), ClassicUi.BevelLight);
        DrawRect(new Rect2(0, 0, b, h), ClassicUi.BevelLight);
        DrawRect(new Rect2(0, h - b, w, b), ClassicUi.BevelDark);
        DrawRect(new Rect2(w - b, 0, b, h), ClassicUi.BevelDark);

        if (string.IsNullOrEmpty(_caption)) return;

        // Inset groove rectangle interrupted at the top-left by the window caption.
        const float gx = 9f, gy = 13f, gb = 4f;
        float gw = Math.Max(0, w - gx * 2), gh = Math.Max(0, h - gy - gx);
        DrawRect(new Rect2(gx, gy, gw, gb), ClassicUi.Groove);
        DrawRect(new Rect2(gx, gy + gh - gb, gw, gb), ClassicUi.Groove);
        DrawRect(new Rect2(gx, gy, gb, gh), ClassicUi.Groove);
        DrawRect(new Rect2(gx + gw - gb, gy, gb, gh), ClassicUi.Groove);

        var font = ThemeDB.FallbackFont;
        const int fontSize = 15;
        float textW = font.GetStringSize(_caption, HorizontalAlignment.Left, -1, fontSize).X;
        DrawRect(new Rect2(22, gy - 3, textW + 12, gb + 6), ClassicUi.PanelFace);
        DrawString(font, new Vector2(29, 20), _caption, HorizontalAlignment.Left, -1, fontSize, new Color(0, 0, 0, 0.55f));
        DrawString(font, new Vector2(28, 19), _caption, HorizontalAlignment.Left, -1, fontSize, Colors.White);
    }
}

/// <summary>Sunken light-grey recessed field inside a <see cref="ClassicWindow"/>.</summary>
public partial class ClassicField : MarginContainer
{
    public static ClassicField Create(int padding = 10)
    {
        var field = new ClassicField();
        field.AddThemeConstantOverride("margin_left", padding + 3);
        field.AddThemeConstantOverride("margin_right", padding + 3);
        field.AddThemeConstantOverride("margin_top", padding + 3);
        field.AddThemeConstantOverride("margin_bottom", padding + 3);
        field.Resized += field.QueueRedraw;
        return field;
    }

    public override void _Draw()
    {
        float w = Size.X, h = Size.Y;
        const float b = 3f;
        if (w <= 0 || h <= 0) return;
        DrawRect(new Rect2(0, 0, w, h), ClassicUi.FieldFace);
        DrawRect(new Rect2(0, 0, w, b), ClassicUi.BevelDark);
        DrawRect(new Rect2(0, 0, b, h), ClassicUi.BevelDark);
        DrawRect(new Rect2(0, h - b, w, b), ClassicUi.BevelLight);
        DrawRect(new Rect2(w - b, 0, b, h), ClassicUi.BevelLight);
    }
}
