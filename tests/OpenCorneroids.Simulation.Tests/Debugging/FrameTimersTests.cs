using OpenCorneroids.Simulation.Debugging;

namespace OpenCorneroids.Simulation.Tests.Debugging;

/// <summary>The F3 readout's section timers, driven by a hand-wound clock of 1000 ticks per second (1 tick = 1 ms).</summary>
public class FrameTimersTests
{
    long _now;
    FrameTimers New(double window = 0.5) => new(() => _now, ticksPerSecond: 1000, windowSeconds: window);

    void Spend(FrameTimers timers, int section, long ms)
    {
        using var _ = timers.Measure(section);
        _now += ms;
    }

    [Fact]
    public void The_first_frame_only_starts_the_clock_and_drops_what_was_timed_while_loading()
    {
        var timers = New();
        int remesh = timers.Register("remesh", showCalls: true);
        Spend(timers, remesh, 400);   // the load builds every chunk

        Assert.False(timers.EndFrame());
        Assert.Equal(0, timers.RunFrame.Frames);
        Assert.Equal(0, timers.Run[remesh].AverageMs);
    }

    [Fact]
    public void A_window_averages_each_section_per_frame_and_keeps_its_worst_frame()
    {
        var timers = New(window: 0.1);
        int ships = timers.Register("ships");
        int shots = timers.Register("shots");
        timers.EndFrame();

        // Four 25 ms frames: ships take 2, 4, 6 and 8 ms; shots run only in the last frame, for 3 ms.
        bool closed = false;
        for (int frame = 1; frame <= 4; frame++)
        {
            Spend(timers, ships, frame * 2);
            if (frame == 4) Spend(timers, shots, 3);
            _now += 25 - frame * 2 - (frame == 4 ? 3 : 0);
            closed = timers.EndFrame();
            if (frame < 4) Assert.False(closed);
        }

        Assert.True(closed);
        Assert.Equal(new FrameTiming(4, 25, 25), timers.WindowFrame);
        Assert.Equal(40, timers.WindowFrame.Fps, 6);
        Assert.Equal(new SectionTiming("ships", 5, 8, 1, false), timers.Window[ships]);
        Assert.Equal(new SectionTiming("shots", 0.75, 3, 0.25, false), timers.Window[shots]);
    }

    [Fact]
    public void Calls_are_counted_per_frame_and_several_runs_in_one_frame_add_up()
    {
        var timers = New(window: 0.01);
        int remesh = timers.Register("remesh", showCalls: true);
        timers.EndFrame();

        for (int i = 0; i < 6; i++) Spend(timers, remesh, 1);
        _now += 10;
        Assert.True(timers.EndFrame());

        var timing = timers.Window[remesh];
        Assert.Equal(6, timing.AverageMs);
        Assert.Equal(6, timing.MaxMs);   // the worst frame, not the worst single run
        Assert.Equal(6, timing.CallsPerFrame);
    }

    [Fact]
    public void The_run_totals_span_windows_until_reset()
    {
        var timers = New(window: 0.02);
        int creatures = timers.Register("creatures");
        timers.EndFrame();
        for (int frame = 0; frame < 10; frame++)
        {
            Spend(timers, creatures, frame == 7 ? 9 : 1);
            _now += 9;
            timers.EndFrame();
        }

        Assert.Equal(10, timers.RunFrame.Frames);
        Assert.Equal(1.8, timers.Run[creatures].AverageMs, 6);
        Assert.Equal(9, timers.Run[creatures].MaxMs);
        Assert.Equal(18, timers.RunFrame.MaxMs);

        timers.ResetRun();
        Assert.Equal(0, timers.RunFrame.Frames);
        Spend(timers, creatures, 2);
        _now += 8;
        timers.EndFrame();
        Assert.Equal(new SectionTiming("creatures", 2, 2, 1, false), timers.Run[creatures]);
    }

    [Fact]
    public void Restart_forgets_everything_so_a_new_world_starts_clean()
    {
        var timers = New(window: 0.01);
        int ships = timers.Register("ships");
        timers.EndFrame();
        Spend(timers, ships, 5);
        _now += 20;
        timers.EndFrame();

        timers.Restart();
        _now += 5000;   // the next world's load
        Assert.False(timers.EndFrame());
        Assert.Empty(timers.Window);
        Assert.Equal(0, timers.RunFrame.Frames);
        _now += 10;
        timers.EndFrame();
        Assert.Equal(10, timers.RunFrame.MaxMs);
    }

    [Fact]
    public void A_section_run_inside_another_counts_only_for_itself()
    {
        var timers = New(window: 0.01);
        int shots = timers.Register("shots");
        int remesh = timers.Register("remesh", showCalls: true);
        timers.EndFrame();

        // A 10 ms shots pass breaks two blocks, each remeshing a chunk for 3 ms inside it.
        using (timers.Measure(shots))
        {
            _now += 2;
            Spend(timers, remesh, 3);
            _now += 1;
            Spend(timers, remesh, 3);
            _now += 1;
        }
        Spend(timers, remesh, 2);   // and the chunk an edit left for later, outside any other section
        _now += 8;
        Assert.True(timers.EndFrame());

        Assert.Equal(new SectionTiming("shots", 4, 4, 1, false), timers.Window[shots]);
        Assert.Equal(new SectionTiming("remesh", 8, 8, 3, true), timers.Window[remesh]);
        Assert.Equal(20, timers.WindowFrame.AverageMs);
    }

    [Fact]
    public void A_skipped_frame_leaves_a_pause_out_of_the_numbers()
    {
        var timers = New(window: 0.01);
        int ships = timers.Register("ships");
        timers.EndFrame();
        Spend(timers, ships, 2);
        _now += 8;
        timers.EndFrame();

        timers.SkipFrame();
        _now += 7000;   // a question over the paused world
        Assert.False(timers.EndFrame());
        Spend(timers, ships, 4);
        _now += 6;
        timers.EndFrame();

        Assert.Equal(new FrameTiming(2, 10, 10), timers.RunFrame);
        Assert.Equal(new SectionTiming("ships", 3, 4, 1, false), timers.Run[ships]);
    }

    [Fact]
    public void Describe_prints_average_and_worst_and_calls_only_where_asked()
    {
        var text = FrameTimers.Describe(new FrameTiming(30, 16.66, 21.04),
        [
            new SectionTiming("ships", 0.424, 0.9, 1, false),
            new SectionTiming("remesh", 0.85, 3.1, 12, true),
        ]);

        Assert.Equal("frame 16.7/21.0 ms   ships 0.42/0.90   remesh 0.85/3.10 (12.0/frame)", text);
    }

    [Fact]
    public void A_window_must_have_a_length()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameTimers(() => 0, 1000, windowSeconds: 0));
    }
}
