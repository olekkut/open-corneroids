using Godot;

namespace OpenCorneroids.Game.World;

/// <summary>
/// Small coloured particles that fly straight and fade: creature blood, bursts, egg goo and needle trails. One
/// <see cref="MultiMeshInstance3D"/> of camera-facing quads for all of them, so a swarm costs one draw call.
/// </summary>
public partial class Specks : MultiMeshInstance3D
{
    const int Capacity = 4096;

    struct Speck
    {
        public Vector3 Position, Velocity;
        public Color Color;
        public float Size;
        public double Age, Life;
    }

    readonly List<Speck> _specks = new();
    readonly Random _random = new();

    public override void _Ready()
    {
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardKeepScale = true,
        };
        Multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            InstanceCount = Capacity,
            VisibleInstanceCount = 0,
            Mesh = new QuadMesh { Size = Vector2.One, Material = material },
        };
        CastShadow = ShadowCastingSetting.Off;
        TopLevel = true;
    }

    /// <summary>One speck flying at <paramref name="velocity"/> for <paramref name="life"/> seconds.</summary>
    public void Add(Vector3 at, Vector3 velocity, Color color, float size, double life)
    {
        if (_specks.Count >= Capacity) _specks.RemoveAt(0);
        _specks.Add(new Speck { Position = at, Velocity = velocity, Color = color, Size = size, Life = Math.Max(0.01, life) });
    }

    /// <summary>
    /// <paramref name="count"/> specks bursting from <paramref name="at"/> in random directions at up to
    /// <paramref name="maxSpeed"/>, living between <paramref name="minLife"/> and <paramref name="maxLife"/> seconds.
    /// </summary>
    public void Burst(Vector3 at, Color color, int count, float size, double minLife, double maxLife, float maxSpeed)
    {
        for (int i = 0; i < count; i++)
        {
            var dir = new Vector3((float)_random.NextDouble() * 2 - 1, (float)_random.NextDouble() * 2 - 1, (float)_random.NextDouble() * 2 - 1);
            if (dir.LengthSquared() > 1) dir = dir.Normalized();
            Add(at, dir * maxSpeed, color, size, minLife + _random.NextDouble() * (maxLife - minLife));
        }
    }

    public override void _Process(double delta)
    {
        var mm = Multimesh;
        if (mm is null) return;
        int shown = 0;
        for (int i = _specks.Count - 1; i >= 0; i--)
        {
            var s = _specks[i];
            s.Age += delta;
            if (s.Age >= s.Life)
            {
                _specks.RemoveAt(i);
                continue;
            }
            s.Position += s.Velocity * (float)delta;
            _specks[i] = s;
        }
        foreach (var s in _specks)
        {
            float fade = (float)(1 - s.Age / s.Life);
            mm.SetInstanceTransform(shown, new Transform3D(Basis.Identity.Scaled(Vector3.One * s.Size), s.Position));
            mm.SetInstanceColor(shown, s.Color with { A = s.Color.A * fade });
            shown++;
        }
        mm.VisibleInstanceCount = shown;
    }
}
