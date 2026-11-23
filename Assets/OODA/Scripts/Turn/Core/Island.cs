using System;

namespace Ooda.Turn.Core
{
    /// <summary>
    /// The island's outline: one landmass with a natural coast, and a ring of islets round it. The Alps' terrain (Editor/
    /// AlpineLayout) and the lands round it (Eastland) both ask here where the sea is, so their coasts are the same line.
    ///
    /// The main island is a rounded oblong about 35 km east to west and 19 km north to south (a superellipse, its radius
    /// swelling and shrinking a few per cent round it, the line wandering a few hundred metres), with bays cut into it: a
    /// harbour bay in the west, the main river's estuary and a lagoon in the south, and an inlet under the peaks in the
    /// north-west. Round it, a kilometre or three off the coast, a broken ring of low islets and sandbanks; and further out
    /// Volcano Isle in the north-west, an island with an airstrip in the south-west, a long island off the east coast
    /// joined to the mainland by a causeway, and the sand spit that closes the lagoon.
    ///
    /// Pads are the flat places things are built on (the towns, the airfields, the causeway): both grounds level
    /// themselves to them last of all, so what stands on one knows its height wherever it is. Pure C#, the same on
    /// every machine. Metres, x east, z north; heights over the sea.
    /// </summary>
    public static class Island
    {
        // ---------- the main island ----------

        public const double CentreX = 8000, CentreZ = 0, RadiusX = 17600, RadiusZ = 9500;
        /// <summary>2 is an ellipse; higher is squarer in the shoulders.</summary>
        public const double Squareness = 2.7;
        /// <summary>The sea floor off the coast (as the Alps': its Base + 20).</summary>
        public const double SeaFloorM = -60;

        /// <summary>How far out a point is as a share of the main island's radius that way (1 on its smooth outline,
        /// before the bays and the wander).</summary>
        public static double Rho(double x, double z)
        {
            double dx = Math.Abs(x - CentreX) / RadiusX, dz = Math.Abs(z - CentreZ) / RadiusZ;
            double r = Math.Pow(Math.Pow(dx, Squareness) + Math.Pow(dz, Squareness), 1 / Squareness);
            double a = Math.Atan2((z - CentreZ) / RadiusZ, (x - CentreX) / RadiusX);
            double swell = 1 + 0.030 * Math.Sin(3 * a + 2.1) + 0.020 * Math.Sin(5 * a + 0.3) + 0.012 * Math.Sin(8 * a + 1.7);
            return r / swell;
        }

        struct Oval
        {
            public double x, z, rx, rz, cos, sin;
            public Oval(double x, double z, double rx, double rz, double rotDeg)
            {
                this.x = x; this.z = z; this.rx = rx; this.rz = rz;
                cos = Math.Cos(rotDeg * Math.PI / 180); sin = Math.Sin(rotDeg * Math.PI / 180);
            }
            /// <summary>1 on the oval's edge, less inside.</summary>
            public double R(double px, double pz)
            {
                double dx = px - x, dz = pz - z;
                double u = (dx * cos + dz * sin) / rx, v = (-dx * sin + dz * cos) / rz;
                return Math.Sqrt(u * u + v * v);
            }
            public double Reach => Math.Max(rx, rz);
        }

        // the bays (rotDeg turns the long axis anticlockwise from east)
        static readonly Oval[] Bays =
        {
            new Oval(-9300, -2600, 1300, 1000, 0),      // the harbour bay, west
            new Oval(100, -9350, 520, 950, 0),          // the main river's estuary
            new Oval(9600, -8950, 2900, 950, 4),        // the lagoon, south
            new Oval(-8250, 5500, 1700, 270, -12),      // the inlet under the peaks, north-west
        };

