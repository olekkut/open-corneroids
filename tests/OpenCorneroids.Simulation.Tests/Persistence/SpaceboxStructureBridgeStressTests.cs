using System.IO.Compression;
using System.Numerics;
using OpenCorneroids.Simulation.Persistence;
using OpenCorneroids.Simulation.Persistence.Nbt;
using OpenCorneroids.Simulation.Shapes;
using Xunit;

namespace OpenCorneroids.Simulation.Tests.Persistence;

/// <summary>
/// Adversarial stress test suite for SpaceboxStructureBridge.
/// Validates multi-cycle round-trips, non-cubic aspect ratios, complex palette scenarios,
/// orientation invariance across all 24 orientations, and console mapping topologies.
/// All tests use isolated in-memory streams and fixtures.
/// </summary>
public class SpaceboxStructureBridgeStressTests
{
    #region 1. Extreme Round-Trips

    [Fact]
    public void MultiCycleRoundTrip_ShipSnapshot_ThreeCycles_StrictEqualityPreserved()
    {
        // Construct a rich ship snapshot with diverse blocks, orientations, and consoles
        int size = 32;
        var cells = new ushort[size * size * size];
        var palette = new List<int>
        {
            WorldSnapshot.AirBlockId, // 0: Air
            11,                       // 1: Steel Hull
            23,                       // 2: Cockpit
            28,                       // 3: Thruster
            30,                       // 4: Laser Cannon
            15,                       // 5: Glass
            20,                       // 6: Reactor
            105,                      // 7: Custom Mod Block 105
            200                       // 8: Custom Mod Block 200
        };

        // Populate a 3D structure located at local offset (4, 4, 4) to (14, 12, 16)
        // Extremities to define bounds:
        cells[WorldSnapshot.CellIndex(size, 4, 4, 4)] = 1;   // min corner
        cells[WorldSnapshot.CellIndex(size, 14, 12, 16)] = 1; // max corner

        // Cockpit and Consoles
        cells[WorldSnapshot.CellIndex(size, 8, 6, 8)] = 2;   // Cockpit console A
        cells[WorldSnapshot.CellIndex(size, 10, 8, 10)] = 2; // Cockpit console B

        // Thrusters and Cannons
        cells[WorldSnapshot.CellIndex(size, 5, 5, 5)] = 3;   // Thruster 1
        cells[WorldSnapshot.CellIndex(size, 5, 5, 6)] = 3;   // Thruster 2
        cells[WorldSnapshot.CellIndex(size, 12, 10, 12)] = 4; // Cannon 1
        cells[WorldSnapshot.CellIndex(size, 12, 10, 13)] = 4; // Cannon 2
        cells[WorldSnapshot.CellIndex(size, 7, 7, 7)] = 5;   // Glass
        cells[WorldSnapshot.CellIndex(size, 6, 6, 6)] = 6;   // Reactor
        cells[WorldSnapshot.CellIndex(size, 9, 9, 9)] = 7;   // Custom 105
        cells[WorldSnapshot.CellIndex(size, 11, 11, 11)] = 8; // Custom 200

        var turned = new List<TurnedCell>
        {
            new(new Int3(5, 5, 5), 4),
            new(new Int3(5, 5, 6), 8),
            new(new Int3(12, 10, 12), 16),
            new(new Int3(12, 10, 13), 23),
            new(new Int3(8, 6, 8), 1),
            new(new Int3(10, 8, 10), 12)
        };

        var consoles = new List<SavedConsole>
        {
            new(
                Cell: new Int3(8, 6, 8),
                Groups:
                [
                    new SavedConsoleGroup(Index: 0, Key: "Space", Targets: [new Int3(5, 5, 5), new Int3(5, 5, 6)]),
                    new SavedConsoleGroup(Index: 1, Key: "W", Targets: [new Int3(5, 5, 5)]) // Overlapping target
                ],
                Camera: new Int3(8, 7, 8)
            ),
            new(
                Cell: new Int3(10, 8, 10),
                Groups:
                [
                    new SavedConsoleGroup(Index: 2, Key: "MouseLeft", Targets: [new Int3(12, 10, 12), new Int3(12, 10, 13)])
                ],
                Camera: null // No camera
            )
        };

        var initialShip = new ShipSnapshot(
            Position: new Vector3(100, 200, 300),
            Rotation: Quaternion.Identity,
            LinearVelocity: Vector3.Zero,
            AngularVelocity: Vector3.Zero,
            Size: size,
            Palette: palette,
            Cells: cells)
        {
            Turned = turned,
            Consoles = consoles
        };

        // Cycle 1: Ship0 -> SBS1 -> Ship1
        using var sbsStream1 = new MemoryStream();
        SpaceboxStructureBridge.Export(initialShip, sbsStream1, "StressTester");
        sbsStream1.Position = 0;
        var ship1 = SpaceboxStructureBridge.Import(sbsStream1, "Cycle1Ship");

        // Cycle 2: Ship1 -> SBS2 -> Ship2
        using var sbsStream2 = new MemoryStream();
        SpaceboxStructureBridge.Export(ship1, sbsStream2, "StressTester");
        sbsStream2.Position = 0;
        var ship2 = SpaceboxStructureBridge.Import(sbsStream2, "Cycle2Ship");

        // Cycle 3: Ship2 -> SBS3 -> Ship3
        using var sbsStream3 = new MemoryStream();
        SpaceboxStructureBridge.Export(ship2, sbsStream3, "StressTester");
        sbsStream3.Position = 0;
        var ship3 = SpaceboxStructureBridge.Import(sbsStream3, "Cycle3Ship");

        // Verification between Cycle 1, Cycle 2, and Cycle 3:
        // Because Ship1 was normalized to bounding box origin (0, 0, 0), Ship1 and Ship2 and Ship3 MUST be exactly identical!
        AssertShipsEqual(ship1, ship2);
        AssertShipsEqual(ship2, ship3);

        // Verify SBS2 and SBS3 raw uncompressed NBT data equality
        sbsStream2.Position = 0;
        sbsStream3.Position = 0;
        var nbt2 = NbtReader.ReadRoot(sbsStream2);
        var nbt3 = NbtReader.ReadRoot(sbsStream3);

        Assert.Equal(nbt2.GetInt("Version"), nbt3.GetInt("Version"));
        Assert.Equal(nbt2.GetString("Name"), nbt3.GetString("Name"));
        Assert.Equal(nbt2.GetString("Author"), nbt3.GetString("Author"));
        Assert.Equal(nbt2.GetIntArray("Blocks"), nbt3.GetIntArray("Blocks"));
        Assert.Equal(nbt2.GetByteArray("Orientations"), nbt3.GetByteArray("Orientations"));

        // Verify bounding box shift math from initialShip to ship1:
        // min corner was (4, 4, 4), so offset is (-4, -4, -4)
        var shift = new Int3(4, 4, 4);
        Assert.Equal(new Int3(14, 12, 16) - shift, new Int3(10, 8, 12)); // Span: 11x9x13
        Assert.Equal(16, ship1.Size); // Smallest power-of-2 >= max(11, 9, 13) = 13 is 16

        // Check console coordinates normalized
        Assert.Equal(2, ship1.Consoles.Count);
        Assert.Equal(new Int3(8, 6, 8) - shift, ship1.Consoles[0].Cell);
        Assert.Equal(new Int3(8, 7, 8) - shift, ship1.Consoles[0].Camera);
        Assert.Equal(new Int3(10, 8, 10) - shift, ship1.Consoles[1].Cell);
        Assert.Null(ship1.Consoles[1].Camera);
    }

