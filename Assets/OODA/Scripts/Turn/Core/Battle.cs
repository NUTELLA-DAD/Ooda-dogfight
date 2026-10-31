using System;
using System.Collections.Generic;
using Ooda.Flight.Core;

namespace Ooda.Turn.Core
{
    /// <summary>Everything that happened in one movement turn, for playback.</summary>
    public class TurnResult
    {
        public double startTime, dt;
        public AircraftFrame[][] frames;
        public List<Trace> traces = new List<Trace>();
        public List<HitEvent> hits = new List<HitEvent>();
        public int FrameCount => frames[0].Length;
    }

    /// <summary>
    /// The aircraft (0 the player, 1 the enemy, then any others, such as the bomber the player escorts), their
    /// sides, their guns and the rounds in the air. Rounds hit aircraft on the other side. Resolve flies one
    /// simultaneous turn from everyone's plans in small steps, firing, moving and checking every packet, and
    /// reports frames for playback. An aircraft with a route flies it by itself. Pure C#, feet, x east, y north, z up; the ground comes in as a function.
    /// A Clone flies a "what if" without touching the real battle (the ghost path, tracer preview, the AI).
    /// </summary>
    public class Battle
    {
        public const double StepSec = 0.02;
        public const double CollisionFt = 25;

        public FlightModel[] planes = new FlightModel[0];
        public CombatNumbers[] combat = new CombatNumbers[0];
        public int[] side = new int[0];              // 0 the player's side, 1 the enemy's
        public Route[] routes = new Route[0];        // set for an aircraft that flies a route by itself
        public double[] health = new double[0];
        public double[] ammo = new double[0];        // seconds of trigger left
        public bool[] down = new bool[0];            // shot down or collided: out of the fight, falling
        public bool[] crashed = new bool[0];         // hit the ground or sea: stopped
        public int Count => planes.Length;
        public List<Bullet> bullets = new List<Bullet>();
        public double time;
        public int turn;
        public bool dispersion = true;
        public Lcg rng = new Lcg(1940);
        public Func<double, double, double> groundFt = (x, y) => 0;
        /// <summary>Solid things above the ground (bridges, tunnel mouths, buildings): true if the segment from a to b (feet) hits one.</summary>
        public Func<Vec3d, Vec3d, bool> obstacleHit;
        /// <summary>Guns on the ground.</summary>
        public FlakSite[] flak = new FlakSite[0];

        double[] fireClock = new double[0];

        public Battle(FlightModel a, CombatNumbers ca, FlightModel b, CombatNumbers cb)
        {
            Add(a, ca, 0);
            Add(b, cb, 1);
        }

        /// <summary>Add an aircraft on a side (0 the player's, 1 the enemy's), flying a route by itself if one is given. Returns its index.</summary>
        public int Add(FlightModel m, CombatNumbers c, int onSide, Route route = null)
        {
            int n = planes.Length;
            Array.Resize(ref planes, n + 1); planes[n] = m;
            Array.Resize(ref combat, n + 1); combat[n] = c;
            Array.Resize(ref side, n + 1); side[n] = onSide;
            Array.Resize(ref routes, n + 1); routes[n] = route?.Copy();
            Array.Resize(ref health, n + 1); health[n] = c.health;
            Array.Resize(ref ammo, n + 1); ammo[n] = c.ammoSec;
            Array.Resize(ref down, n + 1);
            Array.Resize(ref crashed, n + 1);
            Array.Resize(ref fireClock, n + 1);
            return n;
        }

        /// <summary>
        /// The fight is over when the player is out, or the enemy has no aircraft left, or there are aircraft
        /// flying routes (the bombers) and none of them is still flying one: all down, or the ones left are through.
        /// </summary>
        public bool Over
        {
            get
            {
                if (Out(0)) return true;
                bool enemyLeft = false, anyRoute = false, routeFlying = false;
                for (int i = 0; i < Count; i++)
                {
                    if (side[i] != side[0] && !Out(i)) enemyLeft = true;
                    if (routes[i] == null) continue;
                    anyRoute = true;
                    if (!Out(i) && !routes[i].Done) routeFlying = true;
                }
                return !enemyLeft || (anyRoute && !routeFlying);
            }
        }
        public bool Out(int i) => down[i] || crashed[i];

