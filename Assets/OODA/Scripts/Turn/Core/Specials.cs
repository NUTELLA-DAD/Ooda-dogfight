using System;
using Ooda.Flight.Core;

namespace Ooda.Turn.Core
{
    /// <summary>
    /// The arcade specials, a game liberty like the missiles: each fighter carries a few (CombatNumbers.balloonMines,
    /// heatBlades, stealths) and plans them like the rocket, at the start of a turn.
    ///
    /// Balloon mine: let go behind you at any moment of the turn you plan (TurnPlan.mineAt), it slows to a hang in a second
    /// and drifts slowly on (MineDriftFps along the way you were flying, swaying and bobbing). It arms after ArmSec and goes
    /// off on the first enemy that passes within FuseFt, for MineDamage (most of a fighter). It hangs there for LifeSec, so a
    /// tailer who planned to follow you straight flies into it, and anyone can fly into it later.
    ///
    /// They come only with Arcade on in the dogfight setup (DogfightSetup.arcade): Arm gives a fighter its load.
    ///
    /// Heat blade: the airframe glows red hot for the whole turn and the guns stay cold. Any enemy you pass within
    /// BladeReachFt of is cut in half (down at once) and you fly on; two blades meeting both go down. It makes you the hottest
    /// thing in the sky (BladeHeat on top of the engine): every heat-seeker locks you from farther and flares hardly help.
    ///
    /// Jammer: every enemy within JamRangeFt sees a scrambled picture for JamSec (the next two planning phases): their AI
    /// pilots plan against the others where they aren't (Scramble), and a jammed player's ghosts and projections jump about.
    ///
    /// Stealth: unseen for CloakSec (two normal turns): the other side's AI pilots, its flak and gunners don't see you, and the
    /// player can't see a stealthy enemy (only its heat, in the IR view). Heat-seekers still lock you: the engine is as hot as
    /// ever. Firing your guns or a missile shows you again at once.
    ///
    /// Gravity well: thrown ahead and into your turn (it settles WellAheadFt ahead and WellInFt toward where your plan points
    /// the nose) at any moment of the turn you plan (TurnPlan.wellAt), it slows to a hang in a second or so and, once it opens (WellArmSec), pulls
    /// everything near it toward its middle for WellLifeSec: every plane (both sides, you too) at up to WellPlaneG, missiles at
    /// up to WellMissileG and rounds at up to WellRoundG (they're light: a stream across it bends well off its line). The pull
    /// grows toward the core's edge (WellCoreFt) and falls with the square of the range past it, out to WellReachFt. Inside
    /// your turn it adds to your wing's pull, so you turn tighter; a missile that is drawn inside WellCaptureFt loses its
    /// target for good and is flung on. It does no damage itself.
    ///
    /// Portals: a linked pair of rings (Portal), opened at a moment of the turn you plan (TurnPlan.portalAt). The entry
    /// opens round you on your path (or where the plan puts it), the exit where the plan puts it (PortalEnds keeps both in
    /// reach and clear of the ground). Anything that flies through one ring comes out of the other: planes (both sides),
    /// rounds, missiles and flares, keeping their speed, and their heading and bank as they were to the ring they entered.
    /// Both ways, for PortalLifeSec (two turns). It does no damage and is no obstacle.
    /// </summary>
    public static class Specials
    {
        public const double ArmSec = 0.6, LifeSec = 15, FuseFt = 70, MineDamage = 10, MineDragPerSec = 2.5, MineRiseFps = 2;
        /// <summary>The mine's drift once it hangs: the breeze along the way it was let go, a slow sway across it and a bob, in ft/s.</summary>
        public const double MineDriftFps = 12, MineSwayFps = 5, MineBobFps = 2;
        public const double BladeReachFt = 45, BladeHeat = 4;
        public const double CloakSec = 6;
        /// <summary>The jammer: how far it reaches, and how long the ones it catches see scrambled ghosts (two planning phases).</summary>
        public const double JamRangeFt = 9000, JamSec = 6.5;
        /// <summary>How far off a jammed pilot's picture of the others is: their place by up to this, their heading by up to JamTurnDeg.</summary>
        public const double JamOffsetFt = 900, JamTurnDeg = 70;
        /// <summary>
        /// The gravity well's throw: it settles WellAheadFt ahead of you and WellInFt into your turn (toward where the plan
        /// points the nose; with no turn planned, toward your lift), slowing to a hang at WellDragPerSec.
        /// </summary>
        public const double WellAheadFt = 600, WellInFt = 700, WellDragPerSec = 1.5;
        /// <summary>The gravity well: it opens after WellArmSec, pulls for WellLifeSec in all, fading over its last WellFadeSec.</summary>
        public const double WellArmSec = 0.4, WellLifeSec = 6, WellFadeSec = 1;
        /// <summary>The gravity well's pull: strongest at the core's edge, falling with the square of the range out to its reach.</summary>
        public const double WellCoreFt = 700, WellReachFt = 3500;
        /// <summary>The gravity well's pull at its strongest, in G: on a plane, on a missile and on a round.</summary>
        public const double WellPlaneG = 2.5, WellMissileG = 22, WellRoundG = 60;
        /// <summary>A missile drawn this close to the middle of a gravity well loses its target for good.</summary>
        public const double WellCaptureFt = 450;
        /// <summary>The portals: each ring's radius, how long a pair stays open, and how long it takes to open fully (for the look).</summary>
        public const double PortalRadiusFt = 140, PortalLifeSec = 6, PortalOpenSec = 0.3;
        /// <summary>How far from you (when it opens) a portal's ends may be, and how far apart the two must be.</summary>
        public const double PortalReachFt = 15000, PortalMinGapFt = 2000;
        /// <summary>An end is kept at least this far above the ground.</summary>
        public const double PortalGroundFt = 300;
        /// <summary>The entry on your path opens this far ahead of you (in seconds of flight), so you go through it at once.</summary>
        public const double PortalLeadSec = 0.05;
        /// <summary>What each fighter carries with Arcade on.</summary>
        public const int Mines = 2, Blades = 1, Stealths = 1, Jammers = 1, Wells = 2, Portals = 2;

