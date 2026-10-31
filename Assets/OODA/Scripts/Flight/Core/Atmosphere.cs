using System;

namespace Ooda.Flight.Core
{
    /// <summary>Units and the ICAO standard atmosphere, as the OODA tool's core/units.js and core/atmosphere.js.</summary>
    public static class Atmosphere
    {
        public const double KtToFtps = 1.68781;
        public const double FtpsToKt = 1.0 / KtToFtps;
        public const double FtPerM = 3.28084;
        public const double MPerFt = 0.3048;
        public const double GFtps2 = 32.174;

        /// <summary>Air density at altFt as a fraction of sea level, standard day; held at 0.297 above 11 km.</summary>
        public static double DensityRatio(double altFt)
        {
            double h = altFt * MPerFt, T0 = 288.15, L = .0065, g = 9.80665, R = 287.05287;
            if (h < 11000)
            {
                double T = T0 - L * h;
                return Math.Pow(T / T0, g / (R * L) - 1);
            }
            return .297;
        }

        public static double IasToTasKt(double kias, double altFt) => kias / Math.Sqrt(DensityRatio(altFt));
        public static double TasToIasKt(double ktas, double altFt) => ktas * Math.Sqrt(DensityRatio(altFt));
    }
}
