using System.Numerics;
using OpenCorneroids.Content;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Combat;

namespace OpenCorneroids.Simulation.Tests.Combat;

public class WeaponsTests
{
    static readonly ProjectileDef Bolt = new(1, new Rgb8(0, 0, 255), 2, 3000, 0, 50, new Vector3(0.05f, 0.5f, 0.05f), null);

    [Fact]
    public void Recoil_pushes_back_by_pushback_times_projectile_speed()
    {
        var recoil = Weapons.Recoil(new Vector3(0, 0, -2), 0.025f, Bolt);
        Assert.Equal(1.25f * Weapons.RecoilScale, recoil.Z, 5);
        Assert.Equal((0f, 0f), (recoil.X, recoil.Y));
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.99, 0.3)]
    [InlineData(0.5, 0.75)]
    public void Spread_stays_inside_its_cone(double u1, double u2)
    {
        var forward = new Vector3(1, 2, 3);
        var shot = Weapons.Spread(forward, 0.1f, u1, u2);

        Assert.Equal(1f, shot.Length(), 4);
        Assert.True(Vector3.Dot(shot, Vector3.Normalize(forward)) >= MathF.Cos(0.1f) - 1e-5f);
    }

    [Fact]
    public void No_spread_fires_straight() =>
        Assert.Equal(Vector3.UnitX, Weapons.Spread(new Vector3(5, 0, 0), 0, 0.7, 0.2));

    [Fact]
    public void A_grazing_shot_ricochets_and_a_steep_one_hits()
    {
        var up = Vector3.UnitY;
        // 10 degrees into the surface against a 45 degree ricochet angle: glances off, mirrored.
        var grazing = new Vector3(MathF.Cos(0.1745f), -MathF.Sin(0.1745f), 0) * 60;
        var bounced = Weapons.Ricochet(grazing, up, 0.7854f);
        Assert.NotNull(bounced);
        Assert.Equal(-grazing.Y, bounced.Value.Y, 4);
        Assert.Equal(grazing.X, bounced.Value.X, 4);

        Assert.Null(Weapons.Ricochet(new Vector3(0.3f, -1, 0), up, 0.7854f));
        Assert.Null(Weapons.Ricochet(grazing, up, 0));   // no ricochet angle: always a hit
    }

    static ItemDef Gun(int powerUsage) => new(1, "weapon", "weapon", "Gun", "", new Rgb8(0, 0, 0), 1, 1, powerUsage,
        new CellRect(0, 0, 1, 1), null, [], new Dictionary<string, string>(), null);

    [Fact]
    public void Shot_cost_is_power_usage_and_a_negative_one_gives_power_as_in_the_original()
    {
        Assert.Equal(3, Weapons.ShotCost(Gun(3)));
        Assert.Equal(-25, Weapons.ShotCost(Gun(-25)));
        Assert.Equal(25, Weapons.ShotCost(Gun(-25), negativeGivesPower: false));   // ours: the size is what it costs
    }

    [Fact]
    public void The_spread_list_is_the_originals_seeded_list_of_unit_vectors()
    {
        var list = Weapons.SpreadVectors;
        Assert.Equal(256, list.Count);
        Assert.All(list, v => Assert.Equal(1f, v.Length(), 4));
        // Pinned, so a change in the seeded generator can't go unnoticed.
        Near(new Vector3(-0.04551f, -0.54198f, -0.83916f), list[0]);
        Near(new Vector3(0.29655f, -0.79266f, -0.53267f), list[2]);

        var random = new Random(Weapons.SpreadSeed);
        foreach (var v in list)
        {
            var raw = new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f);
            Near(Vector3.Normalize(raw), v);
        }
    }

    static void Near(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"expected {expected}, got {actual}");

    [Theory]
    [InlineData(0.05f)]
    [InlineData(0.1f)]
    [InlineData(0.5f)]
    public void Original_spread_is_not_normalised_so_speed_varies_within_plus_minus_spread(float spread)
    {
        var aim = Vector3.Normalize(new Vector3(1, 2, 3));
        for (int i = 0; i < Weapons.SpreadCount; i++)
        {
            var shot = Weapons.OriginalSpread(aim * 7, spread, i);
            Near(aim + spread * Weapons.SpreadVectors[i], shot);
            Assert.InRange(shot.Length(), 1 - spread - 1e-5f, 1 + spread + 1e-5f);
            Assert.True(Vector3.Dot(Vector3.Normalize(shot), aim) >= MathF.Cos(MathF.Asin(spread)) - 1e-5f);
        }
    }

    [Fact]
    public void Original_spread_is_held_within_half_and_none_fires_straight()
    {
        var aim = Vector3.UnitX;
        Assert.Equal(Weapons.OriginalSpread(aim, 0.5f, 7), Weapons.OriginalSpread(aim, 3f, 7));
        Assert.Equal(aim, Weapons.OriginalSpread(aim * 4, 0, 7));
        Assert.Equal(aim, Weapons.OriginalSpread(aim, -1, 7));
    }

    [Fact]
    public void Each_weapon_steps_through_the_spread_list_and_repeats_after_256_shots()
    {
        var sequence = new SpreadSequence();
        var aim = Vector3.UnitZ;
        var first = sequence.Next(5, aim, 0.1f);
        Assert.Equal(Weapons.OriginalSpread(aim, 0.1f, 0), first);

        // Another weapon has its own index.
        Assert.Equal(first, sequence.Next(6, aim, 0.1f));
        Assert.Equal(Weapons.OriginalSpread(aim, 0.1f, 1), sequence.Next(5, aim, 0.1f));

        for (int i = 2; i < Weapons.SpreadCount; i++) sequence.Next(5, aim, 0.1f);
        Assert.Equal(0, sequence.Peek(5));
        Assert.Equal(first, sequence.Next(5, aim, 0.1f));
    }

    [Fact]
    public void A_shot_starts_right_of_and_below_the_eye_and_ahead_along_the_aim_as_in_the_original()
    {
        var eye = new Vector3(10, 20, 30);
        var from = Weapons.ShotOrigin(eye, new Vector3(0, 0, -5), Vector3.UnitX, Vector3.UnitY);
        Near(new Vector3(10.3f, 19.8f, 29f), from);
        Near(new Vector3(10.3f, 19.8f, 30f), Weapons.ShotOrigin(eye, -Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY, forwardOffset: 0));
    }

    [Fact]
    public void A_trigger_waits_out_its_reload()
    {
        var trigger = new Trigger();
        Assert.True(trigger.TryFire(10, 400));
        Assert.False(trigger.TryFire(10.39, 400));
        Assert.Equal(0.01, trigger.Wait(10.39), 6);
        Assert.True(trigger.TryFire(10.4, 400));
    }

    [Fact]
    public void Blocks_break_when_damage_reaches_health()
    {
        var damage = new BlockDamage<string>();
        Assert.False(damage.Hit("rock", 2, 5));
        Assert.False(damage.Hit("rock", 2, 5));
        Assert.True(damage.Hit("rock", 2, 5));
        Assert.Equal(0, damage.Count);

        Assert.True(damage.Hit("glass", 2, 0));   // health 0: the first hit breaks it
        damage.Hit("a", 1, 9);
        damage.Hit("b", 1, 9);
        damage.ForgetWhere(k => k == "a");
        Assert.Equal((0.0, 1.0), (damage.Taken("a"), damage.Taken("b")));
    }
}
