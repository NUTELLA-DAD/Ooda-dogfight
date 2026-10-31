using System;
using Ooda.Flight.Core;

namespace Ooda.Turn.Core
{
    /// <summary>
    /// One aircraft's orders for one movement turn: where the nose should point when the turn ends,
    /// the throttle, and when the guns fire (seconds into the turn; no burst when end is not after start).
    /// </summary>
    public struct TurnPlan
    {
        public Vec3d aim;          // unit, world frame: the flight path wanted at the end of the turn
        public double throttle;    // 0..1
        public bool wep;           // war emergency power (with the throttle full)
        public double burstStart, burstEnd;
        public bool hold;          // fly on with the stick where it is instead (the "keeps doing what it's doing" projection)

        public bool Fires(double t) => burstEnd > burstStart && t >= burstStart && t < burstEnd;

        public static TurnPlan Straight(FlightModel m) => new TurnPlan { aim = m.Forward, throttle = 1 };
        public static TurnPlan Holding() => new TurnPlan { hold = true };

        public FlightControls ControlsFor(FlightModel m) => hold ? AutoPilot.Hold(m) : AutoPilot.Controls(m, aim, throttle, wep);
    }

    /// <summary>
    /// Flies a FlightModel toward a wanted flight path the way a good pilot would: roll the lift onto the
    /// turn, pull to the stall line (never past it, so the autopilot never departs), and ease off as the
    /// nose arrives. It works the real stick, so everything the free-flight model does (G onset, roll
    /// acceleration, energy bleed in the turn) shows in the result.
    /// </summary>
    public static class AutoPilot
    {
        /// <summary>How quickly the nose is brought onto the aim in its last few degrees (s): bigger is gentler.</summary>
        public const double SettleSec = 0.2;
        /// <summary>The share of the stall line the autopilot will use: the last bit is buffet, not turn.</summary>
        public const double StallMargin = 0.97;

        public static FlightControls Controls(FlightModel m, Vec3d aim, double throttle, bool wep)
        {
            var n = m.numbers;
            Vec3d fwd = m.Forward, up = m.Up, right = m.Right;
            double v = m.state.vel.Length;

            // The turn wanted: the angle still to go, closed at a rate that settles without overshoot
            double cosA = Math.Max(-1, Math.Min(1, Vec3d.Dot(fwd, aim)));
            double angle = Math.Acos(cosA);
            Vec3d w = aim - fwd * cosA;
            double wl = w.Length;
            Vec3d wHat = wl > 1e-9 ? w / wl : up;
            // The G takes time to come off (the onset rate), so the turn is closed at the rate that can still be
            // stopped on the aim: like braking to a line. Near the end, a plain proportional close.
            double brake = 0.5 * n.gOnsetGps * Atmosphere.GFtps2 / Math.Max(v, 1);   // rad/s^2 of turn the unload takes off
            double rate = Math.Min(Math.Sqrt(2 * brake * angle), angle / SettleSec);  // rad/s

            // The lift that gives that turn and holds the path against gravity (in G, square to the path)
            Vec3d gravPerp = Vec3d.Up - fwd * fwd.z;               // what the wing must carry to fly straight
            Vec3d lift = gravPerp + wHat * (rate * v / Atmosphere.GFtps2);

            // Roll the lift vector over to it; pull what lies along the wings' up now (unloads while rolling)
            double rollErr = Math.Atan2(Vec3d.Dot(lift, right), Vec3d.Dot(lift, up));
            double liftMag = lift.Length;
            double gWant = Vec3d.Dot(lift, up);
            if (liftMag < 0.3) rollErr = 0;                        // nothing much asked: leave the wings alone

            double kias = m.Kias;
            double hi = n.AvailableG(kias) * StallMargin;
            double lo = n.AvailableNegG(kias) * StallMargin;
            // Pulling through a big roll error is wasted; push only when the lift is mostly below (an outside turn is poor)
            if (Math.Abs(rollErr) > Math.PI * 0.6) gWant = Math.Max(gWant, 0.2);
            gWant = Math.Max(lo, Math.Min(hi, gWant));

            double pitch = gWant >= 1 ? (gWant - 1) / (n.maxG - 1) : -(1 - gWant) / (1 - n.minG);

            // Roll rate wanted: stop on the error with the roll acceleration available
            double maxRate = Math.Max(1, n.MaxRollRateDps(m.Ktas));
            double accel = n.maxRollRateDps / n.rollFullRateSec;
            double errDeg = rollErr * 180 / Math.PI;
            double wantRate = Math.Sign(errDeg) * Math.Min(maxRate, Math.Sqrt(2 * accel * 0.5 * Math.Abs(errDeg)));
            wantRate = Math.Max(-maxRate, Math.Min(maxRate, wantRate + errDeg * 2)); // a little proportional on the last degrees

            return new FlightControls
            {
                pitch = pitch,
                roll = Math.Max(-1, Math.Min(1, wantRate / maxRate)),
                yaw = 0,
                throttle = throttle,
                wep = wep,
            };
        }

        /// <summary>
        /// "If it keeps doing what it's doing": the stick held where it is (the same G, no roll), the throttle where it is.
        /// </summary>
        public static FlightControls Hold(FlightModel m)
        {
            var n = m.numbers;
            double g = m.g;
            double pitch = g >= 1 ? (g - 1) / (n.maxG - 1) : -(1 - g) / (1 - n.minG);
            return new FlightControls { pitch = pitch, roll = 0, throttle = m.throttle };
        }
    }
}
