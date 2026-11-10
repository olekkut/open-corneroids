using System.Numerics;
using OpenCorneroids.Simulation.Ships;

namespace OpenCorneroids.Simulation.Tests.Ships;

public class ThrustAllocatorWorkspaceTests
{
    static ThrusterMount[] RandomShip(Random rng, int count)
    {
        var mounts = new ThrusterMount[count];
        Vector3[] axes = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
        for (int i = 0; i < count; i++)
            mounts[i] = new ThrusterMount(
                new Vector3(rng.Next(-12, 13), rng.Next(-6, 7), rng.Next(-20, 21)) + new Vector3(0.5f),
                axes[rng.Next(axes.Length)], rng.Next(1, 5) * 250f);
        return mounts;
    }

    static Vector3 RandomVector(Random rng, float scale) =>
        new Vector3((float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1) * scale;

    [Fact]
    public void A_reused_workspace_gives_exactly_the_throttles_of_a_fresh_one()
    {
        var rng = new Random(424242);
        var workspace = new ThrustAllocator.Workspace();
        // Ships of different sizes in turn, so the workspace grows and is then used with fewer thrusters than it holds.
        int[] sizes = [3, 40, 7, 200, 1, 64, 12, 200, 5];
        foreach (int n in sizes)
        {
            var ship = RandomShip(rng, n);
            for (int solve = 0; solve < 6; solve++)
            {
                var com = RandomVector(rng, 3);
                var force = RandomVector(rng, rng.Next(2) == 0 ? 200 : 5000);
                var torque = RandomVector(rng, rng.Next(2) == 0 ? 0 : 3000);
                float availability = solve == 5 ? 0.5f : 1;

                var fresh = ThrustAllocator.Allocate(ship, com, force, torque, availability);
                var reused = new float[n];
                ThrustAllocator.Allocate(ship, com, force, torque, availability, reused, workspace);

                for (int i = 0; i < n; i++)
                    Assert.True(BitConverter.SingleToInt32Bits(fresh[i]) == BitConverter.SingleToInt32Bits(reused[i]),
                        $"ship of {n}, solve {solve}, thruster {i}: {fresh[i]} vs {reused[i]}");
            }
        }
    }

    [Fact]
    public void The_workspace_overload_checks_its_arguments()
    {
        var throttles = new float[1];
        ThrusterMount[] one = [new(Vector3.Zero, Vector3.UnitX, 100)];
        Assert.Throws<ArgumentNullException>(() =>
            ThrustAllocator.Allocate(one, Vector3.Zero, Vector3.UnitX, Vector3.Zero, 1, throttles, null!));
        Assert.Throws<ArgumentException>(() =>
            ThrustAllocator.Allocate(one, Vector3.Zero, Vector3.UnitX, Vector3.Zero, 1, Span<float>.Empty, new ThrustAllocator.Workspace()));
    }
}
