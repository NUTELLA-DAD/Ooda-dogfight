using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ooda.Flight.Core;

namespace Ooda.Turn.Core
{
    /// <summary>
    /// The east lands: the island goes on east past the Alps instead of dropping into the sea, out to a coast about 17 km
    /// further on. Skyline City (City.cs) stands on its north coast, its river running north through it to the sea; south
    /// of the city the river winds up a green valley of fields to an oasis lake near the south coast, between desert
    /// plateaus. On the west bank, on the plateau over the valley, stand three great pyramids in a line (and three little
    /// ones), on the east bank a step pyramid at the end of an avenue of obelisks and two more pyramids further south;
    /// obelisks line the river's banks, and out in the dune sea east of the city a lone pair of pyramids and a ring of
    /// obelisks. The pyramids and the obelisks are part of the ground, like the city's towers: GroundFt is the stone over a
    /// point, so the turn engine, the ghost path, the rounds, the radar and the AI pilots treat them like rock.
    ///
    /// The ground is a pure function (Raw), the same on every machine; it is sampled once into a grid (CellM metres) that
    /// both the drawing (Fx/EastlandScape) and the lookups read, so what you see is what you hit. The Alps' terrain eases
    /// into Raw over its last 1.6 km (AlpineLayout), so the two meet at x = X0 without a step. Pure C#, immutable once
    /// made, safe to read from any thread. Metres, x east, z north; the flight core's GroundFt takes feet, x east, y north.
    /// </summary>
    public sealed class Eastland
    {
        /// <summary>The east lands' square: from the Alps' east edge (X0) on east, and as far north and south as the Alps.</summary>
        public const float X0 = 10000f, X1 = 30000f, Z0 = -10000f, Z1 = 10000f;
        /// <summary>The ground grid's spacing, metres, and its samples a side.</summary>
        public const float CellM = 25f;
        public const int N = (int)((X1 - X0) / CellM) + 1;
        /// <summary>The sea floor off the coast (as the Alps': its Base + 20).</summary>
        public const float SeaFloorM = -60f;
        /// <summary>The river's and the oasis lake's bed; the water is the sea's level (0), so the sea shows in them.</summary>
        public const float RiverBedM = -8f;

        /// <summary>A pyramid, square to the compass: its middle, its half side and its top (m), and the ground it stands on.
        /// A step pyramid (steps > 0) rises in that many tiers.</summary>
        public struct Pyramid
        {
            public string name;
            public float x, z, half, rise, baseM;
            public int steps;
            public float TopM => baseM + rise;
            /// <summary>The stone's top at a point (m), or NegativeInfinity off it.</summary>
            public double At(double px, double pz)
            {
                double d = Math.Max(Math.Abs(px - x), Math.Abs(pz - z));
                if (d >= half) return double.NegativeInfinity;
                double up = 1 - d / half;
                if (steps <= 0) return baseM + rise * up;
                return baseM + rise / steps * Math.Min(steps, Math.Floor(up * steps) + 1);
            }
        }

        /// <summary>An obelisk: a square needle on the ground, its half width at the foot, its height (m); granite or limestone.</summary>
        public struct Obelisk
        {
            public float x, z, half, rise, baseM;
            public bool granite;
            public float TopM => baseM + rise;
            public double At(double px, double pz) => Math.Abs(px - x) < half && Math.Abs(pz - z) < half ? TopM : double.NegativeInfinity;
        }

        // ---------- the plan ----------

        /// <summary>The river's line: straight north through the city, winding either side of it to the south.</summary>
        public static double RiverX(double z)
        {
            double south = City.CentreZ - City.HalfZ;
            if (z >= south) return City.CentreX;
            return City.CentreX + 280 * Math.Sin((south - z) / 1100) * Smooth(south, south - 1200, z);
        }

        /// <summary>The river's half width: the city's 200 m, narrowing to 110 m up the valley.</summary>
        public static double RiverHalf(double z)
        {
            double south = City.CentreZ - City.HalfZ;
            return 110 + 90 * Smooth(south - 1000, south, z);
        }

        /// <summary>The oasis lake the river rises in, near the south coast: its middle and half axes.</summary>
        public const float LakeZ = -7350f, LakeRx = 650f, LakeRz = 420f;
        public static double LakeX => RiverX(LakeZ);

