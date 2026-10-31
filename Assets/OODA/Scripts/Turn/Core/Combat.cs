using System;
using Ooda.Flight.Core;

namespace Ooda.Turn.Core
{
    /// <summary>
    /// An aircraft's guns and how much it takes to bring down. Rounds fly as "packets", one every packetSec,
    /// each standing for the guns' combined fire in that slice of time. Estimates to tune by feel.
    /// </summary>
    [Serializable]
    public class CombatNumbers
    {
        public string guns = "Guns";
        public double muzzleFps = 2900;     // muzzle velocity, ft/s, added to the aircraft's own
        public double packetSec = 0.05;     // one packet every this many seconds of trigger
        public double spreadMil = 3;        // dispersion, milliradians (1 sigma)
        public double hitRadiusFt = 14;     // a packet passing this close to the target's centre hits
        public double damage = 1;           // per packet that hits
        public double ammoSec = 18;         // seconds of trigger
        public double lifeSec = 2.5;        // packets are gone after this (spent, out of range)
        public double dragPerSec = 0.3;     // the round's speed falls by this share each second (exponential)
        public double health = 12;          // damage the airframe takes before it goes down

        public CombatNumbers Copy() => (CombatNumbers)MemberwiseClone();

        /// <summary>Six .50 in Brownings, 1,880 rounds: about 18 s of fire.</summary>
        public static CombatNumbers P51D() => new CombatNumbers { guns = "6 x .50 cal" };

        /// <summary>Two 20 mm Hispanos (120 rounds each) and two .50s: harder hitting, about 12 s of cannon.</summary>
        public static CombatNumbers SpitfireIX() => new CombatNumbers
        {
            guns = "2 x 20 mm + 2 x .50 cal", muzzleFps = 2850, damage = 1.4, ammoSec = 12, dragPerSec = 0.35,
        };
    }

    /// <summary>A small deterministic random number generator that can be copied with its state.</summary>
    public struct Lcg
    {
        public uint s;
        public Lcg(uint seed) { s = seed == 0 ? 1u : seed; }
        public double Next() { s = s * 1664525u + 1013904223u; return (s >> 8) / 16777216.0; }
        /// <summary>Normal, mean 0 and sigma 1 (Box-Muller).</summary>
        public double Gauss()
        {
            double u = Math.Max(1e-9, Next()), v = Next();
            return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v);
        }
    }

    /// <summary>One packet of rounds in flight.</summary>
    public class Bullet
    {
        public int owner;
        public Vec3d pos, vel;
        public double age;
        public double fireTime;   // battle time it left the guns
        public bool alive = true;
        public Bullet Copy() => (Bullet)MemberwiseClone();
    }

    /// <summary>A packet that struck: where on the target (its own frame: x right, y forward, z up, ft) for hit locations later.</summary>
    public struct HitEvent
    {
        public double time;
        public int shooter, target;
        public Vec3d pos, local;
        public double fireTime;
    }

    /// <summary>One moment of one aircraft, for playback.</summary>
    public struct AircraftFrame
    {
        public double time;
        public Vec3d pos, vel, up;
        public double alphaDeg, g, throttle;
        public bool firing, stalled, departed, down;
    }

    /// <summary>One packet's path through a turn, for drawing tracers: a point for each frame from startFrame.</summary>
    public class Trace
    {
        public int owner, startFrame;
        public System.Collections.Generic.List<Vec3d> points = new System.Collections.Generic.List<Vec3d>();
        public bool hit;
        public Vec3d? At(int frame) => frame >= startFrame && frame - startFrame < points.Count ? points[frame - startFrame] : (Vec3d?)null;
    }
}
