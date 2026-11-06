// Checks of the radar and the radar missile, without Unity: the radar locks after a whole turn with the target in its
// cone (and not a warbird's, nor through stealth), the missile hits a target that runs, the notch beats it, the shooter
// turning away drops it, chaff breaks it more often in the notch, and the radar's state survives a clone, a subset, a swap
// and the trip to bytes. The AI fires them and notches them.
// Usage: dotnet run   (exit code = failures)
using System;
using System.Linq;
using Ooda.Flight.Core;
using Ooda.Turn.Core;

static class Program
{
    static int fails;
    static void Check(bool ok, string what) { if (!ok) fails++; Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}"); }

    /// <summary>Heading in degrees from north, clockwise (x east, y north).</summary>
    static FlightModel At(AircraftNumbers n, double x, double y, double kias, double headingDeg = 0) =>
        new FlightModel(n, PointMass.Make(new Vec3d(x, y, 15000), Atmosphere.IasToTasKt(kias, 15000), (90 - headingDeg) * Math.PI / 180)) { throttle = 1 };

    static Vec3d Dir(double headingDeg) => new Vec3d(Math.Sin(headingDeg * Math.PI / 180), Math.Cos(headingDeg * Math.PI / 180), 0);

    /// <summary>An F-4 at the origin heading north, its target gap ft north of it on a heading.</summary>
    static Battle Setup(double gap, double targetHeading, uint seed = 1, AircraftNumbers tn = null, CombatNumbers tc = null)
    {
        var b = new Battle(At(AircraftNumbers.Phantom(), 0, 0, 450), CombatNumbers.Phantom(),
                           At(tn ?? AircraftNumbers.Phantom(), 0, gap, 400, targetHeading), tc ?? CombatNumbers.Phantom())
        { rng = new Lcg(seed * 2654435761u) };
        return b;
    }

    static TurnPlan Fly(double headingDeg, bool radar = false, int target = 1, bool hold = false) =>
        new TurnPlan { aim = Dir(headingDeg), throttle = 1, radarMissile = radar, radarTarget = target, holdFlares = hold };

    /// <summary>Lock for a turn (both flying straight), then fire and fly turns more; the plans after firing from shooter(t) and target(t).</summary>
    static (bool hit, Missile ms) Shoot(Battle b, Func<int, TurnPlan> shooter, Func<int, TurnPlan> target, int turns = 4)
    {
        b.Resolve(new TurnPlan?[] { Fly(0), target(-1) }, 3);
        Missile ms = null; bool hit = false;
        for (int t = 0; t < turns; t++)
        {
            var p = shooter(t); if (t == 0) p.radarMissile = true;
            var r = b.Resolve(new TurnPlan?[] { p, target(t) }, 3);
            ms ??= b.bullets.OfType<Missile>().FirstOrDefault(x => x.radar);
            hit |= r.hits.Any(h => h.shooter == 0 && h.damage >= 5);
        }
        return (hit, ms);
    }

