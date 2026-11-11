using System;
using System.Collections.Generic;

namespace Ooda.Turn.Core
{
    /// <summary>
    /// The cloud sea: a vast skyscape of toon cloud out over the open sea, south-east of the island, for the Cloud Sea arena.
    /// A rolling deck of cloud lies under the fight with holes down to the sea; towering cumulus stand up out of it on every
    /// side, a few of them grown into thunderheads with anvils and rain; loose heaps of fair-weather cumulus drift at the
    /// heights the fight is flown at; and streaks of cirrus run high above. The start line is kept clear (a corridor along the
    /// arena's axis), so every start begins in open air with towers either side.
    ///
    /// Each cloud is a bank of puffs, the same round, inked puffs as the island's toon clouds (Fx/ToonSky, ToonCloud.shader):
    /// Fx/CloudScape draws them, and Density tells how deep in cloud a point is (the screen whites out as you fly in). The
    /// clouds stand still (they billow, they do not drift), so a cloud you saw is still there when you get to it. The layout
    /// comes from a fixed seed, the same on every machine. Pure C#, immutable once made, safe to read from any thread. Metres,
    /// x east, y up, z north (Unity's frame).
    /// </summary>
    public sealed class Skyscape
    {
        /// <summary>The kinds of cloud, as ToonSky.Kind and ToonCloud.shader number them.</summary>
        public const byte Cumulus = 0, Storm = 1, Cirrus = 2, Low = 3, Rain = 4;

        /// <summary>One puff: its middle, its radius (the stretch widens it across), the height it is cut flat at, its bank.</summary>
        public struct Puff
        {
            public float x, y, z, r, stretch, cutY;
            public byte kind;
            public int bank;
        }

        /// <summary>One cloud: its heart (the middle the puffs are laid round), its base and top, its kind, and its puffs
        /// (puffs[first] to puffs[first + count - 1]).</summary>
        public struct Bank
        {
            public float x, y, z, baseY, topY;
            public byte kind;
            public int first, count;
        }

        /// <summary>The middle of the cloud sea (and of its arena), metres.</summary>
        public const float CentreX = 20000f, CentreZ = -20000f;
        /// <summary>How far out from the middle the clouds reach, metres (a disc).</summary>
        public const float RadiusM = 12000f;
        /// <summary>The cloud deck: its flat base, and about where its puffs' tops roll (the highest reach a little over), metres over the sea.</summary>
        public const float DeckBaseM = 420f, DeckTopM = 1150f;
        /// <summary>The start line's direction, degrees clockwise from north, and the corridor along it kept clear of cloud.</summary>
        public const double AxisDeg = 60;
        public const float CorridorHalfLengthM = 3900f, CorridorHalfWidthM = 1300f;
        /// <summary>The usual (Medium) start height, ft: Low (-1100) and a low perch start over the deck, High (+4400) up among the tower tops.</summary>
        public const double StartAltFt = 7500;

        public readonly Puff[] puffs;
        public readonly Bank[] banks;
        public readonly int towers, storms, heaps;

        // a hash of the puffs by square cells over the sea, for Density
        const float CellM = 400f;
        readonly int cells;
        readonly float minX, minZ;
        readonly int[] cellStart, cellPuffs;

        static Skyscape sky;
        static readonly object gate = new object();

        /// <summary>The cloud sea, made once (the first call) and shared.</summary>
        public static Skyscape Sky
        {
            get
            {
                if (sky != null) return sky;
                lock (gate) { if (sky == null) sky = Make(1944); }
                return sky;
            }
        }

        /// <summary>Whether a point (metres, x east, z north) is out over the cloud sea.</summary>
        public static bool Over(double xM, double zM)
        {
            double dx = xM - CentreX, dz = zM - CentreZ;
            return dx * dx + dz * dz < (RadiusM + 1500.0) * (RadiusM + 1500.0);
        }

        /// <summary>How far along the start line and across it a point is from the middle, metres.</summary>
        public static void ToAxis(double xM, double zM, out double along, out double across)
        {
            double a = AxisDeg * Math.PI / 180, dx = xM - CentreX, dz = zM - CentreZ;
            along = dx * Math.Sin(a) + dz * Math.Cos(a);
            across = dx * Math.Cos(a) - dz * Math.Sin(a);
        }

        /// <summary>Where Density's cloud begins, in a puff's radii.</summary>
        public const float EdgeR = 1.1f;

