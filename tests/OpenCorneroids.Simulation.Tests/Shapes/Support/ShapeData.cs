using System.Numerics;
using OpenCorneroids.Content;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Shapes;

namespace OpenCorneroids.Simulation.Tests.Shapes.Support;

/// <summary>
/// Block dimensions as the itemsets give them (size, modelSize, modelPlacement and the walls region). These are the
/// numbers of the real multi-cell blocks in vanilla 1.2.0 and DarthNox v21, typed in by hand so the tests run without
/// the original files.
/// </summary>
static class ShapeData
{
    public static readonly CellRect Cell = new(0, 0, 1, 1);

    public static BlockDef Def(Vector3 size, Vector3 model = default, Vector3 placement = default, CellRect? walls = null,
        CellRect? top = null, CellRect? bottom = null, FaceSet? inactive = null) =>
        new(1, 1, 5, 1, size, model, placement,
            new FaceSet(walls ?? Cell, top ?? new CellRect(1, 0, 1, 1), bottom ?? new CellRect(2, 0, 1, 1)), inactive, null);

    public static BlockShape Shape(Vector3 size, Vector3 model = default, Vector3 placement = default, CellRect? walls = null) =>
        BlockShape.Build(Def(size, model, placement, walls));

    public static readonly BlockShape BlastDoor = Shape(new(2, 4, 1), new(2, 2, 1), new(0, -1, 0), new CellRect(14, 0, 2, 2));
    public static readonly BlockShape BayDoor = Shape(new(11, 22, 2), new(11, 11, 2), new(0, -5.5f, 0), new CellRect(14, 2, 2, 2));
    public static readonly BlockShape SpinalCannon = Shape(new(3, 18, 3), walls: new CellRect(12, 5, 2, 4));

    /// <summary>Name, size, modelSize, modelPlacement, then the model box in local coordinates (min y, max y).</summary>
    public static TheoryData<string, Vector3, Vector3, Vector3, float, float> RealMultiBlocks => new()
    {
        { "Iron blast door", new(2, 4, 1), new(2, 2, 1), new(0, -1, 0), 0, 2 },
        { "Light Assault Cannon", new(1, 2, 1), Vector3.Zero, Vector3.Zero, 0, 2 },
        { "Anti-Corvette Cannon", new(2, 3, 1), Vector3.Zero, Vector3.Zero, 0, 3 },
        { "Anti-Frigate Cannon", new(2, 4, 2), Vector3.Zero, Vector3.Zero, 0, 4 },
        { "Powerful cobalt power block", new(1, 2, 1), Vector3.Zero, Vector3.Zero, 0, 2 },
        { "Standard nuclear power block", new(2, 2, 2), Vector3.Zero, Vector3.Zero, 0, 2 },
        { "Powerful nuclear power block", new(2, 3, 2), Vector3.Zero, Vector3.Zero, 0, 3 },
        { "Weak hydrogen thruster", new(1, 3, 1), new(1, 1, 1), new(0, -1, 0), 0, 1 },
        { "Standard hydrogen thruster", new(1, 5, 1), new(1, 2, 1), new(0, -1.5f, 0), 0, 2 },
        { "Powerful hydrogen thruster", new(2, 4, 2), new(2, 1, 2), new(0, -1.5f, 0), 0, 1 },
        { "Standard ion thruster", new(2, 7, 2), new(2, 2, 2), new(0, -2.5f, 0), 0, 2 },
        { "Big window block", new(2, 2, 1), Vector3.Zero, Vector3.Zero, 0, 2 },
        { "DarthNox bay door", new(11, 22, 2), new(11, 11, 2), new(0, -5.5f, 0), 0, 11 },
        { "DarthNox large blast door", new(3, 6, 1), new(3, 3, 1), new(0, -1.5f, 0), 0, 3 },
        { "DarthNox spinal cannon", new(3, 18, 3), Vector3.Zero, Vector3.Zero, 0, 18 },
        { "DarthNox very big window", new(3, 3, 1), Vector3.Zero, Vector3.Zero, 0, 3 },
    };
}
