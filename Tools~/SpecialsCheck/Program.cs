// Checks of the arcade specials without Unity (see Turn/Core/Specials.cs): the balloon mine catches a tailer who flies
// on and spares one who turns away, goes at the moment planned and drifts slowly; the heat blade cuts an enemy in half and flies on, keeps the guns cold and runs hot;
// stealth hides you from the AI and the flak but not from a heat-seeker, and firing shows you; the AI pilots use all
// three; the gravity well bends a hard turn tighter, makes a tailer's rounds miss and pulls a heat-seeker off; and
// everything survives a clone, a subset, a swap and the trip to bytes.
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
            var ai = new EnemyAi { me = 0, sloppiness = 0 };
            var p = ai.Plan(b, 3);
            Check(p.mine, "with a 109 on its six, it lets a mine go");
            b.balloonMines[0] = 0; ai.Forget();
            p = ai.Plan(b, 3);
            Check(p.well && !p.mine, "no mines left: it throws a gravity well");
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
}