        /// <summary>How much of the desert a point is in, 0 (green: the plain by the Alps, round the city) to 1: south of the
        /// city and east of it, past a green strip along the Alps.</summary>
        public static double DesertWeight(double x, double z)
        {
            double south = Smooth(City.CentreZ - City.HalfZ + 400, City.CentreZ - City.HalfZ - 1200, z);
            double east = Smooth(City.CentreX + City.HalfX + 700, City.CentreX + City.HalfX + 1900, x);
            return Math.Max(south, east) * Smooth(11300, 12600, x);
        }

        /// <summary>How much of the river's green valley a point is in, 0 to 1 (its fields, its flat floor).</summary>
        public static double ValleyWeight(double x, double z)
        {
            return (1 - Smooth(800, 1500, Math.Abs(x - RiverX(z)))) * Smooth(LakeZ - LakeRz - 700, LakeZ - LakeRz, z);
        }

        public static readonly Pyramid[] Pyramids = MakePyramids();
        public static readonly Obelisk[] Obelisks = MakeObelisks();

        static Pyramid[] MakePyramids()
        {
            var list = new List<Pyramid>
            {
                // the three on the west bank's plateau, on a line from north-east to south-west, the greatest first
                new Pyramid { name = "Great Pyramid", x = 13300, z = -3800, half = 160, rise = 190 },
                new Pyramid { name = "Second Pyramid", x = 12900, z = -4420, half = 148, rise = 178 },
                new Pyramid { name = "Third Pyramid", x = 12560, z = -4980, half = 86, rise = 98 },
                new Pyramid { name = "Queen's Pyramid", x = 12700, z = -5260, half = 30, rise = 34 },
                new Pyramid { name = "Queen's Pyramid", x = 12790, z = -5260, half = 30, rise = 34 },
                new Pyramid { name = "Queen's Pyramid", x = 12880, z = -5260, half = 30, rise = 34 },
                // the east bank: a step pyramid at the head of the obelisk avenue, two more down the river
                new Pyramid { name = "Step Pyramid", x = 17400, z = -3000, half = 105, rise = 84, steps = 6 },
                new Pyramid { name = "Red Pyramid", x = 18100, z = -6150, half = 140, rise = 150 },
                new Pyramid { name = "Bent Pyramid", x = 18450, z = -5300, half = 110, rise = 118 },
                // out in the dune sea east of the city
                new Pyramid { name = "Lone Pyramid", x = 23600, z = -1500, half = 180, rise = 210 },
                new Pyramid { name = "Lone Pyramid's Queen", x = 24350, z = -2450, half = 90, rise = 104 },
            };
            for (int k = 0; k < list.Count; k++) { var p = list[k]; p.baseM = (float)Raw0(p.x, p.z); list[k] = p; }
            return list.ToArray();
        }

        static Obelisk[] MakeObelisks()
        {
            var list = new List<Obelisk>();
            var rng = new Lcg(1922);
            void Add(double x, double z, double rise, bool granite) => list.Add(new Obelisk { x = (float)x, z = (float)z, half = 6f, rise = (float)rise, granite = granite });
            // the river's banks south of the city, in pairs across the water
            for (double z = -2000; z >= -7000; z -= 700)
            {
                double off = RiverHalf(z) + 70;
                double rise = 52 + 12 * rng.Next();
                Add(RiverX(z) - off, z, rise, true);
                Add(RiverX(z) + off, z, rise, true);
            }
            // the avenue from the river's east bank to the step pyramid
            foreach (double x in new[] { 16300.0, 16650.0, 17000.0 })
            {
                Add(x, -3000 - 70, 46, false);
                Add(x, -3000 + 70, 46, false);
            }
            // the great pyramids' valley temple, where the causeway meets the valley
            Add(14250, -3700, 58, true);
            Add(14250, -3900, 58, true);
            // the ring in the dune sea east of the city, a taller one in its middle
            const double rx = 22300, rz = 2600, r = 650;
            for (int k = 0; k < 8; k++)
            {
                double a = k * Math.PI / 4 + 0.2;
                Add(rx + r * Math.Cos(a), rz + r * Math.Sin(a), 70 + 26 * rng.Next(), k % 2 == 0);
            }
            Add(rx, rz, 128, true);
            // a pair before the lone pyramid
            Add(23600 - 60, -1500 + 420, 64, false);
            Add(23600 + 60, -1500 + 420, 64, false);
            for (int k = 0; k < list.Count; k++) { var o = list[k]; o.baseM = (float)Raw0(o.x, o.z); list[k] = o; }
            return list.ToArray();
        }

