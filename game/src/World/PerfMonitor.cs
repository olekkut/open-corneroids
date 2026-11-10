using Godot;
using OpenCorneroids.Simulation.Debugging;

namespace OpenCorneroids.Game.World;

/// <summary>
/// Ours: what the F3 readout (<c>--debug-hud</c>) shows about performance, so every performance change has a before
/// and after number. The engine's own counters (draw calls, objects drawn and nodes in the scene, read from
/// <see cref="Performance"/>) and our section timers (<see cref="FrameTimers"/>): ship physics, shots, creatures and
/// chunk remeshing, each as average and worst milliseconds per frame. The sections are exclusive: the remeshing a shot
/// or a creature causes by breaking a block counts as remesh, not as shots or creatures. One set for the whole game:
/// the world view closes each frame (<see cref="WorldView"/>), and the timed sections add to it wherever they run.
/// </summary>
public static class PerfMonitor
{
    public static readonly FrameTimers Timers = new();

    /// <summary>Ship physics: thrust, motion, collisions and riders (<see cref="ShipPhysics.Advance"/>).</summary>
    public static readonly int Ships = Timers.Register("ships");
    /// <summary>Every shot in flight: its path, what it hits, damage (<see cref="Projectiles"/>).</summary>
    public static readonly int Shots = Timers.Register("shots");
    /// <summary>Creature AI, spawning, pushing out of blocks (<see cref="CreatureSystem"/>).</summary>
    public static readonly int Creatures = Timers.Register("creatures");
    /// <summary>Chunk meshes built again after an edit, with how many per frame.</summary>
    public static readonly int Remesh = Timers.Register("remesh", showCalls: true);

    /// <summary>Times one run of a section until the scope is disposed: <c>using var _ = PerfMonitor.Measure(...)</c>.</summary>
    public static FrameTimers.Scope Measure(int section) => Timers.Measure(section);

    /// <summary>The readout: engine counters for the last frame, then the timers of the last window.</summary>
    public static string Readout()
    {
        double draws = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        double objects = Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame);
        double primitives = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
        double nodes = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
        double process = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000;
        return FormattableString.Invariant(
                   $"draws {draws:0}   objects {objects:0}   primitives {primitives:0}   nodes {nodes:0}   process {process:0.0} ms   (sections: ms per frame, average/worst, each without the others)\n") +
               FrameTimers.Describe(Timers.WindowFrame, Timers.Window);
    }

    /// <summary>The run since <see cref="FrameTimers.ResetRun"/>, for the log at the end of a timed run; the engine's
    /// draw and node counts are the last frame's, not the run's.</summary>
    public static string RunSummary() =>
        FormattableString.Invariant($"{Timers.RunFrame.Fps:0} FPS over {Timers.RunFrame.Frames} frames; ") +
        FrameTimers.Describe(Timers.RunFrame, Timers.Run) +
        FormattableString.Invariant($"   last frame: draws {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame):0}") +
        FormattableString.Invariant($", nodes {Performance.GetMonitor(Performance.Monitor.ObjectNodeCount):0}");
}
