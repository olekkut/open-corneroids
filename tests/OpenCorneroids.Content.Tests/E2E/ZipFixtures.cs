using System.IO.Compression;
using System.Text;
using OpenCorneroids.Content.Tests.Support;

namespace OpenCorneroids.Content.Tests.E2E;

/// <summary>
/// Synthetic archive builders for .zip mod and content pack testing.
/// Creates temporary .zip archives using TempTree isolation.
/// </summary>
internal static class ZipFixtures
{
    public static string CreateNestedContentZip(TempTree tree, string zipRelativePath, string packFolder = "MyMod")
    {
        var zipPath = tree.At(zipRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        // Add defaultItemset.xml under Content/Blocksets/
        var itemsetEntry = archive.CreateEntry($"{packFolder}/Content/Blocksets/defaultItemset.xml");
        using (var writer = new StreamWriter(itemsetEntry.Open(), Encoding.UTF8))
        {
            writer.Write(MiniItemset.Xml());
        }

        // Add a dummy texture
        var textureEntry = archive.CreateEntry($"{packFolder}/Content/Textures/Blocks/blockTextures.png");
        using (var stream = textureEntry.Open())
        {
            var png = TestPng.Encode(16, 16, (_, _) => (255, 0, 0, 255));
            stream.Write(png, 0, png.Length);
        }

        return zipPath;
    }

    public static string CreateFlatContentZip(TempTree tree, string zipRelativePath)
    {
        var zipPath = tree.At(zipRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        // Add defaultItemset.xml directly under Blocksets/
        var itemsetEntry = archive.CreateEntry("Blocksets/defaultItemset.xml");
        using (var writer = new StreamWriter(itemsetEntry.Open(), Encoding.UTF8))
        {
            writer.Write(MiniItemset.Xml());
        }

        return zipPath;
    }

    public static string CreateLooseTexturePackZip(TempTree tree, string zipRelativePath)
    {
        var zipPath = tree.At(zipRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        // Loose texture files at archive root
        var blockEntry = archive.CreateEntry("blockTextures.png");
        using (var stream = blockEntry.Open())
        {
            var png = TestPng.Encode(16, 16, (_, _) => (0, 255, 0, 255));
            stream.Write(png, 0, png.Length);
        }

        return zipPath;
    }

    public static string CreateCorruptedZip(TempTree tree, string zipRelativePath)
    {
        var zipPath = tree.At(zipRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        // Write invalid header bytes
        File.WriteAllBytes(zipPath, new byte[] { 0x50, 0x4B, 0x00, 0x00, 0xDE, 0xAD, 0xBE, 0xEF });
        return zipPath;
    }

    public static string CreateDeployExtensionZip(TempTree tree, string zipRelativePath)
    {
        var zipPath = tree.At(zipRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        // ClickOnce .deploy file
        var itemsetEntry = archive.CreateEntry("Content/Blocksets/defaultItemset.xml.deploy");
        using (var writer = new StreamWriter(itemsetEntry.Open(), Encoding.UTF8))
        {
            writer.Write(MiniItemset.Xml());
        }

        return zipPath;
    }
}
