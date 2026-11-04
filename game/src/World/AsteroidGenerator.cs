using Godot;
using OpenCorneroids.Content.Itemsets;

namespace OpenCorneroids.Game.World;

/// <summary>
/// Fills the world with a few noisy asteroids built from the itemset's own data: each asteroid has an asteroidType,
/// its rock comes from that type's asteroid blocks, and minerals of that type are scattered by their rarity.
/// </summary>
public static class AsteroidGenerator
{
    readonly record struct Rock(Vector3 Center, float Radius, AsteroidType Type);

    /// <summary>Generates the asteroids and returns a good viewpoint: (camera position, point to look at).</summary>
    public static (Vector3 Eye, Vector3 Target) Generate(VoxelWorld world, int seed)
    {
        var palette = world.Palette;
        var noise = new FastNoiseLite { Seed = seed, NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex, Frequency = 0.07f };
        var rng = new RandomNumberGenerator { Seed = (ulong)seed };

        // If an itemset has no asteroid blocks at all, build from whatever opaque cubes it has.
        // Only single-cell blocks: the generator writes the chunk grid, which holds no multi-blocks.
        var anyRock = palette.Where(i => i.Get<AsteroidComponent>() is not null).Where(i => !palette.IsObject(i)).ToList();
        if (anyRock.Count == 0) anyRock = palette.GridIndices.Where(i => !palette.IsCutout(i)).Take(3).ToList();

        Rock[] rocks =
        [
            new(new Vector3(64, 64, 64), 22, AsteroidType.Light),
            new(new Vector3(26, 92, 40), 12, AsteroidType.Medium),
            new(new Vector3(100, 44, 96), 15, AsteroidType.Heavy),
        ];
        foreach (var rock in rocks)
        {
            var ofType = palette.Where(i => i.Get<AsteroidComponent>()?.AsteroidType == rock.Type).Where(i => !palette.IsObject(i)).OrderBy(i => palette.Item(i).ItemId).ToList();
            if (ofType.Count == 0) ofType = anyRock;
            var minerals = palette.Where(i => i.Get<MineralComponent>()?.AsteroidType == rock.Type).Where(i => !palette.IsObject(i))
                .Select(i => (Block: i, Weight: Math.Max(1, palette.Item(i).Get<MineralComponent>()!.Rarity))).ToList();
            int totalWeight = minerals.Sum(m => m.Weight);

            int reach = (int)(rock.Radius * 1.5f);
            for (int z = -reach; z <= reach; z++)
            for (int y = -reach; y <= reach; y++)
            for (int x = -reach; x <= reach; x++)
            {
                var p = new Vector3(rock.Center.X + x, rock.Center.Y + y, rock.Center.Z + z);
                float density = rock.Radius - new Vector3(x, y, z).Length() + noise.GetNoise3D(p.X, p.Y, p.Z) * rock.Radius * 0.45f;
                if (density <= 0) continue;

                ushort block = ofType[0];
                float roll = rng.Randf();
                if (totalWeight > 0 && roll < 0.05f)
                {
                    int pick = rng.RandiRange(1, totalWeight);
                    foreach (var mineral in minerals)
                        if ((pick -= mineral.Weight) <= 0) { block = mineral.Block; break; }
                }
                else if (ofType.Count > 1 && roll < 0.13f)
                {
                    block = ofType[rng.RandiRange(1, ofType.Count - 1)];
                }
                world.SetRaw(new Vector3I((int)p.X, (int)p.Y, (int)p.Z), block);
            }
        }

        var main = rocks[0];
        return (main.Center + new Vector3(0, main.Radius * 0.3f, main.Radius * 2.1f), main.Center);
    }
}
