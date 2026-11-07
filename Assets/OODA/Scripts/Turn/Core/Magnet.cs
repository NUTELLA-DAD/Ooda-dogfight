using System;
using Ooda.Flight.Core;

namespace Ooda.Turn.Core
{
    /// <summary>
    /// Magnet rounds (a loadout pick, see Loadout): your packets of rounds curve toward the nearest enemy they are about to
    /// pass. A round only feels an enemy within RangeFt that is ahead of it (it is closing, and the enemy sits inside
    /// ConeDeg of the way it is flying relative to him); then it is bent sideways, square to that line, by just enough to
    /// close the miss by the time it gets there, and never by more than MaxG. So a near miss is pulled in, a stream
    /// that was well off stays a miss, and a round that has passed lets go. Close in there is no time to bend: the magnet
    /// helps most with a burst that is nearly right at a few hundred feet.
    ///
    /// Each packet carries its own flag (Bullet.magnet, set when it leaves the guns), so it works in the preview, the AI's
    /// tries, subsets and snapshots the same way. Flak never has it. Pure C#, no Unity.
    /// </summary>
    public static class Magnet
    {
        /// <summary>How far ahead a round feels an enemy, ft.</summary>
        public const double RangeFt = 600;
        /// <summary>The enemy must sit within this of the round's line (relative to him), degrees.</summary>
        public const double ConeDeg = 12;
        /// <summary>The hardest the round is bent, G.</summary>
        public const double MaxG = 15;

        static readonly double ConeTan = Math.Tan(ConeDeg * Math.PI / 180);

        /// <summary>
        /// The sideways pull on a magnet round at pos, flying vel, fired by side, toward the nearest enemy it is about to
        /// pass (ft/s²); zero when there is none. target: that enemy, or -1.
        /// </summary>
        public static Vec3d Pull(Battle b, Vec3d pos, Vec3d vel, int side, out int target)
        {
            target = -1;
            double bestD = double.PositiveInfinity;
            Vec3d bestMiss = Vec3d.Zero;
            double bestTc = 0;
            for (int j = 0; j < b.Count; j++)
            {
                if (b.side[j] == side || b.Out(j)) continue;
                var s = b.planes[j].state;
                Vec3d r = s.pos - pos;
                double d = r.Length;
                if (d > RangeFt || d >= bestD) continue;
                Vec3d vr = vel - s.vel;
                double sp = vr.Length;
                if (sp < 1) continue;
                Vec3d dir = vr * (1 / sp);
                double along = Vec3d.Dot(r, dir);
                if (along <= 0) continue;   // passed: it lets go
                Vec3d miss = r - dir * along;
                if (miss.Length > along * ConeTan) continue;
                bestD = d; target = j; bestMiss = miss; bestTc = along / sp;
            }
            if (target < 0) return Vec3d.Zero;
            double m = bestMiss.Length;
            if (m < 1e-6) return Vec3d.Zero;
            double need = 2 * m / Math.Max(1e-4, bestTc * bestTc);   // closes the miss by the time it gets there
            double a = Math.Min(need, MaxG * Atmosphere.GFtps2);
            return bestMiss * (a / m);
        }
    }
}
