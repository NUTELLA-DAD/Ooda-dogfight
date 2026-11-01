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
        public Slot[] slots = new Slot[0];           // set for a wingman holding close formation until the formation breaks
        /// <summary>The player's side has broken formation: an enemy fighter came within Slot.BreakFt, or one of ours was hit.</summary>
        public bool formationBroken;
        /// <summary>The player's own place in the formation (never flown for them): straying from it breaks the formation.</summary>
        public Slot playerSlot;
        public double[] health = new double[0];
        public double[] ammo = new double[0];        // seconds of trigger left
        public int[] missiles = new int[0];          // missiles left
        public bool[] down = new bool[0];            // shot down or collided: out of the fight, falling
        public bool[] crashed = new bool[0];         // hit the ground or sea: stopped
        public bool[] hitObstacle = new bool[0];     // crashed into an obstacle (a cable, a bridge, a building) rather than the ground
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
            Array.Resize(ref slots, n + 1);
            Array.Resize(ref health, n + 1); health[n] = c.health;
            Array.Resize(ref ammo, n + 1); ammo[n] = c.ammoSec;
            Array.Resize(ref missiles, n + 1); missiles[n] = c.missiles;
            Array.Resize(ref down, n + 1);
            Array.Resize(ref crashed, n + 1);
            Array.Resize(ref hitObstacle, n + 1);
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
            c.slots = (Slot[])slots.Clone();   // slots never change, so they can be shared
            c.health = (double[])health.Clone(); c.ammo = (double[])ammo.Clone(); c.missiles = (int[])missiles.Clone();
            c.down = (bool[])down.Clone(); c.crashed = (bool[])crashed.Clone(); c.hitObstacle = (bool[])hitObstacle.Clone();
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
            CheckBreak();
            for (int i = 0; i < n; i++)
                plans[i] = routes[i] != null ? routes[i].Plan(planes[i])
                         : InFormation(i) ? slots[i].Plan(this, i, turnSec)
                         : i < given.Length && given[i].HasValue ? given[i].Value : TurnPlan.Holding();
            int steps = (int)Math.Round(turnSec / StepSec);
            var launched = new bool[n];
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
                    if (m.AltFt <= groundFt(m.state.pos.x, m.state.pos.y)) { crashed[i] = true; down[i] = true; }
                    else if (obstacleHit != null && obstacleHit(before[i], m.state.pos)) { crashed[i] = true; down[i] = true; hitObstacle[i] = true; }
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

                // Missiles: one a turn for each plan that asks, the first moment a target is in the lock cone
                for (int i = 0; i < n; i++)
                {
                    if (!plans[i].missile || launched[i] || missiles[i] <= 0 || Out(i)) continue;
                    int tgt = LockTarget(i, plans[i].missileTarget);
                    if (tgt < 0) continue;
                    var m = planes[i];
                    var ms = Missile.Make(i, side[i], tgt, m.state.pos + m.Forward * 20 - m.Up * 4, m.state.vel + m.Forward * 60, time + StepSec);
                    bullets.Add(ms);
                    missiles[i]--; launched[i] = true;
                    if (record) StartTrace(res, traceOf, ms, k);
                }

                // Rounds: gravity and drag, then a check against each other aircraft over the step
                for (int bi = bullets.Count - 1; bi >= 0; bi--)
                {
                    var b = bullets[bi];
                    Vec3d p0b = b.pos;
                    Trace bt = null;
                    if (record) traceOf.TryGetValue(b, out bt);
                    if (b is Missile ms)
                    {
                        bool ok = ms.target >= 0 && ms.target < n && !Out(ms.target);
                        ms.Step(ok ? planes[ms.target].state.pos : default, ok ? planes[ms.target].state.vel : default, ok, StepSec);
                    }
                    else
                    {
                        double decay = Math.Exp(-b.dragPerSec * StepSec);
                        Vec3d vAir = b.vel;
                        b.vel = new Vec3d(vAir.x * decay, vAir.y * decay, vAir.z * decay - Atmosphere.GFtps2 * StepSec);
                        b.pos = b.pos + (vAir + b.vel) * (StepSec / 2);
                        b.age += StepSec;
                    }
                    for (int target = 0; target < n && b.alive; target++)
                    {
                        if (side[target] == b.side || crashed[target]) continue;
                        Vec3d r0 = p0b - before[target], r1 = b.pos - planes[target].state.pos;
                        double reach = b.hitRadiusFt + combat[target].sizeFt;
                        if (bt != null)
                        {
                            // the closest pass so far, against whichever target it comes nearest to hitting (for the preview's near-miss colour)
                            double d = Closest(r0, r1);
                            if (bt.closestTarget < 0 || d / Math.Max(1e-6, reach) < bt.closestFt / Math.Max(1e-6, bt.reachFt))
                            { bt.closestFt = d; bt.reachFt = reach; bt.closestTarget = target; }
                        }
                        if (Passes(r0, r1, reach))
                        {
                            Hit(b, target, res);
                            b.alive = false;
                            if (bt != null) bt.hit = true;
                        }
                    }
                    if (b.age > b.lifeSec || b.pos.z < groundFt(b.pos.x, b.pos.y)) b.alive = false;
                    else if (b.alive && obstacleHit != null && obstacleHit(p0b, b.pos)) b.alive = false;
                    if (bt != null) bt.points.Add(b.pos);
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
            var tr = new Trace { owner = b.owner, startFrame = frame, missile = b is Missile };
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
            if (g.carrier >= 0)
            {
                if (g.carrier >= Count || Out(g.carrier)) return;   // the gunner went down with the bomber
                g.pos = planes[g.carrier].state.pos; g.vel = planes[g.carrier].state.vel;
            }
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
                Vec3d vel = dir * g.muzzleFps + g.vel;
                var b = new Bullet
                {
                    owner = -1 - f, side = g.side, damage = g.damage, hitRadiusFt = g.hitRadiusFt, dragPerSec = g.dragPerSec, lifeSec = g.lifeSec,
                    pos = g.pos + dir * (g.carrier >= 0 ? 40 : 6) + vel * late, vel = vel, age = late, fireTime = time + StepSec - late,
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

        /// <summary>
        /// Who aircraft i's seeker can lock now: the wanted one if it is in the cone and range, else the one nearest the nose; -1 none.
        /// </summary>
        public int LockTarget(int i, int wanted = -1)
        {
            var m = planes[i];
            double cos = Math.Cos(Missile.LockConeDeg * Math.PI / 180);
            int best = -1; double bestCos = cos;
            for (int j = 0; j < Count; j++)
            {
                if (side[j] == side[i] || Out(j)) continue;
                Vec3d r = planes[j].state.pos - m.state.pos;
                double range = r.Length;
                if (range < Missile.MinRangeFt || range > Missile.MaxRangeFt) continue;
                double c = Vec3d.Dot(r * (1 / range), m.Forward);
                if (c < cos) continue;
                if (Vec3d.Dot(r * (-1 / range), planes[j].Forward) > Math.Cos(Missile.NoLockNoseDeg * Math.PI / 180)) continue;   // head-on: no heat to see
                if (j == wanted) return j;
                if (c > bestCos) { bestCos = c; best = j; }
            }
            return wanted >= 0 ? (best >= 0 ? best : -1) : best;
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
        /// <summary>True while aircraft i holds its formation slot (and so plans for itself, like a route).</summary>
        public bool InFormation(int i) => !formationBroken && i < slots.Length && slots[i] != null && !Out(i) && !Out(slots[i].leader);

        /// <summary>
        /// Break formation once an enemy fighter is within Slot.BreakFt of any of ours, or one of ours has been hit.
        /// It stays broken for the rest of the fight.
        /// </summary>
        public void CheckBreak()
        {
            if (formationBroken) return;
            bool any = false;
            for (int i = 0; i < slots.Length; i++) any |= slots[i] != null;
            if (!any) return;
            if (playerSlot != null && !Out(0) && !Out(playerSlot.leader))
            {
                var l = planes[playerSlot.leader];
                if ((playerSlot.Where(l.state.pos, l.state.vel) - planes[0].state.pos).Length > Slot.LeadStrayFt) { formationBroken = true; return; }
            }
            for (int i = 0; i < Count && !formationBroken; i++)
            {
                if (side[i] != 0 || Out(i)) continue;
                if (health[i] < combat[i].health) { formationBroken = true; break; }
                for (int j = 0; j < Count; j++)
                    if (side[j] != 0 && !Out(j) && routes[j] == null && (planes[j].state.pos - planes[i].state.pos).Length < Slot.BreakFt)
                    { formationBroken = true; break; }
            }
        }

        public Battle Subset(int[] keep)
        {
            var c = (Battle)MemberwiseClone();
            int n = keep.Length;
            c.planes = new FlightModel[n]; c.combat = new CombatNumbers[n]; c.side = new int[n]; c.routes = new Route[n];
            c.health = new double[n]; c.ammo = new double[n]; c.missiles = new int[n]; c.down = new bool[n]; c.crashed = new bool[n]; c.hitObstacle = new bool[n]; c.fireClock = new double[n];
            var map = new Dictionary<int, int>();
            for (int j = 0; j < n; j++)
            {
                int i = keep[j]; map[i] = j;
                c.planes[j] = planes[i].Clone(); c.combat[j] = combat[i]; c.side[j] = side[i]; c.routes[j] = routes[i]?.Copy();
                c.health[j] = health[i]; c.ammo[j] = ammo[i]; c.missiles[j] = missiles[i]; c.down[j] = down[i]; c.crashed[j] = crashed[i]; c.hitObstacle[j] = hitObstacle[i]; c.fireClock[j] = fireClock[i];
            }
            c.slots = new Slot[n];
            for (int j = 0; j < n; j++)
            {
                var sl = keep[j] < slots.Length ? slots[keep[j]] : null;
                if (sl != null && map.TryGetValue(sl.leader, out int lj)) { var s2 = sl.Copy(); s2.leader = lj; c.slots[j] = s2; }
            }
            c.bullets = new List<Bullet>();
            foreach (var b in bullets)
            {
                var ms = b as Missile;
                bool aimedIn = ms != null && ms.target >= 0 && map.ContainsKey(ms.target);
                if (b.owner >= 0 && !map.ContainsKey(b.owner) && !aimedIn) continue;   // keep a missile chasing someone kept, whoever fired it
                var bc = b.Copy();
                if (b.owner >= 0) bc.owner = map.TryGetValue(b.owner, out int oj) ? oj : -1000;
                if (bc is Missile mc) mc.target = aimedIn ? map[ms.target] : -1;
                c.bullets.Add(bc);
            }
            var kept = new List<FlakSite>();
            foreach (var g0 in flak)
            {
                var g = g0.Copy();
                if (g.carrier >= 0)
                {
                    if (!map.TryGetValue(g.carrier, out int cj)) continue;   // its bomber is left out: so is the gunner
                    g.carrier = cj; g.pos = planes[g0.carrier].state.pos; g.vel = planes[g0.carrier].state.vel;
                }
                g.target = g0.target >= 0 && map.TryGetValue(g0.target, out int t) ? t : -1;
                kept.Add(g);
            }
            c.flak = kept.ToArray();
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

        /// <summary>How close a straight relative move from r0 to r1 comes to the origin.</summary>
        static double Closest(Vec3d r0, Vec3d r1)
        {
            Vec3d d = r1 - r0;
            double dd = Vec3d.Dot(d, d);
            double s = dd > 1e-12 ? Math.Max(0, Math.Min(1, -Vec3d.Dot(r0, d) / dd)) : 0;
            Vec3d c = r0 + d * s;
            return Math.Sqrt(Vec3d.Dot(c, c));
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
