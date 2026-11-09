using System.Numerics;
using OpenCorneroids.Simulation.Shapes;

namespace OpenCorneroids.Simulation.Creatures;

/// <summary>
/// The mineral parasite, as in the original: it sits on an asteroid eating the block under it (20 s a block, no
/// drop), then looks for the next asteroid or mineral block within two cells and crawls to it at 0.3 u/s. A player who
/// comes within 4, or shoots it from within 16, makes it charge: a push of 10 for one second toward where the player
/// was, then coast, and charge again while the player is within 16. Its touch hurts 10, from its first charge on.
/// </summary>
public sealed class MineralParasite : Creature
{
    public const float CreatureRadius = 0.4f;
    public const int HitPoints = 20;
    public const int Bite = 10;
    public const double EatSeconds = 20;
    public const float CrawlSpeed = 0.3f;
    public const float NoticeDistance = 4;
    public const float ChaseDistance = 16;
    public const float ChargeForce = 10;
    public const double ChargeSeconds = 1;
    public const float CoastStop = 0.3f;

    enum State { Eating, Searching, Crawling, Charging, Coasting, Idle }

    State _state = State.Eating;
    Int3 _food;
    int _foodBlock;
    double _eaten;
    Vector3 _crawlTo;
    bool _crawlToFood;
    Vector3 _chargeDirection;
    double _charged;
    bool _armed;

    /// <param name="cell">The empty cell it sits in.</param>
    /// <param name="floor">The asteroid or mineral block it starts eating.</param>
    public MineralParasite(int grid, Int3 cell, Int3 floor, ICreatureWorld world, double eatSeconds = EatSeconds)
        : base(CreatureKind.MineralParasite, CreatureRadius, HitPoints, HitPoints, 500, grid, Center(cell))
    {
        EatTime = eatSeconds;
        _food = floor;
        _foodBlock = world.BlockAt(grid, floor);
        Facing = Vector3.Normalize(cell - floor);
    }

    /// <summary>Seconds to eat a block (<c>sv_parasite_eat_time</c>).</summary>
    public double EatTime { get; set; }

    public override int TouchDamage => _armed ? Bite : 0;
    public override bool CollidesWithBlocks => _state != State.Eating;
    public override float UpkeepDistance => 32;

    /// <summary>Charging or coasting after a charge: the wobble stops.</summary>
    public bool Attacking => _state is State.Charging or State.Coasting;

    public bool Eating => _state == State.Eating;

    protected override void Think(ICreatureWorld world)
    {
        if (!Attacking && PlayerDistance(world) <= NoticeDistance)
        {
            StartCharge(world);
            return;
        }
        switch (_state)
        {
            case State.Eating:
                if (world.BlockAt(Grid, _food) != _foodBlock || _foodBlock == 0) _state = State.Searching;
                else if (_eaten >= EatTime)
                {
                    world.EatBlock(Grid, _food);
                    _eaten = 0;
                    _state = State.Searching;
                }
                break;
            case State.Searching:
                Search(world);
                break;
            case State.Coasting:
                if (world.Player is null)
                {
                    HasTarget = false;
                    _state = State.Searching;
                }
                else if (Velocity.Length() <= CoastStop)
                {
                    if (PlayerDistance(world) <= ChaseDistance) StartCharge(world);
                    else _state = State.Idle;
                }
                break;
            case State.Charging:
                if (world.Player is null)
                {
                    HasTarget = false;
                    _state = State.Searching;
                }
                break;
        }
    }

    void Search(ICreatureWorld world)
    {
        var here = Int3.Floor(Position);
        int start = world.Random.Next(5);   // 0..4, as in the original
        for (int i = 0; i < Directions.Length; i++)
        {
            var dir = Directions[(start + i) % Directions.Length];
            for (int d = 1; d <= 2; d++)
            {
                var cell = here + dir * d;
                if (!world.IsFood(Grid, cell)) continue;
                var front = cell - dir;
                if (world.IsSolid(Grid, front)) continue;
                _crawlTo = Center(front);
                _crawlToFood = true;
                _food = cell;
                _state = State.Crawling;
                return;
            }
        }
        // Nothing to eat near: wander to a free neighbour (the last in scan order), then look again.
        Int3? free = null;
        foreach (var dir in Directions)
            if (!world.IsSolid(Grid, here + dir)) free = here + dir;
        if (free is { } next)
        {
            _crawlTo = Center(next);
            _crawlToFood = false;
            _state = State.Crawling;
        }
    }

    void StartCharge(ICreatureWorld world)
    {
        if (PlayerLocal(world) is not { } target) return;
        var toward = target - Position;
        if (toward.LengthSquared() < 1e-6f) return;
        HasTarget = true;
        _armed = true;
        _chargeDirection = Vector3.Normalize(toward);
        _charged = 0;
        _state = State.Charging;
    }

