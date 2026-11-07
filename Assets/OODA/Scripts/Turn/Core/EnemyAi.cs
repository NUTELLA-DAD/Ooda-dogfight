using System;
using System.Collections.Generic;
using Ooda.Flight.Core;

namespace Ooda.Turn.Core
{
    /// <summary>
    /// An AI pilot (the enemy, or one of the player's wingmen). It does not see anyone's plan: it assumes the
    /// others keep doing what they are doing (the same projection the player can see), tries a spread of arrows
    /// across its own reach shell, flies each one against that projection, and keeps the one that leaves it best
    /// placed: nose on its target, behind it, at gun range, with speed and height in hand, out of everyone's guns
    /// and off the ground. Then it plans a burst where its rounds would hit. To stay quick with many aircraft it
    /// tries its plans in a small battle of itself, its target and the few aircraft nearest it.
    /// </summary>
    public class EnemyAi
    {
        public enum Role
        {
            /// <summary>Goes for the bombers first (aircraft flying routes), then anyone.</summary>
            Hunter,
            /// <summary>Goes for the nearest enemy fighter.</summary>
            Fighter,
            /// <summary>Goes for the enemy fighter nearest its own side's bombers.</summary>
            Escort,
        }

        public int me = 1;
        public Role role = Role.Hunter;
        /// <summary>Who to go for: an aircraft index, or -1 to choose by role.</summary>
        public int target = -1;
        /// <summary>0 plays the best it finds; more adds noise to its choice (a less sharp pilot).</summary>
        public double sloppiness = 0.15;
        public double idealRangeFt = 900;
        /// <summary>Below this over the ground (ft) it starts to worry; deep valleys need it low.</summary>
        public double safeClearFt = 600;
        /// <summary>How far ahead (s) past the end of the turn it checks its path is clear of the ground: room to turn away next turn.</summary>
        public double lookSec = 4;
        /// <summary>How many aircraft besides itself and its target it keeps in mind (the nearest).</summary>
        public int awareOf = 4;
        /// <summary>How near a projected pass counts as a firing chance, as a multiple of the real hit radius.</summary>
        public double burstSlack = 2.5;
        public Lcg rng = new Lcg(77);
        /// <summary>Leave bridges and buildings out of its tries (set when planning off the main thread, where they can't be asked about).</summary>
        public bool ignoreObstacles;

        public TurnPlan Plan(Battle battle, double turnSec)
        {
            var tries = Tries(battle, turnSec, out var sub, out int them);
            if (tries == null) return TurnPlan.Straight(battle.planes[me]);
            int pick = 0; double bestScore = double.NegativeInfinity;
            for (int k = 0; k < tries.Count; k++)
            {
                double score = tries[k].score + sloppiness * rng.Gauss();
                if (score > bestScore) { bestScore = score; pick = k; }
            }
            return Finish(battle, sub, them, tries[pick].plan, turnSec, tries);
        }

