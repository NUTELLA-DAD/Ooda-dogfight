// Checks of the arcade specials without Unity (see Turn/Core/Specials.cs): the balloon mine catches a tailer who flies
// on and spares one who turns away; the heat blade cuts an enemy in half and flies on, keeps the guns cold and runs hot;
// stealth hides you from the AI and the flak but not from a heat-seeker, and firing shows you; the AI pilots use all
// three; and everything survives a clone, a subset, a swap and the trip to bytes.
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

    /// <summary>A P-51 (0) ahead and a 109 (1) on its tail, gap ft behind, both heading north.</summary>
    static Battle Tail(double gap, double kias = 300) =>
        new Battle(At(AircraftNumbers.P51D(), 0, gap, kias), CombatNumbers.P51D(), At(AircraftNumbers.Bf109G(), 0, 0, kias), CombatNumbers.Bf109G());

    /// <summary>A P-51 (0) heading north and a 109 (1) heading south straight at it, gap ft apart.</summary>
    static Battle HeadOn(double gap) =>
        new Battle(At(AircraftNumbers.P51D(), 0, 0, 300), CombatNumbers.P51D(), At(AircraftNumbers.Bf109G(), 0, gap, 300, South), CombatNumbers.Bf109G());

    static TurnPlan Ahead(Battle b, int i) => TurnPlan.Straight(b.planes[i]);

    static int Main()
    {
        Console.WriteLine("Balloon mine");
        {
            var b = Tail(1200);
            var drop = Ahead(b, 0); drop.mine = true;
            var r = b.Resolve(new TurnPlan?[] { drop, Ahead(b, 1) }, 3);
            Check(b.balloonMines[0] == CombatNumbers.P51D().balloonMines - 1 && r.mines.Count == 1, $"one goes, {b.balloonMines[0]} left, its drift recorded");
            var mine = b.bullets.OfType<Mine>().FirstOrDefault();
            bool caught = r.hits.Any(h => h.shooter == 0 && h.target == 1 && h.damage == Specials.MineDamage);
            for (int t = 0; t < 3 && !caught; t++)
                caught = b.Resolve(new TurnPlan?[] { Ahead(b, 0), Ahead(b, 1) }, 3).hits.Any(h => h.shooter == 0 && h.target == 1 && h.damage == Specials.MineDamage);
            Check(caught, $"a 109 following 1,200 ft behind flies into it ({b.health[1]:F1} of {b.combat[1].health} left)");
            Check(mine == null || mine.vel.Length < 60, $"it hangs nearly still in the air ({(mine == null ? 0 : mine.vel.Length):F0} ft/s)");

            var c = Tail(1200);
            var c1 = Ahead(c, 1); c1.aim = new Vec3d(0.6, 0.8, 0).Normalized;   // breaks off to the right
            c.Resolve(new TurnPlan?[] { drop, c1 }, 3);
            bool spared = true;
            for (int t = 0; t < 4; t++)
            {
                var hits = c.Resolve(new TurnPlan?[] { Ahead(c, 0), new TurnPlan { aim = c.planes[1].Forward, throttle = 1 } }, 3).hits;
                if (hits.Any(h => h.shooter == 0 && h.damage == Specials.MineDamage)) spared = false;
            }
            Check(spared, "one that turns away is spared");

            var d = Tail(1200);
            d.side[1] = 0;   // a wingman behind, not an enemy
            d.Resolve(new TurnPlan?[] { drop, Ahead(d, 1) }, 3);
            for (int t = 0; t < 3; t++) d.Resolve(new TurnPlan?[] { Ahead(d, 0), Ahead(d, 1) }, 3);
            Check(d.health[1] == d.combat[1].health, "a wingman flies through it unharmed");

            var e = Tail(1200);
            e.balloonMines[0] = 0;
            e.Resolve(new TurnPlan?[] { drop, Ahead(e, 1) }, 3);
            Check(!e.bullets.Any(x => x is Mine), "with none left, nothing goes");
        }

        Console.WriteLine("\nHeat blade");
        {
            var plain = HeadOn(2500);
            plain.Resolve(new TurnPlan?[] { Ahead(plain, 0), Ahead(plain, 1) }, 3);
            Check(plain.down[0] && plain.down[1], "head-on without it: both go down");

            var b = HeadOn(2500);
            var ram = Ahead(b, 0); ram.ram = true; ram.burstStart = 0; ram.burstEnd = 3;
            double ammo = b.ammo[0];
            var r = b.Resolve(new TurnPlan?[] { ram, Ahead(b, 1) }, 3);
            Check(b.down[1] && !b.down[0], "with it lit: the 109 is cut in half and the P-51 flies on");
            Check(r.hits.Any(h => h.shooter == 0 && h.target == 1) && b.health[1] <= 0, "recorded as a kill for the P-51");
            Check(b.ammo[0] == ammo && r.traces.Count == 0, "the guns stay cold while it glows");
            Check(b.heatBlades[0] == CombatNumbers.P51D().heatBlades - 1 && r.frames[0].Skip(1).All(f => f.ram), "it uses a charge and glows all turn");
            Check(!b.ramOn[0], "and cools when the turn ends");

            var miss = HeadOn(2500);
            miss.planes[1] = At(AircraftNumbers.Bf109G(), 400, 2500, 300, South);   // passes 400 ft to the side
            miss.Resolve(new TurnPlan?[] { ram, Ahead(miss, 1) }, 3);
            Check(!miss.down[1] && !miss.down[0], "a pass 400 ft wide cuts nothing");

            var both = HeadOn(2500);
            var ram1 = Ahead(both, 1); ram1.ram = true;
            both.Resolve(new TurnPlan?[] { ram, ram1 }, 3);
            Check(both.down[0] && both.down[1], "two blades meeting: both go down");

            // red hot: a seeker locks it from much farther
            var hot = Tail(12000);
            hot.Swap(0, 1);   // the 109 (now 0) behind, the P-51 (now 1) ahead
            int coldLock = hot.LockTarget(0);
            hot.ramOn[1] = true;
            int hotLock = hot.LockTarget(0);
            Check(coldLock == -1 && hotLock == 1, $"a seeker 12,000 ft behind locks it only while it glows (heat {hot.HeatOf(1, hot.planes[0].state.pos):F1})");
        }

        Console.WriteLine("\nStealth");
        {
            var b = Tail(1500);   // the 109 behind the P-51
            var ai = new EnemyAi { me = 1, sloppiness = 0 };
            Check(ai.Target(b) == 0, "the 109's pilot goes for the P-51");
            var cloak = Ahead(b, 0); cloak.cloak = true;
            var r = b.Resolve(new TurnPlan?[] { cloak, Ahead(b, 1) }, 3);
            Check(b.Cloaked(0) && r.frames[0].Skip(1).All(f => f.cloaked), "the P-51 goes stealthy");
            ai.Forget();
            Check(ai.Target(b) == -1, "the 109's pilot can't see it any more");
            var p = ai.Plan(b, 3);
            Check(p.burstEnd <= p.burstStart, "and plans no burst at it");
            Check(b.LockTarget(1) == 0, "but a heat-seeker still locks it");

            var flak = new FlakSite { pos = b.planes[0].state.pos - new Vec3d(0, 0, 3000), side = 1, rangeFt = 8000 };
            b.flak = new[] { flak };
            var r2 = b.Resolve(new TurnPlan?[] { Ahead(b, 0), new TurnPlan { aim = b.planes[1].Forward, throttle = 1 } }, 3);
            Check(!r2.traces.Any(t => t.owner < 0), "the flak doesn't fire at it");
            Check(!b.Cloaked(0), "two turns on, it shows again");
            b.flak = new FlakSite[0];

            var c = Tail(1500);
            c.Resolve(new TurnPlan?[] { cloak, Ahead(c, 1) }, 3);
            var fire = Ahead(c, 0); fire.burstStart = 1; fire.burstEnd = 1.5;
            c.Resolve(new TurnPlan?[] { fire, Ahead(c, 1) }, 3);
            Check(!c.Cloaked(0) && c.stealths[0] == CombatNumbers.P51D().stealths - 1, "firing the guns shows you at once");
        }

        Console.WriteLine("\nThe AI pilots use them");
        {
            // the P-51's pilot, with a 109 on its tail and guns on it, lets a mine go
            var b = Tail(1000);
            var ai = new EnemyAi { me = 0, sloppiness = 0 };
            var p = ai.Plan(b, 3);
            Check(p.mine, "with a 109 on its six, it lets a mine go");
            b.balloonMines[0] = 0; b.health[0] = 5; ai.Forget();
            p = ai.Plan(b, 3);
            Check(p.cloak && !p.mine, "no mines left and hurt: it goes stealthy");

            // head-on and close with a blade: it lights it and goes through
            var h = HeadOn(2600);
            var ai2 = new EnemyAi { me = 0, sloppiness = 0 };
            var hp = ai2.Plan(h, 3);
            Check(hp.ram, "head-on and close, it lights the blade");
            h.Resolve(new TurnPlan?[] { hp, Ahead(h, 1) }, 3);
            Check(h.down[1] && !h.down[0], "and cuts the 109 in half when it flies on");
        }

        Console.WriteLine("\nIn clones, subsets, swaps and bytes");
        {
            var b = Tail(1500);
            var plan = Ahead(b, 0); plan.mine = true; plan.cloak = true;
            b.Resolve(new TurnPlan?[] { plan, Ahead(b, 1) }, 1.5);
            var c = b.Clone();
            Check(c.balloonMines[0] == b.balloonMines[0] && c.Cloaked(0) && !ReferenceEquals(c.balloonMines, b.balloonMines) && c.bullets.Any(x => x is Mine),
                  "a clone keeps the counts, the stealth and the mine, in its own arrays");
            var sub = b.Subset(new[] { 1 });
            Check(sub.bullets.Any(x => x is Mine), "a subset of the 109 alone still has the P-51's mine hanging there");
            var back = Tail(1500);
            Wire.Restore(back, Wire.Encode(b));
            Check(Wire.Hash(back) == Wire.Hash(b) && back.Cloaked(0) && back.bullets.Any(x => x is Mine), "a snapshot brings them across exactly");
            b.Resolve(new TurnPlan?[] { Ahead(b, 0), Ahead(b, 1) }, 3);
            back.Resolve(new TurnPlan?[] { Ahead(back, 0), Ahead(back, 1) }, 3);
            Check(Wire.Hash(back) == Wire.Hash(b), "and both fly the next turn the same");
            int m0 = b.balloonMines[0], s1 = b.stealths[1];
            b.Swap(0, 1);
            Check(b.balloonMines[1] == m0 && b.stealths[0] == s1, "a swap moves them with the plane");
            var pl = new TurnPlan { mine = true, ram = true, cloak = true };
            var pd = Wire.Decode<TurnPlan>(Wire.Encode(pl));
            Check(pd.mine && pd.ram && pd.cloak, "a plan with all three survives the trip to bytes");
        }

        Console.WriteLine(fails == 0 ? "\nAll checks pass." : $"\n{fails} check(s) FAILED.");
        return fails;
    }
}
