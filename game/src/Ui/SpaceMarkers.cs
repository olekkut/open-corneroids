using System.Threading.Tasks;
using Godot;
using OpenCorneroids.Game.World;
using OpenCorneroids.Simulation.Navigation;

namespace OpenCorneroids.Game.Ui;

/// <summary>
/// Navigation markers drawn over the 3D view, as the original shows them: a diamond bracket on each asteroid with its
/// distance and mass ("2260 m / 8456 tn"), in green, and on each ship in white. Asteroids are the face-connected clusters
/// of the static world (<see cref="Clusters"/>), found again in the background a little after the world changes; ships
/// carry their own mass. A marker hides while the player is inside the thing it marks.
/// </summary>
public partial class SpaceMarkers : Control
{
    /// <summary>Lumps smaller than this (a few placed blocks) get no marker. Ours, tunable.</summary>
    const int MinAsteroidCells = 64;
    /// <summary>Seconds between re-finding asteroids after the world changed; mining changes it constantly.</summary>
    const double RefreshSeconds = 3;

    static readonly Color AsteroidColor = new(0.35f, 0.95f, 0.45f);
    static readonly Color ShipColor = new(0.75f, 0.95f, 1f);

    GridSet _grids = null!;
    List<Cluster> _asteroids = new();
    Task<List<Cluster>>? _finding;
    int _foundRevision = -1;
    double _sinceFind = RefreshSeconds;

    public void Setup(GridSet grids)
    {
        _grids = grids;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    public override void _Process(double delta)
    {
        _sinceFind += delta;
        if (_finding is { IsCompleted: true } done)
        {
            if (done.IsCompletedSuccessfully) _asteroids = done.Result;
            else GD.PushWarning($"Finding asteroids failed: {done.Exception?.GetBaseException().Message}");
            _finding = null;
        }
        var world = _grids.World;
        if (_finding is null && world.Revision != _foundRevision && _sinceFind >= RefreshSeconds)
        {
            // The copy is taken here, on the main thread; only the search runs in the background.
            var cells = new ushort[world.GridSize * world.GridSize * world.GridSize];
            world.CopyCellsTo(cells);
            var palette = world.Palette;
            var masses = palette.Indices.ToDictionary(i => i, i => palette.Item(i).Block?.Mass ?? 0f);
            int size = world.GridSize;
            _finding = Task.Run(() => Clusters.Find(cells, size, v => masses.GetValueOrDefault(v), MinAsteroidCells));
            _foundRevision = world.Revision;
            _sinceFind = 0;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (GetViewport().GetCamera3D() is not { } camera) return;
        var eye = camera.GlobalPosition;
        // With stretch mode canvas_items the viewport's visible rect is already the 2D (canvas) size, so the
        // projection lands in canvas coordinates as it is.
        var world = _grids.World;
        foreach (var asteroid in _asteroids)
        {
            var center = world.ToGlobal(asteroid.Center.ToGodot());
            Mark(camera, eye, center, asteroid.Radius, $"{asteroid.Mass:0} tn", AsteroidColor);
        }
        foreach (var ship in _grids.Ships)
        {
            var mass = ship.Mass;
            if (mass.IsMassless) continue;
            var center = ship.ToGlobal(mass.CenterOfMass.ToGodot());
            if (ship.IsAsteroid)
                Mark(camera, eye, center, ship.GridSize * 0.5f, $"{mass.Mass:0} tn", AsteroidColor);
            else
                Mark(camera, eye, center, ship.GridSize * 0.5f, $"{ship.Name}  {mass.Mass:0} tn", ShipColor);
        }
    }

    void Mark(Camera3D camera, Vector3 eye, Vector3 at, float radius, string label, Color color)
    {
        float distance = eye.DistanceTo(at);
        if (distance < radius || camera.IsPositionBehind(at)) return;
        var screen = camera.UnprojectPosition(at);
        // Fades in over the first stretch outside the body, and far away keeps a faint presence.
        float alpha = Math.Clamp((distance - radius) / 20f, 0, 1) * Math.Clamp(1.4f - distance / 3000f, 0.35f, 0.85f);
        var c = color with { A = alpha };

        const float r = 14;
        // A diamond with gaps at its tips: four short strokes, like a bracket around the target.
        foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
        {
            var a = screen + new Vector2(sx * r, 0) + new Vector2(-sx * 4, sy * 4);
            var b = screen + new Vector2(0, sy * r) + new Vector2(sx * 4, -sy * 4);
            DrawLine(a, b, c, 2);
        }
        var bar = screen + new Vector2(r + 6, 0);
        DrawLine(bar + new Vector2(0, -r), bar + new Vector2(0, r), c, 1);
        var font = ThemeDB.FallbackFont;
        DrawString(font, bar + new Vector2(5, -2), $"{distance:0} m", HorizontalAlignment.Left, -1, 13, c);
        DrawString(font, bar + new Vector2(5, 13), label, HorizontalAlignment.Left, -1, 13, c);
    }
}