        /// <summary>How far past the main island's waterline a point is, metres (negative inland): the outline, its wander
        /// and its bays. Low ground meets the sea about where this is 0; a mountain's foot a few hundred metres further out.</summary>
        public static double MainCoastM(double x, double z)
        {
            double rho = Rho(x, z);
            double d = Math.Sqrt((x - CentreX) * (x - CentreX) + (z - CentreZ) * (z - CentreZ));
            double f = rho < 1e-6 ? -RadiusZ : (rho - 1) * d / rho;
            if (f > 4000) return f;
            if (f > -4000)
            {
                f -= 520 * (Eastland.Noise(x / 2300 + 2.2, z / 2300 + 6.1) - 0.5) + 180 * (Eastland.Noise(x / 700 + 7.9, z / 700 + 3.3) - 0.5);
                foreach (var b in Bays)
                {
                    if (Math.Abs(x - b.x) > b.Reach * 2.2 || Math.Abs(z - b.z) > b.Reach * 2.2) continue;
                    f = Math.Max(f, (1 - b.R(x, z)) * Math.Max(1000, Math.Min(b.rx, b.rz)));
                }
            }
            return f;
        }

        /// <summary>How much of the way down to the sea floor the main island's ground is at a point, 0 inland to 1 out at sea:
        /// the land eases down over the last 600 m and the bed drops away over the next 800.</summary>
        public static double MainSea(double coastM) => Eastland.Smooth(-600, 800, coastM);

        // ---------- the ring of islets ----------

        /// <summary>Where the ring lies, as a share of the island's radius, and its half width.</summary>
        public const double RingAt = 1.19, RingHalf = 0.075;

        static double Fbm(double x, double z)
        {
            return (Eastland.Noise(x, z) + 0.5 * Eastland.Noise(x * 2.03 + 11.3, z * 2.03 + 4.7) + 0.25 * Eastland.Noise(x * 4.1 + 3.9, z * 4.1 + 17.1)) / 1.75;
        }

        /// <summary>The ring's islets and sandbanks: their ground at a point (metres over the sea, the sea floor between them).
        /// Low and green with sand round them, a few taller and rocky; none where a named island or a bay's mouth is.</summary>
        public static double IsletM(double x, double z, double rho)
        {
            double band = 1 - Math.Abs(rho - RingAt) / RingHalf;
            if (band <= 0) return SeaFloorM;
            double v = Fbm(x / 1150 + 31.4, z / 1150 + 15.9) + 0.14 * band - 0.14;
            double past = (0.585 - v) * 3600;                    // metres past the islet's waterline, about
            if (past > 420) return SeaFloorM;
            double tall = Eastland.Smooth(0.62, 0.78, Eastland.Noise(x / 3100 + 50.5, z / 3100 + 7.7));
            double top = (4 + 22 * Eastland.Noise(x / 420 + 8.8, z / 420 + 2.2)) * (1 + 3.2 * tall);
            double h = 2.2 + top * Eastland.Smooth(0, 520, -past);
            // a sandbank where it only just fails to break the surface
            return h + (SeaFloorM - h) * Eastland.Smooth(-110, 420, past);
        }

        // ---------- the named islands ----------

        public const double VolcanoX = -12700, VolcanoZ = 7700, VolcanoRadiusM = 1900, VolcanoTopM = 560, CraterRadiusM = 210;

        /// <summary>Volcano Isle: a cone with gullied flanks and a crater at its top, the lava 70 m down inside the rim.</summary>
        public static double VolcanoM(double x, double z)
        {
            double dx = x - VolcanoX, dz = z - VolcanoZ, r = Math.Sqrt(dx * dx + dz * dz);
            if (r > VolcanoRadiusM + 700) return SeaFloorM;
            double a = Math.Atan2(dz, dx);
            double edge = VolcanoRadiusM * (1 + 0.10 * Math.Sin(3 * a + 0.6) + 0.06 * Math.Sin(7 * a + 2.0));
            double u = Math.Max(0, 1 - r / edge);                // 0 at the shore, 1 at the middle
            double gully = 0.5 + 0.5 * Math.Sin(11 * a + 6 * Eastland.Noise(x / 600 + 1.1, z / 600 + 9.9));
            double h = 4 + VolcanoTopM * 1.38 * Math.Pow(u, 1.55) * (1 - 0.16 * gully * (1 - u) * 4 * u);
            if (r < CraterRadiusM * 1.6)
            {
                double rim = VolcanoTopM, lava = VolcanoTopM - 70;
                double c = Eastland.Smooth(CraterRadiusM * 0.55, CraterRadiusM, r);
                h = Math.Min(h, r < CraterRadiusM ? lava + (rim - lava) * c : rim + (h - rim) * Eastland.Smooth(CraterRadiusM, CraterRadiusM * 1.6, r));
            }
            double past = r - edge;
            return h + (SeaFloorM - h) * Eastland.Smooth(-160, 380, past);
        }