    protected override void Act(double dt, ICreatureWorld world)
    {
        switch (_state)
        {
            case State.Eating:
                _eaten += dt;
                break;
            case State.Crawling:
            {
                Velocity = Vector3.Zero;
                var to = _crawlTo - Position;
                float step = CrawlSpeed * (float)dt;
                if (to.Length() <= Math.Max(0.1f, step))
                {
                    Position = _crawlTo;
                    if (_crawlToFood)
                    {
                        _foodBlock = world.BlockAt(Grid, _food);
                        Facing = Vector3.Normalize(Position - Center(_food));
                        _state = State.Eating;
                    }
                    else _state = State.Searching;
                }
                else Position += Vector3.Normalize(to) * step;
                break;
            }
            case State.Charging:
                Push(_chargeDirection * ChargeForce);
                _charged += dt;
                if (_charged >= ChargeSeconds) _state = State.Coasting;
                break;
        }
    }

    protected override void Shot(bool byPlayer, float shooterDistance, ICreatureWorld world)
    {
        if (byPlayer && shooterDistance <= ChaseDistance && !Attacking) StartCharge(world);
    }
}

/// <summary>
/// The swarm egg, as in the original: it sits on an asteroid until the player comes within 5 or a shot hits it, then
/// hatches, oozing green goo, letting a needle out every second (three in all) and vanishing after five seconds.
/// </summary>
public sealed class SwarmEgg : Creature
{
    public const float CreatureRadius = 0.3f;
    public const int HitPoints = 5;
    public const float HatchDistance = 5;
    public const int Needles = 3;
    public const double NeedleSeconds = 1;
    public const double HatchSeconds = 5;
    public const double GooSeconds = 0.05;
    public const float NeedleSpeed = 7;

    bool _hatching;
    double _hatched;
    double _goo;
    int _released;

    public SwarmEgg(int grid, Int3 cell, Int3 floor)
        : base(CreatureKind.SwarmEgg, CreatureRadius, HitPoints, HitPoints, 500, grid, Center(cell))
    {
        Facing = Vector3.Normalize(cell - floor);
    }

    public override bool CollidesWithBlocks => false;
    public override float UpkeepDistance => 32;
    public bool Hatching => _hatching;

    protected override void Think(ICreatureWorld world)
    {
        if (!_hatching && PlayerDistance(world) <= HatchDistance) Hatch(target: true);
    }

    void Hatch(bool target)
    {
        if (_hatching) return;
        _hatching = true;
        HasTarget = target;
    }

    protected override void Act(double dt, ICreatureWorld world)
    {
        if (!_hatching) return;
        _hatched += dt;
        _goo += dt;
        var at = WorldPosition(world);
        var normal = Grid >= 0 ? world.DirectionToWorld(Grid, Facing) : Facing;
        while (_goo >= GooSeconds)
        {
            _goo -= GooSeconds;
            var r = world.Random;
            var jitter = new Vector3((float)(r.NextDouble() - 0.5) * 0.5f, (float)(r.NextDouble() - 0.5) * 0.5f, (float)(r.NextDouble() - 0.5) * 0.5f);
            world.Emit(CreatureEffect.Goo, at, (normal + jitter) * 3);
        }
        while (_released < Needles && _hatched >= (_released + 1) * NeedleSeconds)
        {
            _released++;
            world.Add(new Needle(at, normal * NeedleSpeed, HasTarget));
        }
        if (_hatched >= HatchSeconds) Remove();
    }

    protected override void Shot(bool byPlayer, float shooterDistance, ICreatureWorld world) => Hatch(target: byPlayer);
}

/// <summary>
/// A needle, hatched from a swarm egg, as in the original: it homes on the player, steering toward 7 u/s at them
/// every frame (top speed 10), stings for 5, and is gone after 4 s, on hitting a block, or without a target.
/// </summary>
public sealed class Needle : Creature
{
    public const float CreatureRadius = 0.2f;
    public const int Sting = 5;
    public const float HomingSpeed = 7;
    public const double Lifetime = 4;
    public const double TrailSeconds = 0.1;

    double _trail;

    public Needle(Vector3 world, Vector3 velocity, bool target)
        // 3 health of a maximum of 100 (so it regenerates), as in the original.
        : base(CreatureKind.Needle, CreatureRadius, 3, 100, 10_000, -1, world)
    {
        Velocity = velocity;
        HasTarget = target;
        if (velocity.LengthSquared() > 0) Facing = Vector3.Normalize(velocity);
    }

    public override int TouchDamage => Sting;
    public override float MaxSpeed => 10;
    public override float UpkeepDistance => 32;

    protected override void Think(ICreatureWorld world) { }

