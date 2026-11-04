using System.Numerics;
using OpenCorneroids.Simulation.Shapes;

namespace OpenCorneroids.Simulation.Tests.Shapes;

public class Int3Tests
{
    [Fact]
    public void Arithmetic_and_conversion()
    {
        var a = new Int3(1, -2, 3);
        var b = new Int3(4, 5, -6);
        Assert.Equal(new Int3(5, 3, -3), a + b);
        Assert.Equal(new Int3(-3, -7, 9), a - b);
        Assert.Equal(new Int3(2, -4, 6), a * 2);
        Assert.Equal(new Int3(2, -4, 6), 2 * a);
        Assert.Equal(new Int3(-1, 2, -3), -a);
        Assert.Equal(Int3.Zero, a * 0);
        Assert.Equal(new Int3(1, 1, 1), Int3.One);
        Assert.Equal(new Vector3(1, -2, 3), a.ToVector3());
        Vector3 v = a;
        Assert.Equal(new Vector3(1, -2, 3), v);
        Assert.Equal(new Int3(-1, 0, 2), Int3.Floor(new Vector3(-0.5f, 0.99f, 2)));
        Assert.Equal(Int3.UnitZ, Int3.Cross(Int3.UnitX, Int3.UnitY));
        Assert.Equal(-2, a[1]);
        Assert.Equal(24, new Int3(2, 3, 4).Volume);
    }
}
