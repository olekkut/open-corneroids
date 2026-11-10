using System.Threading.Tasks;
using Godot;
using OpenCorneroids.Game.World;
using OpenCorneroids.Simulation.Navigation;
using OpenCorneroids.Simulation.Timing;

namespace OpenCorneroids.Game.Ui;

/// <summary>
/// Navigation markers drawn over the 3D view, as the original shows them: a diamond bracket on each asteroid with its
/// distance and mass ("2260 m / 8456 tn"), in green, and on each ship in white. Asteroids are the face-connected clusters
/// of the static world (<see cref="Clusters"/>), found again in the background a little after the world changes; ships
/// carry their own mass. A marker hides while the player is inside the thing it marks. Beacons get an orange marker with
/// their distance and name, drawn whether or not target squares are on, as in the original (<c>cl_beacon_markers</c>).
/// </summary>
public partial class SpaceMarkers : Control
{
    /// <summary>Lumps smaller than this (a few placed blocks) get no marker. Ours, <c>cl_marker_min_blocks</c>.</summary>
    static int MinAsteroidCells => Settings.GameSettings.Cvars.Int(Simulation.Settings.Cvars.MarkerMinBlocks);
    /// <summary>Seconds between re-finding asteroids after the world changed; mining changes it constantly.</summary>
    const double RefreshSeconds = 3;
    /// <summary>Seconds between label texts (distance, mass) being written again; the brackets move every frame. Ours.</summary>
    const double LabelSeconds = 0.1;

    /// <summary>The four strokes of the diamond bracket, as (x, y) signs.</summary>
    static readonly (int X, int Y)[] Diamond = [(-1, -1), (1, -1), (1, 1), (-1, 1)];

    static readonly Color AsteroidColor = new(0.35f, 0.95f, 0.45f);
    static readonly Color ShipColor = new(0.75f, 0.95f, 1f);
    /// <summary>Orange (255, 165, 0), as in the original.</summary>
    static readonly Color BeaconColor = Color.Color8(255, 165, 0);

    GridSet _grids = null!;
    IReadOnlyList<Beacon> _beacons = [];
    List<Cluster> _asteroids = new();
    Task<List<Cluster>>? _finding;
    int _foundRevision = -1;
    double _sinceFind = RefreshSeconds;

    // Kept between searches, which run one at a time: the cell copy, the search's buffers and the mass of each block.
    ushort[] _cellCopy = [];
    readonly Clusters.Scratch _scratch = new();
    BlockPalette? _massPalette;
    float[] _masses = [];

    /// <summary>Label texts by what they mark (an asteroid cluster or a ship), written at most every
    /// <see cref="LabelSeconds"/>.</summary>
    readonly Dictionary<object, (string Distance, string Label)> _labels = new();
    readonly RefreshClock _labelClock = new(LabelSeconds);
    bool _relabel;

