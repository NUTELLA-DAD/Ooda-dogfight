using System;
using System.Collections.Generic;
using Ooda.Flight.Core;

namespace Ooda.Turn.Core
{
    /// <summary>
    /// The skyscraper city: a dense grid of towers on a flat island of its own out at sea, east of the Alps, with a wide
    /// river down its middle (the start corridor), narrow street canyons, wide avenues and boulevards, a tall downtown
    /// and a second cluster of towers in midtown. Every tower is a stack of boxes (its setbacks), all standing on the
    /// ground, so the city is a height field: GroundFt is the roof under a point (or the street), and the turn engine,
    /// the ghost preview, the rounds, the radar's line of sight and the AI pilots all treat a tower like a mountain.
    /// The layout comes from a fixed seed, the same on every machine (a game over the network stays in step).
    /// Pure C#, immutable once made, safe to read from any thread. Metres, x east, z north (Unity's map frame); the
    /// flight core's GroundFt takes feet, x east, y north.
    /// </summary>
    public sealed class City
    {
        /// <summary>One box of a tower, standing on the ground: x0..x1 east, z0..z1 north, up to topM over the sea.
        /// baseM is where the box shows from (the tier under it ends there); for the collision every box starts at the ground.</summary>
        public struct Box
        {
            public float x0, z0, x1, z1, baseM, topM;
            /// <summary>The facade (0, 1 glass; 2 stone; 3 brick; 4 concrete), or -1 for a bridge or a pylon (concrete, no windows).</summary>
            public sbyte style;
            /// <summary>Which tower it belongs to (its tiers share it).</summary>
            public int tower;
            public bool Contains(double x, double z) => x >= x0 && x < x1 && z >= z0 && z < z1;
        }

        /// <summary>A flat patch of the ground: a block's pavement, a park, a plaza.</summary>
        public struct Pad { public float x0, z0, x1, z1; public byte kind; }   // kind 0 pavement, 1 park, 2 plaza

        /// <summary>The middle of the city (and of its arena), metres.</summary>
        public const float CentreX = 32000f, CentreZ = 0f;
        /// <summary>How far it reaches either side of the middle, east-west and north-south, metres.</summary>
        public const float HalfX = 3600f, HalfZ = 4400f;
        /// <summary>The ground: the streets and the island stand this high over the sea, metres.</summary>
        public const float StreetM = 4f;
        /// <summary>The river down the middle (north-south), its water this far either side of the centre line, and the
        /// riverside road out to RiverRoadM: no tower stands closer to the centre line than that.</summary>
        public const float RiverHalfM = 200f, RiverRoadM = 270f;

        public readonly Box[] boxes;
        public readonly Pad[] pads;
        public readonly int towers;
        public readonly float tallestM;

        // a hash of the boxes by square cells, for the height lookups
        const float CellM = 40f;
        readonly int cellsX, cellsZ;
        readonly int[] cellStart, cellBoxes;
        readonly float minX, minZ, maxX, maxZ;

        static City skyline;
        static readonly object gate = new object();

        /// <summary>The city, made once (the first call) and shared.</summary>
        public static City Skyline
        {
            get
            {
                if (skyline != null) return skyline;
                lock (gate) { if (skyline == null) skyline = Make(1937); }
                return skyline;
            }
        }

        City(List<Box> b, List<Pad> p, int towerCount)
        {
            boxes = b.ToArray(); pads = p.ToArray(); towers = towerCount;
            minX = CentreX - HalfX; maxX = CentreX + HalfX; minZ = CentreZ - HalfZ; maxZ = CentreZ + HalfZ;
            cellsX = (int)Math.Ceiling((maxX - minX) / CellM); cellsZ = (int)Math.Ceiling((maxZ - minZ) / CellM);
            var lists = new List<int>[cellsX * cellsZ];
            for (int k = 0; k < boxes.Length; k++)
            {
                var q = boxes[k];
                tallestM = Math.Max(tallestM, q.topM);
                int i0 = Clamp((int)((q.x0 - minX) / CellM), cellsX), i1 = Clamp((int)((q.x1 - minX) / CellM), cellsX);
                int j0 = Clamp((int)((q.z0 - minZ) / CellM), cellsZ), j1 = Clamp((int)((q.z1 - minZ) / CellM), cellsZ);
                for (int j = j0; j <= j1; j++)
                    for (int i = i0; i <= i1; i++)
                        (lists[j * cellsX + i] ??= new List<int>()).Add(k);
            }
            cellStart = new int[cellsX * cellsZ + 1];
            var flat = new List<int>();
            for (int c = 0; c < lists.Length; c++)
            {
                cellStart[c] = flat.Count;
                if (lists[c] != null) flat.AddRange(lists[c]);
            }
            cellStart[lists.Length] = flat.Count;
            cellBoxes = flat.ToArray();
        }

