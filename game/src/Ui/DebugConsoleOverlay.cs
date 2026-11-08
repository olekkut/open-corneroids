using Godot;
using OpenCorneroids.Simulation.Debugging;

namespace OpenCorneroids.Game.Ui;

/// <summary>
/// Drop-down developer/debug console toggled with <c>`</c> (<see cref="GameInput.DebugConsole"/>).
/// Wraps the Godot-free <see cref="DebugConsole"/> command processor in a classic bevelled panel with
/// scrollable log output, command history (<c>Up</c>/<c>Down</c>) and <c>Tab</c> autocompletion.
/// </summary>
public partial class DebugConsoleOverlay : CanvasLayer
{
    const int MaxLogLines = 200;

    static readonly Color CommandEchoColor = new(0.55f, 0.92f, 1f);
    static readonly Color OutputColor = new(0.92f, 0.94f, 0.96f);
    static readonly Color ErrorColor = new(1f, 0.48f, 0.42f);
    static readonly Color HintColor = new(0.75f, 0.78f, 0.82f);

    readonly DebugConsole _console = new();
    IDebugConsoleHost _host = null!;
    Func<bool>? _otherWindowOpen;

    Control _root = null!;
    ScrollContainer _scroll = null!;
    VBoxContainer _logBox = null!;
    LineEdit _input = null!;

    public bool IsOpen => _root is not null && _root.Visible;

