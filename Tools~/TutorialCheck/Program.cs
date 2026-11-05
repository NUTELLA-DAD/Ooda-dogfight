// Checks of the training lessons without Unity (see Turn/Core/Tutorial.cs): every lesson builds its one on one and arms
// the foe as it says (the drone and the unarmed 109 can't shoot, the armed one's rounds are softened, nobody but you gets
// the specials); each goal ticks when it should and not before; the drone holds its course; and the lessons can be
// finished: a student in your seat (trying arrows against the bright ghost, as a player does) gets on the unarmed 109's six, shoots the drone down, and lives through the break
// turn lesson.
// Usage: dotnet run   (exit code = failures)
using System;
using System.Linq;
using Ooda.Flight.Core;
using Ooda.Turn.Core;

static class Program
{
    static int fails;
    static void Check(bool ok, string what) { if (!ok) fails++; Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}"); }

    /// <summary>A lesson's battle as the game builds it (over its arena, no ground but the sea), with the foe armed and its pilot.</summary>
    static Battle Build(Tutorial.Lesson l, out EnemyAi foe)
    {
        var a = Arenas.Get(Tutorial.ArenaIndex);
        var b = Scenarios.Dogfight(Tutorial.Setup(l), a.CentreFt, a.altFt, 300, 3000, true, a.axisDeg);
        Tutorial.Arm(b, l);
        foe = Tutorial.Pilot(l, new EnemyAi { me = 1, rng = new Lcg(77 + 13), ignoreObstacles = true, role = EnemyAi.Role.Hunter });
        return b;
    }

    /// <summary>
    /// A student in your seat who uses what the screen shows: the foe's bright ghost (its likeliest plan; the drone just
    /// holds), and arrows tried across the reach shell, as a player drags the stick. It keeps the arrow that scores best
    /// (onto its six, or with guns, the most hits), and fires only over the moments the preview says hit, as F does.
    /// </summary>
    static TurnPlan Student(Battle b, EnemyAi foe, bool guns)
    {
        var env = Envelope.Compute(b.planes[0], 3);
        TurnPlan? bright = foe == null ? (TurnPlan?)null : foe.Predict(b, 3, 1).plans[0];
        TurnPlan best = TurnPlan.Straight(b.planes[0]);
        double bestScore = double.NegativeInfinity;
        (double a, double z) bestWindow = (0, 0);
        for (int d = 0; d < Envelope.Directions; d++)
            for (double frac = 0; frac <= 1.001; frac += 0.125)
            {
                double phi = 2 * Math.PI * d / Envelope.Directions;
                var p = new TurnPlan { aim = frac == 0 ? env.fwd : env.Direction(phi, env.MaxAngle(phi) * frac), throttle = 1, burstStart = 0, burstEnd = guns ? 3 : 0 };
                var s = b.Clone(); s.dispersion = false;
                if (guns) s.health[1] = double.PositiveInfinity;
                var r = s.Resolve(new TurnPlan?[] { p, bright }, 3);
                if (s.Out(0)) continue;   // not into the ground, nor into it
                var on = r.hits.Where(h => h.shooter == 0 && h.target == 1).Select(h => h.fireTime - r.startTime).ToList();
                Tutorial.OnSix(s, 0, 1, out double range, out double nose, out double tail);
                double score = (guns ? on.Count * 10 : 0) - Math.Abs(range - 1000) / 1000 - nose / 30 - tail / 30;
                if (score > bestScore) { bestScore = score; best = p; bestWindow = on.Count > 0 ? (on.Min() - 0.05, on.Max() + 0.05) : (0, 0); }
            }
        best.burstStart = Math.Max(0, bestWindow.a); best.burstEnd = Math.Min(3, bestWindow.z);
        return best;
    }

    /// <summary>Fly turns with the student in your seat until done says stop (or the fight ends). Returns the turns flown.</summary>
    static int Fly(Battle b, EnemyAi foe, int most, bool guns, Func<Battle, bool> done)
    {
        for (int t = 1; t <= most; t++)
        {
            var plans = new TurnPlan?[b.Count];
            plans[0] = Student(b, foe, guns);
            if (foe != null) plans[1] = foe.Plan(b, 3);
            b.Resolve(plans, 3, false);
            if (done(b) || b.Over) return t;
        }
        return most + 1;
    }