        /// <summary>
        /// What this pilot might do this turn, for the player to see: the tries its (a little random) choice is likely
        /// to land on, likeliest first, each with that chance and its burst planned. Plan made afterwards on the same
        /// battle picks from these same tries, so the chances are the real ones.
        /// </summary>
        public Prediction Predict(Battle battle, double turnSec, int most = 4, double least = 0.02, int always = 3)
        {
            var tries = Tries(battle, turnSec, out var sub, out int them);
            if (tries == null) return new Prediction { pilot = me, plans = new[] { TurnPlan.Straight(battle.planes[me]) }, chance = new[] { 1.0 } };
            // the chance each try wins once the noise is added, by drawing it many times (its own dice, not the pilot's)
            var wins = new int[tries.Count];
            var dice = new Lcg(9001u + (uint)battle.turn * 31u + (uint)me);
            const int draws = 600;
            for (int d = 0; d < draws; d++)
            {
                int pick = 0; double bestScore = double.NegativeInfinity;
                for (int k = 0; k < tries.Count; k++)
                {
                    double score = tries[k].score + sloppiness * dice.Gauss();
                    if (score > bestScore) { bestScore = score; pick = k; }
                }
                wins[pick]++;
            }
            // One ghost per kind of move (a climb, a dive, a hard turn each way...): that kind's likeliest try (its best
            // scored when none won), with the chance of any try of that kind, so the spread shows the different things it
            // might do, not one turn four ways. The first `always` kinds it found are shown however unlikely (their
            // chance is still the real one, faint and often under 1%); more only when at least `least` likely.
            var kinds = new Dictionary<MoveKind, (int best, int wins, List<Vec3d> aims)>();
            for (int k = 0; k < tries.Count; k++)
            {
                var kind = tries[k].kind;
                if (!kinds.TryGetValue(kind, out var g)) g = (k, 0, new List<Vec3d>());
                if (wins[k] > wins[g.best] || wins[k] == wins[g.best] && tries[k].score > tries[g.best].score) g.best = k;
                g.wins += wins[k]; g.aims.Add(tries[k].plan.aim);
                kinds[kind] = g;
            }
            var order = new List<(int best, int wins, List<Vec3d> aims, MoveKind kind)>();
            foreach (var kv in kinds) order.Add((kv.Value.best, kv.Value.wins, kv.Value.aims, kv.Key));
            order.Sort((a, b) => a.wins != b.wins ? b.wins.CompareTo(a.wins) : tries[b.best].score.CompareTo(tries[a.best].score));
            var plans = new List<TurnPlan>(); var chance = new List<double>(); var kindList = new List<MoveKind>(); var members = new List<Vec3d[]>();
            foreach (var o in order)
            {
                double c = (double)o.wins / draws;
                if (plans.Count >= most || plans.Count >= always && c < least) break;
                plans.Add(Finish(battle, sub, them, tries[o.best].plan, turnSec, tries));
                chance.Add(c); kindList.Add(o.kind); members.Add(o.aims.ToArray());
            }
            return new Prediction { pilot = me, plans = plans.ToArray(), chance = chance.ToArray(), kinds = kindList.ToArray(), members = members.ToArray() };
        }

        struct Try { public TurnPlan plan; public double score; public MoveKind kind; }

        /// <summary>Drop the tries kept from the last look (after the battle's aircraft have been renumbered, as by Battle.Swap).</summary>
        public void Forget() { lock (this) { cachedTries = null; cachedFor = cachedSub = null; } }

        // the last tries, kept so Plan and Predict on the same battle at the same moment score them only once
        List<Try> cachedTries; Battle cachedFor, cachedSub; double cachedTime = double.NaN, cachedTurnSec; int cachedThem;

