// Checks of the heat-seeker against heat and flares, without Unity: the seeker locks farther on a hot aircraft, a flare
// pair pulls it off often (less often off a jet in afterburner), held flares stay on board, and flares survive a clone,
// a subset and the trip to bytes.
// Usage: dotnet run   (exit code = failures)
using System;
using System.Linq;
using Ooda.Flight.Core;
using Ooda.Turn.Core;

static class Program
{
    static int fails;
    static void Check(bool ok, string what) { if (!ok) fails++; Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}"); }

    static FlightModel At(AircraftNumbers n, double y, double kias, double throttle = 1) =>
        new FlightModel(n, PointMass.Make(new Vec3d(0, y, 10000), Atmosphere.IasToTasKt(kias, 10000), Math.PI / 2)) { throttle = throttle };

    /// <summary>A P-51 behind a target at gap ft, both flying straight, the P-51 firing a missile; the target's flares held or not.</summary>
    static Battle Chase(AircraftNumbers tn, CombatNumbers tc, double gap, uint seed, bool hold, bool wep = false, double throttle = 1)
    {
        var shooter = CombatNumbers.P51D(); shooter.missiles = 2;
        var b = new Battle(At(AircraftNumbers.P51D(), 0, 300), shooter, At(tn, gap, 280, throttle), tc) { rng = new Lcg(seed * 2654435761u) };
        for (int k = 0; k < 5; k++) b.rng.Next();   // (a fight's dice have rolled for the guns by the time a missile flies)
        b.planes[1].wepOn = wep;
        return b;
    }

    static (bool hit, int flaresUsed, bool decoyed) Fly(Battle b, bool hold, bool wep = false, double throttle = 1)
    {
        var shot = new TurnPlan { aim = new Vec3d(0, 1, 0), throttle = 1, missile = true, missileTarget = 1 };
        int start = b.flares[1];
        bool hit = false, decoyed = false;
        for (int t = 0; t < 4; t++)
        {
            var them = new TurnPlan { aim = b.planes[1].Forward, throttle = throttle, wep = wep, holdFlares = hold };
            var r = b.Resolve(new TurnPlan?[] { t == 0 ? shot : TurnPlan.Straight(b.planes[0]), them }, 3,
                              onStep: (bb, k) => { foreach (var x in bb.bullets) if (x is Missile ms && ms.decoy >= 0) decoyed = true; });
            hit |= r.hits.Any(h => h.shooter == 0 && h.damage >= 5);
        }
        return (hit, start - b.flares[1], decoyed);
    }

