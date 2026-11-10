using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace OpenCorneroids.Simulation.Debugging;

/// <summary>What one timed section cost per frame: on average, in its worst frame, and how often it ran.</summary>
/// <param name="Name">The section's name as registered, e.g. "shots".</param>
/// <param name="AverageMs">Milliseconds per frame, averaged over the frames measured.</param>
/// <param name="MaxMs">Milliseconds in the most expensive of those frames.</param>
/// <param name="CallsPerFrame">Measurements per frame, averaged (e.g. chunks remeshed per frame).</param>
/// <param name="ShowCalls">Whether <see cref="FrameTimers.Describe"/> prints <paramref name="CallsPerFrame"/>.</param>
public readonly record struct SectionTiming(string Name, double AverageMs, double MaxMs, double CallsPerFrame, bool ShowCalls);

/// <summary>Whole-frame time over the frames measured: the time from one <see cref="FrameTimers.EndFrame"/> to the next.</summary>
public readonly record struct FrameTiming(int Frames, double AverageMs, double MaxMs)
{
    /// <summary>Frames per second from <see cref="AverageMs"/>; 0 before any frame was measured.</summary>
    public double Fps => AverageMs > 0 ? 1000 / AverageMs : 0;
}

/// <summary>
/// Ours: the section timers of the F3 readout (<c>--debug-hud</c>), so every performance change can be measured
/// before and after. A section (ship physics, shots, creatures, chunk remeshing) adds the time each run of it took
/// with <see cref="Measure"/>; once per frame <see cref="EndFrame"/> closes the frame. The frames are summed twice:
/// into a short window (<see cref="WindowSeconds"/>, the readout, refreshed when the window closes) and into a run
/// total since <see cref="ResetRun"/> (what a timed benchmark run prints at its end). Sections are exclusive: when one
/// runs inside another (a shot breaks a block, which remeshes its chunk), its time counts only for the inner section
/// and is taken out of the outer one, so the sections add up and none is counted twice. Kept free of the engine, with
/// the clock passed in, so it can be tested; used from the main thread only.
/// </summary>
public sealed class FrameTimers
{
    /// <summary>How long the readout averages over before it shows new numbers; the F3 FPS counter uses the same.</summary>
    public const double DefaultWindowSeconds = 0.5;

    sealed class Section(string name, bool showCalls)
    {
        public readonly string Name = name;
        public readonly bool ShowCalls = showCalls;
        public Totals Frame, Window, Run;
    }

    /// <summary>Sums of one section, or of whole frames, over some frames.</summary>
    struct Totals
    {
        public long Ticks, MaxTicks, Calls;

        public void Add(long ticks, long calls)
        {
            Ticks += ticks;
            MaxTicks = Math.Max(MaxTicks, ticks);
            Calls += calls;
        }
    }

    readonly Func<long> _clock;
    readonly double _ticksPerMs;
    readonly long _windowTicks;
    readonly List<Section> _sections = [];
    /// <summary>For each scope still open, innermost last: the time of the scopes that ran inside it.</summary>
    readonly List<long> _open = [];
    long _lastEnd = -1;
    bool _skipFrame;
    long _windowStart;
    int _windowFrames, _runFrames;
    Totals _windowFrame, _runFrame;
    IReadOnlyList<SectionTiming> _window = [];

    /// <param name="clock">Current time in ticks; <see cref="Stopwatch.GetTimestamp"/> when left out.</param>
    /// <param name="ticksPerSecond">The clock's ticks per second; <see cref="Stopwatch.Frequency"/> when left out.</param>
    /// <param name="windowSeconds">See <see cref="WindowSeconds"/>.</param>
    public FrameTimers(Func<long>? clock = null, long ticksPerSecond = 0, double windowSeconds = DefaultWindowSeconds)
    {
        if (windowSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(windowSeconds), "The window must be longer than 0 s.");
        _clock = clock ?? Stopwatch.GetTimestamp;
        long frequency = ticksPerSecond > 0 ? ticksPerSecond : Stopwatch.Frequency;
        _ticksPerMs = frequency / 1000.0;
        _windowTicks = (long)(frequency * windowSeconds);
        WindowSeconds = windowSeconds;
    }

    /// <summary>How many seconds of frames <see cref="Window"/> averages over.</summary>
    public double WindowSeconds { get; }

    /// <summary>The sections of the last closed window, in the order they were registered.</summary>
    public IReadOnlyList<SectionTiming> Window => _window;

    /// <summary>Whole-frame time of the last closed window.</summary>
    public FrameTiming WindowFrame { get; private set; }

    /// <summary>Adds a section to time; returns its handle for <see cref="Measure"/> and <see cref="Record"/>.</summary>
    /// <param name="showCalls">Show how often it ran per frame (e.g. chunks remeshed), not just its time.</param>
    public int Register(string name, bool showCalls = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _sections.Add(new Section(name, showCalls));
        _window = [];
        return _sections.Count - 1;
    }

    /// <summary>Starts timing one run of a section; the run ends when the returned scope is disposed
    /// (<c>using var _ = timers.Measure(section);</c>). A struct, so timing allocates nothing. Scopes opened while it
    /// is open count for their own sections, not this one.</summary>
    public Scope Measure(int section)
    {
        _open.Add(0);
        return new(this, section, _clock());
    }

