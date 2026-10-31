using System;
using System.Collections.Generic;
using Ooda.Flight.Core;

namespace Ooda.Turn.Core
{
    /// <summary>Everything that happened in one movement turn, for playback.</summary>
    public class TurnResult
    {
        public double startTime, dt;
        public AircraftFrame[][] frames = new AircraftFrame[2][];
        public List<Trace> traces = new List<Trace>();
        public List<HitEvent> hits = new List<HitEvent>();
        public int FrameCount => frames[0].Length;
    }

    /// <summary>
    /// Two aircraft (0 the player, 1 the enemy), their guns and the rounds in the air. Resolve flies one
    /// simultaneous turn from both plans in small steps, firing, moving and checking every packet, and
    /// reports frames for playback. Pure C#, feet, x east, y north, z up; the ground comes in as a function.
    /// A Clone flies a "what if" without touching the real battle (the ghost path, tracer preview, the AI).
    /// </summary>
    public class Battle
    {
        public const double StepSec = 0.02;
        public const double CollisionFt = 25;

        public FlightModel[] planes = new FlightModel[2];
        public CombatNumbers[] combat = new CombatNumbers[2];
        public double[] health = new double[2];
        public double[] ammo = new double[2];       // seconds of trigger left
        public bool[] down = new bool[2];            // shot down or collided: out of the fight, falling
        public bool[] crashed = new bool[2];         // hit the ground or sea: stopped
        public List<Bullet> bullets = new List<Bullet>();
        public double time;
        public int turn;
        public bool dispersion = true;
        public Lcg rng = new Lcg(1940);
        public Func<double, double, double> groundFt = (x, y) => 0;
        /// <summary>Solid things above the ground (bridges, tunnel mouths, buildings): true if the segment from a to b (feet) hits one.</summary>
        public Func<Vec3d, Vec3d, bool> obstacleHit;

        double[] fireClock = new double[2];

        public Battle(FlightModel a, CombatNumbers ca, FlightModel b, CombatNumbers cb)
        {
            planes[0] = a; planes[1] = b; combat[0] = ca; combat[1] = cb;
            for (int i = 0; i < 2; i++) { health[i] = combat[i].health; ammo[i] = combat[i].ammoSec; }
        }

        public bool Over => down[0] || down[1] || crashed[0] || crashed[1];
        public bool Out(int i) => down[i] || crashed[i];

        public Battle Clone()
        {
            var c = (Battle)MemberwiseClone();
            c.planes = new[] { planes[0].Clone(), planes[1].Clone() };
            c.health = (double[])health.Clone(); c.ammo = (double[])ammo.Clone();
            c.down = (bool[])down.Clone(); c.crashed = (bool[])crashed.Clone();
            c.fireClock = (double[])fireClock.Clone();
            c.bullets = new List<Bullet>(bullets.Count);
            foreach (var b in bullets) c.bullets.Add(b.Copy());
            return c;
        }

        /// <summary>The direction the guns point: the nose, which sits the angle of attack above the flight path.</summary>
        public static Vec3d GunLine(FlightModel m)
        {
            double a = m.AlphaDeg * Math.PI / 180;
            return (m.Forward * Math.Cos(a) + m.Up * Math.Sin(a)).Normalized;
        }

