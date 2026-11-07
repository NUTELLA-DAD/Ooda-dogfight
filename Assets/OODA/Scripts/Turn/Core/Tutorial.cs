using System;
using Ooda.Flight.Core;

namespace Ooda.Turn.Core
{
    /// <summary>
    /// Training: a dogfight school of short lessons, each a one on one (your P-51 against a Bf 109 over the open farmland)
    /// with a coach that sets you one small goal at a time and ticks it off when you do it. The early lessons fly against a
    /// target drone with no guns that holds its course; then an unarmed Bf 109 that manoeuvres, to learn to get on its six;
    /// then one that shoots back (softly); and last a real fight. The lessons, how each fight is set up, and the checks
    /// for each goal live here, in plain C# (Tools~/TutorialCheck runs them); TurnBattle.Tutorial.cs is the coach on screen.
    /// </summary>
    public static class Tutorial
    {
        /// <summary>What the other plane is in a lesson.</summary>
        public enum Foe
        {
            /// <summary>A target drone: no guns, no pilot. It holds its course, so the preview shows exactly where it goes.</summary>
            Drone,
            /// <summary>A real AI pilot that manoeuvres, but with empty guns and no missiles.</summary>
            Unarmed,
            /// <summary>A real AI pilot that shoots back, its rounds scaled by the lesson's damage.</summary>
            Armed,
        }

        /// <summary>The goal a step sets. Each is checked by <see cref="Met"/>.</summary>
        public enum Goal
        {
            /// <summary>Move the stick off straight ahead (or use the timeline).</summary>
            Steer,
            /// <summary>Fly the turn (or <see cref="Step.turns"/> turns).</summary>
            FlyTurns,
            /// <summary>Pull into the shaker (70% of the hardest clean turn or more) without going into the overpull band.</summary>
            PullHard,
            /// <summary>Fly a turn (or <see cref="Step.turns"/> turns) planned with the stick in the shaker and out of the red.</summary>
            FlyHardTurn,
            /// <summary>Full throttle and WEP.</summary>
            FullPower,
            /// <summary>Behind the enemy: within <see cref="SixRangeFt"/>, it within <see cref="SixNoseDeg"/> of your nose, you within <see cref="SixTailDeg"/> of its tail.</summary>
            OnSix,
            /// <summary>A burst planned that the preview says hits.</summary>
            BurstOnTarget,
            /// <summary>Put rounds into it.</summary>
            HitTarget,
            /// <summary>Shoot it down.</summary>
            KillTarget,
            /// <summary>Plan a way out: a hard pull, the speed brake, the rocket or a balloon mine.</summary>
            Escape,
            /// <summary>Still flying after <see cref="Step.turns"/> turns.</summary>
            Survive,
        }

        public const double SixRangeFt = 3000, SixNoseDeg = 35, SixTailDeg = 60;
        /// <summary>How far the stick must go toward the hardest clean turn to count as pulling hard: where the shaker starts.</summary>
        public const double HardPull = 0.7;
        /// <summary>How far off straight ahead the nose must be planned to count as steering, degrees.</summary>
        public const double SteerDeg = 8;
        /// <summary>The arena every lesson flies over: Town & Farmland, the most room to turn.</summary>
        public const int ArenaIndex = 3;

        public class Step
        {
            public Goal goal;
            /// <summary>For FlyTurns, FlyHardTurn and Survive: how many turns.</summary>
            public int turns = 1;
            /// <summary>What the coach says.</summary>
            public string text;
            /// <summary>The short line in the goal list.</summary>
            public string todo;
        }

        public class Lesson
        {
            public string title, intro, done;
            public Scenarios.Start start = Scenarios.Start.Neutral;
            public DogfightSetup.Range range = DogfightSetup.Range.Medium;
            public Foe foe = Foe.Drone;
            /// <summary>For an AI foe: how sloppy it plans (EnemyAi.sloppiness; the dogfight's is 0.15).</summary>
            public double sloppiness = 0.5;
            /// <summary>For an armed foe: its rounds' damage, as a share of the real thing.</summary>
            public double damageScale = 1;
            /// <summary>The foe's toughness (0: its usual; see CombatNumbers.toughness). The drone is flimsy so the guns lesson doesn't drag.</summary>
            public double foeToughness;
            /// <summary>Where your throttle starts (the dogfight starts at full).</summary>
            public double throttle = 1;
            /// <summary>The foe's throttle (held all fight for the drone; where the AI starts): below full it is slower, and easier to catch.</summary>
            public double foeThrottle = 1;
            /// <summary>The arcade specials (balloon mine, heat blade, stealth) on for you. The foe never gets them here.</summary>
            public bool arcade;
            public Step[] steps;
        }

