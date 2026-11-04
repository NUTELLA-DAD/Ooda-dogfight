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
    /// Heat blade: the airframe glows red hot for the whole turn and the guns stay cold. Any enemy you pass within
    /// BladeReachFt of is cut in half (down at once) and you fly on; two blades meeting both go down. It makes you the hottest
    /// thing in the sky (BladeHeat on top of the engine): every heat-seeker locks you from farther and flares hardly help.
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
