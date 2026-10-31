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
        public double overpull;    // 0..1: the arrow past the ring; the stick goes that far beyond the stall line toward full aft
        /// <summary>The turn's timeline instead of the arrow: points that each change some settings from their time on (see Mark, StateAt).</summary>
        public Mark[] marks;

        public bool HasMarks => marks != null && marks.Length > 0;

        public bool Fires(double t) => burstEnd > burstStart && t >= burstStart && t < burstEnd;

        public static TurnPlan Straight(FlightModel m) => new TurnPlan { aim = m.Forward, throttle = 1 };
        public static TurnPlan Holding() => new TurnPlan { hold = true };

        public FlightControls ControlsFor(FlightModel m) => ControlsFor(m, 0);

        /// <summary>The stick at t seconds into the turn.</summary>
        public FlightControls ControlsFor(FlightModel m, double t)
        {
            if (hold) return AutoPilot.Hold(m);
            if (!HasMarks) return AutoPilot.Controls(m, aim, throttle, wep, overpull);
            var s = StateAt(t);
            var c = s.bankDeg == null ? AutoPilot.HoldBankAndPull(m, s.g, s.throttle, s.wep)
                                      : AutoPilot.BankAndPull(m, s.bankDeg.Value, s.g, s.throttle, s.wep);
            if (!m.Departed) c.yaw = s.rudder;
            return c;
        }

        /// <summary>
        /// Everything in force at t: each point sets only what it names, the rest carries on from the point before.
        /// Before any point names them: wings held where they are, 1 G, the plan's throttle and WEP, no rudder.
        /// </summary>
        public MarkState StateAt(double t)
        {
            var s = new MarkState { g = 1, throttle = throttle, wep = wep };
            if (marks == null) return s;
            foreach (var k in Sorted())
            {
                if (k.time > t + 1e-9) break;
                s.Apply(k);
            }
            return s;
        }

        /// <summary>The marks by time.</summary>
        public Mark[] Sorted()
        {
            var a = (Mark[])marks.Clone();
            Array.Sort(a, (x, y) => x.time.CompareTo(y.time));
            return a;
        }
    }

    /// <summary>
    /// One point on the turn's timeline: from time (s into the turn) on, whatever it names. A null field leaves
    /// that setting as it was. Bank is right positive from wings level; rudder -1..1 (+ right pedal).
    /// </summary>
    public struct Mark
    {
        public double time;
        public double? bankDeg, g, throttle, rudder;
        public bool? wep;
        public Mark(double time, double bankDeg, double g) : this() { this.time = time; this.bankDeg = bankDeg; this.g = g; }
        public Mark(double time) : this() { this.time = time; }
        public bool Empty => bankDeg == null && g == null && throttle == null && rudder == null && wep == null;
    }

    /// <summary>The settings in force at one moment of a timeline.</summary>
    public struct MarkState
    {
        public double? bankDeg;   // null: wings held where they are
        public double g, throttle, rudder;
        public bool wep;

        public void Apply(Mark k)
        {
            if (k.bankDeg != null) bankDeg = k.bankDeg;
            if (k.g != null) g = k.g.Value;
            if (k.throttle != null) throttle = k.throttle.Value;
            if (k.rudder != null) rudder = k.rudder.Value;
            if (k.wep != null) wep = k.wep.Value;
        }
    }

    /// <summary>
    /// Flies a FlightModel toward a wanted flight path the way a good pilot would: roll the lift onto the
    /// turn, pull to the stall line, and ease off as the nose arrives. Asked to overpull (the arrow past
    /// the ring), it hauls the stick beyond the stall line: the wing stalls, the turn mushes and, held long
    /// enough, it departs into a spin. Departed without an overpull order, it flies the spin recovery. It works the real stick, so everything the free-flight model does (G onset, roll
    /// acceleration, energy bleed in the turn) shows in the result.
    /// </summary>
    public static class AutoPilot
    {
        /// <summary>How quickly the nose is brought onto the aim in its last few degrees (s): bigger is gentler.</summary>
        public const double SettleSec = 0.2;
        /// <summary>The share of the stall line the autopilot will use: the last bit is buffet, not turn.</summary>
        public const double StallMargin = 0.97;

        public static FlightControls Controls(FlightModel m, Vec3d aim, double throttle, bool wep, double overpull = 0)
        {
            var n = m.numbers;
            if (m.Departed && overpull < 0.5) return Recovery(m);
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
            // Overpull: only when pulling (the lift roughly where it is wanted), the stick goes on past the stall line
            if (overpull > 0 && Math.Abs(rollErr) < Math.PI / 4 && pitch > 0)
                pitch += (1 - pitch) * Math.Max(0, Math.Min(1, overpull));

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

        /// <summary>The aircraft's bank: the wings' tilt from level around the flight path, degrees, right wing down positive.</summary>
        public static double BankDeg(FlightModel m) => BankDeg(m.Forward, m.Up);

        /// <summary>The bank of wings whose flight path is f and lift line is up: right positive, from wings level.</summary>
        public static double BankDeg(Vec3d f, Vec3d up)
        {
            Vec3d rl = Vec3d.Cross(f, Vec3d.Up);
            if (rl.Length < 1e-6) return 0;                       // straight up or down: no bank to speak of
            rl = rl.Normalized;
            Vec3d ul = Vec3d.Cross(rl, f);
            return Math.Atan2(Vec3d.Dot(up, rl), Vec3d.Dot(up, ul)) * 180 / Math.PI;
        }

        /// <summary>
        /// The pilot's own hands: roll to a bank angle and hold it, and pull a G. Asked for more G than the wing has
        /// at this speed, the stick goes past the stall line, the same as an overpull. Departed, it flies the
        /// recovery unless the G asked is past the stall line (holding it in).
        /// </summary>
        public static FlightControls BankAndPull(FlightModel m, double bankDeg, double g, double throttle, bool wep)
        {
            var n = m.numbers;
            double avail = n.AvailableG(m.Kias);
            if (m.Departed && g <= avail) return Recovery(m);
            double pitch = g >= 1 ? (g - 1) / (n.maxG - 1) : -(1 - g) / (1 - n.minG);
            if (g > avail && avail > 0)
            {
                // past the stall line: how far toward full aft, by how far past it is asked (2 G past is full aft)
                double stallPitch = (avail - 1) / (n.maxG - 1);
                pitch = Math.Max(pitch, stallPitch + (1 - stallPitch) * Math.Min(1, (g - avail) / 2));
            }
            double errDeg = bankDeg - BankDeg(m);
            while (errDeg > 180) errDeg -= 360;
            while (errDeg < -180) errDeg += 360;
            double maxRate = Math.Max(1, n.MaxRollRateDps(m.Ktas));
            double accel = n.maxRollRateDps / n.rollFullRateSec;
            double wantRate = Math.Sign(errDeg) * Math.Min(maxRate, Math.Sqrt(2 * accel * 0.5 * Math.Abs(errDeg)));
            wantRate = Math.Max(-maxRate, Math.Min(maxRate, wantRate + errDeg * 2));
            return new FlightControls
            {
                pitch = Math.Max(-1, Math.Min(1, pitch)),
                roll = Math.Max(-1, Math.Min(1, wantRate / maxRate)),
                throttle = throttle, wep = wep,
            };
        }

        /// <summary>Pull a G with the wings held where they are (no roll asked).</summary>
        public static FlightControls HoldBankAndPull(FlightModel m, double g, double throttle, bool wep)
        {
            var c = BankAndPull(m, BankDeg(m), g, throttle, wep);
            c.roll = Math.Max(-1, Math.Min(1, -m.rollRateDps / Math.Max(1, m.numbers.MaxRollRateDps(m.Ktas))));
            return c;
        }

        /// <summary>Spin recovery: throttle idle, rudder against the spin, stick forward of neutral, ailerons neutral.</summary>
        public static FlightControls Recovery(FlightModel m) => new FlightControls
        {
            pitch = -0.3, roll = 0, yaw = -m.spinDir, throttle = 0,
        };

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