    protected override void Act(double dt, ICreatureWorld world)
    {
        if (!HasTarget || world.Player is not { } player || Age >= Lifetime)
        {
            Remove();
            return;
        }
        var to = player - Position;
        if (to.LengthSquared() > 1e-6f) Push(Vector3.Normalize(to) * HomingSpeed - Velocity);
        if (Velocity.LengthSquared() > 1e-6f) Facing = Vector3.Normalize(Velocity);
        _trail += dt;
        while (_trail >= TrailSeconds)
        {
            _trail -= TrailSeconds;
            var r = world.Random;
            world.Emit(CreatureEffect.Trail, Position,
                Velocity * 0.5f + new Vector3((float)r.NextDouble(), (float)r.NextDouble(), (float)r.NextDouble()));
        }
    }

    public override void HitBlock() => Remove();
}

/// <summary>
/// The scavenger flyer, as the original codes it (its 1.2.0 never spawns one; ours behind <c>sv_scavengers</c>): it
/// drifts about at 1 u/s until a player on foot comes within 24 (or anyone shoots it), then circles at about 16 units,
/// turning its aim at π/4 rad/s and firing a yellow-green bolt (speed 20, damage 5) every 2 s when on target.
/// </summary>
public sealed class Scavenger : Creature
{
    public const float CreatureRadius = 1;
    public const int HitPoints = 20;
    public const float WanderSpeed = 1;
    public const float NoticeDistance = 24;
    public const float KeepDistance = 16;
    public const float TurnRate = MathF.PI / 4;
    public const float FireCone = MathF.PI / 8;
    public const double Reload = 2;

    Vector3 _goal;
    bool _attacking;
    Vector3 _strafe;
    double _reload;

    public Scavenger(Vector3 world, Random random)
        : base(CreatureKind.Scavenger, CreatureRadius, HitPoints, HitPoints, 1000, -1, world)
    {
        _goal = NextGoal(random);
        Facing = Vector3.UnitZ;
    }

    public override float UpkeepDistance => 64;
    public bool Attacking => _attacking;

    /// <summary>Which way its gun points, world space.</summary>
    public Vector3 Aim => Facing;

    Vector3 NextGoal(Random r) =>
        Position + new Vector3((float)r.NextDouble(), (float)r.NextDouble(), (float)r.NextDouble()) * 10;

    protected override void Think(ICreatureWorld world)
    {
        if (!_attacking && !world.PlayerPiloting && PlayerDistance(world) <= NoticeDistance) StartAttack(world);
    }

    void StartAttack(ICreatureWorld world)
    {
        if (world.Player is not { } p) return;
        _attacking = true;
        HasTarget = true;
        var line = p - Position;
        var r = world.Random;
        var random = new Vector3((float)r.NextDouble() - 0.5f, (float)r.NextDouble() - 0.5f, (float)r.NextDouble() - 0.5f);
        var perpendicular = Vector3.Cross(line, random);
        _strafe = perpendicular.LengthSquared() > 1e-6f ? Vector3.Normalize(perpendicular) : Vector3.UnitY;
    }

    protected override void Act(double dt, ICreatureWorld world)
    {
        _reload = Math.Max(0, _reload - dt);
        if (!_attacking || world.Player is not { } player)
        {
            var to = _goal - Position;
            if (to.Length() <= 2) _goal = NextGoal(world.Random);
            else Velocity = Vector3.Normalize(to) * WanderSpeed;
            if (Velocity.LengthSquared() > 1e-6f) Facing = TurnToward(Facing, Vector3.Normalize(Velocity), TurnRate * (float)dt);
            return;
        }
        var line = player - Position;
        float distance = line.Length();
        if (distance < 1e-3f) return;
        var toward = line / distance;
        Facing = TurnToward(Facing, toward, TurnRate * (float)dt);
        Push(_strafe + toward * (distance - KeepDistance));
        if (_reload <= 0 && AngleBetween(Facing, toward) <= FireCone)
        {
            _reload = Reload;
            world.FireBolt(Position + toward * 2, Facing);
        }
    }

    protected override void Shot(bool byPlayer, float shooterDistance, ICreatureWorld world)
    {
        if (byPlayer && !_attacking) StartAttack(world);
    }

    static float AngleBetween(Vector3 a, Vector3 b) => MathF.Acos(Math.Clamp(Vector3.Dot(a, b), -1, 1));

    /// <summary><paramref name="from"/> turned toward <paramref name="to"/> by at most <paramref name="angle"/> radians.</summary>
    public static Vector3 TurnToward(Vector3 from, Vector3 to, float angle)
    {
        float between = AngleBetween(from, to);
        if (between <= angle || between < 1e-5f) return to;
        var axis = Vector3.Cross(from, to);
        if (axis.LengthSquared() < 1e-10f) axis = MathF.Abs(from.Y) < 0.9f ? Vector3.Cross(from, Vector3.UnitY) : Vector3.Cross(from, Vector3.UnitX);
        return Vector3.Normalize(Vector3.Transform(from, Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), angle)));
    }
}