        // ---------- the ground ----------

        /// <summary>
        /// The ground before the pyramids' pads (metres over the sea): a green rolling plain by the Alps and round the city,
        /// the city's ground flat under its streets, desert plateaus with dunes south and east of it, the river's flat valley
        /// and its channel, the oasis lake, and the coast down to the sea floor. Calm (no dunes) round every monument.
        /// </summary>
        static double Raw0(double x, double z)
        {
            double n1 = Noise(x / 2600 + 13.1, z / 2600 + 7.7), n2 = Noise(x / 700 + 3.3, z / 700 + 91.2);
            double dw = DesertWeight(x, z);
            double green = 10 + 30 * n1 * n1 + 8 * n2;
            double plateau = 34 + 30 * n1 + 6 * n2;
            double h = green + (plateau - green) * dw;
            // the dunes: transverse ridges across a north-west wind, their crests broken up, none round the monuments
            h += 26 * Dunes(x, z) * dw * (0.5 + 0.7 * n1) * Calm(x, z);

            // the river's valley: a flat floor of fields, the plateaus stepping down to it
            double v = ValleyWeight(x, z);
            h += (7 + 2 * n2 - h) * v;

            // the city: flat under its streets, easing out over 600 m
            double dcx = Math.Max(0, Math.Abs(x - City.CentreX) - City.HalfX), dcz = Math.Max(0, Math.Abs(z - City.CentreZ) - City.HalfZ);
            double cw = 1 - Smooth(0, 600, Math.Sqrt(dcx * dcx + dcz * dcz));
            h += (City.StreetM - 0.4 - h) * cw;

            // the river's channel and the oasis lake: their beds under the sea's level, the banks rising gently from the water
            if (z > LakeZ)
            {
                double d = Math.Abs(x - RiverX(z)), half = RiverHalf(z);
                h = Math.Min(h, d < half ? RiverBedM : -1.5 + (d - half) * 0.12);
            }
            {
                double ex = (x - LakeX) / LakeRx, ez = (z - LakeZ) / LakeRz;
                double r = Math.Sqrt(ex * ex + ez * ez) * (1 + 0.08 * Math.Sin(3 * Math.Atan2(ez, ex) + 1.3));
                if (r < 1.8) h = Math.Min(h, r < 1 ? RiverBedM : -1.5 + (r - 1) * 60);
            }

            // the coast: as the Alps' to the north and south, and about 15 km on to the east
            double c = Math.Max(Math.Abs(z) / 10000, (x - X0) / 18000) + 0.04 * (Noise(x / 2500 + 2.2, z / 2500 + 6.1) - 0.5)
                       + 0.05 * Smooth(X0, X0 + 3000, x) * (Noise(x / 5200 + 7.9, z / 5200 + 3.3) - 0.5);   // bays and capes, but none at the Alps' edge
            h += (SeaFloorM - h) * Smooth(0.84, 0.98, c);
            return h;
        }

        /// <summary>The ground (metres over the sea) with the pyramids' flat pads: what the grid is sampled from, and what
        /// the Alps ease into along their east edge. Defined everywhere (west of X0 too), but nothing but the Alps' blend reads it there.</summary>
        public static double Raw(double x, double z)
        {
            double h = Raw0(x, z);
            foreach (var p in Pyramids)
            {
                double d = Math.Max(Math.Abs(x - p.x), Math.Abs(z - p.z));
                double inner = p.half * 1.12;
                if (d > inner + 260) continue;
                double w = 1 - Smooth(inner, inner + 260, d);
                h += (p.baseM - h) * w;
            }
            return h;
        }

        /// <summary>1 out in the open, 0 at a monument: the dunes fade out round each.</summary>
        static double Calm(double x, double z)
        {
            double k = 1;
            foreach (var p in PyramidSpots)
            {
                double d = Math.Max(Math.Abs(x - p.x), Math.Abs(z - p.z)) - p.half * 1.1;
                if (d < 700) k = Math.Min(k, Smooth(100, 700, d));
            }
            foreach (var o in ObeliskSpots)
            {
                double dx = x - o.x, dz = z - o.z;
                if (dx * dx + dz * dz < 240 * 240) k = Math.Min(k, Smooth(40, 240, Math.Sqrt(dx * dx + dz * dz)));
            }
            return k;
        }