    [Fact]
    public void MultiCycleRoundTrip_SbsFirst_TightBounds_ThreeCycles()
    {
        // Create an SBS structure directly with tight bounds 5x4x6
        int sx = 5, sy = 4, sz = 6;
        var blocks = new int[sx * sy * sz];
        var orientations = new byte[sx * sy * sz];

        // Corners to ensure bounding box is tight
        blocks[(0 * sz + 0) * sx + 0] = 1; // (0,0,0) -> steel_hull
        blocks[((sy - 1) * sz + (sz - 1)) * sx + (sx - 1)] = 2; // (4,3,5) -> cockpit

        // An orientation
        orientations[((sy - 1) * sz + (sz - 1)) * sx + (sx - 1)] = 17;

        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.SetString("Name", "TightSBS");
        root.SetString("Author", "Architect");
        root.Add(new NbtList("Size", NbtTagType.Int) { sx, sy, sz });

        var paletteTag = new NbtList("Palette", NbtTagType.Compound);
        var airEntry = new NbtCompound(); airEntry.SetString("Name", "default:air"); paletteTag.Add(airEntry);
        var hullEntry = new NbtCompound(); hullEntry.SetString("Name", "default:steel_hull"); paletteTag.Add(hullEntry);
        var cockEntry = new NbtCompound(); cockEntry.SetString("Name", "default:cockpit"); paletteTag.Add(cockEntry);
        root.Add(paletteTag);

        root.SetIntArray("Blocks", blocks);
        root.SetByteArray("Orientations", orientations);

        // Console
        var consolesTag = new NbtList("Consoles", NbtTagType.Compound);
        var cTag = new NbtCompound();
        cTag.Add(new NbtList("Cell", NbtTagType.Int) { 4, 3, 5 });
        var groupsTag = new NbtList("Groups", NbtTagType.Compound);
        var gTag = new NbtCompound();
        gTag.SetInt("Index", 0);
        gTag.SetString("Key", "Space");
        var targetsTag = new NbtList("Targets", NbtTagType.List);
        targetsTag.Add(new NbtList(NbtTagType.Int) { 0, 0, 0 });
        gTag.Add(targetsTag);
        groupsTag.Add(gTag);
        cTag.Add(groupsTag);
        consolesTag.Add(cTag);
        root.Add(consolesTag);

        using var initialSbs = new MemoryStream();
        NbtWriter.WriteRootToGzip(initialSbs, root);

        // Cycle 1: SBS0 -> Ship1
        initialSbs.Position = 0;
        var ship1 = SpaceboxStructureBridge.Import(initialSbs);

        // Cycle 2: Ship1 -> SBS1 -> Ship2
        using var sbs1 = new MemoryStream();
        SpaceboxStructureBridge.Export(ship1, sbs1);
        sbs1.Position = 0;
        var ship2 = SpaceboxStructureBridge.Import(sbs1);

        // Cycle 3: Ship2 -> SBS2 -> Ship3
        using var sbs2 = new MemoryStream();
        SpaceboxStructureBridge.Export(ship2, sbs2);
        sbs2.Position = 0;
        var ship3 = SpaceboxStructureBridge.Import(sbs2);

        AssertShipsEqual(ship1, ship2);
        AssertShipsEqual(ship2, ship3);

        // Verify dimensions of exported SBS remain exactly 5x4x6
        sbs1.Position = 0;
        var readNbt1 = NbtReader.ReadRoot(sbs1);
        var sizeList1 = readNbt1.GetList("Size");
        Assert.NotNull(sizeList1);
        Assert.Equal(5, ((NbtInt)sizeList1[0]).Value);
        Assert.Equal(4, ((NbtInt)sizeList1[1]).Value);
        Assert.Equal(6, ((NbtInt)sizeList1[2]).Value);
    }

