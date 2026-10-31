using System;

namespace Ooda.Flight.Core
{
    /// <summary>
    /// One aircraft's performance set, in the same shape as the OODA tool's makePerformance
    /// (core/aircraft-performance.js): a stall line, G limits, roll limits, G onset, and
    /// thrust and drag per weight fitted as
    ///   thrust/W = thrustK * min(sigma, flatSigma)^exp / (KTAS + V0)
    ///   drag/W   = dragA * KIAS^2 + dragB * G^2 / KIAS^2
    /// Speeds in knots, heights in feet, G as load factor.
    /// </summary>
    [Serializable]
    public class AircraftNumbers
    {
        public string name = "Aircraft";

        // Limits
        public double maxG = 8;
        public double minG = -4;
        public double stallKias = 90;          // 1 G stall, clean
        public double negStallKias = 115;      // 1 G stall, inverted (the wing is less efficient upside down)
        public double vneKias = 440;

        // Roll: full stick rate grows with true airspeed up to a cap; full rate reached in fullRateSec
        public double rollPerKtasDps = 0.45;
        public double maxRollRateDps = 100;
        public double rollFullRateSec = 0.3;
        public double stalledRollFactor = 0.3;

        // G onset: G per second, and how fast that rate itself builds (G/s^2)
        public double gOnsetGps = 6;
        public double gOnsetAccelGps2 = 24;

        // Rudder: side force at full pedal (G), reached from rudderFullKias up
        public double rudderSideG = 0.35;
        public double rudderFullKias = 180;
        public double sideslipDragPerG = 0.05;

        // Thrust and drag fit
        public double thrustK = 76.4;
        public double thrustV0Kt = 40;
        public double thrustDensityExp = 0.9;
        public double thrustFlatSigma = 0.553;
        public double maxStaticThrustPerWeight = 0.45;
        public double wepFactor = 1.15;
        public double dragA = 1.399e-6;
        public double dragB = 843.6;

        // Attitude for the picture only: angle of attack at zero lift and at the stall line
        public double alphaZeroLiftDeg = -1;
        public double alphaStallDeg = 16;

        /// <summary>The most G the wing gives at this speed before it stalls: (KIAS / stall speed)^2.</summary>
        public double StallLimitG(double kias) => (kias / stallKias) * (kias / stallKias);

        /// <summary>The most negative G the wing gives at this speed.</summary>
        public double NegStallLimitG(double kias) => -(kias / negStallKias) * (kias / negStallKias);

        public double AvailableG(double kias) => Math.Min(StallLimitG(kias), maxG);
        public double AvailableNegG(double kias) => Math.Max(NegStallLimitG(kias), minG);

        /// <summary>Full-stick roll rate (deg/s) at a true airspeed.</summary>
        public double MaxRollRateDps(double ktas) => Math.Min(maxRollRateDps, rollPerKtasDps * Math.Max(0, ktas));

        /// <summary>Maximum-power thrust / weight: falls with true airspeed, holds to the flat-rating height, then falls with density.</summary>
        public double ThrustPerWeight(double ktas, double altFt)
        {
            double sigma = Math.Min(Atmosphere.DensityRatio(altFt), thrustFlatSigma);
            double t = thrustK * Math.Pow(sigma, thrustDensityExp) / (ktas + thrustV0Kt);
            return Math.Min(t, maxStaticThrustPerWeight);
        }

        /// <summary>Drag / weight, clean: zero-lift part with KIAS^2, induced part with G^2 / KIAS^2.</summary>
        public double DragPerWeight(double kias, double g)
        {
            double k = Math.Max(kias, 20);
            return dragA * k * k + dragB * g * g / (k * k);
        }

        /// <summary>
        /// The P-51D Mustang, clean, about 9,200 lb. Seeded from public figures (233 sq ft wing, aspect ratio 5.86,
        /// Cd0 about 0.0163, Oswald e about 0.75, Merlin V-1650-7 1,490 hp military and about 1,720 hp WEP, prop
        /// efficiency about 0.85, +8/-4 G, stall about 100 mph, peak roll about 100 deg/s). Estimates to tune.
        /// </summary>
        public static AircraftNumbers P51D() => new AircraftNumbers { name = "P-51D Mustang" };

        /// <summary>The OODA tool's T-6A set (core/t6a-turn-data.js T6A_FIT and T6A_LIMITS), kept to check the port.</summary>
        public static AircraftNumbers T6A() => new AircraftNumbers
        {
            name = "T-6A Texan II",
            maxG = 7, minG = -3.5, stallKias = 86, negStallKias = 110, vneKias = 316,
            rollPerKtasDps = 0.58, maxRollRateDps = 200, rollFullRateSec = 0.3,
            gOnsetGps = 4, gOnsetAccelGps2 = 16,
            thrustK = 96.9613, thrustV0Kt = 74.793, thrustDensityExp = 0.884479, thrustFlatSigma = 0.642634,
            maxStaticThrustPerWeight = 99, dragA = 2.63326e-6, dragB = 642.886,
        };
    }
}