    /// <summary>One run of a section, being timed.</summary>
    public readonly struct Scope : IDisposable
    {
        readonly FrameTimers? _timers;
        readonly int _section;
        readonly long _start;

        internal Scope(FrameTimers timers, int section, long start) => (_timers, _section, _start) = (timers, section, start);

        public void Dispose() => _timers?.Close(_section, _start);
    }

    /// <summary>Ends the innermost open scope: its own time (less the scopes inside it) goes to its section, and all of
    /// it is taken out of the scope around it.</summary>
    void Close(int section, long start)
    {
        long elapsed = Math.Max(0, _clock() - start);
        int top = _open.Count - 1;
        if (top < 0)
        {
            Record(section, elapsed);
            return;
        }
        long nested = _open[top];
        _open.RemoveAt(top);
        Record(section, elapsed - nested);
        if (top > 0) _open[top - 1] += elapsed;
    }

    /// <summary>Adds one run of <paramref name="ticks"/> to a section in the current frame.</summary>
    public void Record(int section, long ticks)
    {
        ref var frame = ref _sections[section].Frame;
        frame.Ticks += Math.Max(0, ticks);
        frame.Calls++;
    }

    /// <summary>
    /// Closes the current frame: its section times go into the window and the run. Call it once per frame, at the same
    /// point each frame. The first call only starts the clock: what was timed before it (loading) is dropped, and so
    /// is the frame after <see cref="SkipFrame"/>. Returns true when this call also closed a window, so the readout only
    /// needs new text then.
    /// </summary>
    public bool EndFrame()
    {
        long now = _clock();
        if (_lastEnd < 0 || _skipFrame)
        {
            foreach (var s in _sections) s.Frame = default;
            if (_lastEnd < 0) _windowStart = now;
            _lastEnd = now;
            _skipFrame = false;
            return false;
        }
        long frameTicks = Math.Max(0, now - _lastEnd);
        _lastEnd = now;
        _windowFrame.Add(frameTicks, 1);
        _runFrame.Add(frameTicks, 1);
        foreach (var s in _sections)
        {
            s.Window.Add(s.Frame.Ticks, s.Frame.Calls);
            s.Run.Add(s.Frame.Ticks, s.Frame.Calls);
            s.Frame = default;
        }
        _windowFrames++;
        _runFrames++;
        if (now - _windowStart < _windowTicks) return false;

        WindowFrame = Timing(_windowFrame, _windowFrames);
        _window = [.. _sections.Select(s => Timing(s, s.Window, _windowFrames))];
        foreach (var s in _sections) s.Window = default;
        _windowFrame = default;
        _windowFrames = 0;
        _windowStart = now;
        return true;
    }

    /// <summary>Whole-frame time since <see cref="ResetRun"/> (or the first frame).</summary>
    public FrameTiming RunFrame => Timing(_runFrame, _runFrames);

    /// <summary>The sections since <see cref="ResetRun"/> (or the first frame).</summary>
    public IReadOnlyList<SectionTiming> Run => [.. _sections.Select(s => Timing(s, s.Run, _runFrames))];

    /// <summary>
    /// Leaves the frame now running out of the numbers: the next <see cref="EndFrame"/> only restarts the clock. For a
    /// world that was paused (a question asked over it), whose first frame back would otherwise span the whole pause.
    /// </summary>
    public void SkipFrame() => _skipFrame = true;

    /// <summary>Starts the run totals again, e.g. once the loading hitch is over.</summary>
    public void ResetRun()
    {
        foreach (var s in _sections) s.Run = default;
        _runFrame = default;
        _runFrames = 0;
    }

    /// <summary>Forgets everything measured (a new world starts): the next <see cref="EndFrame"/> only starts the clock.</summary>
    public void Restart()
    {
        foreach (var s in _sections) s.Frame = s.Window = s.Run = default;
        _windowFrame = _runFrame = default;
        _windowFrames = _runFrames = 0;
        _window = [];
        WindowFrame = default;
        _lastEnd = -1;
        _skipFrame = false;
    }

    FrameTiming Timing(Totals totals, int frames) =>
        frames == 0 ? default : new(frames, totals.Ticks / _ticksPerMs / frames, totals.MaxTicks / _ticksPerMs);

    SectionTiming Timing(Section s, Totals totals, int frames) =>
        frames == 0
            ? new(s.Name, 0, 0, 0, s.ShowCalls)
            : new(s.Name, totals.Ticks / _ticksPerMs / frames, totals.MaxTicks / _ticksPerMs, totals.Calls / (double)frames, s.ShowCalls);

    /// <summary>
    /// One line for the readout or the log: the frame time, then each section as average/worst milliseconds per frame,
    /// e.g. <c>frame 16.7/21.0 ms   ships 0.42/0.90   remesh 0.85/3.10 (12.0/frame)</c>.
    /// </summary>
    public static string Describe(FrameTiming frame, IReadOnlyList<SectionTiming> sections)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"frame {frame.AverageMs:0.0}/{frame.MaxMs:0.0} ms");
        foreach (var s in sections)
        {
            text.Append(CultureInfo.InvariantCulture, $"   {s.Name} {s.AverageMs:0.00}/{s.MaxMs:0.00}");
            if (s.ShowCalls) text.Append(CultureInfo.InvariantCulture, $" ({s.CallsPerFrame:0.0}/frame)");
        }
        return text.ToString();
    }
}
