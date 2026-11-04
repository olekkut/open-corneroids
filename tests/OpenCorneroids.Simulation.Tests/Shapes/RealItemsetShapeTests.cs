using System.Numerics;
using OpenCorneroids.Content;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Shapes;

namespace OpenCorneroids.Simulation.Tests.Shapes;

/// <summary>
/// Checks the model-box convention against every block of every itemset in the developer's local BYOA folder
/// (<c>OPENCORNEROIDS_BYOA</c>, else <c>byoa/</c> next to the solution). The original files are never in the repo, so
/// this is skipped when they are absent.
/// </summary>
public class RealItemsetShapeTests
{
    static readonly string? Root = Locate();

    static string? Locate()
    {
        if (Environment.GetEnvironmentVariable("OPENCORNEROIDS_BYOA") is { Length: > 0 } fromEnv)
            return Directory.Exists(fromEnv) ? fromEnv : null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (!File.Exists(Path.Combine(dir.FullName, "OpenCorneroids.sln"))) continue;
            var byoa = Path.Combine(dir.FullName, "byoa");
            return Directory.Exists(byoa) && Directory.EnumerateDirectories(byoa).Any() ? byoa : null;
        }
        return null;
    }

    sealed class NeedsByoaFactAttribute : FactAttribute
    {
        public NeedsByoaFactAttribute()
        {
            if (Root is null) Skip = "no local BYOA folder (set OPENCORNEROIDS_BYOA or fill <repo>/byoa)";
        }
    }

    /// <summary>
    /// Blocks whose data breaks the convention, and that <see cref="BlockShape.Build(BlockDef)"/> therefore corrects:
    /// DarthNox's small and powerful ion thrusters (both the mod and the v15 repack) put a 3-cell model in 7-cell bounds
    /// with placement −2.5, which centres it half a cell below the bounds; −2 would fill the bottom three cells. It
    /// looks like the 2×7×2 ion thruster's placement copied over. Every vanilla block fits as given.
    /// </summary>
    static readonly string[] KnownMisfits =
    [
        "Chef Anons DarthNox Rebalance Repack v15:Blocksets/DarthNoxtItemset.xml 'Powerful ion thruster'",
        "Chef Anons DarthNox Rebalance Repack v15:Blocksets/DarthNoxtItemset.xml 'Small ion thruster'",
        "DarthNoxItemset:Blocksets/DarthNoxtItemset.xml 'Powerful ion thruster'",
        "DarthNoxItemset:Blocksets/DarthNoxtItemset.xml 'Small ion thruster'",
    ];

    [NeedsByoaFact]
    public void Every_real_block_keeps_its_model_and_door_travel_inside_its_bounds()
    {
        var library = ContentLibrary.Scan([Root!]);
        int checkedBlocks = 0;
        var corrected = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var entry in library.Itemsets)
        {
            if (library.Load(entry, []).Itemset is not { } set) continue;
            foreach (var item in set.Blocks)
            {
                var shape = BlockShape.Build(item.Block!);
                var where = $"{entry.Pack.Id}:{entry.Path} '{item.Name}'";
                if (shape.Problems.Count > 0)
                {
                    corrected.Add(where);
                    Assert.All(shape.Problems, p => Assert.Contains("placement", p));
                }

                var bounds = shape.Size.ToVector3();
                var travel = item.Get<DoorComponent>() is { } door ? DoorMotion.Offset(1, door) : Vector3.Zero;
                foreach (var offset in new[] { Vector3.Zero, travel })
                {
                    var box = shape.ModelBox(Int3.Zero, Orientation.Default, offset);
                    const float e = 1e-4f;
                    Assert.True(box.Min.X > -e && box.Min.Y > -e && box.Min.Z > -e
                                && box.Max.X < bounds.X + e && box.Max.Y < bounds.Y + e && box.Max.Z < bounds.Z + e,
                        $"{where}: model {box} at offset {offset} leaves bounds {bounds}");
                }
                checkedBlocks++;
            }
        }
        Assert.True(checkedBlocks > 0);
        Assert.Equal(KnownMisfits.Where(m => Directory.Exists(Path.Combine(Root!, m.Split(':')[0]))).Order(StringComparer.Ordinal), corrected);
    }
}
