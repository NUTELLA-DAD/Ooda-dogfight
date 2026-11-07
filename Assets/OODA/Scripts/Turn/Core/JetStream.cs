using System;
using Ooda.Flight.Core;

namespace Ooda.Turn.Core
{
    /// <summary>
    /// The jet stream (a loadout pick, Kit.Jet): a wind lane laid at a moment of the turn you plan (TurnPlan.laneAt), a
    /// straight tube of air LengthFt long and RadiusFt round that blows at WindFps (about 180 kt) one way for LifeSec (three
    /// turns). Everything inside it rides the wind: planes (both sides, you too), rounds, missiles, mines, flares and chaff are
    /// carried along with it over the ground on top of their own flight. It is wind, not thrust: a plane's airspeed and energy
    /// don't change, only where it goes, so riding it is a highway (180 kt more over the ground, for nothing), flying against
    /// it is wading upstream, and a stream of rounds fired across it is blown sideways off its line.
    ///
    /// It points two ways. Laid ahead (the default), it starts BackFt behind you and blows along your plan's arrow (where the
    /// nose is to be at the turn's end), so you are in it at once and it carries you the way you are turning: run, close in, or
    /// slide round a turn. Laid back (TurnPlan.laneBack), it starts GapFt behind you and runs back down your path, blowing
    /// back at whoever is on your tail: wading upstream he falls back 300 ft every second he is in it (most of a turn, until
    /// he flies out of its front end), so the gap opens.
    ///
    /// It opens over OpenSec, softens over EdgeFt at its rim and EndFt at its ends, and dies away over FadeSec at the end of
    /// its life. It does no damage and is no obstacle. It rides in the battle's bullets, like a well, so a clone, a subset, a
    /// swap and the wire carry it. Numbers to tune by feel.
    /// </summary>
    public static class JetStream
    {
        /// <summary>How hard it blows, ft/s along the lane (300 ft/s is about 180 kt).</summary>
        public const double WindFps = 300;
        /// <summary>Its size: how long, how far round its middle line it reaches, and how wide its soft rim and ends are.</summary>
        public const double LengthFt = 10000, RadiusFt = 700, EdgeFt = 300, EndFt = 400;
        /// <summary>How long it lasts: it opens over OpenSec, blows for LifeSec in all, dying away over its last FadeSec.</summary>
        public const double OpenSec = 0.5, LifeSec = 9, FadeSec = 1.5;
        /// <summary>Where it starts: laid ahead, BackFt behind you (so you are in it); laid back, GapFt behind you (so you are clear of it).</summary>
        public const double BackFt = 500, GapFt = 400;
        /// <summary>How many the loadout pick brings.</summary>
        public const int Lanes = 2;

        /// <summary>The way a lane laid now from m, flying plan p, blows: along the arrow (ahead), or back down the path (back).</summary>
        public static Vec3d Direction(FlightModel m, TurnPlan p)
        {
            Vec3d fwd = m.Forward;
            if (p.laneBack) return -fwd;
            if (!p.hold && !p.HasMarks && p.aim.Length > 0.5) return p.aim.Normalized;
            return fwd;
        }

        /// <summary>The lane laid now from m, flying plan p: where it starts and the way it blows.</summary>
        public static Lane Lay(int owner, int side, FlightModel m, TurnPlan p, double time)
        {
            Vec3d dir = Direction(m, p);
            Vec3d start = p.laneBack ? m.state.pos - m.Forward * GapFt : m.state.pos - dir * BackFt;
            return Lane.Make(owner, side, start, dir, p.laneBack, time);
        }
    }

    /// <summary>A jet stream lane (see JetStream): a tube of wind from pos, LengthFt along dir. It hits nothing.</summary>
    public class Lane : Bullet
    {
        /// <summary>The way it blows (unit).</summary>
        public Vec3d dir;
        /// <summary>Laid back down the path, at the tailer (it changes nothing in how it blows: only how it was laid).</summary>
        public bool back;

        public static Lane Make(int owner, int side, Vec3d start, Vec3d dir, bool back, double time) => new Lane
        {
            owner = owner, side = side, pos = start, vel = Vec3d.Zero, fireTime = time, dir = dir.Normalized, back = back,
            damage = 0, hitRadiusFt = 0, dragPerSec = 0, lifeSec = JetStream.LifeSec,
        };

        public Vec3d End => pos + dir * JetStream.LengthFt;

        /// <summary>How hard it blows now, 0 to 1: opening over OpenSec, dying away over the last FadeSec.</summary>
        public double Strength => StrengthAt(age);

        public static double StrengthAt(double age)
        {
            if (age < 0 || age >= JetStream.LifeSec) return 0;
            double open = Math.Min(1, age / JetStream.OpenSec);
            double fade = Math.Min(1, (JetStream.LifeSec - age) / JetStream.FadeSec);
            return Math.Max(0, Math.Min(open, fade));
        }

        /// <summary>How far inside it p is, 0 (outside) to 1 (well inside): softening across the rim and at the ends.</summary>
        public double Inside(Vec3d p) => InsideOf(pos, dir, p);

        public static double InsideOf(Vec3d start, Vec3d dir, Vec3d p)
        {
            Vec3d r = p - start;
            double along = Vec3d.Dot(r, dir);
            if (along <= 0 || along >= JetStream.LengthFt) return 0;
            double radial = (r - dir * along).Length;
            if (radial >= JetStream.RadiusFt) return 0;
            double rim = Math.Min(1, (JetStream.RadiusFt - radial) / JetStream.EdgeFt);
            double ends = Math.Min(1, Math.Min(along, JetStream.LengthFt - along) / JetStream.EndFt);
            return rim * ends;
        }

        /// <summary>The wind at p, ft/s.</summary>
        public Vec3d Wind(Vec3d p)
        {
            double s = Strength;
            if (s <= 0) return Vec3d.Zero;
            double k = Inside(p);
            return k <= 0 ? Vec3d.Zero : dir * (JetStream.WindFps * s * k);
        }

        /// <summary>One step: it stays where it was laid (it is the air itself moving) and ages.</summary>
        public void Step(double dt) => age += dt;
    }

    /// <summary>A jet stream lane in a turn's playback: where it lies, who laid it, and how old it was at startFrame.</summary>
    public class LaneTrace
    {
        public int owner, startFrame;
        public double age0;
        public Vec3d start, dir;
        public bool back;

        /// <summary>How hard it blows at frame (dt the step), 0 to 1; 0 before it is laid.</summary>
        public double StrengthAt(int frame, double dt) => frame < startFrame ? 0 : Lane.StrengthAt(age0 + (frame - startFrame) * dt);
    }
}