    [Fact]
    public void NonCubic_ExtremeAspectRatio_1x50x1()
    {
        int sx = 1, sy = 50, sz = 1;
        var blocks = new int[sx * sy * sz];
        var orientations = new byte[sx * sy * sz];

        // Bottom and top blocks
        blocks[0] = 1;  // steel hull at (0, 0, 0)
        blocks[25] = 2; // cockpit at (0, 25, 0)
        blocks[49] = 3; // thruster at (0, 49, 0)

        orientations[25] = 7; // orientation 7

        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.SetString("Name", "Tower1x50x1");
        root.Add(new NbtList("Size", NbtTagType.Int) { sx, sy, sz });

        var paletteTag = new NbtList("Palette", NbtTagType.Compound);
        var air = new NbtCompound(); air.SetString("Name", "default:air"); paletteTag.Add(air);
        var hull = new NbtCompound(); hull.SetString("Name", "default:steel_hull"); paletteTag.Add(hull);
        var cock = new NbtCompound(); cock.SetString("Name", "default:cockpit"); paletteTag.Add(cock);
        var thrust = new NbtCompound(); thrust.SetString("Name", "default:thruster"); paletteTag.Add(thrust);
        root.Add(paletteTag);

        root.SetIntArray("Blocks", blocks);
        root.SetByteArray("Orientations", orientations);

        // Console at (0, 25, 0) targeting (0, 49, 0)
        var consolesTag = new NbtList("Consoles", NbtTagType.Compound);
        var cTag = new NbtCompound();
        cTag.Add(new NbtList("Cell", NbtTagType.Int) { 0, 25, 0 });
        var groupsTag = new NbtList("Groups", NbtTagType.Compound);
        var gTag = new NbtCompound();
        gTag.SetInt("Index", 0);
        gTag.SetString("Key", "Space");
        var targetsTag = new NbtList("Targets", NbtTagType.List);
        targetsTag.Add(new NbtList(NbtTagType.Int) { 0, 49, 0 });
        gTag.Add(targetsTag);
        groupsTag.Add(gTag);
        cTag.Add(groupsTag);
        consolesTag.Add(cTag);
        root.Add(consolesTag);

        using var ms = new MemoryStream();
        NbtWriter.WriteRootToGzip(ms, root);
        ms.Position = 0;

        var ship = SpaceboxStructureBridge.Import(ms);

        // Smallest power-of-2 >= max(1, 50, 1) = 50 is 64
        Assert.Equal(64, ship.Size);

        // Verify non-air cells at (0, y, 0)
        ushort b0 = ship.Cells[WorldSnapshot.CellIndex(ship.Size, 0, 0, 0)];
        ushort b25 = ship.Cells[WorldSnapshot.CellIndex(ship.Size, 0, 25, 0)];
        ushort b49 = ship.Cells[WorldSnapshot.CellIndex(ship.Size, 0, 49, 0)];
        Assert.True(b0 > 0);
        Assert.True(b25 > 0);
        Assert.True(b49 > 0);

        // Verify orientation
        var turnedCell = Assert.Single(ship.Turned);
        Assert.Equal(new Int3(0, 25, 0), turnedCell.Cell);
        Assert.Equal(7, turnedCell.Orientation);

        // Verify console
        var console = Assert.Single(ship.Consoles);
        Assert.Equal(new Int3(0, 25, 0), console.Cell);
        Assert.Single(console.Groups);
        Assert.Equal(new Int3(0, 49, 0), console.Groups[0].Targets[0]);

        // Export and verify SBS Size tag
        using var exportMs = new MemoryStream();
        SpaceboxStructureBridge.Export(ship, exportMs);
        exportMs.Position = 0;
        var exportedNbt = NbtReader.ReadRoot(exportMs);
        var exportedSize = exportedNbt.GetList("Size");
        Assert.NotNull(exportedSize);
        Assert.Equal(1, ((NbtInt)exportedSize[0]).Value);
        Assert.Equal(50, ((NbtInt)exportedSize[1]).Value);
        Assert.Equal(1, ((NbtInt)exportedSize[2]).Value);

        // Re-import and check equality
        exportMs.Position = 0;
        var reimported = SpaceboxStructureBridge.Import(exportMs);
        AssertShipsEqual(ship, reimported);
    }

    [Fact]
    public void NonCubic_AsymmetricAspectRatio_17x3x9()
    {
        int sx = 17, sy = 3, sz = 9;
        var blocks = new int[sx * sy * sz];
        var orientations = new byte[sx * sy * sz];

        // Diagonal corners
        blocks[(0 * sz + 0) * sx + 0] = 1; // (0, 0, 0)
        blocks[(2 * sz + 8) * sx + 16] = 2; // (16, 2, 8)
        blocks[(1 * sz + 4) * sx + 8] = 3;  // (8, 1, 4) - cockpit console

        orientations[(0 * sz + 0) * sx + 0] = 3;
        orientations[(2 * sz + 8) * sx + 16] = 11;
        orientations[(1 * sz + 4) * sx + 8] = 19;

        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.SetString("Name", "Asymmetric17x3x9");
        root.Add(new NbtList("Size", NbtTagType.Int) { sx, sy, sz });

        var paletteTag = new NbtList("Palette", NbtTagType.Compound);
        var air = new NbtCompound(); air.SetString("Name", "default:air"); paletteTag.Add(air);
        var hull = new NbtCompound(); hull.SetString("Name", "default:steel_hull"); paletteTag.Add(hull);
        var thruster = new NbtCompound(); thruster.SetString("Name", "default:thruster"); paletteTag.Add(thruster);
        var cockpit = new NbtCompound(); cockpit.SetString("Name", "default:cockpit"); paletteTag.Add(cockpit);
        root.Add(paletteTag);

        root.SetIntArray("Blocks", blocks);
        root.SetByteArray("Orientations", orientations);

        // Console at (8, 1, 4) with camera at (8, 2, 4) and targets at (0, 0, 0) and (16, 2, 8)
        var consolesTag = new NbtList("Consoles", NbtTagType.Compound);
        var cTag = new NbtCompound();
        cTag.Add(new NbtList("Cell", NbtTagType.Int) { 8, 1, 4 });
        cTag.Add(new NbtList("Camera", NbtTagType.Int) { 8, 2, 4 });
        var groupsTag = new NbtList("Groups", NbtTagType.Compound);
        var gTag = new NbtCompound();
        gTag.SetInt("Index", 0);
        gTag.SetString("Key", "Space");
        var targetsTag = new NbtList("Targets", NbtTagType.List);
        targetsTag.Add(new NbtList(NbtTagType.Int) { 0, 0, 0 });
        targetsTag.Add(new NbtList(NbtTagType.Int) { 16, 2, 8 });
        gTag.Add(targetsTag);
        groupsTag.Add(gTag);
        cTag.Add(groupsTag);
        consolesTag.Add(cTag);
        root.Add(consolesTag);

        using var ms = new MemoryStream();
        NbtWriter.WriteRootToGzip(ms, root);
        ms.Position = 0;

        var ship = SpaceboxStructureBridge.Import(ms);

        // Smallest power-of-2 >= max(17, 3, 9) = 17 is 32
        Assert.Equal(32, ship.Size);

        // Verify cells
        Assert.True(ship.Cells[WorldSnapshot.CellIndex(ship.Size, 0, 0, 0)] > 0);
        Assert.True(ship.Cells[WorldSnapshot.CellIndex(ship.Size, 16, 2, 8)] > 0);
        Assert.True(ship.Cells[WorldSnapshot.CellIndex(ship.Size, 8, 1, 4)] > 0);

        // Verify orientations
        Assert.Equal(3, ship.Turned.Count);
        Assert.Contains(ship.Turned, t => t.Cell == new Int3(0, 0, 0) && t.Orientation == 3);
        Assert.Contains(ship.Turned, t => t.Cell == new Int3(16, 2, 8) && t.Orientation == 11);
        Assert.Contains(ship.Turned, t => t.Cell == new Int3(8, 1, 4) && t.Orientation == 19);

        // Verify console and camera
        var console = Assert.Single(ship.Consoles);
        Assert.Equal(new Int3(8, 1, 4), console.Cell);
        Assert.Equal(new Int3(8, 2, 4), console.Camera);
        Assert.Equal(2, console.Groups[0].Targets.Count);

        // Export and verify round-trip
        using var exportMs = new MemoryStream();
        SpaceboxStructureBridge.Export(ship, exportMs);
        exportMs.Position = 0;

        var reimported = SpaceboxStructureBridge.Import(exportMs);
        AssertShipsEqual(ship, reimported);
    }

