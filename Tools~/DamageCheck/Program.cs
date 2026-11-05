// Checks of systems damage without Unity (see Flight/Core/Systems.cs and Battle.Hit): where a round lands decides the
// system it knocks at (dead astern the tail, head-on the engine); a damaged engine, controls or wings make the aircraft
// fly worse; a holed tank leaks until the engine quits; a blast wears every system; any system knocked out brings the
// aircraft down, and the hit that does it says so; and the damage survives a clone, a swap, a subset and the trip to bytes.
// Usage: dotnet run   (exit code = failures)
using System;
using System.Linq;
using Ooda.Flight.Core;
using Ooda.Turn.Core;

static class Program
{
    static int fails;
    static void Check(bool ok, string what) { if (!ok) fails++; Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}"); }

    const double North = Math.PI / 2, South = -Math.PI / 2;

    static FlightModel At(AircraftNumbers n, double x, double y, double kias, double heading = North) =>
        new FlightModel(n, PointMass.Make(new Vec3d(x, y, 10000), Atmosphere.IasToTasKt(kias, 10000), heading)) { throttle = 1 };

    /// <summary>A P-51 (0) gap ft behind a 109 (1), both heading north.</summary>
    static Battle Astern(double gap) =>
        new Battle(At(AircraftNumbers.P51D(), 0, 0, 300), CombatNumbers.P51D(), At(AircraftNumbers.Bf109G(), 0, gap, 300), CombatNumbers.Bf109G()) { dispersion = false };

    static TurnPlan Ahead(Battle b, int i) => TurnPlan.Straight(b.planes[i]);

    static TurnPlan Fire(Battle b, int i, double from, double to) { var p = Ahead(b, i); p.burstStart = from; p.burstEnd = to; return p; }

    static string Sys(Systems s) => $"engine {s.engine:F2} fuel {s.fuel:F2} controls {s.controls:F2} wings {s.wings:F2}";