        static int Clamp(int i, int n) => i < 0 ? 0 : i >= n ? n - 1 : i;

        /// <summary>Whether a point (metres) is over the city's island at all.</summary>
        public bool Over(double x, double z) => x >= minX && x < maxX && z >= minZ && z < maxZ;

        /// <summary>The top of whatever stands at a point (metres over the sea): a roof, the street, or the river's water
        /// (0). Off the city's island, NegativeInfinity (the rest of the world decides).</summary>
        public double HeightM(double x, double z)
        {
            if (x < minX || x >= maxX || z < minZ || z >= maxZ) return double.NegativeInfinity;
            double h = Math.Abs(x - CentreX) < RiverHalfM ? 0 : StreetM;
            int c = (int)((z - minZ) / CellM) * cellsX + (int)((x - minX) / CellM);
            for (int k = cellStart[c]; k < cellStart[c + 1]; k++)
            {
                ref readonly var q = ref boxes[cellBoxes[k]];
                if (q.topM > h && q.Contains(x, z)) h = q.topM;
            }
            return h;
        }

        /// <summary>The same in the flight core's frame: feet, x east, y north. Off the island, NegativeInfinity.</summary>
        public double GroundFt(double xFt, double yFt)
        {
            double h = HeightM(xFt * Atmosphere.MPerFt, yFt * Atmosphere.MPerFt);
            return double.IsNegativeInfinity(h) ? h : h * Atmosphere.FtPerM;
        }

        /// <summary>Whether a point (feet) is over a tower or a bridge rather than the street or the river: a crash there hit a building.</summary>
        public bool OverBuildingFt(double xFt, double yFt) => HeightM(xFt * Atmosphere.MPerFt, yFt * Atmosphere.MPerFt) > StreetM + 0.5;

        // ---------- the layout ----------