    static int Main()
    {
        Console.WriteLine("Heat");
        {
            var p51 = CombatNumbers.P51D(); var f4 = CombatNumbers.Phantom();
            double cold = Heat.Engine(p51, 0, false, false), full = Heat.Engine(p51, 1, false, false), burner = Heat.Engine(f4, 1, true, false);
            Check(cold < full && full == 1 && burner > 4, $"a P-51 at idle {cold:F2}, full throttle {full:F2}, an F-4 in afterburner {burner:F2}");
            var fwd = new Vec3d(0, 1, 0);
            Check(Heat.Aspect(fwd, new Vec3d(0, -1, 0)) == 1 && Heat.Aspect(fwd, fwd) == 0.25, "all the heat shows from astern, a quarter from ahead");
            Check(Heat.LockRangeFt(1) == Missile.MaxRangeFt && Heat.LockRangeFt(burner) > Heat.LockRangeFt(1) && Heat.LockRangeFt(cold) < Heat.LockRangeFt(1),
                  $"lock range: {Heat.LockRangeFt(cold):F0} ft at idle, {Heat.LockRangeFt(1):F0} at full throttle, {Heat.LockRangeFt(burner):F0} on a burner");
        }

        Console.WriteLine("\nLock range follows the heat");
        {
            var hot = new Battle(At(AircraftNumbers.P51D(), 0, 300), CombatNumbers.P51D(), At(AircraftNumbers.Phantom(), 11500, 300), CombatNumbers.Phantom());
            hot.planes[1].wepOn = true;
            Check(hot.LockTarget(0) == 1, "an F-4 in afterburner locks from 11,500 ft behind");
            hot.planes[1].wepOn = false; hot.planes[1].throttle = 0;
            Check(hot.LockTarget(0) == -1, "the same F-4 at idle doesn't");
            var idle = new Battle(At(AircraftNumbers.P51D(), 0, 300), CombatNumbers.P51D(), At(AircraftNumbers.Bf109G(), 7500, 280, 0), CombatNumbers.Bf109G());
            Check(idle.LockTarget(0) == -1, "a 109 at idle 7,500 ft ahead is too cool to lock");
            idle.planes[1].throttle = 1;
            Check(idle.LockTarget(0) == 1, "at full throttle it locks");
        }

        Console.WriteLine("\nFlares");
        {
            int n = 40, hitsHeld = 0, hitsAuto = 0, hitsBurner = 0, decoys = 0, most = 0; bool heldKept = true;
            for (uint s = 1; s <= n; s++)
            {
                var a = Fly(Chase(AircraftNumbers.Bf109G(), CombatNumbers.Bf109G(), 4000, s, true), true);
                if (a.hit) hitsHeld++;
                heldKept &= a.flaresUsed == 0;
                var b = Fly(Chase(AircraftNumbers.Bf109G(), CombatNumbers.Bf109G(), 4000, s, false), false);
                if (b.hit) hitsAuto++;
                if (b.decoyed) decoys++;
                most = Math.Max(most, b.flaresUsed);
                var c = Fly(Chase(AircraftNumbers.Phantom(), CombatNumbers.Phantom(), 4000, s, false, true), false, true);
                if (c.hit) hitsBurner++;
            }
            Console.WriteLine($"  of {n} missiles from 4,000 ft behind: {hitsHeld} hit a 109 holding its flares, {hitsAuto} one letting them go ({decoys} decoyed), {hitsBurner} an F-4 in afterburner letting them go");
            Check(hitsHeld == n && heldKept, "held flares stay on board and every missile hits");
            Check(hitsAuto < n / 2 && decoys > n / 2, "a pair of flares pulls most missiles off a 109");
            Check(most == Heat.FlaresPerMissile, $"at most a pair go at one missile ({most})");
            Check(hitsBurner > hitsAuto, "flares work less well for a jet in afterburner");
        }

        Console.WriteLine("\nFlares in clones, subsets and bytes");
        {
            var b = Chase(AircraftNumbers.Bf109G(), CombatNumbers.Bf109G(), 4000, 3, false);
            var shot = new TurnPlan { aim = new Vec3d(0, 1, 0), throttle = 1, missile = true, missileTarget = 1 };
            // fly until a flare is in the air
            for (int t = 0; t < 2 && !b.bullets.Any(x => x is Flare); t++)
                b.Resolve(new TurnPlan?[] { t == 0 ? shot : TurnPlan.Straight(b.planes[0]), TurnPlan.Straight(b.planes[1]) }, 1.5);
            bool flareUp = b.bullets.Any(x => x is Flare);
            Check(flareUp, $"a flare is in the air ({b.bullets.Count(x => x is Flare)}), {b.flares[1]} left");
            var c = b.Clone();
            Check(c.flares[1] == b.flares[1] && c.bullets.Count(x => x is Flare) == b.bullets.Count(x => x is Flare) && !ReferenceEquals(c.flares, b.flares), "a clone keeps them, in its own arrays");
            var sub = b.Subset(new[] { 1 });
            Check(sub.flares[0] == b.flares[1] && sub.bullets.Any(x => x is Flare), "a subset keeps the target's flares and count");
            var back = Chase(AircraftNumbers.Bf109G(), CombatNumbers.Bf109G(), 4000, 3, false);
            Wire.Restore(back, Wire.Encode(b));
            Check(Wire.Hash(back) == Wire.Hash(b) && back.bullets.Any(x => x is Flare), "a snapshot brings them across exactly");
            var r1 = b.Resolve(new TurnPlan?[] { TurnPlan.Straight(b.planes[0]), TurnPlan.Straight(b.planes[1]) }, 3);
            var r2 = back.Resolve(new TurnPlan?[] { TurnPlan.Straight(back.planes[0]), TurnPlan.Straight(back.planes[1]) }, 3);
            Check(Wire.Hash(back) == Wire.Hash(b) && r1.flares.Count == r2.flares.Count && r1.flares.Count > 0, $"and both fly the next turn the same ({r1.flares.Count} flare paths recorded)");
            b.Swap(0, 1);
            Check(b.flares[0] == back.flares[1], "a swap moves the flares with the plane");
            var plan = new TurnPlan { holdFlares = true, missileTarget = 2 };
            Check(Wire.Decode<TurnPlan>(Wire.Encode(plan)).holdFlares, "a plan holding its flares survives the trip to bytes");
        }

        Console.WriteLine(fails == 0 ? "\nAll checks pass." : $"\n{fails} check(s) FAILED.");
        return fails;
    }
}