    [Fact]
    public void NonCubic_LargeSpanAspectRatio_255x1x1()
    {
        int sx = 255, sy = 1, sz = 1;
        var blocks = new int[sx * sy * sz];
        var orientations = new byte[sx * sy * sz];

        // Place blocks at ends and middle
        blocks[0] = 1;
        blocks[127] = 2;
        blocks[254] = 1;

        orientations[127] = 15;

        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.SetString("Name", "Span255");
        root.Add(new NbtList("Size", NbtTagType.Int) { sx, sy, sz });

        var paletteTag = new NbtList("Palette", NbtTagType.Compound);
        var air = new NbtCompound(); air.SetString("Name", "default:air"); paletteTag.Add(air);
        var hull = new NbtCompound(); hull.SetString("Name", "default:steel_hull"); paletteTag.Add(hull);
        var cockpit = new NbtCompound(); cockpit.SetString("Name", "default:cockpit"); paletteTag.Add(cockpit);
        root.Add(paletteTag);

        root.SetIntArray("Blocks", blocks);
        root.SetByteArray("Orientations", orientations);

        using var ms = new MemoryStream();
        NbtWriter.WriteRootToGzip(ms, root);
        ms.Position = 0;

        var ship = SpaceboxStructureBridge.Import(ms);

        // Smallest power-of-2 >= max(255, 1, 1) = 255 is 256
        Assert.Equal(256, ship.Size);

        Assert.True(ship.Cells[WorldSnapshot.CellIndex(ship.Size, 0, 0, 0)] > 0);
        Assert.True(ship.Cells[WorldSnapshot.CellIndex(ship.Size, 127, 0, 0)] > 0);
        Assert.True(ship.Cells[WorldSnapshot.CellIndex(ship.Size, 254, 0, 0)] > 0);

        // Verify out-of-span cells are air
        Assert.Equal(0, ship.Cells[WorldSnapshot.CellIndex(ship.Size, 255, 0, 0)]);
        Assert.Equal(0, ship.Cells[WorldSnapshot.CellIndex(ship.Size, 0, 1, 0)]);

        // Verify orientation
        var turned = Assert.Single(ship.Turned);
        Assert.Equal(new Int3(127, 0, 0), turned.Cell);
        Assert.Equal(15, turned.Orientation);

        // Round-trip export
        using var exportMs = new MemoryStream();
        SpaceboxStructureBridge.Export(ship, exportMs);
        exportMs.Position = 0;

        var exportedNbt = NbtReader.ReadRoot(exportMs);
        var sizeList = exportedNbt.GetList("Size");
        Assert.NotNull(sizeList);
        Assert.Equal(255, ((NbtInt)sizeList[0]).Value);
        Assert.Equal(1, ((NbtInt)sizeList[1]).Value);
        Assert.Equal(1, ((NbtInt)sizeList[2]).Value);
    }

    #endregion

    #region 2. Complex Palette Scenarios

    [Fact]
    public void Palette_HundredsOfUniqueBlocks_PreservedExactRoundTrip()
    {
        // 300 unique block IDs: 100..399
        int uniqueCount = 300;
        int sx = 10, sy = 6, sz = 5; // 10 * 6 * 5 = 300 cells
        var blocks = new int[uniqueCount];
        for (int i = 0; i < uniqueCount; i++) blocks[i] = i + 1; // 1-based palette indices

        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.SetString("Name", "MegaPaletteShip");
        root.Add(new NbtList("Size", NbtTagType.Int) { sx, sy, sz });

        var paletteTag = new NbtList("Palette", NbtTagType.Compound);
        var airEntry = new NbtCompound(); airEntry.SetString("Name", "default:air"); paletteTag.Add(airEntry);

        for (int i = 0; i < uniqueCount; i++)
        {
            var entry = new NbtCompound();
            entry.SetString("Name", $"default:block_{100 + i}");
            paletteTag.Add(entry);
        }
        root.Add(paletteTag);

        root.SetIntArray("Blocks", blocks);
        root.SetByteArray("Orientations", new byte[uniqueCount]);

        using var ms = new MemoryStream();
        NbtWriter.WriteRootToGzip(ms, root);
        ms.Position = 0;

        var ship = SpaceboxStructureBridge.Import(ms);
        Assert.NotNull(ship);
        Assert.Equal(16, ship.Size); // >= max(10, 6, 5) is 16

        // Verify palette size: 1 (air) + 300 unique = 301
        Assert.Equal(uniqueCount + 1, ship.Palette.Count);

        // Verify each cell maps back to its original blockId
        for (int y = 0; y < sy; y++)
        {
            for (int z = 0; z < sz; z++)
            {
                for (int x = 0; x < sx; x++)
                {
                    int sbsIdx = (y * sz + z) * sx + x;
                    int expectedBlockId = 100 + sbsIdx;
                    ushort palIdx = ship.Cells[WorldSnapshot.CellIndex(ship.Size, x, y, z)];
                    Assert.Equal(expectedBlockId, ship.Palette[palIdx]);
                }
            }
        }

        // Export and verify re-import preserves all 300 blocks
        using var exportMs = new MemoryStream();
        SpaceboxStructureBridge.Export(ship, exportMs);
        exportMs.Position = 0;

        var reimported = SpaceboxStructureBridge.Import(exportMs);
        AssertShipsEqual(ship, reimported);
    }

