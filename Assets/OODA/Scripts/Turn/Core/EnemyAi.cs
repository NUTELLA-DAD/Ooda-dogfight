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
            var m = battle.planes[me];
            int them = Target(battle);
            if (them < 0) return TurnPlan.Straight(m);
            var sub = battle.Subset(Nearby(battle, them));   // me is 0 in it, the target 1
            if (ignoreObstacles) sub.obstacleHit = sub.wires == null ? null : (Func<Vec3d, Vec3d, bool>)sub.wires.Hit;   // the air hazards' plain shapes, not the colliders
            // only the flak that could reach it this turn
            var near = new List<FlakSite>();
            foreach (var g in sub.flak) if ((g.pos - m.state.pos).Length < g.rangeFt + 3000) near.Add(g);
            sub.flak = near.ToArray();
            // and only the ground and gunners' rounds already in the air that could reach it
            sub.bullets.RemoveAll(b => b.owner < 0 && (b.pos - m.state.pos).Length > 8000);
            var env = Envelope.Compute(m, turnSec);
            bool slow = m.Kias < 220;

            TurnPlan best = TurnPlan.Straight(m);
            double bestScore = double.NegativeInfinity;
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
                    double score = Score(sub, plan, turnSec) + sloppiness * rng.Gauss();
                    if (score > bestScore) { bestScore = score; best = plan; }
                }
            }
            PlanBurst(sub, ref best, turnSec);
            // a missile at the target whenever it might come into the seeker's cone this turn (it launches only once locked)
            if (battle.missiles[me] > 0 && battle.routes[me] == null)
            {
                Vec3d r = battle.planes[them].state.pos - m.state.pos;
                if (r.Length < Missile.MaxRangeFt * 1.2 && Vec3d.Dot(r.Normalized, m.Forward) > Math.Cos(60 * Math.PI / 180))
                { best.missile = true; best.missileTarget = them; }
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
                if (j != me && j != them && !battle.Out(j)) others.Add(((battle.planes[j].state.pos - at).Length, j));
            others.Sort((a, b) => a.r.CompareTo(b.r));
            for (int k = 0; k < others.Count && k < awareOf; k++) if (others[k].r < 9000) keep.Add(others[k].i);
            return keep.ToArray();
        }

        public int Target(Battle battle)
        {
            if (target >= 0 && !battle.Out(target)) return target;
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
                if (battle.side[j] == mySide || battle.Out(j)) continue;
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

        /// <summary>How good a plan is, flown in the small battle (me 0, target 1).</summary>
        double Score(Battle sub, TurnPlan plan, double turnSec)
        {
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
            // damage taken flying it (flak, rounds already in the air)
            score -= 2 * (sub.health[0] - sim.health[0]) / Math.Max(1, sub.combat[0].health) * 10;
            score += 0.5 * (mine.Kias - 250) / 200 + 0.2 * (mine.AltFt - sub.planes[0].AltFt) / 1000;
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

        /// <summary>Fire across the part of the turn whose rounds would hit the projected target, if any.</summary>
        void PlanBurst(Battle sub, ref TurnPlan plan, double turnSec)
        {
            if (sub.ammo[0] <= 0) return;
            var test = plan;
            test.burstStart = 0; test.burstEnd = turnSec;
            var sim = sub.Clone();
            sim.dispersion = false;
            for (int j = 0; j < sim.Count; j++) if (sim.side[j] != sim.side[0]) sim.health[j] = double.PositiveInfinity;   // keep targets flying to see the whole burst
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
}