        /// <summary>A spread of arrows across its reach shell, each scored (without the noise). Null when it has no one to go for.</summary>
        List<Try> Tries(Battle battle, double turnSec, out Battle sub, out int them)
        {
            lock (this)
            {
                if (cachedTries != null && ReferenceEquals(cachedFor, battle) && cachedTime == battle.time && cachedTurnSec == turnSec)
                { sub = cachedSub; them = cachedThem; return cachedTries; }
                var m = battle.planes[me];
                them = Target(battle);
                sub = null;
                if (them < 0) return null;
                var keep = Nearby(battle, them);
                sub = battle.Subset(keep);   // me is 0 in it, the target 1
                if (battle.Jammed(me)) Jam.Scramble(sub, 0, battle.turn, keep);   // jammed: it plans against where the others aren't
                if (ignoreObstacles) sub.obstacleHit = sub.wires == null ? null : (Func<Vec3d, Vec3d, bool>)sub.wires.Hit;   // the air hazards' plain shapes, not the colliders
                // only the flak that could reach it this turn
                var near = new List<FlakSite>();
                foreach (var g in sub.flak) if ((g.pos - m.state.pos).Length < g.rangeFt + 3000) near.Add(g);
                sub.flak = near.ToArray();
                // and only the ground and gunners' rounds already in the air that could reach it
                sub.bullets.RemoveAll(b => b.owner < 0 && (b.pos - m.state.pos).Length > 8000);
                // and none of its own side's (they cannot hit it, and its score is about what happens to it)
                int mySide = sub.side[0];
                sub.bullets.RemoveAll(b => b.side == mySide);
                // and no gun round too far away to reach it before it falls or the turn ends
                double myFps = m.state.vel.Length;
                sub.bullets.RemoveAll(b => !(b is Missile) && (b.pos - m.state.pos).Length > (b.vel.Length + myFps) * Math.Min(turnSec, Math.Max(0, b.lifeSec - b.age)) + 300);
                var env = Envelope.Compute(m, turnSec);
                bool slow = m.Kias < 220;
                turner = TurnerWeight(m, sub.planes[1]);

                var tries = new List<Try>();
                foreach (var fraction in new[] { 0.0, 0.35, 0.7, 1.0 })
                {
                    int count = fraction == 0 ? 1 : 12;
                    for (int k = 0; k < count; k++)
                    {
                        double phi = 2 * Math.PI * k / 12;
                        var plan = new TurnPlan
                        {
                            aim = fraction == 0 ? m.Forward : env.Direction(phi, env.MaxAngle(phi) * fraction),
                            throttle = 1, wep = slow,
                        };
                        double score = Score(sub, plan, turnSec, out Vec3d endVel);
                        tries.Add(new Try { plan = plan, score = score, kind = Kind(m.state.vel, endVel) });
                    }
                }
                cachedTries = tries; cachedFor = battle; cachedSub = sub; cachedTime = battle.time; cachedTurnSec = turnSec; cachedThem = them;
                return tries;
            }
        }

        /// <summary>The chosen arrow with its burst, and a missile when the target might come into the seeker's cone.</summary>
        TurnPlan Finish(Battle battle, Battle sub, int them, TurnPlan best, double turnSec, List<Try> tries)
        {
            var m = battle.planes[me];
            if (Blade(sub, tries, turnSec, out var cut)) return cut;   // a pass that cuts the target in half: nothing else this turn
            Extras(sub, ref best, turnSec);
            PlanBurst(sub, ref best, turnSec);
            Defend(sub, ref best);
            // a missile at the target whenever it might come into the seeker's cone this turn (it launches only once locked)
            if (battle.missiles[me] > 0 && battle.routes[me] == null)
            {
                Vec3d r = battle.planes[them].state.pos - m.state.pos;
                if (!best.cloak && !battle.combat[them].iced && r.Length < Heat.LockRangeFt(battle.HeatOf(them, m.state.pos)) * 1.2 && Vec3d.Dot(r.Normalized, m.Forward) > Math.Cos(60 * Math.PI / 180))
                { best.missile = true; best.missileTarget = them; }
            }
            // the radar on the target, and a radar missile whenever it is in the cone (it launches only once the lock is a turn old)
            if (battle.combat[me].radarRangeFt > 0)
            {
                best.radarTarget = them;
                if (battle.radarMissiles[me] > 0 && battle.routes[me] == null && !best.cloak && battle.InRadarCone(me, them)) best.radarMissile = true;
            }
            return best;
        }

        /// <summary>Me, my target, then the nearest others that are still flying.</summary>
        int[] Nearby(Battle battle, int them)
        {
            var keep = new List<int> { me, them };
            var others = new List<(double r, int i)>();
            Vec3d at = battle.planes[me].state.pos;
            for (int j = 0; j < battle.Count; j++)
                if (j != me && j != them && !battle.Out(j) && !(battle.side[j] != battle.side[me] && battle.Cloaked(j))) others.Add(((battle.planes[j].state.pos - at).Length, j));
            others.Sort((a, b) => a.r.CompareTo(b.r));
            for (int k = 0; k < others.Count && k < awareOf; k++) if (others[k].r < 9000) keep.Add(others[k].i);
            return keep.ToArray();
        }

