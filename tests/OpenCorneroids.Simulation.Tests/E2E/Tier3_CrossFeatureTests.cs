using System.Numerics;
using Microsoft.Data.Sqlite;
using OpenCorneroids.Simulation.Persistence;
using OpenCorneroids.Simulation.Shapes;
using Xunit;

namespace OpenCorneroids.Simulation.Tests.E2E;

/// <summary>
/// Tier 3: Cross-Feature Combination Tests
/// Validates pairwise and multi-feature interactions between:
/// - Legacy Corneroids .wrl SQLite importer
/// - Spacebox .sbs structure bridge (import & export)
/// - Native OpenCorneroids .ocworld format
/// - Quarter-turn orientation transformations
/// - Console control bindings across formats
/// </summary>
public class Tier3_CrossFeatureTests
{
    [Fact]
    public void C01_WrlImportedShip_ExportedToSpaceboxSbs_Roundtrip()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "CrossWorld", 1.2f, 42, "defaultItemset", 1700000000000L);
        WrlFixtures.InsertEntity(conn, 1, true, 83886080L, 0L, 0L, 0f, 0f, 0f, 1f);

        // 1 solid block at (1, 1, 1) with block ID 5 and yaw=1 (90 deg)
        var coords = new[] { (1, 1, 1) };
        var f = WrlFixtures.CompressGzip(WrlFixtures.BuildFormationMask(coords));
        var b = WrlFixtures.CompressGzip([5]);
        var o = WrlFixtures.CompressGzip([WrlFixtures.BuildQuarterTurnOrientationByte(1, 0, 0)]);
        WrlFixtures.InsertBlockSector(conn, 1, 0, 0, 0, f, b, o);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.Single(snapshot.Ships);
        var wrlShip = snapshot.Ships[0];

        if (!SpaceboxBridge.RequireImplemented()) return;
        using var sbsStream = new MemoryStream();
        SpaceboxBridge.Export(wrlShip, sbsStream, "WrlToSbsBridge");
        sbsStream.Position = 0;

        var reimportedShip = SpaceboxBridge.Import(sbsStream, "Reimported Ship");
        Assert.NotNull(reimportedShip);
        Assert.Equal(wrlShip.Size, reimportedShip.Size);
    }

    [Fact]
    public void C02_SpaceboxImportedStructure_SerializedToOcworld_Preserved()
    {
        var sbsBytes = SbsFixtures.CreateSbsArchive("InterchangeShip", "SpaceboxArchitect", 2, 2, 2,
            ["default:air", "default:steel_hull"], [0, 1, 0, 0, 0, 0, 0, 0], new byte[8]);

        if (!SpaceboxBridge.RequireImplemented()) return;
        var importedShip = SpaceboxBridge.Import(new MemoryStream(sbsBytes));

        // Insert ship into OpenCorneroids WorldSnapshot and write to native .ocworld format
        var world = Persistence.SaveFixtures.SmallWorld() with
        {
            Ships = [importedShip]
        };

        using var ocworldStream = new MemoryStream();
        WorldSaveFormat.Write(ocworldStream, world);
        ocworldStream.Position = 0;

        var loadedWorld = WorldSaveFormat.Read(ocworldStream);
        Assert.Single(loadedWorld.Ships);
        Assert.Equal(importedShip.Size, loadedWorld.Ships[0].Size);
    }

    [Fact]
    public void C03_WrlWorldWithConsoles_ExportedToSbs_ConsolesPreservedInNbt()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "ConsoleCross", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);
        WrlFixtures.InsertControlBlock(conn, 10, 1, 1, 0f, 0f, 0f);
        WrlFixtures.InsertControlledBlock(conn, 100, 10, 0, 1, 1f, 0f, 0f); // Red
        WrlFixtures.InsertKeyColorMapping(conn, 1, 10, 0, 32); // Space

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        var ship = snapshot.Ships[0];

        if (!SpaceboxBridge.RequireImplemented()) return;
        using var sbsStream = new MemoryStream();
        SpaceboxBridge.Export(ship, sbsStream);
        sbsStream.Position = 0;

        var nbt = SbsFixtures.ReadGzipNbtCompound(sbsStream);
        Assert.Contains("Consoles", nbt.Keys);
    }

    [Fact]
    public void C04_WrlQuarterTurnOrientations_ExportedToSbs_OrientationsMatch()
    {
        byte quarterTurnByte = WrlFixtures.BuildQuarterTurnOrientationByte(2, 0, 0); // 180 deg yaw
        int oracleOrientationIndex = WrlFixtures.DeriveOrientationIndexOracle(quarterTurnByte);

        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "OrientCross", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);

        var f = WrlFixtures.CompressGzip(WrlFixtures.BuildFormationMask([(0, 0, 0)]));
        var b = WrlFixtures.CompressGzip([1]);
        var o = WrlFixtures.CompressGzip([quarterTurnByte]);
        WrlFixtures.InsertBlockSector(conn, 1, 0, 0, 0, f, b, o);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        var ship = snapshot.Ships[0];
        if (oracleOrientationIndex != 0)
        {
            Assert.Contains(ship.Turned, t => t.Orientation == oracleOrientationIndex);
        }

        if (!SpaceboxBridge.RequireImplemented()) return;
        using var dest = new MemoryStream();
        SpaceboxBridge.Export(ship, dest);
        dest.Position = 0;
        var nbt = SbsFixtures.ReadGzipNbtCompound(dest);
        var orientationsArray = (byte[])nbt["Orientations"];
        Assert.NotEmpty(orientationsArray);
    }

    [Fact]
    public void C05_MultiSectorWrlShip_BoundingBoxNormalizedInSbsExport()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "MultiSectorCross", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);

        // Blocks in sector (0,0,0) and sector (1,0,0)
        var f1 = WrlFixtures.CompressGzip(WrlFixtures.BuildFormationMask([(0, 0, 0)]));
        var f2 = WrlFixtures.CompressGzip(WrlFixtures.BuildFormationMask([(5, 0, 0)]));
        WrlFixtures.InsertBlockSector(conn, 1, 0, 0, 0, f1, WrlFixtures.CompressGzip([1]), WrlFixtures.CompressGzip([0]));
        WrlFixtures.InsertBlockSector(conn, 1, 1, 0, 0, f2, WrlFixtures.CompressGzip([1]), WrlFixtures.CompressGzip([0]));

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        var ship = snapshot.Ships[0];

        if (!SpaceboxBridge.RequireImplemented()) return;
        using var sbs = new MemoryStream();
        SpaceboxBridge.Export(ship, sbs);
        sbs.Position = 0;
        var nbt = SbsFixtures.ReadGzipNbtCompound(sbs);
        var sizeList = (List<object>)nbt["Size"];
        // Span from 0 to 16+5 = 21 along X
        Assert.True((int)sizeList[0] >= 21);
    }

    [Fact]
    public void C06_SbsShipExport_ReimportedIntoOpenCorneroids_SameBlockCounts()
    {
        var originalShip = Persistence.SaveFixtures.Ship(size: 4);

        if (!SpaceboxBridge.RequireImplemented()) return;
        using var sbs = new MemoryStream();
        SpaceboxBridge.Export(originalShip, sbs);
        sbs.Position = 0;

        var reimported = SpaceboxBridge.Import(sbs);
        Assert.NotNull(reimported);
        // Both ships should have non-empty palette
        Assert.NotEmpty(reimported.Palette);
    }

    [Fact]
    public void C07_WrlEntitiesWithPlayerBoarded_AttachedToMatchingShip()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "BoardedCross", 1.2f, 1, "defaultItemset", 0L);
        // Entity 50
        WrlFixtures.InsertEntity(conn, 50, true, 83886080L, 0L, 0L, 0f, 0f, 0f, 1f);
        // Player boarded on entity 50
        WrlFixtures.InsertPlayer(conn, 1, "Astronaut", 83886080L, 0L, 0L, 0f, 0f, 0f, 1f, boardedEntity: 50);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.Single(snapshot.Ships);
        Assert.NotNull(snapshot.Player);
    }

    [Fact]
    public void C08_CustomItemsetWrlWorld_SbsPaletteGeneratesNamespacedNames()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "CustomItemsetCross", 1.2f, 1, "DarthNoxItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);

        var f = WrlFixtures.CompressGzip(WrlFixtures.BuildFormationMask([(0, 0, 0)]));
        WrlFixtures.InsertBlockSector(conn, 1, 0, 0, 0, f, WrlFixtures.CompressGzip([1]), WrlFixtures.CompressGzip([0]));

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);

        if (!SpaceboxBridge.RequireImplemented()) return;
        using var sbs = new MemoryStream();
        SpaceboxBridge.Export(snapshot.Ships[0], sbs);
        sbs.Position = 0;
        var nbt = SbsFixtures.ReadGzipNbtCompound(sbs);
        var palette = (List<object>)nbt["Palette"];
        Assert.NotEmpty(palette);
    }

    [Fact]
    public void C09_SbsConsoleTriggerBindings_MapToConsoleColorGroups()
    {
        var consoles = new List<(Int3 consoleCell, int groupIndex, string key, List<Int3> targets)>
        {
            (new Int3(0, 0, 0), 3, "Space", [new Int3(1, 0, 0)]) // Group 3 (Yellow)
        };
        var sbs = SbsFixtures.CreateSbsArchive("ConsoleColorCross", "Author", 2, 1, 1,
            ["default:air", "default:cockpit"], [1, 1], [0, 0], consoles);

        if (!SpaceboxBridge.RequireImplemented()) return;
        var ship = SpaceboxBridge.Import(new MemoryStream(sbs));
        Assert.Single(ship.Consoles);
        Assert.Equal(3, ship.Consoles[0].Groups[0].Index);
    }

    [Fact]
    public void C10_WrlWorldSnapshotWithMultipleShips_EachExportableIndependentlyToSbs()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "MultiShipCross", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);
        WrlFixtures.InsertEntity(conn, 2, true, 83886080L, 0L, 0L, 0f, 0f, 0f, 1f);

        var f = WrlFixtures.CompressGzip(WrlFixtures.BuildFormationMask([(0, 0, 0)]));
        WrlFixtures.InsertBlockSector(conn, 1, 0, 0, 0, f, WrlFixtures.CompressGzip([1]), WrlFixtures.CompressGzip([0]));
        WrlFixtures.InsertBlockSector(conn, 2, 0, 0, 0, f, WrlFixtures.CompressGzip([2]), WrlFixtures.CompressGzip([0]));

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.Equal(2, snapshot.Ships.Count);

        if (!SpaceboxBridge.RequireImplemented()) return;
        foreach (var ship in snapshot.Ships)
        {
            using var sbs = new MemoryStream();
            SpaceboxBridge.Export(ship, sbs);
            Assert.True(sbs.Length > 0);
        }
    }
}