        static Step S(Goal g, string todo, string text, int turns = 1) => new Step { goal = g, todo = todo, text = text, turns = turns };

        public static readonly Lesson[] All =
        {
            new Lesson
            {
                title = "Plan a turn",
                intro = "Every turn lasts 3 seconds. You plan where to fly, the enemy plans at the same time without seeing yours, then both of you fly it.",
                foe = Foe.Drone,
                steps = new[]
                {
                    S(Goal.Steer, "Steer with the stick",
                      "Drag the stick in the bottom-right corner, or use WASD. Pull it down to lift the nose, push it sideways to roll and turn. The blue path is where you will fly; the blue ghost plane is where you end up."),
                    S(Goal.FlyTurns, "Fly the turn",
                      "Happy with the blue path? Press Fly turn (Enter). The turn plays back, then you plan the next one."),
                },
                done = "That's the whole loop: look, decide, fly. Observe, Orient, Decide, Act: OODA.",
            },
            new Lesson
            {
                title = "Turn hard, not too hard",
                intro = "The harder you pull, the tighter you turn, up to a point.",
                foe = Foe.Drone,
                steps = new[]
                {
                    S(Goal.PullHard, "Pull until the stick shakes",
                      "Drag the stick further down (or hold S) until it starts to shake. The shaking means you are close to the hardest turn the wing can give at this speed."),
                    S(Goal.FlyHardTurn, "Fly a turn shaking, not in the red",
                      "Past the shaking the stick goes into the red: overpull. The wing stalls, the turn goes mushy, and held too long you spin. Fly a turn with the stick shaking but out of the red."),
                },
                done = "Right at the shaker is your tightest turn. Into the red only loses you speed, and maybe the plane.",
            },
            new Lesson
            {
                title = "Speed is life",
                intro = "Hard turns bleed speed, and a slow fighter can't turn or climb. Your throttle is back at 60%.",
                foe = Foe.Drone,
                throttle = 0.6,
                steps = new[]
                {
                    S(Goal.FullPower, "Full throttle: WEP",
                      "Push the throttle to the stop (drag the lever up, hold Shift, or Space): the last notch is WEP, war emergency power, for when you need every horse."),
                    S(Goal.FlyHardTurn, "Fly two hard turns, watch your speed",
                      "Fly two hard turns (stick shaking, out of the red) and watch your speed, the KIAS at the top left. Every fighter turns best near its corner speed, about 300 KIAS for the P-51. Much slower and it turns slowly and mushes.", 2),
                },
                done = "Keep your speed up and you keep your choices. Climbing stores speed as height; diving cashes it back in.",
            },
            new Lesson
            {
                title = "Get on its six",
                intro = "A Bf 109 flies beside you. Its guns are empty for this lesson, but it flies like it means it.",
                foe = Foe.Unarmed,
                sloppiness = 0.6,
                foeThrottle = 0.75,
                steps = new[]
                {
                    S(Goal.FlyTurns, "Read its ghosts, turn toward it",
                      "The red ghosts show where it may go this turn, the bright one likeliest, each with its chance. Turn toward it and fly a turn."),
                    S(Goal.OnSix, "Get behind it",
                      "Now get on its six: behind it, its tail toward you, within 3,000 ft and within 35° of your nose. Plan for where it will be, not where it is. If it out-turns you, pull up and come down on it."),
                },
                done = "On its six it can't shoot you, and you can shoot it. That's the whole game.",
            },
            new Lesson
            {
                title = "Guns: lead the target",
                intro = "You are behind a target drone, throttled back so you can catch it. Close in and shoot.",
                foe = Foe.Drone,
                start = Scenarios.Start.Offensive,
                range = DogfightSetup.Range.Close,
                foeToughness = 5,
                foeThrottle = 0.3,
                steps = new[]
                {
                    S(Goal.BurstOnTarget, "Plan a burst that hits",
                      "Rounds take time to get there, so aim ahead of the target. The red band on the gun bar is when rounds fired would hit: drag across it, or press F to fire where hits are predicted. The line under the bar turns to red HITS when you have it."),
                    S(Goal.HitTarget, "Fly it, hit it",
                      "Fly the turn and watch your tracers. Red ones hit, yellow ones just miss."),
                    S(Goal.KillTarget, "Shoot it down",
                      "Keep at it: steer to keep it just ahead of your nose, plan a burst that hits, fly. Short bursts: your ammunition runs out."),
                },
                done = "Target down. Lead it, get close, fire when the hits are there.",
            },
            new Lesson
            {
                title = "Defend: the break turn",
                intro = "A Bf 109 is on your tail, and this one shoots back (its rounds are softened for the lesson).",
                foe = Foe.Armed,
                start = Scenarios.Start.Defensive,
                sloppiness = 0.35,
                damageScale = 0.35,
                arcade = true,
                steps = new[]
                {
                    S(Goal.Escape, "Plan a way out",
                      "Break: roll toward it and pull hard, stick shaking, so it can't keep its nose on you and overshoots. Or use an extra: 2 speed brake makes it fly past, 1 rocket runs, 4 drops a balloon mine in its path."),
                    S(Goal.Survive, "Survive four turns",
                      "Survive four turns. Keep turning hard toward it: a target crossing fast in front is hard to hit. Its red tracers show where it is shooting.", 4),
                },
                done = "You made it. Turning hard toward the attacker beats running away.",
            },
            new Lesson
            {
                title = "Final: one on one",
                intro = "Everything together: a Bf 109 that fights for real is coming straight at you (its guns and airframe a little softened).",
                foe = Foe.Armed,
                start = Scenarios.Start.HeadOn,
                sloppiness = 0.3,
                damageScale = 0.6,
                foeToughness = 8,
                arcade = true,
                steps = new[]
                {
                    S(Goal.KillTarget, "Shoot it down",
                      "Don't trade shots head-on for long: pass it, turn hard (stick shaking), keep your speed, get on its six, and fire when the hits are there."),
                },
                done = "Training complete. You're ready for a real dogfight.",
            },
        };

