using System.IO;
using OpenCorneroids.Simulation.Persistence;
using OpenCorneroids.Simulation.Persistence.Nbt;
using OpenCorneroids.Simulation.Shapes;
using Xunit;

namespace OpenCorneroids.Simulation.Tests.Persistence;

public class SpaceboxBlockMappingTests
{
    [Theory]
    // Ores and Asteroid Rocks
    [InlineData("cobaltite_rock", 33)]
    [InlineData("default:cobaltite_rock", 33)]
    [InlineData("ice_rock", 32)]
    [InlineData("default:dense_ice_rock", 32)]
    [InlineData("heavy_rock", 2)]
    [InlineData("medium_rock", 1)]
    [InlineData("light_rock", 0)]
    [InlineData("uranium_ore", 3)]
    [InlineData("iron_ore", 4)]
    [InlineData("aluminium_ore", 5)]
    [InlineData("titanium_ore", 16)]
    [InlineData("thulium_ore", 17)]
    [InlineData("obscurium_ore", 18)]
    // Power & Cables
    [InlineData("cable_block", 19)]
    [InlineData("default:cable_block", 19)]
    [InlineData("wire_angle", 19)]
    [InlineData("wire_hub", 19)]
    [InlineData("wire_top", 19)]
    [InlineData("cobalt_power_block", 20)]
    [InlineData("powerful_cobalt_power_block", 21)]
    [InlineData("standard_nuclear_power_block", 39)]
    [InlineData("nuclear_power_block", 40)]
    // Doors & Storage
    [InlineData("iron_door", 28)]
    [InlineData("default:aluminium_door", 29)]
    [InlineData("titanium_door", 31)]
    [InlineData("basic_aluminium_crate", 46)]
    [InlineData("basic_iron_crate", 46)]
    [InlineData("safe", 46)]
    // Windows
    [InlineData("window", 47)]
    [InlineData("reinforced_window", 47)]
    [InlineData("big_window", 48)]
    // Machines
    [InlineData("crafting_table", 34)]
    [InlineData("crusher", 35)]
    [InlineData("enhanced_crusher", 35)]
    [InlineData("furnace", 36)]
    [InlineData("enhanced_furnace", 36)]
    // Thrusters
    [InlineData("thruster_weak", 22)]
    [InlineData("thruster_standard", 37)]
    [InlineData("thruster_powerful", 38)]
    [InlineData("thruster_ion", 41)]
    // Cannons & Guns
    [InlineData("cannon_antipersonnel", 24)]
    [InlineData("cannon_light_assault", 42)]
    [InlineData("cannon_anticorvette", 43)]
    [InlineData("cannon_antifrigate", 44)]
    [InlineData("cannon_heavy_assault", 45)]
    // Lights
    [InlineData("light_red", 25)]
    [InlineData("light_blue", 25)]
    [InlineData("light_green", 25)]
    public void SpaceboxBlockNames_MapToCorrectCorneroidsBlockIds(string blockName, int expectedBlockId)
    {
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.Add(new NbtList("Size", NbtTagType.Int) { 1, 1, 1 });

        var palette = new NbtList("Palette", NbtTagType.Compound);
        var airEntry = new NbtCompound();
        airEntry.SetString("Name", "default:air");
        palette.Add(airEntry);

        var blockEntry = new NbtCompound();
        blockEntry.SetString("Name", blockName);
        palette.Add(blockEntry);

        root.Add(palette);
        root.SetIntArray("Blocks", [1]);
        root.SetByteArray("Orientations", [0]);

        using var ms = new MemoryStream();
        NbtWriter.WriteRootToGzip(ms, root);
        ms.Position = 0;

        var snapshot = SpaceboxStructureBridge.Import(ms);
        Assert.NotNull(snapshot);
        ushort cell = snapshot.Cells[WorldSnapshot.CellIndex(snapshot.Size, 0, 0, 0)];
        int resolvedId = snapshot.Palette[cell];
        Assert.Equal(expectedBlockId, resolvedId);
    }

    [Fact]
    public void RealSpaceboxShip_ContainsExpectedBlockIdsInPalette()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Spacebox_v0.1.5.1_windows", "Structures", "ship.sbs");
        if (!File.Exists(path)) return;

        using var fs = File.OpenRead(path);
        var snapshot = SpaceboxStructureBridge.Import(fs, "RealShip");

        // Ship contains cable_block (19), big_window (48), aluminium_door (29), cobalt_power_block (20), crafting_table (34), furnace (36), etc.
        Assert.Contains(19, snapshot.Palette);
        Assert.Contains(48, snapshot.Palette);
        Assert.Contains(29, snapshot.Palette);
        Assert.Contains(20, snapshot.Palette);
        Assert.Contains(34, snapshot.Palette);
        Assert.Contains(36, snapshot.Palette);
        Assert.Contains(23, snapshot.Palette); // cockpit / console
        Assert.Contains(22, snapshot.Palette); // thruster_weak
    }

    [Fact]
    public void RealSpaceboxAsteroid_ContainsAccurateOresAndRocksInPalette()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Spacebox_v0.1.5.1_windows", "Structures", "asteroid.sbs");
        if (!File.Exists(path)) return;

        using var fs = File.OpenRead(path);
        var snapshot = SpaceboxStructureBridge.Import(fs, "RealAsteroid");

        // Asteroid contains cobaltite_rock (33), ice_rock (32), heavy_rock (2), uranium_ore (3), iron_ore (4), aluminium_ore (5), titanium_ore (16), thulium_ore (17)
        Assert.Contains(33, snapshot.Palette);
        Assert.Contains(32, snapshot.Palette);
        Assert.Contains(2, snapshot.Palette);
        Assert.Contains(3, snapshot.Palette);
        Assert.Contains(4, snapshot.Palette);
        Assert.Contains(5, snapshot.Palette);
        Assert.Contains(16, snapshot.Palette);
        Assert.Contains(17, snapshot.Palette);
    }
}