        /// <summary>Whether a point is on the lava in the crater.</summary>
        public static bool InCrater(double x, double z)
        {
            double dx = x - VolcanoX, dz = z - VolcanoZ;
            return dx * dx + dz * dz < CraterRadiusM * 0.8 * CraterRadiusM * 0.8;
        }

        struct Isle { public Oval o; public double topM; }
        static readonly Isle[] Isles =
        {
            new Isle { o = new Oval(-12200, -8300, 1600, 520, 20), topM = 12 },     // the airstrip's island, south-west
            new Isle { o = new Oval(29500, -2000, 520, 1900, 0), topM = 16 },       // the long island off the east coast, at the causeway's end
        };

        static double IslesM(double x, double z)
        {
            double best = SeaFloorM;
            foreach (var s in Isles)
            {
                if (Math.Abs(x - s.o.x) > s.o.Reach + 900 || Math.Abs(z - s.o.z) > s.o.Reach + 900) continue;
                double past = (s.o.R(x, z) - 1) * Math.Min(s.o.rx, s.o.rz);
                double h = 2.5 + (s.topM - 2.5) * Eastland.Smooth(0, 260, -past) + 3 * (Eastland.Noise(x / 300 + 4.2, z / 300 + 6.6) - 0.5);
                best = Math.Max(best, h + (SeaFloorM - h) * Eastland.Smooth(-90, 380, past));
            }
            return best;
        }

        /// <summary>The ground of everything off the main island at a point (the ring, the volcano, the named islands), metres
        /// over the sea; the sea floor where there is none. The main island's ground is raised to it, never lowered.</summary>
        public static double OffshoreM(double x, double z)
        {
            double rho = Rho(x, z);
            if (rho < RingAt - RingHalf - 0.02) return SeaFloorM;
            double h = Math.Max(IsletM(x, z, rho), IslesM(x, z));
            if (x < VolcanoX + VolcanoRadiusM + 800 && z > VolcanoZ - VolcanoRadiusM - 800) h = Math.Max(h, VolcanoM(x, z));
            return h;
        }

        // ---------- the pads ----------

        /// <summary>A flat place to build on: round a point or along a line, its ground at one height out to radiusM and
        /// easing back to the ground round it over easeM more.</summary>
        public struct Pad
        {
            public string name;
            public double ax, az, bx, bz, radiusM, easeM, levelM;
            public double Distance(double x, double z)
            {
                double vx = bx - ax, vz = bz - az, len2 = vx * vx + vz * vz;
                double t = len2 < 1e-9 ? 0 : ((x - ax) * vx + (z - az) * vz) / len2;
                t = t < 0 ? 0 : t > 1 ? 1 : t;
                double dx = x - (ax + vx * t), dz = z - (az + vz * t);
                return Math.Sqrt(dx * dx + dz * dz);
            }
        }

        static Pad Round(string name, double x, double z, double r, double ease, double level) =>
            new Pad { name = name, ax = x, az = z, bx = x, bz = z, radiusM = r, easeM = ease, levelM = level };
        static Pad Along(string name, double ax, double az, double bx, double bz, double r, double ease, double level) =>
            new Pad { name = name, ax = ax, az = az, bx = bx, bz = bz, radiusM = r, easeM = ease, levelM = level };

        /// <summary>A runway: its two ends, its width, and which pad it lies on.</summary>
        public struct Runway { public string name; public double ax, az, bx, bz, widthM, levelM; }

        public const int HarbourTown = 0, Crossroads = 1, OasisTown = 2, CityAirport = 3, DesertStrip = 4, IslandStrip = 5, Causeway = 6, Spit = 7;

