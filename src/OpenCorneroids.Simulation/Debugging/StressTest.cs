using System.Globalization;
using System.Numerics;
using OpenCorneroids.Simulation.Creatures;
using OpenCorneroids.Simulation.Shapes;
using OpenCorneroids.Simulation.Ships;

namespace OpenCorneroids.Simulation.Debugging;

/// <summary>What the <c>stress</c> console command builds.</summary>
public enum StressKind
{
    /// <summary>A launched hull of about <c>n</c> blocks with a thruster, light, power block or door on its surface cells.</summary>
    Ship,
    /// <summary>A launched gun platform with <c>n</c> guns, all bound to the console's first colour group.</summary>
    Guns,
    /// <summary><c>n</c> creatures of one kind, spread over the aimed asteroid (or ahead, for flyers).</summary>
    Creatures,
    /// <summary><c>n</c> multi-blocks in the static world, in a lattice ahead of the player.</summary>
    Objects,
}

/// <summary>One <c>stress</c> command: what to build and how many.</summary>
/// <param name="Creature">The kind for <see cref="StressKind.Creatures"/>; ignored otherwise.</param>
public readonly record struct StressRequest(StressKind Kind, int Count, CreatureKind Creature = CreatureKind.MineralParasite);

/// <summary>
/// Ours: the <c>stress</c> cheat, which builds big repeatable scenes (a huge ship, a gun battery, a swarm, a field of
/// multi-blocks) so the F3 readout can measure what they cost. Holds the command's parsing, its limits and the pure
/// layout (box sizes, the surface cells that carry fittings, spread directions); the game builds the blocks.
/// </summary>
public static class StressTest
{
    public const string Usage =
        "stress ship <blocks> | stress guns <n> | stress creatures [parasite|egg|needle|scavenger] <n> | stress objects <n>";

    /// <summary>The largest count <c>stress ship</c> takes, the size of the biggest launch (<see cref="Launch.MaxCells"/>).
    /// The ship built is about that many hull blocks, rounded up to whole layers (<see cref="HullSize"/>), with its
    /// fittings on top, so the biggest stress ship is somewhat larger than a launch could collect.</summary>
    public const int MaxShipBlocks = Launch.MaxCells;
    /// <summary>Most guns: as many as the shots alive at once can keep busy (<c>sv_projectile_limit</c> is 512).</summary>
    public const int MaxGuns = 512;
    public const int MaxCreatures = 500;
    public const int MaxObjects = 2000;

    /// <summary>Longest edge of a stress hull: a launched structure's limit (<see cref="Launch.MaxExtent"/>) less room
    /// on both sides for the fittings standing out of it.</summary>
    public const int MaxHullEdge = Launch.MaxExtent - 16;

    /// <summary>The most <paramref name="kind"/> one command builds.</summary>
    public static int MaxCount(StressKind kind) => kind switch
    {
        StressKind.Ship => MaxShipBlocks,
        StressKind.Guns => MaxGuns,
        StressKind.Creatures => MaxCreatures,
        _ => MaxObjects,
    };

    /// <summary>The creature a console word names (as <c>spawn</c> takes them), or null.</summary>
    public static CreatureKind? CreatureNamed(string word) => word.ToLowerInvariant() switch
    {
        "parasite" or "parasites" or "mineralparasite" or "mineral" => CreatureKind.MineralParasite,
        "egg" or "eggs" or "swarmegg" => CreatureKind.SwarmEgg,
        "needle" or "needles" => CreatureKind.Needle,
        "scavenger" or "scavengers" or "flyer" => CreatureKind.Scavenger,
        _ => null,
    };