        /// <summary>How deep in cloud the point is: 0 in clear air, 1 well inside a puff (it rises over the outer part of each).</summary>
        public float Density(float x, float y, float z)
        {
            int i = (int)Math.Floor((x - minX) / CellM), j = (int)Math.Floor((z - minZ) / CellM);
            if (i < 0 || j < 0 || i >= cells || j >= cells) return 0f;
            int c = j * cells + i;
            float best = 0f;
            for (int k = cellStart[c]; k < cellStart[c + 1]; k++)
            {
                var p = puffs[cellPuffs[k]];
                if (p.kind == Rain) continue;
                // (drawn, a puff's edge wanders between 0.84 and 1 of its radius, and it thins away as you come within 1.5
                // radii of its middle: count the cloud from a little outside it, so the white comes up as the puff goes)
                float rh = p.r * p.stretch * EdgeR, rv = p.r * EdgeR;
                float dx = (x - p.x) / rh, dy = (y - p.y) / rv, dz = (z - p.z) / rh;
                float q2 = dx * dx + dy * dy + dz * dz;
                if (q2 >= 1f) continue;
                float d = 1f - Smooth(0.45f, 1f, (float)Math.Sqrt(q2));
                // (cut flat underneath: under the base there is no cloud)
                d *= Smooth(p.cutY - 15f, p.cutY + 40f, y);
                if (d > best) best = d;
            }
            return best;
        }

        static float Smooth(float a, float b, float x)
        {
            float t = Math.Max(0f, Math.Min(1f, (x - a) / (b - a)));
            return t * t * (3f - 2f * t);
        }

        Skyscape(List<Puff> p, List<Bank> b, int towerCount, int stormCount, int heapCount)
        {
            puffs = p.ToArray(); banks = b.ToArray(); towers = towerCount; storms = stormCount; heaps = heapCount;
            float reach = RadiusM + 4000f;
            minX = CentreX - reach; minZ = CentreZ - reach;
            cells = (int)Math.Ceiling(2 * reach / CellM);
            var lists = new List<int>[cells * cells];
            for (int k = 0; k < puffs.Length; k++)
            {
                var q = puffs[k];
                if (q.kind == Rain) continue;
                float rh = q.r * q.stretch * EdgeR;
                int i0 = Clamp((int)Math.Floor((q.x - rh - minX) / CellM)), i1 = Clamp((int)Math.Floor((q.x + rh - minX) / CellM));
                int j0 = Clamp((int)Math.Floor((q.z - rh - minZ) / CellM)), j1 = Clamp((int)Math.Floor((q.z + rh - minZ) / CellM));
                for (int j = j0; j <= j1; j++)
                    for (int i = i0; i <= i1; i++)
                        (lists[j * cells + i] ?? (lists[j * cells + i] = new List<int>())).Add(k);
            }
            cellStart = new int[cells * cells + 1];
            var all = new List<int>();
            for (int c = 0; c < lists.Length; c++)
            {
                cellStart[c] = all.Count;
                if (lists[c] != null) all.AddRange(lists[c]);
            }
            cellStart[lists.Length] = all.Count;
            cellPuffs = all.ToArray();
        }

        int Clamp(int i) => i < 0 ? 0 : i >= cells ? cells - 1 : i;

        // ---------- the layout ----------

        // stable hashes, so the clouds are the same every run and on every machine
        static uint H(uint x) { x ^= x >> 16; x *= 0x7feb352dU; x ^= x >> 15; x *= 0x846ca68bU; x ^= x >> 16; return x; }
        static float R01(int a, int b) => (H((uint)a * 73856093U ^ H((uint)b * 19349663U + 0x9e3779b9U)) & 0xffffff) / 16777216f;
        static float Rng(int a, int b, float lo, float hi) => lo + (hi - lo) * R01(a, b);

        /// <summary>Smooth value noise, 0 to 1, over cells of size m.</summary>
        static float Noise(int seed, float x, float z, float m)
        {
            float fx = x / m, fz = z / m;
            int i = (int)Math.Floor(fx), j = (int)Math.Floor(fz);
            float u = fx - i, v = fz - j;
            u = u * u * (3 - 2 * u); v = v * v * (3 - 2 * v);
            float N(int a, int b) => R01(seed + a * 7919, b * 104729 + 31);
            float n0 = N(i, j) + (N(i + 1, j) - N(i, j)) * u, n1 = N(i, j + 1) + (N(i + 1, j + 1) - N(i, j + 1)) * u;
            return n0 + (n1 - n0) * v;
        }

