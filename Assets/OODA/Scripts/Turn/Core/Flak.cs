using System;
using Ooda.Flight.Core;

namespace Ooda.Turn.Core
{
    /// <summary>
    /// A light anti-aircraft gun on the ground (a quad 20 mm): it fires bursts of tracer at the nearest aircraft
    /// of the other side it can see within range, leading it the way a gunner would, with an aiming error that
    /// stays the same through a burst (so a steady target gets walked, a jinking one gets missed). Rounds are
    /// ordinary packets that fall and slow. Feet, x east, y north, z up. Numbers are estimates to tune by feel.
    /// </summary>
    public class FlakSite
    {
        public Vec3d pos;
        public int side = 1;
        public double rangeFt = 6500;      // slant range it opens fire at
        public double muzzleFps = 2900;
        public double packetSec = 0.05;
        public double burstSec = 1.5, pauseSec = 1.5;
        public double damage = 0.3;        // a 20 mm packet: less than a fighter's whole battery
        public double hitRadiusFt = 12;
        public double spreadMil = 5;       // shot to shot (with dispersion on)
        public double aimErrorMil = 18;    // the gunner's error, held through a burst (1 sigma per axis)
        public double dragPerSec = 0.35, lifeSec = 3.5;
        /// <summary>How much less the gunners want a bomber than a fighter (a bomber counts as this many ranges further away).</summary>
        public double bomberReluctance = 1;

        // state
        public double phase;               // seconds into the burst-and-pause cycle
        public int target = -1;
        public double errA, errB;          // this burst's aiming error, radians
        public double fireClock;

        public FlakSite Copy() => (FlakSite)MemberwiseClone();

        /// <summary>The direction to fire now to meet the target's straight-line path (lead and drop), with this burst's error.</summary>
        public Vec3d Aim(FlightModel m)
        {
            Vec3d rel = m.state.pos - pos, v = m.state.vel;
            double t = rel.Length / muzzleFps;
            for (int k = 0; k < 4; k++)
            {
                double meanSpeed = muzzleFps * (1 - Math.Exp(-dragPerSec * t)) / Math.Max(1e-6, dragPerSec * t);
                t = (rel + v * t).Length / meanSpeed;
            }
            Vec3d aim = rel + v * t + new Vec3d(0, 0, 0.5 * Atmosphere.GFtps2 * t * t);
            Vec3d d = aim.Normalized;
            Vec3d side_ = Vec3d.Cross(d, new Vec3d(0, 0, 1));
            side_ = side_.Length < 1e-6 ? new Vec3d(1, 0, 0) : side_.Normalized;
            Vec3d up = Vec3d.Cross(side_, d);
            return (d + side_ * errA + up * errB).Normalized;
        }
    }
}