    public void Setup(IDebugConsoleHost host, Func<bool>? otherWindowOpen = null)
    {
        _host = host;
        _otherWindowOpen = otherWindowOpen;
        Layer = 5;   // above HUD and modal windows

        _root = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        var backdrop = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.22f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        backdrop.GuiInput += @event =>
        {
            if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
                Close();
        };
        _root.AddChild(backdrop);

        var topCenter = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        topCenter.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        topCenter.AddThemeConstantOverride("margin_top", 18);
        topCenter.AddThemeConstantOverride("margin_left", 40);
        topCenter.AddThemeConstantOverride("margin_right", 40);
        _root.AddChild(topCenter);

        var hCenter = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        topCenter.AddChild(hCenter);

        var window = ClassicWindow.Create("Debug Console  (`)", bevel: 5, padLeft: 18, padTop: 34, padRight: 18, padBottom: 14);
        window.CustomMinimumSize = new Vector2(920, 360);
        hCenter.AddChild(window);

        var vbox = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        vbox.AddThemeConstantOverride("separation", 8);
        window.AddChild(vbox);

        // Sunken dark terminal log area.
        var logFrame = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        var logStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.10f, 0.12f, 0.15f, 0.97f),
            BorderColor = ClassicUi.BevelDark,
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            ContentMarginLeft = 10,
            ContentMarginTop = 8,
            ContentMarginRight = 10,
            ContentMarginBottom = 8,
        };
        logFrame.AddThemeStyleboxOverride("panel", logStyle);
        vbox.AddChild(logFrame);

        _scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        logFrame.AddChild(_scroll);

        _logBox = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _logBox.AddThemeConstantOverride("separation", 3);
        _scroll.AddChild(_logBox);

        // Input row: prompt + LineEdit + Run + Close buttons.
        var inputRow = new HBoxContainer();
        inputRow.AddThemeConstantOverride("separation", 8);
        vbox.AddChild(inputRow);

        var prompt = ClassicUi.Label(">", 16, Colors.White);
        prompt.VerticalAlignment = VerticalAlignment.Center;
        inputRow.AddChild(prompt);

        _input = new LineEdit
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 32),
            PlaceholderText = "Type a command or setting (sv_ricochet 1, find ship, differences, preset original, sv_cheats 1, help)...",
            CaretBlink = true,
            KeepEditingOnTextSubmit = true,
        };
        var inputBox = new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.14f, 0.18f, 0.98f),
            BorderColor = ClassicUi.BevelDark,
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
            ContentMarginTop = 4,
            ContentMarginBottom = 4,
        };
        _input.AddThemeStyleboxOverride("normal", inputBox);
        _input.AddThemeStyleboxOverride("focus", inputBox);
        _input.AddThemeFontSizeOverride("font_size", 15);
        _input.AddThemeColorOverride("font_color", Colors.White);
        _input.AddThemeColorOverride("placeholder_color", new Color(0.55f, 0.60f, 0.66f));
        _input.TextSubmitted += Submit;
        inputRow.AddChild(_input);

        var runBtn = ClassicUi.Button("Run", new Vector2(72, 32), 15);
        runBtn.Pressed += () => Submit(_input.Text);
        inputRow.AddChild(runBtn);

        var closeBtn = ClassicUi.Button("Close", new Vector2(76, 32), 15);
        closeBtn.Pressed += Close;
        inputRow.AddChild(closeBtn);

        var hint = ClassicUi.Label(
            "Settings: <name> [value] · find <text> · cvarlist · differences · reset · preset · exec <file>   Cheats (sv_cheats 1): noclip · god · give · tp · speed   (Tab complete · Up/Down history · ` or Esc close)",
            12, new Color(0.95f, 0.95f, 0.95f));
        vbox.AddChild(hint);

        AppendLine("Open Corneroids Debug Console — type 'help' for all commands.", HintColor);
    }

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public void Open()
    {
        if (IsOpen) return;
        _root.Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _input.GrabFocus();
        _input.CaretColumn = _input.Text.Length;
    }

    public void Close()
    {
        if (!IsOpen) return;
        _input.ReleaseFocus();
        _root.Visible = false;
        if (_otherWindowOpen?.Invoke() != true)
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    /// <summary>Executes <paramref name="commandLine"/> and logs both the command and its output.</summary>
    public DebugConsoleResult RunCommand(string commandLine)
    {
        var trimmed = commandLine.Trim();
        if (trimmed.Length == 0) return new DebugConsoleResult([]);

        AppendLine($"> {trimmed}", CommandEchoColor);
        var result = _console.Execute(trimmed, _host);
        if (result.ClearLog)
        {
            foreach (var child in _logBox.GetChildren()) child.QueueFree();
        }
        for (int i = 0; i < result.Lines.Count; i++)
        {
            AppendLine(result.Lines[i], result.IsErrorLine(i) ? ErrorColor : OutputColor);
            GD.Print($"[console] {result.Lines[i]}");
        }
        return result;
    }

    void Submit(string text)
    {
        var trimmed = text.Trim();
        _input.Clear();
        if (trimmed.Length > 0) RunCommand(trimmed);
        _input.GrabFocus();
    }

    void AppendLine(string text, Color color)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        label.AddThemeFontSizeOverride("font_size", 14);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.65f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        _logBox.AddChild(label);

        while (_logBox.GetChildCount() > MaxLogLines)
        {
            var oldest = _logBox.GetChild(0);
            _logBox.RemoveChild(oldest);
            oldest.QueueFree();
        }

        Callable.From(ScrollToBottom).CallDeferred();
    }

    void ScrollToBottom()
    {
        if (!IsInstanceValid(_scroll)) return;
        if (_scroll.GetVScrollBar() is { } bar)
            _scroll.ScrollVertical = (int)bar.MaxValue;
    }

    public override void _Input(InputEvent @event)
    {
        if (!IsOpen) return;
        if (@event is not InputEventKey { Pressed: true, Echo: false } k) return;

        if (k.IsAction(GameInput.DebugConsole) ||
            k.PhysicalKeycode is Key.Quoteleft or Key.Section or Key.Escape ||
            k.Keycode is Key.Quoteleft or Key.Asciitilde or Key.Section or Key.Escape)
        {
            Close();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (k.Keycode == Key.Up)
        {
            _input.Text = _console.HistoryUp(_input.Text);
            _input.CaretColumn = _input.Text.Length;
            GetViewport().SetInputAsHandled();
        }
        else if (k.Keycode == Key.Down)
        {
            _input.Text = _console.HistoryDown(_input.Text);
            _input.CaretColumn = _input.Text.Length;
            GetViewport().SetInputAsHandled();
        }
        else if (k.Keycode == Key.Tab)
        {
            _input.Text = _console.Autocomplete(_input.Text, _host.Cvars);
            _input.CaretColumn = _input.Text.Length;
            GetViewport().SetInputAsHandled();
        }
    }
}