        public int Target(Battle battle)
        {
            if (target >= 0 && !battle.Out(target) && !battle.Cloaked(target)) return target;
            int mySide = battle.side[me];
            Vec3d at = battle.planes[me].state.pos;
            // where my own side's bombers are, for an escort
            Vec3d guard = at; int nb = 0; Vec3d sum = Vec3d.Zero;
            for (int j = 0; j < battle.Count; j++)
                if (battle.side[j] == mySide && battle.routes[j] != null && !battle.Out(j)) { sum = sum + battle.planes[j].state.pos; nb++; }
            if (nb > 0) guard = sum * (1.0 / nb);

            int best = -1; double bestCost = double.PositiveInfinity;
            for (int j = 0; j < battle.Count; j++)
            {
                if (battle.side[j] == mySide || battle.Out(j) || battle.Cloaked(j)) continue;   // a stealthy one isn't there, as far as it knows
                bool bomber = battle.routes[j] != null;
                double cost = (battle.planes[j].state.pos - at).Length;
                switch (role)
                {
                    case Role.Hunter: if (bomber) cost -= 1e6; break;
                    case Role.Fighter: if (bomber) cost += 1e6; break;
                    case Role.Escort: cost = (battle.planes[j].state.pos - guard).Length + 0.3 * cost + (bomber ? 1e6 : 0); break;
                }
                if (cost < bestCost) { bestCost = cost; best = j; }
            }
            return best;
        }

        /// <summary>
        /// How much this pilot wants a turning fight (1) rather than an up-and-down one (0), from its own aircraft against
        /// its target's at this height: the better it holds a turn against the other, the more it turns; out-turned, it
        /// goes vertical, the more so the better it climbs. Set each look, before the tries are scored.
        /// </summary>
        double turner = 0.5;

        public static double TurnerWeight(FlightModel mine, FlightModel theirs)
        {
            double alt = 0.5 * (mine.AltFt + theirs.AltFt);
            double turn = Math.Log(Math.Max(1, mine.numbers.SustainedTurnDps(alt, true)) / Math.Max(1, theirs.numbers.SustainedTurnDps(alt, true)));
            double climb = Math.Log(Math.Max(100, mine.numbers.ClimbFpm(alt, true)) / Math.Max(100, theirs.numbers.ClimbFpm(alt, true)));
            // the turn decides it (out-turned, it has to go vertical); a climb edge only leans it further up
            return Math.Max(0, Math.Min(1, 0.5 + 4 * turn - climb));
        }

        /// <summary>What kind of move a flown try was, from the flight path at its start and end: for the spread of ghosts.</summary>
        public static MoveKind Kind(Vec3d v0, Vec3d v1)
        {
            const double D = 180 / Math.PI;
            double path0 = Math.Asin(Math.Max(-1, Math.Min(1, v0.z / Math.Max(1e-6, v0.Length)))) * D;
            double path1 = Math.Asin(Math.Max(-1, Math.Min(1, v1.z / Math.Max(1e-6, v1.Length)))) * D;
            double turn = (Math.Atan2(v0.x, v0.y) - Math.Atan2(v1.x, v1.y)) * D;     // + to the left (heading measured clockwise from north)
            while (turn > 180) turn -= 360;
            while (turn < -180) turn += 360;
            if (path1 - path0 > 25 && path1 > 15) return MoveKind.Climb;
            if (path1 - path0 < -25 && path1 < -15) return MoveKind.Dive;
            if (Math.Abs(turn) >= 60) return turn > 0 ? MoveKind.HardLeft : MoveKind.HardRight;
            if (Math.Abs(turn) >= 20) return turn > 0 ? MoveKind.Left : MoveKind.Right;
            return MoveKind.Straight;
        }

        double Score(Battle sub, TurnPlan plan, double turnSec) => Score(sub, plan, turnSec, out _);