        // where the monuments stand, before their bases are known (the calm needs only where, not how high)
        struct Spot { public double x, z, half; }
        static Spot[] pyramidSpots, obeliskSpots;
        static Spot[] PyramidSpots => pyramidSpots ??= new[]
        {
            S(13300, -3800, 160), S(12900, -4420, 148), S(12560, -4980, 86), S(12790, -5260, 120),
            S(17400, -3000, 105), S(18100, -6150, 140), S(18450, -5300, 110), S(23600, -1500, 180), S(24350, -2450, 90),
        };
        static Spot[] ObeliskSpots => obeliskSpots ??= MakeObeliskSpots();
        static Spot S(double x, double z, double half) => new Spot { x = x, z = z, half = half };
        static Spot[] MakeObeliskSpots()
        {
            // the river avenue and the step pyramid's, the valley temple, the ring and the lone pyramid's pair, as MakeObelisks lays them
            var l = new List<Spot>();
            for (double z = -2000; z >= -7000; z -= 700) { double off = RiverHalf(z) + 70; l.Add(S(RiverX(z) - off, z, 6)); l.Add(S(RiverX(z) + off, z, 6)); }
            foreach (double x in new[] { 16300.0, 16650.0, 17000.0 }) l.Add(S(x, -3000, 6));
            l.Add(S(14250, -3800, 6));
            l.Add(S(22300, 2600, 6));
            for (int k = 0; k < 8; k++) { double a = k * Math.PI / 4 + 0.2; l.Add(S(22300 + 650 * Math.Cos(a), 2600 + 650 * Math.Sin(a), 6)); }
            l.Add(S(23600, -1080, 6));
            return l.ToArray();
        }

        /// <summary>Transverse dunes, 0 to 1: a gentle windward slope up to the crest, a steep slip face down the lee.</summary>
        static double Dunes(double x, double z)
        {
            double u = (x * 0.8 + z * 0.6) / 420 + 1.2 * Noise(x / 1800 + 4.4, z / 1800 + 0.7) + 0.4 * Noise(x / 600 + 5.1, z / 600 + 1.3);
            double s = u - Math.Floor(u);
            double p = s < 0.72 ? s / 0.72 : (1 - s) / 0.28;
            p = p * p * (3 - 2 * p);
            return p * (0.55 + 0.45 * Noise(x / 900 + 9.2, z / 900 + 4.6));
        }

        // ---------- the grid and the lookups ----------

        static Eastland land;
        static readonly object gate = new object();

        /// <summary>The east lands, sampled once (the first call, a fraction of a second) and shared.</summary>
        public static Eastland Land
        {
            get
            {
                if (land != null) return land;
                lock (gate) { if (land == null) land = new Eastland(); }
                return land;
            }
        }

        /// <summary>The ground's heights, metres, [row (z) * N + column (x)].</summary>
        public readonly float[] grid;
        public readonly float lowestM, highestM;

        // a hash of the monuments by square cells, for the lookups
        const float MonCellM = 250f;
        const int MonCells = (int)((X1 - X0) / MonCellM);
        readonly int[] monStart, monList;   // index < Pyramids.Length a pyramid, else an obelisk

        Eastland()
        {
            var g = new float[N * N];
            Parallel.For(0, N, j =>
            {
                double z = Z0 + j * CellM;
                for (int i = 0; i < N; i++) g[j * N + i] = (float)Raw(X0 + i * CellM, z);
            });
            grid = g;
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var h in g) { lo = Math.Min(lo, h); hi = Math.Max(hi, h); }
            lowestM = lo; highestM = hi;

            var lists = new List<int>[MonCells * MonCells];
            void Put(int id, double x, double z, double half)
            {
                int i0 = Cl((int)((x - half - X0) / MonCellM)), i1 = Cl((int)((x + half - X0) / MonCellM));
                int j0 = Cl((int)((z - half - Z0) / MonCellM)), j1 = Cl((int)((z + half - Z0) / MonCellM));
                for (int j = j0; j <= j1; j++) for (int i = i0; i <= i1; i++) (lists[j * MonCells + i] ??= new List<int>()).Add(id);
            }
            for (int k = 0; k < Pyramids.Length; k++) Put(k, Pyramids[k].x, Pyramids[k].z, Pyramids[k].half);
            for (int k = 0; k < Obelisks.Length; k++) Put(Pyramids.Length + k, Obelisks[k].x, Obelisks[k].z, Obelisks[k].half);
            monStart = new int[lists.Length + 1];
            var flat = new List<int>();
            for (int c = 0; c < lists.Length; c++) { monStart[c] = flat.Count; if (lists[c] != null) flat.AddRange(lists[c]); }
            monStart[lists.Length] = flat.Count;
            monList = flat.ToArray();
        }