    [Fact]
    public void Palette_CasePermutations_ResolvedCaseInsensitively()
    {
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.SetString("Name", "CaseTester");
        root.Add(new NbtList("Size", NbtTagType.Int) { 7, 1, 1 });

        var paletteTag = new NbtList("Palette", NbtTagType.Compound);
        var entries = new[]
        {
            "DEFAULT:AIR",           // maps to AirBlockId (-1)
            "DEFAULT:STEEL_HULL",    // maps to 11
            "Default:Cockpit",       // maps to 23
            "dEfAuLt:ThRuStEr",      // maps to 28
            "DEFAULT:LASER_CANNON",  // maps to 30
            "DeFaUlT:rEaCtOr",       // maps to 20
            "DEFAULT:BLOCK_777"      // maps to 777
        };

        foreach (var name in entries)
        {
            var tag = new NbtCompound();
            tag.SetString("Name", name);
            paletteTag.Add(tag);
        }
        root.Add(paletteTag);

        root.SetIntArray("Blocks", [0, 1, 2, 3, 4, 5, 6]);
        root.SetByteArray("Orientations", new byte[7]);

        using var ms = new MemoryStream();
        NbtWriter.WriteRootToGzip(ms, root);
        ms.Position = 0;

        var ship = SpaceboxStructureBridge.Import(ms);

        // Check resolved block IDs
        Assert.Equal(0, ship.Cells[WorldSnapshot.CellIndex(ship.Size, 0, 0, 0)]); // Air index 0
        Assert.Equal(11, ship.Palette[ship.Cells[WorldSnapshot.CellIndex(ship.Size, 1, 0, 0)]]);
        Assert.Equal(23, ship.Palette[ship.Cells[WorldSnapshot.CellIndex(ship.Size, 2, 0, 0)]]);
        Assert.Equal(28, ship.Palette[ship.Cells[WorldSnapshot.CellIndex(ship.Size, 3, 0, 0)]]);
        Assert.Equal(30, ship.Palette[ship.Cells[WorldSnapshot.CellIndex(ship.Size, 4, 0, 0)]]);
        Assert.Equal(20, ship.Palette[ship.Cells[WorldSnapshot.CellIndex(ship.Size, 5, 0, 0)]]);
        Assert.Equal(777, ship.Palette[ship.Cells[WorldSnapshot.CellIndex(ship.Size, 6, 0, 0)]]);
    }

    [Fact]
    public void Palette_MixedKnownUnknownAndHeuristicBlockIds_HandledRobustly()
    {
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.SetString("Name", "MixedBlocks");
        root.Add(new NbtList("Size", NbtTagType.Int) { 8, 1, 1 });

        var paletteTag = new NbtList("Palette", NbtTagType.Compound);
        var entries = new[]
        {
            "default:air",                         // index 0: air
            "default:steel_hull",                  // index 1: known -> 11
            "unknown_mod:alien_hull_v2",           // index 2: unknown fallback -> 11
            "mod:hyper_cannon_ultra",              // index 3: keyword cannon -> 30
            "custom_pack:cockpit_deluxe",          // index 4: keyword cockpit -> 23
            "mod:quantum_engine_x",                // index 5: keyword engine -> 28
            "mod:plasma_reactor_mk1",              // index 6: keyword reactor -> 20
            "deep_space:block_888"                 // index 7: explicit id -> 888
        };

        foreach (var name in entries)
        {
            var tag = new NbtCompound();
            tag.SetString("Name", name);
            paletteTag.Add(tag);
        }
        root.Add(paletteTag);

        root.SetIntArray("Blocks", [0, 1, 2, 3, 4, 5, 6, 7]);
        root.SetByteArray("Orientations", new byte[8]);

        using var ms = new MemoryStream();
        NbtWriter.WriteRootToGzip(ms, root);
        ms.Position = 0;

        var ship = SpaceboxStructureBridge.Import(ms);

        Assert.Equal(0, ship.Cells[WorldSnapshot.CellIndex(ship.Size, 0, 0, 0)]);
        Assert.Equal(11, ship.Palette[ship.Cells[WorldSnapshot.CellIndex(ship.Size, 1, 0, 0)]]);
        Assert.Equal(11, ship.Palette[ship.Cells[WorldSnapshot.CellIndex(ship.Size, 2, 0, 0)]]); // Fallback 11
        Assert.Equal(30, ship.Palette[ship.Cells[WorldSnapshot.CellIndex(ship.Size, 3, 0, 0)]]); // Cannon heuristic 30
        Assert.Equal(23, ship.Palette[ship.Cells[WorldSnapshot.CellIndex(ship.Size, 4, 0, 0)]]); // Cockpit heuristic 23
        Assert.Equal(28, ship.Palette[ship.Cells[WorldSnapshot.CellIndex(ship.Size, 5, 0, 0)]]); // Engine heuristic 28
        Assert.Equal(20, ship.Palette[ship.Cells[WorldSnapshot.CellIndex(ship.Size, 6, 0, 0)]]); // Reactor heuristic 20
        Assert.Equal(888, ship.Palette[ship.Cells[WorldSnapshot.CellIndex(ship.Size, 7, 0, 0)]]); // Explicit ID 888
    }

    [Fact]
    public void Palette_MissingAirSentinelAtIndex0_NormalizedGracefully()
    {
        // Malformed SBS where palette[0] is a solid block (default:steel_hull) instead of air
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.SetString("Name", "NoAirAtZero");
        root.Add(new NbtList("Size", NbtTagType.Int) { 2, 1, 1 });

        var paletteTag = new NbtList("Palette", NbtTagType.Compound);
        var hull0 = new NbtCompound(); hull0.SetString("Name", "default:steel_hull"); paletteTag.Add(hull0); // Index 0 is solid hull!
        var cock1 = new NbtCompound(); cock1.SetString("Name", "default:cockpit"); paletteTag.Add(cock1);    // Index 1 is cockpit
        root.Add(paletteTag);

        root.SetIntArray("Blocks", [0, 1]); // cell 0 is hull0, cell 1 is cock1
        root.SetByteArray("Orientations", new byte[2]);

        using var ms = new MemoryStream();
        NbtWriter.WriteRootToGzip(ms, root);
        ms.Position = 0;

        var ship = SpaceboxStructureBridge.Import(ms);

        // Crucial OpenCorneroids invariant: ship.Palette[0] must always be AirBlockId (-1)
        Assert.Equal(WorldSnapshot.AirBlockId, ship.Palette[0]);

        // Cell 0 should map to steel hull (11), cell 1 should map to cockpit (23)
        ushort c0 = ship.Cells[WorldSnapshot.CellIndex(ship.Size, 0, 0, 0)];
        ushort c1 = ship.Cells[WorldSnapshot.CellIndex(ship.Size, 1, 0, 0)];
        Assert.True(c0 > 0);
        Assert.True(c1 > 0);
        Assert.Equal(11, ship.Palette[c0]);
        Assert.Equal(23, ship.Palette[c1]);

        // Untouched cells in the 16x16x16 grid must remain air (palette index 0)
        Assert.Equal(0, ship.Cells[WorldSnapshot.CellIndex(ship.Size, 2, 0, 0)]);

        // Export back to SBS and verify normalized palette has air at index 0
        using var exportMs = new MemoryStream();
        SpaceboxStructureBridge.Export(ship, exportMs);
        exportMs.Position = 0;

        var exportNbt = NbtReader.ReadRoot(exportMs);
        var expPal = exportNbt.GetList("Palette");
        Assert.NotNull(expPal);
        Assert.Equal("default:air", ((NbtCompound)expPal[0]).GetString("Name"));

        // Re-import and verify fidelity
        exportMs.Position = 0;
        var reimported = SpaceboxStructureBridge.Import(exportMs);
        Assert.Equal(11, reimported.Palette[reimported.Cells[WorldSnapshot.CellIndex(reimported.Size, 0, 0, 0)]]);
        Assert.Equal(23, reimported.Palette[reimported.Cells[WorldSnapshot.CellIndex(reimported.Size, 1, 0, 0)]]);
    }

