using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace OpenCorneroids.Simulation.Persistence.Nbt;

/// <summary>
/// Pure C# BCL reader for binary Big-Endian Named Binary Tag (NBT) streams.
/// Supports both raw NBT streams and GZIP-compressed NBT archives.
/// </summary>
public static class NbtReader
{
    public static NbtCompound ReadRoot(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // Check if stream is GZIP-compressed by inspecting first 2 magic bytes
        byte b0 = ReadByteOrThrow(stream);
        byte b1 = ReadByteOrThrow(stream);

        if (b0 == 0x1F && b1 == 0x8B)
        {
            // GZIP stream: reconstruct full stream with magic bytes
            var prepended = new PrefixStream([b0, b1], stream);
            using var gz = new GZipStream(prepended, CompressionMode.Decompress, leaveOpen: true);
            using var ms = new MemoryStream();
            gz.CopyTo(ms);
            ms.Position = 0;
            return ReadRootUncompressed(ms);
        }
        else
        {
            // Uncompressed stream
            var prepended = new PrefixStream([b0, b1], stream);
            return ReadRootUncompressed(prepended);
        }
    }

    public static NbtCompound ReadRootUncompressed(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte rootType = ReadByteOrThrow(stream);
        if (rootType != (byte)NbtTagType.Compound)
        {
            throw new InvalidDataException($"Expected root tag of type TAG_Compound (10), got {rootType}.");
        }

        string rootName = ReadString(stream);
        var root = new NbtCompound(rootName);
        ReadCompoundPayload(stream, root);
        return root;
    }

    private static void ReadCompoundPayload(Stream stream, NbtCompound compound)
    {
        while (true)
        {
            byte tagType = ReadByteOrThrow(stream);
            if (tagType == (byte)NbtTagType.End)
                break;

            string name = ReadString(stream);
            var tag = ReadPayload(stream, (NbtTagType)tagType, name);
            compound.Add(tag);
        }
    }

    private static NbtTag ReadPayload(Stream stream, NbtTagType tagType, string name)
    {
        return tagType switch
        {
            NbtTagType.Byte => new NbtByte(name, ReadByteOrThrow(stream)),
            NbtTagType.Short => new NbtShort(name, ReadShort(stream)),
            NbtTagType.Int => new NbtInt(name, ReadInt(stream)),
            NbtTagType.Long => new NbtLong(name, ReadLong(stream)),
            NbtTagType.Float => new NbtFloat(name, ReadFloat(stream)),
            NbtTagType.Double => new NbtDouble(name, ReadDouble(stream)),
            NbtTagType.ByteArray => new NbtByteArray(name, ReadByteArray(stream)),
            NbtTagType.String => new NbtString(name, ReadString(stream)),
            NbtTagType.List => ReadListPayload(stream, name),
            NbtTagType.Compound => ReadCompoundTag(stream, name),
            NbtTagType.IntArray => new NbtIntArray(name, ReadIntArray(stream)),
            NbtTagType.LongArray => new NbtLongArray(name, ReadLongArray(stream)),
            _ => throw new InvalidDataException($"Unsupported or unknown NBT tag type {(byte)tagType}.")
        };
    }

    private static NbtCompound ReadCompoundTag(Stream stream, string name)
    {
        var compound = new NbtCompound(name);
        ReadCompoundPayload(stream, compound);
        return compound;
    }

    private static NbtList ReadListPayload(Stream stream, string name)
    {
        byte itemType = ReadByteOrThrow(stream);
        int count = ReadInt(stream);

        var list = new NbtList(name, (NbtTagType)itemType);
        if (count <= 0)
            return list;

        for (int i = 0; i < count; i++)
        {
            var item = ReadPayload(stream, (NbtTagType)itemType, string.Empty);
            list.Add(item);
        }
        return list;
    }

    private static byte ReadByteOrThrow(Stream stream)
    {
        int b = stream.ReadByte();
        if (b < 0) throw new EndOfStreamException("Unexpected end of NBT stream while reading byte.");
        return (byte)b;
    }

