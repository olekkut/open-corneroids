using System.Numerics;
using System.Runtime.InteropServices;

namespace OpenCorneroids.Simulation.Grids;

/// <summary>
/// One model's triangle mesh as plain arrays: positions, normals and texture coordinates per vertex, and three vertex
/// indices per triangle. Built once at origin zero, so the same part serves every placed copy of that model
/// (<see cref="MeshBatch.Add"/> moves it to where each copy stands).
/// </summary>
public sealed class MeshPart
{
    public MeshPart(Vector3[] positions, Vector3[] normals, Vector2[] uvs, int[] indices)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(normals);
        ArgumentNullException.ThrowIfNull(uvs);
        ArgumentNullException.ThrowIfNull(indices);
        if (normals.Length != positions.Length || uvs.Length != positions.Length)
            throw new ArgumentException("Every vertex needs a position, a normal and a texture coordinate.");
        if (indices.Length % 3 != 0) throw new ArgumentException("Indices come in threes, one triangle each.", nameof(indices));
        foreach (int i in indices)
            if ((uint)i >= (uint)positions.Length) throw new ArgumentOutOfRangeException(nameof(indices), i, "An index points past the vertices.");
        Positions = positions;
        Normals = normals;
        Uvs = uvs;
        Indices = indices;
    }

    public Vector3[] Positions { get; }
    public Vector3[] Normals { get; }
    public Vector2[] Uvs { get; }
    public int[] Indices { get; }

    public int VertexCount => Positions.Length;
}

/// <summary>
/// Ours, for speed: many placed copies of model meshes merged into one mesh, so a chunk full of thrusters, consoles and
/// power blocks is drawn in one call instead of one per object. The original also draws every multi-cell block except
/// doors and guns as part of its sector's single mesh. Each copy is the cached <see cref="MeshPart"/> moved by its
/// offset; nothing is rotated here, as the part was already built in its orientation. The buffers are reused between
/// builds (<see cref="Clear"/>), so rebuilding a chunk allocates only when it grows.
/// </summary>
public sealed class MeshBatch
{
    readonly List<Vector3> _positions = new();
    readonly List<Vector3> _normals = new();
    readonly List<Vector2> _uvs = new();
    readonly List<int> _indices = new();

    public int VertexCount => _positions.Count;
    public int IndexCount => _indices.Count;
    public bool IsEmpty => _positions.Count == 0;

    /// <summary>Appends a copy of <paramref name="part"/> moved by <paramref name="offset"/>; its indices are shifted
    /// past the vertices already in the batch.</summary>
    public void Add(MeshPart part, Vector3 offset)
    {
        ArgumentNullException.ThrowIfNull(part);
        int start = _positions.Count;
        _positions.EnsureCapacity(start + part.VertexCount);
        foreach (var p in part.Positions) _positions.Add(p + offset);
        _normals.AddRange(part.Normals);
        _uvs.AddRange(part.Uvs);
        _indices.EnsureCapacity(_indices.Count + part.Indices.Length);
        foreach (int i in part.Indices) _indices.Add(start + i);
    }

    /// <summary>Starts a new batch, keeping the buffers.</summary>
    public void Clear()
    {
        _positions.Clear();
        _normals.Clear();
        _uvs.Clear();
        _indices.Clear();
    }

    /// <summary>The merged arrays, valid until the next <see cref="Add"/> or <see cref="Clear"/>.</summary>
    public ReadOnlySpan<Vector3> Positions => CollectionsMarshal.AsSpan(_positions);
    public ReadOnlySpan<Vector3> Normals => CollectionsMarshal.AsSpan(_normals);
    public ReadOnlySpan<Vector2> Uvs => CollectionsMarshal.AsSpan(_uvs);
    public ReadOnlySpan<int> Indices => CollectionsMarshal.AsSpan(_indices);
}
