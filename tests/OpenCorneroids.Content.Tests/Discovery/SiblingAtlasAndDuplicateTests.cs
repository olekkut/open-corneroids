using OpenCorneroids.Content.Discovery;
using OpenCorneroids.Content.Tests.Support;
using Xunit;

namespace OpenCorneroids.Content.Tests.Discovery;

public class SiblingAtlasAndDuplicateTests
{
    [Fact]
    public void Sibling_atlas_files_and_duplicate_ids_are_tolerantly_loaded()
    {
        using var tree = new TempTree();
        var opaque = TestPng.Atlas(4, 4, 8, (_, _) => 255);

        // Game install
        tree.Write("games/Game/Corneroids/Corneroids.exe", "MZ");
        tree.Write("games/Game/Corneroids/Content/Blocksets/defaultItemset.xml",
            MiniItemset.Xml().Replace("miniblocks", "defaultblocks").Replace("minisprites", "defaultsprites"));
        tree.Write("games/Game/Corneroids/Content/Textures/Blocks/DefaultBlocks.xml", """
            <blockTextureAtlases>
              <atlas><name>Default</name><key>defaultblocks</key><texture>Textures/Blocks/blockTextures.png</texture><blockSize>8</blockSize></atlas>
            </blockTextureAtlases>
            """);
        tree.Write("games/Game/Corneroids/Content/Textures/Blocks/blockTextures.png", opaque);
        tree.Write("games/Game/Corneroids/Content/Textures/Sprites/SpriteTextureAtlases.xml", """
            <spriteTextureAtlases>
              <atlas><name>Default</name><key>defaultsprites</key><texture>Textures/Sprites/spriteImages.png</texture><blockSize>8</blockSize></atlas>
            </spriteTextureAtlases>
            """);
        tree.Write("games/Game/Corneroids/Content/Textures/Sprites/spriteImages.png", opaque);

        // Community mod with duplicate block ID and sibling atlas XML file
        var duplicateAsteroid = $"""<asteroid itemId="99" blockId="0">{MiniItemset.Common("Duplicate Asteroid")}{MiniItemset.BlockFields("0 0 1 1")}<asteroidType>light</asteroidType></asteroid>""";
        var modXml = MiniItemset.Xml(blocksExtra: $"<asteroids>{duplicateAsteroid}</asteroids>")
            .Replace("<key>MiniItemset</key>", "<key>CustomItemset</key>")
            .Replace("<name>Mini</name>", "<name>Custom Mod</name>")
            .Replace("miniblocks", "CustomBlocksKey")
            .Replace("minisprites", "defaultsprites");

        tree.Write("mods/CustomMod/Content/Blocksets/customItemset.xml", modXml);

        // Sibling XML file in Textures/Blocks/
        tree.Write("mods/CustomMod/Content/Textures/Blocks/CustomBlocksKey.xml", """
            <blockTextureAtlases>
              <atlas>
                <name>Custom</name>
                <key>CustomBlocksKey</key>
                <texture>Textures/Blocks/custom.png</texture>
                <blockSize>8</blockSize>
              </atlas>
            </blockTextureAtlases>
            """);
        tree.Write("mods/CustomMod/Content/Textures/Blocks/custom.png", opaque);

        var library = ContentLibrary.Scan([tree.Root], baseInstallId: "Game");
        var entry = library.FindItemset("CustomItemset", out var problem);
        Assert.Null(problem);
        Assert.NotNull(entry);

        var loaded = library.Load(entry!);
        Assert.NotNull(loaded.Itemset);
        Assert.NotNull(loaded.BlockAtlas);
        // Duplicate blockId was reported as warning, not error
        Assert.False(loaded.Diagnostics.HasErrors);
        Assert.Contains(loaded.Diagnostics, d => d.Code == Codes.DuplicateBlockId && d.Severity == Severity.Warning);
    }
}
