using System.Numerics;

namespace OpenCorneroids.Content.Xnb;

/// <summary>One drawable piece of a model: triangles over its own vertices.</summary>
/// <param name="Positions">Model space, as stored (bone transforms are not applied).</param>
/// <param name="Normals">One per position; zero when the vertex format has none.</param>
/// <param name="Uvs">One per position; zero when the vertex format has none.</param>
/// <param name="Indices">Triangle list, three per triangle, into this part's vertices.</param>
public sealed record XnbMeshPart(Vector3[] Positions, Vector3[] Normals, Vector2[] Uvs, int[] Indices);

/// <summary>A bone: its name, transform relative to its parent, and parent (-1 for a root).</summary>
public sealed record XnbBone(string Name, Matrix4x4 Transform, int Parent);

/// <summary>
/// An XNA 3.x <c>Model</c> read from a <c>.xnb</c>: its bones and the triangles of every mesh part, with the texture
/// path its effect names. Enough to draw the original's creature and player models.
/// </summary>
/// <remarks>
/// Layout of the primary object (XNA 3.x, every count a uint32): bones (each a name string object and a 4×4 matrix),
/// then each bone's parent and children as bone references (a byte below 255 bones, else a uint32; 0 = none, n = bone
/// n−1); the model's vertex declarations (each an object: element count, then per element stream and offset (int16),
/// format, method, usage and usage index (bytes)); the meshes (name, parent bone, bounding sphere, vertex buffer object
/// (byte size and data), index buffer object (16-bit flag, byte size, data), tag object, parts (stream offset, base
/// vertex, vertex count, start index, primitive count, vertex declaration index, tag object, effect as a shared
/// resource index)); the root bone; the tag. The shared resources follow, here a <c>BasicEffect</c> whose texture is an
/// external reference.
/// </remarks>
public sealed class XnbModel
{
    XnbModel(IReadOnlyList<XnbBone> bones, IReadOnlyList<XnbMeshPart> parts, string? texturePath)
    {
        Bones = bones;
        Parts = parts;
        TexturePath = texturePath;
    }

    public IReadOnlyList<XnbBone> Bones { get; }
    public IReadOnlyList<XnbMeshPart> Parts { get; }

    /// <summary>The first effect's texture as written in the file (relative to the model, e.g. <c>..\needle_0</c>), if any.</summary>
    public string? TexturePath { get; }

