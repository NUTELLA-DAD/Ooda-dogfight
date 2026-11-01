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
        public double sizeFt = 0;           // as a target: added to the shooter's hit radius (a big aircraft is easier to hit)
        public double damage = 1;           // per packet that hits
        public double ammoSec = 18;         // seconds of trigger
        public double lifeSec = 2.5;        // packets are gone after this (spent, out of range)
        public double dragPerSec = 0.3;     // the round's speed falls by this share each second (exponential)
        public double health = 12;          // damage the airframe takes before it goes down
        public int missiles = 2;             // heat-seekers carried (a game liberty: no WW2 fighter had them)

        public CombatNumbers Copy() => (CombatNumbers)MemberwiseClone();

        /// <summary>
        /// Six .50 in Brownings, 1,880 rounds: about 18 s of fire. The player's side gets a forgiving hit radius and a
        /// tighter cone (a game liberty, so shots are easier to land); the enemy's guns keep the defaults.
        /// </summary>
        public static CombatNumbers P51D() => new CombatNumbers { guns = "6 x .50 cal", hitRadiusFt = 20, spreadMil = 2.5 };

        /// <summary>Two 20 mm Hispanos (120 rounds each) and two .50s: harder hitting, about 12 s of cannon.</summary>
        public static CombatNumbers SpitfireIX() => new CombatNumbers
        {
            guns = "2 x 20 mm + 2 x .50 cal", muzzleFps = 2850, damage = 1.4, ammoSec = 12, dragPerSec = 0.35,
        };

        /// <summary>One 20 mm MG 151 through the hub and two 13 mm MG 131s over the engine: about 15 s of fire.</summary>
        public static CombatNumbers Bf109G() => new CombatNumbers
        {
            guns = "20 mm + 2 x 13 mm", muzzleFps = 2650, damage = 1.25, ammoSec = 15, dragPerSec = 0.35,
        };

        /// <summary>
        /// The B-17G as a target: 104 ft span, so a much bigger hit sphere; four engines and a big airframe take
        /// about three times a fighter's damage. Its own gunners come later: no guns for now. Estimates to tune.
        /// </summary>
        public static CombatNumbers B17G() => new CombatNumbers
        {
            guns = "gunners (see FlakSite.B17Gunner)", ammoSec = 0, sizeFt = 22, health = 36, missiles = 0,
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

    /// <summary>One packet of rounds in flight. It carries what it does when it hits, so ground guns can fire them too.</summary>
    public class Bullet
    {
        public int owner;          // the aircraft that fired it, or -1 - k for flak site k
        public int side;           // it hits aircraft on the other side
        public double damage, hitRadiusFt, dragPerSec, lifeSec;
        public Vec3d pos, vel;
        public double age;
        public double fireTime;   // battle time it left the guns
        public bool alive = true;
        public Bullet Copy() => (Bullet)MemberwiseClone();
    }

    /// <summary>
    /// A heat-seeking missile (a game liberty for a WW2 setting): a short motor burn, then it coasts and slows. It steers by
    /// proportional navigation at up to maxG, and loses its target for good if the target gets more than gimbalDeg off its nose
    /// (a hard break across it) or goes down; then it flies on unguided. It bursts within its hit radius (a proximity fuse).
    /// Numbers are estimates to tune by feel.
    /// </summary>
    public class Missile : Bullet
    {
        public int target = -1;
        public double burnSec = 2.5, thrustFps2 = 700, maxG = 20, gimbalDeg = 40, nav = 4;

        /// <summary>Launch envelope: the target within this cone off the shooter's nose, between these ranges.</summary>
        public const double LockConeDeg = 30, MinRangeFt = 1200, MaxRangeFt = 9000;
        /// <summary>A heat-seeker needs the engine's heat: no lock from inside this cone off the target's nose (head-on).</summary>
        public const double NoLockNoseDeg = 80;

        public static Missile Make(int owner, int side, int target, Vec3d pos, Vec3d vel, double time) => new Missile
        {
            owner = owner, side = side, target = target, pos = pos, vel = vel, fireTime = time,
            damage = 8, hitRadiusFt = 30, dragPerSec = 0.05, lifeSec = 9,
        };

        /// <summary>One step of flight: steer at the target (if still held), the motor while it burns, drag and gravity.</summary>
        public void Step(Vec3d targetPos, Vec3d targetVel, bool targetOk, double dt)
        {
            double speed = Math.Max(1, vel.Length);
            Vec3d dir = vel * (1 / speed);
            Vec3d acc = new Vec3d(0, 0, -Atmosphere.GFtps2);
            if (target >= 0 && targetOk)
            {
                Vec3d r = targetPos - pos;
                double range = Math.Max(1, r.Length);
                if (Vec3d.Dot(r * (1 / range), dir) < Math.Cos(gimbalDeg * Math.PI / 180)) target = -1;   // off the seeker: lost
                else
                {
                    Vec3d vr = targetVel - vel;
                    Vec3d omega = Vec3d.Cross(r, vr) * (1 / (range * range));   // the sight line's turn rate
                    Vec3d a = Vec3d.Cross(omega, vel) * nav;
                    a = a - dir * Vec3d.Dot(a, dir);                              // steering only, across the path
                    a = a + new Vec3d(0, 0, Atmosphere.GFtps2) - dir * (Atmosphere.GFtps2 * dir.z);   // and hold itself up against gravity
                    double max = maxG * Atmosphere.GFtps2, len = a.Length;
                    if (len > max) a = a * (max / len);
                    acc = acc + a;
                }
            }
            else if (target >= 0) target = -1;
            if (age < burnSec) acc = acc + dir * thrustFps2;
            Vec3d v0 = vel;
            vel = (vel + acc * dt) * Math.Exp(-dragPerSec * dt);
            pos = pos + (v0 + vel) * (dt / 2);
            age += dt;
        }
    }

    /// <summary>A packet that struck: where on the target (its own frame: x right, y forward, z up, ft) for hit locations later.</summary>
    public struct HitEvent
    {
        public double time;
        public int shooter, target;   // shooter: an aircraft, or -1 - k for flak site k
        public double damage;
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
        public bool missile;      // a missile's smoke trail rather than a tracer
        /// <summary>
        /// Recorded runs only: the closest the packet came to an opposing aircraft (ft), against the target it came
        /// nearest to hitting (closestTarget, -1 none), and that pass's hit distance (the round's hit radius plus the target's size).
        /// </summary>
        public double closestFt = double.PositiveInfinity, reachFt = 1;
        public int closestTarget = -1;
        /// <summary>A near miss: it did not hit, but passed within twice the hit distance.</summary>
        public bool Near => !hit && closestTarget >= 0 && closestFt <= 2 * reachFt;
        public Vec3d? At(int frame) => frame >= startFrame && frame - startFrame < points.Count ? points[frame - startFrame] : (Vec3d?)null;
    }
}