    static int Main()
    {
        Console.WriteLine("The lessons");
        Check(Tutorial.Count == 7, $"{Tutorial.Count} lessons");
        foreach (var l in Tutorial.All)
        {
            var b = Build(l, out var foe);
            bool shape = b.Count == 2 && b.side[0] == 0 && b.side[1] == 1
                      && DogfightSetup.Of(b.planes[0].numbers) == Craft.Mustang && DogfightSetup.Of(b.planes[1].numbers) == Craft.Bf109;
            bool words = !string.IsNullOrEmpty(l.title) && !string.IsNullOrEmpty(l.intro) && !string.IsNullOrEmpty(l.done)
                      && l.steps.Length > 0 && l.steps.All(s => !string.IsNullOrEmpty(s.text) && !string.IsNullOrEmpty(s.todo));
            bool guns = l.foe == Tutorial.Foe.Armed ? b.ammo[1] > 0 : b.ammo[1] == 0;
            bool noExtras = b.missiles[1] == 0 && b.balloonMines[1] + b.heatBlades[1] + b.stealths[1] == 0;
            bool mine = l.arcade ? b.balloonMines[0] > 0 : b.balloonMines[0] == 0;
            bool pilot = (foe == null) == (l.foe == Tutorial.Foe.Drone) && (foe == null || foe.sloppiness == l.sloppiness);
            Check(shape && words && guns && noExtras && mine && pilot && b.planes[0].throttle == l.throttle,
                  $"{l.title}: P-51 v Bf 109, {l.foe}{(l.foe == Tutorial.Foe.Armed ? $" ({l.damageScale * 100:F0}% damage)" : "")}, {l.steps.Length} steps{(l.arcade ? ", specials for you" : "")}");
        }
        {
            var l = Tutorial.All.First(x => x.foeHealth > 0);
            var b = Build(l, out _);
            Check(b.health[1] == l.foeHealth && b.combat[1].health == l.foeHealth, $"the drone's airframe is {l.foeHealth} (the bar reads it full)");
            var real = CombatNumbers.Bf109G();
            Check(real.ammoSec > 0 && real.missiles > 0, "and the real Bf 109's numbers are left as they were (Arm copies them)");
        }

        Console.WriteLine("The goals");
        {
            var l = Tutorial.All[0];
            var b = Build(l, out _);
            var env = Envelope.Compute(b.planes[0], 3);
            var plan = new TurnPlan { aim = env.fwd, throttle = 1 };
            Tutorial.Look Look(TurnPlan p) => new Tutorial.Look { battle = b, plan = p, env = env, planning = true, foe = 1, foeHealthAtStep = b.health[1] };
            var steer = new Tutorial.Step { goal = Tutorial.Goal.Steer };
            Check(!Tutorial.Met(steer, Look(plan)), "straight ahead is not steering");
            var p = plan; p.aim = env.Direction(Math.PI / 2, 15 * Math.PI / 180);
            Check(Tutorial.Met(steer, Look(p)), "15° to the side is");
            var hard = new Tutorial.Step { goal = Tutorial.Goal.PullHard };
            p.aim = env.Direction(0, env.MaxAngle(0) * 0.5);
            Check(!Tutorial.Met(hard, Look(p)), $"half the hardest turn is not pulling hard (pull {Tutorial.Pull(p, env):F2})");
            p.aim = env.Direction(0, env.MaxAngle(0) * 0.85);
            Check(Tutorial.Met(hard, Look(p)), $"85% is (pull {Tutorial.Pull(p, env):F2})");
            p.overpull = 0.3;
            Check(!Tutorial.Met(hard, Look(p)), "but not once it is into the overpull band");
            var power = new Tutorial.Step { goal = Tutorial.Goal.FullPower };
            var q = plan; q.throttle = 0.6;
            Check(!Tutorial.Met(power, Look(q)), "60% throttle is not full power");
            q.throttle = 1; Check(!Tutorial.Met(power, Look(q)), "nor full throttle without WEP");
            q.wep = true; Check(Tutorial.Met(power, Look(q)), "full and WEP is");
            var esc = new Tutorial.Step { goal = Tutorial.Goal.Escape };
            Check(!Tutorial.Met(esc, Look(plan)), "flying straight is no escape");
            var e = plan; e.brake = true; Check(Tutorial.Met(esc, Look(e)), "the speed brake is");
            e = plan; e.mine = true; Check(Tutorial.Met(esc, Look(e)), "a balloon mine is");
            var turns = new Tutorial.Step { goal = Tutorial.Goal.FlyTurns, turns = 2 };
            var k = Look(plan); k.turns = 1; Check(!Tutorial.Met(turns, k), "one turn of two is not done");
            k.turns = 2; Check(Tutorial.Met(turns, k), "two is");
            var hardTurns = new Tutorial.Step { goal = Tutorial.Goal.FlyHardTurn, turns = 2 };
            k = Look(plan); k.turns = 2; k.hardTurns = 1;
            Check(!Tutorial.Met(hardTurns, k), "two turns flown, one of them hard, is not two hard turns");
            k.hardTurns = 2; Check(Tutorial.Met(hardTurns, k), "two hard ones is");
            var six = new Tutorial.Step { goal = Tutorial.Goal.OnSix };
            Check(!Tutorial.Met(six, Look(plan)), "side by side is not on its six");
            var off = Build(Tutorial.All.First(x => x.start == Scenarios.Start.Offensive), out _);
            Tutorial.OnSix(off, 0, 1, out double r, out double nose, out double tail);
            Check(Tutorial.Met(six, new Tutorial.Look { battle = off, plan = plan, env = Envelope.Compute(off.planes[0], 3), planning = true, foe = 1 }),
                  $"the close offensive start is ({r:F0} ft, {nose:F0}° off your nose, {tail:F0}° off its tail)");
        }

        Console.WriteLine("The drone");
        {
            var b = Build(Tutorial.All[0], out var foe);
            Vec3d fwd = b.planes[1].Forward;
            double alt = b.planes[1].AltFt;
            int hitsOnMe = 0;
            for (int t = 0; t < 5; t++)
            {
                var r = b.Resolve(new TurnPlan?[] { TurnPlan.Straight(b.planes[0]), null }, 3);
                hitsOnMe += r.hits.Count(h => h.target == 0);
            }
            double turned = Envelope.Angle(fwd, b.planes[1].Forward) * 180 / Math.PI;
            Check(foe == null && turned < 3 && Math.Abs(b.planes[1].AltFt - alt) < 300, $"with no pilot it holds its course ({turned:F1}° off, {b.planes[1].AltFt - alt:+0;-0} ft in 15 s)");
            Check(hitsOnMe == 0 && !b.Out(0) && !b.Out(1), "and nobody is hit or goes down");
        }

        Console.WriteLine("The lessons can be finished");
        {
            var l = Tutorial.All.First(x => x.title.StartsWith("Get on"));
            var b = Build(l, out var foe);
            int t = Fly(b, foe, 12, false, x => Tutorial.OnSix(x, 0, 1, out _, out _, out _));
            Tutorial.OnSix(b, 0, 1, out double r, out double nose, out double tail);
            Check(t <= 12 && !b.Out(0), $"{l.title}: on its six in {t} turns ({r:F0} ft, {nose:F0}° off the nose, {tail:F0}° off its tail)");
        }
        {
            var l = Tutorial.All.First(x => x.title.StartsWith("Guns"));
            var b = Build(l, out var foe);
            int t = Fly(b, foe, 10, true, x => x.Out(1));
            Check(t <= 10 && b.Out(1) && !b.Out(0), $"{l.title}: the drone goes down in {t} turns");
        }
        {
            var l = Tutorial.All.First(x => x.start == Scenarios.Start.Defensive);
            var b = Build(l, out var foe);
            int t = Fly(b, foe, 4, false, x => false);
            Check(!b.Out(0), $"{l.title}: you are still flying after 4 turns ({b.health[0]:F1} of {b.combat[0].health} left)");
            var full = Scenarios.Dogfight(Tutorial.Setup(l), Arenas.Get(Tutorial.ArenaIndex).CentreFt, Arenas.Get(Tutorial.ArenaIndex).altFt, 300, 3000, true, Arenas.Get(Tutorial.ArenaIndex).axisDeg);
            Check(b.combat[1].damage < full.combat[1].damage, $"its rounds do {b.combat[1].damage:F2} against the real {full.combat[1].damage:F2}");
        }
        {
            var l = Tutorial.All[Tutorial.Count - 1];
            var b = Build(l, out var foe);
            int t = Fly(b, foe, 30, true, x => x.Out(1));
            Console.WriteLine($"  (info) {l.title}: the student {(b.Out(1) && !b.Out(0) ? $"wins in {t} turns" : b.Out(0) ? $"loses in {t} turns" : "has not won in 30 turns")} (you {b.health[0]:F1} of {b.combat[0].health}, it {b.health[1]:F1} of {b.combat[1].health}, range {(b.planes[1].state.pos - b.planes[0].state.pos).Length:F0} ft)");
        }

        Console.WriteLine(fails == 0 ? "\nAll checks pass." : $"\n{fails} check(s) failed.");
        return fails;
    }
}
