// Checks of the arcade specials without Unity (see Turn/Core/Specials.cs): the balloon mine catches a tailer who flies
// on and spares one who turns away, goes at the moment planned and drifts slowly; the heat blade cuts an enemy in half and flies on, keeps the guns cold and runs hot;
// stealth hides you from the AI and the flak but not from a heat-seeker, and firing shows you; the AI pilots use all
// three; the gravity well bends a hard turn tighter, makes a tailer's rounds miss and pulls a heat-seeker off; magnet rounds pull a near miss in, and only that; and
// the time slow bomb bursts on the nearest threat and slows the other side's planes, rounds and missiles in it; and
// the blink hops ahead along the path at the moment planned, dodges a tailer's rounds and a heat-seeker, stops short of
// the ground, and the AI uses it; and everything survives a clone, a subset, a swap and the trip to bytes.
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
        new Battle(At(AircraftNumbers.P51D(), 0, gap, kias), Specials.Arm(CombatNumbers.P51D()), At(AircraftNumbers.Bf109G(), 0, 0, kias), Specials.Arm(CombatNumbers.Bf109G()));

    /// <summary>A P-51 (0) heading north and a 109 (1) heading south straight at it, gap ft apart.</summary>
    static Battle HeadOn(double gap) =>
        new Battle(At(AircraftNumbers.P51D(), 0, 0, 300), Specials.Arm(CombatNumbers.P51D()), At(AircraftNumbers.Bf109G(), 0, gap, 300, South), Specials.Arm(CombatNumbers.Bf109G()));

    static TurnPlan Ahead(Battle b, int i) => TurnPlan.Straight(b.planes[i]);

    static int Main()
    {
        Console.WriteLine("Balloon mine");
        {
            var b = Tail(1200);
            var drop = Ahead(b, 0); drop.mine = true;
            var r = b.Resolve(new TurnPlan?[] { drop, Ahead(b, 1) }, 3);
            Check(b.balloonMines[0] == Specials.Mines - 1 && r.mines.Count == 1, $"one goes, {b.balloonMines[0]} left, its drift recorded");
            var mine = b.bullets.OfType<Mine>().FirstOrDefault();
            bool caught = r.hits.Any(h => h.shooter == 0 && h.target == 1 && h.damage == Specials.MineDamage);
            for (int t = 0; t < 3 && !caught; t++)
                caught = b.Resolve(new TurnPlan?[] { Ahead(b, 0), Ahead(b, 1) }, 3).hits.Any(h => h.shooter == 0 && h.target == 1 && h.damage == Specials.MineDamage);
            Check(caught, $"a 109 following 1,200 ft behind flies into it (its worst system {b.SystemsOf(1).Worst:F2} worn)");
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
            Check(!d.SystemsOf(1).Hurt, "a wingman flies through it unharmed");

            var e = Tail(1200);
            e.balloonMines[0] = 0;
            e.Resolve(new TurnPlan?[] { drop, Ahead(e, 1) }, 3);
            Check(!e.bullets.Any(x => x is Mine), "with none left, nothing goes");

            // let go late in the turn: it leaves where the plane is then, far up the path from where it starts
            var f = Tail(1200);
            var late = Ahead(f, 0); late.mine = true; late.mineAt = 2.2;
            var start = f.planes[0].state.pos;
            var rf = f.Resolve(new TurnPlan?[] { late, Ahead(f, 1) }, 3);
            var lm = f.bullets.OfType<Mine>().FirstOrDefault();
            double secs = rf.mines.Count == 1 ? rf.mines[0].startFrame * rf.dt : -1;
            double along = lm == null ? 0 : lm.pos.y - start.y;
            Check(lm != null && Math.Abs(secs - 2.2) <= rf.dt + 1e-9, $"planned for 2.2 s, it goes {secs:F2} s into the turn");
            Check(along > 900, $"from where the plane was then, {along:F0} ft up the path (a mine at the start would be about -50)");
            var g = Tail(1200);
            var past = Ahead(g, 0); past.mine = true; past.mineAt = 9;
            var rg = g.Resolve(new TurnPlan?[] { past, Ahead(g, 1) }, 3);
            Check(g.bullets.OfType<Mine>().Count() == 1, $"one planned past the turn's end goes at its last moment (frame {(rg.mines.Count > 0 ? rg.mines[0].startFrame : -1)})");

            // once it hangs it drifts slowly on, the way the plane was going, and never falls
            var h = Tail(1200);
            h.Resolve(new TurnPlan?[] { drop, new TurnPlan { aim = new Vec3d(0.6, 0.8, 0).Normalized, throttle = 1 } }, 3);
            var hm = h.bullets.OfType<Mine>().First();
            var p0 = hm.pos;
            double minSpeed = 1e9, maxSpeed = 0;
            for (int t = 0; t < 4; t++)
            {
                h.Resolve(new TurnPlan?[] { Ahead(h, 0), new TurnPlan { aim = h.planes[1].Forward, throttle = 1 } }, 3);
                if (!hm.alive) break;
                minSpeed = Math.Min(minSpeed, hm.vel.Length); maxSpeed = Math.Max(maxSpeed, hm.vel.Length);
            }
            var moved = hm.pos - p0;
            Check(hm.alive && moved.y > 60 && moved.Length < 400 && moved.z > -1, $"over 12 s it drifts {moved.Length:F0} ft, {moved.y:F0} north (the way it was let go), {moved.z:F0} up");
            Check(maxSpeed < 25 && minSpeed > 3, $"slowly: {minSpeed:F0} to {maxSpeed:F0} ft/s");
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
            Check(r.hits.Any(h => h.shooter == 0 && h.target == 1) && b.SystemsOf(1).Lost, "recorded as a kill for the P-51");
            Check(b.ammo[0] == ammo && r.traces.Count == 0, "the guns stay cold while it glows");
            Check(b.heatBlades[0] == Specials.Blades - 1 && r.frames[0].Skip(1).All(f => f.ram), "it uses a charge and glows all turn");
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

        Console.WriteLine("\nSonic boom");
        {
            // flown straight with the blade lit: it builds, pushes on and breaks the barrier
            var b = Tail(900);
            var cold = Tail(900);
            var ram = Ahead(b, 0); ram.ram = true;
            var r = b.Resolve(new TurnPlan?[] { ram, Ahead(b, 1) }, 3);
            var rc = cold.Resolve(new TurnPlan?[] { Ahead(cold, 0), Ahead(cold, 1) }, 3);
            var boom = r.booms.FirstOrDefault(e => e.owner == 0);
            double boomSec = boom == null ? -1 : boom.frame * r.dt;
            Check(boom != null && Math.Abs(boomSec - Specials.BoomChargeSec) < 0.1, $"flown straight lit, it booms after {boomSec:F2} s");
            var f0 = r.frames[0];
            bool builds = true;
            for (int k = 2; k < f0.Length; k++) if (f0[k].ramHeat + 1e-9 < f0[k - 1].ramHeat) builds = false;
            Check(builds && f0[f0.Length / 4].ramHeat > 0.1 && f0[f0.Length / 4].ramHeat < 0.9 && f0[f0.Length - 1].ramHeat >= 1, "the glow builds red to white hot over it");
            Check(!f0[boom == null ? 1 : boom.frame - 1].sonic && f0[f0.Length - 1].sonic, "and it is past the barrier from the boom on");
            double kt = b.planes[0].Kias - cold.planes[0].Kias;
            Check(kt > 10, $"building it pushes the P-51 on ({kt:F0} kt faster than without)");
            var sh = r.shoves.FirstOrDefault(e => e.owner == 0 && e.target == 1);
            double off = (b.planes[1].state.pos - cold.planes[1].state.pos).Length;
            Check(sh != null && sh.dv.Length > 0.5 * Specials.BoomShoveFps * (1 - 900.0 / Specials.BoomReachFt) && off > 100,
                $"the 109 900 ft behind is shoved off course ({(sh == null ? 0 : sh.dv.Length):F0} ft/s, {off:F0} ft off its line by the turn's end)");
            Check(r.shoves.Count(e => e.target == 1) == 1, "once a turn");
            Check(rc.booms.Count == 0 && rc.shoves.Count == 0, "without the blade: no boom");

            var far = Tail(2600);
            var rf = far.Resolve(new TurnPlan?[] { ram, Ahead(far, 1) }, 3);
            Check(rf.booms.Count == 1 && rf.shoves.Count == 0, "one 2,600 ft behind is out of its reach");

            var turn = Tail(900);
            var hard = new TurnPlan { aim = new Vec3d(1, 0.2, 0).Normalized, throttle = 1, ram = true };
            var rt = turn.Resolve(new TurnPlan?[] { hard, Ahead(turn, 1) }, 3);
            Check(rt.booms.Count == 0 && rt.frames[0].Last().ramHeat < 1, $"a hard turn never builds it (heat {rt.frames[0].Last().ramHeat:F2})");

            var friend = Tail(900);
            friend.side[1] = 0;
            var rw = friend.Resolve(new TurnPlan?[] { ram, Ahead(friend, 1) }, 3);
            Check(rw.booms.Count == 1 && rw.shoves.Count == 0, "your own side is spared");

            // ahead of the nose: the boom never pushes your cut away
            var h = HeadOn(2900);
            var rh = h.Resolve(new TurnPlan?[] { ram, Ahead(h, 1) }, 3);
            Check(rh.booms.Count == 1 && rh.booms[0].frame * rh.dt < 2 && h.down[1] && !h.down[0] && rh.shoves.Count == 0,
                "head-on, it booms first and still cuts the 109 dead ahead in half, unshoved");

            // white hot: past the barrier it cuts from farther
            double wide = Specials.BladeReachFt + (Specials.BoomBladeReachFt - Specials.BladeReachFt) / 2 + 9;
            var w = HeadOn(2900); w.planes[1] = At(AircraftNumbers.Bf109G(), wide, 2900, 300, South);
            w.Resolve(new TurnPlan?[] { ram, Ahead(w, 1) }, 3);
            var wc = HeadOn(2500); wc.planes[1] = At(AircraftNumbers.Bf109G(), wide, 2500, 300, South);
            var early = Ahead(wc, 0); early.ram = true;
            var rwc = wc.Resolve(new TurnPlan?[] { early, Ahead(wc, 1) }, 1.2);   // passing before the boom
            Check(w.down[1] && !w.down[0], $"white hot, a pass {wide:F0} ft wide cuts the 109");
            Check(rwc.booms.Count == 0, "and before the barrier the pass is still to come");

            // a jet already near Mach 1 booms at once
            var jet = new Battle(new FlightModel(AircraftNumbers.F15C(), PointMass.Make(new Vec3d(0, 900, 10000), Atmosphere.SoundKt(10000), North)) { throttle = 1 },
                Specials.Arm(CombatNumbers.F15C()), At(AircraftNumbers.Bf109G(), 0, 0, 300), Specials.Arm(CombatNumbers.Bf109G()));
            var jr = Ahead(jet, 0); jr.ram = true;
            var rj = jet.Resolve(new TurnPlan?[] { jr, Ahead(jet, 1) }, 3);
            Check(rj.booms.Count == 1 && rj.booms[0].frame <= 1, $"an F-15 at Mach 1 booms the moment it lights (frame {(rj.booms.Count > 0 ? rj.booms[0].frame : -1)})");

            // the same on every machine: a clone flies it identically
            var m1 = Tail(900); var m2 = m1.Clone();
            m1.Resolve(new TurnPlan?[] { ram, Ahead(m1, 1) }, 3, false);
            m2.Resolve(new TurnPlan?[] { ram, Ahead(m2, 1) }, 3, true);
            Check((m1.planes[1].state.pos - m2.planes[1].state.pos).Length < 1e-9 && (m1.planes[0].state.vel - m2.planes[0].state.vel).Length < 1e-9,
                "recorded or not, two copies fly it exactly the same");
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
            Check(!c.Cloaked(0) && c.stealths[0] == Specials.Stealths - 1, "firing the guns shows you at once");
        }

        Console.WriteLine("\nJammer");
        {
            var b = Tail(1500);
            b.Add(At(AircraftNumbers.Bf109G(), 0, -15000, 300), Specials.Arm(CombatNumbers.Bf109G()), 1);   // a second 109, far behind
            var jam = Ahead(b, 0); jam.jam = true;
            b.Resolve(new TurnPlan?[] { jam, Ahead(b, 1), Ahead(b, 2) }, 3);
            Check(b.jammers[0] == Specials.Jammers - 1 && b.Jammed(1) && !b.Jammed(2) && !b.Jammed(0), "the 109 close by is jammed, the one 15,000 ft back isn't, nor the jammer");
            b.Resolve(new TurnPlan?[] { Ahead(b, 0), Ahead(b, 1), Ahead(b, 2) }, 3);
            Check(b.Jammed(1), "still jammed when it plans the next turn");
            b.Resolve(new TurnPlan?[] { Ahead(b, 0), Ahead(b, 1), Ahead(b, 2) }, 3);
            Check(!b.Jammed(1), "clear again the turn after");

            // a jammed pilot plans against where its target isn't
            var c = Tail(1200); var d = Tail(1200);
            var jc = Ahead(c, 0); jc.jam = true;
            c.Resolve(new TurnPlan?[] { jc, Ahead(c, 1) }, 3);
            d.Resolve(new TurnPlan?[] { Ahead(d, 0), Ahead(d, 1) }, 3);
            var pc = new EnemyAi { me = 1, sloppiness = 0 }.Plan(c, 3);
            var pd = new EnemyAi { me = 1, sloppiness = 0 }.Plan(d, 3);
            Check(Vec3d.Dot(pc.aim, pd.aim) < 0.9999 || pc.burstStart != pd.burstStart || pc.burstEnd != pd.burstEnd, "a jammed 109 plans differently from a clear one in the same spot");
            Jam.Offset(3, 1, 0, out var s1, out double r1); Jam.Offset(3, 1, 0, out var s2, out double r2);
            Check((s1 - s2).Length == 0 && r1 == r2 && s1.Length > 0, $"the scramble is the same on every machine ({s1.Length:F0} ft off, {r1 * 180 / Math.PI:F0} deg turned)");
        }

        Console.WriteLine("\nThe AI pilots use them");
        {
            // the P-51's pilot, with a 109 on its tail and guns on it, lets a mine go
            var b = Tail(1000);
            b.blinks[0] = 0;   // the blink has its own checks (BlinkHop)
            var ai = new EnemyAi { me = 0, sloppiness = 0 };
            var p = ai.Plan(b, 3);
            Check(p.mine, "with a 109 on its six, it lets a mine go");
            b.balloonMines[0] = 0; ai.Forget();
            p = ai.Plan(b, 3);
            Check(p.ram && !p.mine && p.burstEnd <= p.burstStart, "no mines left: it lights the heat ram to boom the tailer off its shot");
            b.heatBlades[0] = 0; ai.Forget();
            p = ai.Plan(b, 3);
            Check(p.slow && !p.mine && !p.ram, "no mines or blade left: it throws a time slow bomb at the tailer");
            b.slowBombs[0] = 0; ai.Forget();
            p = ai.Plan(b, 3);
            Check(p.well && !p.mine && !p.ram && !p.slow, "no mines, blade or slow bombs left: it throws a gravity well");
            b.wells[0] = 0; ai.Forget();
            p = ai.Plan(b, 3);
            Check(p.jam && !p.mine && !p.well, "no mines or wells left: it sets off the jammer");
            b.jammers[0] = 0; b.planes[0].systems.Wear(SystemId.Controls, 0.6); ai.Forget();
            p = ai.Plan(b, 3);
            Check(p.cloak && !p.mine && !p.jam, "no mines or jammer left, and hurt: it goes stealthy");

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
            var pl = new TurnPlan { mine = true, mineAt = 1.35, ram = true, cloak = true };
            var pd = Wire.Decode<TurnPlan>(Wire.Encode(pl));
            Check(pd.mine && pd.mineAt == 1.35 && pd.ram && pd.cloak, "a plan with all three (and the mine's moment) survives the trip to bytes");
        }

        Console.WriteLine("\nThe Arcade switch");
        {
            var on = new DogfightSetup { randomStart = false, ours = new[] { Craft.Mustang, Craft.Phantom }, theirs = new[] { Craft.Bf109 } };
            var b = Scenarios.Dogfight(on, Vec3d.Zero);
            Check(Enumerable.Range(0, b.Count).All(i => b.balloonMines[i] == Specials.Mines && b.heatBlades[i] == Specials.Blades && b.stealths[i] == Specials.Stealths && b.jammers[i] == Specials.Jammers),
                  "a dogfight with Arcade on: every fighter carries the specials");
            var off = on.Copy(); off.arcade = false;
            var c = Scenarios.Dogfight(off, Vec3d.Zero);
            Check(Enumerable.Range(0, c.Count).All(i => c.balloonMines[i] == 0 && c.heatBlades[i] == 0 && c.stealths[i] == 0), "with Arcade off: none");
            var drop = TurnPlan.Straight(c.planes[0]); drop.mine = drop.ram = drop.cloak = true;
            c.Resolve(new TurnPlan?[] { drop, TurnPlan.Straight(c.planes[1]) }, 3);
            Check(!c.bullets.Any(x => x is Mine) && !c.Cloaked(0), "and planning one does nothing");
            Check(!DogfightSetup.FromBytes(off.ToBytes()).arcade && DogfightSetup.FromBytes(on.ToBytes()).arcade, "the switch survives the trip to bytes (for the friend over the network)");
            Check(new CombatNumbers().balloonMines == 0 && CombatNumbers.P51D().stealths == 0, "the other missions carry none");
        }

        Console.WriteLine("\nThe loadout: two picks each");
        {
            var setup = new DogfightSetup
            {
                ours = new[] { Craft.Mustang, Craft.F15 }, theirs = new[] { Craft.Bf109, Craft.Zero },
                randomStart = false, start = Scenarios.Start.HeadOn, randomArena = false,
                oursLoadouts = new[] { new Loadout(Kit.Rocket, Kit.Mines), new Loadout(Kit.Stealth, Kit.Rocket) },
                theirsLoadouts = new[] { new Loadout(Kit.Missiles, Kit.Blade), new Loadout(Kit.Jammer, Kit.Jammer) },
            };
            var b = Scenarios.Dogfight(setup, Vec3d.Zero);
            // order: 0 our lead, 1 their lead, 2 our wingman, 3 their wingman
            Check(b.planes[0].RocketReady && b.missiles[0] == 0 && b.balloonMines[0] == Specials.Mines && b.heatBlades[0] == 0 && b.stealths[0] == 0 && b.jammers[0] == 0,
                  "rocket + mines: the rocket and two mines, no heat-seekers, nothing else");
            Check(b.missiles[1] == CombatNumbers.Bf109G().missiles && b.heatBlades[1] == 1 && !b.planes[1].RocketReady && b.balloonMines[1] == 0,
                  "heat-seekers + blade: its missiles and a blade, no rocket");
            Check(b.stealths[2] == 1 && !b.planes[2].RocketReady && b.missiles[2] == CombatNumbers.F15C().missiles, "an F-15 that picked stealth + rocket gets stealth + heat-seekers instead");
            var jet = new Loadout(Kit.Stealth, Kit.Rocket).FitTo(Craft.F15);
            Check(jet.Has(Kit.Stealth) && !jet.Has(Kit.Rocket) && jet.a != jet.b, $"the F-15's stealth + rocket becomes {jet}");
            var twice = new Loadout(Kit.Jammer, Kit.Jammer).FitTo(Craft.Zero);
            Check(twice.Has(Kit.Jammer) && twice.a != twice.b && b.jammers[3] == 1, $"the same pick twice becomes two different ones ({twice})");
            Check(DogfightSetup.Numbers(Craft.Mustang).rocketG > 0, "taking the rocket off one plane leaves the type's numbers alone");
            var p = new Loadout(Kit.Missiles, Kit.Mines).Pick(Kit.Stealth, Craft.Mustang);
            Check(p.Has(Kit.Mines) && p.Has(Kit.Stealth) && !p.Has(Kit.Missiles), $"a third pick replaces the older one ({p})");
            Check(new Loadout(Kit.Missiles, Kit.Mines).Pick(Kit.Rocket, Craft.Phantom).Equals(new Loadout(Kit.Missiles, Kit.Mines)), "an F-4 can't pick the rocket");

            var back = DogfightSetup.FromBytes(setup.ToBytes());
            Check(back.oursLoadouts.SequenceEqual(setup.oursLoadouts) && back.theirsLoadouts.SequenceEqual(setup.theirsLoadouts), "the loadouts survive the trip to bytes");
            var bb = Scenarios.Dogfight(back, Vec3d.Zero);
            Check(Enumerable.Range(0, b.Count).All(i => bb.missiles[i] == b.missiles[i] && bb.balloonMines[i] == b.balloonMines[i] && bb.stealths[i] == b.stealths[i]
                                                        && bb.planes[i].numbers.rocketG == b.planes[i].numbers.rocketG), "and build the same battle on the friend's machine");
            var wider = setup.Copy(); wider.ours = DogfightSetup.Resize(wider.ours, 4); wider.FitLoadouts();
            Check(wider.oursLoadouts.Length == 4 && wider.oursLoadouts[3].Equals(wider.oursLoadouts[1]), "a plane added brings what the one before it brings");
            var none = setup.Copy(); none.oursLoadouts = none.theirsLoadouts = null;
            var nb = Scenarios.Dogfight(DogfightSetup.FromBytes(none.ToBytes()), Vec3d.Zero);
            Check(nb.balloonMines[0] == Specials.Mines && nb.stealths[0] == 1 && nb.missiles[0] > 0 && nb.planes[0].RocketReady, "no loadouts picked (the tutorial): everything, as before");
            var straight = setup.Copy(); straight.arcade = false;
            var sb = Scenarios.Dogfight(straight, Vec3d.Zero);
            Check(sb.missiles[0] > 0 && sb.planes[0].RocketReady && sb.balloonMines[0] == 0 && sb.stealths[2] == 0, "Arcade off: the loadouts don't count (heat-seekers and the rocket, no specials)");
            var old = setup.ToBytes(); old[0] = 4; Array.Resize(ref old, old.Length - 1 - 2 * 4);
            var ob = DogfightSetup.FromBytes(old);
            Check(ob.oursLoadouts == null && ob.arcade, "a setup from before the loadouts still reads");

            // the AI with no specials picked plans none
            var bare = new DogfightSetup { ours = new[] { Craft.Mustang }, theirs = new[] { Craft.Bf109 }, randomStart = false, start = Scenarios.Start.Defensive, range = DogfightSetup.Range.Close,
                                           oursLoadouts = new[] { new Loadout(Kit.Missiles, Kit.Rocket) }, theirsLoadouts = new[] { new Loadout(Kit.Missiles, Kit.Rocket) } };
            var ab = Scenarios.Dogfight(bare, Vec3d.Zero);
            bool special = false;
            for (int t = 0; t < 6 && !ab.down[0] && !ab.down[1]; t++)
            {
                var p0 = new EnemyAi { me = 0, sloppiness = 0 }.Plan(ab, 3); var p1 = new EnemyAi { me = 1, sloppiness = 0 }.Plan(ab, 3);
                if (p0.mine || p0.ram || p0.cloak || p0.jam || p1.mine || p1.ram || p1.cloak || p1.jam) special = true;
                ab.Resolve(new TurnPlan?[] { p0, p1 }, 3);
            }
            Check(!special && !ab.bullets.Any(x => x is Mine), "AI pilots with missiles + rocket never plan a special");

            // radar missiles: a jet's pick
            Check(!Loadout.Allowed(Kit.Radar, Craft.Mustang) && Loadout.Allowed(Kit.Radar, Craft.F15) && Loadout.Allowed(Kit.Radar, Craft.Phantom), "radar missiles: jets only");
            Check(Loadout.DefaultFor(Craft.F15).Has(Kit.Radar) && Loadout.DefaultFor(Craft.F15).Has(Kit.Missiles) && !Loadout.DefaultFor(Craft.Spitfire).Has(Kit.Radar),
                  "a jet brings both its missiles unless picked otherwise");
            var jets = new DogfightSetup { ours = new[] { Craft.F15, Craft.Phantom }, theirs = new[] { Craft.Mustang }, randomStart = false,
                                           oursLoadouts = new[] { new Loadout(Kit.Radar, Kit.Ice), new Loadout(Kit.Missiles, Kit.Stealth) }, theirsLoadouts = new[] { new Loadout(Kit.Radar, Kit.Missiles) } };
            jets.FitLoadouts();
            var jb = Scenarios.Dogfight(jets, Vec3d.Zero);
            Check(jb.radarMissiles[0] == CombatNumbers.F15C().radarMissiles && jb.missiles[0] == 0 && jb.combat[0].radarRangeFt > 0, "an F-15 with radar + ice: its Sparrows, no heat-seekers");
            Check(jb.radarMissiles[2] == 0 && jb.combat[2].radarRangeFt > 0 && jb.chaff[2] > 0, "an F-4 without radar missiles keeps its radar and chaff");
            Check(!jets.theirsLoadouts[0].Has(Kit.Radar) && jb.missiles[1] > 0, $"a P-51 that picked radar missiles gets something it can carry ({jets.theirsLoadouts[0]})");

            // ice: no heat-seeker locks an iced plane, and one that hits it does nothing
            Check(jb.combat[0].iced && !jb.combat[2].iced && jb.HeatOf(0, jb.planes[1].state.pos) == 0, "ice: cold as the sky to a seeker");
            var tail = Tail(4000);
            tail.combat[0] = tail.combat[0].Copy(); tail.combat[0].iced = true;
            tail.missiles[1] = 4;
            Check(tail.LockTarget(1) < 0, "a 109 4,000 ft behind an iced P-51 gets no lock");
            var shoot = Ahead(tail, 1); shoot.missile = true; shoot.missileTarget = 0;
            tail.Resolve(new TurnPlan?[] { Ahead(tail, 0), shoot }, 3);
            Check(tail.missiles[1] == 4 && !tail.bullets.Any(x => x is Missile), "so its heat-seeker never goes");
            var warm = Tail(4000);
            Check(warm.LockTarget(1) == 0, "the same P-51 without ice is locked");
            var hit = Tail(4000);
            hit.combat[0] = hit.combat[0].Copy(); hit.combat[0].iced = true;
            hit.bullets.Add(Missile.Make(1, 1, 0, hit.planes[0].state.pos - hit.planes[0].Forward * 300, hit.planes[0].state.vel + hit.planes[0].Forward * 400, hit.time));
            for (int t = 0; t < 2; t++) hit.Resolve(new TurnPlan?[] { Ahead(hit, 0), Ahead(hit, 1) }, 3);
            var bare2 = Tail(4000);
            bare2.bullets.Add(Missile.Make(1, 1, 0, bare2.planes[0].state.pos - bare2.planes[0].Forward * 300, bare2.planes[0].state.vel + bare2.planes[0].Forward * 400, bare2.time));
            for (int t = 0; t < 2; t++) bare2.Resolve(new TurnPlan?[] { Ahead(bare2, 0), Ahead(bare2, 1) }, 3);
            Check(!hit.SystemsOf(0).Hurt && !hit.down[0] && (bare2.SystemsOf(0).Hurt || bare2.down[0]), "a heat-seeker already on its way shatters on the ice (it hits one without)");
            var ai2 = new EnemyAi { me = 1, sloppiness = 0 }.Plan(tail, 3);
            Check(!ai2.missile, "the AI doesn't try a heat-seeker on an iced plane");
            var icy = new DogfightSetup { oursLoadouts = new[] { new Loadout(Kit.Ice, Kit.Mines) }, theirsLoadouts = new[] { new Loadout(Kit.Missiles, Kit.Mines) }, randomStart = false };
            Check(DogfightSetup.FromBytes(icy.ToBytes()).oursLoadouts[0].Has(Kit.Ice) && Scenarios.Dogfight(DogfightSetup.FromBytes(icy.ToBytes()), Vec3d.Zero).combat[0].iced,
                  "ice survives the trip to bytes");
        }

        GravityWell();
        MagnetRounds();
        SlowBomb();
        BlinkHop();

        Console.WriteLine(fails == 0 ? "\nAll checks pass." : $"\n{fails} check(s) FAILED.");
        return fails;
    }

    static double Heading(Vec3d v) => Math.Atan2(v.y, v.x) * 180 / Math.PI;

    /// <summary>How far (degrees) a P-51 at kias turns in two hard turns to its left, throwing a gravity well at the start or not.</summary>
    static double TurnWith(double kias, bool well)
    {
        var b = new Battle(At(AircraftNumbers.P51D(), 0, 0, kias), Specials.Arm(CombatNumbers.P51D()), At(AircraftNumbers.Bf109G(), 50000, 0, 300), Specials.Arm(CombatNumbers.Bf109G()));
        double total = 0;
        for (int t = 0; t < 2; t++)
        {
            var f = b.planes[0].Forward;
            var p = new TurnPlan { aim = new Vec3d(-f.y, f.x, 0).Normalized, throttle = 1 };   // 90 degrees left: past the ring, a hard turn
            if (well && t == 0) p.well = true;
            double h0 = Heading(b.planes[0].Forward);
            b.Resolve(new TurnPlan?[] { p, Ahead(b, 1) }, 3, false);
            double d = Heading(b.planes[0].Forward) - h0;
            total += d < -180 ? d + 360 : d > 180 ? d - 360 : d;
        }
        return total;
    }

    static void GravityWell()
    {
        Console.WriteLine("\nGravity well");
        {
            var b = Tail(30000);
            var throwIt = Ahead(b, 0); throwIt.well = true; throwIt.wellAt = 1;
            var r = b.Resolve(new TurnPlan?[] { throwIt, Ahead(b, 1) }, 3);
            var w = b.bullets.OfType<Well>().FirstOrDefault();
            Check(b.wells[0] == Specials.Wells - 1 && r.wells.Count == 1 && w != null, $"one is thrown, {b.wells[0]} left, its drift recorded");
            Check(r.wells.Count == 1 && Math.Abs(r.wells[0].startFrame - 1 / Battle.StepSec) <= 1, $"it goes at the moment planned (frame {(r.wells.Count > 0 ? r.wells[0].startFrame : -1)})");
            Check(w != null && w.pos.y > b.planes[0].state.pos.y - 3000 && w.pos.z > 10000 + 400 && w.vel.Length < 80,
                  $"it settles ahead and (flying straight) above, to a hang ({(w == null ? 0 : w.vel.Length):F0} ft/s)");
            Check(!r.hits.Any(), "it hurts no one");
            for (int t = 0; t < 2; t++) b.Resolve(new TurnPlan?[] { Ahead(b, 0), Ahead(b, 1) }, 3);
            Check(!b.bullets.OfType<Well>().Any(), $"it is gone after {Specials.WellLifeSec:F0} s");
        }
        {
            double[] kts = { 220, 300, 380 };
            var gains = kts.Select(k => TurnWith(k, true) / TurnWith(k, false) - 1).ToArray();
            Check(gains.All(g => g > 0.1), $"thrown into a hard turn, the P-51 turns tighter: {string.Join(", ", kts.Zip(gains, (k, g) => $"{k:F0} kt +{g * 100:F0}%"))} over two turns");
        }
        {
            // a 109 900 ft behind fires all of two turns; the P-51 flies straight, with a well or without
            int Hits(bool well)
            {
                int hits = 0;
                foreach (double gap in new[] { 600.0, 900, 1200 })
                {
                    var b = Tail(gap); b.dispersion = false;
                    for (int t = 0; t < 2; t++)
                    {
                        var p0 = Ahead(b, 0); p0.well = well && t == 0;
                        var p1 = Ahead(b, 1); p1.burstStart = 0; p1.burstEnd = 3;
                        hits += b.Resolve(new TurnPlan?[] { p0, p1 }, 3, false).hits.Count(h => h.shooter == 1);
                    }
                }
                return hits;
            }
            int without = Hits(false), with = Hits(true);
            Check(with * 2 < without, $"a 109 shooting from behind lands {with} packets with the well thrown, {without} without");
        }
        {
            // a missile flown straight through a well's core loses its target and is flung off
            var b = Tail(30000);
            var me = b.planes[0];
            var ms = Missile.Make(1, 1, 0, me.state.pos - me.Forward * 6000, me.state.vel + me.Forward * 400, b.time);
            var wl = Well.Make(0, 0, me.state.pos - me.Forward * 3500, Vec3d.Zero, b.time); wl.age = 1;
            b.bullets.Add(ms); b.bullets.Add(wl);
            b.Resolve(new TurnPlan?[] { Ahead(b, 0), Ahead(b, 1) }, 3);
            Check(!ms.Tracking && ms.lost == MissileLoss.Well, $"a heat-seeker drawn through its core loses its target ({ms.lost})");
            for (int t = 0; t < 2; t++) b.Resolve(new TurnPlan?[] { Ahead(b, 0), Ahead(b, 1) }, 3);
            Check(!b.SystemsOf(0).Hurt && !b.down[0], "and misses the plane it was after");

            // in a chase: a 109 fires a heat-seeker from behind, the P-51 throws a well ahead (straight or breaking left) and flies through
            int Saved(bool well)
            {
                int saved = 0;
                foreach (double gap in new[] { 2500.0, 3500, 4500, 5500 })
                    foreach (bool brk in new[] { false, true })
                    {
                        var c0 = Specials.Arm(CombatNumbers.P51D()); c0.flares = 0;
                        var c = new Battle(At(AircraftNumbers.P51D(), 0, gap, 300), c0, At(AircraftNumbers.Bf109G(), 0, 0, 300), Specials.Arm(CombatNumbers.Bf109G()));
                        bool hit = false;
                        for (int t = 0; t < 4 && !hit; t++)
                        {
                            var p0 = Ahead(c, 0); if (brk) p0.aim = new Vec3d(-0.7, 0.7, 0).Normalized;
                            p0.well = well && t == 0;
                            var p1 = Ahead(c, 1); p1.missile = t == 0; p1.missileTarget = 0;
                            hit = c.Resolve(new TurnPlan?[] { p0, p1 }, 3, false).hits.Any(h => h.shooter == 1 && h.damage >= 8);
                        }
                        if (!hit) saved++;
                    }
                return saved;
            }
            int s0 = Saved(false), s1 = Saved(true);
            Check(s1 >= s0 + 2, $"thrown ahead with a heat-seeker on your tail and no flares, it saves you {s1} times of 8 ({s0} without)");
        }
        {
            // the AI pilots: a missile on it, it throws one
            var b = Tail(30000);
            var me = b.planes[0];
            b.bullets.Add(Missile.Make(1, 1, 0, me.state.pos - me.Forward * 4000, me.state.vel + me.Forward * 400, b.time));
            b.balloonMines[0] = 0; b.jammers[0] = 0;
            var p = new EnemyAi { me = 0, sloppiness = 0 }.Plan(b, 3);
            Check(p.well, "an AI pilot with a heat-seeker on it throws a well");
        }
        {
            // clones, subsets, swaps, bytes
            var b = Tail(1500);
            var plan = Ahead(b, 0); plan.well = true;
            b.Resolve(new TurnPlan?[] { plan, Ahead(b, 1) }, 1.5);
            var c = b.Clone();
            Check(c.wells[0] == b.wells[0] && !ReferenceEquals(c.wells, b.wells) && c.bullets.Any(x => x is Well), "a clone keeps the count and the well, in its own array");
            Check(b.Subset(new[] { 1 }).bullets.Any(x => x is Well), "a subset of the 109 alone still feels the P-51's well");
            var back = Tail(1500);
            Wire.Restore(back, Wire.Encode(b));
            Check(Wire.Hash(back) == Wire.Hash(b) && back.bullets.Any(x => x is Well), "a snapshot brings it across exactly");
            b.Resolve(new TurnPlan?[] { Ahead(b, 0), Ahead(b, 1) }, 3);
            back.Resolve(new TurnPlan?[] { Ahead(back, 0), Ahead(back, 1) }, 3);
            Check(Wire.Hash(back) == Wire.Hash(b), "and both fly the next turn the same");
            int w0 = b.wells[0], w1 = b.wells[1];
            b.Swap(0, 1);
            Check(b.wells[1] == w0 && b.wells[0] == w1, "a swap moves the count with the plane");
            var pd = Wire.Decode<TurnPlan>(Wire.Encode(new TurnPlan { well = true, wellAt = 2.15 }));
            Check(pd.well && pd.wellAt == 2.15, "a plan with a well (and its moment) survives the trip to bytes");
        }
        {
            // the loadout pick
            var l = new Loadout(Kit.Missiles, Kit.Mines).Pick(Kit.Well, Craft.Spitfire);
            Check(l.Has(Kit.Well) && l.Has(Kit.Mines) && Loadout.Allowed(Kit.Well, Craft.F15), $"any plane can pick it ({l})");
            var set = new DogfightSetup { randomStart = false, ours = new[] { Craft.Mustang }, theirs = new[] { Craft.Bf109 },
                                          oursLoadouts = new[] { new Loadout(Kit.Well, Kit.Missiles) }, theirsLoadouts = new[] { new Loadout(Kit.Missiles, Kit.Mines) } };
            var b = Scenarios.Dogfight(DogfightSetup.FromBytes(set.ToBytes()), Vec3d.Zero);
            Check(b.wells[0] == Specials.Wells && b.wells[1] == 0 && b.balloonMines[0] == 0, "picked, it brings two wells (and only the picker has them), through the trip to bytes");
            var noWell = TurnPlan.Straight(b.planes[1]); noWell.well = true;
            b.Resolve(new TurnPlan?[] { TurnPlan.Straight(b.planes[0]), noWell }, 3);
            Check(!b.bullets.OfType<Well>().Any(), "without the pick, planning one does nothing");
        }
    }

    /// <summary>A 109 (1) gap ft behind a P-51 (0) that sits off ft to its right (and a little high), both heading north; the 109 with magnet rounds or not.</summary>
    static Battle Offset(double gap, double off, bool magnet)
    {
        var c1 = Specials.Arm(CombatNumbers.Bf109G()); c1.magnet = magnet;
        var b = new Battle(At(AircraftNumbers.P51D(), off, gap, 300), Specials.Arm(CombatNumbers.P51D()), At(AircraftNumbers.Bf109G(), 0, 0, 300), c1);
        b.dispersion = false;
        b.Spare(0);   // keep it flying to count the whole burst
        return b;
    }

    /// <summary>The packets of a one-turn burst from the 109 that hit the P-51 flying straight.</summary>
    static int BurstHits(Battle b)
    {
        var p1 = Ahead(b, 1); p1.burstStart = 0; p1.burstEnd = 3;
        return b.Resolve(new TurnPlan?[] { Ahead(b, 0), p1 }, 3, true).hits.Count(h => h.shooter == 1 && h.target == 0);
    }

    static void MagnetRounds()
    {
        Console.WriteLine("\nMagnet rounds");
        {
            int h0 = BurstHits(Offset(700, 0, false)), m0 = BurstHits(Offset(700, 0, true));
            int h20 = BurstHits(Offset(700, 20, false)), m20 = BurstHits(Offset(700, 20, true));
            int h40 = BurstHits(Offset(700, 40, false)), m40 = BurstHits(Offset(700, 40, true));
            Check(m0 == h0 && h0 > 0, $"a burst dead on hits the same with them or without ({m0} and {h0} packets)");
            Check(h20 == 0 && m20 > 0, $"a burst passing 20 ft wide at 700 ft is pulled in ({m20} packets hit, {h20} without)");
            Check(m40 == 0, $"one 40 ft wide stays a miss: the pull is limited ({m40})");
            Check(BurstHits(Offset(400, 20, true)) == 0 && BurstHits(Offset(400, 15, true)) > 0, "close in there is less time to bend: at 400 ft only a 15 ft miss is pulled in");
        }
        {
            // a friend where the enemy was: no pull, the rounds fly just as they would without
            var a = Offset(700, 20, true); var c = Offset(700, 20, false);
            a.side[0] = 1; c.side[0] = 1;
            var pa = Ahead(a, 1); pa.burstStart = 0; pa.burstEnd = 1;
            a.Resolve(new TurnPlan?[] { Ahead(a, 0), pa }, 1, false); c.Resolve(new TurnPlan?[] { Ahead(c, 0), pa }, 1, false);
            var ba = a.bullets.Where(x => !(x is Missile)).ToList(); var bc = c.bullets.Where(x => !(x is Missile)).ToList();
            Check(ba.Count == bc.Count && ba.Count > 0 && ba.Zip(bc, (x, y) => (x.pos - y.pos).Length).Max() < 1e-6, "they never curve toward a friend");
        }
        {
            // what the preview and the AI see: a clone flies them the same
            var b = Offset(700, 20, true);
            int fromClone = BurstHits(b.Clone());
            Check(fromClone == BurstHits(b), $"a clone (the tracer preview) curves them the same ({fromClone})");
        }
        {
            // the AI pilot: it fires at a near miss it would let pass without them
            bool Fires(bool magnet, double off) { var b = Offset(1000, off, magnet); var p = new EnemyAi { me = 1, sloppiness = 0 }.Plan(b, 3); return p.burstEnd > p.burstStart; }
            double off = 0;
            for (double o = 20; o <= 80; o += 5) if (!Fires(false, o)) { off = o; break; }
            Check(off > 0 && Fires(true, off), $"an AI pilot with them fires at a target {off} ft off his line, one without holds his fire");
        }
        {
            // the trip to bytes: a snapshot with magnet rounds in the air flies on the same
            var b = Offset(700, 20, true);
            var p1 = Ahead(b, 1); p1.burstStart = 0; p1.burstEnd = 3;
            b.Resolve(new TurnPlan?[] { Ahead(b, 0), p1 }, 1.5);
            var back = Offset(700, 20, true);
            Wire.Restore(back, Wire.Encode(b));
            Check(Wire.Hash(back) == Wire.Hash(b) && back.bullets.Any(x => x.magnet), "a snapshot brings the magnet rounds in the air across exactly");
            var hb = b.Resolve(new TurnPlan?[] { Ahead(b, 0), p1 }, 1.5).hits.Count;
            var hk = back.Resolve(new TurnPlan?[] { Ahead(back, 0), p1 }, 1.5).hits.Count;
            Check(Wire.Hash(back) == Wire.Hash(b) && hb == hk, "and both fly the rest of the burst the same");
        }
        {
            // the loadout pick
            var l = new Loadout(Kit.Missiles, Kit.Mines).Pick(Kit.Magnet, Craft.F15);
            Check(l.Has(Kit.Magnet) && Loadout.Short(Kit.Magnet) == "MAG", $"any plane can pick it ({l})");
            var set = new DogfightSetup { randomStart = false, ours = new[] { Craft.Mustang }, theirs = new[] { Craft.Bf109 },
                                          oursLoadouts = new[] { new Loadout(Kit.Magnet, Kit.Missiles) }, theirsLoadouts = new[] { new Loadout(Kit.Missiles, Kit.Mines) } };
            var b = Scenarios.Dogfight(DogfightSetup.FromBytes(set.ToBytes()), Vec3d.Zero);
            Check(b.combat[0].magnet && !b.combat[1].magnet, "picked, the picker's rounds (only his) are magnet rounds, through the trip to bytes");
            set.arcade = false;
            Check(!Scenarios.Dogfight(DogfightSetup.FromBytes(set.ToBytes()), Vec3d.Zero).combat[0].magnet, "with Arcade off, none");
        }
    }

    /// <summary>How far aircraft j flies in one straight turn of battle b (everyone flying straight).</summary>
    static double Flown(Battle b, int j)
    {
        var p0 = b.planes[j].state.pos;
        b.Resolve(new TurnPlan?[] { Ahead(b, 0), Ahead(b, 1) }, 3, false);
        return (b.planes[j].state.pos - p0).Length;
    }

    static void SlowBomb()
    {
        Console.WriteLine("\nTime slow bomb");
        {
            // a 109 1,500 ft behind: the bomb goes back at it and bursts round it
            var b = Tail(1500);
            var throwIt = Ahead(b, 0); throwIt.slow = true; throwIt.slowAt = 1;
            var r = b.Resolve(new TurnPlan?[] { throwIt, Ahead(b, 1) }, 3);
            var tb = b.bullets.OfType<TimeBubble>().FirstOrDefault();
            Check(b.slowBombs[0] == Specials.SlowBombs - 1 && r.bubbles.Count == 1 && tb != null, $"one is thrown, {b.slowBombs[0]} left, its flight recorded");
            Check(r.bubbles.Count == 1 && Math.Abs(r.bubbles[0].startFrame - 1 / Battle.StepSec) <= 1, $"it goes at the moment planned (frame {(r.bubbles.Count > 0 ? r.bubbles[0].startFrame : -1)})");
            double off = tb == null ? 1e9 : (tb.pos - b.planes[1].state.pos).Length;
            Check(tb != null && tb.Burst && tb.vel.Length == 0 && off < Specials.SlowRadiusFt * Specials.SlowCoreShare, $"it bursts on the 109 and hangs there (the 109 {off:F0} ft from its middle)");
            Check(!r.hits.Any(), "it hurts no one");
            Check(b.SlowClock(b.planes[1].state.pos) == Specials.SlowFactor && b.SlowClock(b.planes[0].state.pos) == 1, "inside, the 109's clock runs at half; the P-51 that threw it is clear of it");
            for (int t = 0; t < 2; t++) b.Resolve(new TurnPlan?[] { Ahead(b, 0), Ahead(b, 1) }, 3);
            Check(!b.bullets.OfType<TimeBubble>().Any(), $"it is gone after {Specials.SlowLifeSec:F0} s");
        }
        {
            // a bubble sat on each plane: it flies about half as far, the thrower too (it slows everyone)
            var free = Tail(30000); double free1 = Flown(free, 1);
            var slowed = Tail(30000);
            var on109 = TimeBubble.Make(0, 0, slowed.planes[1].state.pos, slowed.planes[1].state.pos + slowed.planes[1].state.vel * 1.5, slowed.time); on109.age = Specials.SlowFlySec;
            on109.pos = slowed.planes[1].state.pos + slowed.planes[1].state.vel * 1.5; on109.vel = Vec3d.Zero;
            slowed.bullets.Add(on109);
            double slow1 = Flown(slowed, 1);
            Check(slow1 / free1 > 0.45 && slow1 / free1 < 0.7, $"a 109 flown through the middle of one covers {slow1 / free1 * 100:F0}% of the ground in a turn");
            var mine = Tail(30000); double mine0 = Flown(Tail(30000), 0);
            var onMe = TimeBubble.Make(0, 0, mine.planes[0].state.pos, mine.planes[0].state.pos, mine.time); onMe.age = Specials.SlowFlySec;
            mine.bullets.Add(onMe);
            double own = Flown(mine, 0);
            Check(own / mine0 > 0.45 && own / mine0 < 0.75, $"the P-51 in its own bubble is slowed too ({own / mine0 * 100:F0}% of the ground)");
            // thrown at a 109 only 600 ft behind, it bursts past him, clear of the thrower
            var close = Tail(600);
            var land = Specials.SlowLanding(close, 0);
            var meThen = close.planes[0].state.pos + close.planes[0].state.vel * Specials.SlowFlySec;
            Check((land - meThen).Length >= Specials.SlowRadiusFt + Specials.SlowClearFt - 1 && (land - close.planes[1].state.pos).Length < Specials.SlowRadiusFt * Specials.SlowCoreShare,
                  $"thrown at a 109 600 ft behind, it bursts {(land - meThen).Length:F0} ft from the thrower, with the 109 inside");
        }
        {
            // a 109 900 ft behind: thrown at it, the gap opens
            double Gap(bool slow)
            {
                var b = Tail(900);
                for (int t = 0; t < 2; t++)
                {
                    var p0 = Ahead(b, 0); p0.slow = slow && t == 0;
                    b.Resolve(new TurnPlan?[] { p0, Ahead(b, 1) }, 3, false);
                }
                return (b.planes[0].state.pos - b.planes[1].state.pos).Length;
            }
            double g0 = Gap(false), g1 = Gap(true);
            Check(g1 > g0 + 800, $"thrown at a 109 900 ft behind, the gap is {g1:F0} ft two turns on ({g0:F0} ft without)");

            // and its stream crawls: fewer of its rounds land
            int Hits(bool slow)
            {
                int hits = 0;
                foreach (double gap in new[] { 600.0, 900, 1200 })
                {
                    var b = Tail(gap); b.dispersion = false;
                    for (int t = 0; t < 2; t++)
                    {
                        var p0 = Ahead(b, 0); p0.slow = slow && t == 0;
                        var p1 = Ahead(b, 1); p1.burstStart = 0; p1.burstEnd = 3;
                        hits += b.Resolve(new TurnPlan?[] { p0, p1 }, 3, false).hits.Count(h => h.shooter == 1);
                    }
                }
                return hits;
            }
            int without = Hits(false), with = Hits(true);
            Check(with < without, $"a 109 shooting from behind lands {with} packets with the bomb thrown, {without} without");
        }
        {
            // a heat-seeker on your tail: the bomb goes at the missile, not the 109 farther back, and the missile loses time
            var b = Tail(30000);
            var me = b.planes[0];
            var ms = Missile.Make(1, 1, 0, me.state.pos - me.Forward * 2500, me.state.vel + me.Forward * 200, b.time);
            b.bullets.Add(ms);
            var landing = Specials.SlowLanding(b, 0);
            Check((landing - ms.pos).Length < 2500, $"aimed at the missile closing on it ({(landing - ms.pos).Length:F0} ft from it)");
            var c = b.Clone();
            var p0 = Ahead(b, 0); p0.slow = true;
            b.Resolve(new TurnPlan?[] { p0, Ahead(b, 1) }, 1.5, false);
            c.Resolve(new TurnPlan?[] { Ahead(c, 0), Ahead(c, 1) }, 1.5, false);
            var msB = b.bullets.OfType<Missile>().FirstOrDefault(); var msC = c.bullets.OfType<Missile>().FirstOrDefault();
            Check(msB != null && msC != null && msB.age < msC.age - 0.3, $"the missile in it lives {(msB == null ? 0 : msB.age):F2} s of the 1.5 s ({(msC == null ? 0 : msC.age):F2} s without)");
        }
        {
            // the AI pilots: a missile on it and no well, it throws one at it
            var b = Tail(30000);
            var me = b.planes[0];
            b.bullets.Add(Missile.Make(1, 1, 0, me.state.pos - me.Forward * 3500, me.state.vel + me.Forward * 400, b.time));
            b.balloonMines[0] = 0; b.jammers[0] = 0; b.wells[0] = 0;
            var p = new EnemyAi { me = 0, sloppiness = 0 }.Plan(b, 3);
            Check(p.slow, "an AI pilot with a heat-seeker on it and no wells throws a slow bomb");
        }
        {
            // clones, subsets, swaps, bytes
            var b = Tail(1500);
            var plan = Ahead(b, 0); plan.slow = true;
            b.Resolve(new TurnPlan?[] { plan, Ahead(b, 1) }, 1.5);
            var c = b.Clone();
            Check(c.slowBombs[0] == b.slowBombs[0] && !ReferenceEquals(c.slowBombs, b.slowBombs) && c.bullets.Any(x => x is TimeBubble), "a clone keeps the count and the bubble, in its own array");
            Check(b.Subset(new[] { 1 }).bullets.Any(x => x is TimeBubble), "a subset of the 109 alone is still slowed by the P-51's bubble");
            var back = Tail(1500);
            Wire.Restore(back, Wire.Encode(b));
            Check(Wire.Hash(back) == Wire.Hash(b) && back.bullets.Any(x => x is TimeBubble), "a snapshot brings it across exactly");
            b.Resolve(new TurnPlan?[] { Ahead(b, 0), Ahead(b, 1) }, 3);
            back.Resolve(new TurnPlan?[] { Ahead(back, 0), Ahead(back, 1) }, 3);
            Check(Wire.Hash(back) == Wire.Hash(b), "and both fly the next turn the same");
            int s0 = b.slowBombs[0], s1 = b.slowBombs[1];
            b.Swap(0, 1);
            Check(b.slowBombs[1] == s0 && b.slowBombs[0] == s1, "a swap moves the count with the plane");
            var pd = Wire.Decode<TurnPlan>(Wire.Encode(new TurnPlan { slow = true, slowAt = 2.15 }));
            Check(pd.slow && pd.slowAt == 2.15, "a plan with a slow bomb (and its moment) survives the trip to bytes");
        }
        {
            // the loadout pick
            var l = new Loadout(Kit.Missiles, Kit.Mines).Pick(Kit.Slow, Craft.Spitfire);
            Check(l.Has(Kit.Slow) && l.Has(Kit.Mines) && Loadout.Allowed(Kit.Slow, Craft.F15), $"any plane can pick it ({l})");
            var set = new DogfightSetup { randomStart = false, ours = new[] { Craft.Mustang }, theirs = new[] { Craft.Bf109 },
                                          oursLoadouts = new[] { new Loadout(Kit.Slow, Kit.Missiles) }, theirsLoadouts = new[] { new Loadout(Kit.Missiles, Kit.Mines) } };
            var b = Scenarios.Dogfight(DogfightSetup.FromBytes(set.ToBytes()), Vec3d.Zero);
            Check(b.slowBombs[0] == Specials.SlowBombs && b.slowBombs[1] == 0 && b.wells[0] == 0, "picked, it brings two slow bombs (and only the picker has them), through the trip to bytes");
            var none = TurnPlan.Straight(b.planes[1]); none.slow = true;
            b.Resolve(new TurnPlan?[] { TurnPlan.Straight(b.planes[0]), none }, 3);
            Check(!b.bullets.OfType<TimeBubble>().Any(), "without the pick, planning one does nothing");
        }
    }

    static void BlinkHop()
    {
        Console.WriteLine("\nBlink");
        {
            var b = Tail(30000);
            var hop = Ahead(b, 0); hop.blink = true; hop.blinkAt = 1;
            var plain = Tail(30000);
            var r = b.Resolve(new TurnPlan?[] { hop, Ahead(b, 1) }, 3);
            plain.Resolve(new TurnPlan?[] { Ahead(plain, 0), Ahead(plain, 1) }, 3);
            int jf = Array.FindIndex(r.frames[0], f => f.blink);
            double gained = b.planes[0].state.pos.y - plain.planes[0].state.pos.y;
            Check(b.blinks[0] == Specials.Blinks - 1, $"one goes, {b.blinks[0]} left");
            Check(Math.Abs(jf - 1 / Battle.StepSec) <= 1, $"at the moment planned (frame {jf})");
            Check(Math.Abs(gained - Specials.BlinkFt) < 60, $"it ends the turn {gained:F0} ft farther up the path than flying it ({Specials.BlinkFt:F0})");
            Check(jf > 0 && (r.frames[0][jf].pos - r.frames[0][jf - 1].pos).Length > Specials.BlinkFt - 60, "in a single step");
            Check(Math.Abs(b.planes[0].state.vel.Length - plain.planes[0].state.vel.Length) < 15, "flying on at the speed it would have had there");
        }
        {
            // along the planned path: in a hard left turn the hop curls left with it
            var b = Tail(30000); var plain = Tail(30000);
            var f = b.planes[0].Forward;
            var turn = new TurnPlan { aim = new Vec3d(-f.y, f.x, 0).Normalized, throttle = 1 };
            var hop = turn; hop.blink = true; hop.blinkAt = 0.5;
            b.Resolve(new TurnPlan?[] { hop, Ahead(b, 1) }, 3);
            plain.Resolve(new TurnPlan?[] { turn, Ahead(plain, 1) }, 3);
            double h = Heading(b.planes[0].Forward) - Heading(plain.planes[0].Forward);
            h = h < -180 ? h + 360 : h > 180 ? h - 360 : h;
            Check(h > 5, $"in a hard turn it comes out turned on round the turn ({h:F0} deg more than flying it)");
        }
        {
            // a 109 behind fires a half-second burst; the P-51 blinks as the rounds are on their way, or doesn't
            int Hits(bool blink)
            {
                int hits = 0;
                foreach (double gap in new[] { 1000.0, 1300, 1600 })
                {
                    var b = Tail(gap); b.dispersion = false;
                    var p0 = Ahead(b, 0); p0.blink = blink; p0.blinkAt = 0.5;
                    var p1 = Ahead(b, 1); p1.burstStart = 0; p1.burstEnd = 0.5;
                    var rr = b.Resolve(new TurnPlan?[] { p0, p1 }, 3, false);
                    hits += rr.hits.Count(x => x.shooter == 1);
                }
                return hits;
            }
            int without = Hits(false), with = Hits(true);
            Check(without > 0 && with * 3 < without, $"a burst on its way when it blinks lands {with} packets, {without} without");
        }
        {
            // a heat-seeker from behind with no flares: blink as it closes and it loses you
            int Saved(bool blink)
            {
                int saved = 0;
                foreach (double gap in new[] { 2500.0, 3500, 4500, 5500 })
                {
                    var c0 = Specials.Arm(CombatNumbers.P51D()); c0.flares = 0;
                    var c = new Battle(At(AircraftNumbers.P51D(), 0, gap, 300), c0, At(AircraftNumbers.Bf109G(), 0, 0, 300), Specials.Arm(CombatNumbers.Bf109G()));
                    bool hit = false;
                    for (int t = 0; t < 4 && !hit; t++)
                    {
                        var p0 = Ahead(c, 0);
                        if (blink && t > 0 && c.blinks[0] > 0) p0 = new EnemyAi { me = 0, sloppiness = 0 }.Plan(c, 3);
                        else if (t > 0) p0 = Ahead(c, 0);
                        var p1 = Ahead(c, 1); p1.missile = t == 0; p1.missileTarget = 0;
                        hit = c.Resolve(new TurnPlan?[] { p0, p1 }, 3, false).hits.Any(x => x.shooter == 1 && x.damage >= 8);
                    }
                    if (!hit) saved++;
                }
                return saved;
            }
            int s0 = Saved(false), s1 = Saved(true);
            Check(s1 >= s0 + 2, $"with a heat-seeker on its tail and no flares, an AI pilot's blink saves it {s1} times of 4 ({s0} without)");
            var b = Tail(30000);
            var me = b.planes[0];
            var ms = Missile.Make(1, 1, 0, me.state.pos - me.Forward * 2000, me.state.vel + me.Forward * 400, b.time);
            b.bullets.Add(ms);
            var hop = Ahead(b, 0); hop.blink = true; hop.blinkAt = 0.2;
            b.Resolve(new TurnPlan?[] { hop, Ahead(b, 1) }, 3);
            Check(!ms.Tracking && ms.lost == MissileLoss.Blink, $"a heat-seeker 2,000 ft behind loses you ({ms.lost})");
        }
        {
            // diving at the ground: the hop stops short, over the ground
            var n = AircraftNumbers.P51D();
            var dive = new FlightModel(n, PointMass.Make(new Vec3d(0, 0, 900), Atmosphere.IasToTasKt(300, 900), North)) { throttle = 1 };
            var st = dive.state; st.vel = new Vec3d(0, st.vel.Length * 0.7, -st.vel.Length * 0.7); st.up = new Vec3d(0, 0.7, 0.7).Normalized; dive.state = st;
            var b = new Battle(dive, Specials.Arm(CombatNumbers.P51D()), At(AircraftNumbers.Bf109G(), 0, -30000, 300), Specials.Arm(CombatNumbers.Bf109G()));
            var hop = new TurnPlan { aim = dive.Forward, throttle = 1, blink = true, blinkAt = 0 };
            b.Resolve(new TurnPlan?[] { hop, Ahead(b, 1) }, 0.1);
            Check(!b.crashed[0] && b.planes[0].AltFt >= Specials.BlinkFloorFt - 30, $"diving at the ground, it stops short ({b.planes[0].AltFt:F0} ft up)");
        }
        {
            // the AI: someone on its tail with guns on it, and no mines
            var b = Tail(900);
            b.balloonMines[0] = 0; b.wells[0] = 0; b.heatBlades[0] = 0; b.slowBombs[0] = 0;
            var p = new EnemyAi { me = 0, sloppiness = 0 }.Plan(b, 3);
            Check(p.blink, "an AI pilot with a 109 on its tail (and no mines or wells) blinks");
        }
        {
            // clones, subsets, swaps, bytes
            var b = Tail(1500);
            var c = b.Clone();
            Check(c.blinks[0] == b.blinks[0] && !ReferenceEquals(c.blinks, b.blinks), "a clone keeps the count, in its own array");
            Check(b.Subset(new[] { 1 }).blinks[0] == b.blinks[1], "a subset keeps it");
            var plan = Ahead(b, 0); plan.blink = true; plan.blinkAt = 1.25;
            b.Resolve(new TurnPlan?[] { plan, Ahead(b, 1) }, 3);
            var back = Tail(1500);
            Wire.Restore(back, Wire.Encode(b));
            Check(Wire.Hash(back) == Wire.Hash(b) && back.blinks[0] == b.blinks[0], "a snapshot brings it across exactly");
            var p2 = Ahead(b, 0); p2.blink = true; var p2b = Ahead(back, 0); p2b.blink = true;
            b.Resolve(new TurnPlan?[] { p2, Ahead(b, 1) }, 3);
            back.Resolve(new TurnPlan?[] { p2b, Ahead(back, 1) }, 3);
            Check(Wire.Hash(back) == Wire.Hash(b) && b.blinks[0] == 0, "and both blink the next turn the same");
            int k0 = b.blinks[0], k1 = b.blinks[1];
            b.Swap(0, 1);
            Check(b.blinks[1] == k0 && b.blinks[0] == k1, "a swap moves the count with the plane");
            var pd = Wire.Decode<TurnPlan>(Wire.Encode(new TurnPlan { blink = true, blinkAt = 2.15 }));
            Check(pd.blink && pd.blinkAt == 2.15, "a plan with a blink (and its moment) survives the trip to bytes");
        }
        {
            var l = new Loadout(Kit.Missiles, Kit.Mines).Pick(Kit.Blink, Craft.F15);
            Check(l.Has(Kit.Blink) && Loadout.Allowed(Kit.Blink, Craft.Zero), $"any plane can pick it ({l})");
            var set = new DogfightSetup { randomStart = false, ours = new[] { Craft.Mustang }, theirs = new[] { Craft.Bf109 },
                                          oursLoadouts = new[] { new Loadout(Kit.Blink, Kit.Missiles) }, theirsLoadouts = new[] { new Loadout(Kit.Missiles, Kit.Mines) } };
            var b = Scenarios.Dogfight(DogfightSetup.FromBytes(set.ToBytes()), Vec3d.Zero);
            Check(b.blinks[0] == Specials.Blinks && b.blinks[1] == 0, "picked, it brings two blinks (and only the picker has them), through the trip to bytes");
            var start = b.planes[1].state.pos;
            var no = TurnPlan.Straight(b.planes[1]); no.blink = true;
            var r = b.Resolve(new TurnPlan?[] { TurnPlan.Straight(b.planes[0]), no }, 3);
            Check(!r.frames[1].Any(f => f.blink), "without the pick, planning one does nothing");
        }
    }
}
