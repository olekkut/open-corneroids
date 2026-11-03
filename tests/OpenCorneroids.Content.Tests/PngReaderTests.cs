using OpenCorneroids.Content.Imaging;
using OpenCorneroids.Content.Tests.Support;

namespace OpenCorneroids.Content.Tests;

public class PngReaderTests
{
    [Fact]
    public void Decodes_what_was_encoded()
    {
        var png = TestPng.Encode(7, 5, (x, y) => ((byte)(x * 30), (byte)(y * 40), (byte)(x + y), (byte)(x * y * 9)));

        var image = PngReader.Read(png);

        Assert.Equal((7, 5), (image.Width, image.Height));
        for (int y = 0; y < 5; y++)
        for (int x = 0; x < 7; x++)
        {
            int i = (y * 7 + x) * 4;
            Assert.Equal(((byte)(x * 30), (byte)(y * 40), (byte)(x + y), (byte)(x * y * 9)),
                (image.Pixels[i], image.Pixels[i + 1], image.Pixels[i + 2], image.Pixels[i + 3]));
        }
        Assert.Equal((7, 5), PngReader.ReadSize(new MemoryStream(png)));
    }

    [Fact]
    public void Refuses_files_that_are_not_PNGs() =>
        Assert.Throws<InvalidDataException>(() => PngReader.Read("GIF89a not a png"u8.ToArray()));
}