        /// <summary>
        /// Fly one turn. With record false nothing is stored for playback (faster, for previews and the AI);
        /// hits and outcomes still happen. onStep sees the battle after each step (the AI scores along the way).
        /// </summary>
        public TurnResult Resolve(TurnPlan p0, TurnPlan p1, double turnSec, bool record = true, Action<Battle, int> onStep = null)
        {
            var plans = new[] { p0, p1 };
            int steps = (int)Math.Round(turnSec / StepSec);
            var res = new TurnResult { startTime = time, dt = StepSec };
            var traceOf = record ? new Dictionary<Bullet, Trace>() : null;
            if (record)
            {
                res.frames[0] = new AircraftFrame[steps + 1];
                res.frames[1] = new AircraftFrame[steps + 1];
                for (int i = 0; i < 2; i++) res.frames[i][0] = Frame(i, false);
                foreach (var b in bullets) StartTrace(res, traceOf, b, 0);
            }

            for (int k = 1; k <= steps; k++)
            {
                double t = (k - 1) * StepSec;   // time into the turn at the start of this step
                bool[] firing = new bool[2];
                var before = new[] { planes[0].state.pos, planes[1].state.pos };

                // Aircraft
                for (int i = 0; i < 2; i++)
                {
                    if (crashed[i]) continue;
                    var m = planes[i];
                    FlightControls c = down[i]
                        ? new FlightControls { pitch = -0.1, roll = 0.25 * (i == 0 ? 1 : -1), throttle = 0 }
                        : plans[i].ControlsFor(m);
                    m.Step(c, StepSec);
                    if (m.AltFt <= groundFt(m.state.pos.x, m.state.pos.y) || (obstacleHit != null && obstacleHit(before[i], m.state.pos)))
                    { crashed[i] = true; down[i] = true; }
                }

                // Guns: a packet each packetSec of trigger while there is ammunition
                for (int i = 0; i < 2; i++)
                {
                    if (Out(i) || !plans[i].Fires(t) || ammo[i] <= 0) { fireClock[i] = 0; continue; }
                    firing[i] = true;
                    ammo[i] = Math.Max(0, ammo[i] - StepSec);
                    fireClock[i] -= StepSec;
                    while (fireClock[i] <= 0)
                    {
                        fireClock[i] += combat[i].packetSec;
                        var b = Fire(i, -fireClock[i]);
                        bullets.Add(b);
                        if (record) StartTrace(res, traceOf, b, k);
                    }
                }

                // Rounds: gravity and drag, then a check against each other aircraft over the step
                for (int bi = bullets.Count - 1; bi >= 0; bi--)
                {
                    var b = bullets[bi];
                    Vec3d p0b = b.pos;
                    double decay = Math.Exp(-combat[b.owner].dragPerSec * StepSec);
                    Vec3d vAir = b.vel;
                    b.vel = new Vec3d(vAir.x * decay, vAir.y * decay, vAir.z * decay - Atmosphere.GFtps2 * StepSec);
                    b.pos = b.pos + (vAir + b.vel) * (StepSec / 2);
                    b.age += StepSec;
                    int target = 1 - b.owner;
                    if (!crashed[target] && Passes(p0b - before[target], b.pos - planes[target].state.pos, combat[b.owner].hitRadiusFt))
                    {
                        Hit(b, target, res);
                        b.alive = false;
                        if (record && traceOf.TryGetValue(b, out var ht)) ht.hit = true;
                    }
                    if (b.age > combat[b.owner].lifeSec || b.pos.z < groundFt(b.pos.x, b.pos.y)) b.alive = false;
                    else if (b.alive && obstacleHit != null && obstacleHit(p0b, b.pos)) b.alive = false;
                    if (record && traceOf.TryGetValue(b, out var tr)) tr.points.Add(b.pos);
                    if (!b.alive) bullets.RemoveAt(bi);
                }

                // Mid-air collision: both go down
                if (!crashed[0] && !crashed[1] && Passes(before[0] - before[1], planes[0].state.pos - planes[1].state.pos, CollisionFt))
                {
                    down[0] = down[1] = true;
                }

                time += StepSec;
                if (record) for (int i = 0; i < 2; i++) res.frames[i][k] = Frame(i, firing[i]);
                onStep?.Invoke(this, k);
            }
            turn++;
            return res;
        }

        AircraftFrame Frame(int i, bool firing)
        {
            var m = planes[i];
            return new AircraftFrame
            {
                time = time, pos = m.state.pos, vel = m.state.vel, up = m.state.up,
                alphaDeg = m.AlphaDeg, g = m.g, throttle = m.throttle, firing = firing, stalled = m.stalled, departed = m.Departed, down = down[i],
            };
        }

        static void StartTrace(TurnResult res, Dictionary<Bullet, Trace> traceOf, Bullet b, int frame)
        {
            var tr = new Trace { owner = b.owner, startFrame = frame };
            tr.points.Add(b.pos);
            traceOf[b] = tr;
            res.traces.Add(tr);
        }

        Bullet Fire(int i, double late)
        {
            var m = planes[i];
            var cn = combat[i];
            Vec3d dir = GunLine(m);
            if (dispersion)
            {
                double s = cn.spreadMil / 1000;
                dir = (dir + m.Right * (rng.Gauss() * s) + m.Up * (rng.Gauss() * s)).Normalized;
            }
            Vec3d vel = m.state.vel + dir * cn.muzzleFps;
            // Leave from just ahead of the aircraft; a packet fired partway through the step has flown a little already
            return new Bullet { owner = i, pos = m.state.pos + dir * 12 + vel * late, vel = vel, age = late, fireTime = time + StepSec - late };
        }

        void Hit(Bullet b, int target, TurnResult res)
        {
            var m = planes[target];
            Vec3d rel = b.pos - m.state.pos;
            var e = new HitEvent
            {
                time = time + StepSec, shooter = b.owner, target = target, pos = b.pos, fireTime = b.fireTime,
                local = new Vec3d(Vec3d.Dot(rel, m.Right), Vec3d.Dot(rel, m.Forward), Vec3d.Dot(rel, m.Up)),
            };
            res.hits.Add(e);
            if (down[target]) return;
            health[target] -= combat[b.owner].damage;
            if (health[target] <= 0) down[target] = true;
        }

        /// <summary>Whether a straight relative move from r0 to r1 comes within radius of the origin.</summary>
        static bool Passes(Vec3d r0, Vec3d r1, double radius)
        {
            Vec3d d = r1 - r0;
            double dd = Vec3d.Dot(d, d);
            double s = dd > 1e-12 ? Math.Max(0, Math.Min(1, -Vec3d.Dot(r0, d) / dd)) : 0;
            Vec3d c = r0 + d * s;
            return Vec3d.Dot(c, c) < radius * radius;
        }
    }
}
