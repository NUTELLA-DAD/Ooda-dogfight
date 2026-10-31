using System;
using Ooda.Flight.Core;

namespace Ooda.Turn.Core
{
    /// <summary>
    /// A wingman's place in close formation: lateralFt to the right of, backFt behind and upFt above the leader,
    /// in the leader's heading frame (level). Held until the formation breaks (see Battle.CheckBreak).
    /// </summary>
    public class Slot
    {
        /// <summary>An enemy fighter this close to any of ours breaks the formation: about 2 NM, a turn or two before guns range.</summary>
        public const double BreakFt = 12000;

        public int leader;
        public double lateralFt, backFt, upFt;
        /// <summary>How long to take closing up on the slot, s: shorter is tighter but twitchier.</summary>
        public double closeSec = 6;

        public Slot Copy() => (Slot)MemberwiseClone();

        /// <summary>Where the slot is when the leader is at pos flying vel.</summary>
        public Vec3d Where(Vec3d pos, Vec3d vel)
        {
            Vec3d d = new Vec3d(vel.x, vel.y, 0);
            d = d.Length < 1e-6 ? new Vec3d(0, 1, 0) : d.Normalized;
            Vec3d r = new Vec3d(d.y, -d.x, 0);
            return pos + r * lateralFt - d * backFt + new Vec3d(0, 0, upFt);
        }

        /// <summary>
        /// Station keeping for one turn: fly the leader's velocity plus whatever closes the gap to the slot (where it will be
        /// at the end of the turn) over closeSec, with the throttle set for that speed.
        /// </summary>
        public TurnPlan Plan(Battle b, int me, double turnSec)
        {
            var m = b.planes[me];
            var l = b.planes[leader];
            Vec3d lEnd = l.state.pos + l.state.vel * turnSec;
            Vec3d slotEnd = Where(lEnd, l.state.vel);
            Vec3d meEnd = m.state.pos + m.state.vel * turnSec;
            Vec3d gap = slotEnd - meEnd;
            Vec3d want = l.state.vel + gap * (1.0 / closeSec);
            double wantSpeed = want.Length, speed = Math.Max(1, m.state.vel.Length);
            Vec3d aim = want.Normalized;
            // no more than a gentle 25 degrees of turn a turn, and not steeply up or down
            double max = 25 * Math.PI / 180, ang = Envelope.Angle(m.Forward, aim);
            if (ang > max)
            {
                Vec3d side = (aim - m.Forward * Vec3d.Dot(aim, m.Forward)).Normalized;
                aim = (m.Forward * Math.Cos(max) + side * Math.Sin(max)).Normalized;
            }
            double climb = Math.Max(-0.15, Math.Min(0.15, aim.z));
            double h = Math.Sqrt(Math.Max(1e-9, aim.x * aim.x + aim.y * aim.y));
            aim = new Vec3d(aim.x / h * Math.Sqrt(1 - climb * climb), aim.y / h * Math.Sqrt(1 - climb * climb), climb);
            double throttle = Math.Max(0.1, Math.Min(1, 0.55 + (wantSpeed - speed) / speed * 6));
            return new TurnPlan { aim = aim, throttle = throttle };
        }
    }
}
