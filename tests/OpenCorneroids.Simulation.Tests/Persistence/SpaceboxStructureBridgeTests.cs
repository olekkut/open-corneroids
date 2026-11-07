using System.IO.Compression;
using System.Numerics;
using OpenCorneroids.Simulation.Persistence;
using OpenCorneroids.Simulation.Persistence.Nbt;
using OpenCorneroids.Simulation.Shapes;
using Xunit;

namespace OpenCorneroids.Simulation.Tests.Persistence;

public class SpaceboxStructureBridgeTests
{
    [Fact]
    public void Import_RealSpaceboxShipFile_ParsesAllBlocksAndConsoles()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Spacebox_v0.1.5.1_windows", "Structures", "ship.sbs");
        if (!File.Exists(path)) return;

        using var fs = File.OpenRead(path);
        var snapshot = SpaceboxStructureBridge.Import(fs, "MyShip");

        Assert.NotNull(snapshot);
        Assert.True(snapshot.Size >= 16);
        Assert.NotEmpty(snapshot.Palette);

        // Verify non-zero cells match block count (203 blocks in the ship)
        int placedCount = 0;
        foreach (var cell in snapshot.Cells)
        {
            if (cell != 0) placedCount++;
        }
        Assert.Equal(203, placedCount);
        Assert.NotEmpty(snapshot.Consoles);
    }

    [Fact]
    public void Import_RealSpaceboxAsteroidFile_ParsesSuccessfully()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Spacebox_v0.1.5.1_windows", "Structures", "asteroid.sbs");
        if (!File.Exists(path)) return;

        using var fs = File.OpenRead(path);
        var snapshot = SpaceboxStructureBridge.Import(fs, "MyAsteroid");

        Assert.NotNull(snapshot);
        Assert.True(snapshot.Size >= 16);
        Assert.NotEmpty(snapshot.Palette);

        int placedCount = 0;
        foreach (var cell in snapshot.Cells)
        {
            if (cell != 0) placedCount++;
        }
        Assert.True(placedCount > 0);
    }

    [Fact]
    public void NbtReaderAndWriter_AllTagTypes_Roundtrip()
    {
        var root = new NbtCompound("RootTag");
        root.SetByte("ByteTag", 42);
        root.SetShort("ShortTag", 1337);
        root.SetInt("IntTag", 100_000);
        root.SetLong("LongTag", 9_876_543_210L);
        root.SetFloat("FloatTag", 3.14159f);
        root.SetDouble("DoubleTag", 2.718281828459045);
        root.SetByteArray("ByteArrayTag", [10, 20, 30, 40, 50]);
        root.SetString("StringTag", "OpenCorneroids Spacebox Bridge");
        root.SetIntArray("IntArrayTag", [100, 200, 300, 400]);
        root.SetLongArray("LongArrayTag", [1000L, 2000L, 3000L]);

        var list = new NbtList("ListTag", NbtTagType.String)
        {
            new NbtString("EntryAlpha"),
            new NbtString("EntryBeta"),
            new NbtString("EntryGamma")
        };
        root.Add(list);

        var nested = new NbtCompound("NestedCompound");
        nested.SetString("Child", "Value");
        root.Add(nested);

        // Serialize uncompressed
        using var rawMs = new MemoryStream();
        NbtWriter.WriteRoot(rawMs, root, "RootTag");
        rawMs.Position = 0;

        var readUncompressed = NbtReader.ReadRootUncompressed(rawMs);
        Assert.Equal("RootTag", readUncompressed.Name);
        Assert.Equal(42, readUncompressed.GetByte("ByteTag"));
        Assert.Equal(1337, readUncompressed.GetShort("ShortTag"));
        Assert.Equal(100_000, readUncompressed.GetInt("IntTag"));
        Assert.Equal(9_876_543_210L, readUncompressed.GetLong("LongTag"));
        Assert.Equal(3.14159f, readUncompressed.GetFloat("FloatTag"), 4);
        Assert.Equal(2.718281828459045, readUncompressed.GetDouble("DoubleTag"), 8);
        Assert.Equal(new byte[] { 10, 20, 30, 40, 50 }, readUncompressed.GetByteArray("ByteArrayTag"));
        Assert.Equal("OpenCorneroids Spacebox Bridge", readUncompressed.GetString("StringTag"));
        Assert.Equal(new[] { 100, 200, 300, 400 }, readUncompressed.GetIntArray("IntArrayTag"));
        Assert.Equal(new[] { 1000L, 2000L, 3000L }, readUncompressed.GetLongArray("LongArrayTag"));

        var readList = readUncompressed.GetList("ListTag");
        Assert.NotNull(readList);
        Assert.Equal(3, readList.Count);
        Assert.Equal("EntryAlpha", ((NbtString)readList[0]).Value);

        var readNested = readUncompressed.GetCompound("NestedCompound");
        Assert.NotNull(readNested);
        Assert.Equal("Value", readNested.GetString("Child"));

        // Serialize GZIP compressed
        using var gzipMs = new MemoryStream();
        NbtWriter.WriteRootToGzip(gzipMs, root, "RootTag", CompressionLevel.Optimal);
        gzipMs.Position = 0;

        var readGzip = NbtReader.ReadRoot(gzipMs);
        Assert.Equal(readUncompressed.GetString("StringTag"), readGzip.GetString("StringTag"));
        Assert.Equal(readUncompressed.GetInt("IntTag"), readGzip.GetInt("IntTag"));
    }

    [Fact]
    public void RoundTrip_SimpleShip_PreservesBlocksAndPalette()
    {
        int size = 16;
        var cells = new ushort[size * size * size];
        var palette = new List<int> { WorldSnapshot.AirBlockId, 11, 23, 28 }; // steel hull, cockpit, thruster

        // Place blocks
        cells[WorldSnapshot.CellIndex(size, 2, 2, 2)] = 1; // steel hull
        cells[WorldSnapshot.CellIndex(size, 2, 2, 3)] = 2; // cockpit
        cells[WorldSnapshot.CellIndex(size, 2, 2, 4)] = 3; // thruster

        var originalShip = new ShipSnapshot(
            Position: new Vector3(10, 20, 30),
            Rotation: Quaternion.Identity,
            LinearVelocity: Vector3.Zero,
            AngularVelocity: Vector3.Zero,
            Size: size,
            Palette: palette,
            Cells: cells);

        using var sbsStream = new MemoryStream();
        SpaceboxStructureBridge.Export(originalShip, sbsStream, "ShipyardArchitect");
        sbsStream.Position = 0;

        var importedShip = SpaceboxStructureBridge.Import(sbsStream, "ImportedShip");

        Assert.NotNull(importedShip);
        Assert.Equal(size, importedShip.Size);
        Assert.Contains(WorldSnapshot.AirBlockId, importedShip.Palette);

        // Bounding box was normalized to (0, 0, 0)
        // Original span along Z was z in [2..4], so in normalized coords: z in [0..2]
        ushort c0 = importedShip.Cells[WorldSnapshot.CellIndex(importedShip.Size, 0, 0, 0)];
        ushort c1 = importedShip.Cells[WorldSnapshot.CellIndex(importedShip.Size, 0, 0, 1)];
        ushort c2 = importedShip.Cells[WorldSnapshot.CellIndex(importedShip.Size, 0, 0, 2)];

        Assert.True(c0 > 0);
        Assert.True(c1 > 0);
        Assert.True(c2 > 0);
    }

    [Fact]
    public void RoundTrip_ConsolesWithKeyBindingsAndTargets_Preserved()
    {
        int size = 16;
        var cells = new ushort[size * size * size];
        var palette = new List<int> { WorldSnapshot.AirBlockId, 23, 28 };

        cells[WorldSnapshot.CellIndex(size, 1, 1, 1)] = 1; // cockpit console
        cells[WorldSnapshot.CellIndex(size, 1, 1, 2)] = 2; // target thruster

        var console = new SavedConsole(
            Cell: new Int3(1, 1, 1),
            Groups:
            [
                new SavedConsoleGroup(Index: 0, Key: "Space", Targets: [new Int3(1, 1, 2)]),
                new SavedConsoleGroup(Index: 1, Key: "W", Targets: [new Int3(1, 1, 2)])
            ],
            Camera: new Int3(1, 2, 1));

        var ship = new ShipSnapshot(
            Position: Vector3.Zero,
            Rotation: Quaternion.Identity,
            LinearVelocity: Vector3.Zero,
            AngularVelocity: Vector3.Zero,
            Size: size,
            Palette: palette,
            Cells: cells)
        {
            Consoles = [console]
        };

        using var sbs = new MemoryStream();
        SpaceboxStructureBridge.Export(ship, sbs, "ConsoleEngineer");
        sbs.Position = 0;

        var imported = SpaceboxStructureBridge.Import(sbs);

        Assert.Single(imported.Consoles);
        var importedConsole = imported.Consoles[0];
        Assert.Equal(new Int3(0, 0, 0), importedConsole.Cell); // Normalized bounding box shifted by (1, 1, 1)
        Assert.Equal(new Int3(0, 1, 0), importedConsole.Camera);
        Assert.Equal(2, importedConsole.Groups.Count);
        Assert.Equal(0, importedConsole.Groups[0].Index);
        Assert.Equal("Space", importedConsole.Groups[0].Key);
        Assert.Single(importedConsole.Groups[0].Targets);
        Assert.Equal(new Int3(0, 0, 1), importedConsole.Groups[0].Targets[0]);
    }

    [Fact]
    public void RoundTrip_All24Orientations_Preserved()
    {
        int size = 16;
        var cells = new ushort[size * size * size];
        var turned = new List<TurnedCell>();
        var palette = new List<int> { WorldSnapshot.AirBlockId, 11 };

        for (int i = 1; i < 24; i++)
        {
            var cell = new Int3(i % 4, (i / 4) % 4, i / 16);
            cells[WorldSnapshot.CellIndex(size, cell.X, cell.Y, cell.Z)] = 1;
            turned.Add(new TurnedCell(cell, i));
        }

        var ship = new ShipSnapshot(
            Position: Vector3.Zero,
            Rotation: Quaternion.Identity,
            LinearVelocity: Vector3.Zero,
            AngularVelocity: Vector3.Zero,
            Size: size,
            Palette: palette,
            Cells: cells)
        {
            Turned = turned
        };

        using var sbs = new MemoryStream();
        SpaceboxStructureBridge.Export(ship, sbs, "ChiralArchitect");
        sbs.Position = 0;

        var imported = SpaceboxStructureBridge.Import(sbs);

        Assert.Equal(23, imported.Turned.Count);
        foreach (var tc in turned)
        {
            Assert.Contains(imported.Turned, t => t.Cell == tc.Cell && t.Orientation == tc.Orientation);
        }
    }

    [Fact]
    public void PaletteResolution_KnownAndUnknownBlocks_HandledGracefully()
    {
        // 1x1x1 structure with unknown mod block
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.SetString("Name", "AlienFrigate");
        root.SetString("Author", "Xenomorph");

        var sizeTag = new NbtList("Size", NbtTagType.Int) { 1, 1, 1 };
        root.Add(sizeTag);

        var paletteTag = new NbtList("Palette", NbtTagType.Compound);
        var airEntry = new NbtCompound();
        airEntry.SetString("Name", "default:air");
        paletteTag.Add(airEntry);

        var alienEntry = new NbtCompound();
        alienEntry.SetString("Name", "mod_x/advanced:dark_matter_drive");
        paletteTag.Add(alienEntry);

        root.Add(paletteTag);
        root.SetIntArray("Blocks", [1]);
        root.SetByteArray("Orientations", [0]);

        using var ms = new MemoryStream();
        NbtWriter.WriteRootToGzip(ms, root);
        ms.Position = 0;

        var ship = SpaceboxStructureBridge.Import(ms);
        Assert.NotNull(ship);
        Assert.Contains(ship.Palette, id => id > 0); // Unknown block fell back to generic solid block
        Assert.True(ship.Cells[WorldSnapshot.CellIndex(ship.Size, 0, 0, 0)] > 0);
    }

    [Fact]
    public void PaletteResolution_AirSentinels_ResolveToAir()
    {
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.SetString("Name", "VacuumCube");

        var sizeTag = new NbtList("Size", NbtTagType.Int) { 1, 1, 1 };
        root.Add(sizeTag);

        var paletteTag = new NbtList("Palette", NbtTagType.Compound);
        var emptyAir = new NbtCompound();
        emptyAir.SetString("Name", "");
        paletteTag.Add(emptyAir);

        root.Add(paletteTag);
        root.SetIntArray("Blocks", [0]);
        root.SetByteArray("Orientations", [0]);

        using var ms = new MemoryStream();
        NbtWriter.WriteRootToGzip(ms, root);
        ms.Position = 0;

        var ship = SpaceboxStructureBridge.Import(ms);
        Assert.Equal(WorldSnapshot.AirBlockId, ship.Palette[0]);
        Assert.Equal(0, ship.Cells[WorldSnapshot.CellIndex(ship.Size, 0, 0, 0)]);
    }

    [Fact]
    public void NonCubic_Structure_PaddedToSmallestEnclosingPowerOfTwo()
    {
        int sx = 5, sy = 3, sz = 9;
        var blocks = new int[sx * sy * sz];
        // One solid block at (4, 2, 8)
        blocks[(2 * sz + 8) * sx + 4] = 1;

        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.SetString("Name", "NonCubicProbe");

        root.Add(new NbtList("Size", NbtTagType.Int) { sx, sy, sz });

        var paletteTag = new NbtList("Palette", NbtTagType.Compound);
        var airEntry = new NbtCompound();
        airEntry.SetString("Name", "default:air");
        paletteTag.Add(airEntry);
        var hullEntry = new NbtCompound();
        hullEntry.SetString("Name", "default:steel_hull");
        paletteTag.Add(hullEntry);
        root.Add(paletteTag);

        root.SetIntArray("Blocks", blocks);
        root.SetByteArray("Orientations", new byte[sx * sy * sz]);

        using var ms = new MemoryStream();
        NbtWriter.WriteRootToGzip(ms, root);
        ms.Position = 0;

        var ship = SpaceboxStructureBridge.Import(ms);
        Assert.Equal(16, ship.Size); // 16 is smallest power of 2 >= max(5, 3, 9) = 9
        ushort solidCell = ship.Cells[WorldSnapshot.CellIndex(ship.Size, 4, 2, 8)];
        Assert.True(solidCell > 0);

        // Cells outside structure bounds remain air
        ushort outOfBounds = ship.Cells[WorldSnapshot.CellIndex(ship.Size, 10, 10, 10)];
        Assert.Equal(0, outOfBounds);
    }

    [Fact]
    public void CorruptNbtStream_ThrowsWorldSaveException()
    {
        using var corruptMs = new MemoryStream([0x1F, 0x8B, 0xFF, 0xFF, 0x00]);
        Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(corruptMs));
    }

    [Fact]
    public void BlocksLengthMismatch_ThrowsWorldSaveException()
    {
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.Add(new NbtList("Size", NbtTagType.Int) { 2, 2, 2 }); // 8 cells

        var paletteTag = new NbtList("Palette", NbtTagType.Compound);
        var air = new NbtCompound(); air.SetString("Name", "default:air"); paletteTag.Add(air);
        root.Add(paletteTag);

        root.SetIntArray("Blocks", [0, 0]); // Mismatch: 2 elements instead of 8

        using var ms = new MemoryStream();
        NbtWriter.WriteRootToGzip(ms, root);
        ms.Position = 0;

        Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(ms));
    }

    [Fact]
    public void OrientationsLengthMismatch_DefaultsToZero()
    {
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.Add(new NbtList("Size", NbtTagType.Int) { 1, 1, 1 });

        var paletteTag = new NbtList("Palette", NbtTagType.Compound);
        var air = new NbtCompound(); air.SetString("Name", "default:air"); paletteTag.Add(air);
        var hull = new NbtCompound(); hull.SetString("Name", "default:steel_hull"); paletteTag.Add(hull);
        root.Add(paletteTag);

        root.SetIntArray("Blocks", [1]);
        root.SetByteArray("Orientations", []); // Empty orientations array

        using var ms = new MemoryStream();
        NbtWriter.WriteRootToGzip(ms, root);
        ms.Position = 0;

        var ship = SpaceboxStructureBridge.Import(ms);
        Assert.NotNull(ship);
        Assert.Empty(ship.Turned); // Missing orientation defaulted to 0 (no turned cell)
    }

    [Fact]
    public void EmptyShip_ExportsMinimal1x1x1Structure()
    {
        var ship = new ShipSnapshot(
            Position: Vector3.Zero,
            Rotation: Quaternion.Identity,
            LinearVelocity: Vector3.Zero,
            AngularVelocity: Vector3.Zero,
            Size: 16,
            Palette: [WorldSnapshot.AirBlockId],
            Cells: new ushort[16 * 16 * 16]);

        using var ms = new MemoryStream();
        SpaceboxStructureBridge.Export(ship, ms, "EmptyTester");
        ms.Position = 0;

        var nbt = NbtReader.ReadRoot(ms);
        var sizeList = nbt.GetList("Size");
        Assert.NotNull(sizeList);
        Assert.Equal(1, ((NbtInt)sizeList[0]).Value);
        Assert.Equal(1, ((NbtInt)sizeList[1]).Value);
        Assert.Equal(1, ((NbtInt)sizeList[2]).Value);

        var blocks = nbt.GetIntArray("Blocks");
        Assert.NotNull(blocks);
        Assert.Single(blocks);
        Assert.Equal(0, blocks[0]);
    }

    [Fact]
    public void InvalidDimensions_Exceeding512OrNonPositive_ThrowsWorldSaveException()
    {
        var rootZero = new NbtCompound();
        rootZero.Add(new NbtList("Size", NbtTagType.Int) { 0, 10, 10 });
        using var msZero = new MemoryStream();
        NbtWriter.WriteRootToGzip(msZero, rootZero);
        msZero.Position = 0;
        Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(msZero));

        var rootHuge = new NbtCompound();
        rootHuge.Add(new NbtList("Size", NbtTagType.Int) { 600, 10, 10 });
        using var msHuge = new MemoryStream();
        NbtWriter.WriteRootToGzip(msHuge, rootHuge);
        msHuge.Position = 0;
        Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(msHuge));
    }
}