    /// <summary>
    /// Reads the arguments after <c>stress</c>: a kind, for creatures an optional creature (mineral parasites when
    /// left out), and a count from 1 to <see cref="MaxCount"/>. Null with <paramref name="error"/> set when they don't fit.
    /// </summary>
    public static StressRequest? Parse(IReadOnlyList<string> args, out string? error)
    {
        error = null;
        if (args.Count == 0)
        {
            error = $"Usage: {Usage}";
            return null;
        }
        StressKind? kind = args[0].ToLowerInvariant() switch
        {
            "ship" => StressKind.Ship,
            "guns" or "gun" => StressKind.Guns,
            "creatures" or "creature" => StressKind.Creatures,
            "objects" or "object" or "multiblocks" => StressKind.Objects,
            _ => null,
        };
        if (kind is not { } k)
        {
            error = $"Unknown stress test '{args[0]}'. Usage: {Usage}";
            return null;
        }

        var creature = CreatureKind.MineralParasite;
        int next = 1;
        if (k == StressKind.Creatures && args.Count > 1 && !int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            if (CreatureNamed(args[1]) is not { } named)
            {
                error = $"Unknown creature '{args[1]}': parasite, egg, needle or scavenger.";
                return null;
            }
            creature = named;
            next = 2;
        }
        int max = MaxCount(k);
        if (args.Count != next + 1 || !int.TryParse(args[next], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
        {
            error = $"Usage: {Usage}";
            return null;
        }
        if (count < 1 || count > max)
        {
            error = $"Count {count} is out of range: 1 to {max} for 'stress {args[0].ToLowerInvariant()}'.";
            return null;
        }
        return new StressRequest(k, count, creature);
    }

    /// <summary>
    /// A box of at least <paramref name="blocks"/> cells, as near a cube as whole layers allow: a square base of the
    /// cube root's edge, and enough layers on it. Fewer than one extra layer's cells over the count.
    /// </summary>
    public static Int3 HullSize(int blocks)
    {
        blocks = Math.Clamp(blocks, 1, MaxShipBlocks);
        int edge = Math.Clamp((int)Math.Round(Math.Cbrt(blocks)), 1, MaxHullEdge);
        int layers = (blocks + edge * edge - 1) / (edge * edge);
        return new Int3(edge, layers, edge);
    }

    /// <summary>
    /// A flat plate (two cells thick) whose top face has a slot every <paramref name="spacing"/> cells for at least
    /// <paramref name="slots"/> fittings, within <see cref="MaxHullEdge"/>; the rest go on its other faces.
    /// </summary>
    public static Int3 PlateSize(int slots, int spacing)
    {
        spacing = Math.Max(1, spacing);
        int perRow = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(Math.Max(1, slots))));
        int edge = Math.Clamp(perRow * spacing, 1, MaxHullEdge);
        return new Int3(edge, 2, edge);
    }

    /// <summary>The six outward face normals, top first (so a plate's guns go up and a console sits on top).</summary>
    public static readonly IReadOnlyList<Int3> FaceNormals =
        [Int3.UnitY, -Int3.UnitY, Int3.UnitX, -Int3.UnitX, Int3.UnitZ, -Int3.UnitZ];

    /// <summary>
    /// The surface cells of a box of <paramref name="size"/> (from 0,0,0) that carry a fitting, with the outward normal
    /// of their face: every <paramref name="spacing"/>-th cell both ways across each face, starting half a spacing in
    /// so the slots sit centred. Faces in <see cref="FaceNormals"/> order; a corner cell can be a slot of each face it
    /// is on. The fitting goes in the cell beyond (<c>Cell + Normal</c>).
    /// </summary>
    public static IEnumerable<(Int3 Cell, Int3 Normal)> FaceSlots(Int3 size, int spacing)
    {
        spacing = Math.Max(1, spacing);
        int start = spacing / 2;
        foreach (var normal in FaceNormals)
        {
            int axis = normal.X != 0 ? 0 : normal.Y != 0 ? 1 : 2;
            int u = (axis + 1) % 3, v = (axis + 2) % 3;
            int layer = normal[axis] > 0 ? size[axis] - 1 : 0;
            for (int a = start; a < size[u]; a += spacing)
            for (int b = start; b < size[v]; b += spacing)
            {
                var cell = new int[3];
                cell[axis] = layer;
                cell[u] = a;
                cell[v] = b;
                yield return (new Int3(cell[0], cell[1], cell[2]), normal);
            }
        }
    }

    /// <summary>Whether <paramref name="cell"/> is on the outside layer of a box of <paramref name="size"/>.</summary>
    public static bool IsShell(Int3 cell, Int3 size) =>
        cell.X == 0 || cell.Y == 0 || cell.Z == 0 || cell.X == size.X - 1 || cell.Y == size.Y - 1 || cell.Z == size.Z - 1;

    /// <summary>
    /// The <paramref name="index"/>-th of <paramref name="count"/> directions spread evenly over a cone of
    /// <paramref name="halfAngleDegrees"/> around <paramref name="forward"/> (a sunflower spiral: the first is
    /// <paramref name="forward"/> itself, the last on the cone's edge), for spreading creatures over what the player aims at.
    /// </summary>
    public static Vector3 SpreadDirection(Vector3 forward, int index, int count, float halfAngleDegrees)
    {
        var axis = Vector3.Normalize(forward);
        if (count <= 1 || index <= 0) return axis;
        double golden = Math.PI * (3 - Math.Sqrt(5));
        float radius = MathF.Tan(halfAngleDegrees * MathF.PI / 180) * MathF.Sqrt(Math.Min(index, count - 1) / (float)(count - 1));
        float angle = (float)(index * golden);
        var side = Vector3.Normalize(Vector3.Cross(axis, MathF.Abs(axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
        var up = Vector3.Cross(side, axis);
        return Vector3.Normalize(axis + (side * MathF.Cos(angle) + up * MathF.Sin(angle)) * radius);
    }

    /// <summary>The <paramref name="index"/>-th point of a cubic lattice <paramref name="side"/> points wide, filled
    /// row by row, then layer by layer.</summary>
    public static Int3 LatticePoint(int index, int side)
    {
        side = Math.Max(1, side);
        return new Int3(index % side, index / (side * side), index / side % side);
    }
}
