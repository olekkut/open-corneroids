using System.Text;

namespace OpenCorneroids.Content.Xnb;

/// <summary>
/// A compiled XNA content file (<c>.xnb</c>) opened up: the list of type readers it names, its shared-resource count and
/// a reader positioned at the primary object. Handles plain and LZX-compressed files (XNA 3.x, format 4/5).
/// </summary>
/// <remarks>
/// Layout: <c>"XNB"</c>, a platform byte, a version byte, a flags byte (0x80 = LZX compressed), the whole file's size
/// (int32), then for a compressed file the decompressed size (int32) and LZX frames, each preceded by its compressed
/// size (two bytes, big-endian) or, when the first byte is 0xFF, by its decompressed size and then its compressed size.
/// The content: a 7-bit-encoded count of type readers, each a string name and an int32 version; a 7-bit count of shared
/// resources; then the primary object, introduced by a 7-bit reader index (0 = null).
/// </remarks>
public sealed class XnbFile
{
    /// <summary>Larger content than this is refused: the original's files are kilobytes.</summary>
    public const int MaxContentBytes = 64 * 1024 * 1024;

    XnbFile(byte[] content, IReadOnlyList<string> readers, int sharedResources, int primaryOffset)
    {
        Content = content;
        TypeReaders = readers;
        SharedResourceCount = sharedResources;
        PrimaryOffset = primaryOffset;
    }

    /// <summary>The decompressed content, starting at the type reader list.</summary>
    public byte[] Content { get; }

    /// <summary>Type reader names, e.g. <c>Microsoft.Xna.Framework.Content.Texture2DReader, ...</c>.</summary>
    public IReadOnlyList<string> TypeReaders { get; }

    public int SharedResourceCount { get; }

    /// <summary>Where in <see cref="Content"/> the primary object starts (its reader index included).</summary>
    public int PrimaryOffset { get; }

    /// <summary>The short name of the reader of the primary object, e.g. <c>Texture2DReader</c>; null for a null object.</summary>
    public string? PrimaryReader
    {
        get
        {
            var reader = OpenPrimary();
            int index = reader.Read7BitEncodedInt();
            return index == 0 ? null : ShortName(index);
        }
    }

    /// <summary>A reader at the primary object, before its reader index.</summary>
    public BinaryReader OpenPrimary()
    {
        var stream = new MemoryStream(Content, writable: false) { Position = PrimaryOffset };
        return new BinaryReader(stream, Encoding.UTF8);
    }

    /// <summary>The short class name of type reader <paramref name="oneBasedIndex"/>: the part before the first comma or
    /// generic bracket, after the last dot.</summary>
    public string ShortName(int oneBasedIndex)
    {
        if (oneBasedIndex < 1 || oneBasedIndex > TypeReaders.Count)
            throw new InvalidDataException($"XNB reader index {oneBasedIndex} is outside 1..{TypeReaders.Count}.");
        var name = TypeReaders[oneBasedIndex - 1];
        int cut = name.IndexOfAny([',', '[', '`']);
        if (cut >= 0) name = name[..cut];
        return name[(name.LastIndexOf('.') + 1)..];
    }

    public static XnbFile Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Read(buffer.ToArray());
    }

    public static XnbFile Read(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Length < 10 || file[0] != 'X' || file[1] != 'N' || file[2] != 'B')
            throw new InvalidDataException("Not an XNB file.");
        byte version = file[4], flags = file[5];
        if (version is not (4 or 5)) throw new InvalidDataException($"XNB version {version} is not supported (XNA 3.x writes 4 or 5).");
        int fileSize = BitConverter.ToInt32(file, 6);
        if (fileSize != file.Length) throw new InvalidDataException($"XNB file says it is {fileSize} bytes but is {file.Length}.");

        byte[] content;
        if ((flags & 0x80) != 0)
        {
            if (file.Length < 14) throw new InvalidDataException("XNB file is cut short.");
            int size = BitConverter.ToInt32(file, 10);
            if (size < 0 || size > MaxContentBytes) throw new InvalidDataException($"XNB content size {size} is not plausible.");
            content = Decompress(file.AsSpan(14), size);
        }
        else
        {
            content = file[10..];
        }

        using var reader = new BinaryReader(new MemoryStream(content, writable: false), Encoding.UTF8);
        int readerCount = reader.Read7BitEncodedInt();
        if (readerCount is < 0 or > 1024) throw new InvalidDataException($"XNB lists {readerCount} type readers.");
        var readers = new List<string>(readerCount);
        for (int i = 0; i < readerCount; i++)
        {
            readers.Add(reader.ReadString());
            reader.ReadInt32();   // reader version
        }
        int shared = reader.Read7BitEncodedInt();
        if (shared < 0) throw new InvalidDataException("XNB shared resource count is negative.");
        return new XnbFile(content, readers, shared, (int)reader.BaseStream.Position);
    }

    static byte[] Decompress(ReadOnlySpan<byte> data, int size)
    {
        var output = new byte[size];
        var lzx = new LzxDecoder(16);
        int inPos = 0, outPos = 0;
        while (outPos < size)
        {
            if (inPos + 2 > data.Length) throw new InvalidDataException("XNB compressed data ends early.");
            int frameSize = 0x8000;
            int blockSize;
            if (data[inPos] == 0xFF)
            {
                if (inPos + 5 > data.Length) throw new InvalidDataException("XNB compressed data ends early.");
                frameSize = (data[inPos + 1] << 8) | data[inPos + 2];
                blockSize = (data[inPos + 3] << 8) | data[inPos + 4];
                inPos += 5;
            }
            else
            {
                blockSize = (data[inPos] << 8) | data[inPos + 1];
                inPos += 2;
            }
            if (blockSize == 0 || frameSize == 0) break;
            if (inPos + blockSize > data.Length) throw new InvalidDataException("XNB compressed frame runs past the file.");
            frameSize = Math.Min(frameSize, size - outPos);
            lzx.Decompress(data.Slice(inPos, blockSize), output.AsSpan(outPos, frameSize));
            inPos += blockSize;
            outPos += frameSize;
        }
        if (outPos != size) throw new InvalidDataException($"XNB decompressed to {outPos} bytes, expected {size}.");
        return output;
    }
}
