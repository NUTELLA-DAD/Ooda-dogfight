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
    ///
    /// Blink: at any moment of the turn you plan (TurnPlan.blinkAt) you vanish and reappear BlinkFt farther along your
    /// planned path, flying on as if you had flown it (the same speed, heading and bank you would have had there), in a
    /// single step. The other side's rounds and missiles already in the air inside BlinkShakeFt fly on at the empty sky
    /// and can't hit you any more (a missile on you loses you) (an enemy radar tracking you starts its lock over). The hop is cut short
    /// before it would reach the ground or an obstacle.
    ///
    /// Ricochet shield: raised at the start of a turn you plan (TurnPlan.mirror), a mirror bubble round the plane for
    /// MirrorSec. Every round (a fighter's packet or flak) that would strike it bounces off instead, mirrored about the
    /// back down the line it came in on, splayed by how far off the middle it struck (MirrorSpread: one dead on comes
    /// straight back, one at the edge a few degrees off) at MirrorKeep of its speed
    /// against the plane. A bounced round is the shield's now: it carries MirrorDamage of its punch, flies on to hit anyone on
    /// the other side of the shield (the shooter too, if it comes back his way, and a kill is the shield's), and can bounce off
    /// another shield. Missiles, mines and blades still bite, and your own guns fire out through it.
    ///
    /// Grapple: from the moment of the turn you plan (TurnPlan.grappleAt) a hook flies at the first enemy inside GrappleReachFt
    /// and GrappleConeDeg of your nose (the one you are after first) and holds him on a line as long as the gap was, reeling in
    /// at GrappleReelFps to GrappleMinFt, for GrappleLifeSec (two turns) or until you cut it (plan it again with the line out).
    /// The line only pulls, never pushes: slack while you close, and once taut a stiff, springy rope (GrappleK, damped by
    /// GrappleC on the rate you pull apart) shared between you by weight, so a heavy plane drags a light one round. Flown past
    /// or round him it is a pivot: you swing far tighter than your wing could turn you, and he is dragged the other way. It
    /// holds with up to GrappleHoldG between you (past that its brake pays line out) and you both feel it (FlightModel.Tug):
    /// a hard swing greys you out. Taut with you pulling apart faster than GrappleSnapFps (flying opposite ways) it goes
    /// bar-taut and snaps in a violent yank: up to GrappleJoltMaxFps of your speed apart is
    /// taken out at once, shared by weight, and each end takes the jolt: G-seconds enough to knock a pilot cold
    /// (GrappleStressPerG) and wear on every system (GrappleWearPerFps), worst on the lighter plane.
    ///
    /// Snow cloud: let go at any moment of the turn you plan (TurnPlan.snowAt), it billows out round you, its middle
    /// SnowBehindFt behind, from SnowStartFt to SnowRadiusFt across in SnowGrowSec, slowing to a hang, and hangs there for
    /// SnowLifeSec, thinning over its last SnowFadeSec. Every plane inside it (both sides, you too) picks up rime ice
    /// (Systems.rime) at up to RimePerSec, most in its middle and none at its edge: you fly out through the back half of
    /// yours, a tailer through the whole of it. Rime costs lift, speed, climb and roll until it melts off, but while it lies
    /// on the airframe (past Systems.RimeColdAt) no heat-seeker can lock the plane, and one already on its way shatters on
    /// it, as on the Ice pick. It hits nothing and does no damage.
    ///
    /// Gravity flip: set off at the start of the turn, it opens a bubble of upside-down gravity FlipReachFt round where you
    /// are, fixed in the sky, for FlipSec (this turn and the next). Inside it everything falls up: every plane but yours
    /// (both sides, wrecks too) is pushed up at FlipG, so gravity pulls up at 1 G instead of down at 1 G, and rounds,
    /// missiles, flares and chaff arc up instead of dropping. You keep your weight: the tailer behind you floats up off your
    /// line, a diver overshoots upward, and a wreck falls into the sky. Full strength out to FlipCoreFt, fading to nothing at
    /// its edge. It does no damage itself.
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
        /// <summary>
        /// The blink: how far along the planned path it jumps (the path flown on ahead, cut short before the ground or an
        /// obstacle, and never under BlinkFloorFt over the ground), and how close a heat-seeker or radar missile on you must
        /// be to lose you (it closes on where you were).
        /// </summary>
        public const double BlinkFt = 1500, BlinkFloorFt = 150, BlinkShakeFt = 5000;
        /// <summary>The ricochet shield: how long it stays up, the share of a round's speed (against the plane) and punch a bounce keeps.</summary>
        public const double MirrorSec = 3, MirrorKeep = 0.7, MirrorDamage = 0.8;
        /// <summary>
        /// The bubble is a retro-reflector (like a cat's eye): a bounce goes back down the line the round came in on, splayed
        /// out by how far off the middle it struck, up to about this share of a radian at the very edge (7 degrees). So a
        /// close shooter gets a good part of his own burst back, and the rest sprays round him.
        /// </summary>
        public const double MirrorSpread = 0.12;
        /// <summary>The snow cloud: where its middle starts (behind you), how big it starts and grows, and how fast it slows to a hang.</summary>
        public const double SnowBehindFt = 120, SnowStartFt = 200, SnowRadiusFt = 650, SnowGrowSec = 1.2, SnowDragPerSec = 1.5;
        /// <summary>The snow cloud hangs for SnowLifeSec (four turns), thinning over its last SnowFadeSec.</summary>
        public const double SnowLifeSec = 12, SnowFadeSec = 2;
        /// <summary>The rime a plane picks up each second in the middle of a full snow cloud (less toward its edge).</summary>
        public const double RimePerSec = 0.75;
        /// <summary>The gravity flip's bubble: full strength out to FlipCoreFt, fading to nothing at FlipReachFt.</summary>
        public const double FlipCoreFt = 1500, FlipReachFt = 2000;
        /// <summary>The gravity flip: open for FlipSec (two turns), opening over FlipOpenSec and fading over its last FlipFadeSec.</summary>
        public const double FlipSec = 6, FlipOpenSec = 0.25, FlipFadeSec = 0.5;
        /// <summary>The gravity flip's push up, in G: 2 turns 1 G down into 1 G up.</summary>
        public const double FlipG = 2;
        /// <summary>The portals: each ring's radius, how long a pair stays open, and how long it takes to open fully (for the look).</summary>
        public const double PortalRadiusFt = 140, PortalLifeSec = 6, PortalOpenSec = 0.3;
        /// <summary>How far from you (when it opens) a portal's ends may be, and how far apart the two must be.</summary>
        public const double PortalReachFt = 15000, PortalMinGapFt = 2000;
        /// <summary>An end is kept at least this far above the ground.</summary>
        public const double PortalGroundFt = 300;
        /// <summary>The entry on your path opens this far ahead of you (in seconds of flight), so you go through it at once.</summary>
        public const double PortalLeadSec = 0.05;
        /// <summary>What each fighter carries with Arcade on.</summary>
        public const int Mines = 2, Blades = 1, Stealths = 1, Jammers = 1, Wells = 2, SlowBombs = 2, Blinks = 2, Mirrors = 2, Grapples = 2, SnowClouds = 2, Flips = 2, Portals = 2;
        /// <summary>The time slow bomb's throw: at the nearest threat within SlowReachFt, flying SlowFlySec to burst (none in reach: SlowAheadFt ahead).</summary>
        public const double SlowReachFt = 5000, SlowFlySec = 0.6, SlowAheadFt = 1200, SlowClearFt = 200;
        /// <summary>The time slow bubble: SlowRadiusFt round its middle (full slow inside SlowCoreShare of it, easing to none at the edge), for SlowLifeSec after it bursts, fading over SlowFadeSec.</summary>
        public const double SlowRadiusFt = 1200, SlowCoreShare = 0.85, SlowLifeSec = 6, SlowFadeSec = 0.6;
        /// <summary>How fast time runs for everyone inside a time slow bubble, as a share of the clock.</summary>
        public const double SlowFactor = 0.5;
        /// <summary>The grapple's reach: an enemy within GrappleReachFt and GrappleConeDeg of the nose can be hooked.</summary>
        public const double GrappleReachFt = 2500, GrappleConeDeg = 50;
        /// <summary>The line: it holds GrappleLifeSec, reeling in at GrappleReelFps to no shorter than GrappleMinFt.</summary>
        public const double GrappleLifeSec = 6, GrappleReelFps = 80, GrappleMinFt = 350;
        /// <summary>The rope's spring (per s², on the stretch) and damping (per s, on the rate the two pull apart).</summary>
        public const double GrappleK = 30, GrappleC = 4;
        /// <summary>
        /// The line holds them with up to GrappleHoldG between them (a brake on its drum pays line out past that, so a hard
        /// swing drags rather than breaks); but taut with the two pulling apart faster than GrappleSnapFps (opposite ways) it snaps.
        /// </summary>
        public const double GrappleHoldG = 12, GrappleSnapFps = 420;
        /// <summary>The snap's yank: at most this much of the speed apart taken out at once, ft/s.</summary>
        public const double GrappleJoltMaxFps = 320;
        /// <summary>What the yank does to each end: G-seconds of stress per G-second of jolt, and wear on every system per ft/s.</summary>
        public const double GrappleStressPerG = 1.2, GrappleWearPerFps = 0.0025;

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
                    if (b.side[j] == b.side[i] || b.Out(j) || !b.Sees(i, j)) continue;
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

        /// <summary>
        /// A round strikes a ricochet shield: rel is where it is against the plane (ft, from its middle, at the sphere of radius
        /// reach), relVel its velocity against the plane. The new relative velocity: back the way it came, splayed by how far
        /// off the middle it struck (see MirrorSpread), at MirrorKeep of its speed (a round already leaving keeps its line);
        /// normal the outward normal of the bubble where it struck.
        /// </summary>
        public static Vec3d Bounce(Vec3d rel, Vec3d relVel, out Vec3d normal)
        {
            double r = rel.Length, speed = relVel.Length;
            normal = r > 1e-6 ? rel * (1 / r) : (speed > 1e-9 ? relVel * (-1 / speed) : new Vec3d(0, 0, 1));
            if (speed < 1e-9 || Vec3d.Dot(relVel, normal) >= 0) return relVel * MirrorKeep;   // already leaving: it keeps its line
            Vec3d back = relVel * (-1 / speed);
            Vec3d off = normal - back * Vec3d.Dot(normal, back);   // how far off the middle it struck, square to its line (0 to 1)
            return (back + off * MirrorSpread).Normalized * (speed * MirrorKeep);
        }

        /// <summary>Give a fighter its arcade load (the same numbers, changed in place, returned for chaining).</summary>
        public static CombatNumbers Arm(CombatNumbers c)
        {
            c.balloonMines = Mines; c.heatBlades = Blades; c.stealths = Stealths; c.jammers = Jammers; c.wells = Wells; c.slowBombs = SlowBombs; c.blinks = Blinks; c.mirrors = Mirrors; c.grapples = Grapples; c.lanes = JetStream.Lanes; c.flips = Flips; c.portals = Portals;
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
    /// <summary>
    /// A grapple's line (see Specials): owner hooked target and the line between them pulls them together whenever it is
    /// taut. It rides in the battle's bullets (at the owner's end, hitting nothing) so a clone, a subset, a swap and the wire
    /// carry it like a mine.
    /// </summary>
    public class Tether : Bullet
    {
        /// <summary>The shooter recorded on a snap's hits: no one (so the gun previews never count a yank as a hit).</summary>
        public const int Nobody = -100000;

        public int target = -1;
        /// <summary>How long the line is now, ft: the gap when it hooked, reeling in.</summary>
        public double lengthFt;
        /// <summary>Last step's pull along it, in G on the lighter of the two (0 slack).</summary>
        public double tensionG;

        public static Tether Make(int owner, int side, int target, Vec3d pos, double lengthFt, double time) => new Tether
        {
            owner = owner, side = side, target = target, pos = pos, fireTime = time, lengthFt = lengthFt,
            damage = 0, hitRadiusFt = 0, lifeSec = Specials.GrappleLifeSec,
        };

        /// <summary>
        /// One step of the line between a (its owner) and b (the one hooked): reel in, then if taut pull both by weight
        /// (FlightModel.Tug). True when it snaps instead: joltA and joltB are the sudden changes in speed the yank gives each
        /// (the caller applies them, with the harm). It does not move either plane when it snaps.
        /// </summary>
        public bool Step(FlightModel a, FlightModel b, double dt, out Vec3d joltA, out Vec3d joltB)
        {
            joltA = joltB = Vec3d.Zero;
            age += dt;
            pos = a.state.pos; vel = a.state.vel;
            lengthFt = Math.Max(Specials.GrappleMinFt, lengthFt - Specials.GrappleReelFps * dt);
            Vec3d r = b.state.pos - a.state.pos;
            double d = r.Length;
            tensionG = 0;
            if (d <= lengthFt || d < 1e-6) return false;   // slack
            Vec3d u = r * (1 / d);
            double stretch = d - lengthFt, apart = Vec3d.Dot(b.state.vel - a.state.vel, u);
            double pull = Math.Max(0, Specials.GrappleK * stretch + Specials.GrappleC * apart);   // ft/s² between them
            double ma = Math.Max(1, a.numbers.weightLb), mb = Math.Max(1, b.numbers.weightLb);
            double shareA = mb / (ma + mb), shareB = ma / (ma + mb);
            if (apart > Specials.GrappleSnapFps)
            {
                tensionG = Specials.GrappleHoldG * 2 * Math.Max(shareA, shareB);   // for the picture: bar-taut as it parts
                double dv = Math.Min(Math.Max(0, apart), Specials.GrappleJoltMaxFps);
                joltA = u * (dv * shareA);
                joltB = u * (-dv * shareB);
                alive = false;
                return true;
            }
            double hold = Specials.GrappleHoldG * Atmosphere.GFtps2;
            if (pull > hold)
            {
                pull = hold;
                lengthFt = Math.Max(lengthFt, d - Math.Max(0, hold - Specials.GrappleC * apart) / Specials.GrappleK);   // the brake pays out
            }
            tensionG = pull * Math.Max(shareA, shareB) / Atmosphere.GFtps2;
            a.Tug(u * (pull * shareA), dt);
            b.Tug(u * (-pull * shareB), dt);
            return false;
        }
    }

    /// <summary>
    /// A snow cloud let go round you (see Specials): it billows out, slows to a hang and ices every plane that flies
    /// through it. It hits nothing and does no damage; it rides in the battle's bullets so a clone, a subset, a swap and
    /// the wire carry it like a mine.
    /// </summary>
    public class SnowCloud : Bullet
    {
        public static SnowCloud Make(int owner, int side, Vec3d pos, Vec3d vel, double time) => new SnowCloud
        {
            owner = owner, side = side, pos = pos, vel = vel, fireTime = time,
            damage = 0, hitRadiusFt = 0, dragPerSec = Specials.SnowDragPerSec, lifeSec = Specials.SnowLifeSec,
        };

        /// <summary>How far across it is now: billowing out from SnowStartFt to SnowRadiusFt.</summary>
        public double RadiusFt => RadiusAt(age);

        public static double RadiusAt(double age)
        {
            double f = Math.Max(0, Math.Min(1, age / Specials.SnowGrowSec));
            f = 1 - (1 - f) * (1 - f);   // fast at first, easing out
            return Specials.SnowStartFt + (Specials.SnowRadiusFt - Specials.SnowStartFt) * f;
        }

        /// <summary>How thick it is now, 0 to 1: full from the start, thinning over its last SnowFadeSec.</summary>
        public double Thickness => ThicknessAt(age, lifeSec);

        public static double ThicknessAt(double age, double life) => age >= life ? 0 : Math.Max(0, Math.Min(1, (life - age) / Specials.SnowFadeSec));

        /// <summary>How dense it is at p, 0 to 1: thickest in the middle, none at its edge.</summary>
        public double Density(Vec3d p)
        {
            double r = RadiusFt, d = (p - pos).Length;
            if (d >= r) return 0;
            double u = d / r;
            return Thickness * (1 - u * u);
        }

        /// <summary>One step: it coasts on, slowing to a hang (no gravity on it: a cloud floats).</summary>
        public void Step(double dt)
        {
            Vec3d v0 = vel;
            vel = vel * Math.Exp(-dragPerSec * dt);
            pos = pos + (v0 + vel) * (dt / 2);
            age += dt;
        }
    }

    /// <summary>
    /// A gravity flip's bubble (see Specials): fixed in the sky where it was set off, it pushes everything inside it up, all
    /// but the plane that set it off. It hits nothing and does no damage; it rides in the battle's bullets so a clone, a
    /// subset, a swap and the wire carry it like a well.
    /// </summary>
    public class Flip : Bullet
    {
        public static Flip Make(int owner, int side, Vec3d pos, double time) => new Flip
        {
            owner = owner, side = side, pos = pos, vel = Vec3d.Zero, fireTime = time,
            damage = 0, hitRadiusFt = 0, dragPerSec = 0, lifeSec = Specials.FlipSec,
        };

        /// <summary>How hard it flips now, 0 to 1: opening over FlipOpenSec, fading over the last FlipFadeSec.</summary>
        public double Strength
        {
            get
            {
                if (age >= lifeSec) return 0;
                double open = Math.Min(1, age / Specials.FlipOpenSec);
                double fade = Math.Min(1, (lifeSec - age) / Specials.FlipFadeSec);
                return Math.Max(0, Math.Min(open, fade));
            }
        }

        public bool Open => Strength > 0;

        /// <summary>How much of its flip reaches p, 0 to 1: all of it inside the core, fading to nothing at the edge.</summary>
        public static double Share(double r) => r <= Specials.FlipCoreFt ? 1 : r >= Specials.FlipReachFt ? 0 : (Specials.FlipReachFt - r) / (Specials.FlipReachFt - Specials.FlipCoreFt);

        /// <summary>Inside its reach (whatever its strength).</summary>
        public bool Holds(Vec3d p) => (p - pos).Length < Specials.FlipReachFt;

        /// <summary>Its push up on something at p, ft/s² (none on the plane who, if that is the one that set it off; -1 for a round).</summary>
        public Vec3d Lift(Vec3d p, int who = -1)
        {
            if (who >= 0 && who == owner) return Vec3d.Zero;
            double s = Strength * Share((p - pos).Length);
            return s <= 0 ? Vec3d.Zero : new Vec3d(0, 0, s * Specials.FlipG * Atmosphere.GFtps2);
        }

        /// <summary>One step: it stays where it was set off and only ages.</summary>
        public void Step(double dt) => age += dt;
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