        /// <summary>How good a plan is, flown in the small battle (me 0, target 1).</summary>
        double Score(Battle sub, TurnPlan plan, double turnSec, out Vec3d endVel)
        {
            endVel = sub.planes[0].state.vel;
            var sim = sub.Clone();
            sim.dispersion = false;
            double minClear = double.PositiveInfinity, shotBonus = 0;
            sim.Resolve(new TurnPlan?[] { plan }, turnSec, false, (b, k) =>
            {
                var a = b.planes[0];
                minClear = Math.Min(minClear, a.AltFt - b.groundFt(a.state.pos.x, a.state.pos.y));
                if (k % 5 == 0)
                {
                    Vec3d los = b.planes[1].state.pos - a.state.pos;
                    double r = los.Length;
                    if (r < 2500 && Envelope.Angle(Battle.GunLine(a), los) < 4 * Math.PI / 180) shotBonus = 1;
                }
            });
            endVel = sim.planes[0].state.vel;
            if (sim.crashed[0] || sim.down[0]) return -100;

            var mine = sim.planes[0];
            var theirs = sim.planes[1];
            Vec3d l = theirs.state.pos - mine.state.pos;
            double range = l.Length;
            double ata = Envelope.Angle(mine.Forward, l);          // my nose off the line to them
            double aa = Envelope.Angle(theirs.Forward, l);         // 0 when I am at their six

            double score = -2.0 * ata - 1.2 * aa;
            score -= Math.Abs(range - idealRangeFt) / 2500;
            // anyone on the other side with guns pointing at me
            for (int j = 0; j < sim.Count; j++)
            {
                if (sim.side[j] == sim.side[0] || sim.Out(j) || sim.combat[j].ammoSec <= 0) continue;
                Vec3d lj = sim.planes[j].state.pos - mine.state.pos;
                double rj = lj.Length, theirAta = Envelope.Angle(sim.planes[j].Forward, -lj);   // their nose off the line to me
                if (theirAta < 20 * Math.PI / 180 && rj < 4000) score -= 1.5 * (1 - theirAta / (20 * Math.PI / 180));
            }
            // a radar missile still guiding on it at the end of the turn: notch it (or chaff it) before it arrives
            foreach (var b in sim.bullets) if (b is Missile rm && rm.radar && rm.target == 0) score -= 6;
            // harm to its systems flying it (flak, rounds already in the air): a whole system knocked out costs 10
            score -= 10 * (sim.SystemsOf(0).Total - sub.SystemsOf(0).Total);
            // blacking out (G-LOC) leaves it flying blind for seconds: a pilot avoids pulling that hard for that long
            if (sim.planes[0].glocs > sub.planes[0].glocs) score -= 4;
            score -= 0.5 * sim.planes[0].GreyOut;
            // Energy, by its own aircraft: below corner speed it turns slower and wider, so that always costs. A turner
            // wants to sit at corner speed (faster only widens its turn); an up-and-down fighter wants energy in hand,
            // speed and height together, to climb away and dive back in.
            var start = sub.planes[0];
            double corner = mine.numbers.CornerKias;
            score -= 0.75 * Math.Max(0, corner - mine.Kias) / 200;
            score -= turner * 0.15 * Math.Max(0, mine.Kias - corner) / 200;
            double vEnd = mine.state.vel.Length, vStart = start.state.vel.Length;
            double energyGain = (mine.AltFt - start.AltFt + (vEnd * vEnd - vStart * vStart) / (2 * Atmosphere.GFtps2)) / 1000;
            score += (1 - turner) * 0.4 * energyGain + turner * 0.2 * (mine.AltFt - start.AltFt) / 1000;
            if (minClear < safeClearFt) score -= (safeClearFt - minClear) / 200;
            // where it is heading after the turn: straight on for lookSec, is the ground (a valley wall) in the way?
            double ahead = double.PositiveInfinity;
            Vec3d p0 = mine.state.pos, v0 = mine.state.vel;
            for (int s = 1; s <= 8; s++)
            {
                double t = lookSec * s / 8;
                Vec3d q = p0 + v0 * t;
                double c = q.z - sim.groundFt(q.x, q.y);
                if (c < 0) { score -= 40 * (1 - (double)(s - 1) / 8); ahead = 0; break; }
                ahead = Math.Min(ahead, c);
            }
            if (ahead < safeClearFt) score -= (safeClearFt - ahead) / 150;
            if (mine.Departed) score -= 3;
            return score + shotBonus;
        }

