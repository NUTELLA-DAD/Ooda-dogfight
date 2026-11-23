using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ooda.Flight.Core;

namespace Ooda.Turn.Core
{
    /// <summary>
    /// The lands round the Alps. The island's outline is Island's (one landmass with bays, a ring of islets round it,
    /// Volcano Isle and the named islands); the Alps' terrain is the middle of its west half, and this is all the ground
    /// outside that square: the strips of coast and the islets to its north, south and west, and the east lands.
    ///
    /// The east lands: the island goes on east past the Alps instead of dropping into the sea, out to a coast about 16 km
    /// further on. Wooded foothills come down from the Alps to a reservoir behind a dam, its river running north to the sea;
    /// Red Canyon cuts through sandstone mesas from the desert out to the east coast; the towns and the airfields stand on
    /// Island's pads. Skyline City (City.cs) stands on its north coast, its river running north through it to the sea; south
    /// of the city the river winds up a green valley of fields to an oasis lake near the south coast, between desert
    /// plateaus. On the west bank, on the plateau over the valley, stand three great pyramids in a line (and three little
    /// ones), on the east bank a step pyramid at the end of an avenue of obelisks and two more pyramids further south;
    /// obelisks line the river's banks, and out in the dune sea east of the city a lone pair of pyramids and a ring of
    /// obelisks. The pyramids and the obelisks are part of the ground, like the city's towers: GroundFt is the stone over a
    /// point, so the turn engine, the ghost path, the rounds, the radar and the AI pilots treat them like rock.
    ///
    /// The ground is a pure function (Raw), the same on every machine; it is sampled once into a grid (CellM metres) that
    /// both the drawing (Fx/EastlandScape) and the lookups read, so what you see is what you hit. The Alps' terrain eases
    /// into Raw over its last 1.6 km on every side (AlpineLayout), so the two meet at its edges without a step. Pure C#, immutable once
    /// made, safe to read from any thread. Metres, x east, z north; the flight core's GroundFt takes feet, x east, y north.
    /// </summary>
    public sealed class Eastland
    {
        /// <summary>The Alps' east edge, where the east lands begin; the grid runs from GridX0 (west of the Alps, for the
        /// islets there) to X1 and from Z0 to Z1, with the Alps' own square (AlpsHalf either way from the middle) left to them.</summary>
        public const float X0 = 10000f, X1 = 32000f, Z0 = -14000f, Z1 = 14000f;
        public const float GridX0 = -16000f, AlpsHalf = 10000f;
        /// <summary>The ground grid's spacing, metres, and its samples east to west and north to south.</summary>
        public const float CellM = 25f;
        public const int NX = (int)((X1 - GridX0) / CellM) + 1, NZ = (int)((Z1 - Z0) / CellM) + 1;
        /// <summary>The sea floor off the coast (as the Alps': its Base + 20).</summary>
        public const float SeaFloorM = (float)Island.SeaFloorM;
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

        /// <summary>A rock standing out of the sea (a sea stack), or the lighthouse: round, flat-topped, the ground under it
        /// wherever that is (the sea's bed, mostly).</summary>
        public struct Rock
        {
            public float x, z, half, rise, baseM;
            public bool lighthouse;
            public float TopM => baseM + rise;
            public double At(double px, double pz)
            {
                double dx = px - x, dz = pz - z;
                return dx * dx + dz * dz < half * half ? TopM : double.NegativeInfinity;
            }
        }

        /// <summary>A block of masonry square to the compass (the dam): its middle, its half sides and its top.</summary>
        public struct Block
        {
            public string name;
            public float x, z, hx, hz, topM;
            public double At(double px, double pz) => Math.Abs(px - x) < hx && Math.Abs(pz - z) < hz ? topM : double.NegativeInfinity;
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
            // east of the city too, but only its south half: the north-east corner (the airport's) stays green
            double east = Smooth(City.CentreX + City.HalfX + 700, City.CentreX + City.HalfX + 1900, x) * Smooth(4200, 1800, z + 900 * (Noise(x / 1900 + 6.6, z / 1900 + 1.4) - 0.5));
            // the farmland gives way to scrub and the scrub to sand over 3 km, the line wandering
            return Math.Max(south, east) * Smooth(10700, 13600, x + 1100 * (Noise(x / 2400 + 3.7, z / 2400 + 8.2) - 0.5));
        }

        /// <summary>How much of the river's green valley a point is in, 0 to 1 (its fields, its flat floor).</summary>
        public static double ValleyWeight(double x, double z)
        {
            return (1 - Smooth(950, 1150, Math.Abs(x - RiverX(z)))) * Smooth(LakeZ - LakeRz - 700, LakeZ - LakeRz, z);
        }

