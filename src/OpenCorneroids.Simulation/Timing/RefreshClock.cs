namespace OpenCorneroids.Simulation.Timing;

/// <summary>
/// Says when a slowly changing readout (the HUD status line, marker labels) is due to be written again, so its text is
/// built a few times a second instead of every frame. Due on the first frame, then once per <see cref="Interval"/>;
/// a long frame makes it due once, not several times over. Ours.
/// </summary>
public sealed class RefreshClock
{
    double _wait;

    /// <param name="interval">Seconds between refreshes; zero or less makes every frame due.</param>
    public RefreshClock(double interval) => Interval = double.IsFinite(interval) ? Math.Max(0, interval) : 0;

    /// <summary>Seconds between refreshes.</summary>
    public double Interval { get; }

    /// <summary>Counts down <paramref name="delta"/> seconds; true when the readout should be written this frame.</summary>
    public bool Due(double delta)
    {
        if (double.IsFinite(delta) && delta > 0) _wait -= delta;
        if (_wait > 0) return false;
        _wait = Math.Max(0, _wait + Interval);
        if (_wait <= 0) _wait = Interval;
        return true;
    }

    /// <summary>Makes the next <see cref="Due"/> true whatever time is left (something the readout shows just changed).</summary>
    public void Restart() => _wait = 0;
}