    [Fact]
    public void Palette_WhitespaceOrEmptyBlockNames_MapSafelyToAir()
    {
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.SetString("Name", "WhitespacePalette");
        root.Add(new NbtList("Size", NbtTagType.Int) { 4, 1, 1 });

        var paletteTag = new NbtList("Palette", NbtTagType.Compound);
        var p0 = new NbtCompound(); p0.SetString("Name", ""); paletteTag.Add(p0);
        var p1 = new NbtCompound(); p1.SetString("Name", "   "); paletteTag.Add(p1);
        var p2 = new NbtCompound(); p2.SetString("Name", "\t\r\n"); paletteTag.Add(p2);
        var p3 = new NbtCompound(); p3.SetString("Name", "default:steel_hull"); paletteTag.Add(p3);
        root.Add(paletteTag);

        root.SetIntArray("Blocks", [0, 1, 2, 3]);
        root.SetByteArray("Orientations", new byte[4]);

        using var ms = new MemoryStream();
        NbtWriter.WriteRootToGzip(ms, root);
        ms.Position = 0;

        var ship = SpaceboxStructureBridge.Import(ms);

        // Cells 0, 1, 2 must all resolve to air (palette index 0)
        Assert.Equal(0, ship.Cells[WorldSnapshot.CellIndex(ship.Size, 0, 0, 0)]);
        Assert.Equal(0, ship.Cells[WorldSnapshot.CellIndex(ship.Size, 1, 0, 0)]);
        Assert.Equal(0, ship.Cells[WorldSnapshot.CellIndex(ship.Size, 2, 0, 0)]);

        // Cell 3 must be steel hull
        ushort c3 = ship.Cells[WorldSnapshot.CellIndex(ship.Size, 3, 0, 0)];
        Assert.True(c3 > 0);
        Assert.Equal(11, ship.Palette[c3]);
    }

    #endregion

    #region 3. Orientation Stress