        public static readonly Pyramid[] Pyramids = MakePyramids();
        public static readonly Obelisk[] Obelisks = MakeObelisks();
        public static readonly Rock[] Rocks = MakeRocks();
        public static readonly Block[] Blocks =
        {
            new Block { name = "Dam", x = ReservoirX, z = DamZ, hx = DamHalfLengthM, hz = DamHalfThickM, topM = DamTopM },
        };

        /// <summary>The sea stacks off the west coast (a field of them north of the harbour bay, the tallest 120 m), and the
        /// lighthouse at the east end of the lagoon's sand spit.</summary>
        static Rock[] MakeRocks()
        {
            // x, z, half width, top over the sea
            var plan = new (double x, double z, double half, double top)[]
            {
                (-12300, 900, 55, 95), (-12850, 250, 40, 70), (-11900, -250, 60, 120), (-12550, -900, 38, 60),
                (-13150, 1500, 30, 48), (-11650, 1600, 42, 82), (-12200, 2300, 50, 105), (-13400, -300, 26, 40),
                (-11300, 600, 34, 66),
            };
            var list = new List<Rock>();
            foreach (var r in plan)
            {
                float g = (float)Raw0(r.x, r.z);
                list.Add(new Rock { x = (float)r.x, z = (float)r.z, half = (float)r.half, baseM = g, rise = (float)(Math.Max(0, g) + r.top) - g });
            }
            var spit = Island.Pads[Island.Spit];
            list.Add(new Rock { x = (float)spit.bx - 40, z = (float)spit.bz, half = 7, baseM = (float)spit.levelM, rise = 44, lighthouse = true });
            return list.ToArray();
        }

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
            for (double z = -2000; z >= -6300; z -= 700)
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

        // ---------- the foothills, the reservoir and its river ----------

        /// <summary>The reservoir in the foothills east of the Alps: its middle, its half axes and its water's height; the dam
        /// across its north end, and the river that runs from the dam's foot north to the sea.</summary>
        public const float ReservoirX = 10750f, ReservoirZ = 3700f, ReservoirRx = 380f, ReservoirRz = 850f, ReservoirLevelM = 46f;
        public const float DamZ = 4580f, DamHalfLengthM = 330f, DamHalfThickM = 30f, DamTopM = 56f;

        /// <summary>How much of the wooded foothills a point is in, 0 to 1: by the Alps' edge, north of the farmland, fading
        /// out toward the city.</summary>
        public static double FoothillWeight(double x, double z)
        {
            return Smooth(12300, 9900, x) * Smooth(-2200, 600, z + 700 * (Noise(x / 1700 + 9.1, z / 1700 + 2.3) - 0.5));
        }

        /// <summary>The reservoir's shore: 1 on it, less in the water.</summary>
        public static double ReservoirR(double x, double z)
        {
            double ex = (x - ReservoirX) / ReservoirRx, ez = (z - ReservoirZ) / ReservoirRz;
            return Math.Sqrt(ex * ex + ez * ez) * (1 + 0.07 * Math.Sin(3 * Math.Atan2(ez, ex) + 0.4));
        }

        /// <summary>The dam's river: its line north of the dam, and where it starts.</summary>
        public static double DamRiverX(double z) => ReservoirX + 70 * Math.Sin((z - DamZ) / 620);
        public const float DamRiverFromZ = DamZ + DamHalfThickM - 4f, DamRiverHalfM = 55f;

        // ---------- Red Canyon ----------

        /// <summary>Red Canyon's line, from its head in the desert east of the river out to the east coast: a slot through a
        /// belt of sandstone mesas.</summary>
        public static double[] CanyonX => canyonX ??= new double[] { 19600, 20500, 21500, 22400, 23700, 25800 };
        public static double[] CanyonZ => canyonZ ??= new double[] { -1900, -3000, -3250, -4300, -4900, -5300 };
        static double[] canyonX, canyonZ;   // (made on first use: the pyramids' bases ask for the ground before the fields below are set)
        public const float CanyonFloorM = 7f, CanyonHalfFloorM = 60f, CanyonWallM = 115f;

        static double CanyonDistance(double x, double z)
        {
            double best = double.MaxValue;
            for (int k = 0; k + 1 < CanyonX.Length; k++)
            {
                double ax = CanyonX[k], az = CanyonZ[k], vx = CanyonX[k + 1] - ax, vz = CanyonZ[k + 1] - az;
                double t = ((x - ax) * vx + (z - az) * vz) / (vx * vx + vz * vz);
                t = t < 0 ? 0 : t > 1 ? 1 : t;
                double dx = x - (ax + vx * t), dz = z - (az + vz * t);
                best = Math.Min(best, dx * dx + dz * dz);
            }
            return Math.Sqrt(best);
        }

