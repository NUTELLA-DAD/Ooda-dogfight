using System;

namespace Ooda.Flight.Core
{
    /// <summary>
    /// The OODA tool's point-mass step (core/point-mass.js), ported: one aircraft as a point with a velocity,
    /// pulled by G along its lift, by (thrust - drag) / weight along its path, and by gravity; fourth-order
    /// Runge-Kutta. Feet, x east, y north, z up; velocity is true airspeed in ft/s.
    ///
    /// Two ways to aim the lift:
    ///  - StepBank: the tool's own, bank measured from the horizon (an AI pilot's "hold 60 degrees of bank").
    ///  - StepBody: the lift along the aircraft's own up, carried with the path (a player's stick: roll moves the
    ///    up, nothing levels it). Same forces, same integrator; with no roll they fly the same path.
    /// </summary>
    public static class PointMass
    {
        public struct State
        {
            public Vec3d pos;   // ft
            public Vec3d vel;   // ft/s, true airspeed
            public Vec3d up;    // unit, square to vel: the lift direction at 0 bank (StepBank) or the aircraft's up (StepBody)
        }

        /// <summary>(thrust - drag) / weight for a true airspeed (kt), height (ft) and G.</summary>
        public delegate double ExcessFn(double ktas, double altFt, double g);

        /// <summary>The up square to the path from the horizon, kept pointing the same way as prevUp (point-mass.js upFrom).</summary>
        public static Vec3d UpFrom(Vec3d vHat, Vec3d prevUp)
        {
            double c = vHat.z;
            var u = new Vec3d(-c * vHat.x, -c * vHat.y, 1 - c * vHat.z);
            double m = u.Length;
            if (m < 1e-9)
            {
                double p = Vec3d.Dot(prevUp, vHat);
                u = prevUp - vHat * p;
                m = u.Length;
            }
            u = u / m;
            return Vec3d.Dot(u, prevUp) < 0 ? -u : u;
        }

        static Vec3d Accel(Vec3d v, double altFt, Vec3d lift, double g, double sideG, Vec3d right, ExcessFn excessFn, out double speed)
        {
            speed = v.Length;
            Vec3d vHat = v / speed;
            double excess = excessFn(speed * Atmosphere.FtpsToKt, altFt, g);
            Vec3d a = (lift * g + right * sideG + vHat * excess) * Atmosphere.GFtps2;
            a.z -= Atmosphere.GFtps2;
            return a;
        }

        delegate Vec3d AccelAt(Vec3d p, Vec3d v);

        static State Rk4(State s, double dt, AccelAt accel, Func<Vec3d, Vec3d> nextUp)
        {
            Vec3d p0 = s.pos, v0 = s.vel;
            Vec3d a1 = accel(p0, v0);
            Vec3d p2 = p0 + v0 * (dt / 2), v2 = v0 + a1 * (dt / 2);
            Vec3d a2 = accel(p2, v2);
            Vec3d p3 = p0 + v2 * (dt / 2), v3 = v0 + a2 * (dt / 2);
            Vec3d a3 = accel(p3, v3);
            Vec3d p4 = p0 + v3 * dt, v4 = v0 + a3 * dt;
            Vec3d a4 = accel(p4, v4);
            double w = dt / 6;
            Vec3d dp = (v0 + v2 * 2 + v3 * 2 + v4) * w;
            Vec3d dv = (a1 + a2 * 2 + a3 * 2 + a4) * w;
            Vec3d v = v0 + dv;
            return new State { pos = p0 + dp, vel = v, up = nextUp(v.Normalized) };
        }

        /// <summary>One step of the tool's own kind: G along the lift, bank from the horizon (right positive).</summary>
        public static State StepBank(State s, double g, double bankRad, double dt, ExcessFn excessFn)
        {
            Vec3d up0 = s.up;
            double cb = Math.Cos(bankRad), sb = Math.Sin(bankRad);
            AccelAt accel = (p, v) =>
            {
                Vec3d vHat = v.Normalized;
                Vec3d up = UpFrom(vHat, up0);
                Vec3d right = Vec3d.Cross(vHat, up);
                Vec3d lift = up * cb + right * sb;
                return Accel(v, p.z, lift, g, 0, right, excessFn, out _);
            };
            return Rk4(s, dt, accel, vHat => UpFrom(vHat, up0));
        }

        /// <summary>
        /// One step with the lift along the aircraft's own up (s.up), carried square to the path at every stage,
        /// plus a side force (G, toward the right wing) for the rudder.
        /// </summary>
        public static State StepBody(State s, double g, double sideG, double dt, ExcessFn excessFn)
        {
            Vec3d up0 = s.up;
            AccelAt accel = (p, v) =>
            {
                Vec3d vHat = v.Normalized;
                Vec3d up = up0.SquareTo(vHat);
                Vec3d right = Vec3d.Cross(vHat, up);
                return Accel(v, p.z, up, g, sideG, right, excessFn, out _);
            };
            return Rk4(s, dt, accel, vHat => up0.SquareTo(vHat));
        }

        /// <summary>A level, wings-level state from speed (KTAS), heading (math radians, counter-clockwise from east) and climb angle.</summary>
        public static State Make(Vec3d pos, double ktas, double headingRad, double climbRad = 0)
        {
            double v = ktas * Atmosphere.KtToFtps;
            double ch = Math.Cos(headingRad), sh = Math.Sin(headingRad), cg = Math.Cos(climbRad), sg = Math.Sin(climbRad);
            return new State
            {
                pos = pos,
                vel = new Vec3d(v * cg * ch, v * cg * sh, v * sg),
                up = new Vec3d(-sg * ch, -sg * sh, cg),
            };
        }
    }
}