        /// <summary>How much better (in score) the rocket must leave it before it spends it, and the same for the speed brake.</summary>
        public double rocketMargin = 0.25, brakeMargin = 0.15;

        /// <summary>
        /// The rocket and the speed brake: the chosen arrow flown again with the rocket lit (if it is ready), and with the
        /// throttle chopped and the brake out (to make an attacker overshoot, or not to overshoot its own target); either
        /// kept when it leaves it clearly better placed. Slow and with the rocket ready, it lights it unless that is worse.
        /// </summary>
        void Extras(Battle sub, ref TurnPlan plan, double turnSec)
        {
            var m = sub.planes[0];
            if (m.Departed || sub.routes[0] != null) return;
            double baseScore = Score(sub, plan, turnSec);
            if (m.RocketReady)
            {
                var r = plan; r.rocket = true;
                double s = Score(sub, r, turnSec);
                if (s > baseScore + (m.Kias < 200 ? 0 : rocketMargin)) { plan = r; baseScore = s; }
            }
            if (sub.planes[0].numbers.brakeG > 0 && !plan.rocket)
            {
                var b = plan; b.brake = true; b.throttle = 0; b.wep = false;
                double s = Score(sub, b, turnSec);
                if (s > baseScore + brakeMargin) plan = b;
            }
        }

        /// <summary>
        /// The heat blade: with a charge and the target close, each try flown again red hot; if one cuts the target in half
        /// (against its projection) and leaves this pilot flying, it takes the best scored of those. Guns cold that turn.
        /// </summary>
        bool Blade(Battle sub, List<Try> tries, double turnSec, out TurnPlan cut)
        {
            cut = default;
            if (tries == null || sub.heatBlades[0] <= 0 || sub.routes[0] != null || sub.planes[0].Departed) return false;
            if ((sub.planes[1].state.pos - sub.planes[0].state.pos).Length > 4500) return false;
            double bestScore = double.NegativeInfinity; bool found = false;
            foreach (var t in tries)
            {
                var p = t.plan; p.ram = true;
                var sim = sub.Clone();
                sim.dispersion = false;
                sim.Resolve(new TurnPlan?[] { p }, turnSec, false);
                if (!sim.down[1] || sub.down[1] || sim.down[0] || sim.crashed[0]) continue;
                if (t.score > bestScore) { bestScore = t.score; cut = p; found = true; }
            }
            return found;
        }

