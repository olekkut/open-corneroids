using OpenCorneroids.Simulation.Shapes;
using OpenCorneroids.Simulation.Ships;

namespace OpenCorneroids.Simulation.Tests.Ships;

public class PowerNetworkTests
{
    static PowerNode Node(int id, float supply, float demand, params Int3[] cells) => new(id, cells, supply, demand);
    static Int3 C(int x, int y = 0, int z = 0) => new(x, y, z);

    [Fact]
    public void Separate_networks_share_nothing()
    {
        var solution = PowerNetwork.Solve(
        [
            Node(1, 10, 0, C(0)), Node(2, 0, 5, C(1)),
            Node(3, 2, 0, C(10)), Node(4, 0, 8, C(11)),
        ]);
        Assert.Equal(2, solution.Networks.Count);
        Assert.Equal([1, 2], solution.Networks[0].NodeIds);
        Assert.Equal(10, solution.Networks[0].Supply);
        Assert.Equal(5, solution.Networks[0].Demand);
        Assert.Equal(1, solution.SatisfactionOf(2));
        Assert.Equal(0.25f, solution.SatisfactionOf(4));
        Assert.Equal(0.25f, solution.SatisfactionOf(3));
        Assert.Equal(1, solution.NetworkIndexOf(4));
    }

    [Fact]
    public void Cables_bridge_a_generator_to_a_consumer()
    {
        PowerNode[] ends = [Node(1, 10, 0, C(0)), Node(2, 0, 4, C(4))];
        var unbridged = PowerNetwork.Solve(ends);
        Assert.Equal(2, unbridged.Networks.Count);
        Assert.False(unbridged.IsPowered(2));

        var bridged = PowerNetwork.Solve([.. ends, Node(10, 0, 0, C(1)), Node(11, 0, 0, C(2)), Node(12, 0, 0, C(3))]);
        Assert.Single(bridged.Networks);
        Assert.Equal([1, 2, 10, 11, 12], bridged.Networks[0].NodeIds);
        Assert.Equal(1, bridged.SatisfactionOf(2));
        Assert.True(bridged.IsPowered(11));
    }

    [Fact]
    public void Multi_cell_blocks_conduct_through_every_cell()
    {
        // A 1×3×1 generator, a consumer touching only its top cell.
        var solution = PowerNetwork.Solve([Node(1, 6, 0, C(0), C(0, 1), C(0, 2)), Node(2, 0, 3, C(1, 2))]);
        Assert.Single(solution.Networks);
        Assert.Equal(1, solution.SatisfactionOf(2));
    }

    [Fact]
    public void Faces_connect_along_every_axis()
    {
        foreach (var neighbour in new[] { C(1), C(-1), C(0, 1), C(0, -1), C(0, 0, 1), C(0, 0, -1) })
        {
            var solution = PowerNetwork.Solve([Node(1, 6, 0, C(0)), Node(2, 0, 3, neighbour)]);
            Assert.Single(solution.Networks);
            Assert.Equal(1, solution.SatisfactionOf(2));
        }
    }

    [Fact]
    public void Diagonal_cells_do_not_connect()
    {
        var solution = PowerNetwork.Solve([Node(1, 6, 0, C(0)), Node(2, 0, 3, C(1, 1)), Node(3, 0, 3, C(0, 1, 1))]);
        Assert.Equal(3, solution.Networks.Count);
        Assert.Equal(0, solution.SatisfactionOf(2));
    }

    [Fact]
    public void Shortage_scales_every_consumer_by_the_same_fraction()
    {
        var solution = PowerNetwork.Solve(
        [
            Node(1, 10, 0, C(0)), Node(2, 0, 30, C(1)), Node(3, 0, 10, C(0, 1)), Node(4, 0, 0, C(0, -1)),
        ]);
        var net = Assert.Single(solution.Networks);
        Assert.Equal(40, net.Demand);
        Assert.Equal(0.25f, net.Satisfaction);
        Assert.All([1, 2, 3, 4], id => Assert.Equal(0.25f, solution.SatisfactionOf(id)));
        Assert.All([1, 2, 3, 4], id => Assert.True(solution.IsPowered(id)));
    }

    [Fact]
    public void Network_without_supply_is_dark()
    {
        var solution = PowerNetwork.Solve([Node(1, 0, 5, C(0)), Node(2, 0, 0, C(1))]);
        Assert.Equal(0, solution.Networks[0].Satisfaction);
        Assert.False(solution.IsPowered(1));
    }

