using System.Numerics;
using OpenCorneroids.Simulation.Physics;

namespace OpenCorneroids.Simulation.Tests.Physics;

public class VoxelColliderTests
{
    static readonly Vector3 Body = new(0.3f, 0.9f, 0.3f);
    const float Close = 1e-3f;
    /// <summary>A stopped box must end at least this far short of the face it hit: half the ~1e-4 skin the
    /// spec asks for, leaving room for rounding. A literal, so a collider with no skin fails.</summary>
    const float Gap = 5e-5f;

    /// <summary>Sparse block set; anything not added is empty space.</summary>
    sealed class Blocks
    {
        readonly HashSet<(int, int, int)> _solid = new();
        public int Queries { get; private set; }

        public Blocks Fill(int x0, int y0, int z0, int x1, int y1, int z1)
        {
            for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                _solid.Add((x, y, z));
            return this;
        }

        public Blocks Clear(int x, int y, int z)
        {
            _solid.Remove((x, y, z));
            return this;
        }

        public bool IsSolid(int x, int y, int z)
        {
            Queries++;
            return _solid.Contains((x, y, z));
        }
    }

    [Fact]
    public void Falls_onto_a_floor_and_rests_on_it()
    {
        var world = new Blocks().Fill(-5, 0, -5, 5, 0, 5);
        var start = new Vector3(0.5f, 3f, 0.5f);

        var result = VoxelCollider.Move(start, Body, new Vector3(0, -5, 0), world.IsSolid);

        Assert.True(result.HitY);
        Assert.False(result.HitX || result.HitZ);
        Assert.InRange(result.Position.Y - Body.Y, 1f + Gap, 1f + Close);
        Assert.False(VoxelCollider.Overlaps(result.Position, Body, world.IsSolid));

        // Resting there, a further push down does nothing and sideways movement is unaffected by the floor.
        var again = VoxelCollider.Move(result.Position, Body, new Vector3(2, -1, 0), world.IsSolid);
        Assert.True(again.HitY);
        Assert.False(again.HitX);
        Assert.Equal(result.Position.Y, again.Position.Y, 5);
        Assert.Equal(2.5f, again.Position.X, 4);
    }

    [Fact]
    public void Slides_along_a_wall_hit_at_an_angle()
    {
        var world = new Blocks().Fill(3, -5, -20, 3, 5, 20); // wall facing -X at x = 3
        var start = new Vector3(1.5f, 0.5f, 0.5f);

        var result = VoxelCollider.Move(start, Body, new Vector3(4, 0, 3), world.IsSolid);

        Assert.True(result.HitX);
        Assert.False(result.HitZ);
        Assert.InRange(result.Position.X + Body.X, 3f - Close, 3f - Gap);
        Assert.Equal(3.5f, result.Position.Z, 4);
    }

