using System.Numerics;
using OpenCorneroids.Simulation.Combat;
using OpenCorneroids.Simulation.Items;
using static OpenCorneroids.Simulation.Tests.Items.Support.TestItems;

namespace OpenCorneroids.Simulation.Tests.Combat;

public class DamageTests
{
    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(5, 0.2986)]
    [InlineData(10, 0.3997)]
    [InlineData(20, 0.5074)]
    [InlineData(60, 0.6851)]
    [InlineData(500, 0.6851)]   // capped at 60
    [InlineData(-4, 0.0)]
    public void Armor_reduction_follows_the_logarithm_and_the_cap(int armor, double expected)
    {
        Assert.Equal(expected, Damage.ArmorReduction(armor), 3);
    }

    [Fact]
    public void Damage_after_armor_is_rounded_down()
    {
        Assert.Equal(100, Damage.AfterArmor(100, 0));
        Assert.Equal(70, Damage.AfterArmor(100, 5));      // 100 × (1 − 0.2986) = 70.1
        Assert.Equal(31, Damage.AfterArmor(100, 60));
        Assert.Equal(0, Damage.AfterArmor(1, 60));
        Assert.Equal(0, Damage.AfterArmor(-5, 0));
        Assert.Equal(0, Damage.AfterArmor(double.NaN, 0));
        // A tuned scale: armor 5 with scale 3 takes off ln(6)/3 ≈ 0.597.
        Assert.Equal(40, Damage.AfterArmor(100, 5, scale: 3));
    }

    [Theory]
    [InlineData(4f, 0)]
    [InlineData(7.99f, 0)]
    [InlineData(8f, 0)]
    [InlineData(9f, 7)]
    [InlineData(12.5f, 31)]
    [InlineData(20f, 84)]
    public void Impacts_hurt_above_eight_metres_per_second(float speed, int expected)
    {
        Assert.Equal(expected, Damage.Impact(speed));
    }

    [Fact]
    public void Impact_damage_can_be_tuned_or_turned_off()
    {
        Assert.Equal(10, Damage.Impact(7f, threshold: 2, perSpeed: 2));
        Assert.Equal(0, Damage.Impact(50f, perSpeed: 0));
    }

    [Fact]
    public void A_segment_hits_a_sphere_where_it_first_enters()
    {
        var center = new Vector3(0, 0, 5);
        Assert.Equal(0.465f, Damage.SegmentHitsSphere(Vector3.Zero, new Vector3(0, 0, 10), center, 0.35f)!.Value, 3);
        // Passing 0.7 m to the side: inside the 0.735 m hit radius, outside a smaller one.
        Assert.NotNull(Damage.SegmentHitsSphere(new Vector3(0.7f, 0, 0), new Vector3(0.7f, 0, 10), center, Damage.ShotHitRadius));
        Assert.Null(Damage.SegmentHitsSphere(new Vector3(0.7f, 0, 0), new Vector3(0.7f, 0, 10), center, 0.6f));
        // Too short to reach it, or going away from it.
        Assert.Null(Damage.SegmentHitsSphere(Vector3.Zero, new Vector3(0, 0, 3), center, 0.5f));
        Assert.Null(Damage.SegmentHitsSphere(new Vector3(0, 0, 6), new Vector3(0, 0, 20), center, 0.5f));
        // Starting inside counts at once.
        Assert.Equal(0f, Damage.SegmentHitsSphere(center, center + Vector3.UnitX, center, 0.5f));
    }

    [Theory]
    [InlineData(0.0, 0.0, 0.0, 0.0)]
    [InlineData(0.99, 0.5, 0.01, 0.99)]
    [InlineData(0.3, 0.9, 0.6, 0.5)]
    public void Respawn_points_lie_in_the_band_up_every_axis(double u1, double u2, double u3, double u4)
    {
        var death = new Vector3(10, -20, 30);
        var point = Damage.RespawnPoint(death, u1, u2, u3, u4);
        var offset = point - death;
        Assert.InRange(offset.Length(), Damage.RespawnMin - 0.01f, Damage.RespawnMax + 0.01f);
        Assert.True(offset.X > 0 && offset.Y > 0 && offset.Z > 0);
    }

    [Fact]
    public void A_shot_absorbs_what_a_block_has_left_and_flies_on_with_the_rest()
    {
        var damage = new BlockDamage<int>();
        // A 20-damage shot through a block of health 5: 5 absorbed, block broken, 15 left.
        Assert.Equal((5.0, true), damage.Absorb(1, 20, 5));
        // A 3-damage shot into a block of health 10, twice: dented, then 6 taken in all.
        Assert.Equal((3.0, false), damage.Absorb(2, 3, 10));
        Assert.Equal((3.0, false), damage.Absorb(2, 3, 10));
        Assert.Equal(6, damage.Taken(2));
        // The next 10-damage shot spends only the 4 hit points left.
        Assert.Equal((4.0, true), damage.Absorb(2, 10, 10));
        Assert.Equal(0, damage.Count);
        // Health 0 in the data counts as 1 hit point.
        Assert.Equal((1.0, true), damage.Absorb(3, 50, 0));
        // A shot with nothing left does nothing.
        Assert.Equal((0.0, false), damage.Absorb(4, 0, 10));
    }

    static Suit NewSuit() => new(Set());

    [Fact]
    public void Hurt_takes_health_and_death_clears_buffs()
    {
        var suit = NewSuit();
        Assert.Equal(30, suit.Hurt(30));
        Assert.Equal(70, suit.Health);
        Assert.False(suit.IsDead);

        Assert.Equal(70, suit.Hurt(500));
        Assert.True(suit.IsDead);
        Assert.Equal(0, suit.Health);
        Assert.Equal(0, suit.Hurt(10));   // nothing more while dead
    }

    [Fact]
    public void God_mode_takes_no_damage()
    {
        var suit = NewSuit();
        suit.GodMode = true;
        Assert.Equal(0, suit.Hurt(80));
        Assert.Equal(100, suit.Health);
    }

    [Fact]
    public void Health_regenerates_half_a_point_a_second_but_not_while_dead()
    {
        var suit = NewSuit();
        suit.Hurt(10);
        suit.Tick(4);
        Assert.Equal(92, suit.Health, 6);
        suit.HealthRegenPerSecond = 0;
        suit.Tick(10);
        Assert.Equal(92, suit.Health, 6);

        suit.HealthRegenPerSecond = 0.5;
        suit.Die();
        suit.Tick(100);
        Assert.Equal(0, suit.Health);
    }

    [Fact]
    public void Respawn_gives_the_base_hundred_and_full_power()
    {
        var suit = NewSuit();
        suit.TrySpend(70);
        suit.Die();
        suit.Respawn();
        Assert.False(suit.IsDead);
        Assert.Equal((100.0, 100.0), (suit.Health, suit.Power));
    }

    [Fact]
    public void Setting_health_to_zero_kills_and_a_dead_save_loads_alive()
    {
        var suit = NewSuit();
        suit.SetHealth(0);
        Assert.True(suit.IsDead);

        var other = NewSuit();
        other.Restore(suit.Save());
        Assert.False(other.IsDead);
        Assert.Equal(100, other.Health);
    }
}