        public static readonly Pad[] Pads =
        {
            Round("Harbour Town", -7600, -2550, 470, 1100, 9),
            Round("Crossroads", 11000, -2700, 520, 320, 26),
            Round("Oasis Town", 17350, -7400, 380, 300, 30),
            Along("City Airport", 20600, 4300, 22700, 5300, 300, 380, 14),
            Along("Desert Strip", 19300, -6900, 20500, -5700, 220, 320, 42),
            Along("Island Strip", -12850, -8537, -11550, -8063, 200, 160, 12),
            Along("Causeway", 26250, -1860, 29300, -2000, 46, 60, 5),
            Along("Sand spit", 7100, -9750, 12200, -9420, 70, 150, 2.6),
        };

        public static readonly Runway[] Runways =
        {
            new Runway { name = "City Airport", ax = 20750, az = 4371, bx = 22550, bz = 5229, widthM = 70, levelM = 14 },
            new Runway { name = "Desert Strip", ax = 19400, az = -6800, bx = 20400, bz = -5800, widthM = 50, levelM = 42 },
            new Runway { name = "Island Strip", ax = -12750, az = -8500, bx = -11650, bz = -8100, widthM = 46, levelM = 12 },
        };

        /// <summary>A road outside the Alps (theirs are AlpineRoads'): its line's corners (x, z pairs), its width, and whether
        /// it is a main road (the loop that ties the towns together) or a lane.</summary>
        public struct Road { public string name; public double[] xz; public double widthM; public bool main; }

        public static readonly Road[] Roads =
        {
            new Road { name = "Farmland to Crossroads", main = true, widthM = 9, xz = new double[] { 9800, -3300, 10150, -3230, 10500, -3050, 11000, -2700 } },
            new Road { name = "Crossroads to Oasis Town", main = true, widthM = 9, xz = new double[] { 11000, -2700, 11500, -4300, 11800, -5800, 12900, -6900, 14200, -8150, 15400, -8420, 16500, -8050, 17350, -7400 } },
            new Road { name = "Oasis Town to the Desert Strip", main = true, widthM = 9, xz = new double[] { 17350, -7400, 18400, -7250, 19300, -6900 } },
            new Road { name = "Crossroads to the city", main = true, widthM = 9, xz = new double[] { 11000, -2700, 11350, -1500, 11700, -400, 11900, 300 } },
            new Road { name = "River road", widthM = 6, xz = new double[] { 16280, -1250, 16300, -2400, 16200, -3600, 16300, -4800, 16150, -6000, 16350, -6900, 16700, -7250, 17350, -7400 } },
            new Road { name = "City to the airport", main = true, widthM = 9, xz = new double[] { 19100, 4000, 19900, 4050, 20600, 4300 } },
            new Road { name = "Causeway", widthM = 7, xz = new double[] { 24000, -1700, 25000, -1780, 26050, -1850, 29300, -2000, 29500, -2500 } },
        };

