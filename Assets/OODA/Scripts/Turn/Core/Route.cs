using System;
using Ooda.Flight.Core;

namespace Ooda.Turn.Core
{
    /// <summary>
    /// A course flown by an aircraft on its own, the way a bomber pilot holds formation on the lead's route:
    /// gentle turns (at most maxTurnDeg a turn) toward the next waypoint, gentle climbs and descents, and
    /// the throttle worked to hold a cruise speed. Waypoints are in feet (x east, y north, z height).
    /// A waypoint counts as reached when the aircraft passes within reachFt of it, or passes abeam it.
    /// </summary>
    public class Route
    {
        public Vec3d[] points = new Vec3d[0];
        public double kias = 135;
        public double reachFt = 1500;
        public double maxTurnDeg = 20;
        public double maxClimbDeg = 2.5;
        public int next;

        public bool Done => next >= points.Length;
        public Route Copy() => (Route)MemberwiseClone();   // the points are never changed, so they can be shared

        public void Advance(FlightModel m)
        {
            while (!Done)
            {
                Vec3d to = points[next] - m.state.pos;
                double flat = Math.Sqrt(to.x * to.x + to.y * to.y);
                bool passed = to.x * m.state.vel.x + to.y * m.state.vel.y < 0;
                if (flat < reachFt || (passed && flat < 4 * reachFt)) next++;
                else break;
            }
        }

        public TurnPlan Plan(FlightModel m)
        {
            double throttle = Math.Max(0.15, Math.Min(1, 0.75 + (kias - m.Kias) * 0.03));
            Vec3d fwd = m.Forward;
            if (Done) return new TurnPlan { aim = Level(fwd, 0), throttle = throttle };

            Vec3d to = points[next] - m.state.pos;
            double flat = Math.Max(1, Math.Sqrt(to.x * to.x + to.y * to.y));
            double climb = Math.Max(-maxClimbDeg, Math.Min(maxClimbDeg, Math.Atan2(to.z, flat) * 180 / Math.PI)) * Math.PI / 180;
            Vec3d want = Level(to, climb);
            // turn at most maxTurnDeg this turn, from the current path toward the waypoint
            double angle = Envelope.Angle(fwd, want), max = maxTurnDeg * Math.PI / 180;
            if (angle > max)
            {
                Vec3d side = (want - fwd * Vec3d.Dot(want, fwd)).Normalized;
                want = (fwd * Math.Cos(max) + side * Math.Sin(max)).Normalized;
            }
            return new TurnPlan { aim = want, throttle = throttle };
        }

        /// <summary>The horizontal direction of d, tilted up by climb (radians).</summary>
        static Vec3d Level(Vec3d d, double climb)
        {
            Vec3d h = new Vec3d(d.x, d.y, 0).Normalized;
            return new Vec3d(h.x * Math.Cos(climb), h.y * Math.Cos(climb), Math.Sin(climb));
        }
    }
}
