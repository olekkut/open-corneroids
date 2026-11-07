using OpenCorneroids.Simulation.Shapes;

namespace OpenCorneroids.Simulation.Ships;

/// <summary>
/// One powered block (one whose <c>BlockDef.Power</c> is set) for <see cref="PowerNetwork.Solve"/>.
/// </summary>
/// <param name="Id">Caller's handle, unique within one solve.</param>
/// <param name="Cells">The grid cells the block occupies; a multi-cell block conducts through all of them.</param>
/// <param name="Supply">Power it supplies, ≥ 0.</param>
/// <param name="Demand">Power it consumes right now, ≥ 0 (a thruster only while firing).</param>
public readonly record struct PowerNode(int Id, Int3[] Cells, float Supply, float Demand)
{
    /// <summary>
    /// From an itemset power value: positive supplies, negative consumes, 0 just conducts. With
    /// <paramref name="consuming"/> false a consumer's demand is 0 (an idle thruster) but it still conducts.
    /// </summary>
    public static PowerNode FromPower(int id, Int3[] cells, int power, bool consuming = true) =>
        new(id, cells, Math.Max(0, power), consuming ? Math.Max(0, -power) : 0);
}

/// <summary>One connected group of powered blocks.</summary>
/// <param name="NodeIds">Members, in input order.</param>
/// <param name="Satisfaction">See <see cref="PowerNetwork.Solve"/>.</param>
public sealed record PowerNetworkInfo(IReadOnlyList<int> NodeIds, float Supply, float Demand, float Satisfaction)
{
    /// <summary>Number of powered blocks in the network, shown in the power network window as in the original.</summary>
    public int BlockCount => NodeIds.Count;

    /// <summary>Signed power sum (<c>Supply - Demand</c>), shown in the power network window as in the original.</summary>
    public float Balance => Supply - Demand;

    /// <summary>
    /// As in the original: a network is active when its signed power sum is non-negative (<c>Balance &gt;= 0</c>).
    /// When negative, every block in the network is inactive.
    /// </summary>
    public bool IsActive => Balance >= 0;
}

/// <summary>The result of <see cref="PowerNetwork.Solve"/>.</summary>
public sealed class PowerSolution
{
    readonly Dictionary<int, int> _networkOf;

    internal PowerSolution(IReadOnlyList<PowerNetworkInfo> networks, Dictionary<int, int> networkOf)
    {
        Networks = networks;
        _networkOf = networkOf;
    }

    /// <summary>Every network, ordered by its first node in the input.</summary>
    public IReadOnlyList<PowerNetworkInfo> Networks { get; }

    /// <summary>Index into <see cref="Networks"/> of the node's network, or −1 for an unknown id.</summary>
    public int NetworkIndexOf(int id) => _networkOf.TryGetValue(id, out var index) ? index : -1;

    /// <summary>The node's network, or <c>null</c> for an unknown id.</summary>
    public PowerNetworkInfo? NetworkOf(int id) => _networkOf.TryGetValue(id, out var index) ? Networks[index] : null;

    /// <summary>The node's satisfaction (its network's), or 0 for an unknown id.</summary>
    public float SatisfactionOf(int id) => _networkOf.TryGetValue(id, out var index) ? Networks[index].Satisfaction : 0;

    /// <summary>
    /// Whether the node's network is active under the original's all-or-nothing rule (<see cref="PowerNetworkInfo.IsActive"/>).
    /// </summary>
    public bool IsActive(int id) => _networkOf.TryGetValue(id, out var index) && Networks[index].IsActive;

    /// <summary>
    /// Whether the block's network delivers positive satisfaction (<c>Satisfaction &gt; 0</c>).
    /// </summary>
    public bool IsPowered(int id) => SatisfactionOf(id) > 0;
}

/// <summary>
/// Groups face-adjacent powered blocks into networks and computes each network's power balance (as in the original) and
/// proportional satisfaction.
/// </summary>
public static class PowerNetwork
{
    static readonly Int3[] Forward = [Int3.UnitX, Int3.UnitY, Int3.UnitZ];

    /// <summary>
    /// Solves all networks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two nodes are in the same network when a cell of one is face-adjacent to (or the same as) a cell of the other,
    /// directly or through other nodes. Only the cells passed in conduct; blocks without power are simply not nodes.
    /// </para>
    /// <para>
    /// Per network: supply and demand are the sums over its nodes, and satisfaction is <c>min(1, supply / demand)</c>.
    /// A network with no demand has satisfaction 1 when it has supply and 0 when it has none (a lone cable or an idle
    /// thruster with no generator stays dark). Every node gets its network's satisfaction: a consumer runs at that
    /// fraction (thrusters scale their force by it), and a block is drawn active when it is above 0.
    /// </para>
    /// <para>Negative or non-finite supply and demand count as 0; a node without cells forms a network of its own.</para>
    /// </remarks>
    /// <exception cref="ArgumentException">Two nodes share an id.</exception>
    public static PowerSolution Solve(IEnumerable<PowerNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var list = nodes.ToList();
        var indexOfId = new Dictionary<int, int>(list.Count);
        for (int i = 0; i < list.Count; i++)
            if (!indexOfId.TryAdd(list[i].Id, i))
                throw new ArgumentException($"Power node id {list[i].Id} appears twice.", nameof(nodes));

        var parent = new int[list.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Root(int i)
        {
            while (parent[i] != i) i = parent[i] = parent[parent[i]];
            return i;
        }
        void Union(int a, int b)
        {
            a = Root(a);
            b = Root(b);
            if (a == b) return;
            // The smaller index wins, so roots (and network order) do not depend on hashing.
            if (a < b) parent[b] = a;
            else parent[a] = b;
        }

        var owner = new Dictionary<Int3, int>();
        for (int i = 0; i < list.Count; i++)
            foreach (var cell in list[i].Cells ?? [])
                if (!owner.TryAdd(cell, i)) Union(owner[cell], i);
        foreach (var (cell, i) in owner)
            foreach (var step in Forward)
                if (owner.TryGetValue(cell + step, out var j)) Union(i, j);

        var networkOfRoot = new Dictionary<int, int>();
        var members = new List<List<int>>();
        var supply = new List<double>();
        var demand = new List<double>();
        var networkOf = new Dictionary<int, int>(list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            int root = Root(i);
            if (!networkOfRoot.TryGetValue(root, out var network))
            {
                network = members.Count;
                networkOfRoot.Add(root, network);
                members.Add([]);
                supply.Add(0);
                demand.Add(0);
            }
            members[network].Add(list[i].Id);
            supply[network] += NonNegative(list[i].Supply);
            demand[network] += NonNegative(list[i].Demand);
            networkOf.Add(list[i].Id, network);
        }

        var networks = new PowerNetworkInfo[members.Count];
        for (int n = 0; n < networks.Length; n++)
        {
            double s = supply[n], d = demand[n];
            double satisfaction = d > 0 ? Math.Min(1, s / d) : s > 0 ? 1 : 0;
            networks[n] = new PowerNetworkInfo(members[n], (float)s, (float)d, (float)satisfaction);
        }
        return new PowerSolution(networks, networkOf);
    }

    static double NonNegative(float v) => float.IsFinite(v) && v > 0 ? v : 0;
}
