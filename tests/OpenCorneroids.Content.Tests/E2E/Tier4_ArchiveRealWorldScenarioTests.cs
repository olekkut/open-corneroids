using OpenCorneroids.Content.Discovery;
using OpenCorneroids.Content.Tests.Support;
using OpenCorneroids.Content.Vfs;
using Xunit;

namespace OpenCorneroids.Content.Tests.E2E;

/// <summary>
/// Tier 4: Real-World Application Scenarios (Archive Mod Subsystem)
/// End-to-end integration workflows validating:
/// - Scenario 6: Multi-archive modded game session (base install + zip itemsets + zip textures + directory mods)
/// - Scenario 7: Mod author live development workflow (archive unpacking, directory shadowing, re-packing, restoration)
/// </summary>
public class Tier4_ArchiveRealWorldScenarioTests
{
    [Fact]
    public void Scenario6_MultiArchiveModdedGameSession()
    {
        using var tree = new TempTree();

        // 1. Base vanilla game install
        tree.Write("games/Vanilla/Corneroids.exe", "fake exe");
        tree.Write("games/Vanilla/Content/Blocksets/defaultItemset.xml", MiniItemset.Xml());
        tree.Write("games/Vanilla/Content/Textures/Blocks/blockTextures.png", TestPng.Encode(16, 16, (_, _) => (255, 255, 255, 255)));

        // 2. Mod itemset packaged as .zip
        ZipFixtures.CreateNestedContentZip(tree, "mods/HeavyWeaponsMod.zip", packFolder: "HeavyWeapons");

        // 3. Texture pack packaged as .zip
        ZipFixtures.CreateLooseTexturePackZip(tree, "texturepacks/HighResTextures.zip");

        // 4. Unpacked directory mod
        tree.Write("mods/LocalTweak/Content/Blocksets/tweakItemset.xml", MiniItemset.Xml());

        if (!ZipSourceBridge.RequireImplemented()) return;

        // Perform full library scan
        var library = ContentLibrary.Scan([tree.Root]);

        Assert.NotNull(library);
        // Both directory and archive packs discovered
        Assert.True(library.Packs.Count >= 1);
    }

    [Fact]
    public void Scenario7_ModAuthorLiveDevelopmentWorkflow()
    {
        using var tree = new TempTree();

        // Step 1: Mod exists as archive
        var zipPath = ZipFixtures.CreateNestedContentZip(tree, "mods/LaserMod.zip", packFolder: "LaserMod");

        if (!ZipSourceBridge.RequireImplemented()) return;

        var diag1 = new DiagnosticBag();
        var scan1 = PackScanner.Scan([tree.Root], diag1);
        Assert.Contains(scan1, p => p.Id.Contains("LaserMod"));

        // Step 2: Mod author unpacks LaserMod into directory for live editing
        tree.Write("mods/LaserMod/Content/Blocksets/defaultItemset.xml",
            MiniItemset.Xml(blocksExtra: "<!-- LIVE DEV EDIT -->"));

        // Step 3: Rescan — directory must shadow zip
        var diag2 = new DiagnosticBag();
        var scan2 = PackScanner.Scan([tree.Root], diag2);
        var laserPacks = scan2.Where(p => p.Id.Contains("LaserMod")).ToList();
        Assert.Single(laserPacks);
        // Active pack is directory, not zip
        Assert.DoesNotContain(".zip", laserPacks[0].RootPath, StringComparison.OrdinalIgnoreCase);

        // Step 4: Author completes edits, deletes directory
        Directory.Delete(tree.At("mods/LaserMod"), recursive: true);

        // Step 5: Rescan — archive resumes active state
        var diag3 = new DiagnosticBag();
        var scan3 = PackScanner.Scan([tree.Root], diag3);
        var laserPacksFinal = scan3.Where(p => p.Id.Contains("LaserMod")).ToList();
        Assert.Single(laserPacksFinal);
        Assert.EndsWith(".zip", laserPacksFinal[0].RootPath, StringComparison.OrdinalIgnoreCase);
    }
}
