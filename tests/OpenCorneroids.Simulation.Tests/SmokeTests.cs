namespace OpenCorneroids.Simulation.Tests;

public class SmokeTests
{
    [Fact]
    public void Library_loads() => Assert.Equal("OpenCorneroids.Simulation", typeof(SmokeTests).Assembly.GetName().Name!.Replace(".Tests", ""));
}
