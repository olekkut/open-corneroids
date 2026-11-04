using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Shapes;

namespace OpenCorneroids.Simulation.Persistence;

/// <summary>
/// The <c>.ocworld</c> container. Our own format; the original game's saves are not read or imitated.
/// <code>
/// "OCWS"          4 bytes ASCII magic
/// version         u16 little-endian
/// metadataLength  i32 little-endian, then that many bytes of UTF-8 JSON: everything except the cells
/// cells           one zlib stream (RFC 1950) running to the end of the file
/// </code>
/// The zlib data inflates to runs of (u16 little-endian palette index, unsigned LEB128 run length) that cover exactly
/// Size³ cells in index order. Worlds are mostly air, so runs shrink them by orders of magnitude before zlib even starts.
/// The metadata comes first and is length-prefixed so a world list can show names and itemsets without inflating cells.
/// </summary>
/// <remarks>
/// Files are untrusted (shared worlds, half-written files after a crash): <see cref="Read"/> and
/// <see cref="ReadSummary"/> report every malformed input as <see cref="WorldSaveException"/>. The cells have no length
/// prefix, so a truncated or unfinished file is caught by requiring the zlib stream to end in its Adler-32 trailer
/// exactly at the end of the file; .NET's inflater alone treats running out of input as a clean end.
/// </remarks>
public static class WorldSaveFormat
{
    /// <summary>The first format: cells, palette, player, inventory (and the optional fields added while it was current).</summary>
    public const ushort BaseVersion = 1;
    /// <summary>
    /// Version 1 plus <see cref="WorldSnapshot.Objects"/>. A version 1 reader skips unknown JSON members, so it would load
    /// such a file without its objects and erase them on its next save; the newer version makes it refuse the file
    /// ("update the game") instead. Files without objects are still written as <see cref="BaseVersion"/>, so older builds
    /// keep reading every world that has nothing for them to lose.
    /// </summary>
    public const ushort ObjectsVersion = 2;
    /// <summary>The newest version this build reads and writes.</summary>
    public const ushort CurrentVersion = ObjectsVersion;
    /// <summary>Largest accepted edge length; 1024³ cells is already 2 GiB in memory.</summary>
    public const int MaxSize = 1024;
    /// <summary>Metadata is small (names, palette, inventory); the cap stops a hostile length from allocating gigabytes.</summary>
    public const int MaxMetadataBytes = 16 * 1024 * 1024;
    /// <summary>Cells are u16, so the palette can't address more entries than this.</summary>
    public const int MaxPaletteEntries = ushort.MaxValue + 1;
    /// <summary>
    /// The compressed cells are buffered before they are inflated, so this bounds that buffer. Real worlds are mostly
    /// air and compress to kilobytes; tunable.
    /// </summary>
    public const int MaxCellBytes = 1 << 30;
    /// <summary>
    /// Most <see cref="WorldSnapshot.Objects"/> one world may hold. Far beyond any real ship, and small enough that the
    /// JSON stays well within <see cref="MaxMetadataBytes"/>; tunable.
    /// </summary>
    public const int MaxObjects = 65536;

