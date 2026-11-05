using System;
using Ooda.Flight.Core;

namespace Ooda.Turn.Core
{
    /// <summary>
    /// The arcade specials, a game liberty like the missiles: each fighter carries a few (CombatNumbers.balloonMines,
    /// heatBlades, stealths) and plans them like the rocket, at the start of a turn.
    ///
    /// Balloon mine: let go behind you, it slows to a hang in a second and drifts up a little. It arms after ArmSec and goes
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
    /// </summary>
    public static class Specials
    {
        public const double ArmSec = 0.6, LifeSec = 15, FuseFt = 70, MineDamage = 10, MineDragPerSec = 2.5, MineRiseFps = 4;
        public const double BladeReachFt = 45, BladeHeat = 4;
        public const double CloakSec = 6;
        /// <summary>The jammer: how far it reaches, and how long the ones it catches see scrambled ghosts (two planning phases).</summary>
        public const double JamRangeFt = 9000, JamSec = 6.5;
        /// <summary>How far off a jammed pilot's picture of the others is: their place by up to this, their heading by up to JamTurnDeg.</summary>
        public const double JamOffsetFt = 900, JamTurnDeg = 70;
        /// <summary>What each fighter carries with Arcade on.</summary>
        public const int Mines = 2, Blades = 1, Stealths = 1, Jammers = 1;

        /// <summary>Give a fighter its arcade load (the same numbers, changed in place, returned for chaining).</summary>
        public static CombatNumbers Arm(CombatNumbers c)
        {
            c.balloonMines = Mines; c.heatBlades = Blades; c.stealths = Stealths; c.jammers = Jammers;
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
        public static Mine Make(int owner, int side, Vec3d pos, Vec3d vel, double time) => new Mine
        {
            owner = owner, side = side, pos = pos, vel = vel, fireTime = time,
            damage = Specials.MineDamage, hitRadiusFt = Specials.FuseFt, dragPerSec = Specials.MineDragPerSec, lifeSec = Specials.LifeSec,
        };

        public bool Armed => age >= Specials.ArmSec;

        /// <summary>One step: it slows to the air's speed and drifts up a little (it is a balloon: no falling).</summary>
        public void Step(double dt)
        {
            double decay = System.Math.Exp(-dragPerSec * dt);
            Vec3d v0 = vel, drift = new Vec3d(0, 0, Specials.MineRiseFps);
            vel = vel * decay + drift * (1 - decay);
            pos = pos + (v0 + vel) * (dt / 2);
            age += dt;
        }
    }
}
