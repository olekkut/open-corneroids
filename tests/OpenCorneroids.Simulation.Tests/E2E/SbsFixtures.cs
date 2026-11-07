using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using OpenCorneroids.Simulation.Shapes;

namespace OpenCorneroids.Simulation.Tests.E2E;

/// <summary>
/// Authoritative synthetic fixtures for Spacebox .sbs GZIP NBT structure files.
/// Implements standard Big-Endian NBT binary serialization and deserialization directly in memory.
/// </summary>
public static class SbsFixtures
{
    public const byte TagEnd = 0;
    public const byte TagByte = 1;
    public const byte TagShort = 2;
    public const byte TagInt = 3;
    public const byte TagLong = 4;
    public const byte TagFloat = 5;
    public const byte TagDouble = 6;
    public const byte TagByteArray = 7;
    public const byte TagString = 8;
    public const byte TagList = 9;
    public const byte TagCompound = 10;
    public const byte TagIntArray = 11;

    public static byte[] CreateSbsArchive(
        string name,
        string author,
        int sizeX,
        int sizeY,
        int sizeZ,
        string[] palette,
        int[] blocks,
        byte[] orientations,
        List<(Int3 consoleCell, int groupIndex, string key, List<Int3> targets)>? consoles = null)
    {
        using var uncompressed = new MemoryStream();
        using var writer = new BinaryWriter(uncompressed);

        // Root TAG_Compound("")
        writer.Write(TagCompound);
        WriteString(writer, "");

        // Version = 1
        WriteTagHeader(writer, TagInt, "Version");
        WriteInt(writer, 1);

        // Name
        WriteTagHeader(writer, TagString, "Name");
        WriteString(writer, name);

        // Author
        WriteTagHeader(writer, TagString, "Author");
        WriteString(writer, author);

        // Size [sizeX, sizeY, sizeZ]
        WriteTagHeader(writer, TagList, "Size");
        writer.Write(TagInt);
        WriteInt(writer, 3);
        WriteInt(writer, sizeX);
        WriteInt(writer, sizeY);
        WriteInt(writer, sizeZ);

        // Palette
        WriteTagHeader(writer, TagList, "Palette");
        writer.Write(TagCompound);
        WriteInt(writer, palette.Length);
        foreach (var blockName in palette)
        {
            // Compound entry with Name string
            WriteTagHeader(writer, TagString, "Name");
            WriteString(writer, blockName);
            writer.Write(TagEnd);
        }

        // Blocks (TAG_Int_Array)
        WriteTagHeader(writer, TagIntArray, "Blocks");
        WriteInt(writer, blocks.Length);
        foreach (var b in blocks)
        {
            WriteInt(writer, b);
        }

        // Orientations (TAG_Byte_Array)
        WriteTagHeader(writer, TagByteArray, "Orientations");
        WriteInt(writer, orientations.Length);
        writer.Write(orientations);

        // Consoles
        if (consoles != null && consoles.Count > 0)
        {
            WriteTagHeader(writer, TagList, "Consoles");
            writer.Write(TagCompound);
            WriteInt(writer, consoles.Count);
            foreach (var (consoleCell, groupIndex, key, targets) in consoles)
            {
                // Cell [x, y, z]
                WriteTagHeader(writer, TagList, "Cell");
                writer.Write(TagInt);
                WriteInt(writer, 3);
                WriteInt(writer, consoleCell.X);
                WriteInt(writer, consoleCell.Y);
                WriteInt(writer, consoleCell.Z);

                // Groups list of compound
                WriteTagHeader(writer, TagList, "Groups");
                writer.Write(TagCompound);
                WriteInt(writer, 1);

                WriteTagHeader(writer, TagInt, "Index");
                WriteInt(writer, groupIndex);

                WriteTagHeader(writer, TagString, "Key");
                WriteString(writer, key);

                // Targets list of list of int
                WriteTagHeader(writer, TagList, "Targets");
                writer.Write(TagList);
                WriteInt(writer, targets.Count);
                foreach (var t in targets)
                {
                    writer.Write(TagInt);
                    WriteInt(writer, 3);
                    WriteInt(writer, t.X);
                    WriteInt(writer, t.Y);
                    WriteInt(writer, t.Z);
                }
                writer.Write(TagEnd); // End group compound

                writer.Write(TagEnd); // End console compound
            }
        }

        // Root compound End
        writer.Write(TagEnd);
        writer.Flush();

        // GZIP compress
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            var rawBytes = uncompressed.ToArray();
            gzip.Write(rawBytes, 0, rawBytes.Length);
        }
        return compressed.ToArray();
    }

    public static Dictionary<string, object> ReadUncompressedNbtCompound(Stream stream)
    {
        using var reader = new BinaryReader(stream);
        var rootTag = reader.ReadByte();
        if (rootTag != TagCompound) throw new InvalidDataException($"Expected TAG_Compound root, got {rootTag}");
        var rootName = ReadString(reader);
        return ReadCompoundPayload(reader);
    }

    public static Dictionary<string, object> ReadGzipNbtCompound(Stream gzipStream)
    {
        using var decompressed = new MemoryStream();
        using (var gz = new GZipStream(gzipStream, CompressionMode.Decompress, leaveOpen: true))
        {
            gz.CopyTo(decompressed);
        }
        decompressed.Position = 0;
        return ReadUncompressedNbtCompound(decompressed);
    }

    private static Dictionary<string, object> ReadCompoundPayload(BinaryReader reader)
    {
        var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            var tagType = reader.ReadByte();
            if (tagType == TagEnd) break;
            var tagName = ReadString(reader);
            dict[tagName] = ReadPayload(reader, tagType);
        }
        return dict;
    }

    private static object ReadPayload(BinaryReader reader, byte tagType)
    {
        return tagType switch
        {
            TagByte => reader.ReadByte(),
            TagShort => ReadShort(reader),
            TagInt => ReadInt(reader),
            TagLong => ReadLong(reader),
            TagFloat => ReadFloat(reader),
            TagDouble => ReadDouble(reader),
            TagByteArray => reader.ReadBytes(ReadInt(reader)),
            TagString => ReadString(reader),
            TagList => ReadListPayload(reader),
            TagCompound => ReadCompoundPayload(reader),
            TagIntArray => ReadIntArrayPayload(reader),
            _ => throw new InvalidDataException($"Unknown NBT tag type {tagType}")
        };
    }

    private static List<object> ReadListPayload(BinaryReader reader)
    {
        var itemType = reader.ReadByte();
        var count = ReadInt(reader);
        var list = new List<object>(Math.Max(0, count));
        for (int i = 0; i < count; i++)
        {
            list.Add(ReadPayload(reader, itemType));
        }
        return list;
    }

    private static int[] ReadIntArrayPayload(BinaryReader reader)
    {
        var count = ReadInt(reader);
        var arr = new int[count];
        for (int i = 0; i < count; i++) arr[i] = ReadInt(reader);
        return arr;
    }

    private static void WriteTagHeader(BinaryWriter writer, byte tagType, string name)
    {
        writer.Write(tagType);
        WriteString(writer, name);
    }

    private static void WriteString(BinaryWriter writer, string str)
    {
        var bytes = Encoding.UTF8.GetBytes(str);
        Span<byte> len = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(len, (ushort)bytes.Length);
        writer.Write(len);
        writer.Write(bytes);
    }

    private static string ReadString(BinaryReader reader)
    {
        Span<byte> len = stackalloc byte[2];
        reader.Read(len);
        ushort length = BinaryPrimitives.ReadUInt16BigEndian(len);
        var bytes = reader.ReadBytes(length);
        return Encoding.UTF8.GetString(bytes);
    }

    private static void WriteInt(BinaryWriter writer, int val)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buf, val);
        writer.Write(buf);
    }

    private static int ReadInt(BinaryReader reader)
    {
        Span<byte> buf = stackalloc byte[4];
        reader.Read(buf);
        return BinaryPrimitives.ReadInt32BigEndian(buf);
    }

    private static short ReadShort(BinaryReader reader)
    {
        Span<byte> buf = stackalloc byte[2];
        reader.Read(buf);
        return BinaryPrimitives.ReadInt16BigEndian(buf);
    }

    private static long ReadLong(BinaryReader reader)
    {
        Span<byte> buf = stackalloc byte[8];
        reader.Read(buf);
        return BinaryPrimitives.ReadInt64BigEndian(buf);
    }

    private static float ReadFloat(BinaryReader reader)
    {
        Span<byte> buf = stackalloc byte[4];
        reader.Read(buf);
        return BinaryPrimitives.ReadSingleBigEndian(buf);
    }

    private static double ReadDouble(BinaryReader reader)
    {
        Span<byte> buf = stackalloc byte[8];
        reader.Read(buf);
        return BinaryPrimitives.ReadDoubleBigEndian(buf);
    }
}
