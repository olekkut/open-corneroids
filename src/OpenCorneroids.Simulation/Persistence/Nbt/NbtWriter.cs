using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace OpenCorneroids.Simulation.Persistence.Nbt;

/// <summary>
/// Pure C# BCL writer for binary Big-Endian Named Binary Tag (NBT) streams.
/// Supports writing raw NBT streams and GZIP-compressed NBT streams.
/// </summary>
public static class NbtWriter
{
    public static void WriteRoot(Stream stream, NbtCompound root, string rootName = "")
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(root);

        // Root TAG_Compound
        stream.WriteByte((byte)NbtTagType.Compound);
        WriteString(stream, rootName ?? root.Name ?? string.Empty);
        WriteCompoundPayload(stream, root);
    }

    public static void WriteRootToGzip(
        Stream stream,
        NbtCompound root,
        string rootName = "",
        CompressionLevel compressionLevel = CompressionLevel.Optimal)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(root);

        using (var gzip = new GZipStream(stream, compressionLevel, leaveOpen: true))
        {
            WriteRoot(gzip, root, rootName);
            gzip.Flush();
        }
    }

    private static void WriteCompoundPayload(Stream stream, NbtCompound compound)
    {
        foreach (var tag in compound)
        {
            stream.WriteByte((byte)tag.TagType);
            WriteString(stream, tag.Name);
            WritePayload(stream, tag);
        }
        stream.WriteByte((byte)NbtTagType.End);
    }

    private static void WritePayload(Stream stream, NbtTag tag)
    {
        switch (tag)
        {
            case NbtByte b:
                stream.WriteByte(b.Value);
                break;
            case NbtShort s:
                WriteShort(stream, s.Value);
                break;
            case NbtInt i:
                WriteInt(stream, i.Value);
                break;
            case NbtLong l:
                WriteLong(stream, l.Value);
                break;
            case NbtFloat f:
                WriteFloat(stream, f.Value);
                break;
            case NbtDouble d:
                WriteDouble(stream, d.Value);
                break;
            case NbtByteArray ba:
                WriteByteArray(stream, ba.Value);
                break;
            case NbtString str:
                WriteString(stream, str.Value);
                break;
            case NbtList list:
                WriteListPayload(stream, list);
                break;
            case NbtCompound comp:
                WriteCompoundPayload(stream, comp);
                break;
            case NbtIntArray ia:
                WriteIntArray(stream, ia.Value);
                break;
            case NbtLongArray la:
                WriteLongArray(stream, la.Value);
                break;
            default:
                throw new InvalidOperationException($"Unsupported NBT tag type: {tag.GetType().Name}");
        }
    }

    private static void WriteListPayload(Stream stream, NbtList list)
    {
        byte itemType = (byte)(list.Count > 0 && list.ItemType == NbtTagType.End
            ? list[0].TagType
            : list.ItemType);

        stream.WriteByte(itemType);
        WriteInt(stream, list.Count);

        for (int i = 0; i < list.Count; i++)
        {
            WritePayload(stream, list[i]);
        }
    }

    private static void WriteShort(Stream stream, short value)
    {
        Span<byte> buf = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(buf, value);
        stream.Write(buf);
    }

    private static void WriteInt(Stream stream, int value)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buf, value);
        stream.Write(buf);
    }

    private static void WriteLong(Stream stream, long value)
    {
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(buf, value);
        stream.Write(buf);
    }

    private static void WriteFloat(Stream stream, float value)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteSingleBigEndian(buf, value);
        stream.Write(buf);
    }

    private static void WriteDouble(Stream stream, double value)
    {
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(buf, value);
        stream.Write(buf);
    }

    private static void WriteString(Stream stream, string value)
    {
        value ??= string.Empty;
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > ushort.MaxValue)
            throw new InvalidOperationException($"String exceeds maximum NBT string length of {ushort.MaxValue} bytes.");

        Span<byte> lenBuf = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(lenBuf, (ushort)bytes.Length);
        stream.Write(lenBuf);
        if (bytes.Length > 0)
        {
            stream.Write(bytes);
        }
    }

    private static void WriteByteArray(Stream stream, byte[] value)
    {
        value ??= [];
        WriteInt(stream, value.Length);
        if (value.Length > 0)
        {
            stream.Write(value);
        }
    }

    private static void WriteIntArray(Stream stream, int[] value)
    {
        value ??= [];
        WriteInt(stream, value.Length);
        Span<byte> buf = stackalloc byte[4];
        for (int i = 0; i < value.Length; i++)
        {
            BinaryPrimitives.WriteInt32BigEndian(buf, value[i]);
            stream.Write(buf);
        }
    }

    private static void WriteLongArray(Stream stream, long[] value)
    {
        value ??= [];
        WriteInt(stream, value.Length);
        Span<byte> buf = stackalloc byte[8];
        for (int i = 0; i < value.Length; i++)
        {
            BinaryPrimitives.WriteInt64BigEndian(buf, value[i]);
            stream.Write(buf);
        }
    }
}
