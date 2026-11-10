using Godot;

namespace OpenCorneroids.Game.World;

/// <summary>
/// Small coloured particles that fly straight and fade: hit sparks, drill sparks, creature blood, bursts, egg goo and
/// needle trails. One <see cref="MultiMeshInstance3D"/> of camera-facing quads for all of them, so any number costs one
/// draw call, filled with one buffer upload per frame (<see cref="InstanceBatch"/>).
/// </summary>
/// <remarks>
/// The specks live in a ring buffer, oldest first: adding one is O(1) and, when all <see cref="Capacity"/> are in use,
/// replaces the oldest. Each frame the living ones are moved down over the dead in one pass, keeping their order.
/// </remarks>
public partial class Specks : MultiMeshInstance3D
{
    /// <summary>Specks alive at once at most; ours.</summary>
    public const int Capacity = 4096;

    struct Speck
    {
        public Vector3 Position, Velocity;
        public Color Color;
        public float Size;
        public double Age, Life;
        /// <summary>Whether it fades out over its life; sparks keep their colour to the end, as they always have.</summary>
        public bool Fades;
    }

    readonly Speck[] _ring = new Speck[Capacity];
    int _start, _count;
    readonly Random _random = new();
    InstanceBatch _batch = null!;

    /// <summary>Specks alive now.</summary>
    public int Count => _count;

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
        _batch = new InstanceBatch(new QuadMesh { Size = Vector2.One, Material = material }, colors: true);
        Multimesh = _batch.Multimesh;
        CastShadow = ShadowCastingSetting.Off;
        TopLevel = true;
    }

    /// <summary>One speck flying at <paramref name="velocity"/> for <paramref name="life"/> seconds, fading out over
    /// that time unless <paramref name="fades"/> is false.</summary>
    public void Add(Vector3 at, Vector3 velocity, Color color, float size, double life, bool fades = true)
    {
        if (_count == Capacity)
        {
            _start = (_start + 1) % Capacity;
            _count--;
        }
        _ring[(_start + _count++) % Capacity] = new Speck { Position = at, Velocity = velocity, Color = color, Size = size, Life = Math.Max(0.01, life), Fades = fades };
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

    /// <summary>
    /// <paramref name="count"/> specks of one size and life bursting from <paramref name="at"/> in random directions at
    /// <paramref name="minSpeed"/> to <paramref name="maxSpeed"/>: a spark. Every direction is equally likely and the
    /// specks keep their colour until they vanish, the look sparks had as particle nodes.
    /// </summary>
    public void Spark(Vector3 at, Color color, int count, float size, double life, float minSpeed, float maxSpeed)
    {
        for (int i = 0; i < count; i++)
            Add(at, RandomDirection() * (minSpeed + (float)_random.NextDouble() * (maxSpeed - minSpeed)), color, size, life, fades: false);
    }

    /// <summary>A unit vector with every direction equally likely: a point picked evenly inside the unit ball (retrying
    /// the corners of the cube around it, which would favour the diagonals), then normalised.</summary>
    Vector3 RandomDirection()
    {
        while (true)
        {
            var p = new Vector3((float)_random.NextDouble() * 2 - 1, (float)_random.NextDouble() * 2 - 1, (float)_random.NextDouble() * 2 - 1);
            float lengthSq = p.LengthSquared();
            if (lengthSq is > 1e-6f and <= 1f) return p / MathF.Sqrt(lengthSq);
        }
    }

    public override void _Process(double delta)
    {
        if (_batch is null) return;
        _batch.Clear();
        int kept = 0;
        for (int i = 0; i < _count; i++)
        {
            ref var s = ref _ring[(_start + i) % Capacity];
            s.Age += delta;
            if (s.Age >= s.Life) continue;
            s.Position += s.Velocity * (float)delta;
            if (kept != i) _ring[(_start + kept) % Capacity] = s;
            kept++;
            float fade = s.Fades ? (float)(1 - s.Age / s.Life) : 1f;
            _batch.Add(new Transform3D(Basis.Identity.Scaled(Vector3.One * s.Size), s.Position), s.Color with { A = s.Color.A * fade });
        }
        _count = kept;
        _batch.Commit();
    }
}
