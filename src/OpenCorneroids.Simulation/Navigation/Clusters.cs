using System.Numerics;

namespace OpenCorneroids.Simulation.Navigation;

/// <summary>A connected lump of blocks: an asteroid, or a station built onto one.</summary>
/// <param name="Center">Mean of the cell centres, in cell units.</param>
/// <param name="Radius">From <see cref="Center"/> to the farthest cell centre, plus half a cell.</param>
/// <param name="Mass">Sum of the blocks' masses.</param>
public sealed record Cluster(Vector3 Center, float Radius, double Mass, int Cells);

/// <summary>
/// Splits a cubic cell grid into face-connected clusters, for the HUD's asteroid markers (the original shows distance
/// and mass for each asteroid). Pure data, so it can run off the main thread on a copy of the cells.
/// </summary>
public static class Clusters
{
    /// <param name="cells">Size³ cell values at <c>(z * size + y) * size + x</c>; 0 is empty.</param>
    /// <param name="massOf">Mass of a non-empty cell value.</param>
    /// <param name="minCells">Smaller clusters (loose blocks, a lone placed cube) are left out.</param>
    /// <returns>Clusters, largest first.</returns>
    public static List<Cluster> Find(ReadOnlySpan<ushort> cells, int size, Func<ushort, float> massOf, int minCells = 1)
    {
        ArgumentNullException.ThrowIfNull(massOf);
        if (size <= 0 || cells.Length != size * size * size)
            throw new ArgumentException($"expected {size}³ cells, got {cells.Length}", nameof(cells));

        var seen = new bool[cells.Length];
        var queue = new int[cells.Length];
        var members = new List<int>();
        var found = new List<Cluster>();
        int plane = size * size;
        for (int start = 0; start < cells.Length; start++)
        {
            if (cells[start] == 0 || seen[start]) continue;
            members.Clear();
            int head = 0, tail = 0;
            queue[tail++] = start;
            seen[start] = true;
            double mass = 0;
            var sum = Vector3.Zero;
            while (head < tail)
            {
                int i = queue[head++];
                members.Add(i);
                int x = i % size, y = i / size % size, z = i / plane;
                sum += new Vector3(x, y, z);
                mass += massOf(cells[i]);
                if (x > 0) Visit(i - 1, cells);
                if (x < size - 1) Visit(i + 1, cells);
                if (y > 0) Visit(i - size, cells);
                if (y < size - 1) Visit(i + size, cells);
                if (z > 0) Visit(i - plane, cells);
                if (z < size - 1) Visit(i + plane, cells);
            }
            if (members.Count < minCells) continue;
            var center = sum / members.Count;
            float far = 0;
            foreach (int i in members)
                far = MathF.Max(far, Vector3.DistanceSquared(center, new Vector3(i % size, i / size % size, i / plane)));
            // Cell values are corner coordinates; the centres are half a cell further on.
            found.Add(new Cluster(center + new Vector3(0.5f), MathF.Sqrt(far) + 0.5f, mass, members.Count));

            void Visit(int n, ReadOnlySpan<ushort> c)
            {
                if (c[n] == 0 || seen[n]) return;
                seen[n] = true;
                queue[tail++] = n;
            }
        }
        found.Sort((a, b) => b.Cells.CompareTo(a.Cells));
        return found;
    }
}