    /// <summary>Corners of the box around every vertex of every part.</summary>
    public (Vector3 Min, Vector3 Max) Bounds
    {
        get
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var part in Parts)
                foreach (var p in part.Positions)
                {
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
            return Parts.Count == 0 ? default : (min, max);
        }
    }

    // XNA 3.x VertexElementFormat and VertexElementUsage values used by models.
    const byte FormatVector2 = 1, FormatVector3 = 2, FormatVector4 = 3, FormatColor = 4;
    const byte UsagePosition = 0, UsageNormal = 3, UsageTextureCoordinate = 5;

    sealed record Element(int Offset, byte Format, byte Usage, byte UsageIndex);

    public static XnbModel Read(XnbFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var r = file.OpenPrimary();
        if (r.Read7BitEncodedInt() is var index && (index == 0 || file.ShortName(index) != "ModelReader"))
            throw new InvalidDataException("XNB file does not hold a model.");

        int boneCount = Count(r, 1024, "bones");
        var names = new string[boneCount];
        var transforms = new Matrix4x4[boneCount];
        for (int i = 0; i < boneCount; i++)
        {
            names[i] = ReadStringObject(r, file) ?? "";
            transforms[i] = ReadMatrix(r);
        }
        var parents = new int[boneCount];
        for (int i = 0; i < boneCount; i++)
        {
            parents[i] = ReadBone(r, boneCount);
            int children = Count(r, boneCount, "child bones");
            for (int c = 0; c < children; c++) ReadBone(r, boneCount);
        }

        int declarationCount = Count(r, 256, "vertex declarations");
        var declarations = new List<Element[]>(declarationCount);
        for (int i = 0; i < declarationCount; i++)
        {
            ExpectReader(r, file, "VertexDeclarationReader");
            int elements = Count(r, 64, "vertex elements");
            var list = new Element[elements];
            for (int e = 0; e < elements; e++)
            {
                r.ReadInt16();                    // stream
                int offset = r.ReadInt16();
                byte format = r.ReadByte();
                r.ReadByte();                     // method
                byte usage = r.ReadByte();
                byte usageIndex = r.ReadByte();
                list[e] = new Element(offset, format, usage, usageIndex);
            }
            declarations.Add(list);
        }

        int meshCount = Count(r, 1024, "meshes");
        var parts = new List<XnbMeshPart>();
        var effectsUsed = new List<int>();
        for (int m = 0; m < meshCount; m++)
        {
            ReadStringObject(r, file);
            ReadBone(r, boneCount);
            for (int i = 0; i < 4; i++) r.ReadSingle();   // bounding sphere
            ExpectReader(r, file, "VertexBufferReader");
            var vertices = r.ReadBytes(Count(r, XnbFile.MaxContentBytes, "vertex bytes"));
            ExpectReader(r, file, "IndexBufferReader");
            bool sixteen = r.ReadBoolean();
            var indexBytes = r.ReadBytes(Count(r, XnbFile.MaxContentBytes, "index bytes"));
            SkipNullObject(r, "mesh tag");
            int partCount = Count(r, 1024, "mesh parts");
            for (int p = 0; p < partCount; p++)
            {
                int streamOffset = (int)r.ReadUInt32();
                int baseVertex = (int)r.ReadUInt32();
                int numVertices = (int)r.ReadUInt32();
                int startIndex = (int)r.ReadUInt32();
                int primitives = (int)r.ReadUInt32();
                int declaration = (int)r.ReadUInt32();
                SkipNullObject(r, "part tag");
                effectsUsed.Add(r.Read7BitEncodedInt());
                if (declaration < 0 || declaration >= declarations.Count)
                    throw new InvalidDataException($"Model part names vertex declaration {declaration} of {declarations.Count}.");
                parts.Add(BuildPart(declarations[declaration], vertices, streamOffset, baseVertex, numVertices,
                    indexBytes, sixteen, startIndex, primitives));
            }
        }
        ReadBone(r, boneCount);   // root
        SkipNullObject(r, "model tag");

        // Shared resources: the effects. Only the first's texture is wanted.
        string? texture = null;
        if (file.SharedResourceCount > 0)
        {
            int reader = r.Read7BitEncodedInt();
            if (reader != 0 && file.ShortName(reader) == "BasicEffectReader") texture = r.ReadString();
        }

        var bones = Enumerable.Range(0, boneCount).Select(i => new XnbBone(names[i], transforms[i], parents[i])).ToArray();
        return new XnbModel(bones, parts, string.IsNullOrEmpty(texture) ? null : texture);
    }

    static XnbMeshPart BuildPart(Element[] elements, byte[] vertices, int streamOffset, int baseVertex, int numVertices,
        byte[] indexBytes, bool sixteen, int startIndex, int primitives)
    {
        int stride = elements.Length == 0 ? 0 : elements.Max(e => e.Offset + SizeOf(e.Format));
        if (stride <= 0) throw new InvalidDataException("Model vertex format is empty.");
        var position = elements.FirstOrDefault(e => e.Usage == UsagePosition && e.UsageIndex == 0)
                       ?? throw new InvalidDataException("Model vertices have no position.");
        var normal = elements.FirstOrDefault(e => e.Usage == UsageNormal && e.UsageIndex == 0 && e.Format == FormatVector3);
        var uv = elements.FirstOrDefault(e => e.Usage == UsageTextureCoordinate && e.UsageIndex == 0 && e.Format == FormatVector2);
        if (position.Format != FormatVector3) throw new InvalidDataException("Model positions are not Vector3.");

        int first = streamOffset + baseVertex * stride;
        if (numVertices < 0 || first < 0 || first + (long)numVertices * stride > vertices.Length)
            throw new InvalidDataException("Model part reaches outside its vertex buffer.");
        var positions = new Vector3[numVertices];
        var normals = new Vector3[numVertices];
        var uvs = new Vector2[numVertices];
        for (int v = 0; v < numVertices; v++)
        {
            int at = first + v * stride;
            positions[v] = ReadVector3(vertices, at + position.Offset);
            if (normal is not null) normals[v] = ReadVector3(vertices, at + normal.Offset);
            if (uv is not null) uvs[v] = new Vector2(BitConverter.ToSingle(vertices, at + uv.Offset), BitConverter.ToSingle(vertices, at + uv.Offset + 4));
        }

        int indexSize = sixteen ? 2 : 4;
        int count = primitives * 3;
        if (primitives < 0 || startIndex < 0 || (startIndex + (long)count) * indexSize > indexBytes.Length)
            throw new InvalidDataException("Model part reaches outside its index buffer.");
        var indices = new int[count];
        for (int i = 0; i < count; i++)
        {
            int at = (startIndex + i) * indexSize;
            int value = sixteen ? BitConverter.ToUInt16(indexBytes, at) : BitConverter.ToInt32(indexBytes, at);
            // Indices are relative to the part's base vertex.
            if (value < 0 || value >= numVertices) throw new InvalidDataException($"Model index {value} is outside 0..{numVertices - 1}.");
            indices[i] = value;
        }
        return new XnbMeshPart(positions, normals, uvs, indices);
    }

    static int SizeOf(byte format) => format switch
    {
        0 => 4,                 // Single
        FormatVector2 => 8,
        FormatVector3 => 12,
        FormatVector4 => 16,
        FormatColor => 4,
        _ => 4,
    };

    static Vector3 ReadVector3(byte[] data, int at) =>
        new(BitConverter.ToSingle(data, at), BitConverter.ToSingle(data, at + 4), BitConverter.ToSingle(data, at + 8));

    static Matrix4x4 ReadMatrix(BinaryReader r)
    {
        Span<float> m = stackalloc float[16];
        for (int i = 0; i < 16; i++) m[i] = r.ReadSingle();
        return new Matrix4x4(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
    }

    static int Count(BinaryReader r, int max, string what)
    {
        uint n = r.ReadUInt32();
        if (n > max) throw new InvalidDataException($"Model lists {n} {what}.");
        return (int)n;
    }

    static int ReadBone(BinaryReader r, int boneCount)
    {
        int reference = boneCount < 255 ? r.ReadByte() : (int)r.ReadUInt32();
        if (reference > boneCount) throw new InvalidDataException($"Model bone reference {reference} is outside 0..{boneCount}.");
        return reference - 1;
    }

    static string? ReadStringObject(BinaryReader r, XnbFile file)
    {
        int reader = r.Read7BitEncodedInt();
        if (reader == 0) return null;
        if (file.ShortName(reader) != "StringReader") throw new InvalidDataException("Model expected a string.");
        return r.ReadString();
    }

    static void ExpectReader(BinaryReader r, XnbFile file, string name)
    {
        int reader = r.Read7BitEncodedInt();
        if (reader == 0 || file.ShortName(reader) != name) throw new InvalidDataException($"Model expected a {name} object.");
    }

    static void SkipNullObject(BinaryReader r, string what)
    {
        if (r.Read7BitEncodedInt() != 0) throw new InvalidDataException($"Model {what} is not empty; tags are not supported.");
    }
}
