// Checks of the turn engine a pilot would recognise: the shell is bigger toward a pull than a push,
// the autopilot gets the nose where it was asked without departing, rounds drop and hit what is ahead,
// and two AI pilots fight to a finish without the engine breaking.
using System;
using System.Diagnostics;
using System.Linq;
using Ooda.Flight.Core;
using Ooda.Turn.Core;

static class TurnChecks
{
    const double D = 180 / Math.PI;

    static double Nearest(Battle b)
    {
        double best = double.PositiveInfinity;
        for (int i = 0; i < b.Count; i++) for (int j = 0; j < b.Count; j++)
            if (b.side[i] != b.side[j] && !b.Out(i) && !b.Out(j)) best = Math.Min(best, (b.planes[i].state.pos - b.planes[j].state.pos).Length);
        return best;
    }

    public static int Run()
    {
        int fails = 0;
        void Check(bool ok, string what) { if (!ok) fails++; Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}"); }

        Console.WriteLine("\nTurn engine");
        var battle = Scenarios.HeadOn(new Vec3d(0, -19685, 0));
        for (int i = 0; i < 2; i++)
        {
            var m = battle.planes[i];
            var env = Envelope.Compute(m, Scenarios.TurnSec);
            Console.WriteLine($"  {m.numbers.name} at {m.Kias:F0} KIAS, reach in 3 s: pull {env.MaxAngle(0) * D:F0} deg, right {env.MaxAngle(Math.PI / 2) * D:F0}, push {env.MaxAngle(Math.PI) * D:F0}, left {env.MaxAngle(1.5 * Math.PI) * D:F0}");
            Check(env.MaxAngle(0) > env.MaxAngle(Math.PI), "pulls further than it pushes");
            Check(Math.Abs(env.MaxAngle(Math.PI / 2) - env.MaxAngle(1.5 * Math.PI)) < 3 / D, "left and right turns match");

            foreach (var (phi, frac, name) in new[] { (Math.PI / 2, 0.5, "half a right turn"), (0.0, 0.9, "most of a pull"), (1.5 * Math.PI, 0.9, "most of a left turn"), (Math.PI * 0.75, 0.6, "a diving right turn") })
            {
                var f = m.Clone();
                Vec3d aim = env.Direction(phi, env.MaxAngle(phi) * frac);
                bool departed = false; double peak = 0;
                for (int s = 0; s < 150; s++) { f.Step(AutoPilot.Controls(f, aim, 1, false), Battle.StepSec); departed |= f.Departed; peak = Math.Max(peak, f.g); }
                double err = Envelope.Angle(f.Forward, aim) * D;
                Console.WriteLine($"    {name}: nose {err:F1} deg off the aim after 3 s, peak {peak:F1} G, {f.Kias:F0} KIAS");
                Check(err < 6 && !departed, $"{name} arrives without departing");
            }
        }

        // Timeline points that each set only some things: level for 1 s, roll to 60 right at 1 s, pull 5 G at 1.6 s
        // (the bank carries on), throttle to idle at 2 s (bank and pull carry on)
        {
            var m = new FlightModel(AircraftNumbers.P51D(), PointMass.Make(new Vec3d(0, 0, 8000), Atmosphere.IasToTasKt(300, 8000), Math.PI / 2)) { throttle = 1 };
            var p = new TurnPlan { throttle = 1, marks = new[] { new Mark(2.0) { throttle = 0 }, new Mark(0, 0, 1), new Mark(1.0) { bankDeg = 60 }, new Mark(1.6) { g = 5 } } };
            var st = p.StateAt(2.5);
            Check(st.bankDeg == 60 && st.g == 5 && st.throttle == 0 && !st.wep, "a point changes only what it names; the rest carries on");
            double bankAt1 = 0, bankAt16 = 0, headAt1 = 0; bool departed = false; double peak = 0;
            for (int s = 0; s < 150; s++)
            {
                double t = s * Battle.StepSec;
                m.Step(p.ControlsFor(m, t), Battle.StepSec);
                departed |= m.Departed; peak = Math.Max(peak, m.g);
                if (s == 49) { bankAt1 = AutoPilot.BankDeg(m); headAt1 = Math.Atan2(m.state.vel.x, m.state.vel.y) * D; }
                if (s == 79) bankAt16 = AutoPilot.BankDeg(m);
            }
            double head = Math.Atan2(m.state.vel.x, m.state.vel.y) * D;
            Console.WriteLine($"  marks (level 1 s, roll 60 right, pull 5 G at 1.6 s): bank {bankAt1:F0} at 1 s, {bankAt16:F0} at 1.6 s, {AutoPilot.BankDeg(m):F0} at 3 s; heading {headAt1:F0} then {head:F0}; peak {peak:F1} G");
            Check(Math.Abs(bankAt1) < 3 && bankAt16 > 30 && Math.Abs(AutoPilot.BankDeg(m) - 60) < 10 && head > headAt1 + 10 && !departed, "level, then roll, then pull: it rolls on straight flight and turns right");
            // the same marks asking 9 G at 160 KIAS: past the stall line, it stalls
            var slow = new FlightModel(AircraftNumbers.P51D(), PointMass.Make(new Vec3d(0, 0, 8000), Atmosphere.IasToTasKt(160, 8000), Math.PI / 2)) { throttle = 1 };
            var hard = new TurnPlan { throttle = 1, marks = new[] { new Mark(0, 60, 9) } };
            bool stalled = false;
            for (int s = 0; s < 150; s++) { slow.Step(hard.ControlsFor(slow, s * Battle.StepSec), Battle.StepSec); stalled |= slow.stalled; }
            Console.WriteLine($"  a 9 G mark at 160 KIAS: stalled {stalled}, departure {slow.departure}");
            Check(stalled, "a mark asking more G than the wing has stalls it, like the arrow's overpull");
        }

        // Overpull: past the ring at 160 KIAS the wing stalls and it departs; the next turn's arrow recovers it
        {
            var m = new FlightModel(AircraftNumbers.P51D(), PointMass.Make(new Vec3d(0, 0, 8000), Atmosphere.IasToTasKt(160, 8000), Math.PI / 2));
            var env = Envelope.Compute(m, 3);
            var aim = env.Clamp(env.Direction(0, env.MaxAngle(0) + Envelope.OverpullRange), true);
            double op = env.Overpull(aim);
            bool departed = false;
            for (int s = 0; s < 150; s++) { m.Step(AutoPilot.Controls(m, aim, 1, false, op), Battle.StepSec); departed |= m.Departed; }
            var straight = m.Forward;
            for (int s = 0; s < 150; s++) m.Step(AutoPilot.Controls(m, straight, 1, false), Battle.StepSec);
            Console.WriteLine($"  overpull at 160 KIAS: departed {departed}; after a normal turn: {m.departure}, {m.Kias:F0} KIAS");
            Check(departed && !m.Departed, "overpulling departs, and the next normal arrow recovers");
        }

        // Guns: a target 1,000 ft ahead flying the same way
        {
            var shooter = new FlightModel(AircraftNumbers.P51D(), PointMass.Make(new Vec3d(0, 0, 5000), 300, Math.PI / 2));
            var target = new FlightModel(AircraftNumbers.P51D(), PointMass.Make(new Vec3d(0, 1000, 5000), 300, Math.PI / 2));
            shooter.throttle = target.throttle = 1;
            var b = new Battle(shooter, CombatNumbers.P51D(), target, CombatNumbers.P51D()) { dispersion = false };
            b.health[1] = double.PositiveInfinity;
            var r = b.Resolve(new TurnPlan { aim = shooter.Forward, throttle = 1, burstStart = 0, burstEnd = 0.5 }, TurnPlan.Holding(), 3);
            Vec3d last = Vec3d.Zero; foreach (var tr in r.traces) if (tr.owner == 0 && !tr.hit) { last = tr.points[tr.points.Count - 1]; break; }
            Console.WriteLine($"  dead astern at 1,000 ft, 0.5 s burst: {r.hits.Count} of {r.traces.Count} packets hit");
            Check(r.hits.Count >= 5, "a steady burst from dead astern hits");

            // Drop: fire level, see how far the rounds fall by 1,500 ft out
            var lone = new FlightModel(AircraftNumbers.P51D(), PointMass.Make(new Vec3d(0, 0, 5000), 300, Math.PI / 2));
            var far = new FlightModel(AircraftNumbers.P51D(), PointMass.Make(new Vec3d(50000, 0, 5000), 300, 0));
            var b2 = new Battle(lone, CombatNumbers.P51D(), far, CombatNumbers.P51D()) { dispersion = false };
            var r2 = b2.Resolve(new TurnPlan { aim = lone.Forward, throttle = 1, burstStart = 0, burstEnd = 0.04 }, TurnPlan.Holding(), 2);
            var tr0 = r2.traces[0]; double drop = 0, tof = 0;
            for (int k = 0; k < tr0.points.Count; k++) { var q = tr0.points[k]; if (q.y - r2.frames[0][tr0.startFrame + k].pos.y >= 1500) { drop = 5000 - q.z; tof = k * Battle.StepSec; break; } }
            Console.WriteLine($"  a round fired level reaches 1,500 ft ahead in {tof:F2} s, having dropped {drop:F1} ft below the gun line's start");
            Check(drop > 0 && tof > 0.3 && tof < 1.0, "rounds take time to arrive and fall: you must lead and hold the nose up");
        }

        // A bridge across the path: flying into it crashes, the rounds stop on it
        {
            var a = new FlightModel(AircraftNumbers.P51D(), PointMass.Make(new Vec3d(0, 0, 5000), 300, Math.PI / 2));
            var far = new FlightModel(AircraftNumbers.P51D(), PointMass.Make(new Vec3d(50000, 0, 5000), 300, 0));
            var b = new Battle(a, CombatNumbers.P51D(), far, CombatNumbers.P51D()) { dispersion = false };
            b.obstacleHit = (p, q) => (p.y < 800) != (q.y < 800) && Math.Abs(p.z - 5000) < 100;   // a wall 800 ft ahead
            var r = b.Resolve(new TurnPlan { aim = a.Forward, throttle = 1, burstStart = 0, burstEnd = 0.1 }, TurnPlan.Holding(), 3);
            double furthest = 0; foreach (var tr in r.traces) foreach (var q in tr.points) furthest = Math.Max(furthest, q.y);
            Console.WriteLine($"  flying at a bridge 800 ft ahead: crashed {b.crashed[0]}; furthest round {furthest:F0} ft");
            Check(b.crashed[0] && furthest < 1000, "a bridge in the way stops the aircraft and the rounds");
        }

        // Two AI pilots fight from the head-on start
        {
            var fight = Scenarios.HeadOn(new Vec3d(0, -19685, 0));
            var ai0 = new EnemyAi { me = 0, rng = new Lcg(5) };
            var ai1 = new EnemyAi { me = 1 };
            var sw = Stopwatch.StartNew(); double worst = 0;
            int turn = 0;
            for (; turn < 60 && !fight.Over; turn++)
            {
                var t0 = sw.Elapsed.TotalMilliseconds;
                var p0 = ai0.Plan(fight, Scenarios.TurnSec);
                var p1 = ai1.Plan(fight, Scenarios.TurnSec);
                worst = Math.Max(worst, (sw.Elapsed.TotalMilliseconds - t0) / 2);
                var r = fight.Resolve(p0, p1, Scenarios.TurnSec);
                var a = fight.planes[0]; var e = fight.planes[1];
                double range = (e.state.pos - a.state.pos).Length;
                if (turn < 12 || turn % 5 == 0 || fight.Over)
                    Console.WriteLine($"    turn {turn + 1,2}: range {range,6:F0} ft  P-51 {a.Kias,3:F0} KIAS {a.AltFt,5:F0} ft hp {fight.health[0],4:F1} {(p0.burstEnd > p0.burstStart ? "FIRE" : "    ")}" +
                                      $"  Spit {e.Kias,3:F0} KIAS {e.AltFt,5:F0} ft hp {fight.health[1],4:F1} {(p1.burstEnd > p1.burstStart ? "FIRE" : "    ")}  hits {r.hits.Count}");
            }
            string end = fight.crashed[0] ? "P-51 crashed" : fight.crashed[1] ? "Spitfire crashed" : fight.down[0] && fight.down[1] ? "both down" : fight.down[0] ? "P-51 shot down" : fight.down[1] ? "Spitfire shot down" : "no result";
            Console.WriteLine($"  AI vs AI: {end} after {turn} turns; slowest AI plan {worst:F0} ms");
            Check(worst < 1500, "an AI plan takes well under the time a player spends planning");
        }
        // Escort: the B-17 flies its route up a valley and bends with it; the Spitfire goes for it
        {
            var route = new[] { new Vec3d(0, -19700, 4100), new Vec3d(0, 16400, 4300), new Vec3d(8200, 26200, 5200), new Vec3d(13100, 34800, 5600) };
            var fight = Scenarios.Escort(route);
            var ai0 = new EnemyAi { me = 0, rng = new Lcg(5) };
            var ai1 = new EnemyAi { me = 1 };
            Console.WriteLine($"  B-17 starts at {fight.planes[2].Kias:F0} KIAS; the Spitfire goes for aircraft {ai1.Target(fight)}");
            Check(ai1.Target(fight) == 2, "the Spitfire picks the bomber");
            int turn = 0; double worst = 0; var sw = Stopwatch.StartNew();
            for (; turn < 80 && !fight.Over; turn++)
            {
                var t0 = sw.Elapsed.TotalMilliseconds;
                var p0 = ai0.Plan(fight, Scenarios.TurnSec);
                var p1 = ai1.Plan(fight, Scenarios.TurnSec);
                worst = Math.Max(worst, (sw.Elapsed.TotalMilliseconds - t0) / 2);
                var r = fight.Resolve(p0, p1, Scenarios.TurnSec);
                var bm = fight.planes[2];
                if (turn < 6 || turn % 5 == 0 || fight.Over)
                    Console.WriteLine($"    turn {turn + 1,2}: B-17 {bm.Kias,3:F0} KIAS {bm.AltFt,5:F0} ft heading {Math.Atan2(bm.state.vel.x, bm.state.vel.y) * D,4:F0} waypoint {fight.routes[2].next} hp {fight.health[2],4:F1}" +
                                      $"  Spit {(fight.planes[1].state.pos - bm.state.pos).Length,6:F0} ft off hp {fight.health[1],4:F1}  P-51 hp {fight.health[0],4:F1}  hits {r.hits.Count}");
            }
            string end = fight.routes[2].Done ? "the B-17 reached the target" : fight.Out(2) ? "the B-17 went down" : fight.Out(1) ? "the Spitfire went down" : fight.Out(0) ? "the P-51 went down" : "no result";
            Console.WriteLine($"  escort AI vs AI: {end} after {turn} turns; slowest AI plan {worst:F0} ms");
            Check(fight.Over && worst < 1500, "the escort fight ends, and the AI still plans quickly with three aircraft");

            // The bomber alone keeps to its route and its speed
            var solo = Scenarios.Escort(route);
            solo.planes[1] = new FlightModel(AircraftNumbers.SpitfireIX(), PointMass.Make(new Vec3d(-90000, 0, 20000), 300, 0));
            int t2 = 0; double minK = 999, maxK = 0;
            for (; t2 < 120 && !solo.routes[2].Done && !solo.Out(2); t2++)
            {
                solo.Resolve(TurnPlan.Holding(), TurnPlan.Holding(), Scenarios.TurnSec, false);
                if (t2 > 3) { minK = Math.Min(minK, solo.planes[2].Kias); maxK = Math.Max(maxK, solo.planes[2].Kias); }
            }
            Console.WriteLine($"  B-17 alone: route done {solo.routes[2].Done} after {t2} turns, {minK:F0} to {maxK:F0} KIAS");
            Check(solo.routes[2].Done && minK > 120 && maxK < 160, "the bomber flies its whole route near its cruise speed");
        }
        // Flak: a gun fires at a bomber flying straight past 2,500 ft away; some rounds should hit
        {
            var bm = new FlightModel(AircraftNumbers.B17G(), PointMass.Make(new Vec3d(-3000, 2500, 1500), Atmosphere.IasToTasKt(135, 1500), 0)) { throttle = 0.8 };
            var far = new FlightModel(AircraftNumbers.Bf109G(), PointMass.Make(new Vec3d(0, -90000, 20000), 300, 0));
            var fb = new Battle(far, CombatNumbers.Bf109G(), bm, CombatNumbers.B17G());
            fb.side[0] = 1; fb.side[1] = 0;   // the bomber is the flak's enemy
            fb.flak = new[] { new FlakSite { pos = new Vec3d(0, 0, 0), side = 1, phase = 2.9 } };
            int hits = 0, shots = 0; double missSum = 0; int missN = 0;
            for (int t = 0; t < 4; t++)
            {
                var r = fb.Resolve(TurnPlan.Holding(), TurnPlan.Holding(), 3);
                hits += r.hits.Count(h => h.shooter < 0); shots += r.traces.Count(tr => tr.owner < 0 && tr.startFrame > 0);
                foreach (var tr in r.traces) if (tr.owner < 0)
                    {
                        double best = double.PositiveInfinity;
                        for (int k = 0; k < tr.points.Count && tr.startFrame + k < r.frames[1].Length; k++) best = Math.Min(best, (tr.points[k] - r.frames[1][tr.startFrame + k].pos).Length);
                        if (best < 1e8) { missSum += best; missN++; }
                    }
            }
            Console.WriteLine($"  flak at a bomber passing 2,500 ft off: {hits} hits from {shots} packets; mean closest pass {missSum / Math.Max(1, missN):F0} ft; bomber hp {fb.health[1]:F1}");
            Check(hits > 0 && hits < shots / 2, "flak hits a steady bomber sometimes, not every round");
        }

        // B-17 gunners: a Bf 109 closing straight in on a lone B-17's tail gets shot at and hit; nobody on the bomber's side is hit
        {
            Vec3d bp = new Vec3d(0, 0, 12000);
            var bomber = new FlightModel(AircraftNumbers.B17G(), PointMass.Make(bp, Atmosphere.IasToTasKt(150, 12000), Math.PI / 2)) { throttle = 0.8 };
            Vec3d fp = bp + new Vec3d(0, -4000, 300);
            var foe = new FlightModel(AircraftNumbers.Bf109G(), PointMass.Make(fp, Atmosphere.IasToTasKt(280, 12000), Math.PI / 2)) { throttle = 1 };
            var gb = new Battle(bomber, CombatNumbers.B17G(), foe, CombatNumbers.Bf109G());
            gb.routes[0] = new Route { kias = 150, points = new[] { bp, bp + new Vec3d(0, 60000, 0) }, next = 1 };
            Scenarios.AddGunners(gb);
            int onFoe = 0, onOwn = 0, packets = 0;
            for (int t = 0; t < 3 && !gb.Out(1); t++)
            {
                var r = gb.Resolve(new TurnPlan?[] { null, new TurnPlan { aim = (gb.planes[0].state.pos - gb.planes[1].state.pos).Normalized, throttle = 1 } }, 3);
                onFoe += r.hits.Count(h => h.shooter < 0 && h.target == 1); onOwn += r.hits.Count(h => h.shooter < 0 && h.target == 0);
                packets += r.traces.Count(tr => tr.owner < 0 && tr.startFrame > 0);
            }
            Console.WriteLine($"  B-17 gunners at a Bf 109 closing on the tail: {packets} packets, {onFoe} hits on it, {onOwn} on the bomber; 109 hp {gb.health[1]:F1}");
            Check(onFoe > 0 && onOwn == 0, "the B-17's gunners hit a fighter closing on its tail, and never their own bomber");
        }

        // Missiles: from 4,000 ft behind a 109 flying straight it hits; one launched at a 109 already breaking hard across it can miss
        {
            FlightModel At(AircraftNumbers n, double y, double kias) => new FlightModel(n, PointMass.Make(new Vec3d(0, y, 10000), Atmosphere.IasToTasKt(kias, 10000), Math.PI / 2)) { throttle = 1 };
            var mb = new Battle(At(AircraftNumbers.P51D(), 0, 300), CombatNumbers.P51D(), At(AircraftNumbers.Bf109G(), 4000, 280), CombatNumbers.Bf109G());
            var shot = new TurnPlan { aim = new Vec3d(0, 1, 0), throttle = 1, missile = true, missileTarget = 1 };
            int mHits = 0;
            for (int t = 0; t < 3; t++)
            {
                var r = mb.Resolve(new TurnPlan?[] { t == 0 ? shot : TurnPlan.Straight(mb.planes[0]), TurnPlan.Straight(mb.planes[1]) }, 3);
                mHits += r.hits.Count(h => h.shooter == 0 && h.damage >= 5);
            }
            Console.WriteLine($"  missile at a straight-flying 109 from 4,000 ft behind: {mHits} hit, 109 down {mb.Out(1)}, missiles left {mb.missiles[0]}");
            Check(mHits == 1 && mb.missiles[0] == 1, "a missile fired from behind at a 109 flying straight hits it, and uses one of two");
            var far = new Battle(At(AircraftNumbers.P51D(), 0, 300), CombatNumbers.P51D(), At(AircraftNumbers.Bf109G(), 15000, 280), CombatNumbers.Bf109G());
            var rf = far.Resolve(new TurnPlan?[] { shot, TurnPlan.Straight(far.planes[1]) }, 3);
            Check(far.missiles[0] == 2 && !rf.traces.Any(tr => tr.missile), "no missile leaves while the target is out of range");
        }

        // The squadron battle in a twisting valley like the alpine map's: 8 v 8, 3 bombers, flak
        {
            const double F = 3.28084;
            var routeM = new[] { (0, 664, -9600), (0, 672, -7800), (0, 680, -6000), (634, 728, -4641), (634, 781, -3141), (0, 829, -1781), (0, 881, -281), (-634, 929, 1078), (-634, 982, 2578), (0, 1030, 3938), (0, 1083, 5438), (634, 1131, 6797), (634, 1180, 8297) };
            var route = routeM.Select(p => new Vec3d(p.Item1 * F, p.Item3 * F, p.Item2 * F)).ToArray();
            var flakM = new[] { (380, 403, -5267), (599, 455, -3816), (35, 557, -956), (-380, 609, 452), (-599, 658, 1903), (-317, 708, 3341), (35, 759, 4763), (599, 857, 7622) };
            var flak = flakM.Select(p => new Vec3d(p.Item1 * F, p.Item3 * F, p.Item2 * F)).ToArray();
            // the valley: a 140 m floor 300 m under the route, walls rising 2 in 1 to 1,000 m over the floor
            double Ground(double x, double y)
            {
                double best = double.PositiveInfinity, floor = 0;
                for (int i = 0; i + 1 < route.Length; i++)
                {
                    Vec3d a = route[i], b = route[i + 1];
                    double dx = b.x - a.x, dy = b.y - a.y, u = Math.Max(i == 0 ? -1 : 0, Math.Min(1, ((x - a.x) * dx + (y - a.y) * dy) / (dx * dx + dy * dy)));   // the valley runs on back past the start
                    double d = Math.Sqrt(Math.Pow(x - a.x - u * dx, 2) + Math.Pow(y - a.y - u * dy, 2));
                    if (d < best) { best = d; floor = a.z + (b.z - a.z) * u - 300 * F; }
                }
                return floor + Math.Min(1000 * F, Math.Max(0, best - 230) * 2.0);
            }
            var fight = Scenarios.Squadron(route, flak);
            fight.groundFt = Ground;
            int n = fight.Count;
            Console.WriteLine($"  squadron battle: {n} aircraft, {fight.flak.Length} flak guns; bombers at {fight.planes[2].Kias:F0} KIAS");
            double minClearStart = double.PositiveInfinity;
            for (int i = 0; i < n; i++) minClearStart = Math.Min(minClearStart, fight.planes[i].AltFt - Ground(fight.planes[i].state.pos.x, fight.planes[i].state.pos.y));
            Console.WriteLine($"  everyone starts at least {minClearStart:F0} ft over the valley floor or walls");
            for (int i = 0; i < n; i++) { var q = fight.planes[i].state.pos; double c = q.z - Ground(q.x, q.y); if (c < 300) Console.WriteLine($"    aircraft {i} ({fight.planes[i].numbers.name}) at ({q.x:F0}, {q.y:F0}, {q.z:F0}) ft is {c:F0} ft over the ground"); }
            Check(minClearStart > 300, "the squadron battle starts with everyone clear of the ground");
            var ais = new EnemyAi[n];
            for (int i = 0; i < n; i++)
            {
                if (fight.routes[i] != null) continue;
                bool ours = fight.side[i] == 0;
                ais[i] = new EnemyAi { me = i, rng = new Lcg((uint)(11 + i * 7)), role = ours ? (i % 2 == 0 ? EnemyAi.Role.Escort : EnemyAi.Role.Fighter) : (i % 2 == 0 ? EnemyAi.Role.Hunter : EnemyAi.Role.Fighter) };
            }
            double worstSlot = 0; int formedTurns = 0;
            var sw = Stopwatch.StartNew(); double worstTurn = 0; int turn = 0, flakHits = 0, flakOnBombers = 0, ground = 0;
            for (; turn < 40 && !fight.Over; turn++)
            {
                var t0 = sw.Elapsed.TotalMilliseconds;
                var plans = new TurnPlan?[n];
                bool wasFormed = !fight.formationBroken;
                fight.CheckBreak();
                if (wasFormed && fight.formationBroken) Console.WriteLine($"    turn {turn + 1}: the formation breaks, nearest foe {Nearest(fight):F0} ft; widest gap from a slot so far {worstSlot:F0} ft");
                if (!fight.formationBroken)
                    for (int i = 0; i < n; i++)
                        if (fight.slots[i] != null && !fight.Out(i))
                        {
                            var sl = fight.slots[i]; var l = fight.planes[sl.leader];
                            worstSlot = Math.Max(worstSlot, (sl.Where(l.state.pos, l.state.vel) - fight.planes[i].state.pos).Length);
                        }
                System.Threading.Tasks.Parallel.For(0, n, i => { if (ais[i] != null && !fight.Out(i) && !fight.InFormation(i)) plans[i] = ais[i].Plan(fight, Scenarios.TurnSec); });
                worstTurn = Math.Max(worstTurn, sw.Elapsed.TotalMilliseconds - t0);
                var wasDown = (bool[])fight.down.Clone(); var hpBefore = (double[])fight.health.Clone();
                if (!fight.formationBroken) formedTurns++;
                var r = fight.Resolve(plans, Scenarios.TurnSec);
                flakHits += r.hits.Count(h => h.shooter < 0); flakOnBombers += r.hits.Count(h => h.shooter < 0 && fight.routes[h.target] != null);
                for (int i = 0; i < n; i++)
                    if (!wasDown[i] && fight.down[i])
                    {
                        var q = fight.planes[i].state.pos;
                        string why = fight.crashed[i] ? (fight.health[i] < hpBefore[i] ? "shot, then hit the ground" : "flew into the ground") : fight.health[i] <= 0 ? "shot down" : "collided";
                        Console.WriteLine($"      turn {turn + 1}: {i} {fight.planes[i].numbers.name} {why} at {q.z - Ground(q.x, q.y):F0} ft clear, {fight.planes[i].Kias:F0} KIAS, heading {Math.Atan2(fight.planes[i].state.vel.x, fight.planes[i].state.vel.y) * D:F0}");
                    }
                if (true)
                {
                    int ours = 0, theirs = 0, bombers = 0;
                    for (int i = 0; i < n; i++) { if (fight.Out(i)) continue; if (fight.routes[i] != null) bombers++; else if (fight.side[i] == 0) ours++; else theirs++; }
                    Console.WriteLine($"    turn {turn + 1,2}: P-51s {ours} Bf 109s {theirs} B-17s {bombers} (lead at waypoint {fight.routes[2].next}, {fight.planes[2].Kias:F0} KIAS)  hits {r.hits.Count} ({r.hits.Count(h => h.shooter < 0)} flak), packets {r.traces.Count(t => t.owner >= 0 && t.startFrame > 0)} guns {r.traces.Count(t => t.owner < 0 && t.startFrame > 0)} flak, nearest foe {Nearest(fight):F0} ft  AI planning {sw.Elapsed.TotalMilliseconds - t0:F0} ms");
                }
            }
            for (int i = 0; i < n; i++) if (fight.crashed[i]) ground++;
            Console.WriteLine($"  squadron AI vs AI: over {fight.Over} after {turn} turns; {ground} flew into the ground; flak hit {flakHits} times ({flakOnBombers} on bombers); slowest turn's planning {worstTurn:F0} ms for {ais.Count(a => a != null)} AI pilots on {Environment.ProcessorCount} cores");
            Check(worstTurn < 3000, "all the AI pilots plan a turn in under 3 s together");
            Console.WriteLine($"  close formation: held {formedTurns} turns, widest gap from a slot {worstSlot:F0} ft");
            Check(formedTurns >= 2 && worstSlot < 400, "the escorts hold close formation (within 400 ft of their slots) until the Bf 109s arrive, then break");
        }
        return fails;
    }
}
