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

        // Jets only (zero for the propeller aircraft, which leaves them exactly as they were): the ram rise in thrust
        // with Mach, thrust *= 1 + ramPerMach2 * M^2, and the transonic drag rise, the zero-lift drag multiplied by
        // 1 + wave(M): nothing below waveMcrit, climbing smoothly to waveRise at waveMpeak, then easing off by
        // waveFallPerMach of it per Mach above, never under waveFloor of it.
        public double ramPerMach2 = 0;
        public double waveRise = 0;
        public double waveMcrit = 0.85;
        public double waveMpeak = 1.08;
        public double waveFallPerMach = 0.55;
        public double waveFloor = 0.4;

        // The game's extras (no WW2 fighter had either): a rocket that kicks for a few seconds, then has to wait,
        // and a speed brake. Estimates to tune by feel.
        public double rocketG = 1.3;              // the rocket's thrust / weight while it burns
        public double rocketSec = 2.5;            // how long it burns
        public double rocketCooldownSec = 30;     // from lighting it until it can be lit again (10 normal turns)
        public double brakeG = 0.7;               // the speed brake's drag / weight at 300 KIAS (with the square of the speed)

        // Attitude for the picture only: angle of attack at zero lift and at the stall line
        public double alphaZeroLiftDeg = -1;
        public double alphaStallDeg = 16;

        // Departure and spin (estimates to tune by feel; the point mass has no rotation of its own, so these drive it)
        public double departureStallMarginG = 0.75; // G asked beyond the stall line before a departure starts building
        public double departureSec = 0.8;           // seconds of that (less with rudder or aileron) before a wing drops
        public double incipientSec = 2.5;           // the incipient phase; held pro-spin past it, the spin develops
        public double spinRateDps = 150;            // developed autorotation, about 2.4 s a turn
        public double spinAlphaDeg = 40;            // the nose sits this far above the falling path
        public double spinLiftFraction = 0.3;       // the stalled wing's lift against the stall line
        public double spinDragPerWeight = 0.9;      // extra drag at 120 KIAS, so the spin settles near that speed
        public double incipientRecoverySec = 0.8;   // stick forward held this long gets out of an incipient spin
        public double spinRecoverySec = 2.5;        // opposite rudder and stick forward held this long stops a developed one

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
            t = Math.Min(t, maxStaticThrustPerWeight);
            if (ramPerMach2 > 0) { double m = Atmosphere.Mach(ktas, altFt); t *= 1 + ramPerMach2 * m * m; }
            return t;
        }

        /// <summary>The transonic and supersonic drag rise / weight (zero unless waveRise is set): extra zero-lift drag with Mach.</summary>
        public double WaveDragPerWeight(double ktas, double altFt, double kias)
        {
            if (waveRise <= 0) return 0;
            double m = Atmosphere.Mach(ktas, altFt), f;
            if (m <= waveMcrit) return 0;
            if (m < waveMpeak) { double u = (m - waveMcrit) / (waveMpeak - waveMcrit); f = waveRise * u * u * (3 - 2 * u); }
            else f = waveRise * Math.Max(waveFloor, 1 - waveFallPerMach * (m - waveMpeak));
            double k = Math.Max(kias, 20);
            return dragA * k * k * f;
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

        /// <summary>
        /// The Spitfire LF IX, clean, about 7,400 lb. Seeded from public figures (242 sq ft wing, aspect ratio 5.6,
        /// Cd0 about 0.021, Oswald e about 0.8, Merlin 66 about 1,580 hp, about 404 mph at 21,000 ft, stall about
        /// 90 mph, 450 mph IAS dive limit, ailerons heavy at speed so a lower roll cap). Estimates to tune.
        /// It turns tighter and climbs better than the Mustang and is a little slower; the Mustang rolls and dives better.
        /// </summary>
        public static AircraftNumbers SpitfireIX() => new AircraftNumbers
        {
            name = "Spitfire IX",
            maxG = 8, minG = -4, stallKias = 81, negStallKias = 105, vneKias = 390,
            rollPerKtasDps = 0.4, maxRollRateDps = 80,
            thrustK = 98.9, thrustV0Kt = 40, thrustDensityExp = 0.9, thrustFlatSigma = 0.53,
            maxStaticThrustPerWeight = 0.45, dragA = 2.294e-6, dragB = 650.6,
            alphaStallDeg = 15,
        };

        /// <summary>
        /// The Bf 109 G-6, clean, about 6,940 lb. Seeded from public figures (173 sq ft wing, aspect ratio 6.1,
        /// Cd0 about 0.024, Oswald e about 0.75, DB 605A about 1,455 hp held to about 18,700 ft, prop efficiency about
        /// 0.8, about 386 mph at 22,600 ft, slats so a clean stall near 105 mph, 750 km/h IAS dive limit, light
        /// ailerons slow but heavy at high speed). Estimates to tune. It climbs well and is fast low down; the
        /// Mustang is faster high up, rolls better at speed and out-turns it a little.
        /// </summary>
        public static AircraftNumbers Bf109G() => new AircraftNumbers
        {
            name = "Bf 109 G-6",
            maxG = 7.5, minG = -3.5, stallKias = 90, negStallKias = 112, vneKias = 405,
            rollPerKtasDps = 0.42, maxRollRateDps = 90,
            thrustK = 92.2, thrustV0Kt = 40, thrustDensityExp = 0.9, thrustFlatSigma = 0.56,
            maxStaticThrustPerWeight = 0.45, dragA = 2.026e-6, dragB = 824,
            alphaStallDeg = 17,
        };

        /// <summary>
        /// The B-17G Flying Fortress at a combat weight of about 55,000 lb. Seeded from public figures (1,420 sq ft
        /// wing, aspect ratio 7.6, Cd0 about 0.026 with the turrets, Oswald e about 0.75, four Wright R-1820s at
        /// 1,200 hp each, turbo-supercharged so the power holds to about 25,000 ft, prop efficiency about 0.8, stall
        /// about 100 mph, a 3 G limit and a heavy, slow roll). Estimates to tune: it cruises near 170 KIAS low down
        /// and tops out a little under 200 KIAS at the valley heights.
        /// </summary>
        public static AircraftNumbers B17G() => new AircraftNumbers
        {
            rocketG = 0, brakeG = 0,
            name = "B-17G Flying Fortress",
            maxG = 3, minG = -1, stallKias = 87, negStallKias = 120, vneKias = 235,
            rollPerKtasDps = 0.15, maxRollRateDps = 30, rollFullRateSec = 0.8,
            gOnsetGps = 1.5, gOnsetAccelGps2 = 4,
            rudderSideG = 0.1,
            thrustK = 46.9, thrustV0Kt = 40, thrustDensityExp = 0.9, thrustFlatSigma = 0.448,
            maxStaticThrustPerWeight = 0.25, wepFactor = 1.0, dragA = 2.273e-6, dragB = 640.5,
            alphaStallDeg = 16,
        };

        /// <summary>
        /// The F-4E Phantom II for the jet test (not in the WW2 missions), clean, light, about 41,000 lb. Seeded from public
        /// figures (530 sq ft wing, aspect ratio 2.8, Cd0 about 0.021, two J79s at about 11,900 lbf dry and 17,800 lbf in
        /// afterburner, +7.3 G, clean stall about 150 kt, about Mach 1.1 at sea level and Mach 2.2 high up), then made a
        /// little livelier to fly. Jet thrust hardly falls with speed (the big thrustV0Kt), rises with ram and thins with
        /// height; WEP is the afterburner (wepFactor). Estimates to tune by feel: it tops out near 630 kt dry and 705 kt
        /// in afterburner at sea level, and reaches Mach 2 in about seven minutes of afterburner at 36,000 ft.
        /// </summary>
        public static AircraftNumbers Phantom() => new AircraftNumbers
        {
            name = "F-4 Phantom II",
            maxG = 7.5, minG = -3, stallKias = 140, negStallKias = 175, vneKias = 750,
            rollPerKtasDps = 0.55, maxRollRateDps = 200, rollFullRateSec = 0.35,
            gOnsetGps = 8, gOnsetAccelGps2 = 32,
            rudderSideG = 0.3, rudderFullKias = 250,
            thrustK = 2200, thrustV0Kt = 4000, thrustDensityExp = 0.8, thrustFlatSigma = 1.0,
            maxStaticThrustPerWeight = 99, wepFactor = 1.75, dragA = 9.2e-7, dragB = 3300,
            ramPerMach2 = 0.35, waveRise = 1.5,
            rocketG = 0, brakeG = 1.0,
            alphaZeroLiftDeg = -1, alphaStallDeg = 20,
            spinRateDps = 120, spinDragPerWeight = 0.6,
        };

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