        /// <summary>
        /// Where a portal pair opened now by m, flying plan p, goes: the entry on m's path just ahead (facing along it) unless
        /// the plan places it (portalEntryFace set), the exit where the plan says (facing portalExitFace; none: m's way).
        /// Each end is kept within PortalReachFt of m and PortalGroundFt above the ground, and the exit at least
        /// PortalMinGapFt from the entry. The same on every machine.
        /// </summary>
        public static void PortalEnds(FlightModel m, TurnPlan p, Func<double, double, double> groundFt, out Vec3d a, out Vec3d fa, out Vec3d b, out Vec3d fb)
        {
            Vec3d at = m.state.pos;
            Vec3d Reach(Vec3d q) { Vec3d d = q - at; double l = d.Length; return l > PortalReachFt ? at + d * (PortalReachFt / l) : q; }
            Vec3d Lift(Vec3d q) { double g = groundFt != null ? groundFt(q.x, q.y) : 0; if (q.z < g + PortalGroundFt) q.z = g + PortalGroundFt; return q; }
            if (p.portalEntryFace.Length > 0.5) { a = Lift(Reach(p.portalEntry)); fa = p.portalEntryFace.Normalized; }
            else { a = at + m.state.vel * PortalLeadSec; fa = m.state.vel.Normalized; }
            fb = p.portalExitFace.Length > 0.5 ? p.portalExitFace.Normalized : m.Forward;
            b = Lift(Reach(p.portalExit));
            Vec3d gap = b - a;
            if (gap.Length < PortalMinGapFt)
            {
                Vec3d away = gap.Length > 1 ? gap.Normalized : fb;
                b = Lift(a + away * PortalMinGapFt);
            }
        }

        /// <summary>Where a gravity well thrown now from m, flying plan p, settles: ahead and into the turn.</summary>
        public static Vec3d WellLanding(FlightModel m, TurnPlan p)
        {
            Vec3d fwd = m.Forward, inward = m.Up;
            if (!p.hold && !p.HasMarks)
            {
                Vec3d across = p.aim - fwd * Vec3d.Dot(p.aim, fwd);   // the planned turn, square to the nose
                if (across.Length > 0.05) inward = across.Normalized;
            }
            return m.state.pos + fwd * WellAheadFt + inward * WellInFt;
        }

        /// <summary>
        /// The gravity well's pull at range r as a share of its strongest: growing linearly to the core's edge (like a planet's
        /// inside), then falling with the square of the range, and none past its reach.
        /// </summary>
        public static double WellFalloff(double r) => r >= WellReachFt ? 0 : r <= WellCoreFt ? Math.Max(0, r) / WellCoreFt : WellCoreFt * WellCoreFt / (r * r);

        /// <summary>Give a fighter its arcade load (the same numbers, changed in place, returned for chaining).</summary>
        public static CombatNumbers Arm(CombatNumbers c)
        {
            c.balloonMines = Mines; c.heatBlades = Blades; c.stealths = Stealths; c.jammers = Jammers; c.wells = Wells; c.portals = Portals;
            return c;
        }
    }

