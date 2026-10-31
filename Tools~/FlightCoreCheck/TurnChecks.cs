// Checks of the turn engine a pilot would recognise: the shell is bigger toward a pull than a push,
// the autopilot gets the nose where it was asked without departing, rounds drop and hit what is ahead,
// and two AI pilots fight to a finish without the engine breaking.
using System;
using System.Diagnostics;
using Ooda.Flight.Core;
using Ooda.Turn.Core;

static class TurnChecks
{
    const double D = 180 / Math.PI;

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
        return fails;
    }
}