        /// <summary>How much of the mesas a point is on, 0 to 1 (a sharp edge: their cliffs).</summary>
        public static double MesaWeight(double x, double z)
        {
            if (x < CanyonX[0] - 2200 || x > 26400 || z > CanyonZ[0] + 2200 || z < CanyonZ[CanyonZ.Length - 1] - 2200) return 0;
            double d = CanyonDistance(x, z) + 420 * (Noise(x / 520 + 1.3, z / 520 + 7.7) - 0.5);
            return Smooth(0.3, 0.7, 1 - Smooth(950, 1500, d)) * Calm(x, z);
        }

        // ---------- the ground ----------

        /// <summary>
        /// The ground before the pyramids' pads (metres over the sea): a green rolling plain by the Alps and round the city,
        /// wooded foothills north of it with the reservoir among them, the city's ground flat under its streets, desert plateaus
        /// with dunes south and east of it, Red Canyon's mesas and its slot, the river's flat valley and its channel, the oasis
        /// lake, and the island's coast down to the sea floor (Island), with the islets and the volcano off it. Calm (no
        /// dunes) round every monument.
        /// </summary>
        static double Raw0(double x, double z)
        {
            double coast = Island.MainCoastM(x, z);
            double h = SeaFloorM;
            if (coast < 900)
            {
                h = Mainland(x, z);
                h += (SeaFloorM - h) * Island.MainSea(coast);
            }
            double off = Island.OffshoreM(x, z);
            return off > h ? off : h;
        }

        /// <summary>The main island's ground outside the Alps, before its coast.</summary>
        static double Mainland(double x, double z)
        {
            double n1 = Noise(x / 2600 + 13.1, z / 2600 + 7.7), n2 = Noise(x / 700 + 3.3, z / 700 + 91.2);
            double dw = DesertWeight(x, z);
            double green = 10 + 30 * n1 * n1 + 8 * n2;
            // the foothills: rounded wooded ridges, lower toward the city
            double fw = FoothillWeight(x, z);
            if (fw > 0)
            {
                double r = 1 - Math.Abs(2 * Noise(x / 1500 + 21.7, z / 1500 + 5.9) - 1);
                green += fw * (26 + 235 * Math.Pow(r, 1.4) + 30 * n2);
            }
            double plateau = 34 + 30 * n1 + 6 * n2;
            double h = green + (plateau - green) * dw;
            // the mesas: flat-topped, in terraces, their edges cliffs
            double mesa = MesaWeight(x, z);
            // the dunes: transverse ridges across a north-west wind, their crests broken up, none round the monuments
            h += 42 * Dunes(x, z) * dw * (0.55 + 0.6 * n1) * Calm(x, z) * (1 - mesa);
            if (mesa > 0)
            {
                double top = 150 + 70 * Noise(x / 900 + 4.4, z / 900 + 2.2);
                top = 38 * Math.Floor(top / 38) + 6 * n2;
                h += (top - h) * mesa;
                double d = CanyonDistance(x, z) + 26 * (Noise(x / 140 + 6.1, z / 140 + 3.9) - 0.5);
                if (d < CanyonHalfFloorM + CanyonWallM) h = Math.Min(h, CanyonFloorM + (h - CanyonFloorM) * Smooth(CanyonHalfFloorM, CanyonHalfFloorM + CanyonWallM, d));
            }
            else if (x > CanyonX[CanyonX.Length - 2])
            {
                // the canyon's mouth, past the mesas: a wadi down to the shore
                double d = CanyonDistance(x, z);
                if (d < 400) h = Math.Min(h, CanyonFloorM + (h - CanyonFloorM) * Smooth(CanyonHalfFloorM, 400, d));
            }

            // the river's valley: a flat floor of fields, the plateaus stepping down to it
            double v = ValleyWeight(x, z);
            h += (7 + 2 * n2 - h) * v;

            // the city: flat under its streets, easing out over 600 m
            double dcx = Math.Max(0, Math.Abs(x - City.CentreX) - City.HalfX), dcz = Math.Max(0, Math.Abs(z - City.CentreZ) - City.HalfZ);
            double cw = 1 - Smooth(0, 600, Math.Sqrt(dcx * dcx + dcz * dcz));
            h += (City.StreetM - 0.4 - h) * cw;

            // the reservoir: high ground round it for the dam to hold against, its bed shelving, a beach up from the water
            if (Math.Abs(x - ReservoirX) < 2200 && Math.Abs(z - ReservoirZ) < 3200)
            {
                double rr = ReservoirR(x, z);
                h = Math.Max(h, (ReservoirLevelM + 34 + 20 * n2) * (1 - Smooth(1.5, 2.5, rr)));
                if (rr < 1.7 && z < DamZ + 20) h = Math.Min(h, rr < 1 ? ReservoirLevelM - 22 * (1 - rr * rr) : ReservoirLevelM + (rr - 1) * 150);
            }
            // the dam's river: a gorge under the dam widening to a valley, north to the sea (its water is the sea's)
            if (z > DamRiverFromZ && Math.Abs(x - ReservoirX) < 2600)
            {
                double d = Math.Abs(x - DamRiverX(z));
                double slope = 0.3 + (0.12 - 0.3) * Smooth(DamRiverFromZ, DamRiverFromZ + 1900, z);   // (wide at the dam, so its face shows)
                h = Math.Min(h, d < DamRiverHalfM ? RiverBedM : -1.5 + (d - DamRiverHalfM) * slope);
            }

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
            return h;
        }