    public void Setup(GridSet grids, IReadOnlyList<Beacon>? beacons = null)
    {
        _grids = grids;
        _beacons = beacons ?? [];
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    public override void _Process(double delta)
    {
        _sinceFind += delta;
        if (_labelClock.Due(delta))
        {
            _relabel = true;
            _labels.Clear();
        }
        if (_finding is { IsCompleted: true } done)
        {
            if (done.IsCompletedSuccessfully) _asteroids = done.Result;
            else GD.PushWarning($"Finding asteroids failed: {done.Exception?.GetBaseException().Message}");
            _finding = null;
        }
        var world = _grids.World;
        if (_finding is null && world.Revision != _foundRevision && _sinceFind >= RefreshSeconds)
        {
            // The copy is taken here, on the main thread; only the search runs in the background. The buffers are
            // reused: no search is running now, and the next starts only after this one has finished.
            int size = world.GridSize;
            if (_cellCopy.Length != size * size * size) _cellCopy = new ushort[size * size * size];
            world.CopyCellsTo(_cellCopy);
            var masses = MassTable(world.Palette);
            var cells = _cellCopy;
            var scratch = _scratch;
            int minCells = MinAsteroidCells;
            _finding = Task.Run(() => Clusters.Find(cells, size, v => v < masses.Length ? masses[v] : 0f, minCells, scratch));
            _foundRevision = world.Revision;
            _sinceFind = 0;
        }
        QueueRedraw();
    }

    /// <summary>Each block's mass by palette index, built once per palette.</summary>
    float[] MassTable(BlockPalette palette)
    {
        if (ReferenceEquals(palette, _massPalette)) return _masses;
        var masses = new float[palette.Count + 1];
        foreach (var i in palette.Indices) masses[i] = palette.Item(i).Block?.Mass ?? 0f;
        _massPalette = palette;
        return _masses = masses;
    }

    public override void _Draw()
    {
        if (GetViewport().GetCamera3D() is not { } camera) return;
        var eye = camera.GlobalPosition;
        // Beacons ignore "Draw target squares", as in the original.
        if (Settings.GameSettings.Cvars.Bool(Simulation.Settings.Cvars.BeaconMarkers))
            foreach (var beacon in _beacons)
                Mark(camera, eye, beacon.Position.ToGodot(), 0, beacon, static (b, _) => ((Beacon)b).Message, BeaconColor, fadeNear: false);
        // The original's "Draw target squares" option.
        if (!Settings.GameSettings.Cvars.Bool(Simulation.Settings.Cvars.DrawTargets))
        {
            _relabel = false;
            return;
        }
        // With stretch mode canvas_items the viewport's visible rect is already the 2D (canvas) size, so the
        // projection lands in canvas coordinates as it is.
        var world = _grids.World;
        foreach (var asteroid in _asteroids)
        {
            var center = world.ToGlobal(asteroid.Center.ToGodot());
            Mark(camera, eye, center, asteroid.Radius, asteroid, static (a, _) => $"{((Cluster)a).Mass:0} tn", AsteroidColor);
        }
        foreach (var ship in _grids.Ships)
        {
            var mass = ship.Mass;
            if (mass.IsMassless) continue;
            var center = ship.ToGlobal(mass.CenterOfMass.ToGodot());
            if (ship.IsAsteroid)
                Mark(camera, eye, center, ship.GridSize * 0.5f, ship, static (s, m) => $"{m:0} tn", AsteroidColor);
            else
                Mark(camera, eye, center, ship.GridSize * 0.5f, ship, static (s, m) => $"{((ShipGrid)s).Name}  {m:0} tn", ShipColor);
        }
        _relabel = false;
    }

    /// <param name="marked">What the marker stands for; its texts are kept under it between label refreshes.</param>
    /// <param name="label">Writes the second line from <paramref name="marked"/> and the ship's mass (unused for an
    /// asteroid cluster, whose mass is its own).</param>
    /// <param name="fadeNear">Whether the marker fades in over the first 20 m outside the body (asteroids and ships);
    /// a beacon, a point, shows at full strength however near.</param>
    void Mark(Camera3D camera, Vector3 eye, Vector3 at, float radius, object marked, Func<object, float, string> label, Color color,
        bool fadeNear = true)
    {
        float distance = eye.DistanceTo(at);
        if (distance < radius || camera.IsPositionBehind(at)) return;
        var screen = camera.UnprojectPosition(at);
        // Fades in over the first stretch outside the body, and far away keeps a faint presence.
        float near = fadeNear ? Math.Clamp((distance - radius) / 20f, 0, 1) : 1;
        float alpha = near * Math.Clamp(1.4f - distance / 3000f, 0.35f, 0.85f);
        var c = color with { A = alpha };

        const float r = 14;
        // A diamond with gaps at its tips: four short strokes, like a bracket around the target.
        foreach (var (sx, sy) in Diamond)
        {
            var a = screen + new Vector2(sx * r, 0) + new Vector2(-sx * 4, sy * 4);
            var b = screen + new Vector2(0, sy * r) + new Vector2(sx * 4, -sy * 4);
            DrawLine(a, b, c, 2);
        }
        var bar = screen + new Vector2(r + 6, 0);
        DrawLine(bar + new Vector2(0, -r), bar + new Vector2(0, r), c, 1);
        var font = ThemeDB.FallbackFont;
        if (_relabel || !_labels.TryGetValue(marked, out var texts))
        {
            float mass = marked is ShipGrid ship ? ship.Mass.Mass : 0;
            _labels[marked] = texts = ($"{distance:0} m", label(marked, mass));
        }
        DrawString(font, bar + new Vector2(5, -2), texts.Distance, HorizontalAlignment.Left, -1, 13, c);
        DrawString(font, bar + new Vector2(5, 13), texts.Label, HorizontalAlignment.Left, -1, 13, c);
    }
}