    [Fact]
    public void Stops_in_a_corner()
    {
        var world = new Blocks()
            .Fill(3, -5, -5, 3, 5, 5)   // wall at x = 3
            .Fill(-5, -5, 3, 5, 5, 3);  // wall at z = 3
        var start = new Vector3(0.5f, 0.5f, 0.5f);

        var result = VoxelCollider.Move(start, Body, new Vector3(6, 0, 6), world.IsSolid);

        Assert.True(result.HitX && result.HitZ);
        Assert.InRange(result.Position.X + Body.X, 3f - Close, 3f - Gap);
        Assert.InRange(result.Position.Z + Body.Z, 3f - Close, 3f - Gap);
        Assert.False(VoxelCollider.Overlaps(result.Position, Body, world.IsSolid));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void A_twenty_block_move_does_not_tunnel_through_a_thin_wall(int direction)
    {
        var world = new Blocks().Fill(10 * direction, -3, -3, 10 * direction, 3, 3);
        var start = new Vector3(0.5f, 0.5f, 0.5f);

        var result = VoxelCollider.Move(start, Body, new Vector3(20 * direction, 0, 0), world.IsSolid);

        Assert.True(result.HitX);
        float face = direction > 0 ? 10f : -9f;
        float leading = direction > 0 ? result.Position.X + Body.X : result.Position.X - Body.X;
        Assert.InRange(Math.Abs(leading - face), Gap, Close);
        Assert.False(VoxelCollider.Overlaps(result.Position, Body, world.IsSolid));
    }

    [Fact]
    public void Works_at_negative_coordinates()
    {
        var world = new Blocks().Fill(-20, -11, -20, 0, -11, 0); // floor whose top face is y = -10
        var start = new Vector3(-7.25f, -6f, -3.75f);

        var down = VoxelCollider.Move(start, Body, new Vector3(-1, -10, -1), world.IsSolid);

        Assert.True(down.HitY);
        Assert.InRange(down.Position.Y - Body.Y, -10f + Gap, -10f + Close);
        Assert.Equal(-8.25f, down.Position.X, 4);
        Assert.Equal(-4.75f, down.Position.Z, 4);

        var wallWorld = new Blocks().Fill(-6, -3, -3, -6, 3, 3); // occupies [-6,-5) in x
        var left = VoxelCollider.Move(new Vector3(-2.5f, 0.5f, 0.5f), Body, new Vector3(-10, 0, 0), wallWorld.IsSolid);
        Assert.True(left.HitX);
        Assert.InRange(left.Position.X - Body.X, -5f + Gap, -5f + Close);
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(-2, 0)]
    [InlineData(0, 2)]
    [InlineData(0, -2)]
    public void A_box_inside_rock_still_moves_out_sideways(int dx, int dz)
    {
        var world = new Blocks().Fill(0, 0, 0, 0, 1, 0); // the 1×2×1 column the box sits in
        var start = new Vector3(0.5f, 1f, 0.5f);
        Assert.True(VoxelCollider.Overlaps(start, Body, world.IsSolid));

        var result = VoxelCollider.Move(start, Body, new Vector3(dx, 0, dz), world.IsSolid);

        Assert.False(result.HitAny);
        Assert.Equal(start + new Vector3(dx, 0, dz), result.Position);
        Assert.False(VoxelCollider.Overlaps(result.Position, Body, world.IsSolid));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void A_box_with_a_block_in_its_body_leaves_it_away_from_the_block_only(int direction)
    {
        // One block in the lower or upper half of the body (y 0.1..1.9): moving away frees the box,
        // moving towards the block's far side would push it deeper and is refused.
        int cell = direction > 0 ? 0 : 1;
        var world = new Blocks().Fill(0, cell, 0, 0, cell, 0);
        var start = new Vector3(0.5f, 1f, 0.5f);

        var away = VoxelCollider.Move(start, Body, new Vector3(0, 3 * direction, 0), world.IsSolid);
        Assert.False(away.HitY);
        Assert.Equal(1f + 3 * direction, away.Position.Y, 4);

        var into = VoxelCollider.Move(start, Body, new Vector3(0, -3 * direction, 0), world.IsSolid);
        Assert.True(into.HitY);
        Assert.Equal(start, into.Position);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void A_box_a_hair_inside_a_thin_wall_does_not_pass_through_it(int direction)
    {
        var world = new Blocks().Fill(5, -5, -5, 5, 5, 5); // occupies [5,6) in x
        // Leading face 0.002 into the wall, from either side.
        float x = direction > 0 ? 5.002f - Body.X : 5.998f + Body.X;
        var start = new Vector3(x, 0.5f, 0.5f);

        var result = VoxelCollider.Move(start, Body, new Vector3(20 * direction, 0, 0), world.IsSolid);

        Assert.True(result.HitX);
        Assert.Equal(start.X, result.Position.X);
    }

    [Fact]
    public void A_box_a_hair_inside_the_floor_does_not_sink_into_it()
    {
        var world = new Blocks().Fill(-5, -3, -5, 5, 0, 5); // top face at y = 1
        var start = new Vector3(0.5f, 0.998f + Body.Y, 0.5f);

        var result = VoxelCollider.Move(start, Body, new Vector3(0, -2, 0), world.IsSolid);

        Assert.True(result.HitY);
        Assert.Equal(start.Y, result.Position.Y);
        Assert.False(VoxelCollider.Move(start, Body, new Vector3(0, 1, 0), world.IsSolid).HitAny);
    }

    [Fact]
    public void A_box_buried_in_solid_rock_returns_from_move()
    {
        var world = new Blocks().Fill(-10, -10, -10, 10, 10, 10);
        var start = new Vector3(0.5f, 0.5f, 0.5f);

        var result = VoxelCollider.Move(start, Body, new Vector3(3, 3, 3), world.IsSolid);

        Assert.True(result.HitX && result.HitY && result.HitZ);
        Assert.True(world.Queries < 1000);
    }

    [Fact]
    public void Non_finite_delta_is_ignored()
    {
        var world = new Blocks();
        var start = new Vector3(1.5f, 2f, 3.5f);

        var result = VoxelCollider.Move(start, Body, new Vector3(float.NaN, float.PositiveInfinity, 1), world.IsSolid);

        Assert.Equal(new Vector3(1.5f, 2f, 4.5f), result.Position);
    }

    [Fact]
    public void Touching_a_face_is_not_an_overlap()
    {
        var world = new Blocks().Fill(0, 0, 0, 0, 0, 0);

        Assert.False(VoxelCollider.Overlaps(new Vector3(0.5f, 1.9f, 0.5f), Body, world.IsSolid));
        Assert.False(VoxelCollider.Overlaps(new Vector3(1.3f, 0.5f, 0.5f), Body, world.IsSolid));
        Assert.True(VoxelCollider.Overlaps(new Vector3(1.29f, 0.5f, 0.5f), Body, world.IsSolid));
        Assert.True(VoxelCollider.Overlaps(new Vector3(0.5f, 1.85f, 0.5f), Body, world.IsSolid));
    }

    [Fact]
    public void Fits_through_a_one_block_corridor()
    {
        // A 1-wide, 2-high corridor along Z; the body (0.6 × 1.8) fits with room to spare.
        var world = new Blocks()
            .Fill(-1, 0, 0, 1, 0, 20)
            .Fill(-1, 3, 0, 1, 3, 20)
            .Fill(-1, 1, 0, -1, 2, 20)
            .Fill(1, 1, 0, 1, 2, 20);
        var start = new Vector3(0.5f, 2f, -3f);

        var result = VoxelCollider.Move(start, Body, new Vector3(0, 0, 15), world.IsSolid);

        Assert.False(result.HitAny);
        Assert.Equal(12f, result.Position.Z, 4);
    }

    [Fact]
    public void Resolves_x_before_z_when_passing_a_block_corner()
    {
        // A single column diagonally ahead. X first slips past it along x and then hits it moving in z;
        // Z first would do the opposite.
        var world = new Blocks().Fill(2, -5, 2, 2, 5, 2);
        var start = new Vector3(1.5f, 0.5f, 1.5f);

        var result = VoxelCollider.Move(start, Body, new Vector3(1, 0, 1), world.IsSolid);

        Assert.False(result.HitX);
        Assert.True(result.HitZ);
        Assert.Equal(2.5f, result.Position.X, 4);
        Assert.InRange(result.Position.Z + Body.Z, 2f - Close, 2f - Gap);
    }

    [Fact]
    public void Vertical_moves_check_the_right_column()
    {
        var world = new Blocks().Fill(3, 0, 0, 3, 0, 0); // one block, asymmetric in x and z

        var onTop = VoxelCollider.Move(new Vector3(3.5f, 3f, 0.5f), Body, new Vector3(0, -5, 0), world.IsSolid);
        Assert.True(onTop.HitY);
        Assert.InRange(onTop.Position.Y - Body.Y, 1f + Gap, 1f + Close);

        var beside = VoxelCollider.Move(new Vector3(0.5f, 3f, 3.5f), Body, new Vector3(0, -5, 0), world.IsSolid);
        Assert.False(beside.HitY);
        Assert.Equal(-2f, beside.Position.Y, 4);
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(0f)]
    [InlineData(-0.0004f)]
    public void A_point_sized_box_on_a_cell_boundary_still_collides(float y)
    {
        var world = new Blocks().Fill(5, -5, -5, 5, 5, 5);

        var result = VoxelCollider.Move(new Vector3(0.5f, y, 0.5f), Vector3.Zero, new Vector3(10, 0, 0), world.IsSolid);

        Assert.True(result.HitX);
        Assert.InRange(result.Position.X, 5f - Close, 5f - Gap);
    }

    [Theory]
    [InlineData(2.2e9f)]
    [InlineData(-2.2e9f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Move_returns_for_coordinates_beyond_int_range(float x)
    {
        var world = new Blocks();
        var start = new Vector3(x, 0.5f, 0.5f);

        var result = VoxelCollider.Move(start, Body, new Vector3(1, 1, 1), world.IsSolid);

        Assert.False(result.HitAny);
        Assert.False(VoxelCollider.Overlaps(start, Body, world.IsSolid));
        Assert.True(world.Queries < 1000);
    }

    [Fact]
    public void FindFreeSpot_returns_the_same_place_when_already_free()
    {
        var world = new Blocks();
        var start = new Vector3(4.5f, 5f, 6.5f);

        Assert.Equal(start, VoxelCollider.FindFreeSpot(start, Body, world.IsSolid));
    }

    [Fact]
    public void FindFreeSpot_escapes_a_solid_region_upwards()
    {
        // A wide slab: leaving through the top or the bottom takes 4 blocks, through a side 11.
        var world = new Blocks().Fill(-10, -3, -10, 10, 2, 10);
        var start = new Vector3(0.5f, 0f, 0.5f);

        var spot = VoxelCollider.FindFreeSpot(start, Body, world.IsSolid);

        Assert.NotNull(spot);
        Assert.False(VoxelCollider.Overlaps(spot.Value, Body, world.IsSolid));
        Assert.Equal(new Vector3(0.5f, 4f, 0.5f), spot.Value);
    }

    [Fact]
    public void FindFreeSpot_takes_a_nearer_side_exit_over_a_long_climb()
    {
        // A tall solid column, but open space one block to the +X side of the body.
        var world = new Blocks().Fill(-2, -10, -2, 0, 10, 2);
        var start = new Vector3(0.5f, 0f, 0.5f);

        var spot = VoxelCollider.FindFreeSpot(start, Body, world.IsSolid);

        Assert.Equal(new Vector3(1.5f, 0f, 0.5f), spot);
    }

    [Theory]
    [InlineData(4, true)]
    [InlineData(3, false)]
    public void FindFreeSpot_includes_a_spot_exactly_maxRadius_away(int maxRadius, bool found)
    {
        // Leaving upwards takes exactly 4 blocks; every other way out takes at least 10.
        var world = new Blocks().Fill(-10, -10, -10, 10, 2, 10);
        var start = new Vector3(0.5f, 0f, 0.5f);

        var spot = VoxelCollider.FindFreeSpot(start, Body, world.IsSolid, maxRadius);

        Assert.Equal(found ? new Vector3(0.5f, 4f, 0.5f) : null, spot);
    }

    [Fact]
    public void FindFreeSpot_prefers_the_higher_of_two_equally_near_spots()
    {
        // Solid rock with two body-sized pockets at the same distance: one up and to +X, one down and to -X.
        var world = new Blocks().Fill(-10, -10, -10, 10, 10, 10)
            .Clear(1, 1, 0).Clear(1, 2, 0)
            .Clear(-1, -1, 0).Clear(-1, 0, 0);
        var start = new Vector3(0.5f, 1f, 0.5f); // occupies cells y = 0 and 1

        var spot = VoxelCollider.FindFreeSpot(start, Body, world.IsSolid);

        Assert.Equal(new Vector3(1.5f, 2f, 0.5f), spot);
    }

    [Fact]
    public void FindFreeSpot_gives_up_beyond_the_radius()
    {
        var world = new Blocks().Fill(-10, -10, -10, 10, 10, 10);

        Assert.Null(VoxelCollider.FindFreeSpot(new Vector3(0.5f, 0.5f, 0.5f), Body, world.IsSolid, maxRadius: 4));
    }
}