        /// <summary>A road's line: a smooth curve through its corners, a point every stepM or so (x, z pairs).</summary>
        public static double[] RoadLine(Road road, double stepM)
        {
            var line = new System.Collections.Generic.List<double>();
            int n = road.xz.Length / 2;
            double X(int k) => road.xz[Math.Min(Math.Max(k, 0), n - 1) * 2];
            double Z(int k) => road.xz[Math.Min(Math.Max(k, 0), n - 1) * 2 + 1];
            double Curve(double p0, double p1, double p2, double p3, double t) =>
                0.5 * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t);
            for (int s = 0; s + 1 < n; s++)
            {
                double dx = X(s + 1) - X(s), dz = Z(s + 1) - Z(s);
                int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dz * dz) / stepM));
                for (int k = 0; k < steps; k++)
                {
                    double t = k / (double)steps;
                    line.Add(Curve(X(s - 1), X(s), X(s + 1), X(s + 2), t));
                    line.Add(Curve(Z(s - 1), Z(s), Z(s + 1), Z(s + 2), t));
                }
            }
            line.Add(X(n - 1)); line.Add(Z(n - 1));
            return line.ToArray();
        }

        // A road's bed: the ground under it cut and filled to an even grade (the ground along its line, smoothed over
        // some 300 m either way and kept above the water), so a road over the dunes is a road and not a staircase.
        sealed class Grade { public double[] xz, y; public double minX, maxX, minZ, maxZ, halfM; }
        const double GradeStepM = 20, GradeEaseM = 46;
        static Grade[] grades;

        static Grade[] MakeGrades(Func<double, double, double> bare)
        {
            var made = new Grade[Roads.Length];
            for (int r = 0; r < Roads.Length; r++)
            {
                var g = new Grade { xz = RoadLine(Roads[r], GradeStepM), halfM = Roads[r].widthM / 2 + 4 };
                int n = g.xz.Length / 2;
                var y = new double[n];
                g.minX = g.minZ = double.MaxValue; g.maxX = g.maxZ = double.MinValue;
                for (int k = 0; k < n; k++)
                {
                    y[k] = Math.Max(2.6, bare(g.xz[k * 2], g.xz[k * 2 + 1]));
                    g.minX = Math.Min(g.minX, g.xz[k * 2]); g.maxX = Math.Max(g.maxX, g.xz[k * 2]);
                    g.minZ = Math.Min(g.minZ, g.xz[k * 2 + 1]); g.maxZ = Math.Max(g.maxZ, g.xz[k * 2 + 1]);
                }
                for (int pass = 0; pass < 2; pass++)
                {
                    var s = new double[n];
                    for (int k = 0; k < n; k++)
                    {
                        // (a narrower window toward each end, so the road meets the ground it starts from)
                        int w = Math.Min(15, Math.Min(k, n - 1 - k));
                        double sum = 0;
                        for (int q = k - w; q <= k + w; q++) sum += y[q];
                        s[k] = sum / (2 * w + 1);
                    }
                    y = s;
                }
                g.y = y;
                made[r] = g;
            }
            return made;
        }

        /// <summary>The ground with the roads' beds cut into it. bare is the ground without them (what the beds are
        /// graded from).</summary>
        public static double WithRoads(double x, double z, double h, Func<double, double, double> bare)
        {
            var all = grades;
            if (all == null) { all = MakeGrades(bare); grades = all; }
            foreach (var g in all)
            {
                double reach = g.halfM + GradeEaseM;
                if (x < g.minX - reach || x > g.maxX + reach || z < g.minZ - reach || z > g.maxZ + reach) continue;
                double best = reach * reach, at = 0;
                int n = g.y.Length;
                for (int k = 0; k + 1 < n; k++)
                {
                    double ax = g.xz[k * 2], az = g.xz[k * 2 + 1], vx = g.xz[k * 2 + 2] - ax, vz = g.xz[k * 2 + 3] - az;
                    double qx = x - ax, qz = z - az;
                    if (Math.Abs(qx) > reach + 30 || Math.Abs(qz) > reach + 30) continue;
                    double len2 = vx * vx + vz * vz, t = len2 < 1e-9 ? 0 : (qx * vx + qz * vz) / len2;
                    t = t < 0 ? 0 : t > 1 ? 1 : t;
                    double dx = qx - vx * t, dz = qz - vz * t, d2 = dx * dx + dz * dz;
                    if (d2 < best) { best = d2; at = g.y[k] + (g.y[k + 1] - g.y[k]) * t; }
                }
                if (best >= reach * reach) continue;
                h += (at - h) * (1 - Eastland.Smooth(g.halfM, reach, Math.Sqrt(best)));
            }
            return h;
        }

        /// <summary>The ground with the pads laid on it.</summary>
        public static double WithPads(double x, double z, double h)
        {
            foreach (var p in Pads)
            {
                double reach = p.radiusM + p.easeM;
                if (x < Math.Min(p.ax, p.bx) - reach || x > Math.Max(p.ax, p.bx) + reach || z < Math.Min(p.az, p.bz) - reach || z > Math.Max(p.az, p.bz) + reach) continue;
                double d = p.Distance(x, z);
                if (d >= reach) continue;
                h += (p.levelM - h) * (1 - Eastland.Smooth(p.radiusM, reach, d));
            }
            return h;
        }
    }
}