        /// <summary>How far a point (metres) lies outside the start corridor: negative inside it.</summary>
        public static float OutsideCorridor(float x, float z)
        {
            ToAxis(x, z, out double along, out double across);
            double a = Math.Abs(along) - CorridorHalfLengthM, c = Math.Abs(across) - CorridorHalfWidthM;
            if (a <= 0 && c <= 0) return (float)Math.Max(a, c);
            return (float)Math.Sqrt(Math.Max(a, 0) * Math.Max(a, 0) + Math.Max(c, 0) * Math.Max(c, 0));
        }

        static Skyscape Make(int seed)
        {
            var puffs = new List<Puff>(9000);
            var banks = new List<Bank>(512);
            // the wind the island's clouds ride (ToonSky.Wind): the towers lean with it and the cirrus streaks along it
            float wx = 0.8f, wz = 0.6f;

            int bankNo = 0;
            float hx = 0, hy = 0, hz = 0, bBase = 0, bTop = 0; byte bKind = 0; int bFirst = 0;
            void Begin(float x, float y, float z, float baseY, float topY, byte kind)
            {
                hx = x; hy = y; hz = z; bBase = baseY; bTop = topY; bKind = kind; bFirst = puffs.Count;
            }
            void End()
            {
                if (puffs.Count == bFirst) return;
                banks.Add(new Bank { x = hx, y = hy, z = hz, baseY = bBase, topY = bTop, kind = bKind, first = bFirst, count = puffs.Count - bFirst });
                bankNo++;
            }
            void Add(float x, float y, float z, float r, byte kind, float stretch, float cutY) =>
                puffs.Add(new Puff { x = x, y = y, z = z, r = r, kind = kind, stretch = stretch, cutY = cutY, bank = bankNo });

            // ---- the deck: a rolling floor of cloud, in tiles (each a bank), with holes down to the sea
            const float Tile = 3000f, Spacing = 430f;
            int tiles = (int)Math.Ceiling(RadiusM / Tile);
            for (int tj = -tiles; tj < tiles; tj++)
                for (int ti = -tiles; ti < tiles; ti++)
                {
                    float x0 = CentreX + ti * Tile, z0 = CentreZ + tj * Tile;
                    Begin(x0 + Tile / 2, DeckBaseM + 250f, z0 + Tile / 2, DeckBaseM, DeckTopM, Cumulus);
                    int n = (int)(Tile / Spacing);
                    for (int b = 0; b < n; b++)
                        for (int a = 0; a < n; a++)
                        {
                            int s = seed * 17 + (ti + 64) * 9973 + (tj + 64) * 5279 + b * 211 + a * 13;
                            float x = x0 + (a + 0.5f) * Spacing + Rng(s, 1, -0.38f, 0.38f) * Spacing;
                            float z = z0 + (b + 0.5f) * Spacing + Rng(s, 2, -0.38f, 0.38f) * Spacing;
                            float dx = x - CentreX, dz = z - CentreZ, d = (float)Math.Sqrt(dx * dx + dz * dz);
                            if (d > RadiusM) continue;
                            // holes: where the noise is low, and more of them toward the edge, so the deck frays out
                            float hole = Noise(seed + 5, x, z, 2600f) * 0.75f + Noise(seed + 6, x, z, 900f) * 0.25f;
                            if (hole < 0.34f + 0.2f * Smooth(RadiusM * 0.75f, RadiusM, d)) continue;
                            float swell = Noise(seed + 7, x, z, 1800f);   // the deck rolls: higher puffs where it swells
                            float r = Rng(s, 3, 250f, 360f) * (0.8f + 0.35f * swell);
                            float y = DeckBaseM + r * 0.45f + swell * 160f + Rng(s, 4, 0f, 60f);
                            Add(x, y, z, r, Cumulus, Rng(s, 5, 1.15f, 1.5f), DeckBaseM);
                        }
                    End();
                }

            // ---- the towers: cumulus standing up out of the deck all round, clear of the start corridor
            var tw = new List<(float x, float z, float w, float top, bool storm)>();
            for (int tries = 0; tries < 2500 && tw.Count < 58; tries++)
            {
                int s = seed * 29 + tries * 7;
                float a = Rng(s, 1, 0f, (float)(2 * Math.PI)), d = RadiusM * 0.92f * (float)Math.Sqrt(R01(s, 2));
                float x = CentreX + (float)Math.Cos(a) * d, z = CentreZ + (float)Math.Sin(a) * d;
                float w = Rng(s, 3, 450f, 1000f);
                bool storm = tw.Count % 12 == 5 && d > 4500f;   // a few thunderheads, out from the middle
                if (storm) w *= 1.3f;
                if (OutsideCorridor(x, z) < w * 1.6f + 250f) continue;
                bool crowded = false;
                foreach (var t in tw)
                {
                    float ex = t.x - x, ez = t.z - z;
                    if (ex * ex + ez * ez < (t.w + w + 450f) * (t.w + w + 450f)) { crowded = true; break; }
                }
                if (crowded) continue;
                float top = storm ? Rng(s, 4, 6200f, 7200f) : Rng(s, 4, 2500f, 5200f);
                tw.Add((x, z, w, top, storm));
            }
            int towerCount = 0, stormCount = 0;
            foreach (var t in tw)
            {
                int s = seed * 41 + towerCount * 997;
                byte kind = t.storm ? Storm : Cumulus;
                float baseY = DeckTopM - 200f, H = t.top - baseY;
                Begin(t.x, baseY + H * 0.4f, t.z, baseY, t.top, kind);
                int levels = Math.Max(3, (int)Math.Ceiling(H / (t.w * 0.75f)));
                for (int j = 0; j < levels; j++)
                {
                    float f = j / (levels - 1f), ring = t.w * (1.05f - 0.3f * f);
                    int per = j == 0 ? 9 : 7;
                    for (int k = 0; k < per; k++)
                    {
                        int q = s + j * 97 + k * 13;
                        float a = (k + Rng(q, 3, -0.35f, 0.35f)) / per * (float)(2 * Math.PI), rr = ring * Rng(q, 4, 0.25f, 0.8f);
                        float r = t.w * Rng(q, 5, 0.5f, 0.72f) * (j == 0 ? 0.9f : 1f);
                        float lean = f * f * t.w * 0.5f;   // (leaning a little downwind as it climbs)
                        Add(t.x + (float)Math.Cos(a) * rr + wx * lean, baseY + r * 0.45f + f * (H - r * 1.1f), t.z + (float)Math.Sin(a) * rr + wz * lean, r, kind, 1f, baseY);
                    }
                }
                // a cauliflower head: a few big puffs heaped on top
                for (int k = 0; k < 6; k++)
                {
                    int q = s + 5000 + k * 17;
                    float a = Rng(q, 3, 0f, (float)(2 * Math.PI)), rr = t.w * 0.45f * (float)Math.Sqrt(R01(q, 4));
                    float r = t.w * Rng(q, 5, 0.42f, 0.6f);
                    Add(t.x + (float)Math.Cos(a) * rr + wx * t.w * 0.5f, t.top - r * Rng(q, 6, 0.5f, 1.0f), t.z + (float)Math.Sin(a) * rr + wz * t.w * 0.5f, r, kind, 1f, baseY);
                }
                // wisps inside: small puffs through the body, so flying through one has cloud streaming past in the white
                for (int k = 0; k < 14; k++)
                {
                    int q = s + 7000 + k * 19;
                    // (kept well inside the body, so none pokes out of its side as a little ball)
                    float a = Rng(q, 3, 0f, (float)(2 * Math.PI)), rr = t.w * 0.4f * (float)Math.Sqrt(R01(q, 4));
                    Add(t.x + (float)Math.Cos(a) * rr, baseY + Rng(q, 6, 0.15f, 0.8f) * H, t.z + (float)Math.Sin(a) * rr, t.w * Rng(q, 5, 0.14f, 0.24f), kind, 1f, baseY);
                }
                if (t.storm)
                {
                    // the anvil: a wide flat shelf of stretched puffs at the top, cut flat underneath, swept downwind
                    float T = t.w;
                    for (int k = 0; k < 26; k++)
                    {
                        int q = s + 9000 + k * 19;
                        float a = Rng(q, 3, 0f, (float)(2 * Math.PI)), rr = (float)Math.Sqrt(R01(q, 4)) * T * 2.4f;
                        float r = T * Rng(q, 5, 0.38f, 0.55f);
                        Add(t.x + (float)Math.Cos(a) * rr + wx * T * 1.3f, t.top - T * Rng(q, 6, 0.1f, 0.35f), t.z + (float)Math.Sin(a) * rr + wz * T * 1.3f, r, Storm, Rng(q, 7, 1.5f, 2.1f), t.top - T * 0.6f);
                    }
                    // rain under it, down through the deck to the sea
                    for (int k = 0; k < 3; k++)
                    {
                        int q = s + 11000 + k * 23;
                        float a = (k + Rng(q, 3, -0.3f, 0.3f)) / 3f * (float)(2 * Math.PI), rr = Rng(q, 4, 0.2f, 0.6f) * T;
                        float h = DeckBaseM + 20f, w = T * Rng(q, 5, 0.5f, 0.8f);
                        Add(t.x + (float)Math.Cos(a) * rr, h * 0.5f, t.z + (float)Math.Sin(a) * rr, h * 0.5f, Rain, w / (h * 0.5f), float.MinValue);
                    }
                    stormCount++;
                }
                End();
                towerCount++;
            }

            // ---- loose heaps of cumulus at the fight's heights, between the towers (never in the start corridor)
            int heapCount = 0;
            for (int tries = 0; tries < 5000 && heapCount < 240; tries++)
            {
                int s = seed * 53 + tries * 11;
                float a = Rng(s, 1, 0f, (float)(2 * Math.PI)), d = (RadiusM - 400f) * (float)Math.Sqrt(R01(s, 2));
                float x = CentreX + (float)Math.Cos(a) * d, z = CentreZ + (float)Math.Sin(a) * d;
                float S = Rng(s, 3, 200f, 620f), baseY = Rng(s, 4, 1450f, 3700f);
                if (OutsideCorridor(x, z) < S * 1.6f + 200f) continue;
                bool inTower = false;
                foreach (var t in tw)
                {
                    float ex = t.x - x, ez = t.z - z;
                    if (baseY < t.top && ex * ex + ez * ez < (t.w * 1.2f + S * 1.4f) * (t.w * 1.2f + S * 1.4f)) { inTower = true; break; }
                }
                if (inTower) continue;
                Begin(x, baseY + S * 0.5f, z, baseY, baseY + S * 1.5f, Cumulus);
                int rim = 3 + (int)(R01(s, 5) * 2.99f);
                for (int k = 0; k < rim; k++)
                {
                    int q = s + k * 13;
                    float aa = (k + Rng(q, 3, -0.3f, 0.3f)) / rim * (float)(2 * Math.PI), rr = S * Rng(q, 4, 0.6f, 0.85f);
                    float r = S * Rng(q, 5, 0.45f, 0.65f);
                    Add(x + (float)Math.Cos(aa) * rr, baseY + r * 0.4f, z + (float)Math.Sin(aa) * rr, r, Cumulus, 1f, baseY);
                }
                int heap = 4 + (int)(R01(s, 6) * 5.99f);
                for (int k = 0; k < heap; k++)
                {
                    int q = s + 5000 + k * 17;
                    float aa = Rng(q, 3, 0f, (float)(2 * Math.PI)), rr = (float)Math.Sqrt(R01(q, 4)) * 0.6f, mid = 1f - rr * rr / 0.36f * 0.8f;
                    float r = S * (0.5f + 0.5f * mid) * Rng(q, 5, 0.85f, 1.15f);
                    Add(x + (float)Math.Cos(aa) * rr * S, baseY + r * 0.35f + mid * S * Rng(q, 6, 0.3f, 0.9f), z + (float)Math.Sin(aa) * rr * S, r, Cumulus, 1f, baseY);
                }
                End();
                heapCount++;
            }

            // ---- cirrus: thin streaks high above, along the wind
            for (int st = 0; st < 26; st++)
            {
                int s = seed * 67 + st * 101;
                float a = Rng(s, 1, 0f, (float)(2 * Math.PI)), d = RadiusM * (float)Math.Sqrt(R01(s, 2));
                float x = CentreX + (float)Math.Cos(a) * d, z = CentreZ + (float)Math.Sin(a) * d, y = Rng(s, 3, 6800f, 7800f);
                float len = Rng(s, 4, 1800f, 4200f);
                int m = 7 + (int)(R01(s, 5) * 3.99f);
                Begin(x, y, z, y - 400f, y + 200f, Cirrus);
                for (int k = 0; k < m; k++)
                {
                    int q = s + k * 7;
                    float u = k / (m - 1f) * 2f - 1f;
                    float r = Rng(q, 5, 140f, 240f) * (1.1f - 0.6f * Math.Abs(u));
                    Add(x + wx * u * len * 0.5f, y + Rng(q, 6, -40f, 40f), z + wz * u * len * 0.5f, r, Cirrus, Rng(q, 7, 2.6f, 3.6f), y - r * 10f);
                }
                End();
            }

            return new Skyscape(puffs, banks, towerCount, stormCount, heapCount);
        }
    }
}