    /// <summary>The jammer's scrambling, the same on every machine (it rolls its own dice from the turn and the pilot).</summary>
    public static class Jam
    {
        /// <summary>Where a jammed pilot (me) thinks aircraft j is: moved and turned by an amount fixed for this turn.</summary>
        public static void Offset(int turn, int me, int j, out Vec3d shift, out double turnRad)
        {
            var dice = new Lcg(7919u * (uint)(turn + 1) + 104729u * (uint)(me + 1) + 1299709u * (uint)(j + 1));
            for (int k = 0; k < 3; k++) dice.Next();
            double a = 2 * Math.PI * dice.Next(), r = Specials.JamOffsetFt * (0.5 + 0.5 * dice.Next());
            shift = new Vec3d(Math.Cos(a) * r, Math.Sin(a) * r, (dice.Next() - 0.5) * r * 0.6);
            turnRad = (dice.Next() * 2 - 1) * Specials.JamTurnDeg * Math.PI / 180;
        }

        /// <summary>Scramble a pilot's private copy of the battle (index me in it is the jammed one): everyone on the other side moved and turned.</summary>
        public static void Scramble(Battle sub, int me, int turn, int[] originalIndex)
        {
            for (int j = 0; j < sub.Count; j++)
            {
                if (sub.side[j] == sub.side[me]) continue;
                Offset(turn, originalIndex != null ? originalIndex[me] : me, originalIndex != null ? originalIndex[j] : j, out var shift, out double rot);
                var st = sub.planes[j].state;
                double c = Math.Cos(rot), s = Math.Sin(rot);
                Vec3d Turn(Vec3d v) => new Vec3d(v.x * c - v.y * s, v.x * s + v.y * c, v.z);
                st.pos = st.pos + shift; st.vel = Turn(st.vel); st.up = Turn(st.up);
                sub.planes[j].state = st;
            }
        }
    }

    /// <summary>A balloon mine hanging in the air (see Specials). It hits like a round, once armed, and hits nothing of its own side.</summary>
    public class Mine : Bullet
    {
        /// <summary>The breeze it settles into: on the way it was let go at DriftFps, and up at MineRiseFps.</summary>
        public Vec3d drift;
        /// <summary>Where its slow sway and bob start, so two mines don't move in step.</summary>
        public double swayPhase;

        public static Mine Make(int owner, int side, Vec3d pos, Vec3d vel, double time, int seed = 0)
        {
            var flat = new Vec3d(vel.x, vel.y, 0);
            var along = flat.Length > 1e-6 ? flat * (1 / flat.Length) : new Vec3d(1, 0, 0);
            return new Mine
            {
                owner = owner, side = side, pos = pos, vel = vel, fireTime = time,
                damage = Specials.MineDamage, hitRadiusFt = Specials.FuseFt, dragPerSec = Specials.MineDragPerSec, lifeSec = Specials.LifeSec,
                drift = along * Specials.MineDriftFps + new Vec3d(0, 0, Specials.MineRiseFps),
                swayPhase = 1.7 * seed + 2.3 * owner,
            };
        }

        public bool Armed => age >= Specials.ArmSec;

        /// <summary>
        /// One step: it slows to a hang in about a second, then drifts on the breeze, swaying side to side and bobbing a
        /// little (it is a balloon: no falling).
        /// </summary>
        public void Step(double dt)
        {
            double decay = System.Math.Exp(-dragPerSec * dt);
            var flat = new Vec3d(drift.x, drift.y, 0);
            var side = flat.Length > 1e-6 ? new Vec3d(-flat.y, flat.x, 0) * (1 / flat.Length) : new Vec3d(0, 1, 0);
            double a = age + swayPhase;
            Vec3d want = drift + side * (Specials.MineSwayFps * System.Math.Sin(a * 0.45)) + new Vec3d(0, 0, Specials.MineBobFps * System.Math.Sin(a * 1.1));
            Vec3d v0 = vel;
            vel = vel * decay + want * (1 - decay);
            pos = pos + (v0 + vel) * (dt / 2);
            age += dt;
        }
    }

    /// <summary>
    /// A gravity well thrown ahead (see Specials): it slows to a hang and, once open, pulls planes, missiles and rounds toward
    /// its middle. It hits nothing and does no damage; it rides in the battle's bullets so a clone, a subset, a swap and the
    /// wire carry it like a mine.
    /// </summary>
    public class Well : Bullet
    {
        public static Well Make(int owner, int side, Vec3d pos, Vec3d vel, double time) => new Well
        {
            owner = owner, side = side, pos = pos, vel = vel, fireTime = time,
            damage = 0, hitRadiusFt = 0, dragPerSec = Specials.WellDragPerSec, lifeSec = Specials.WellLifeSec,
        };

        /// <summary>How hard it pulls now, 0 to 1: shut until WellArmSec, opening over a quarter second, fading out at the end.</summary>
        public double Strength
        {
            get
            {
                if (age < Specials.WellArmSec || age >= lifeSec) return 0;
                double open = Math.Min(1, (age - Specials.WellArmSec) / 0.25);
                double fade = Math.Min(1, (lifeSec - age) / Specials.WellFadeSec);
                return Math.Max(0, Math.Min(open, fade));
            }
        }