    static int Main()
    {
        Console.WriteLine("Lock");
        {
            var b = Setup(15000, 0);
            b.Resolve(new TurnPlan?[] { Fly(0, true), Fly(0) }, 1.5);
            Check(b.radarTrack[0] == 1 && !b.Locked(0) && b.radarMissiles[0] == 2, $"half a turn in the cone: tracking, not locked yet ({b.trackSec[0]:F2} s), nothing fired");
            b.Resolve(new TurnPlan?[] { Fly(0), Fly(0) }, 1.5);
            Check(b.Locked(0), $"a whole turn in the cone: locked ({b.trackSec[0]:F2} s)");
            Check(b.radarTrack[1] == -1, "the target, heading away, has the shooter behind it: not in its cone");
            var p51 = new Battle(At(AircraftNumbers.P51D(), 0, 0, 300), CombatNumbers.P51D(), At(AircraftNumbers.Bf109G(), 0, 5000, 280), CombatNumbers.Bf109G());
            p51.Resolve(new TurnPlan?[] { Fly(0, true), Fly(0) }, 3);
            Check(p51.radarTrack[0] == -1 && p51.radarMissiles[0] == 0, "a warbird has no radar and no radar missiles");
            var off = Setup(15000, 0);
            off.planes[1].state.pos = new Vec3d(15000, 8000, 15000);   // 62 degrees off the nose
            off.Resolve(new TurnPlan?[] { Fly(0), Fly(0) }, 3);
            Check(off.radarTrack[0] == -1, "outside the 60 degree cone: no track");
            var far = Setup(31000, 180);
            far.Resolve(new TurnPlan?[] { Fly(0), Fly(180) }, 3);
            Check(far.radarTrack[0] == 1, "out past 30,000 ft it picks them up as they close");
            var sneak = Setup(15000, 0);
            sneak.cloakUntil[1] = 100;
            sneak.Resolve(new TurnPlan?[] { Fly(0), Fly(0) }, 3);
            Check(sneak.radarTrack[0] == -1, "a stealthy target doesn't show");
            var swap = Setup(15000, 0);
            swap.Add(At(AircraftNumbers.Phantom(), -3000, 14000, 400), CombatNumbers.Phantom(), 1);
            swap.Resolve(new TurnPlan?[] { Fly(0, false, 1), Fly(0), Fly(0) }, 2);
            swap.Resolve(new TurnPlan?[] { Fly(0, false, 2), Fly(0), Fly(0) }, 1.5);
            Check(swap.radarTrack[0] == 2 && swap.trackSec[0] < 1.6, $"asking for another target starts the lock over ({swap.trackSec[0]:F2} s on it)");
        }

        Console.WriteLine("\nThe missile");
        {
            var b = Setup(15000, 0);
            var (hit, ms) = Shoot(b, t => Fly(0), t => Fly(0, hold: true));
            Check(ms != null && b.radarMissiles[0] == 1, "locked, it fires one, one left");
            Check(hit, $"a target running straight away is hit ({ms?.lost})");
            var notLocked = Setup(15000, 0);
            notLocked.Resolve(new TurnPlan?[] { Fly(0, true), Fly(0) }, 2.9);
            Check(notLocked.radarMissiles[0] == 2, "not a whole turn yet: it holds fire");

            var beam = Setup(12000, 90);
            var (hitB, msB) = Shoot(beam, t => Fly(0), t => Fly(90, hold: true));
            Check(!hitB && msB != null && msB.lost == MissileLoss.Notch, $"a target flying square across the line of sight notches it ({msB?.lost})");

            var shallow = Setup(12000, 45);
            var (hitS, msS) = Shoot(shallow, t => Fly(0), t => Fly(45, hold: true));
            Check(hitS, $"45 degrees off isn't a notch: hit ({msS?.lost})");

            var away = Setup(15000, 0);
            var (hitA, msA) = Shoot(away, t => t == 0 ? Fly(0) : Fly(120), t => Fly(0, hold: true));
            Check(!hitA && msA != null && msA.lost == MissileLoss.Radar, $"the shooter turning away drops the lock and the missile ({msA?.lost})");

            var beamFlares = Setup(12000, 0);
            var (hitF, msF) = Shoot(beamFlares, t => Fly(0), t => Fly(0, hold: false));
            Check(beamFlares.flares[1] == CombatNumbers.Phantom().flares, "flares never go at a radar missile");
        }

        Console.WriteLine("\nChaff");
        {
            int n = 60, broke = 0, brokeNotch = 0;
            for (uint s = 1; s <= n; s++)
            {
                var a = Setup(15000, 0, s);
                var (_, ms) = Shoot(a, t => Fly(0), t => Fly(0));
                if (ms != null && ms.lost == MissileLoss.Chaff) broke++;
                // in the notch only long enough for the chaff, not the notch itself
                double p = Radar.ChaffBreak(true);
                var rng = new Lcg(s * 7919u);
                if (rng.Next() < p || rng.Next() < p) brokeNotch++;
            }
            double expect = 1 - Math.Pow(1 - Radar.ChaffChance, Radar.ChaffPerMissile);
            Console.WriteLine($"  of {n} missiles at a target running straight away letting its chaff go, {broke} broken (expected about {expect * n:F0}); in the notch a pair would break {brokeNotch}");
            Check(broke > n * expect * 0.5 && broke < n * expect * 1.6, "a pair of chaff breaks about a third of them");
            Check(brokeNotch > broke * 1.8, "in the notch chaff works far better");
            var held = Setup(15000, 0);
            Shoot(held, t => Fly(0), t => Fly(0, hold: true));
            Check(held.chaff[1] == CombatNumbers.Phantom().chaff, "held, none goes");
            var used = Setup(15000, 0, 5);
            Shoot(used, t => Fly(0), t => Fly(0));
            Check(CombatNumbers.Phantom().chaff - used.chaff[1] <= Radar.ChaffPerMissile && used.chaff[1] < CombatNumbers.Phantom().chaff, $"at most a pair at one missile ({CombatNumbers.Phantom().chaff - used.chaff[1]})");
        }

        Console.WriteLine("\nClones, subsets, swaps and bytes");
        {
            var b = Setup(15000, 0);
            b.Resolve(new TurnPlan?[] { Fly(0), Fly(0) }, 3);
            b.Resolve(new TurnPlan?[] { Fly(0, true), Fly(0, hold: true) }, 0.5);
            Check(b.bullets.Any(x => x is Missile m && m.radar), "a radar missile is in the air");
            var c = b.Clone();
            Check(c.radarTrack[0] == 1 && c.Locked(0) && !ReferenceEquals(c.radarTrack, b.radarTrack) && c.radarMissiles[0] == 1, "a clone keeps the lock and the count, in its own arrays");
            var sub = b.Subset(new[] { 1 });
            var sm = sub.bullets.OfType<Missile>().FirstOrDefault(x => x.radar);
            Check(sm != null && sm.target == 0 && sub.Illuminating(sm.owner, 0), "a subset without the shooter keeps the missile guiding on the target");
            var both = b.Subset(new[] { 1, 0 });
            Check(both.radarTrack[1] == 0 && both.Locked(1), "a subset renumbers the lock");
            var back = Setup(15000, 0);
            Wire.Restore(back, Wire.Encode(b));
            Check(Wire.Hash(back) == Wire.Hash(b) && back.Locked(0) && back.bullets.Any(x => x is Missile m && m.radar), "a snapshot brings the lock and the missile across exactly");
            var r1 = b.Resolve(new TurnPlan?[] { Fly(0), Fly(0) }, 3);
            var r2 = back.Resolve(new TurnPlan?[] { Fly(0), Fly(0) }, 3);
            Check(Wire.Hash(back) == Wire.Hash(b) && r1.chaff.Count == r2.chaff.Count, $"and both fly the next turn the same ({r1.chaff.Count} chaff clouds)");
            var sw = Setup(15000, 0);
            sw.Resolve(new TurnPlan?[] { Fly(0), Fly(0) }, 3);
            sw.Swap(0, 1);
            Check(sw.radarTrack[1] == 0 && sw.Locked(1) && sw.radarTrack[0] == -1, "a swap moves the lock with the plane and renumbers its target");
            var plan = new TurnPlan { radarMissile = true, radarTarget = 3 };
            var pb = Wire.Decode<TurnPlan>(Wire.Encode(plan));
            Check(pb.radarMissile && pb.radarTarget == 3, "a plan with a radar shot survives the trip to bytes");
        }

        Console.WriteLine("\nThe AI");
        {
            var b = Setup(15000, 0);
            b.planes[1].state = PointMass.Make(new Vec3d(0, 15000, 15000), Atmosphere.IasToTasKt(400, 15000), -Math.PI / 2);   // coming at us
            var ai = new EnemyAi { me = 0, sloppiness = 0 };
            int fired = 0;
            for (int t = 0; t < 3; t++)
            {
                var p = ai.Plan(b, 3);
                if (p.radarMissile) fired++;
                b.Resolve(new TurnPlan?[] { p, Fly(180, hold: true) }, 3);
                if (b.bullets.Any(x => x is Missile m && m.radar)) break;
            }
            Check(b.bullets.Any(x => x is Missile m && m.radar) || b.radarMissiles[0] < 2, "an AI F-4 with a target in its cone locks and fires");

            int notched = 0, straightHit = 0, n = 6;
            for (uint s = 1; s <= n; s++)
            {
                var d = Setup(16000, 0, s);
                d.Resolve(new TurnPlan?[] { Fly(0), Fly(0) }, 3);
                d.Resolve(new TurnPlan?[] { Fly(0, true), Fly(0, hold: true) }, 0.1);
                var def = new EnemyAi { me = 1, sloppiness = 0, rng = new Lcg(s) };
                bool hit = false;
                for (int t = 0; t < 4; t++)
                {
                    var p = def.Plan(d, 3); p.holdFlares = true;
                    p.radarMissile = false;
                    var r = d.Resolve(new TurnPlan?[] { Fly(0), p }, 3);
                    hit |= r.hits.Any(h => h.shooter == 0 && h.damage >= 5);
                }
                var ms = d.bullets.OfType<Missile>().FirstOrDefault(x => x.radar);
                if (!hit) notched++;
                Console.WriteLine($"    seed {s}: {(hit ? "hit" : "survived")} ({ms?.lost.ToString() ?? "missile gone"})");
            }
            Check(notched >= n / 2, $"an AI F-4 with a radar missile on it gets out of the way most of the time ({notched} of {n})");
        }

        Console.WriteLine(fails == 0 ? "\nAll checks pass." : $"\n{fails} check(s) FAILED.");
        return fails;
    }
}
