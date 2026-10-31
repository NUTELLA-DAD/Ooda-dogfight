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

    /// <summary>Controlled flight, a departure that is still building, or a developed spin.</summary>
    public enum Departure { None, Incipient, Spin }

    /// <summary>
    /// The player's aircraft: the OODA point mass flown through the OODA limits and smoothing.
    /// The stick asks for G (neutral is 1 G, trimmed level flight; full back asks maxG, full forward minG);
    /// the G builds at the onset rate and is held under the stall line, so a hard pull at low speed only
    /// mushes. The stick's roll asks for a roll rate up to the full-stick rate at this true airspeed, built
    /// up at the roll acceleration. Nothing levels the wings or holds height: let go in a bank and it
    /// keeps the bank and the nose drops, as a trimmed aeroplane would.
    ///
    /// Held deep past the stall line (rudder or aileron makes it come sooner) a wing drops and the aircraft
    /// departs: an incipient spin, then a developed one, autorotating about the falling path at high angle
    /// of attack with the ailerons nearly useless. Standard recovery gets out: throttle back, opposite
    /// rudder and stick forward of neutral, held; an incipient one also recovers just by unloading.
    /// The departure is a layer on the point mass (it has no rotational dynamics of its own); its numbers
    /// are estimates to tune by feel.
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

        public Departure departure = Departure.None;
        public int spinDir = 1;         // +1 spinning right, -1 left
        public double departBuild;      // seconds of deep stall built up toward a departure
        public double departTime;       // seconds since departing
        public double recoverTime;      // seconds of recovery inputs held
        double alphaShown;
        readonly Random wingDrop = new Random(1944);

        public bool Departed => departure != Departure.None;
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

        /// <summary>The angle of attack for the picture, degrees: from the lift asked of the wing against the stall line, or the spin's.</summary>
        public double AlphaDeg => alphaShown;

        double FlownAlphaDeg()
        {
            if (Departed) return numbers.spinAlphaDeg;
            double cl = g / Math.Max(numbers.StallLimitG(Math.Max(Kias, 1)), 1e-6); // CL / CLmax
            double a = numbers.alphaZeroLiftDeg + cl * (numbers.alphaStallDeg - numbers.alphaZeroLiftDeg);
            if (stalled && gAsked > g) a += Math.Min(8, (gAsked - g) * 2); // the nose keeps coming up past the stall
            return Math.Max(-12, Math.Min(numbers.alphaStallDeg + 8, a));
        }

        public void Step(FlightControls c, double dt)
        {
            var n = numbers;
            double ktas = Ktas, kias = Kias;
            double p = Math.Max(-1, Math.Min(1, c.pitch));
            double ail = Math.Max(-1, Math.Min(1, c.roll));
            double ped = Math.Max(-1, Math.Min(1, c.yaw));

            // G: the stick's ask, eased at the onset rate and its acceleration, then held inside the wing's limits
            gAsked = p >= 0 ? 1 + p * (n.maxG - 1) : 1 + (-p) * (n.minG - 1);
            double hi = n.AvailableG(kias), lo = n.AvailableNegG(kias);
            double maxRate = n.MaxRollRateDps(ktas);
            double rollAccel = n.maxRollRateDps / n.rollFullRateSec;
            double extraDrag = 0;

            if (!Departed)
            {
                double wantRate = Math.Max(-n.gOnsetGps, Math.Min(n.gOnsetGps, (gAsked - g) / Math.Max(dt, 0.05)));
                double dRate = n.gOnsetAccelGps2 * dt;
                gRate = Math.Max(gRate - dRate, Math.Min(gRate + dRate, wantRate));
                double gNext = g + gRate * dt;
                stalled = gNext > hi + 1e-9 || gNext < lo - 1e-9;
                g = Math.Max(lo, Math.Min(hi, gNext));
                if (stalled) gRate = 0;

                // Roll: toward the stick's share of the full-stick rate, at the roll acceleration
                double wantRoll = ail * maxRate * (stalled ? n.stalledRollFactor : 1);
                rollRateDps = Math.Max(rollRateDps - rollAccel * dt, Math.Min(rollRateDps + rollAccel * dt, wantRoll));

                // Departure: held deep past the stall line; crossed controls bring it on sooner
                double depth = gAsked - hi;
                if (stalled && p > 0 && depth > n.departureStallMarginG)
                    departBuild += dt * (1 + 2 * Math.Abs(ped) + Math.Abs(ail));
                else
                    departBuild = Math.Max(0, departBuild - 2 * dt);
                if (departBuild > n.departureSec)
                {
                    departure = Departure.Incipient;
                    double bias = ped + 0.5 * ail;
                    spinDir = Math.Abs(bias) > 0.1 ? Math.Sign(bias) : (wingDrop.NextDouble() < 0.5 ? -1 : 1);
                    departTime = recoverTime = departBuild = 0;
                }
            }
            else
            {
                departTime += dt;
                stalled = true;
                // The stalled wing still lifts, a little; the G follows it
                g = Math.Min(n.maxG, n.spinLiftFraction * n.StallLimitG(kias));
                gRate = 0;

                // Autorotation: the roll about the path builds toward the spin rate; the ailerons barely touch it
                double build = departure == Departure.Spin ? 1 : Math.Min(1, departTime / n.incipientSec);
                double wantRoll = spinDir * n.spinRateDps * build + ail * 0.1 * maxRate;
                rollRateDps = Math.Max(rollRateDps - rollAccel * dt, Math.Min(rollRateDps + rollAccel * dt, wantRoll));
                extraDrag = n.spinDragPerWeight * Math.Pow(kias / 120.0, 2);

                // Recovery: opposite rudder and the stick forward, held; an incipient spin also recovers when unloaded
                bool antiRudder = -ped * spinDir > 0.5;
                bool unloaded = p <= 0.05;
                bool recovering = departure == Departure.Incipient ? unloaded : antiRudder && unloaded;
                recoverTime = recovering ? recoverTime + dt * (antiRudder ? 1.5 : 1) : Math.Max(0, recoverTime - dt);
                double needed = departure == Departure.Incipient ? n.incipientRecoverySec : n.spinRecoverySec;
                if (recoverTime > needed)
                {
                    departure = Departure.None;
                    departTime = recoverTime = 0;
                    g = Math.Min(g, 1);
                }
                else if (departure == Departure.Incipient && departTime > n.incipientSec && !unloaded)
                {
                    departure = Departure.Spin;
                }
            }

            // Rudder: a side force that grows with dynamic pressure (none in a spin: the rudder is fighting the rotation)
            double q = Math.Min(1, (kias / n.rudderFullKias) * (kias / n.rudderFullKias));
            sideG = Departed ? 0 : ped * n.rudderSideG * q;

            // Engine: the throttle spools toward the lever
            double lever = Math.Max(0, Math.Min(1, c.throttle));
            throttle += Math.Max(-dt * 0.8, Math.Min(dt * 0.8, lever - throttle));
            double power = throttle * (c.wep && lever > 0.95 ? n.wepFactor : 1);
            double side = Math.Abs(sideG);
            PointMass.ExcessFn excessFn = (vKt, altFt, gg) =>
                power * n.ThrustPerWeight(vKt, altFt) - n.DragPerWeight(Atmosphere.TasToIasKt(vKt, altFt), gg)
                - n.sideslipDragPerG * side - extraDrag;
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

            alphaShown += (FlownAlphaDeg() - alphaShown) * Math.Min(1, dt * 4);
        }
    }
}
