using OpenCorneroids.Content.Tests.Byoa;
using OpenCorneroids.Content.Xnb;

namespace OpenCorneroids.Content.Tests.Xnb;

public class XnbTests
{
    const string Vanilla = "Corneroids-Alpha-1.2.0";

    static string ContentDir => Path.Combine(ByoaRoot.Root!, Vanilla, "Corneroids", "Content");

    static XnbFile Open(string relative) =>
        XnbFile.Read(File.ReadAllBytes(Path.Combine(ContentDir, relative.Replace('\\', Path.DirectorySeparatorChar))));

    [ByoaFact(Vanilla)]
    public void Every_bundled_xnb_decompresses_to_its_stated_size()
    {
        int count = 0;
        foreach (var file in Directory.EnumerateFiles(ContentDir, "*.xnb", SearchOption.AllDirectories))
        {
            var bytes = File.ReadAllBytes(file);
            var xnb = XnbFile.Read(bytes);   // throws on a size mismatch
            Assert.NotEmpty(xnb.TypeReaders);
            count++;
        }
        Assert.True(count >= 60, $"only {count} xnb files");
    }

    [ByoaTheory(Vanilla)]
    [InlineData(@"Models\needle.xnb", 13, 16)]
    [InlineData(@"Models\egg.xnb", 74, 120)]
    [InlineData(@"Models\mineralParasite.xnb", 194, 236)]
    [InlineData(@"Models\scavenger.xnb", 268, 404)]
    [InlineData(@"Models\astronaut_final.xnb", 178, 132)]
    public void Creature_models_read_with_their_triangles(string path, int vertices, int triangles)
    {
        var model = XnbModel.Read(Open(path));
        Assert.Equal(vertices, model.Parts.Sum(p => p.Positions.Length));
        Assert.Equal(triangles, model.Parts.Sum(p => p.Indices.Length) / 3);
        foreach (var part in model.Parts)
        {
            Assert.All(part.Indices, i => Assert.InRange(i, 0, part.Positions.Length - 1));
            Assert.All(part.Normals, n => Assert.InRange(n.Length(), 0.9f, 1.1f));
            Assert.All(part.Uvs, uv => Assert.True(uv.X is >= -0.01f and <= 1.01f && uv.Y is >= -0.01f and <= 1.01f));
        }
        var (min, max) = model.Bounds;
        Assert.InRange((max - min).Length(), 0.3f, 6f);   // creature-sized, in blocks
    }

    [ByoaTheory(Vanilla)]
    [InlineData(@"Textures\mineralParasite.xnb", 64, 64)]
    [InlineData(@"Textures\needle.xnb", 16, 16)]
    [InlineData(@"Textures\swarmEgg.xnb", 32, 32)]
    [InlineData(@"Textures\scavenger.xnb", 64, 64)]
    [InlineData(@"Textures\Sprites\damageIndicator.xnb", 8, 32)]
    public void Creature_textures_read_as_rgba(string path, int width, int height)
    {
        var texture = XnbTexture.Read(Open(path));
        Assert.Equal((width, height), (texture.Width, texture.Height));
        Assert.Equal(width * height * 4, texture.Rgba.Length);
        Assert.Contains(texture.Rgba.Where((_, i) => i % 4 == 3), a => a > 0);   // not all transparent
    }

    [Fact]
    public void Damaged_files_are_refused_cleanly()
    {
        Assert.Throws<InvalidDataException>(() => XnbFile.Read([1, 2, 3]));
        Assert.Throws<InvalidDataException>(() => XnbFile.Read("XNBw\u0004\u0000\u0010\u0000\u0000\u0000"u8.ToArray()));
        // A compressed file whose frames are garbage.
        var garbage = new byte[64];
        "XNBw"u8.CopyTo(garbage);
        garbage[4] = 4;
        garbage[5] = 0x80;
        BitConverter.GetBytes(64).CopyTo(garbage, 6);
        BitConverter.GetBytes(1000).CopyTo(garbage, 10);
        for (int i = 14; i < 64; i++) garbage[i] = (byte)(i * 37);
        garbage[14] = 0;
        garbage[15] = 40;
        Assert.ThrowsAny<Exception>(() => XnbFile.Read(garbage));
    }

    [Fact]
    public void An_uncompressed_xnb_is_read_as_is()
    {
        // One type reader ("R", version 0), no shared resources, a null primary object.
        byte[] content = [1, 1, (byte)'R', 0, 0, 0, 0, 0, 0];
        var file = new byte[10 + content.Length];
        "XNBw"u8.CopyTo(file);
        file[4] = 5;
        BitConverter.GetBytes(file.Length).CopyTo(file, 6);
        content.CopyTo(file, 10);
        var xnb = XnbFile.Read(file);
        Assert.Equal(["R"], xnb.TypeReaders);
        Assert.Null(xnb.PrimaryReader);
    }
}
