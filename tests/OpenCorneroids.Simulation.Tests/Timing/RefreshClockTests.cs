using OpenCorneroids.Simulation.Timing;

namespace OpenCorneroids.Simulation.Tests.Timing;

public class RefreshClockTests
{
    [Fact]
    public void Due_on_the_first_frame_then_ten_times_a_second()
    {
        var clock = new RefreshClock(0.1);
        int due = 0;
        for (int frame = 0; frame < 60; frame++)
            if (clock.Due(1 / 60.0)) due++;

        Assert.InRange(due, 10, 11);
    }

    [Fact]
    public void A_long_frame_makes_it_due_once()
    {
        var clock = new RefreshClock(0.1);
        Assert.True(clock.Due(0.016));
        Assert.True(clock.Due(2.0));
        Assert.False(clock.Due(0.016));
    }

    [Fact]
    public void Restart_makes_the_next_frame_due()
    {
        var clock = new RefreshClock(0.1);
        Assert.True(clock.Due(0.016));
        Assert.False(clock.Due(0.016));

        clock.Restart();

        Assert.True(clock.Due(0.016));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void No_interval_makes_every_frame_due(double interval)
    {
        var clock = new RefreshClock(interval);
        for (int frame = 0; frame < 5; frame++) Assert.True(clock.Due(0.016));
    }

    [Fact]
    public void Odd_frame_lengths_count_as_nothing()
    {
        var clock = new RefreshClock(0.1);
        Assert.True(clock.Due(0.016));
        Assert.False(clock.Due(double.NaN));
        Assert.False(clock.Due(-5));
        Assert.False(clock.Due(double.PositiveInfinity));
    }
}