        public Battle Clone()
        {
            var c = (Battle)MemberwiseClone();
            c.planes = new FlightModel[Count];
            c.routes = new Route[Count];
            for (int i = 0; i < Count; i++) { c.planes[i] = planes[i].Clone(); c.routes[i] = routes[i]?.Copy(); }
            c.health = (double[])health.Clone(); c.ammo = (double[])ammo.Clone();
            c.down = (bool[])down.Clone(); c.crashed = (bool[])crashed.Clone();
            c.fireClock = (double[])fireClock.Clone();
            c.flak = new FlakSite[flak.Length];
            for (int k = 0; k < flak.Length; k++) c.flak[k] = flak[k].Copy();
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
        /// p0 and p1 are the player's and the enemy's plans; aircraft with routes plan for themselves.
        /// </summary>
        public TurnResult Resolve(TurnPlan p0, TurnPlan p1, double turnSec, bool record = true, Action<Battle, int> onStep = null)
            => Resolve(new TurnPlan?[] { p0, p1 }, turnSec, record, onStep);

        /// <summary>Fly one turn from a plan for each aircraft by index; a missing one holds (or flies its route).</summary>
        public TurnResult Resolve(TurnPlan?[] given, double turnSec, bool record = true, Action<Battle, int> onStep = null)
        {
            int n = Count;
            var plans = new TurnPlan[n];
            for (int i = 0; i < n; i++)
                plans[i] = routes[i] != null ? routes[i].Plan(planes[i]) : i < given.Length && given[i].HasValue ? given[i].Value : TurnPlan.Holding();
            int steps = (int)Math.Round(turnSec / StepSec);
            var res = new TurnResult { startTime = time, dt = StepSec, frames = new AircraftFrame[n][] };
            var traceOf = record ? new Dictionary<Bullet, Trace>() : null;
            if (record)
            {
                for (int i = 0; i < n; i++) { res.frames[i] = new AircraftFrame[steps + 1]; res.frames[i][0] = Frame(i, false); }
                foreach (var b in bullets) StartTrace(res, traceOf, b, 0);
            }

            for (int k = 1; k <= steps; k++)
            {
                double t = (k - 1) * StepSec;   // time into the turn at the start of this step
                bool[] firing = new bool[n];
                var before = new Vec3d[n];
                for (int i = 0; i < n; i++) before[i] = planes[i].state.pos;

                // Aircraft
                for (int i = 0; i < n; i++)
                {
                    if (crashed[i]) continue;
                    var m = planes[i];
                    FlightControls c = down[i]
                        ? new FlightControls { pitch = -0.1, roll = 0.25 * (i % 2 == 0 ? 1 : -1), throttle = 0 }
                        : plans[i].ControlsFor(m, t);
                    m.Step(c, StepSec);
                    if (m.AltFt <= groundFt(m.state.pos.x, m.state.pos.y) || (obstacleHit != null && obstacleHit(before[i], m.state.pos)))
                    { crashed[i] = true; down[i] = true; }
                }

                for (int i = 0; i < n; i++) routes[i]?.Advance(planes[i]);

                // Guns: a packet each packetSec of trigger while there is ammunition
                for (int i = 0; i < n; i++)
                {
                    if (Out(i) || !plans[i].Fires(t) || ammo[i] <= 0) { fireClock[i] = 0; continue; }
                    firing[i] = true;
                    ammo[i] = Math.Max(0, ammo[i] - StepSec);
                    fireClock[i] -= StepSec;
                    while (fireClock[i] <= 0)
                    {
                        double late = -fireClock[i];   // how long ago in this step the packet was due: it has flown that long
                        fireClock[i] += combat[i].packetSec;
                        var b = Fire(i, late);
                        bullets.Add(b);
                        if (record) StartTrace(res, traceOf, b, k);
                    }
                }
                for (int f = 0; f < flak.Length; f++) FireFlak(f, res, traceOf, k);

                // Rounds: gravity and drag, then a check against each other aircraft over the step
                for (int bi = bullets.Count - 1; bi >= 0; bi--)
                {
                    var b = bullets[bi];
                    Vec3d p0b = b.pos;
                    double decay = Math.Exp(-b.dragPerSec * StepSec);
                    Vec3d vAir = b.vel;
                    b.vel = new Vec3d(vAir.x * decay, vAir.y * decay, vAir.z * decay - Atmosphere.GFtps2 * StepSec);
                    b.pos = b.pos + (vAir + b.vel) * (StepSec / 2);
                    b.age += StepSec;
                    for (int target = 0; target < n && b.alive; target++)
                    {
                        if (side[target] == b.side || crashed[target]) continue;
                        if (Passes(p0b - before[target], b.pos - planes[target].state.pos, b.hitRadiusFt + combat[target].sizeFt))
                        {
                            Hit(b, target, res);
                            b.alive = false;
                            if (record && traceOf.TryGetValue(b, out var ht)) ht.hit = true;
                        }
                    }
                    if (b.age > b.lifeSec || b.pos.z < groundFt(b.pos.x, b.pos.y)) b.alive = false;
                    else if (b.alive && obstacleHit != null && obstacleHit(p0b, b.pos)) b.alive = false;
                    if (record && traceOf.TryGetValue(b, out var tr)) tr.points.Add(b.pos);
                    if (!b.alive) bullets.RemoveAt(bi);
                }

                // Mid-air collision: both go down
                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                        if (!crashed[i] && !crashed[j] && Passes(before[i] - before[j], planes[i].state.pos - planes[j].state.pos, CollisionFt + combat[i].sizeFt + combat[j].sizeFt))
                            down[i] = down[j] = true;

                time += StepSec;
                if (record) for (int i = 0; i < n; i++) res.frames[i][k] = Frame(i, firing[i]);
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
            return new Bullet
            {
                owner = i, side = side[i], damage = cn.damage, hitRadiusFt = cn.hitRadiusFt, dragPerSec = cn.dragPerSec, lifeSec = cn.lifeSec,
                pos = m.state.pos + dir * 12 + vel * late, vel = vel, age = late, fireTime = time + StepSec - late,
            };
        }

        /// <summary>A flak gun's step: start a burst at whoever it can see, fire packets through it, then pause.</summary>
        void FireFlak(int f, TurnResult res, Dictionary<Bullet, Trace> traceOf, int k)
        {
            var g = flak[f];
            double cycle = g.burstSec + g.pauseSec;
            g.phase += StepSec;
            if (g.phase >= cycle) { g.phase -= cycle; g.target = PickFlakTarget(g); g.fireClock = 0; g.errA = rng.Gauss() * g.aimErrorMil / 1000; g.errB = rng.Gauss() * g.aimErrorMil / 1000; }
            if (g.phase >= g.burstSec || g.target < 0 || Out(g.target)) return;
            g.fireClock -= StepSec;
            while (g.fireClock <= 0)
            {
                double late = -g.fireClock;
                g.fireClock += g.packetSec;
                Vec3d dir = g.Aim(planes[g.target]);
                if (dispersion)
                {
                    double s = g.spreadMil / 1000;
                    dir = (dir + new Vec3d(rng.Gauss() * s, rng.Gauss() * s, rng.Gauss() * s)).Normalized;
                }
                Vec3d vel = dir * g.muzzleFps;
                var b = new Bullet
                {
                    owner = -1 - f, side = g.side, damage = g.damage, hitRadiusFt = g.hitRadiusFt, dragPerSec = g.dragPerSec, lifeSec = g.lifeSec,
                    pos = g.pos + dir * 6 + vel * late, vel = vel, age = late, fireTime = time + StepSec - late,
                };
                bullets.Add(b);
                if (traceOf != null) StartTrace(res, traceOf, b, k);
            }
        }

        int PickFlakTarget(FlakSite g)
        {
            // the gunners go for fighters first: they are the low, fast threat; a bomber only when no fighter is in reach
            int best = -1; double bestR = double.PositiveInfinity;
            for (int i = 0; i < Count; i++)
            {
                if (side[i] == g.side || Out(i)) continue;
                double r = (planes[i].state.pos - g.pos).Length;
                if (r > g.rangeFt) continue;
                double cost = r + (routes[i] != null ? g.rangeFt * g.bomberReluctance : 0);
                if (cost < bestR && CanSee(g.pos + new Vec3d(0, 0, 8), planes[i].state.pos)) { bestR = cost; best = i; }
            }
            return best;
        }

        /// <summary>A clear line between two points: over the ground all the way and through no obstacle.</summary>
        public bool CanSee(Vec3d a, Vec3d b)
        {
            int n = Math.Max(4, (int)((b - a).Length / 300));
            for (int s = 1; s < n; s++)
            {
                Vec3d p = a + (b - a) * ((double)s / n);
                if (p.z < groundFt(p.x, p.y)) return false;
            }
            return obstacleHit == null || !obstacleHit(a, b);
        }

        /// <summary>
        /// A smaller battle with only some of the aircraft (by index, in that order), for the AI to try plans in
        /// quickly. Rounds fired by the others are left out; the flak and the ground stay.
        /// </summary>
        public Battle Subset(int[] keep)
        {
            var c = (Battle)MemberwiseClone();
            int n = keep.Length;
            c.planes = new FlightModel[n]; c.combat = new CombatNumbers[n]; c.side = new int[n]; c.routes = new Route[n];
            c.health = new double[n]; c.ammo = new double[n]; c.down = new bool[n]; c.crashed = new bool[n]; c.fireClock = new double[n];
            var map = new Dictionary<int, int>();
            for (int j = 0; j < n; j++)
            {
                int i = keep[j]; map[i] = j;
                c.planes[j] = planes[i].Clone(); c.combat[j] = combat[i]; c.side[j] = side[i]; c.routes[j] = routes[i]?.Copy();
                c.health[j] = health[i]; c.ammo[j] = ammo[i]; c.down[j] = down[i]; c.crashed[j] = crashed[i]; c.fireClock[j] = fireClock[i];
            }
            c.bullets = new List<Bullet>();
            foreach (var b in bullets)
            {
                if (b.owner >= 0 && !map.ContainsKey(b.owner)) continue;
                var bc = b.Copy(); if (b.owner >= 0) bc.owner = map[b.owner]; c.bullets.Add(bc);
            }
            c.flak = new FlakSite[flak.Length];
            for (int k = 0; k < flak.Length; k++)
            {
                c.flak[k] = flak[k].Copy();
                c.flak[k].target = flak[k].target >= 0 && map.TryGetValue(flak[k].target, out int t) ? t : -1;
            }
            return c;
        }

        void Hit(Bullet b, int target, TurnResult res)
        {
            var m = planes[target];
            Vec3d rel = b.pos - m.state.pos;
            var e = new HitEvent
            {
                time = time + StepSec, shooter = b.owner, target = target, pos = b.pos, fireTime = b.fireTime, damage = b.damage,
                local = new Vec3d(Vec3d.Dot(rel, m.Right), Vec3d.Dot(rel, m.Forward), Vec3d.Dot(rel, m.Up)),
            };
            res.hits.Add(e);
            if (down[target]) return;
            health[target] -= b.damage;
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
