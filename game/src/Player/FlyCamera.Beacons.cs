using Godot;
using OpenCorneroids.Game.Ui;
using OpenCorneroids.Game.World;
using OpenCorneroids.Simulation.Navigation;

namespace OpenCorneroids.Game.Player;

/// <summary>
/// The beacon tool (B), as in the original; the rules are in <see cref="Simulation.Navigation.Beacons"/>. B on foot puts
/// it in hand and empties the hotbar selection; a slot key, the wheel or the right button put it away. The nearest
/// beacon within <c>sv_beacon_reach</c> gets a red box, and a left click removes it or, with none that near, asks for a
/// name and puts a beacon <c>sv_beacon_place_distance</c> ahead.
/// </summary>
public partial class FlyCamera
{
    /// <summary>The world's beacons, shared with the markers and the save; set by the world. Null: no beacon tool.</summary>
    public List<Beacon>? Beacons { get; set; }

    bool _beaconTool;
    /// <summary>Index in <see cref="Beacons"/> of the beacon the tool picked this frame, or -1.</summary>
    int _pickedBeacon = -1;
    /// <summary>The "Beacon" name window while it is open; play input stops meanwhile, the game runs on.</summary>
    ConfirmationDialog? _beaconDialog;
    MeshInstance3D? _beaconBox;

    /// <summary>The red box on the picked beacon: 1 m, as in the original.</summary>
    static readonly Color BeaconBoxColor = new(1f, 0f, 0f);

    static Simulation.Navigation.Beacons.Tuning BeaconRules => Simulation.Navigation.Beacons.Tuning.From(Rules);

    /// <summary>B: the beacon tool in hand instead of any hotbar item, on foot only (as in the original).</summary>
    public void EquipBeaconTool()
    {
        if (_seat is not null || _dead || Beacons is null) return;
        _bindTool = null;
        StopActions();
        _mode.Deselect();
        _beaconTool = true;
    }

    void PutAwayBeaconTool() => _beaconTool = false;

    /// <summary>
    /// Once a frame: the tool goes away if something else took the hand (a console's binding tool, an item chosen
    /// elsewhere), the nearest beacon in reach is picked, and its red box drawn.
    /// </summary>
    void UpdateBeaconTool()
    {
        if (_beaconTool && (_bindTool is not null || _mode.HeldItem is not null || _mode.SelectedBlock != BlockPalette.Air || Beacons is null))
            _beaconTool = false;
        _pickedBeacon = _beaconTool && _seat is null && !_dead
            ? Simulation.Navigation.Beacons.Pick(Beacons!, GlobalPosition.ToNumerics(), BeaconRules.Reach)
            : -1;

        if (_beaconBox is null)
        {
            if (_cornerTexture is null) return;
            _beaconBox = new MeshInstance3D
            {
                Mesh = SelectorBoxMesh(SelectorMaterial(_cornerTexture, BeaconBoxColor)),
                Visible = false,
                TopLevel = true,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_beaconBox);
        }
        _beaconBox.Visible = _pickedBeacon >= 0 && !WindowOpen;
        if (_beaconBox.Visible) _beaconBox.GlobalTransform = new Transform3D(Basis.Identity, Beacons![_pickedBeacon].Position.ToGodot());
    }

    /// <summary>A left click with the tool (on release): the picked beacon goes at once, with no question, as in the
    /// original; with none picked the name window opens.</summary>
    void ApplyBeaconToolClick()
    {
        if (Beacons is null) return;
        // Picked again now: a click can come before this frame's pick (a scripted release on the first frame).
        int picked = Simulation.Navigation.Beacons.Pick(Beacons, GlobalPosition.ToNumerics(), BeaconRules.Reach);
        if (picked >= 0)
        {
            Beacons.RemoveAt(picked);
            _pickedBeacon = -1;
            return;
        }
        AskBeaconName();
    }

    void AskBeaconName()
    {
        if (_beaconDialog is not null) return;
        StopActions();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _beaconDialog = Dialogs.AskText(_hud, "Beacon", "Beacon name:", BeaconRules.NameMax,
            typed =>
            {
                _beaconDialog = null;
                PlaceBeacon(typed);
                RecaptureAfterDialog();
            },
            () =>
            {
                _beaconDialog = null;
                RecaptureAfterDialog();
            });
    }

    /// <summary>
    /// Puts a beacon named <paramref name="typed"/> (trimmed, cut to <c>sv_beacon_name_max</c>) ahead along the view;
    /// an empty name puts none. Also the <c>--beacon</c> hook. True when one was put.
    /// </summary>
    public bool PlaceBeacon(string typed)
    {
        var rules = BeaconRules;
        if (Beacons is null || Simulation.Navigation.Beacons.Clean(typed, rules.NameMax) is not { } name) return false;
        var at = Simulation.Navigation.Beacons.PlaceAt(GlobalPosition.ToNumerics(), (-GlobalTransform.Basis.Z).ToNumerics(), rules.PlaceDistance);
        Beacons.Add(new Beacon(at, name));
        return true;
    }

    void CloseBeaconDialog()
    {
        var dialog = _beaconDialog;
        _beaconDialog = null;
        if (dialog is not null && IsInstanceValid(dialog)) dialog.Hide();
    }

    void RecaptureAfterDialog()
    {
        if (!_inputLocked && !_dead && !WindowOpen) Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    /// <summary>The help text over the crosshair while the tool is in hand (with <c>cl_show_hints</c>).</summary>
    string BeaconPrompt() =>
        !Rules.Bool(Simulation.Settings.Cvars.ShowHints) ? ""
        : _pickedBeacon >= 0 ? "Click to remove the beacon!"
        : "Click to create a new beacon!";
}