    [Fact]
    public void Orientation_EverySingleOrientation0To23_PreservedThroughExportAndImport()
    {
        int count = 24;
        int size = 32;
        var cells = new ushort[size * size * size];
        var turned = new List<TurnedCell>();
        var palette = new List<int> { WorldSnapshot.AirBlockId, 11 }; // steel hull

        // Place 24 blocks in a row at (0..23, 0, 0)
        for (int i = 0; i < count; i++)
        {
            cells[WorldSnapshot.CellIndex(size, i, 0, 0)] = 1;
            if (i > 0)
            {
                // Orientation 0 is default/unturned, so 1..23 are explicitly turned
                turned.Add(new TurnedCell(new Int3(i, 0, 0), i));
            }
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

        // Export to SBS
        using var sbsStream = new MemoryStream();
        SpaceboxStructureBridge.Export(ship, sbsStream, "ChiralStressMaster");
        sbsStream.Position = 0;

        // Inspect raw NBT orientations tag
        var nbt = NbtReader.ReadRoot(sbsStream);
        var orientTag = nbt.GetByteArray("Orientations");
        Assert.NotNull(orientTag);
        Assert.Equal(24, orientTag.Length);

        // Verify orientation 0 is stored as 0 byte, and 1..23 match index
        for (int i = 0; i < count; i++)
        {
            Assert.Equal((byte)i, orientTag[i]);
        }

        // Re-import to ShipSnapshot
        sbsStream.Position = 0;
        var imported = SpaceboxStructureBridge.Import(sbsStream);

        // Verify 23 turned cells (orientation 0 is not stored in Turned list)
        Assert.Equal(23, imported.Turned.Count);

        for (int i = 1; i < count; i++)
        {
            var tc = Assert.Single(imported.Turned, t => t.Orientation == i);
            Assert.Equal(new Int3(i, 0, 0), tc.Cell);
        }

        // Second round-trip check
        using var sbsStream2 = new MemoryStream();
        SpaceboxStructureBridge.Export(imported, sbsStream2);
        sbsStream2.Position = 0;
        var imported2 = SpaceboxStructureBridge.Import(sbsStream2);

        AssertShipsEqual(imported, imported2);
    }

    [Fact]
    public void Orientation_LegacyQuarterTurnBytesAbove23_DecodedProperlyWithoutException()
    {
        // Quarter-turn encoded bytes: yaw = b & 3, pitch = (b >> 2) & 3, roll = (b >> 4) & 3
        // For instance, byte = 1 | (2 << 2) | (2 << 4) = 41 (> 23)
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.SetString("Name", "LegacyQuarterTurnShip");
        root.Add(new NbtList("Size", NbtTagType.Int) { 3, 1, 1 });

        var paletteTag = new NbtList("Palette", NbtTagType.Compound);
        var air = new NbtCompound(); air.SetString("Name", "default:air"); paletteTag.Add(air);
        var hull = new NbtCompound(); hull.SetString("Name", "default:steel_hull"); paletteTag.Add(hull);
        root.Add(paletteTag);

        root.SetIntArray("Blocks", [1, 1, 1]);
        root.SetByteArray("Orientations", [0, 41, 58]); // 0, legacy quarter-turn 41, legacy quarter-turn 58

        using var ms = new MemoryStream();
        NbtWriter.WriteRootToGzip(ms, root);
        ms.Position = 0;

        var ship = SpaceboxStructureBridge.Import(ms);
        Assert.NotNull(ship);

        // Orientation 0 at (0,0,0) is unturned; orientation bytes 41 and 58 must decode to valid 0..23 indices
        foreach (var tc in ship.Turned)
        {
            Assert.InRange(tc.Orientation, 1, 23);
        }
    }

    #endregion

    #region 4. Console Mapping Stress

    [Fact]
    public void ConsoleMapping_MultipleConsoles_OverlappingGroups_AndMixedCameras()
    {
        int size = 32;
        var cells = new ushort[size * size * size];
        var palette = new List<int>
        {
            WorldSnapshot.AirBlockId,
            11, // Steel Hull
            23, // Cockpit
            28, // Thruster
            30  // Cannon
        };

        // Console 1 at (2, 2, 2)
        cells[WorldSnapshot.CellIndex(size, 2, 2, 2)] = 2;
        // Console 2 at (6, 6, 6)
        cells[WorldSnapshot.CellIndex(size, 6, 6, 6)] = 2;
        // Console 3 at (10, 10, 10)
        cells[WorldSnapshot.CellIndex(size, 10, 10, 10)] = 2;

        // Targets
        cells[WorldSnapshot.CellIndex(size, 3, 2, 2)] = 3; // T1
        cells[WorldSnapshot.CellIndex(size, 4, 2, 2)] = 3; // T2
        cells[WorldSnapshot.CellIndex(size, 5, 2, 2)] = 4; // T3
        cells[WorldSnapshot.CellIndex(size, 7, 6, 6)] = 4; // T4

        var t1 = new Int3(3, 2, 2);
        var t2 = new Int3(4, 2, 2);
        var t3 = new Int3(5, 2, 2);
        var t4 = new Int3(7, 6, 6);

        var consoles = new List<SavedConsole>
        {
            // Console 1: Multiple groups, overlapping targets, with Camera
            new(
                Cell: new Int3(2, 2, 2),
                Groups:
                [
                    new SavedConsoleGroup(Index: 0, Key: "Space", Targets: [t1, t2]),
                    new SavedConsoleGroup(Index: 1, Key: "W", Targets: [t2, t3]),     // t2 overlaps
                    new SavedConsoleGroup(Index: 2, Key: "MouseLeft", Targets: [t1, t3]) // t1, t3 overlap
                ],
                Camera: new Int3(2, 3, 2)
            ),
            // Console 2: Shares targets with Console 1, empty group, NO Camera
            new(
                Cell: new Int3(6, 6, 6),
                Groups:
                [
                    new SavedConsoleGroup(Index: 0, Key: "Space", Targets: [t1, t4]),
                    new SavedConsoleGroup(Index: 5, Key: "R", Targets: []) // Empty targets
                ],
                Camera: null
            ),
            // Console 3: Single group, with Camera
            new(
                Cell: new Int3(10, 10, 10),
                Groups:
                [
                    new SavedConsoleGroup(Index: 3, Key: "F", Targets: [t4])
                ],
                Camera: new Int3(10, 11, 10)
            )
        };

        var ship = new ShipSnapshot(
            Position: Vector3.Zero,
            Rotation: Quaternion.Identity,
            LinearVelocity: Vector3.Zero,
            AngularVelocity: Vector3.Zero,
            Size: size,
            Palette: palette,
            Cells: cells)
        {
            Consoles = consoles
        };

        using var sbs = new MemoryStream();
        SpaceboxStructureBridge.Export(ship, sbs, "ConsoleTopologyMaster");
        sbs.Position = 0;

        // Verify NBT camera serialization
        var nbt = NbtReader.ReadRoot(sbs);
        var consolesList = nbt.GetList("Consoles");
        Assert.NotNull(consolesList);
        Assert.Equal(3, consolesList.Count);

        var c1Tag = (NbtCompound)consolesList[0];
        var c2Tag = (NbtCompound)consolesList[1];
        var c3Tag = (NbtCompound)consolesList[2];

        Assert.True(c1Tag.Contains("Camera"));
        Assert.False(c2Tag.Contains("Camera")); // Console 2 had null camera
        Assert.True(c3Tag.Contains("Camera"));

        // Import and verify fidelity
        sbs.Position = 0;
        var imported = SpaceboxStructureBridge.Import(sbs);

        Assert.Equal(3, imported.Consoles.Count);

        // Bounding box shift was (2, 2, 2)
        var shift = new Int3(2, 2, 2);

        var ic1 = imported.Consoles[0];
        Assert.Equal(new Int3(2, 2, 2) - shift, ic1.Cell);
        Assert.Equal(new Int3(2, 3, 2) - shift, ic1.Camera);
        Assert.Equal(3, ic1.Groups.Count);
        Assert.Equal(2, ic1.Groups[0].Targets.Count);
        Assert.Equal(t1 - shift, ic1.Groups[0].Targets[0]);
        Assert.Equal(t2 - shift, ic1.Groups[0].Targets[1]);
        Assert.Equal(2, ic1.Groups[1].Targets.Count);
        Assert.Equal(t2 - shift, ic1.Groups[1].Targets[0]);
        Assert.Equal(t3 - shift, ic1.Groups[1].Targets[1]);

        var ic2 = imported.Consoles[1];
        Assert.Equal(new Int3(6, 6, 6) - shift, ic2.Cell);
        Assert.Null(ic2.Camera);
        Assert.Equal(2, ic2.Groups.Count);
        Assert.Empty(ic2.Groups[1].Targets); // Empty targets list preserved

        var ic3 = imported.Consoles[2];
        Assert.Equal(new Int3(10, 10, 10) - shift, ic3.Cell);
        Assert.Equal(new Int3(10, 11, 10) - shift, ic3.Camera);

        // Re-export and verify round-trip invariance
        using var sbs2 = new MemoryStream();
        SpaceboxStructureBridge.Export(imported, sbs2);
        sbs2.Position = 0;
        var imported2 = SpaceboxStructureBridge.Import(sbs2);
        AssertShipsEqual(imported, imported2);
    }

    [Fact]
    public void ConsoleMapping_IntArrayTargets_ImportedCorrectly()
    {
        // SBS fixture where targets are NbtIntArray instead of NbtList of ints
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.SetString("Name", "IntArrayTargetShip");
        root.Add(new NbtList("Size", NbtTagType.Int) { 3, 3, 3 });

        var paletteTag = new NbtList("Palette", NbtTagType.Compound);
        var air = new NbtCompound(); air.SetString("Name", "default:air"); paletteTag.Add(air);
        var hull = new NbtCompound(); hull.SetString("Name", "default:steel_hull"); paletteTag.Add(hull);
        root.Add(paletteTag);

        root.SetIntArray("Blocks", new int[27]);
        root.SetByteArray("Orientations", new byte[27]);

        var consolesTag = new NbtList("Consoles", NbtTagType.Compound);
        var cTag = new NbtCompound();
        cTag.Add(new NbtList("Cell", NbtTagType.Int) { 1, 1, 1 });

        var groupsTag = new NbtList("Groups", NbtTagType.Compound);
        var gTag = new NbtCompound();
        gTag.SetInt("Index", 0);
        gTag.SetString("Key", "Space");

        var targetsTag = new NbtList("Targets", NbtTagType.IntArray);
        targetsTag.Add(new NbtIntArray([2, 1, 1])); // Target as TAG_Int_Array
        gTag.Add(targetsTag);
        groupsTag.Add(gTag);
        cTag.Add(groupsTag);
        consolesTag.Add(cTag);
        root.Add(consolesTag);

        using var ms = new MemoryStream();
        NbtWriter.WriteRootToGzip(ms, root);
        ms.Position = 0;

        var ship = SpaceboxStructureBridge.Import(ms);
        var console = Assert.Single(ship.Consoles);
        Assert.Equal(new Int3(1, 1, 1), console.Cell);
        Assert.Single(console.Groups);
        var target = Assert.Single(console.Groups[0].Targets);
        Assert.Equal(new Int3(2, 1, 1), target);
    }

    [Fact]
    public void ConsoleMapping_MalformedConsoleCompound_HandledGracefully()
    {
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.SetString("Name", "MalformedConsole");
        root.Add(new NbtList("Size", NbtTagType.Int) { 2, 2, 2 });

        var paletteTag = new NbtList("Palette", NbtTagType.Compound);
        var air = new NbtCompound(); air.SetString("Name", "default:air"); paletteTag.Add(air);
        root.Add(paletteTag);

        root.SetIntArray("Blocks", new int[8]);
        root.SetByteArray("Orientations", new byte[8]);

        var consolesTag = new NbtList("Consoles", NbtTagType.Compound);

        // Malformed compound missing "Cell" tag entirely
        var badTag1 = new NbtCompound();
        consolesTag.Add(badTag1);

        // Malformed compound with invalid Cell list count
        var badTag2 = new NbtCompound();
        badTag2.Add(new NbtList("Cell", NbtTagType.Int) { 1, 1 }); // only 2 ints
        consolesTag.Add(badTag2);

        // Valid compound
        var goodTag = new NbtCompound();
        goodTag.Add(new NbtList("Cell", NbtTagType.Int) { 0, 0, 0 });
        consolesTag.Add(goodTag);

        root.Add(consolesTag);

        using var ms = new MemoryStream();
        NbtWriter.WriteRootToGzip(ms, root);
        ms.Position = 0;

        var ship = SpaceboxStructureBridge.Import(ms);

        // Only the valid console should be parsed
        var console = Assert.Single(ship.Consoles);
        Assert.Equal(new Int3(0, 0, 0), console.Cell);
    }

    #endregion

    #region Helper Assertion Methods

    private static void AssertShipsEqual(ShipSnapshot expected, ShipSnapshot actual)
    {
        Assert.NotNull(expected);
        Assert.NotNull(actual);
        Assert.Equal(expected.Size, actual.Size);

        // Entry 0 must always be AirBlockId (-1)
        Assert.Equal(WorldSnapshot.AirBlockId, expected.Palette[0]);
        Assert.Equal(WorldSnapshot.AirBlockId, actual.Palette[0]);

        // Check that both palettes contain the exact same set of unique block IDs
        var expectedBlockSet = new HashSet<int>(expected.Palette);
        var actualBlockSet = new HashSet<int>(actual.Palette);
        Assert.Equal(expectedBlockSet, actualBlockSet);

        // Check cell-by-cell semantic block equality across the entire 3D volume
        Assert.Equal(expected.Cells.Length, actual.Cells.Length);
        for (int i = 0; i < expected.Cells.Length; i++)
        {
            ushort expPalIdx = expected.Cells[i];
            ushort actPalIdx = actual.Cells[i];
            int expBlockId = (expPalIdx < expected.Palette.Count) ? expected.Palette[expPalIdx] : WorldSnapshot.AirBlockId;
            int actBlockId = (actPalIdx < actual.Palette.Count) ? actual.Palette[actPalIdx] : WorldSnapshot.AirBlockId;

            if (expBlockId != actBlockId)
            {
                int x = i % expected.Size;
                int y = (i / expected.Size) % expected.Size;
                int z = i / (expected.Size * expected.Size);
                Assert.Fail($"Cell block mismatch at ({x}, {y}, {z}): expected blockId {expBlockId}, got {actBlockId}");
            }
        }

        // Check turned blocks
        Assert.Equal(expected.Turned.Count, actual.Turned.Count);
        foreach (var expTurn in expected.Turned)
        {
            var actTurn = Assert.Single(actual.Turned, t => t.Cell == expTurn.Cell);
            Assert.Equal(expTurn.Orientation, actTurn.Orientation);
        }

        // Check consoles
        Assert.Equal(expected.Consoles.Count, actual.Consoles.Count);
        for (int i = 0; i < expected.Consoles.Count; i++)
        {
            var expC = expected.Consoles[i];
            var actC = Assert.Single(actual.Consoles, c => c.Cell == expC.Cell);
            Assert.Equal(expC.Camera, actC.Camera);
            Assert.Equal(expC.Groups.Count, actC.Groups.Count);

            for (int g = 0; g < expC.Groups.Count; g++)
            {
                var expG = expC.Groups[g];
                var actG = Assert.Single(actC.Groups, grp => grp.Index == expG.Index);
                Assert.Equal(expG.Key, actG.Key);
                Assert.Equal(expG.Targets.Count, actG.Targets.Count);
                for (int t = 0; t < expG.Targets.Count; t++)
                {
                    Assert.Equal(expG.Targets[t], actG.Targets[t]);
                }
            }
        }
    }

    #endregion
}