        /// <summary>
        /// A missile on it: a gravity well thrown ahead. Someone on its tail with guns on it: a balloon mine let go in their path;
        /// with none left, a gravity flip (if they are close enough to catch in it); then a gravity well; then the jammer; then (hurt) stealth, unless it is firing this turn (that would show it
        /// again at once).
        /// </summary>
        void Defend(Battle sub, ref TurnPlan plan)
        {
            if (sub.routes[0] != null) return;
            var me0 = sub.planes[0];
            // a missile closing on it: a gravity well thrown ahead, to fly through and drag the missile in after
            if (sub.wells[0] > 0)
                foreach (var b in sub.bullets)
                    if (b is Missile ms && ms.target == 0 && ms.Tracking && (ms.pos - me0.state.pos).Length < 7000) { plan.well = true; plan.wellAt = 0; return; }
            bool tailed = false;
            double tailFt = double.PositiveInfinity;
            for (int j = 0; j < sub.Count; j++)
            {
                if (sub.side[j] == sub.side[0] || sub.Out(j)) continue;
                Vec3d l = me0.state.pos - sub.planes[j].state.pos;   // from them to me
                double r = l.Length;
                if (r > 3000 || r < 1) continue;
                bool theirNoseOnMe = Envelope.Angle(sub.planes[j].Forward, l) < 25 * Math.PI / 180;
                bool theyreBehind = Envelope.Angle(me0.Forward, l) < 60 * Math.PI / 180;
                if (theirNoseOnMe && theyreBehind) { tailed = true; tailFt = Math.Min(tailFt, r); }
            }
            if (!tailed) return;
            if (sub.balloonMines[0] > 0) { plan.mine = true; return; }
            // close enough to catch in the bubble: flip gravity on them, and they float up off its tail
            if (sub.flips[0] > 0 && tailFt < Specials.FlipCoreFt) { plan.flip = true; return; }
            if (sub.wells[0] > 0) { plan.well = true; plan.wellAt = 0; return; }   // the tailer's rounds bend off, and it turns tighter
            if (sub.jammers[0] > 0) { plan.jam = true; return; }
            bool hurt = sub.SystemsOf(0).AnyDamaged;
            if (sub.stealths[0] > 0 && hurt && plan.burstEnd <= plan.burstStart && !plan.missile) plan.cloak = true;
        }

        /// <summary>Fire across the part of the turn whose rounds would hit the projected target, if any.</summary>
        void PlanBurst(Battle sub, ref TurnPlan plan, double turnSec)
        {
            if (sub.ammo[0] <= 0) return;
            var test = plan;
            test.burstStart = 0; test.burstEnd = turnSec;
            var sim = sub.Clone();
            sim.dispersion = false;
            for (int j = 0; j < sim.Count; j++) if (sim.side[j] != sim.side[0]) sim.Spare(j);   // keep targets flying to see the whole burst
            // The target will not fly exactly as projected: fire at anything that comes near (a wider "hit" here)
            sim.combat = (CombatNumbers[])sim.combat.Clone();
            for (int j = 0; j < sim.Count; j++) sim.combat[j] = sim.combat[j].Copy();
            sim.combat[0].hitRadiusFt *= burstSlack;
            // rounds already in the air are not this burst's
            sim.bullets.Clear();
            var r = sim.Resolve(new TurnPlan?[] { test }, turnSec, false);
            double first = double.PositiveInfinity, last = double.NegativeInfinity;
            foreach (var h in r.hits)
            {
                if (h.shooter != 0) continue;
                double t = h.fireTime - sub.time;
                first = Math.Min(first, t); last = Math.Max(last, t);
            }
            if (last < first) return;
            plan.burstStart = Math.Max(0, first - 0.15);
            plan.burstEnd = Math.Min(turnSec, last + 0.15);
        }
    }

    /// <summary>The kinds of move the ghosts are spread across.</summary>
    public enum MoveKind { Straight, Left, Right, HardLeft, HardRight, Climb, Dive }

    /// <summary>
    /// What an AI pilot might do this turn: one plan per kind of move, likeliest first, each with the chance it flies
    /// any move of that kind (members holds the aims of every try of that kind).
    /// </summary>
    public class Prediction
    {
        public int pilot;
        public TurnPlan[] plans;
        public double[] chance;
        public MoveKind[] kinds;
        public Vec3d[][] members;

        /// <summary>A chance as the ghosts' label: a whole percent, or "<1%" for a kind it almost never picks.</summary>
        public static string Percent(double c) => c < 0.005 ? "<1%" : $"{c * 100:F0}%";

        /// <summary>Which ghost a flown plan belongs to (its kind), or -1 when it was none of them.</summary>
        public int Rank(TurnPlan flown)
        {
            for (int k = 0; k < plans.Length; k++)
            {
                if (Vec3d.Dot(flown.aim, plans[k].aim) > 0.99999) return k;
                if (members != null && k < members.Length)
                    foreach (var a in members[k]) if (Vec3d.Dot(flown.aim, a) > 0.99999) return k;
            }
            return -1;
        }
    }
}
