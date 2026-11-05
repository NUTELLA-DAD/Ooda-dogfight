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
        public int flares = 6;               // flares to decoy a heat-seeker with (the same liberty)
        /// <summary>The arcade specials carried (see Specials): balloon mines, heat-blade charges and stealth charges. None unless Specials.Arm gives them (a dogfight with Arcade on).</summary>
        public int balloonMines, heatBlades, stealths, jammers;
        /// <summary>
        /// The engines' heat as a seeker sees it from dead astern (see Heat): at idle, at full throttle, and the factor for
        /// WEP (a jet's afterburner). A piston fighter at full throttle is 1, the yardstick for the seeker's lock range.
        /// </summary>
        public double heatIdle = 0.5, heatFull = 1, heatWep = 1.2;

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
        /// One 20 mm Hispano and four .50s, all in the nose: no convergence, so a tight cone that holds at any range,
        /// about 15 s of fire. Two engines and a bigger airframe (52 ft span): a little tougher, and a bigger target.
        /// </summary>
        public static CombatNumbers P38L() => new CombatNumbers
        {
            guns = "20 mm + 4 x .50 cal", muzzleFps = 2850, damage = 1.35, ammoSec = 15, spreadMil = 2.5, dragPerSec = 0.32,
            health = 15, sizeFt = 3, heatIdle = 0.75, heatFull = 1.5,
        };

        /// <summary>
        /// Two 20 mm Type 99s in the wings (60 rounds each, slow and loopy) and two 7.7 mm Type 97s over the engine: hard
        /// hitting while the cannon last, a wider cone, about 12 s of fire. No armour and no self-sealing tanks: it goes
        /// down quickly.
        /// </summary>
        public static CombatNumbers A6M2() => new CombatNumbers
        {
            guns = "2 x 20 mm + 2 x 7.7 mm", muzzleFps = 2000, damage = 1.3, ammoSec = 12, spreadMil = 3.5, dragPerSec = 0.4,
            health = 9, heatIdle = 0.45, heatFull = 0.9,
        };

        /// <summary>
        /// The F-4E Phantom's M61 Vulcan, 20 mm, 640 rounds at 6,000 a minute: a fast, hard-hitting stream but only about
        /// 7 s of it, a little wider spread. Four heat-seekers (Sidewinders). A bigger, tougher airframe (63 ft long, 38 ft
        /// span) and so a bigger target. Estimates to tune by feel.
        /// </summary>
        public static CombatNumbers Phantom() => new CombatNumbers
        {
            guns = "M61 20 mm", muzzleFps = 3380, damage = 1.3, ammoSec = 7, spreadMil = 3.5, dragPerSec = 0.3,
            health = 16, sizeFt = 6, missiles = 4, flares = 8,
            heatIdle = 1.2, heatFull = 2.4, heatWep = 2.4,   // two J79s: hot, and blazing in afterburner
        };

        /// <summary>
        /// The B-17G as a target: 104 ft span, so a much bigger hit sphere; four engines and a big airframe take
        /// about three times a fighter's damage. Its own gunners come later: no guns for now. Estimates to tune.
        /// </summary>
        public static CombatNumbers B17G() => new CombatNumbers
        {
            guns = "gunners (see FlakSite.B17Gunner)", ammoSec = 0, sizeFt = 22, health = 36, missiles = 0, flares = 0,
            heatIdle = 1.5, heatFull = 3,   // four radials
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
        /// <summary>The flare (its id) the seeker has been pulled onto instead of its target; -1 none.</summary>
        public int decoy = -1;
        /// <summary>How many flares its target has let go at it, and when the next may go (battle time): a pair at most.</summary>
        public int flaresDrawn;
        public double nextFlareAt;
        public double burnSec = 2.5, thrustFps2 = 700, maxG = 20, gimbalDeg = 40, nav = 4;
        /// <summary>Still guided: on its target or on a flare.</summary>
        public bool Tracking => target >= 0 || decoy >= 0;

        /// <summary>Launch envelope: the target within this cone off the shooter's nose, between these ranges.</summary>
        public const double LockConeDeg = 30, MinRangeFt = 1200, MaxRangeFt = 9000;
        /// <summary>A heat-seeker needs the engine's heat: no lock from inside this cone off the target's nose (head-on).</summary>
        public const double NoLockNoseDeg = 80;

        public static Missile Make(int owner, int side, int target, Vec3d pos, Vec3d vel, double time) => new Missile
        {
            owner = owner, side = side, target = target, pos = pos, vel = vel, fireTime = time,
            damage = 8, hitRadiusFt = 30, dragPerSec = 0.05, lifeSec = 9,
        };

        /// <summary>
        /// One step of flight: steer at what the seeker holds (the target, or the flare it was decoyed onto) if it still
        /// holds it, the motor while it burns, drag and gravity.
        /// </summary>
        public void Step(Vec3d targetPos, Vec3d targetVel, bool targetOk, double dt)
        {
            double speed = Math.Max(1, vel.Length);
            Vec3d dir = vel * (1 / speed);
            Vec3d acc = new Vec3d(0, 0, -Atmosphere.GFtps2);
            if (Tracking && targetOk)
            {
                Vec3d r = targetPos - pos;
                double range = Math.Max(1, r.Length);
                if (Vec3d.Dot(r * (1 / range), dir) < Math.Cos(gimbalDeg * Math.PI / 180)) target = decoy = -1;   // off the seeker: lost
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
            else if (Tracking) target = decoy = -1;
            if (age < burnSec) acc = acc + dir * thrustFps2;
            Vec3d v0 = vel;
            vel = (vel + acc * dt) * Math.Exp(-dragPerSec * dt);
            pos = pos + (v0 + vel) * (dt / 2);
            age += dt;
        }
    }

    /// <summary>
    /// A flare: a magnesium candle let go behind an aircraft to decoy a heat-seeker. It burns far hotter than an engine for a
    /// few seconds while it slows and falls away; it hits nothing. Each one has one chance to pull a seeker off: see Heat.Lure.
    /// </summary>
    public class Flare : Bullet
    {
        public int id;
        public static Flare Make(int id, int owner, int side, Vec3d pos, Vec3d vel, double time) => new Flare
        {
            id = id, owner = owner, side = side, pos = pos, vel = vel, fireTime = time,
            damage = 0, hitRadiusFt = 0, dragPerSec = 1.2, lifeSec = Heat.FlareBurnSec,
        };
    }

    /// <summary>
    /// Heat as the missile's seeker sees it, in one unit: a piston fighter's engine at full throttle seen from dead astern is 1.
    /// The seeker's lock range grows with the square root of the heat it sees (an inverse-square sensor); a flare is hotter
    /// than an engine, so it can pull the seeker off, the more likely the cooler the aircraft looks. The IR view draws the same numbers.
    /// </summary>
    public static class Heat
    {
        public const double FlareHeat = 1.6, FlareBurnSec = 3.5;
        /// <summary>The motor while it burns, and the hot body after.</summary>
        public const double MissileBurnHeat = 3, MissileCoastHeat = 0.5;
        /// <summary>A seeker on someone within this range makes its target's flares go (unless they are held), two, this far apart.</summary>
        public const double FlareWarnFt = 3000, FlareGapSec = 0.35;
        public const int FlaresPerMissile = 2;
        /// <summary>The best chance a single flare has of pulling a seeker off (against a cold target).</summary>
        public const double LureMax = 0.9;

        /// <summary>How much of the engine heat shows from a direction: all of it from astern, a quarter from ahead.</summary>
        /// <param name="fwd">The aircraft's nose (unit).</param><param name="toEye">From the aircraft toward the seeker (unit).</param>
        public static double Aspect(Vec3d fwd, Vec3d toEye)
        {
            double behind = (1 - Vec3d.Dot(fwd, toEye)) / 2;   // 1 dead astern, 0 dead ahead
            return 0.25 + 0.75 * behind * behind;
        }

        /// <summary>The engines' heat from astern for a throttle, WEP (the afterburner) and the rocket.</summary>
        public static double Engine(CombatNumbers c, double throttle, bool wep, bool rocket)
        {
            double t = Math.Max(0, Math.Min(1, throttle));
            return (c.heatIdle + (c.heatFull - c.heatIdle) * t) * (wep ? c.heatWep : 1) + (rocket ? 2 : 0);
        }

        /// <summary>An aircraft's heat as seen from eye.</summary>
        public static double Of(FlightModel m, CombatNumbers c, Vec3d eye)
        {
            Vec3d r = eye - m.state.pos;
            double len = r.Length;
            double asp = len < 1 ? 1 : Aspect(m.Forward, r * (1 / len));
            return Engine(c, m.throttle, m.wepOn, m.RocketBurning) * asp;
        }

        /// <summary>A flare's heat at an age: flaring up in a tenth of a second, then fading as it burns down.</summary>
        public static double Flare(double age) => age < 0 || age > FlareBurnSec ? 0
            : FlareHeat * Math.Min(1, age / 0.1) * (1 - 0.6 * age / FlareBurnSec);

        /// <summary>A missile's heat at an age.</summary>
        public static double Missile(double age, double burnSec) => age < burnSec ? MissileBurnHeat : MissileCoastHeat;

        /// <summary>The seeker's lock range on something this hot (Missile.MaxRangeFt for a heat of 1).</summary>
        public static double LockRangeFt(double heat) => Ooda.Turn.Core.Missile.MaxRangeFt * Math.Max(0.6, Math.Min(1.5, Math.Sqrt(Math.Max(0, heat))));

        /// <summary>The chance a fresh flare pulls a seeker off a target that looks this hot to it.</summary>
        public static double Lure(double targetHeat) => LureMax * FlareHeat / (FlareHeat + Math.Max(0, targetHeat));
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
        public bool rocket, brake;   // the rocket burning, the speed brake out
        public bool wep;             // war emergency power in (a jet's afterburner lit)
        public bool ram, cloaked;
        /// <summary>Where the nose points (it leaves the path in vertical flight: a tail slide, a hammerhead); zero in an old recording: along vel.</summary>
        public Vec3d nose;
        /// <summary>The nose along vel when it isn't recorded.</summary>
        public Vec3d Nose => nose.Length > 0.5 ? nose : vel.Normalized;    // the heat blade glowing; stealthy (unseen by the other side)
    }

    /// <summary>One packet's path through a turn, for drawing tracers: a point for each frame from startFrame.</summary>
    public class Trace
    {
        public int owner, startFrame;
        public System.Collections.Generic.List<Vec3d> points = new System.Collections.Generic.List<Vec3d>();
        public bool hit;
        public bool missile;      // a missile's smoke trail rather than a tracer
        public bool mine;         // a balloon mine's drift (kept in TurnResult.mines); hit: it went off
        public double age0;       // how old the round (missile, flare) was at startFrame, s
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