        public static int Count => All.Length;

        public static Lesson Get(int i) => All[Math.Max(0, Math.Min(All.Length - 1, i))];

        /// <summary>The dogfight a lesson flies: your P-51 against a Bf 109 over the farmland at medium height and 300 KIAS.</summary>
        public static DogfightSetup Setup(Lesson l) => new DogfightSetup
        {
            ours = new[] { Craft.Mustang }, theirs = new[] { Craft.Bf109 },
            randomStart = false, start = l.start, range = l.range,
            height = DogfightSetup.Height.Medium, speed = DogfightSetup.Pace.Cruise,
            randomArena = false, arena = ArenaIndex,
            easyAimOurs = true, arcade = l.arcade,
        };

        /// <summary>
        /// Fit a freshly built lesson battle to the lesson: the foe's guns (emptied, or softened), its airframe, no specials
        /// or missiles for it, and your throttle. Each changed CombatNumbers is a copy.
        /// </summary>
        public static void Arm(Battle b, Lesson l)
        {
            for (int i = 0; i < b.Count; i++)
            {
                if (b.side[i] == b.side[0]) continue;
                var c = b.combat[i].Copy();
                if (l.foe != Foe.Armed) c.ammoSec = 0;
                c.damage *= l.damageScale;
                if (l.foeToughness > 0) c.toughness = l.foeToughness;
                c.missiles = 0; c.radarMissiles = 0;
                c.balloonMines = c.heatBlades = c.stealths = c.wells = c.slowBombs = c.blinks = c.mirrors = c.grapples = c.snowClouds = c.lanes = 0;
                b.combat[i] = c;
                b.ammo[i] = c.ammoSec;
                b.missiles[i] = 0; b.radarMissiles[i] = 0;
                b.balloonMines[i] = b.heatBlades[i] = b.stealths[i] = b.wells[i] = b.slowBombs[i] = b.blinks[i] = b.mirrors[i] = b.grapples[i] = b.snowClouds[i] = b.lanes[i] = 0;
            }
            b.planes[0].throttle = l.throttle;
            b.planes[1].throttle = l.foeThrottle;
        }

