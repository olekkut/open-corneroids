using Godot;

namespace OpenCorneroids.Game.Ui;

/// <summary>
/// Fullscreen loading screen displaying step-by-step progress, progress bar with percentage,
/// current active operation details, and a checklist of completed stages.
/// </summary>
public partial class LoadingScreen : Control
{
    ProgressBar _progressBar = null!;
    Label _percentLabel = null!;
    Label _stepLabel = null!;
    Label _detailLabel = null!;
    VBoxContainer _historyBox = null!;
    readonly List<string> _history = new();
    const int MaxHistory = 5;

    public void Setup(string worldName)
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // block any clicking through while loading

        var background = new ColorRect { Color = new Color(0.03f, 0.035f, 0.06f) };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(background);

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var card = new VBoxContainer { CustomMinimumSize = new Vector2(560, 0) };
        card.AddThemeConstantOverride("separation", 10);
        center.AddChild(card);

        var header = Text("OPEN CORNEROIDS", 30, new Color(0.9f, 0.95f, 1f));
        header.HorizontalAlignment = HorizontalAlignment.Center;
        card.AddChild(header);

        var subHeader = Text($"Loading {worldName}...", 16, new Color(0.6f, 0.7f, 0.85f));
        subHeader.HorizontalAlignment = HorizontalAlignment.Center;
        card.AddChild(subHeader);

        card.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });

        // Progress bar container
        var barContainer = new PanelContainer();
        _progressBar = new ProgressBar
        {
            CustomMinimumSize = new Vector2(560, 26),
            ShowPercentage = false,
            MinValue = 0,
            MaxValue = 100,
            Value = 0,
            MouseFilter = MouseFilterEnum.Ignore,
        };

        var barBg = new StyleBoxFlat
        {
            BgColor = Color.Color8(20, 24, 34),
            BorderColor = Color.Color8(60, 75, 105),
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
        };
        barBg.SetBorderWidthAll(2);

        var barFill = new StyleBoxFlat
        {
            BgColor = Color.Color8(52, 148, 245),
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3,
            CornerRadiusBottomRight = 3,
        };

        _progressBar.AddThemeStyleboxOverride("background", barBg);
        _progressBar.AddThemeStyleboxOverride("fill", barFill);

        _percentLabel = Text("0%", 13, Colors.White);
        _percentLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _percentLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _percentLabel.VerticalAlignment = VerticalAlignment.Center;
        _progressBar.AddChild(_percentLabel);

        card.AddChild(_progressBar);

        // Current step label
        _stepLabel = Text("Initializing...", 16, new Color(0.95f, 0.98f, 1f));
        _stepLabel.HorizontalAlignment = HorizontalAlignment.Center;
        card.AddChild(_stepLabel);

        // Sub-detail label
        _detailLabel = Text("", 13, new Color(0.65f, 0.75f, 0.9f));
        _detailLabel.HorizontalAlignment = HorizontalAlignment.Center;
        card.AddChild(_detailLabel);

        card.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });

        // History checklist
        _historyBox = new VBoxContainer();
        _historyBox.AddThemeConstantOverride("separation", 4);
        card.AddChild(_historyBox);
    }

    /// <summary>
    /// Updates the loading status, advances the progress bar, and awaits a render frame
    /// so the Godot viewport displays the change immediately to the user.
    /// </summary>
    public async Task SetStep(string stepText, double fraction, string? detail = null, string? completedNotice = null)
    {
        _stepLabel.Text = stepText;
        _detailLabel.Text = detail ?? "";
        double percent = Math.Clamp(fraction * 100.0, 0.0, 100.0);
        _progressBar.Value = percent;
        _percentLabel.Text = $"{(int)Math.Round(percent)}%";

        if (!string.IsNullOrWhiteSpace(completedNotice))
        {
            _history.Add(completedNotice);
            if (_history.Count > MaxHistory) _history.RemoveAt(0);

            foreach (var child in _historyBox.GetChildren()) child.QueueFree();
            foreach (var item in _history)
            {
                var row = Text($"✓ {item}", 13, new Color(0.45f, 0.85f, 0.55f));
                row.HorizontalAlignment = HorizontalAlignment.Center;
                _historyBox.AddChild(row);
            }
        }

        // Yield to allow Godot's engine and renderer to render this frame
        if (IsInsideTree())
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    static Label Text(string text, int size, Color? color = null, bool wrap = false)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        if (color is { } c) label.AddThemeColorOverride("font_color", c);
        if (wrap) label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return label;
    }
}
