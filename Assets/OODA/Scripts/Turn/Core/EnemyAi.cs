using System;
using Ooda.Flight.Core;

namespace Ooda.Turn.Core
{
    /// <summary>
    /// The enemy pilot. It does not see the player's plan: it assumes the player keeps doing what they are
    /// doing (the same projection the player can see), tries a spread of arrows across its own reach shell,
    /// flies each one against that projection, and keeps the one that leaves it best placed: nose on the
    /// player, behind them, at gun range, with speed and height in hand, out of the player's guns and off
    /// the ground. Then it plans a burst where its rounds would hit.
    /// </summary>
    public class EnemyAi
    {
        public int me = 1;
        /// <summary>Who to go for: an aircraft index, or -1 to choose (one flying a route, such as the bomber, first).</summary>
        public int target = -1;
        /// <summary>0 plays the best it finds; more adds noise to its choice (a less sharp pilot).</summary>
        public double sloppiness = 0.15;
        public double idealRangeFt = 900;
        /// <summary>How near a projected pass counts as a firing chance, as a multiple of the real hit radius.</summary>
        public double burstSlack = 2.5;
        public Lcg rng = new Lcg(77);

        public TurnPlan Plan(Battle battle, double turnSec)
        {
            int them = Target(battle);
            var m = battle.planes[me];
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
                    double score = Score(battle, plan, them, turnSec) + sloppiness * rng.Gauss();
                    if (score > bestScore) { bestScore = score; best = plan; }
                }
            }
            PlanBurst(battle, ref best, turnSec);
            return best;
        }

        TurnPlan TheirProjection() => TurnPlan.Holding();

        public int Target(Battle battle)
        {
            if (target >= 0 && !battle.Out(target)) return target;
            int best = -1; double bestRange = double.PositiveInfinity;
            for (int j = 0; j < battle.Count; j++)
            {
                if (battle.side[j] == battle.side[me] || battle.Out(j)) continue;
                double r = (battle.planes[j].state.pos - battle.planes[me].state.pos).Length - (battle.routes[j] != null ? 1e9 : 0);
                if (r < bestRange) { bestRange = r; best = j; }
            }
            return best >= 0 ? best : 1 - me;
        }

        double Score(Battle battle, TurnPlan plan, int them, double turnSec)
        {
            var sim = battle.Clone();
            sim.dispersion = false;
            double minClear = double.PositiveInfinity, shotBonus = 0;
            var p0 = me == 0 ? plan : TheirProjection();
            var p1 = me == 0 ? TheirProjection() : plan;
            sim.Resolve(p0, p1, turnSec, false, (b, k) =>
            {
                var a = b.planes[me];
                minClear = Math.Min(minClear, a.AltFt - b.groundFt(a.state.pos.x, a.state.pos.y));
                if (k % 5 == 0)
                {
                    Vec3d los = b.planes[them].state.pos - a.state.pos;
                    double r = los.Length;
                    if (r < 2500 && Envelope.Angle(Battle.GunLine(a), los) < 4 * Math.PI / 180) shotBonus = 1;
                }
            });
            if (sim.crashed[me] || sim.down[me]) return -100;

            var mine = sim.planes[me];
            var theirs = sim.planes[them];
            Vec3d l = theirs.state.pos - mine.state.pos;
            double range = l.Length;
            double ata = Envelope.Angle(mine.Forward, l);          // my nose off the line to them
            double aa = Envelope.Angle(theirs.Forward, l);         // 0 when I am at their six

            double score = -2.0 * ata - 1.2 * aa;
            score -= Math.Abs(range - idealRangeFt) / 2500;
            // anyone on the other side with guns pointing at me
            for (int j = 0; j < sim.Count; j++)
            {
                if (sim.side[j] == sim.side[me] || sim.Out(j) || sim.combat[j].ammoSec <= 0) continue;
                Vec3d lj = sim.planes[j].state.pos - mine.state.pos;
                double rj = lj.Length, theirAta = Envelope.Angle(sim.planes[j].Forward, -lj);   // their nose off the line to me
                if (theirAta < 20 * Math.PI / 180 && rj < 4000) score -= 1.5 * (1 - theirAta / (20 * Math.PI / 180));
            }
            score += 0.5 * (mine.Kias - 250) / 200 + 0.2 * (mine.AltFt - battle.planes[me].AltFt) / 1000;
            if (minClear < 1500) score -= (1500 - minClear) / 300;
            if (mine.Departed) score -= 3;
            return score + shotBonus;
        }

        /// <summary>Fire across the part of the turn whose rounds would hit the projected target, if any.</summary>
        void PlanBurst(Battle battle, ref TurnPlan plan, double turnSec)
        {
            if (battle.ammo[me] <= 0) return;
            var test = plan;
            test.burstStart = 0; test.burstEnd = turnSec;
            var sim = battle.Clone();
            sim.dispersion = false;
            for (int j = 0; j < sim.Count; j++) if (sim.side[j] != sim.side[me]) sim.health[j] = double.PositiveInfinity;   // keep targets flying to see the whole burst
            // The target will not fly exactly as projected: fire at anything that comes near (a wider "hit" here)
            sim.combat = (CombatNumbers[])sim.combat.Clone();
            for (int j = 0; j < sim.Count; j++) sim.combat[j] = sim.combat[j].Copy();
            sim.combat[me].hitRadiusFt *= burstSlack;
            // rounds already in the air are not this burst's
            sim.bullets.Clear();
            var r = me == 0 ? sim.Resolve(test, TheirProjection(), turnSec, false) : sim.Resolve(TheirProjection(), test, turnSec, false);
            double first = double.PositiveInfinity, last = double.NegativeInfinity;
            foreach (var h in r.hits)
            {
                if (h.shooter != me) continue;
                double t = h.fireTime - battle.time;
                first = Math.Min(first, t); last = Math.Max(last, t);
            }
            if (last < first) return;
            plan.burstStart = Math.Max(0, first - 0.15);
            plan.burstEnd = Math.Min(turnSec, last + 0.15);
        }
    }
}
