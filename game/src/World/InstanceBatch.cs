using Godot;

namespace OpenCorneroids.Game.World;

/// <summary>
/// Ours, for speed: the instances of one <see cref="MultiMesh"/> written into a plain float buffer and handed to the
/// engine in one call per frame (<see cref="Commit"/>), instead of one engine call per instance and value. The
/// multimesh grows by doubling as more instances are needed and never shrinks; only the first
/// <see cref="Count"/> are drawn. Its bounds are fixed and huge, so the engine never recomputes them from the instances
/// (and never culls the batch as a whole).
/// </summary>
public sealed class InstanceBatch
{
    const int FirstCapacity = 64;
    const float Reach = 1_000_000;

    readonly int _stride;
    float[] _buffer = [];

    /// <param name="mesh">The shared mesh drawn at every instance.</param>
    /// <param name="colors">Each instance carries a colour (4 more floats after its transform).</param>
    public InstanceBatch(Mesh mesh, bool colors)
    {
        _stride = colors ? 16 : 12;
        Multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = colors,
            Mesh = mesh,
            CustomAabb = new Aabb(-Vector3.One * Reach, Vector3.One * (2 * Reach)),
        };
    }

    public MultiMesh Multimesh { get; }

    /// <summary>Instances written since <see cref="Clear"/>.</summary>
    public int Count { get; private set; }

    /// <summary>Starts a new frame's instances.</summary>
    public void Clear() => Count = 0;

    /// <summary>Appends an instance: its transform, and its colour when the batch has colours.</summary>
    public void Add(in Transform3D t, Color color = default)
    {
        if ((Count + 1) * _stride > _buffer.Length) Array.Resize(ref _buffer, Math.Max(FirstCapacity, _buffer.Length / _stride * 2) * _stride);
        int i = Count++ * _stride;
        var b = _buffer;
        // Row by row, as the engine stores a 3×4 transform: each row holds one component of the three axes and the origin.
        b[i] = t.Basis.X.X; b[i + 1] = t.Basis.Y.X; b[i + 2] = t.Basis.Z.X; b[i + 3] = t.Origin.X;
        b[i + 4] = t.Basis.X.Y; b[i + 5] = t.Basis.Y.Y; b[i + 6] = t.Basis.Z.Y; b[i + 7] = t.Origin.Y;
        b[i + 8] = t.Basis.X.Z; b[i + 9] = t.Basis.Y.Z; b[i + 10] = t.Basis.Z.Z; b[i + 11] = t.Origin.Z;
        if (_stride == 16)
        {
            b[i + 12] = color.R; b[i + 13] = color.G; b[i + 14] = color.B; b[i + 15] = color.A;
        }
    }

    /// <summary>Hands this frame's instances to the engine and draws exactly those.</summary>
    public void Commit()
    {
        var mm = Multimesh;
        int capacity = _buffer.Length / _stride;
        if (Count == 0)
        {
            mm.VisibleInstanceCount = 0;
            return;
        }
        // Resizing clears the engine's copy, which the upload below fills again at once.
        if (mm.InstanceCount != capacity)
        {
            mm.InstanceCount = capacity;
            mm.CustomAabb = new Aabb(-Vector3.One * Reach, Vector3.One * (2 * Reach));
        }
        RenderingServer.MultimeshSetBuffer(mm.GetRid(), _buffer.AsSpan(0, capacity * _stride));
        mm.VisibleInstanceCount = Count;
    }
}