        /// <summary>
        /// Lay the city out from a seed. Across: the river, the riverside roads, then blocks either side out to the edge,
        /// 140 to 210 m wide, with 50 m streets between and a 95 m avenue every third. Along: the same, with three 120 m
        /// boulevards crossing the whole city (and the river, on bridges with tall pylons). A block is a park now and then;
        /// else one to four towers, as tall as their place says: a downtown peak by the river, a midtown cluster to the
        /// south-east, mid-rise round them and low blocks at the edges. The tall ones step back as they rise.
        /// </summary>
        public static City Make(uint seed)
        {
            var rng = new Lcg(seed);
            var boxes = new List<Box>();
            var pads = new List<Pad>();
            int tower = 0;

            // the rows along z (north-south): blocks between streets, three boulevards at fixed places
            double[] boulevards = { -1700, 1500, 3300 };
            var rows = Strips(-HalfZ + 60, HalfZ - 60, boulevards, 120, ref rng);
            foreach (int east in new[] { -1, 1 })
            {
                var cols = Strips(RiverRoadM, HalfX - 60, new double[0], 0, ref rng);
                foreach (var (c0, c1) in cols)
                    foreach (var (r0, r1) in rows)
                    {
                        float x0 = CentreX + (float)(east > 0 ? c0 : -c1), x1 = CentreX + (float)(east > 0 ? c1 : -c0);
                        float z0 = CentreZ + (float)r0, z1 = CentreZ + (float)r1;
                        double mx = (x0 + x1) / 2 - CentreX, mz = (z0 + z1) / 2 - CentreZ;
                        double tall = Tallness(mx, mz);
                        // parks: more of them out where it is low, and a big central one north of downtown
                        bool central = Math.Abs(mx) < 900 && mz > 2000 && mz < 3200;
                        if (central || rng.Next() < 0.05 + 0.08 * (1 - tall))
                        {
                            pads.Add(new Pad { x0 = x0, z0 = z0, x1 = x1, z1 = z1, kind = 1 });
                            continue;
                        }
                        pads.Add(new Pad { x0 = x0, z0 = z0, x1 = x1, z1 = z1, kind = 0 });
                        // the lots: split along the longer side into one to three, the short way in two if it is big
                        const float set = 6f;   // a pavement round the buildings
                        float bx0 = x0 + set, bx1 = x1 - set, bz0 = z0 + set, bz1 = z1 - set;
                        bool alongX = bx1 - bx0 >= bz1 - bz0;
                        int n = tall > 0.6 ? 1 + (int)(rng.Next() * 2) : 1 + (int)(rng.Next() * 3);
                        int m = (alongX ? bz1 - bz0 : bx1 - bx0) > 150 && rng.Next() < 0.5 ? 2 : 1;
                        for (int a = 0; a < n; a++)
                            for (int b = 0; b < m; b++)
                            {
                                float u0 = a / (float)n, u1 = (a + 1) / (float)n, v0 = b / (float)m, v1 = (b + 1) / (float)m;
                                float lx0, lx1, lz0, lz1;
                                if (alongX) { lx0 = Lerp(bx0, bx1, u0); lx1 = Lerp(bx0, bx1, u1); lz0 = Lerp(bz0, bz1, v0); lz1 = Lerp(bz0, bz1, v1); }
                                else { lz0 = Lerp(bz0, bz1, u0); lz1 = Lerp(bz0, bz1, u1); lx0 = Lerp(bx0, bx1, v0); lx1 = Lerp(bx0, bx1, v1); }
                                // a gap between neighbours on the lot (an alley), and now and then a lot left as a plaza
                                const float gap = 5f;
                                if (a > 0) { if (alongX) lx0 += gap; else lz0 += gap; }
                                if (b > 0) { if (alongX) lz0 += gap; else lx0 += gap; }
                                if (n * m > 1 && rng.Next() < 0.08) continue;
                                Tower(boxes, ref rng, tower++, lx0, lz0, lx1, lz1, tall);
                            }
                    }
            }

            // the bridges over the river on the boulevards: a deck and two tall pylons each, the pylons beside the water
            foreach (double bz in boulevards)
            {
                float z = CentreZ + (float)bz;
                boxes.Add(new Box { x0 = CentreX - RiverRoadM, x1 = CentreX + RiverRoadM, z0 = z - 14, z1 = z + 14, baseM = 22, topM = 28, style = -1, tower = tower });
                foreach (int s in new[] { -1, 1 })
                {
                    float px = CentreX + s * (RiverHalfM - 25);
                    boxes.Add(new Box { x0 = px - 8, x1 = px + 8, z0 = z - 8, z1 = z + 8, baseM = 0, topM = 135, style = -1, tower = tower });
                }
                tower++;
            }
            // the river's embankments and the riverside roads: plain pavement pads (the water between is the sea showing)
            foreach (int s in new[] { -1, 1 })
            {
                float a = CentreX + s * RiverHalfM, b = CentreX + s * RiverRoadM;
                pads.Add(new Pad { x0 = Math.Min(a, b), x1 = Math.Max(a, b), z0 = CentreZ - HalfZ, z1 = CentreZ + HalfZ, kind = 2 });
            }
            return new City(boxes, pads, tower);
        }

        /// <summary>
        /// How tall this part of the city is, 0 (the low edges) to 1 (the downtown peak): downtown by the river a little north
        /// of the middle, a midtown cluster south-east, mid-rise between, falling off to the edges.
        /// </summary>
        public static double Tallness(double mx, double mz)
        {
            double Peak(double cx, double cz, double r) { double dx = (mx - cx) / r, dz = (mz - cz) / r; return Math.Exp(-(dx * dx + dz * dz)); }
            double t = Math.Max(Peak(250, 300, 1500), 0.75 * Peak(2000, -2600, 1000));
            t = Math.Max(t, 0.45 * Peak(-2300, -2000, 1100));   // an older west side, mid-rise
            double edge = Math.Max(Math.Abs(mx) / HalfX, Math.Abs(mz) / HalfZ);
            return Math.Max(0, Math.Min(1, t * (1 - 0.6 * Math.Max(0, edge - 0.7) / 0.3)));
        }