        /// <summary>The ground (metres over the sea) with the pyramids' flat pads: what the grid is sampled from, and what
        /// the Alps ease into along their east edge. Defined everywhere (west of X0 too), but nothing but the Alps' blend reads it there.</summary>
        public static double Raw(double x, double z) => Island.WithPads(x, z, Island.WithRoads(x, z, Bare(x, z), BareGround));

        static readonly Func<double, double, double> BareGround = Bare;

        /// <summary>The ground before the island's roads and pads are laid on it.</summary>
        static double Bare(double x, double z)
        {
            double h = Raw0(x, z);
            foreach (var p in Pyramids)
            {
                double inner = p.half * 1.12;
                double d = inner + Rounded(x - p.x, z - p.z, inner);   // a square pad with rounded corners: no creases along its diagonals
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
                double d = Rounded(x - p.x, z - p.z, p.half * 1.1);
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
            for (double z = -2000; z >= -6300; z -= 700) { double off = RiverHalf(z) + 70; l.Add(S(RiverX(z) - off, z, 6)); l.Add(S(RiverX(z) + off, z, 6)); }
            foreach (double x in new[] { 16300.0, 16650.0, 17000.0 }) l.Add(S(x, -3000, 6));
            l.Add(S(14250, -3800, 6));
            l.Add(S(22300, 2600, 6));
            for (int k = 0; k < 8; k++) { double a = k * Math.PI / 4 + 0.2; l.Add(S(22300 + 650 * Math.Cos(a), 2600 + 650 * Math.Sin(a), 6)); }
            l.Add(S(23600, -1080, 6));
            return l.ToArray();
        }

        /// <summary>How far a point (dx, dz from the middle) is outside a square of this half side, round its corners (0 inside).</summary>
        static double Rounded(double dx, double dz, double half)
        {
            double ox = Math.Max(0, Math.Abs(dx) - half), oz = Math.Max(0, Math.Abs(dz) - half);
            return Math.Sqrt(ox * ox + oz * oz);
        }

        /// <summary>Transverse dunes, 0 to 1: a gentle windward slope up to the crest, a steep slip face down the lee.</summary>
        static double Dunes(double x, double z)
        {
            double u = (x * 0.8 + z * 0.6) / 330 + 1.2 * Noise(x / 1800 + 4.4, z / 1800 + 0.7) + 0.4 * Noise(x / 600 + 5.1, z / 600 + 1.3);
            double s = u - Math.Floor(u);
            double p = s < 0.75 ? s / 0.75 : (1 - s) / 0.25;
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
        const int MonCellsX = (int)((X1 - GridX0) / MonCellM), MonCellsZ = (int)((Z1 - Z0) / MonCellM);
        readonly int[] monStart, monList;   // index < Pyramids.Length a pyramid, then the obelisks, the rocks and the blocks

        Eastland()
        {
            var g = new float[NX * NZ];
            Parallel.For(0, NZ, j =>
            {
                double z = Z0 + j * CellM;
                // (the Alps' square is theirs: nothing reads the grid inside it but a cell or two along its edges)
                bool alpsRow = z > -AlpsHalf + 2 * CellM && z < AlpsHalf - 2 * CellM;
                for (int i = 0; i < NX; i++)
                {
                    double x = GridX0 + i * CellM;
                    g[j * NX + i] = alpsRow && x > -AlpsHalf + 2 * CellM && x < X0 - 2 * CellM ? 0f : (float)Raw(x, z);
                }
            });
            grid = g;
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var h in g) { lo = Math.Min(lo, h); hi = Math.Max(hi, h); }
            lowestM = lo; highestM = hi;

            var lists = new List<int>[MonCellsX * MonCellsZ];
            void Put(int id, double x, double z, double halfX, double halfZ)
            {
                int i0 = ClX((int)((x - halfX - GridX0) / MonCellM)), i1 = ClX((int)((x + halfX - GridX0) / MonCellM));
                int j0 = ClZ((int)((z - halfZ - Z0) / MonCellM)), j1 = ClZ((int)((z + halfZ - Z0) / MonCellM));
                for (int j = j0; j <= j1; j++) for (int i = i0; i <= i1; i++) (lists[j * MonCellsX + i] ??= new List<int>()).Add(id);
            }
            int id0 = 0;
            for (int k = 0; k < Pyramids.Length; k++) Put(id0 + k, Pyramids[k].x, Pyramids[k].z, Pyramids[k].half, Pyramids[k].half);
            id0 += Pyramids.Length;
            for (int k = 0; k < Obelisks.Length; k++) Put(id0 + k, Obelisks[k].x, Obelisks[k].z, Obelisks[k].half, Obelisks[k].half);
            id0 += Obelisks.Length;
            for (int k = 0; k < Rocks.Length; k++) Put(id0 + k, Rocks[k].x, Rocks[k].z, Rocks[k].half, Rocks[k].half);
            id0 += Rocks.Length;
            for (int k = 0; k < Blocks.Length; k++) Put(id0 + k, Blocks[k].x, Blocks[k].z, Blocks[k].hx, Blocks[k].hz);
            monStart = new int[lists.Length + 1];
            var flat = new List<int>();
            for (int c = 0; c < lists.Length; c++) { monStart[c] = flat.Count; if (lists[c] != null) flat.AddRange(lists[c]); }
            monStart[lists.Length] = flat.Count;
            monList = flat.ToArray();
        }

        static int ClX(int i) => i < 0 ? 0 : i >= MonCellsX ? MonCellsX - 1 : i;
        static int ClZ(int j) => j < 0 ? 0 : j >= MonCellsZ ? MonCellsZ - 1 : j;

        /// <summary>Whether a point (metres) is over these lands at all: inside the grid and off the Alps' own square.</summary>
        public static bool Over(double x, double z) => x >= GridX0 && x < X1 && z >= Z0 && z < Z1 && !InAlps(x, z);

        /// <summary>Whether a point is over the Alps' terrain's square.</summary>
        public static bool InAlps(double x, double z) => x < X0 && x >= -AlpsHalf && z >= -AlpsHalf && z < AlpsHalf;

        /// <summary>The ground's height from the grid (metres over the sea, the sea floor under the water), without the monuments.</summary>
        public double GroundM(double x, double z)
        {
            double fx = (x - GridX0) / CellM, fz = (z - Z0) / CellM;
            if (fx < 0) fx = 0; else if (fx > NX - 1.001) fx = NX - 1.001;
            if (fz < 0) fz = 0; else if (fz > NZ - 1.001) fz = NZ - 1.001;
            int i = (int)fx, j = (int)fz;
            double u = fx - i, w = fz - j;
            int k = j * NX + i;
            double a = grid[k] + (grid[k + 1] - grid[k]) * u, b = grid[k + NX] + (grid[k + NX + 1] - grid[k + NX]) * u;
            return a + (b - a) * w;
        }

        /// <summary>The grid's height at a sample (column i east, row j north).</summary>
        public float Sample(int i, int j) => grid[(j < 0 ? 0 : j >= NZ ? NZ - 1 : j) * NX + (i < 0 ? 0 : i >= NX ? NX - 1 : i)];

        /// <summary>The stone of a pyramid or an obelisk over a point (metres), or NegativeInfinity if there is none.</summary>
        public double MonumentM(double x, double z)
        {
            if (!Over(x, z)) return double.NegativeInfinity;
            int c = ClZ((int)((z - Z0) / MonCellM)) * MonCellsX + ClX((int)((x - GridX0) / MonCellM));
            double h = double.NegativeInfinity;
            for (int k = monStart[c]; k < monStart[c + 1]; k++)
            {
                int id = monList[k];
                double t;
                if (id < Pyramids.Length) t = Pyramids[id].At(x, z);
                else if ((id -= Pyramids.Length) < Obelisks.Length) t = Obelisks[id].At(x, z);
                else if ((id -= Obelisks.Length) < Rocks.Length) t = Rocks[id].At(x, z);
                else t = Blocks[id - Rocks.Length].At(x, z);
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
