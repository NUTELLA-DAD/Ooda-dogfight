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
    /// Sonic boom (the heat blade's second half): while the blade is lit and you fly straight (the path turning under
    /// BoomStraightDegPerSec), it pushes you on (BoomPushG) and the glow builds from red to white hot; after BoomChargeSec of
    /// straight flight (turning pauses it), or at once in a jet already past BoomMach, you break the barrier with a boom.
    /// From then to the end of the turn the blade is white hot and cuts from BoomBladeReachFt, and the shock cone trailing
    /// behind you shoves every enemy within BoomReachFt that is level with or behind you (more than BoomClearDeg off the nose) off
    /// course, once a turn each: up to BoomShoveFps straight away from you, less the farther off. The one dead ahead you
    /// are cutting is never pushed out of the way; the ones beside and behind are scattered. Your own side is spared.
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
    /// Time slow bomb: thrown at the moment you plan (TurnPlan.slowAt) at the nearest threat within SlowReachFt (a missile
    /// tracking you first, else the nearest enemy plane; none, it bursts SlowAheadFt ahead), it flies SlowFlySec and bursts
    /// where they will be into a bubble SlowRadiusFt across the middle, which hangs there for SlowLifeSec (pushed out past them
    /// so it bursts at least SlowClearFt beyond its edge from you). Inside it time runs at SlowFactor for everyone, both sides,
    /// you too: planes, rounds, missiles and flares all move (and turn, burn and count down) at that share of the clock, and
    /// come back to full speed as they leave. It does no damage: a tailer caught in it falls back and its stream crawls, a
    /// missile in it loses seconds, a target ahead in it is a sitting duck (until you fly in after it).
    /// </summary>
    public static class Specials
    {
        public const double ArmSec = 0.6, LifeSec = 15, FuseFt = 70, MineDamage = 10, MineDragPerSec = 2.5, MineRiseFps = 2;
        /// <summary>The mine's drift once it hangs: the breeze along the way it was let go, a slow sway across it and a bob, in ft/s.</summary>
        public const double MineDriftFps = 12, MineSwayFps = 5, MineBobFps = 2;
        public const double BladeReachFt = 45, BladeHeat = 4;
        /// <summary>The sonic boom: the straight flight that builds it, the push while it builds, and the Mach that sets it off at once.</summary>
        public const double BoomChargeSec = 1.5, BoomStraightDegPerSec = 10, BoomPushG = 0.8, BoomMach = 0.95;
        /// <summary>The sonic boom's shock cone: how far it shoves, how hard at its closest, and how far off the nose it clears.</summary>
        public const double BoomReachFt = 2000, BoomShoveFps = 200, BoomClearDeg = 80;
        /// <summary>The white-hot blade's reach once the barrier is broken.</summary>
        public const double BoomBladeReachFt = 90;
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
        /// <summary>What each fighter carries with Arcade on.</summary>
        public const int Mines = 2, Blades = 1, Stealths = 1, Jammers = 1, Wells = 2, SlowBombs = 2;
        /// <summary>The time slow bomb's throw: at the nearest threat within SlowReachFt, flying SlowFlySec to burst (none in reach: SlowAheadFt ahead).</summary>
        public const double SlowReachFt = 5000, SlowFlySec = 0.6, SlowAheadFt = 1200, SlowClearFt = 200;
        /// <summary>The time slow bubble: SlowRadiusFt round its middle (full slow inside SlowCoreShare of it, easing to none at the edge), for SlowLifeSec after it bursts, fading over SlowFadeSec.</summary>
        public const double SlowRadiusFt = 1200, SlowCoreShare = 0.85, SlowLifeSec = 6, SlowFadeSec = 0.6;
        /// <summary>How fast time runs for everyone inside a time slow bubble, as a share of the clock.</summary>
        public const double SlowFactor = 0.5;

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
        /// Where a time slow bomb thrown now by aircraft i bursts: on the nearest threat within reach, led by the bomb's flight
        /// (a missile tracking i first, else the nearest enemy plane it can see), else straight ahead.
        /// </summary>
        public static Vec3d SlowLanding(Battle b, int i)
        {
            var m = b.planes[i];
            Vec3d me = m.state.pos;
            double best = SlowReachFt;
            Vec3d? at = null;
            foreach (var x in b.bullets)
                if (x is Missile ms && ms.side != b.side[i] && ms.target == i && ms.Tracking)
                {
                    double d = (ms.pos - me).Length;
                    if (d < best) { best = d; at = ms.pos + ms.vel * SlowFlySec; }
                }
            if (at == null)
                for (int j = 0; j < b.Count; j++)
                {
                    if (b.side[j] == b.side[i] || b.Out(j) || b.Cloaked(j)) continue;
                    double d = (b.planes[j].state.pos - me).Length;
                    if (d < best) { best = d; at = b.planes[j].state.pos + b.planes[j].state.vel * SlowFlySec; }
                }
            Vec3d c = at ?? me + m.Forward * SlowAheadFt + m.state.vel * SlowFlySec;
            // it slows the thrower too: burst it past the threat, clear of where the thrower will be
            Vec3d meThen = me + m.state.vel * SlowFlySec, off = c - meThen;
            double need = SlowRadiusFt + SlowClearFt, d0 = off.Length;
            if (d0 < need) c = meThen + (d0 > 1 ? off * (1 / d0) : -m.Forward) * need;
            return c;
        }

        /// <summary>
        /// The gravity well's pull at range r as a share of its strongest: growing linearly to the core's edge (like a planet's
        /// inside), then falling with the square of the range, and none past its reach.
        /// </summary>
        public static double WellFalloff(double r) => r >= WellReachFt ? 0 : r <= WellCoreFt ? Math.Max(0, r) / WellCoreFt : WellCoreFt * WellCoreFt / (r * r);

        /// <summary>
        /// The sonic boom's shove on a plane at rel from the booming one (flying along fwd): a change of velocity straight
        /// away from it, strongest close in and none past BoomReachFt or ahead of the nose. Zero when it is spared.
        /// </summary>
        public static Vec3d BoomShove(Vec3d rel, Vec3d fwd)
        {
            double d = rel.Length;
            if (d >= BoomReachFt || d < 1e-6) return Vec3d.Zero;
            if (Vec3d.Dot(rel, fwd) / d > Math.Cos(BoomClearDeg * Math.PI / 180)) return Vec3d.Zero;   // ahead of the nose: the cut, not the shove
            return rel * (BoomShoveFps * (1 - d / BoomReachFt) / d);
        }

        /// <summary>Give a fighter its arcade load (the same numbers, changed in place, returned for chaining).</summary>
        public static CombatNumbers Arm(CombatNumbers c)
        {
            c.balloonMines = Mines; c.heatBlades = Blades; c.stealths = Stealths; c.jammers = Jammers; c.wells = Wells; c.slowBombs = SlowBombs;
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
    /// A time slow bomb (see Specials): thrown at a threat, it flies SlowFlySec, stops dead and bursts into a bubble where time
    /// runs at SlowFactor for everyone. It hits nothing and does no damage; it rides in the battle's bullets so a clone,
    /// a subset, a swap and the wire carry it like a mine.
    /// </summary>
    public class TimeBubble : Bullet
    {
        public static TimeBubble Make(int owner, int side, Vec3d from, Vec3d to, double time) => new TimeBubble
        {
            owner = owner, side = side, pos = from, vel = (to - from) * (1 / Specials.SlowFlySec), fireTime = time,
            damage = 0, hitRadiusFt = 0, dragPerSec = 0, lifeSec = Specials.SlowFlySec + Specials.SlowLifeSec,
        };

        /// <summary>Burst: the bomb has arrived and the bubble is up.</summary>
        public bool Burst => age >= Specials.SlowFlySec;

        /// <summary>How strong the bubble is now, 0 to 1: shut in flight, snapping open over 0.15 s, fading out at the end.</summary>
        public double Strength
        {
            get
            {
                if (age < Specials.SlowFlySec || age >= lifeSec) return 0;
                double open = Math.Min(1, (age - Specials.SlowFlySec) / 0.15);
                double fade = Math.Min(1, (lifeSec - age) / Specials.SlowFadeSec);
                return Math.Max(0, Math.Min(open, fade));
            }
        }

        /// <summary>The share of the clock anything at p runs at (either side): SlowFactor deep inside, easing to 1 at the edge.</summary>
        public double Clock(Vec3d p)
        {
            double st = Strength;
            if (st <= 0) return 1;
            double d = (p - pos).Length, r = Specials.SlowRadiusFt, core = r * Specials.SlowCoreShare;
            if (d >= r) return 1;
            double inside = d <= core ? 1 : (r - d) / (r - core);
            return 1 - (1 - Specials.SlowFactor) * st * inside;
        }

        /// <summary>One step: it flies straight to where it bursts, then hangs there.</summary>
        public void Step(double dt)
        {
            double before = age;
            age += dt;
            if (before >= Specials.SlowFlySec) { vel = Vec3d.Zero; return; }
            double fly = Math.Min(dt, Specials.SlowFlySec - before);
            pos = pos + vel * fly;
            if (age >= Specials.SlowFlySec) vel = Vec3d.Zero;
        }
    }
}