    [Fact]
    public void Zero_demand_network_is_satisfied_when_it_has_supply()
    {
        var withSupply = PowerNetwork.Solve([Node(1, 5, 0, C(0)), Node(2, 0, 0, C(1))]);
        Assert.Equal(1, withSupply.Networks[0].Satisfaction);
        Assert.True(withSupply.IsPowered(2));

        var loneCable = PowerNetwork.Solve([Node(1, 0, 0, C(0))]);
        Assert.Equal(0, loneCable.Networks[0].Satisfaction);
        Assert.False(loneCable.IsPowered(1));
    }

    [Fact]
    public void Idle_thrusters_conduct_but_demand_nothing()
    {
        var thruster = PowerNode.FromPower(2, [C(1)], -5, consuming: false);
        Assert.Equal(0, thruster.Demand);
        Assert.Equal(5, PowerNode.FromPower(2, [C(1)], -5).Demand);
        Assert.Equal(7, PowerNode.FromPower(1, [C(0)], 7).Supply);
        var solution = PowerNetwork.Solve([PowerNode.FromPower(1, [C(0)], 4), thruster, PowerNode.FromPower(3, [C(2)], -8)]);
        var net = Assert.Single(solution.Networks);
        Assert.Equal(0.5f, net.Satisfaction);
    }

    [Fact]
    public void All_or_nothing_balance_tracks_block_count_and_activity()
    {
        // Surplus (+10 - 1 - 5 = +4): active.
        var surplus = PowerNetwork.Solve([
            PowerNode.FromPower(1, [C(0)], 10),
            PowerNode.FromPower(2, [C(1)], -1),
            PowerNode.FromPower(3, [C(2)], -5),
        ]);
        var net1 = Assert.Single(surplus.Networks);
        Assert.Equal(3, net1.BlockCount);
        Assert.Equal(4, net1.Balance);
        Assert.True(net1.IsActive);
        Assert.All([1, 2, 3], id => Assert.True(surplus.IsActive(id)));
        Assert.Same(net1, surplus.NetworkOf(2));

        // Exact zero balance (+10 - 10 = 0): still active (>= 0).
        var exact = PowerNetwork.Solve([
            PowerNode.FromPower(1, [C(0)], 10),
            PowerNode.FromPower(2, [C(1)], -10),
        ]);
        Assert.Equal(0, exact.Networks[0].Balance);
        Assert.True(exact.Networks[0].IsActive);
        Assert.True(exact.IsActive(1));

        // Deficit (+10 - 10 - 1 = -1): whole network is inactive (< 0).
        var deficit = PowerNetwork.Solve([
            PowerNode.FromPower(1, [C(0)], 10),
            PowerNode.FromPower(2, [C(1)], -10),
            PowerNode.FromPower(3, [C(2)], -1),
        ]);
        var net3 = Assert.Single(deficit.Networks);
        Assert.Equal(3, net3.BlockCount);
        Assert.Equal(-1, net3.Balance);
        Assert.False(net3.IsActive);
        Assert.All([1, 2, 3], id => Assert.False(deficit.IsActive(id)));
        Assert.Null(deficit.NetworkOf(99));
        Assert.False(deficit.IsActive(99));
    }

    [Fact]
    public void Bad_input_is_handled()
    {
        Assert.Empty(PowerNetwork.Solve([]).Networks);
        Assert.Throws<ArgumentException>(() => PowerNetwork.Solve([Node(1, 1, 0, C(0)), Node(1, 0, 1, C(5))]));
        var solution = PowerNetwork.Solve([Node(1, float.NaN, -3, C(0)), Node(2, 4, float.PositiveInfinity, C(1)), Node(3, 1, 1)]);
        Assert.Equal(2, solution.Networks.Count);
        Assert.Equal(4, solution.Networks[0].Supply);
        Assert.Equal(0, solution.Networks[0].Demand);
        Assert.Equal(1, solution.SatisfactionOf(3));
        Assert.Equal(-1, solution.NetworkIndexOf(99));
        Assert.Equal(0, solution.SatisfactionOf(99));
        // Two nodes claiming the same cell are connected.
        Assert.Single(PowerNetwork.Solve([Node(1, 1, 0, C(3)), Node(2, 0, 1, C(3))]).Networks);
    }
}
