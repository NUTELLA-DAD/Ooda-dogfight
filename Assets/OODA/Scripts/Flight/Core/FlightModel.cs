using System;

namespace Ooda.Flight.Core
{
    /// <summary>What the pilot's hands are doing. Stick and pedals -1..1, throttle 0..1.</summary>
    public struct FlightControls
    {
        public double pitch;    // + pulls (stick back)
        public double roll;     // + rolls right
        public double yaw;      // + right pedal
        public double throttle; // 0..1
        public bool wep;        // war emergency power
    }

    /// <summary>
    /// The player's aircraft: the OODA point mass flown through the OODA limits and smoothing.
    /// The stick asks for G (neutral is 1 G, trimmed level flight; full back asks maxG, full forward minG);
    /// the G builds at the onset rate and is held under the stall line, so a hard pull at low speed only
    /// mushes. The stick's roll asks for a roll rate up to the full-stick rate at this true airspeed, built
    /// up at the roll acceleration. Nothing levels the wings or holds height: let go in a bank and it
    /// keeps the bank and the nose drops, as a trimmed aeroplane would.
    /// </summary>
    public class FlightModel
    {
        public AircraftNumbers numbers;
        public PointMass.State state;

        // What was flown last step (for the HUD, the picture and the pilot's head)
        public double g = 1;            // G flown
        public double gRate;            // G/s
        public double gAsked = 1;       // G the stick asked for
        public double rollRateDps;      // signed, right positive
        public double sideG;
        public double throttle = 1;     // the engine's, eased toward the lever
        public bool stalled;            // the stick asks for more than the wing gives
        public double excess;           // (thrust - drag) / weight

        public double Ktas => state.vel.Length * Atmosphere.FtpsToKt;
        public double AltFt => state.pos.z;
        public double Kias => Atmosphere.TasToIasKt(Ktas, AltFt);
        public Vec3d Forward => state.vel.Normalized;
        public Vec3d Up => state.up;
        public Vec3d Right => Vec3d.Cross(Forward, Up);

        /// <summary>The slowest the point mass may fly (its path has no direction at rest), ft/s.</summary>
        public const double MinSpeedFtps = 50;

        public FlightModel(AircraftNumbers numbers, PointMass.State start)
        {
            this.numbers = numbers;
            state = start;
        }

        /// <summary>The angle of attack for the picture, degrees: from the lift asked of the wing against the stall line.</summary>
        public double AlphaDeg
        {
            get
            {
                double cl = g / Math.Max(numbers.StallLimitG(Math.Max(Kias, 1)), 1e-6); // CL / CLmax
                double a = numbers.alphaZeroLiftDeg + cl * (numbers.alphaStallDeg - numbers.alphaZeroLiftDeg);
                return Math.Max(-12, Math.Min(numbers.alphaStallDeg + 4, a));
            }
        }

        public void Step(FlightControls c, double dt)
        {
            var n = numbers;
            double ktas = Ktas, kias = Kias;

            // G: the stick's ask, eased at the onset rate and its acceleration, then held inside the wing's limits
            double p = Math.Max(-1, Math.Min(1, c.pitch));
            gAsked = p >= 0 ? 1 + p * (n.maxG - 1) : 1 + (-p) * (n.minG - 1);
            double wantRate = Math.Max(-n.gOnsetGps, Math.Min(n.gOnsetGps, (gAsked - g) / Math.Max(dt, 0.05)));
            double dRate = n.gOnsetAccelGps2 * dt;
            gRate = Math.Max(gRate - dRate, Math.Min(gRate + dRate, wantRate));
            double gNext = g + gRate * dt;
            double hi = n.AvailableG(kias), lo = n.AvailableNegG(kias);
            stalled = gNext > hi + 1e-9 || gNext < lo - 1e-9;
            g = Math.Max(lo, Math.Min(hi, gNext));
            if (stalled) gRate = 0;

            // Roll: toward the stick's share of the full-stick rate, at the roll acceleration
            double maxRate = n.MaxRollRateDps(ktas) * (stalled ? n.stalledRollFactor : 1);
            double wantRoll = Math.Max(-1, Math.Min(1, c.roll)) * maxRate;
            double rollAccel = n.maxRollRateDps / n.rollFullRateSec;
            double dRoll = rollAccel * dt;
            rollRateDps = Math.Max(rollRateDps - dRoll, Math.Min(rollRateDps + dRoll, wantRoll));

            // Rudder: a side force that grows with dynamic pressure
            double q = Math.Min(1, (kias / n.rudderFullKias) * (kias / n.rudderFullKias));
            sideG = Math.Max(-1, Math.Min(1, c.yaw)) * n.rudderSideG * q;

            // Engine: the throttle spools toward the lever
            double lever = Math.Max(0, Math.Min(1, c.throttle));
            throttle += Math.Max(-dt * 0.8, Math.Min(dt * 0.8, lever - throttle));
            double power = throttle * (c.wep && lever > 0.95 ? n.wepFactor : 1);
            double side = Math.Abs(sideG);
            PointMass.ExcessFn excessFn = (vKt, altFt, gg) =>
                power * n.ThrustPerWeight(vKt, altFt) - n.DragPerWeight(Atmosphere.TasToIasKt(vKt, altFt), gg) - n.sideslipDragPerG * side;
            excess = excessFn(ktas, AltFt, g);

            state = PointMass.StepBody(state, g, sideG, dt, excessFn);

            // The speed floor, then the roll about the new path
            double speed = state.vel.Length;
            if (speed < MinSpeedFtps) state.vel = state.vel * (MinSpeedFtps / speed);
            Vec3d fwd = state.vel.Normalized;
            Vec3d up = state.up.SquareTo(fwd);
            Vec3d right = Vec3d.Cross(fwd, up);
            double phi = rollRateDps * Math.PI / 180 * dt;
            state.up = (up * Math.Cos(phi) + right * Math.Sin(phi)).SquareTo(fwd);
        }
    }
}