    static int Main()
    {
        Console.WriteLine("Where a round lands");
        {
            Check(Systems.Struck(0, 20, 2) == SystemId.Engine && Systems.Struck(1, -20, 3) == SystemId.Controls
                  && Systems.Struck(18, 2, 3) == SystemId.Wings && Systems.Struck(-18, -3, 1) == SystemId.Wings
                  && Systems.Struck(2, 3, 14) == SystemId.Fuel && Systems.Struck(3, -2, -14) == SystemId.Fuel,
                  "the nose is the engine, the tail the controls, the wings out to the side, the tanks above and below");

            var b = Astern(700);
            var r = b.Resolve(new TurnPlan?[] { Fire(b, 0, 0, 0.15), Ahead(b, 1) }, 3);
            var on = r.hits.Where(h => h.shooter == 0 && h.target == 1).ToList();
            Check(on.Count > 0 && on.All(h => h.system == (int)SystemId.Controls), $"dead astern: {on.Count} hits, all on the tail");
            var s = b.SystemsOf(1);
            Check(s.controls > 0 && s.engine == 0 && s.fuel == 0 && s.wings == 0, $"so only the controls are worn ({Sys(s)})");
            Check(Math.Abs(s.controls - on.Sum(h => h.wear)) < 1e-9 && Math.Abs(on[0].wear - b.combat[0].damage / b.Capacity(1, SystemId.Controls)) < 1e-9,
                  $"each hit wears it by its damage over the system's share of the toughness ({on[0].wear:F3})");

            var h2 = new Battle(At(AircraftNumbers.P51D(), 0, 0, 300), CombatNumbers.P51D(), At(AircraftNumbers.Bf109G(), 0, 2600, 300, South), CombatNumbers.Bf109G()) { dispersion = false };
            var rh = h2.Resolve(new TurnPlan?[] { Fire(h2, 0, 0, 0.15), Ahead(h2, 1) }, 1);
            var hon = rh.hits.Where(h => h.shooter == 0 && h.target == 1).ToList();
            Check(hon.Count > 0 && hon.All(h => h.system == (int)SystemId.Engine), $"head-on: {hon.Count} hits, all on the engine");
        }

        Console.WriteLine("\nKnocked out");
        {
            var b = Astern(700);
            var r = b.Resolve(new TurnPlan?[] { Fire(b, 0, 0, 1.5), Ahead(b, 1) }, 3);
            var on = r.hits.Where(h => h.shooter == 0 && h.target == 1).ToList();
            var s = b.SystemsOf(1);
            Check(b.down[1] && s.Lost && s.LostTo == SystemId.Controls, $"a long burst from astern shoots the tail away ({on.Count} hits)");
            Check(on.Count(h => h.downed) == 1, "and exactly one hit is the one that brought it down");
            int needed = (int)Math.Ceiling(b.Capacity(1, SystemId.Controls) / b.combat[0].damage);
            Console.WriteLine($"  (info) {needed} P-51 packets knock out a 109's tail, engine or tanks; {Math.Ceiling(b.Capacity(1, SystemId.Wings) / b.combat[0].damage)} its wings");

            var c = Astern(700);
            c.Spare(1);
            var rc = c.Resolve(new TurnPlan?[] { Fire(c, 0, 0, 1.5), Ahead(c, 1) }, 3);
            Check(rc.hits.Count(h => h.target == 1) == on.Count && !c.SystemsOf(1).Hurt && !c.down[1], "a spared target is hit as often but takes nothing (the AI's and the preview's what-if)");
        }

        Console.WriteLine("\nDamaged, it flies worse");
        {
            double Speed(Systems s)
            {
                var m = At(AircraftNumbers.P51D(), 0, 0, 300); m.systems = s;
                for (int k = 0; k < 2000; k++) m.Step(new FlightControls { throttle = 1, wep = true }, 0.02);
                return m.Kias;
            }
            var hurt = new Systems(); hurt.Wear(SystemId.Engine, 0.55);
            double whole = Speed(new Systems()), slow = Speed(hurt);
            Check(slow < whole - 30, $"engine damaged: 40 s at full throttle and WEP makes {slow:F0} KIAS against {whole:F0}");
            var m2 = At(AircraftNumbers.P51D(), 0, 0, 300); m2.systems = hurt;
            m2.Step(new FlightControls { throttle = 1, wep = true }, 0.02);
            Check(!m2.wepOn, "and WEP won't go in");

            double Roll(Systems s, out double g)
            {
                var m = At(AircraftNumbers.P51D(), 0, 0, 350); m.systems = s;
                double most = 0; g = 0;
                for (int k = 0; k < 100; k++) { m.Step(new FlightControls { roll = 1, pitch = 1, throttle = 1 }, 0.02); most = Math.Max(most, m.rollRateDps); g = Math.Max(g, m.g); }
                return most;
            }
            var ctl = new Systems(); ctl.Wear(SystemId.Controls, 0.6);
            double r0 = Roll(new Systems(), out double g0), r1 = Roll(ctl, out double g1);
            Check(r1 < 0.6 * r0 && g1 < 0.8 * g0, $"controls damaged: rolls at {r1:F0} deg/s (whole {r0:F0}) and pulls {g1:F1} G (whole {g0:F1})");
            var wing = new Systems(); wing.Wear(SystemId.Wings, 0.6);
            double rw = Roll(wing, out double gw), sw = Speed(wing);
            Check(gw < 0.85 * g0 && sw < whole - 10, $"wings damaged: pulls {gw:F1} G and makes {sw:F0} KIAS");
            Check(Math.Abs(Speed(new Systems()) - whole) < 1e-9, "a whole aircraft flies exactly as before");
        }

        Console.WriteLine("\nA holed tank");
        {
            var b = Astern(4000);
            b.planes[1].systems.Wear(SystemId.Fuel, 0.55);
            Check(b.SystemsOf(1).Leaking && b.SystemsOf(1).leakLeftSec == Systems.LeakSec, $"holed: it leaks, {Systems.LeakSec:F0} s of fuel left");
            int turns = 0;
            while (!b.down[1] && turns < 20) { b.Resolve(new TurnPlan?[] { Ahead(b, 0), Ahead(b, 1) }, 3); turns++; }
            Check(b.down[1] && turns == 10 && b.SystemsOf(1).LostTo == SystemId.Engine, $"the engine quits when it runs dry, in the tenth turn ({turns})");
        }

        Console.WriteLine("\nA blast");
        {
            var b = Astern(4000);
            var tgt = b.planes[1];
            b.bullets.Add(Mine.Make(0, 0, tgt.state.pos + tgt.Forward * 300, Vec3d.Zero, b.time - 2));
            var r = b.Resolve(new TurnPlan?[] { Ahead(b, 0), Ahead(b, 1) }, 3);
            var hit = r.hits.FirstOrDefault(h => h.target == 1);
            var s = b.SystemsOf(1);
            double w = Specials.MineDamage / b.combat[1].toughness;
            Check(hit.system == -1 && new[] { s.engine, s.fuel, s.controls, s.wings }.All(x => Math.Abs(x - w) < 1e-9),
                  $"a balloon mine wears every system by {w:F2} ({Sys(s)})");
            Check(!b.down[1] && s.AnyDamaged, "badly damaged but still flying");
        }

        Console.WriteLine("\nBailing out");
        {
            var b = Astern(4000);
            b.planes[1].systems.Wear(SystemId.Engine, 1);
            var r = b.Resolve(new TurnPlan?[] { Ahead(b, 0), Ahead(b, 1) }, 3);
            var s = b.SystemsOf(1);
            Check(b.down[1] && s.bailed && s.bailAt < b.time, "the engine dead, the pilot bails out");
            var f = r.frames[1][r.FrameCount - 1];
            Check(f.bailed && f.bailPos.z > 9000, "the frames carry where he left it, for the parachute");
            var chute = Systems.ChuteAt(s.bailPos, s.bailVel, 60);
            Check(Math.Abs(chute.z - (s.bailPos.z - Systems.ChuteSinkFtps * 60 + s.bailVel.z * Systems.ChuteLagSec)) < 1, $"a minute later he has sunk {s.bailPos.z - chute.z:F0} ft under the canopy");
            for (int t = 0; t < 30 && !b.crashed[1]; t++) b.Resolve(new TurnPlan?[] { Ahead(b, 0), Ahead(b, 1) }, 3);
            Check(b.crashed[1], "and the empty aircraft falls on and crashes");
            var w = Astern(4000);
            w.planes[1].systems.Wear(SystemId.Wings, 1);
            w.Resolve(new TurnPlan?[] { Ahead(w, 0), Ahead(w, 1) }, 3);
            Check(w.down[1] && !w.SystemsOf(1).bailed, "a folded wing brings it down without a bail-out");
        }

        Console.WriteLine("\nG-LOC");
        {
            double most = 0;
            FlightModel Pull(double pitch, double sec, double kias = 400)
            {
                var m = At(AircraftNumbers.P51D(), 0, 0, kias);
                most = 0;
                for (int k = 0; k < sec / 0.02; k++) { m.Step(new FlightControls { pitch = pitch, throttle = 1 }, 0.02); most = Math.Max(most, m.g); }
                return m;
            }
            var one = Pull(1, 3);
            Check(!one.BlackedOut && one.glocs == 0, $"one full 3 s pull from fresh: greying ({one.GreyOut:F2}) but still awake");
            var two = Pull(1, 6);
            Check(two.glocs == 1, $"held for two turns: blacked out ({two.glocs})");
            var gentle = Pull(0.6, 30);
            Check(gentle.glocs == 0, $"30 s of loops at up to {most:F1} G: never");
            var slow = Pull(1, 12, 180);
            Check(slow.glocs == 0, $"slow, the wing can't pull enough G to do it (at most {most:F1} G)");
            var m = Pull(1, 4.5);
            Check(m.BlackedOut, "out cold");
            double g0 = m.g;
            for (int k = 0; k < 75; k++) m.Step(new FlightControls { pitch = 1, roll = 1, throttle = 1 }, 0.02);
            Check(m.g < 1.6 && Math.Abs(m.rollRateDps) < 5, $"the stick held hard back does nothing: it unloads ({g0:F1} G to {m.g:F1})");
            for (int k = 0; k < 250; k++) m.Step(new FlightControls { pitch = 0, throttle = 1 }, 0.02);
            Check(!m.BlackedOut, $"he comes round after {FlightModel.GlocSec:F0} s");
            var c = m.Clone();
            Check(c.gStress == m.gStress && c.glocs == m.glocs, "a clone carries the G stress");
        }

        Console.WriteLine("\nCarried along");
        {
            var b = Astern(700);
            b.Resolve(new TurnPlan?[] { Fire(b, 0, 0, 0.3), Ahead(b, 1) }, 3);
            b.planes[0].systems.Wear(SystemId.Fuel, 0.6);
            var before = b.SystemsOf(1);
            var c = b.Clone();
            c.Resolve(new TurnPlan?[] { Fire(c, 0, 0, 0.3), Ahead(c, 1) }, 3);
            Check(b.SystemsOf(1).controls == before.controls && c.SystemsOf(1).controls > before.controls, "a clone's hits stay in the clone");
            var back = Astern(700);
            Wire.Restore(back, Wire.Encode(b));
            Check(Wire.Hash(back) == Wire.Hash(b) && back.SystemsOf(1).controls == before.controls && back.SystemsOf(0).leakLeftSec == b.SystemsOf(0).leakLeftSec,
                  "a snapshot brings the damage and the leak across exactly");
            back.Resolve(new TurnPlan?[] { Ahead(back, 0), Ahead(back, 1) }, 3);
            b.Resolve(new TurnPlan?[] { Ahead(b, 0), Ahead(b, 1) }, 3);
            Check(Wire.Hash(back) == Wire.Hash(b), "and both fly the next turn the same");
            var sub = b.Subset(new[] { 1 });
            Check(sub.SystemsOf(0).controls == b.SystemsOf(1).controls, "a subset keeps it");
            b.Swap(0, 1);
            Check(b.SystemsOf(0).controls == before.controls && b.SystemsOf(1).Leaking, "a swap moves it with the plane");
        }

        Console.WriteLine(fails == 0 ? "\nAll checks pass." : $"\n{fails} check(s) FAILED.");
        return fails;
    }
}
