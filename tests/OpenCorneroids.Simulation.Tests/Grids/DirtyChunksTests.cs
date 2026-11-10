using OpenCorneroids.Simulation.Grids;
using OpenCorneroids.Simulation.Settings;

namespace OpenCorneroids.Simulation.Tests.Grids;

public class DirtyChunksTests
{
    static readonly Func<int, float> NoDistance = _ => 0;

    [Fact]
    public void A_chunk_marked_many_times_is_built_once()
    {
        var dirty = new DirtyChunks(8);
        Assert.True(dirty.Mark(3));
        Assert.False(dirty.Mark(3));
        Assert.True(dirty.Mark(5));
        Assert.False(dirty.Mark(3));

        var taken = new List<int>();
        dirty.Take(int.MaxValue, NoDistance, taken);

        Assert.Equal([3, 5], taken);
        Assert.Equal(0, dirty.Count);
        Assert.False(dirty.IsDirty(3));
        Assert.True(dirty.Mark(3));   // and can be marked again after it was built
    }

    [Fact]
    public void Chunks_outside_the_grid_are_ignored()
    {
        var dirty = new DirtyChunks(4);
        Assert.False(dirty.Mark(-1));
        Assert.False(dirty.Mark(4));
        Assert.False(dirty.IsDirty(-1));
        Assert.False(dirty.IsDirty(99));
        Assert.Equal(0, dirty.Count);
    }

    [Fact]
    public void Without_a_limit_chunks_come_out_in_the_order_they_were_marked()
    {
        var dirty = new DirtyChunks(10);
        foreach (int slot in new[] { 7, 2, 9, 0 }) dirty.Mark(slot);
        var taken = new List<int>();

        dirty.Take(4, slot => -slot, taken);

        Assert.Equal([7, 2, 9, 0], taken);
    }

    [Fact]
    public void Under_a_limit_the_nearest_go_first_and_the_rest_wait()
    {
        var dirty = new DirtyChunks(10);
        foreach (int slot in new[] { 7, 2, 9, 0, 4 }) dirty.Mark(slot);
        float[] distance = [5, 0, 1, 0, 3, 0, 0, 9, 0, 1];
        var taken = new List<int>();

        dirty.Take(2, slot => distance[slot], taken);
        Assert.Equal([2, 9], taken);          // both 1 away: equal distances keep the queue order
        Assert.Equal(3, dirty.Count);
        Assert.False(dirty.IsDirty(2));
        Assert.True(dirty.IsDirty(7));

        dirty.Take(2, slot => distance[slot], taken);
        Assert.Equal([4, 0], taken);          // 3 and 5 away

        dirty.Take(2, slot => distance[slot], taken);
        Assert.Equal([7], taken);
        Assert.Equal(0, dirty.Count);
    }

    [Fact]
    public void Equal_distances_keep_queue_order_and_unknown_ones_go_last()
    {
        var dirty = new DirtyChunks(6);
        foreach (int slot in new[] { 5, 1, 3, 2 }) dirty.Mark(slot);
        var taken = new List<int>();

        dirty.Take(3, slot => slot == 1 ? float.NaN : 1, taken);

        Assert.Equal([5, 3, 2], taken);
    }

    [Fact]
    public void Clearing_forgets_the_queue()
    {
        var dirty = new DirtyChunks(4);
        dirty.Mark(1);
        dirty.Mark(2);
        dirty.Clear();
        Assert.Equal(0, dirty.Count);
        Assert.False(dirty.IsDirty(1));
        Assert.True(dirty.Mark(1));
    }

    [Fact]
    public void A_queue_taken_a_little_each_frame_keeps_its_order_and_marks()
    {
        var dirty = new DirtyChunks(20);
        for (int slot = 19; slot >= 0; slot--) dirty.Mark(slot);
        var taken = new List<int>();

        dirty.Take(3, slot => slot, taken);
        Assert.Equal([0, 1, 2], taken);
        Assert.Equal(17, dirty.Count);
        Assert.True(dirty.Mark(1));           // built, so it can be marked again
        Assert.False(dirty.Mark(5));          // still waiting

        dirty.Take(int.MaxValue, _ => 0, taken);
        Assert.Equal([19, 18, 17, 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 1], taken);
    }

    [Fact]
    public void Without_a_limit_every_grid_is_flushed_whole_in_mark_order()
    {
        var world = new DirtyChunks(8);
        var ship = new DirtyChunks(8);
        world.Mark(4); world.Mark(1);
        ship.Mark(7);
        var taken = new List<(int, int)>();

        new RemeshScheduler().Take([world, ship], 0, (_, _) => 0, taken);

        Assert.Equal([(0, 4), (0, 1), (1, 7)], taken);
        Assert.Equal(0, world.Count + ship.Count);
    }

    [Fact]
    public void The_shared_limit_goes_to_the_nearest_chunks_of_any_grid()
    {
        var world = new DirtyChunks(8);
        var ship = new DirtyChunks(8);
        foreach (int slot in new[] { 0, 1, 2, 3 }) world.Mark(slot);
        ship.Mark(5);
        ship.Mark(6);
        float[][] distance = [[10, 11, 12, 13, 0, 0, 0, 0], [0, 0, 0, 0, 0, 2, 1, 0]];
        var scheduler = new RemeshScheduler();
        var taken = new List<(int, int)>();

        scheduler.Take([world, ship], 1, (q, slot) => distance[q][slot], taken);
        Assert.Equal([(1, 6)], taken);        // the busy world comes first in the list, but the ship is nearer

        scheduler.Take([world, ship], 2, (q, slot) => distance[q][slot], taken);
        Assert.Equal([(1, 5), (0, 0)], taken);
        Assert.Equal(3, world.Count);
        Assert.Equal(0, ship.Count);
    }

    [Fact]
    public void Equal_distances_go_by_grid_then_queue_order()
    {
        var world = new DirtyChunks(8);
        var ship = new DirtyChunks(8);
        world.Mark(3); world.Mark(2);
        ship.Mark(1); ship.Mark(0);
        var taken = new List<(int, int)>();

        new RemeshScheduler().Take([world, ship], 3, (q, slot) => q == 0 && slot == 3 ? float.NaN : 1, taken);

        Assert.Equal([(0, 2), (1, 1), (1, 0)], taken);
        Assert.True(world.IsDirty(3));
    }

    [Fact]
    public void The_grids_are_flushed_once_per_frame()
    {
        var scheduler = new RemeshScheduler();
        Assert.True(scheduler.StartFrame(10));
        Assert.False(scheduler.StartFrame(10));   // a second grid asking in the same frame
        Assert.True(scheduler.StartFrame(11));
    }

    [Fact]
    public void The_setting_defaults_to_every_changed_chunk_each_frame()
    {
        Assert.Equal("cl_remesh_per_frame", Cvars.RemeshPerFrame.Name);
        Assert.Equal(0, Cvars.RemeshPerFrame.Default);
        Assert.Contains(Cvars.RemeshPerFrame, Cvars.All);
        Assert.False(Cvars.RemeshPerFrame.Flags.HasFlag(CvarFlags.World));
    }
}
