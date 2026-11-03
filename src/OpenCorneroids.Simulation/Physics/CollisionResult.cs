using System.Numerics;

namespace OpenCorneroids.Simulation.Physics;

/// <summary>Where a moved box ended up, and on which axes it was stopped, so callers can cancel velocity
/// only along the blocked axes and keep sliding along the others.</summary>
public readonly record struct CollisionResult(Vector3 Position, bool HitX, bool HitY, bool HitZ)
{
    public bool HitAny => HitX || HitY || HitZ;
}