        /// <summary>The foe's AI pilot for a lesson, or null for the drone (no pilot: it holds its course).</summary>
        public static EnemyAi Pilot(Lesson l, EnemyAi usual)
        {
            if (l.foe == Foe.Drone) return null;
            if (usual != null) usual.sloppiness = l.sloppiness;
            return usual;
        }

        /// <summary>What the coach looks at to tick a goal off.</summary>
        public struct Look
        {
            public Battle battle;
            /// <summary>The plan in hand (only read while planning).</summary>
            public TurnPlan plan;
            /// <summary>The reach of the plane you are in this turn (only read while planning).</summary>
            public Envelope env;
            public bool planning;
            /// <summary>Rounds of yours the preview puts into the foe with the planned burst.</summary>
            public int burstHits;
            /// <summary>Turns flown since this step began.</summary>
            public int turns;
            /// <summary>Turns flown since this step began that were planned with a hard pull out of the red.</summary>
            public int hardTurns;
            /// <summary>The harm to the foe's systems when this step began (Systems.Total).</summary>
            public double foeWearAtStep;
            /// <summary>The foe (the plane the coach means), usually 1.</summary>
            public int foe;
        }

        /// <summary>How far the plan pulls toward the hardest clean turn in its direction: 0 straight ahead, 1 at the ring.</summary>
        public static double Pull(TurnPlan p, Envelope env)
        {
            if (env == null || p.HasMarks) return 0;
            env.Polar(p.aim, out double phi, out double theta);
            return theta / Math.Max(1e-6, env.MaxAngle(phi));
        }

        /// <summary>A hard pull, in the shaker but not into the overpull band.</summary>
        public static bool PullsHard(TurnPlan p, Envelope env) => p.overpull <= 0 && Pull(p, env) >= HardPull;

        /// <summary>Behind the foe: in range, it near your nose, you near its tail.</summary>
        public static bool OnSix(Battle b, int me, int foe, out double rangeFt, out double noseDeg, out double tailDeg)
        {
            var m = b.planes[me]; var t = b.planes[foe];
            Vec3d los = t.state.pos - m.state.pos;
            rangeFt = los.Length;
            noseDeg = Envelope.Angle(m.Forward, los) * 180 / Math.PI;
            tailDeg = Envelope.Angle(t.Forward, los) * 180 / Math.PI;
            return !b.Out(me) && !b.Out(foe) && rangeFt <= SixRangeFt && noseDeg <= SixNoseDeg && tailDeg <= SixTailDeg;
        }

        /// <summary>Has the step's goal been met?</summary>
        public static bool Met(Step s, Look k)
        {
            var b = k.battle;
            bool alive = !b.Out(0);
            switch (s.goal)
            {
                case Goal.Steer:
                    if (!k.planning) return false;
                    if (k.plan.HasMarks) return true;
                    return k.env != null && Envelope.Angle(k.plan.aim, k.env.fwd) * 180 / Math.PI >= SteerDeg;
                case Goal.FlyTurns: return k.turns >= s.turns;
                case Goal.PullHard: return k.planning && PullsHard(k.plan, k.env);
                case Goal.FlyHardTurn: return k.hardTurns >= s.turns;
                case Goal.FullPower: return k.planning && k.plan.throttle >= 0.99 && k.plan.wep;
                case Goal.OnSix: return k.planning && OnSix(b, 0, k.foe, out _, out _, out _);
                case Goal.BurstOnTarget: return k.planning && k.plan.burstEnd > k.plan.burstStart && k.burstHits > 0;
                case Goal.HitTarget: return b.SystemsOf(k.foe).Total > k.foeWearAtStep + 1e-9 || b.Out(k.foe);
                case Goal.KillTarget: return alive && b.Out(k.foe);
                case Goal.Escape: return k.planning && (PullsHard(k.plan, k.env) || k.plan.brake || k.plan.rocket || k.plan.mine);
                case Goal.Survive: return alive && k.turns >= s.turns;
            }
            return false;
        }
    }
}