        public bool Open => Strength > 0;

        /// <summary>The pull on something at p, ft/s², for a strongest pull of peakG.</summary>
        public Vec3d Pull(Vec3d p, double peakG)
        {
            double s = Strength;
            if (s <= 0) return Vec3d.Zero;
            Vec3d r = pos - p;
            double d = r.Length;
            if (d < 1e-6) return Vec3d.Zero;
            return r * (s * peakG * Atmosphere.GFtps2 * Specials.WellFalloff(d) / d);
        }

        /// <summary>One step: it coasts on, slowing to a hang (no gravity on it: it holds itself up).</summary>
        public void Step(double dt)
        {
            Vec3d v0 = vel;
            vel = vel * Math.Exp(-dragPerSec * dt);
            pos = pos + (v0 + vel) * (dt / 2);
            age += dt;
        }
    }

    /// <summary>
    /// A linked pair of portal rings (see Specials): this one at pos facing faceA, its twin at posB facing faceB. Anything
    /// that crosses either ring's disc comes out of the other, its place, speed and heading turned from one ring's frame to
    /// the other's (flown through A along faceA, you come out of B along faceB; backwards through A, backwards out of B).
    /// It hangs still, hits nothing and does no damage; it rides in the battle's bullets so a clone, a subset, a swap and
    /// the wire carry it like a mine.
    /// </summary>
    public class Portal : Bullet
    {
        public Vec3d posB, faceA, faceB;
        public double radiusFt = Specials.PortalRadiusFt;

        public static Portal Make(int owner, int side, Vec3d a, Vec3d fa, Vec3d b, Vec3d fb, double time) => new Portal
        {
            owner = owner, side = side, pos = a, faceA = fa.Normalized, posB = b, faceB = fb.Normalized, fireTime = time,
            damage = 0, hitRadiusFt = 0, dragPerSec = 0, lifeSec = Specials.PortalLifeSec,
        };

        public bool Open => age < lifeSec;

        /// <summary>How open it looks, 0 to 1: growing over PortalOpenSec, closing over the last half second.</summary>
        public static double Openness(double age) => age < 0 || age >= Specials.PortalLifeSec ? 0
            : Math.Max(0, Math.Min(1, Math.Min(age / Specials.PortalOpenSec + 0.2, (Specials.PortalLifeSec - age) / 0.5)));

        /// <summary>A ring's frame: its face, and right and up square to it (up as near the sky's as it can be).</summary>
        public static void Frame(Vec3d f, out Vec3d right, out Vec3d up)
        {
            Vec3d refUp = Math.Abs(f.z) > 0.98 ? new Vec3d(0, 1, 0) : Vec3d.Up;
            right = Vec3d.Cross(f, refUp).Normalized;
            up = Vec3d.Cross(right, f).Normalized;
        }

        /// <summary>A direction turned from one ring's frame to the other's (fromA: from A's to B's).</summary>
        public Vec3d MapDir(Vec3d v, bool fromA)
        {
            Vec3d f0 = fromA ? faceA : faceB, f1 = fromA ? faceB : faceA;
            Frame(f0, out var r0, out var u0);
            Frame(f1, out var r1, out var u1);
            return f1 * Vec3d.Dot(v, f0) + r1 * Vec3d.Dot(v, r0) + u1 * Vec3d.Dot(v, u0);
        }

        /// <summary>A point turned from one ring to the other (fromA: from A's to B's).</summary>
        public Vec3d MapPoint(Vec3d p, bool fromA) => fromA ? posB + MapDir(p - pos, true) : pos + MapDir(p - posB, false);

        /// <summary>Does the path from p0 to p1 go through one of the rings? fromA says which (A, to come out of B).</summary>
        public bool Crosses(Vec3d p0, Vec3d p1, out bool fromA)
        {
            fromA = true;
            if (!Open) return false;
            if (Through(pos, faceA, p0, p1)) return true;
            fromA = false;
            return Through(posB, faceB, p0, p1);
        }

        bool Through(Vec3d c, Vec3d f, Vec3d p0, Vec3d p1)
        {
            double d0 = Vec3d.Dot(p0 - c, f), d1 = Vec3d.Dot(p1 - c, f);
            if (d0 > 0 && d1 > 0 || d0 < 0 && d1 < 0 || d0 == d1) return false;
            if (d0 == 0 && d1 == 0) return false;
            Vec3d x = p0 + (p1 - p0) * (d0 / (d0 - d1));
            return (x - c).Length <= radiusFt;
        }

        /// <summary>One step: it hangs where it is and ages.</summary>
        public void Step(double dt) => age += dt;
    }
}