        /// <summary>Strips from a to b (metres along an axis): blocks 140 to 210 m wide, a 50 m street after each and a 95 m
        /// avenue after every third; at each of the fixed places a wide street (wideM) is cut instead.</summary>
        static List<(double, double)> Strips(double a, double b, double[] wide, double wideM, ref Lcg rng)
        {
            var list = new List<(double, double)>();
            double at = a;
            int k = 0;
            while (at < b - 100)
            {
                double w = 140 + 70 * rng.Next();
                double end = Math.Min(b, at + w);
                // a fixed wide street inside this block: end it there, and go on past the street
                double cut = double.NaN;
                foreach (double c in wide) if (c + wideM / 2 > at && c - wideM / 2 < end + 50) { cut = c; break; }
                if (!double.IsNaN(cut))
                {
                    if (cut - wideM / 2 - at > 60) list.Add((at, cut - wideM / 2));
                    at = Math.Max(at, cut + wideM / 2);
                    k = 0;
                    continue;
                }
                list.Add((at, end));
                k++;
                at = end + (k % 3 == 0 ? 95 : 50);
            }
            return list;
        }

        static float Lerp(float a, float b, float t) => a + (b - a) * t;

        /// <summary>One tower on its lot: its height from how tall this part of the city is (and a little luck), its facade,
        /// and setbacks as it rises if it is tall, with now and then a spire on the tallest.</summary>
        static void Tower(List<Box> boxes, ref Lcg rng, int id, float x0, float z0, float x1, float z1, double tall)
        {
            if (x1 - x0 < 18 || z1 - z0 < 18) return;
            double luck = rng.Next();
            double h = 22 + tall * 380 * (0.45 + 0.75 * luck);
            if (tall > 0.7 && rng.Next() < 0.12) h *= 1.3;        // a landmark
            h = Math.Min(h, 560);
            float top = (float)(StreetM + h);
            sbyte style = (sbyte)(h > 170 ? (rng.Next() < 0.5 ? 0 : 1) : h > 80 ? (sbyte)(rng.Next() < 0.35 ? 1 : rng.Next() < 0.5 ? 2 : 4) : (sbyte)(rng.Next() < 0.5 ? 3 : rng.Next() < 0.6 ? 2 : 4));
            // a lot is not always filled to its edges: the tall ones stand back a little more
            float inset = (float)(rng.Next() * (h > 150 ? 10 : 4));
            x0 += inset; z0 += inset; x1 -= inset; z1 -= inset;
            if (h > 130 && rng.Next() < 0.75)
            {
                // setbacks: the base to 45-60 %, a tier stepped in to about 85 %, the crown stepped in again
                float t1 = (float)(StreetM + h * R01(ref rng, 0.45, 0.6)), t2 = (float)(StreetM + h * R01(ref rng, 0.78, 0.88));
                float w = Math.Min(x1 - x0, z1 - z0);
                float s1 = w * 0.12f, s2 = w * 0.26f;
                boxes.Add(new Box { x0 = x0, z0 = z0, x1 = x1, z1 = z1, baseM = StreetM, topM = t1, style = style, tower = id });
                boxes.Add(new Box { x0 = x0 + s1, z0 = z0 + s1, x1 = x1 - s1, z1 = z1 - s1, baseM = t1, topM = t2, style = style, tower = id });
                boxes.Add(new Box { x0 = x0 + s2, z0 = z0 + s2, x1 = x1 - s2, z1 = z1 - s2, baseM = t2, topM = top, style = style, tower = id });
                if (h > 300 && rng.Next() < 0.6)
                {
                    float cx = (x0 + x1) / 2, cz = (z0 + z1) / 2;
                    boxes.Add(new Box { x0 = cx - 2.5f, z0 = cz - 2.5f, x1 = cx + 2.5f, z1 = cz + 2.5f, baseM = top, topM = top + (float)(25 + 35 * rng.Next()), style = -1, tower = id });
                }
            }
            else
            {
                boxes.Add(new Box { x0 = x0, z0 = z0, x1 = x1, z1 = z1, baseM = StreetM, topM = top, style = style, tower = id });
                // a plant room on the roof of a mid-rise
                if (h > 40 && rng.Next() < 0.5)
                {
                    float w = Math.Min(x1 - x0, z1 - z0) * 0.3f, cx = (x0 + x1) / 2 + (float)(rng.Next() - 0.5) * w, cz = (z0 + z1) / 2;
                    boxes.Add(new Box { x0 = cx - w / 2, z0 = cz - w / 2, x1 = cx + w / 2, z1 = cz + w / 2, baseM = top, topM = top + 6, style = 4, tower = id });
                }
            }
        }

        static double R01(ref Lcg rng, double a, double b) => a + (b - a) * rng.Next();
    }
}