    private static void ReadExact(Stream stream, Span<byte> destination)
    {
        int totalRead = 0;
        while (totalRead < destination.Length)
        {
            int read = stream.Read(destination[totalRead..]);
            if (read == 0)
                throw new EndOfStreamException($"Unexpected end of NBT stream while reading {destination.Length} bytes.");
            totalRead += read;
        }
    }

    private static short ReadShort(Stream stream)
    {
        Span<byte> buf = stackalloc byte[2];
        ReadExact(stream, buf);
        return BinaryPrimitives.ReadInt16BigEndian(buf);
    }

    private static int ReadInt(Stream stream)
    {
        Span<byte> buf = stackalloc byte[4];
        ReadExact(stream, buf);
        return BinaryPrimitives.ReadInt32BigEndian(buf);
    }

    private static long ReadLong(Stream stream)
    {
        Span<byte> buf = stackalloc byte[8];
        ReadExact(stream, buf);
        return BinaryPrimitives.ReadInt64BigEndian(buf);
    }

    private static float ReadFloat(Stream stream)
    {
        Span<byte> buf = stackalloc byte[4];
        ReadExact(stream, buf);
        return BinaryPrimitives.ReadSingleBigEndian(buf);
    }

    private static double ReadDouble(Stream stream)
    {
        Span<byte> buf = stackalloc byte[8];
        ReadExact(stream, buf);
        return BinaryPrimitives.ReadDoubleBigEndian(buf);
    }

    private static string ReadString(Stream stream)
    {
        Span<byte> lenBuf = stackalloc byte[2];
        ReadExact(stream, lenBuf);
        ushort len = BinaryPrimitives.ReadUInt16BigEndian(lenBuf);
        if (len == 0) return string.Empty;

        byte[] bytes = new byte[len];
        ReadExact(stream, bytes);
        return Encoding.UTF8.GetString(bytes);
    }

    private static byte[] ReadByteArray(Stream stream)
    {
        int length = ReadInt(stream);
        if (length < 0) throw new InvalidDataException($"Negative byte array length: {length}.");
        if (length == 0) return [];

        byte[] bytes = new byte[length];
        ReadExact(stream, bytes);
        return bytes;
    }

    private static int[] ReadIntArray(Stream stream)
    {
        int length = ReadInt(stream);
        if (length < 0) throw new InvalidDataException($"Negative int array length: {length}.");
        if (length == 0) return [];

        var arr = new int[length];
        Span<byte> buf = stackalloc byte[4];
        for (int i = 0; i < length; i++)
        {
            ReadExact(stream, buf);
            arr[i] = BinaryPrimitives.ReadInt32BigEndian(buf);
        }
        return arr;
    }

    private static long[] ReadLongArray(Stream stream)
    {
        int length = ReadInt(stream);
        if (length < 0) throw new InvalidDataException($"Negative long array length: {length}.");
        if (length == 0) return [];

        var arr = new long[length];
        Span<byte> buf = stackalloc byte[8];
        for (int i = 0; i < length; i++)
        {
            ReadExact(stream, buf);
            arr[i] = BinaryPrimitives.ReadInt64BigEndian(buf);
        }
        return arr;
    }

    /// <summary>
    /// Helper stream that yields prefix bytes before delegating to inner stream.
    /// </summary>
    private sealed class PrefixStream : Stream
    {
        private readonly byte[] _prefix;
        private readonly Stream _inner;
        private int _prefixOffset;

        public PrefixStream(byte[] prefix, Stream inner)
        {
            _prefix = prefix;
            _inner = inner;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _prefix.Length + _inner.Length;
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count == 0) return 0;
            if (_prefixOffset < _prefix.Length)
            {
                int prefixRemaining = _prefix.Length - _prefixOffset;
                int toCopy = Math.Min(count, prefixRemaining);
                Array.Copy(_prefix, _prefixOffset, buffer, offset, toCopy);
                _prefixOffset += toCopy;
                return toCopy;
            }
            return _inner.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