    const int HeaderBytes = 10;
    const int ZlibHeaderBytes = 2;
    const int ZlibTrailerBytes = 4;
    const int MaxRunBytes = 2 + 5;
    static ReadOnlySpan<byte> Magic => "OCWS"u8;

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = 16,
    };

    public static void Write(Stream stream, WorldSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(snapshot);
        if ((MetadataProblem(snapshot) ?? CellsProblem(snapshot)) is { } problem)
            throw new ArgumentException($"Cannot save world: {problem}.", nameof(snapshot));

        var metadata = JsonSerializer.SerializeToUtf8Bytes(MetadataDto.From(snapshot), Json);
        if (metadata.Length > MaxMetadataBytes)
            throw new ArgumentException($"Cannot save world: metadata is {metadata.Length} bytes, over the {MaxMetadataBytes} byte limit.", nameof(snapshot));
        using var cells = CompressCells(snapshot.Cells);
        if (cells.Length > MaxCellBytes)
            throw new ArgumentException($"Cannot save world: the compressed cells are {cells.Length} bytes, over the {MaxCellBytes} byte limit.", nameof(snapshot));

        Span<byte> header = stackalloc byte[HeaderBytes];
        Magic.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], VersionFor(snapshot));
        BinaryPrimitives.WriteInt32LittleEndian(header[6..], metadata.Length);
        stream.Write(header);
        stream.Write(metadata);
        stream.Write(cells.GetBuffer(), 0, (int)cells.Length);
    }

    /// <summary>The oldest version that can hold everything in <paramref name="snapshot"/>; see <see cref="ObjectsVersion"/>.</summary>
    public static ushort VersionFor(WorldSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Objects is { Count: > 0 } ? ObjectsVersion : BaseVersion;
    }

    /// <summary>Reads the stream to its end; anything after the cells is damage, not a place for future data.</summary>
    public static WorldSnapshot Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Guard(() =>
        {
            var meta = ReadMetadata(stream);
            return meta with { Cells = ReadCells(stream, meta.Size, meta.Palette.Count) };
        });
    }

    /// <summary>
    /// Every check <see cref="Read"/> makes, cells and checksum included, without allocating the cell grid. Lets a world
    /// list tell a save whose header reads but whose body is cut off from a healthy one before the player picks it.
    /// </summary>
    /// <exception cref="WorldSaveException">The stream is not a readable world save.</exception>
    public static void Verify(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Guard(() =>
        {
            var meta = ReadMetadata(stream);
            ReadCells(stream, meta.Size, meta.Palette.Count, fill: false);
            return true;
        });
    }

    /// <summary>Header and metadata only; the cell block is not touched, so listing many worlds stays cheap.</summary>
    public static WorldSummary ReadSummary(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var meta = Guard(() => ReadMetadata(stream));
        return new WorldSummary(meta.Name, meta.SavedAt, meta.Itemset, stream is FileStream file ? file.Name : "") { Creative = meta.Creative };
    }

    /// <summary>Turns every way a hostile file can break the decoders into one exception type callers can catch.</summary>
    static T Guard<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (EndOfStreamException e)
        {
            throw new WorldSaveException("World save is truncated.", e);
        }
        catch (InvalidDataException e)
        {
            throw new WorldSaveException("World save is damaged: the compressed cell data is corrupt.", e);
        }
        catch (JsonException e)
        {
            throw new WorldSaveException($"World save is damaged: the metadata is not valid JSON ({e.Message}).", e);
        }
        catch (InvalidOperationException e)
        {
            // System.Text.Json reports some malformed UTF-8 this way rather than as JsonException.
            throw new WorldSaveException("World save is damaged: the metadata is not valid UTF-8 JSON.", e);
        }
    }

    static WorldSaveException Damaged(string detail) => new($"World save is damaged: {detail}.");

    // ---- metadata ----

    /// <summary>Returns the snapshot with empty <see cref="WorldSnapshot.Cells"/>; the caller fills them in.</summary>
    static WorldSnapshot ReadMetadata(Stream stream)
    {
        Span<byte> header = stackalloc byte[HeaderBytes];
        stream.ReadExactly(header[..4]);
        if (!header[..4].SequenceEqual(Magic))
            throw new WorldSaveException("Not an Open Corneroids world save (wrong file signature).");
        stream.ReadExactly(header[4..]);

        var version = BinaryPrimitives.ReadUInt16LittleEndian(header[4..]);
        if (version > CurrentVersion)
            throw new WorldSaveException($"World save is format version {version}, but this build reads up to version {CurrentVersion}. Update the game to load it.");
        if (version == 0) throw Damaged("format version 0 does not exist");

        var length = BinaryPrimitives.ReadInt32LittleEndian(header[6..]);
        if (length < 0 || length > MaxMetadataBytes)
            throw Damaged($"metadata length {length} is outside 0..{MaxMetadataBytes}");
        var json = new byte[length];
        stream.ReadExactly(json);

        var dto = JsonSerializer.Deserialize<MetadataDto>(json, Json) ?? throw Damaged("the metadata is null");
        var snapshot = dto.ToSnapshot();
        if (MetadataProblem(snapshot) is { } problem) throw Damaged(problem);
        return snapshot;
    }

    /// <summary>Shared by write and read so a file this build writes is always one it accepts.</summary>
    static string? MetadataProblem(WorldSnapshot s)
    {
        if (s.Name is null) return "the world has no name";
        if (s.Itemset is null || s.Itemset.Key is null || s.Itemset.Name is null || s.Itemset.PackId is null || s.Itemset.Fingerprint is null)
            return "the itemset binding is incomplete";
        if (s.Size < 1 || s.Size > MaxSize) return $"size {s.Size} is outside 1..{MaxSize}";
        if (s.Palette is null || s.Palette.Count == 0) return "the palette is empty";
        if (s.Palette.Count > MaxPaletteEntries) return $"the palette has {s.Palette.Count} entries, over the {MaxPaletteEntries} limit";
        if (s.Palette[0] != WorldSnapshot.AirBlockId) return $"palette entry 0 is {s.Palette[0]}, not the air sentinel {WorldSnapshot.AirBlockId}";
        for (int i = 1; i < s.Palette.Count; i++)
            if (s.Palette[i] < 0) return $"palette entry {i} is the negative blockId {s.Palette[i]}";
        if (s.Player is null) return "the player state is missing";
        if (!IsFinite(s.Player.Position) || !IsFinite(s.Player.Velocity) || !float.IsFinite(s.Player.Pitch) || !float.IsFinite(s.Player.Yaw))
            return "the player state has a non-finite number";
        if (s.Inventory is null) return "the inventory is missing";
        if (s.TexturePacks is null) return "the texture pack list is missing";
        if (s.TexturePacks.Any(id => id is null)) return "a texture pack id is null";
        return ObjectsProblem(s);
    }

    /// <summary>
    /// What can be checked without the itemset. The origin is the footprint's minimum cell, so it must lie inside the
    /// world; whether the whole footprint fits needs the block's shape (<see cref="PlacedObject.FootprintProblem"/>).
    /// </summary>
    static string? ObjectsProblem(WorldSnapshot s)
    {
        if (s.Objects is null) return "the object list is missing";
        if (s.Objects.Count > MaxObjects) return $"the world has {s.Objects.Count} objects, over the {MaxObjects} limit";
        for (int i = 0; i < s.Objects.Count; i++)
        {
            var o = s.Objects[i];
            if (o is null) return $"object {i} is null";
            if (o.BlockId < 0) return $"object {i} has the negative blockId {o.BlockId}";
            if (!Orientation.IsValidIndex(o.Orientation)) return $"object {i} has orientation {o.Orientation}, outside 0..{Orientation.Count - 1}";
            var p = o.Origin;
            if (p.X < 0 || p.Y < 0 || p.Z < 0 || p.X >= s.Size || p.Y >= s.Size || p.Z >= s.Size)
                return $"object {i} has origin {p}, outside the {s.Size}-cell world";
            if (!float.IsFinite(o.Open) || o.Open < 0 || o.Open > 1) return $"object {i} has open fraction {o.Open}, outside 0..1";
        }
        return null;
    }

    static string? CellsProblem(WorldSnapshot s)
    {
        long expected = (long)s.Size * s.Size * s.Size;
        if (s.Cells is null || s.Cells.Length != expected) return $"expected {expected} cells for size {s.Size}, got {s.Cells?.Length ?? 0}";
        for (int i = 0; i < s.Cells.Length; i++)
            if (s.Cells[i] >= s.Palette.Count) return $"cell {i} uses palette index {s.Cells[i]}, but the palette has {s.Palette.Count} entries";
        return null;
    }

    static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    // ---- cells ----

    static MemoryStream CompressCells(ushort[] cells)
    {
        var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            var buffer = new byte[64 * 1024];
            int used = 0;
            for (int start = 0; start < cells.Length;)
            {
                var value = cells[start];
                int end = start + 1;
                while (end < cells.Length && cells[end] == value) end++;

                if (used > buffer.Length - MaxRunBytes) { zlib.Write(buffer, 0, used); used = 0; }
                BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(used), value);
                used += 2;
                for (uint run = (uint)(end - start); ; run >>= 7)
                {
                    if (run < 0x80) { buffer[used++] = (byte)run; break; }
                    buffer[used++] = (byte)(run | 0x80);
                }
                start = end;
            }
            zlib.Write(buffer, 0, used);
        }
        return output;
    }

    /// <summary>
    /// Inflates the cells twice: once to validate every run and the checksum, then into the array. A file can declare
    /// Size 1024 in a few hundred bytes, so allocating the 2 GiB grid before the data has proven itself would let any
    /// garbage file cost that much memory. Inflating is cheap next to that, and real cell blocks are small.
    /// </summary>
    /// <param name="fill">False only validates (see <see cref="Verify"/>) and returns an empty array.</param>
    static ushort[] ReadCells(Stream stream, int size, int paletteCount, bool fill = true)
    {
        var block = ReadCellBlock(stream);
        if (block.Count < ZlibHeaderBytes) throw Damaged("the cell data is missing");
        int cmf = block[0], flg = block[1];
        if ((cmf & 0x0F) != 8 || cmf >> 4 > 7 || (cmf << 8 | flg) % 31 != 0)
            throw Damaged("the cell data is not a zlib stream");
        if ((flg & 0x20) != 0) throw Damaged("the cell data asks for a zlib preset dictionary, which this format never uses");

        long count = (long)size * size * size;
        DecodeRuns(block, count, paletteCount, cells: null);
        if (!fill) return [];
        var cells = AllocateCells(count, size);
        DecodeRuns(block, count, paletteCount, cells);
        return cells;
    }

    /// <summary>Everything left in the stream, growing with the bytes actually present rather than a declared length.</summary>
    static ArraySegment<byte> ReadCellBlock(Stream stream)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        for (int read; (read = stream.Read(chunk)) > 0;)
        {
            if (buffer.Length + read > MaxCellBytes) throw Damaged($"the cell data is over the {MaxCellBytes} byte limit");
            buffer.Write(chunk, 0, read);
        }
        return new ArraySegment<byte>(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>Validates the runs and the zlib trailer, filling <paramref name="cells"/> when it is given.</summary>
    static void DecodeRuns(ArraySegment<byte> block, long count, int paletteCount, ushort[]? cells)
    {
        var body = new TailStream(block[ZlibHeaderBytes..]);
        using var inflater = new DeflateStream(body, CompressionMode.Decompress);
        var input = new ByteReader(inflater);
        try
        {
            long filled = 0;
            while (filled < count)
            {
                int low = input.Next(), high = input.Next();
                if (high < 0) throw Damaged($"the cell data ends after {filled} of {count} cells");
                int value = low | high << 8;
                if (value >= paletteCount)
                    throw Damaged($"cell {filled} uses palette index {value}, but the palette has {paletteCount} entries");

                var run = input.NextVarint();
                if (run == 0) throw Damaged($"zero-length cell run at cell {filled}");
                if (run > count - filled) throw Damaged($"cell runs cover more than the {count} cells of the world");
                cells?.AsSpan((int)filled, (int)run).Fill((ushort)value);
                filled += run;
            }
            if (input.Next() >= 0) throw Damaged("extra data after the last cell run");
        }
        catch (IOException e)
        {
            // The input is in memory, so this is the inflater rejecting the data (ZLibException is an IOException).
            throw new WorldSaveException("World save is damaged: the compressed cell data is corrupt.", e);
        }

        // The inflater stops asking for input only once the deflate data is complete, so exactly the trailer must be
        // left: less means a cut-off or never-finished stream, more means junk after it.
        if (body.Remaining < ZlibTrailerBytes) throw Damaged("the compressed cell data is incomplete");
        if (body.Remaining > ZlibTrailerBytes) throw Damaged("unexpected bytes after the compressed cells");
        if (cells is null && BinaryPrimitives.ReadUInt32BigEndian(block.AsSpan(block.Count - ZlibTrailerBytes)) != input.Adler32)
            throw Damaged("the cell data fails its zlib checksum");
    }

    static ushort[] AllocateCells(long count, int size)
    {
        try
        {
            return new ushort[count];
        }
        catch (OutOfMemoryException e)
        {
            throw new WorldSaveException($"Not enough memory to load a world of size {size}.", e);
        }
    }

    /// <summary>Buffered byte access over the inflater, plus the Adler-32 of everything it produced.</summary>
    /// <remarks>Per-byte reads on <see cref="DeflateStream"/> are slow.</remarks>
    sealed class ByteReader(Stream source)
    {
        const uint AdlerModulus = 65521;
        /// <summary>Most bytes that can be summed before the 32-bit sums must be reduced (zlib calls it NMAX).</summary>
        const int AdlerBlock = 5552;

        readonly byte[] _buffer = new byte[64 * 1024];
        int _position, _count;
        uint _a = 1, _b;

        public uint Adler32 => _b << 16 | _a;

        /// <summary>The next byte, or -1 at the end of the data.</summary>
        public int Next()
        {
            if (_position == _count)
            {
                _count = source.Read(_buffer, 0, _buffer.Length);
                _position = 0;
                if (_count == 0) return -1;
                AddToChecksum(_buffer.AsSpan(0, _count));
            }
            return _buffer[_position++];
        }

        /// <summary>Unsigned LEB128, at most 5 bytes / 32 bits.</summary>
        public uint NextVarint()
        {
            uint value = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
                int b = Next();
                if (b < 0) throw Damaged("the cell data ends inside a run length");
                if (shift == 28 && b > 0x0F) throw Damaged("a run length does not fit in 32 bits");
                value |= (uint)(b & 0x7F) << shift;
                if (b < 0x80) return value;
            }
            throw Damaged("a run length does not fit in 32 bits");
        }

        void AddToChecksum(ReadOnlySpan<byte> data)
        {
            while (!data.IsEmpty)
            {
                var part = data[..Math.Min(data.Length, AdlerBlock)];
                foreach (var b in part) { _a += b; _b += _a; }
                _a %= AdlerModulus;
                _b %= AdlerModulus;
                data = data[part.Length..];
            }
        }
    }

    /// <summary>
    /// Feeds the deflate data to the inflater so that <see cref="Remaining"/> afterwards says exactly what it left unread.
    /// </summary>
    /// <remarks>
    /// The inflater reads ahead and silently drops whatever follows the end of the deflate data. Handing out the last
    /// <see cref="Tail"/> bytes one at a time means it never holds bytes past that end, so a finished stream leaves
    /// exactly its 4-byte trailer unread; junk after it either was never requested or kept the tail from being reached.
    /// </remarks>
    sealed class TailStream(ArraySegment<byte> data) : Stream
    {
        const int Tail = 64;
        int _position;

        public int Remaining => data.Count - _position;

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (Remaining == 0 || buffer.IsEmpty) return 0;
            int take = Remaining > Tail ? Math.Min(buffer.Length, Remaining - Tail) : 1;
            data.AsSpan(_position, take).CopyTo(buffer);
            _position += take;
            return take;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // ---- JSON shape ----
    // DTOs rather than the public records: the file format must not change when a record gains a member, and every
    // field is nullable so a missing one is reported by name instead of silently defaulting to zero.

    sealed class MetadataDto
    {
        public string? Name { get; set; }
        public DateTimeOffset? SavedAt { get; set; }
        public BindingDto? Itemset { get; set; }
        public int? Seed { get; set; }
        public int? Size { get; set; }
        public int[]? Palette { get; set; }
        public PlayerDto? Player { get; set; }
        public StackDto?[]? Inventory { get; set; }
        // Added after version 1 shipped. Optional, so older files (which lack them) still read; a missing value means
        // survival and no texture packs, which is what those files were.
        public bool? Creative { get; set; }
        public string?[]? TexturePacks { get; set; }
        // Added later still, optional for the same reason: a file without it has no objects.
        public ObjectDto?[]? Objects { get; set; }

        public static MetadataDto From(WorldSnapshot s) => new()
        {
            Name = s.Name,
            SavedAt = s.SavedAt,
            Itemset = new() { Key = s.Itemset.Key, Name = s.Itemset.Name, PackId = s.Itemset.PackId, Fingerprint = s.Itemset.Fingerprint },
            Seed = s.Seed,
            Size = s.Size,
            Palette = [.. s.Palette],
            Player = new()
            {
                Position = [s.Player.Position.X, s.Player.Position.Y, s.Player.Position.Z],
                Velocity = [s.Player.Velocity.X, s.Player.Velocity.Y, s.Player.Velocity.Z],
                Pitch = s.Player.Pitch,
                Yaw = s.Player.Yaw,
            },
            Inventory = [.. s.Inventory.Select(stack => stack is null ? null : new StackDto { ItemId = stack.ItemId, Quantity = stack.Quantity })],
            Creative = s.Creative,
            TexturePacks = [.. s.TexturePacks],
            Objects = [.. s.Objects.Select(o => new ObjectDto
            {
                BlockId = o.BlockId,
                Origin = [o.Origin.X, o.Origin.Y, o.Origin.Z],
                Orientation = o.Orientation,
                Open = o.Open,
            })],
        };

        public WorldSnapshot ToSnapshot()
        {
            var itemset = Itemset ?? throw Missing("itemset");
            var player = Player ?? throw Missing("player");
            return new WorldSnapshot(
                Name ?? throw Missing("name"),
                SavedAt ?? throw Missing("savedAt"),
                new ItemsetBinding(
                    itemset.Key ?? throw Missing("itemset.key"),
                    itemset.Name ?? throw Missing("itemset.name"),
                    itemset.PackId ?? throw Missing("itemset.packId"),
                    itemset.Fingerprint ?? throw Missing("itemset.fingerprint")),
                Seed ?? throw Missing("seed"),
                Size ?? throw Missing("size"),
                Palette ?? throw Missing("palette"),
                [],
                new PlayerState(
                    Vector(player.Position, "player.position"),
                    Vector(player.Velocity, "player.velocity"),
                    player.Pitch ?? throw Missing("player.pitch"),
                    player.Yaw ?? throw Missing("player.yaw")),
                [.. (Inventory ?? throw Missing("inventory")).Select((slot, i) => slot is null ? null : new ItemStack(
                    slot.ItemId ?? throw Missing($"inventory[{i}].itemId"),
                    slot.Quantity ?? throw Missing($"inventory[{i}].quantity")))])
            {
                Creative = Creative ?? false,
                TexturePacks = [.. (TexturePacks ?? []).Select((id, i) => id ?? throw Damaged($"metadata field 'texturePacks[{i}]' is null"))],
                Objects = [.. (Objects ?? []).Select((o, i) => o is null
                    ? throw Damaged($"metadata field 'objects[{i}]' is null")
                    : new PlacedObject(
                        o.BlockId ?? throw Missing($"objects[{i}].blockId"),
                        o.Origin is [var x, var y, var z] ? new Int3(x, y, z) : throw Damaged($"metadata field 'objects[{i}].origin' is not three integers"),
                        o.Orientation ?? throw Missing($"objects[{i}].orientation"),
                        o.Open ?? 0))],
            };
        }

        static Vector3 Vector(float[]? xyz, string field) =>
            xyz is [var x, var y, var z] ? new Vector3(x, y, z) : throw Damaged($"metadata field '{field}' is not three numbers");

        static WorldSaveException Missing(string field) => Damaged($"metadata field '{field}' is missing");
    }

    sealed class BindingDto
    {
        public string? Key { get; set; }
        public string? Name { get; set; }
        public string? PackId { get; set; }
        public string? Fingerprint { get; set; }
    }

    sealed class PlayerDto
    {
        public float[]? Position { get; set; }
        public float[]? Velocity { get; set; }
        public float? Pitch { get; set; }
        public float? Yaw { get; set; }
    }

    /// <remarks><c>open</c> may be left out; it is 0 for anything that isn't a door.</remarks>
    sealed class ObjectDto
    {
        public int? BlockId { get; set; }
        public int[]? Origin { get; set; }
        public int? Orientation { get; set; }
        public float? Open { get; set; }
    }

    sealed class StackDto
    {
        public int? ItemId { get; set; }
        public int? Quantity { get; set; }
    }
}
