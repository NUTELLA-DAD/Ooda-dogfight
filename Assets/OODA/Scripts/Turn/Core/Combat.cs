using System;
using Ooda.Flight.Core;

namespace Ooda.Turn.Core
{
    /// <summary>
    /// An aircraft's guns and how tough it is (see Systems). Rounds fly as "packets", one every packetSec,
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
        /// <summary>How much the airframe takes: each system (Systems) is knocked out by Battle.SystemShare of it, the wings by WingsShare.</summary>
        public double toughness = 12;
        public int missiles = 2;             // heat-seekers carried (a game liberty: no WW2 fighter had them)
        public int flares = 6;               // flares to decoy a heat-seeker with (the same liberty)
        /// <summary>
        /// The radar (see Radar): how far it sees, 0 for none (every warbird), the radar-guided missiles carried, and the
        /// chaff bundles to break one with. Jets only.
        /// </summary>
        public double radarRangeFt;
        public int radarMissiles, chaff;
        /// <summary>The arcade specials carried (see Specials): balloon mines, heat-blade charges and stealth charges. None unless Specials.Arm gives them (a dogfight with Arcade on).</summary>
        public int balloonMines, heatBlades, stealths, jammers;
        /// <summary>Gravity wells carried (see Specials): none unless the loadout (or Specials.Arm) gives them.</summary>
        public int wells;
        /// <summary>Time slow bombs carried (see Specials): none unless the loadout (or Specials.Arm) gives them.</summary>
        public int slowBombs;
        /// <summary>Ice (a loadout pick, see Loadout): the airframe is cased in ice, cold as the sky, so no heat-seeker can lock it and one that blunders into it shatters harmlessly. Radar missiles and guns still bite.</summary>
        public bool iced;
        /// <summary>Magnet rounds (a loadout pick, see Magnet): the rounds curve toward the nearest enemy they pass.</summary>
        public bool magnet;
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
            toughness = 15, sizeFt = 3, heatIdle = 0.75, heatFull = 1.5,
        };

        /// <summary>
        /// Two 20 mm Type 99s in the wings (60 rounds each, slow and loopy) and two 7.7 mm Type 97s over the engine: hard
        /// hitting while the cannon last, a wider cone, about 12 s of fire. No armour and no self-sealing tanks: it goes
        /// down quickly.
        /// </summary>
        public static CombatNumbers A6M2() => new CombatNumbers
        {
            guns = "2 x 20 mm + 2 x 7.7 mm", muzzleFps = 2000, damage = 1.3, ammoSec = 12, spreadMil = 3.5, dragPerSec = 0.4,
            toughness = 9, heatIdle = 0.45, heatFull = 0.9,
        };

        /// <summary>
        /// The F-4E Phantom's M61 Vulcan, 20 mm, 640 rounds at 6,000 a minute: a fast, hard-hitting stream but only about
        /// 7 s of it, a little wider spread. Four heat-seekers (Sidewinders). A bigger, tougher airframe (63 ft long, 38 ft
        /// span) and so a bigger target. Estimates to tune by feel.
        /// </summary>
        public static CombatNumbers Phantom() => new CombatNumbers
        {
            guns = "M61 20 mm", muzzleFps = 3380, damage = 1.3, ammoSec = 7, spreadMil = 3.5, dragPerSec = 0.3,
            toughness = 16, sizeFt = 6, missiles = 4, flares = 8,
            heatIdle = 1.2, heatFull = 2.4, heatWep = 2.4,   // two J79s: hot, and blazing in afterburner
            radarRangeFt = 30000, radarMissiles = 2, chaff = 6,   // the APQ-120 and two Sparrows
        };

        /// <summary>
        /// The F-15C's M61A1, 20 mm, 940 rounds at 6,000 a minute: the F-4's stream with more of it, about 9 s. Four
        /// heat-seekers (Sidewinders) on the wing pylons. A big, tough airframe (64 ft long, 43 ft span), so a big target.
        /// Estimates to tune by feel.
        /// </summary>
        public static CombatNumbers F15C() => new CombatNumbers
        {
            guns = "M61A1 20 mm", muzzleFps = 3380, damage = 1.3, ammoSec = 9, spreadMil = 3.2, dragPerSec = 0.3,
            toughness = 17, sizeFt = 6, missiles = 4, flares = 8,
            heatIdle = 1.2, heatFull = 2.4, heatWep = 2.6,   // two F100s: hot, and blazing in afterburner
            radarRangeFt = 36000, radarMissiles = 2, chaff = 6,   // the APG-63 and two Sparrows
        };

        /// <summary>
        /// The Su-27's GSh-30-1, one 30 mm in the right wing root, 150 rounds at about 1,650 a minute: only about 5.5 s of
        /// fire, but each shell hits much harder than a 20 mm and the single barrel holds a tight cone. Six heat-seekers
        /// (R-73s) on the wing stations and two radar missiles (R-27s). A big, rugged airframe (72 ft long, 48 ft span),
        /// so a bigger target than the Eagle and a little tougher. Estimates to tune by feel.
        /// </summary>
        public static CombatNumbers Su27() => new CombatNumbers
        {
            guns = "GSh-30-1 30 mm", muzzleFps = 2820, damage = 1.9, ammoSec = 5.5, spreadMil = 2.6, dragPerSec = 0.32,
            toughness = 18, sizeFt = 7, missiles = 6, flares = 8,
            heatIdle = 1.2, heatFull = 2.4, heatWep = 2.7,   // two AL-31Fs: hot, and blazing in afterburner
            radarRangeFt = 33000, radarMissiles = 2, chaff = 6,   // the N001 and two R-27s
        };

        /// <summary>
        /// The B-17G as a target: 104 ft span, so a much bigger hit sphere; four engines and a big airframe take
        /// about three times a fighter's damage. Its own gunners come later: no guns for now. Estimates to tune.
        /// </summary>
        public static CombatNumbers B17G() => new CombatNumbers
        {
            guns = "gunners (see FlakSite.B17Gunner)", ammoSec = 0, sizeFt = 22, toughness = 36, missiles = 0, flares = 0,
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
        public bool magnet;       // a magnet round (see Magnet): it curves toward the nearest enemy it passes
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
        /// <summary>
        /// A radar missile (see Radar) rather than a heat-seeker: it rides the shooter's radar, so it guides only while the
        /// shooter holds the target in its cone, and it is beaten by the notch and by chaff, never by flares.
        /// </summary>
        public bool radar;
        /// <summary>Who it was fired at (kept after it loses them, unlike target).</summary>
        public int aimedAt = -1;
        /// <summary>How long its target has held the notch (flown square across the missile's line of sight) without a break.</summary>
        public double notchSec;
        /// <summary>Chaff its target has let go at it, and when the next bundle may go (battle time).</summary>
        public int chaffDrawn;
        public double nextChaffAt;
        /// <summary>Why it lost its target, once it has (for the warnings and the replay).</summary>
        public MissileLoss lost;
        /// <summary>Still guided: on its target or on a flare.</summary>
        public bool Tracking => target >= 0 || decoy >= 0;

        /// <summary>Lose the target for good, saying why.</summary>
        public void Lose(MissileLoss why)
        {
            if (!Tracking) return;
            target = decoy = -1;
            lost = why;
        }

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
        /// A radar missile (a Sparrow): a longer burn, so a longer reach than the heat-seeker, a wider seeker and a bigger
        /// warhead. It needs the shooter's radar lock to launch and to guide (see Radar). Estimates to tune by feel.
        /// </summary>
        public static Missile MakeRadar(int owner, int side, int target, Vec3d pos, Vec3d vel, double time) => new Missile
        {
            owner = owner, side = side, target = target, aimedAt = target, pos = pos, vel = vel, fireTime = time, radar = true,
            damage = 9, hitRadiusFt = 35, dragPerSec = 0.04, lifeSec = 16, burnSec = 3.5, thrustFps2 = 650, maxG = 25, gimbalDeg = 60,
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
                bool notched = false;
                if (radar && target >= 0)
                {
                    // the notch: the target flying square across the line of sight shows no closing speed; held long enough, it is gone
                    notchSec = Radar.Beaming(targetVel, r) ? notchSec + dt : 0;
                    notched = notchSec >= Radar.NotchHoldSec - 1e-9;
                }
                if (notched) Lose(MissileLoss.Notch);
                else if (Vec3d.Dot(r * (1 / range), dir) < Math.Cos(gimbalDeg * Math.PI / 180)) Lose(MissileLoss.Seeker);   // off the seeker: lost
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
            else if (Tracking) Lose(MissileLoss.Gone);
            if (age < burnSec) acc = acc + dir * thrustFps2;
            Vec3d v0 = vel;
            vel = (vel + acc * dt) * Math.Exp(-dragPerSec * dt);
            pos = pos + (v0 + vel) * (dt / 2);
            age += dt;
        }
    }

    /// <summary>Why a missile lost its target.</summary>
    public enum MissileLoss { None, Seeker, Gone, Flare, Notch, Radar, Chaff, Well }

    /// <summary>
    /// Radar and radar missiles, for jets only (a warbird has no radar: CombatNumbers.radarRangeFt 0), kept arcade simple.
    /// The radar sees every enemy inside its cone (ConeDeg off the nose, out to radarRangeFt) that isn't stealthy. It tracks
    /// one: the one asked for (TurnPlan.radarTarget) when it is in the cone, else the one it was tracking, else the one nearest
    /// the nose. Keep the same one in the cone for LockSec (one whole turn) and it is locked: a radar missile can go. The missile
    /// guides only while the shooter's radar still tracks that target in its cone (fire, and you must keep pointing at them),
    /// and the target beats it two ways. The notch: fly square across the missile's line of sight (within NotchBandDeg of 90
    /// degrees) for NotchHoldSec, and with no closing speed to see it loses you for good. Chaff: a pair of bundles goes at a
    /// radar missile closing inside ChaffWarnFt (unless held, with the flares), each with one chance to break it, a much
    /// better chance while you are in the notch.
    /// </summary>
    public static class Radar
    {
        public const double ConeDeg = 60, LockSec = 3;
        /// <summary>No radar missile inside this range (it needs room to arm and turn).</summary>
        public const double MinRangeFt = 1500;
        /// <summary>Once fired, the shooter's radar keeps the missile guided out to this times its range.</summary>
        public const double HoldRangeFactor = 1.25;
        public const double NotchBandDeg = 25, NotchHoldSec = 1.0;
        public const double ChaffWarnFt = 4000, ChaffGapSec = 0.35, ChaffBurnSec = 4;
        public const int ChaffPerMissile = 2;
        public const double ChaffChance = 0.2, ChaffChanceNotching = 0.7;

        /// <summary>How far the velocity is off square to the line of sight, degrees: 0 flying dead across it, 90 straight along it.</summary>
        public static double OffBeamDeg(Vec3d vel, Vec3d los)
        {
            double v = vel.Length, l = los.Length;
            if (v < 1 || l < 1) return 90;
            double c = Math.Abs(Vec3d.Dot(vel, los)) / (v * l);
            return Math.Asin(Math.Min(1, c)) * 180 / Math.PI;
        }

        /// <summary>In the notch: flying within NotchBandDeg of square across the line of sight (los from the missile to the target).</summary>
        public static bool Beaming(Vec3d vel, Vec3d los) => OffBeamDeg(vel, los) < NotchBandDeg;

        /// <summary>The chance one chaff bundle breaks a radar missile off a target that is (or isn't) in the notch.</summary>
        public static double ChaffBreak(bool notching) => notching ? ChaffChanceNotching : ChaffChance;
    }

    /// <summary>A bundle of chaff: a cloud of foil that blooms behind a jet, hangs and falls. It hits nothing; see Radar.</summary>
    public class Chaff : Bullet
    {
        public static Chaff Make(int owner, int side, Vec3d pos, Vec3d vel, double time) => new Chaff
        {
            owner = owner, side = side, pos = pos, vel = vel, fireTime = time,
            damage = 0, hitRadiusFt = 0, dragPerSec = 2.5, lifeSec = Radar.ChaffBurnSec,
        };
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

    /// <summary>
    /// A packet that struck: where it entered the target's hit sphere (its own frame: x right, y forward, z up, ft), the
    /// system it struck (a SystemId, or -1 for a blast that wears them all), how much it wore it (wear, a share of the
    /// system), and whether it brought the target down.
    /// </summary>
    public struct HitEvent
    {
        public double time;
        public int shooter, target;   // shooter: an aircraft, or -1 - k for flak site k
        public double damage;
        public int system;
        public double wear;
        public bool downed;
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
        /// <summary>The heat blade's build toward the sonic boom, 0 to 1 (white hot at 1), and whether it has broken the barrier.</summary>
        public double ramHeat;
        public bool sonic;
        /// <summary>The pilot's G stress, 0 to 1 (1 blacked out: G-LOC); see FlightModel.GreyOut.</summary>
        public double greyOut;
        /// <summary>The pilot has bailed out: when (battle time; meaningless while bailed is false), from where and how fast.</summary>
        public bool bailed;
        public double bailAt;
        public Vec3d bailPos, bailVel;
        /// <summary>Where the nose points (it leaves the path in vertical flight: a tail slide, a hammerhead); zero in an old recording: along vel.</summary>
        public Vec3d nose;
        /// <summary>The nose along vel when it isn't recorded.</summary>
        public Vec3d Nose => nose.Length > 0.5 ? nose : vel.Normalized;    // the heat blade glowing; stealthy (unseen by the other side)
    }

    /// <summary>A sonic boom (see Specials): who broke the barrier, at which frame of the turn, where and flying which way.</summary>
    public class BoomEvent
    {
        public int owner, frame;
        public Vec3d pos, dir;
    }

    /// <summary>A plane the sonic boom shoved off course: by whose boom, at which frame, and the change in its velocity (ft/s).</summary>
    public class BoomShoveEvent
    {
        public int owner, target, frame;
        public Vec3d pos, dv;
    }

    /// <summary>One packet's path through a turn, for drawing tracers: a point for each frame from startFrame.</summary>
    public class Trace
    {
        public int owner, startFrame;
        public System.Collections.Generic.List<Vec3d> points = new System.Collections.Generic.List<Vec3d>();
        public bool hit;
        public bool missile;      // a missile's smoke trail rather than a tracer
        public bool radar;        // that missile is radar guided
        public bool chaff;        // a chaff cloud (kept in TurnResult.chaff)
        public bool mine;         // a balloon mine's drift (kept in TurnResult.mines); hit: it went off
        public bool well;         // a gravity well's drift (kept in TurnResult.wells)
        public bool magnet;       // a magnet round's path (see Magnet): drawn with a violet tint
        public bool bubble;       // a time slow bomb's flight and bubble (kept in TurnResult.bubbles)
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
