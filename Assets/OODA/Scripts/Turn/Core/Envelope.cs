using System;
using Ooda.Flight.Core;

namespace Ooda.Turn.Core
{
    /// <summary>
    /// Where the nose can be at the end of the turn: for each direction around the nose (phi, 0 = the pilot's
    /// "up" (a pull), 90 = right wing, 180 = a push, measured in the aircraft's frame at the start of the
    /// turn) the most it can turn in turnSec at full power, found by flying it. The player's arrow is held
    /// inside this shell.
    /// </summary>
    public class Envelope
    {
        public const int Directions = 24;
        public Vec3d fwd, up, right;           // the start frame
        public double[] maxAngle = new double[Directions];   // radians from fwd
        public Vec3d[] rim = new Vec3d[Directions];          // the flight path actually reached flying each one
        public double turnSec;

        public static Envelope Compute(FlightModel start, double turnSec)
        {
            var e = new Envelope { fwd = start.Forward, up = start.Up, right = start.Right, turnSec = turnSec };
            int steps = (int)Math.Round(turnSec / Battle.StepSec);
            for (int k = 0; k < Directions; k++)
            {
                double phi = 2 * Math.PI * k / Directions;
                Vec3d aim = e.Direction(phi, 175 * Math.PI / 180);
                var m = start.Clone();
                double best = 0;
                for (int s = 0; s < steps; s++)
                {
                    m.Step(AutoPilot.Controls(m, aim, 1, true), Battle.StepSec);
                    best = Math.Max(best, Angle(e.fwd, m.Forward));
                }
                e.maxAngle[k] = best;
                e.rim[k] = m.Forward;
            }
            return e;
        }

        /// <summary>The direction theta from the start nose, around it by phi.</summary>
        public Vec3d Direction(double phi, double theta)
        {
            Vec3d side = up * Math.Cos(phi) + right * Math.Sin(phi);
            return fwd * Math.Cos(theta) + side * Math.Sin(theta);
        }

        /// <summary>A direction's phi and theta in the start frame.</summary>
        public void Polar(Vec3d d, out double phi, out double theta)
        {
            d = d.Normalized;
            theta = Angle(fwd, d);
            phi = Math.Atan2(Vec3d.Dot(d, right), Vec3d.Dot(d, up));
            if (phi < 0) phi += 2 * Math.PI;
        }

        /// <summary>The most turn available toward phi, between the flown directions.</summary>
        public double MaxAngle(double phi)
        {
            double f = phi / (2 * Math.PI) * Directions;
            int i = ((int)Math.Floor(f) % Directions + Directions) % Directions;
            double u = f - Math.Floor(f);
            return maxAngle[i] * (1 - u) + maxAngle[(i + 1) % Directions] * u;
        }

        /// <summary>How far past the ring the arrow may go to overpull (radians beyond the ring).</summary>
        public const double OverpullRange = 25 * Math.PI / 180;

        /// <summary>The furthest the arrow may go toward phi: the ring plus the overpull band.</summary>
        public double OuterAngle(double phi) => Math.Min(Math.PI * 0.97, MaxAngle(phi) + OverpullRange);

        /// <summary>Hold a wanted direction inside the shell (or, allowing overpull, inside the outer band).</summary>
        public Vec3d Clamp(Vec3d d, bool allowOverpull = false)
        {
            Polar(d, out double phi, out double theta);
            double max = allowOverpull ? OuterAngle(phi) : MaxAngle(phi);
            return theta <= max ? d.Normalized : Direction(phi, max);
        }

        /// <summary>0 inside the ring, rising to 1 at the outer edge of the overpull band.</summary>
        public double Overpull(Vec3d d)
        {
            Polar(d, out double phi, out double theta);
            double max = MaxAngle(phi);
            return Math.Max(0, Math.Min(1, (theta - max) / Math.Max(1e-6, OuterAngle(phi) - max)));
        }

        public static double Angle(Vec3d a, Vec3d b) => Math.Acos(Math.Max(-1, Math.Min(1, Vec3d.Dot(a.Normalized, b.Normalized))));
    }
}
