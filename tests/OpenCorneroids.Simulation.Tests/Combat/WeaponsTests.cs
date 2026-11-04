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

    [Fact]
    public void Shot_cost_is_the_size_of_power_usage()
    {
        static ItemDef Gun(int powerUsage) => new(1, "weapon", "weapon", "Gun", "", new Rgb8(0, 0, 0), 1, 1, powerUsage,
            new CellRect(0, 0, 1, 1), null, [], new Dictionary<string, string>(), null);
        Assert.Equal(3, Weapons.ShotCost(Gun(3)));
        Assert.Equal(25, Weapons.ShotCost(Gun(-25)));
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