        static int Cl(int i) => i < 0 ? 0 : i >= MonCells ? MonCells - 1 : i;

        /// <summary>Whether a point (metres) is over the east lands' square at all.</summary>
        public static bool Over(double x, double z) => x >= X0 && x < X1 && z >= Z0 && z < Z1;

        /// <summary>The ground's height from the grid (metres over the sea, the sea floor under the water), without the monuments.</summary>
        public double GroundM(double x, double z)
        {
            double fx = (x - X0) / CellM, fz = (z - Z0) / CellM;
            if (fx < 0) fx = 0; else if (fx > N - 1.001) fx = N - 1.001;
            if (fz < 0) fz = 0; else if (fz > N - 1.001) fz = N - 1.001;
            int i = (int)fx, j = (int)fz;
            double u = fx - i, w = fz - j;
            int k = j * N + i;
            double a = grid[k] + (grid[k + 1] - grid[k]) * u, b = grid[k + N] + (grid[k + N + 1] - grid[k + N]) * u;
            return a + (b - a) * w;
        }

        /// <summary>The grid's height at a sample (column i east, row j north).</summary>
        public float Sample(int i, int j) => grid[(j < 0 ? 0 : j >= N ? N - 1 : j) * N + (i < 0 ? 0 : i >= N ? N - 1 : i)];

        /// <summary>The stone of a pyramid or an obelisk over a point (metres), or NegativeInfinity if there is none.</summary>
        public double MonumentM(double x, double z)
        {
            if (!Over(x, z)) return double.NegativeInfinity;
            int c = Cl((int)((z - Z0) / MonCellM)) * MonCells + Cl((int)((x - X0) / MonCellM));
            double h = double.NegativeInfinity;
            for (int k = monStart[c]; k < monStart[c + 1]; k++)
            {
                int id = monList[k];
                double t = id < Pyramids.Length ? Pyramids[id].At(x, z) : Obelisks[id - Pyramids.Length].At(x, z);
                if (t > h) h = t;
            }
            return h;
        }

        /// <summary>The top of whatever is at a point (metres over the sea): the ground or the stone on it. Off the east lands, NegativeInfinity
        /// (the Alps, or the city, decide).</summary>
        public double HeightM(double x, double z)
        {
            if (!Over(x, z)) return double.NegativeInfinity;
            return Math.Max(GroundM(x, z), MonumentM(x, z));
        }

        /// <summary>The same in the flight core's frame (feet, x east, y north), at the sea's surface over the water. Off the east lands, NegativeInfinity.</summary>
        public double GroundFt(double xFt, double yFt)
        {
            double h = HeightM(xFt * Atmosphere.MPerFt, yFt * Atmosphere.MPerFt);
            return double.IsNegativeInfinity(h) ? h : Math.Max(0, h) * Atmosphere.FtPerM;
        }

        /// <summary>Whether a point (feet) is over a pyramid or an obelisk: a crash there hit a monument.</summary>
        public bool OverMonumentFt(double xFt, double yFt)
        {
            double x = xFt * Atmosphere.MPerFt, z = yFt * Atmosphere.MPerFt;
            return MonumentM(x, z) > GroundM(x, z) + 0.5;
        }

        // ---------- helpers ----------

        /// <summary>0 at a, 1 at b (either way round), smooth between.</summary>
        public static double Smooth(double a, double b, double x)
        {
            double t = (x - a) / (b - a);
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            return t * t * (3 - 2 * t);
        }

        /// <summary>Smooth value noise, 0 to 1, the same on every machine.</summary>
        public static double Noise(double x, double y)
        {
            double fx = Math.Floor(x), fy = Math.Floor(y);
            int ix = (int)fx, iy = (int)fy;
            double u = x - fx, v = y - fy;
            u = u * u * (3 - 2 * u); v = v * v * (3 - 2 * v);
            double a = Hash(ix, iy), b = Hash(ix + 1, iy), c = Hash(ix, iy + 1), d = Hash(ix + 1, iy + 1);
            return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
        }

        public static double Hash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xffffff) / 16777216.0;
            }
        }
    }
}
