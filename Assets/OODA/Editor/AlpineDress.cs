using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Ooda.EditorTools
{
    /// <summary>
    /// What the ground is dressed in, decided from its shape alone (no Unity objects, so Tools~/WorldPreview draws the same
    /// thing): the six ground colours, where the trees stand, where the farmhouses and the village's chalets go, and the
    /// little stone bridges over the river. AlpineWorld lays them out in the scene.
    /// </summary>
    internal static partial class AlpineWorld
    {
        // ---------- the ground colours ----------
        // 0 meadow, 1 forest, 2 rock, 3 snow, 4 ripe wheat (rows along the field), 5 fresh pasture, 6 ripe wheat (rows across),
        // 7 young green grain. Toon: narrow steps between flat colours. 8 layers draw in the same two terrain passes as 5 to 8.

        public const int CoverLayers = 8;

        /// <summary>How much of each ground colour a point has (they add up to 1).</summary>
        static void GroundCover(float[,] h, float x, float z, float[] w)
        {
            float y = SampleArr(h, x, z);
            float slope = Slope(h, x, z);
            float s = SouthOf(x, z), n = NorthOf(x, z);
            float jitter = 120f * (Mathf.PerlinNoise(x * 0.003f + 1, z * 0.003f + 4) - 0.5f);
            float snowLine = Mathf.Lerp(2250f, 1950f, n) + jitter;
            float snowW = Step(snowLine - 100, snowLine, y) * (1 - Step(72, 80, slope));
            float rockW = Mathf.Max(Step(RockSlope, RockSlope + 6, slope), Step(snowLine - 200, snowLine - 50, y)) * (1 - snowW);
            float forestW = ForestShare(x, z, y, slope, s, jitter) * (1 - rockW) * (1 - snowW);
            // the farmland's patchwork: fields of wheat and pasture on the gentle low ground of the south
            float farmW = Step(0.35f, 0.7f, s) * (1 - Step(8, 13, slope)) * (1 - Step(420, 500, y)) * (1 - forestW) * (1 - rockW) * (1 - snowW);
            if (RiverNear(x, z)) farmW = 0;
            int patch = FieldPatch(x, z, out _);
            float meadowW = Mathf.Max(0, 1 - snowW - rockW - forestW - farmW);
            for (int k = 0; k < CoverLayers; k++) w[k] = 0;
            w[0] = meadowW + (patch == 0 ? farmW : 0);
            w[1] = forestW; w[2] = rockW; w[3] = snowW;
            w[4] = patch == 1 ? farmW : 0;
            w[5] = patch == 2 ? farmW : 0;
            w[6] = patch == 3 ? farmW : 0;
            w[7] = patch == 4 ? farmW : 0;
        }

        /// <summary>The slope where bare rock starts (it is all rock 6 degrees steeper); the forest climbs to just under it.</summary>
        const float RockSlope = 42f;

        /// <summary>
        /// How much of a point is pine forest, before rock and snow take their share: the high-mountain look, the valley sides
        /// filled with forest from just above the floor up to the tree line (1650 to 1750 m), up slopes to 40-46 degrees, with
        /// only a few meadow clearings in the middle and the north; in the south the woods are clumps among the fields.
        /// </summary>
        static float ForestShare(float x, float z, float y, float slope, float s, float jitter, bool hill = false)
        {
            float clumpLo = Mathf.Lerp(0.28f, 0.60f, s);
            float forestLo = hill ? 30f : Mathf.Lerp(200f, 60f, s);   // a grass hill is wooded from its foot
            return Step(forestLo, forestLo + 60, y) * (1 - Step(1650 + jitter, 1750 + jitter, y)) * (1 - Step(RockSlope - 2, RockSlope + 4, slope))
                   * Step(clumpLo, clumpLo + 0.08f, Mathf.PerlinNoise(x * 0.0012f + 7, z * 0.0012f + 3));
        }

        /// <summary>Which field a point of the farmland is in: 0 meadow, 1 ripe wheat, 2 pasture, 3 ripe wheat with its rows
        /// across, 4 young green grain; edge is how far it is from the
        /// field's nearest side, m. The fields are a grid of about 300 by 200 m turned 17 degrees, each its own colour.</summary>
        static int FieldPatch(float x, float z, out float edge)
        {
            const float A = 17f * Mathf.Deg2Rad, W = 300f, D = 200f;
            float u = x * Mathf.Cos(A) - z * Mathf.Sin(A), v = x * Mathf.Sin(A) + z * Mathf.Cos(A);
            // the rows wobble a little, so it isn't a perfect grid
            v += 40f * Mathf.Sin(u / 900f);
            int cu = Mathf.FloorToInt(u / W), cv = Mathf.FloorToInt(v / D);
            float fu = u - cu * W, fv = v - cv * D;
            edge = Mathf.Min(Mathf.Min(fu, W - fu), Mathf.Min(fv, D - fv));
            uint k = (uint)(cu * 73856093) ^ (uint)(cv * 19349663);
            k ^= k >> 13; k *= 0x5bd1e995; k ^= k >> 15;
            float r = (k % 1000) / 1000f;
            return r < 0.2f ? 0 : r < 0.4f ? 1 : r < 0.55f ? 3 : r < 0.75f ? 4 : 2;
        }

        /// <summary>Whether the river's water shows at point k: not inside a lake (its water is there already), not at the sea, and
        /// only where both banks stand over it, so it never floats over the ground.</summary>
        static bool RiverWet(float[,] h, List<RiverPoint> r, int k)
        {
            var rp = r[k];
            if (InLakeWater(rp.p.x, rp.p.y) || rp.surface < 0.5f) return false;
            if (InLake(rp.p.x, rp.p.y)) return true;   // by a lake's shore the banks fall away into it: the water runs on to meet the lake's
            Vector2 dir = (r[Mathf.Min(k + 1, r.Count - 1)].p - r[Mathf.Max(k - 1, 0)].p).normalized, across = V(dir.y, -dir.x);
            float off = rp.half + 14f;
            return SampleArr(h, rp.p.x + across.x * off, rp.p.y + across.y * off) > rp.surface + 0.3f
                && SampleArr(h, rp.p.x - across.x * off, rp.p.y - across.y * off) > rp.surface + 0.3f;
        }

        /// <summary>Under a lake's own water (its mesh reaches 1.12 of its shore): the river's ribbon stops here, overlapping it a little.</summary>
        static bool InLakeWater(float x, float z) => Lakes.Any(l => LakeR(l, x, z) < 1.1f);

        static bool[,] riverNear;
        static bool RiverNear(float x, float z) => riverNear != null && riverNear[Idx(z), Idx(x)];

        // ---------- trees ----------

        struct TreeSpot { public float x, y, z, scale, tall, yaw; public int kind; }   // kind 0 round, 1 tall, 2 pine; y its foot

        /// <summary>The forest's trees stand on a jittered grid this far apart (m): with the pines 1.3 to 2.1 times the model
        /// (21 to 34 m tall, 8 to 13 m across, like real spruce and fir), their crowns nearly touch.</summary>
        public const float ForestGap = 15f;

        /// <summary>
        /// The trees. The forest is dense: a tree on every cell of a jittered grid ForestGap apart wherever there is forest,
        /// thinning out at its edges. On open ground the forest is the ground's forest colour (GroundCover). On the models,
        /// the grass hills and the mountains, the trees stand on the model's own surface, up its lower slopes to the tree line
        /// wherever it is no steeper than the forest climbs. In the mountains it is all pines; the woods of the farmland in the
        /// south are leafy. Then the scattered trees: in the meadows, up past the forest, and on the farmland hedgerows along the
        /// fields' sides.
        /// </summary>
        static List<TreeSpot> TreeSpots(float[,] h, List<Vector3> rail)
        {
            var rnd = new System.Random(23);
            var trees = new List<TreeSpot>();
            var w = new float[CoverLayers];
            // what shows: the ground, or a model's surface over it
            var top = (float[,])h.Clone();
            if (modelTop != null)
                for (int j = 0; j < Res; j++) for (int i = 0; i < Res; i++) top[j, i] = Mathf.Max(h[j, i], modelTop[j, i]);
            float half = Size * 0.46f;
            int cells = Mathf.FloorToInt(2 * half / ForestGap);
            for (int cj = 0; cj < cells; cj++)
                for (int ci = 0; ci < cells; ci++)
                {
                    float x = -half + (ci + 0.1f + 0.8f * (float)rnd.NextDouble()) * ForestGap;
                    float z = -half + (cj + 0.1f + 0.8f * (float)rnd.NextDouble()) * ForestGap;
                    double keep = rnd.NextDouble(), pick = rnd.NextDouble(), size = rnd.NextDouble(), yaw = rnd.NextDouble();
                    float ground = SampleArr(h, x, z), y = SampleArr(top, x, z);
                    if (y < 25 || y > 1800) continue;
                    float s = SouthOf(x, z), n = NorthOf(x, z);
                    float share;
                    bool onModel = y > ground + 2f;
                    if (onModel)
                    {
                        // on a model: its own slope and height decide; the snow and bare rock on it are its own texture
                        float jitter = 120f * (Mathf.PerlinNoise(x * 0.003f + 1, z * 0.003f + 4) - 0.5f);
                        // not on a mountain's upper part (its snow and bare rock), and less steep than on open ground: the
                        // model has cliffs finer than the 20 m it is sampled at
                        float up = modelUp[Idx(z), Idx(x)];
                        share = ForestShare(x, z, y, Slope(top, x, z) + 6f, s, jitter, true) * (1 - Step(0.25f, 0.35f, up));
                        y -= 1.5f;   // sunk a little: the surface between the samples may dip
                    }
                    else { GroundCover(h, x, z, w); share = w[1]; }
                    // the edges thin out, and a fine noise opens small gaps inside, so it isn't a carpet
                    float dense = share * Mathf.Lerp(0.7f, 1.15f, Mathf.PerlinNoise(x * 0.011f + 2, z * 0.011f + 9));
                    if (keep >= dense) continue;
                    if (InTown(x, z) || InLake(x, z) || RiverNear(x, z) || InVillage(x, z) || NearRail(rail, x, z, 25f)) continue;
                    float broad = Step(0.35f, 0.7f, s) * 0.7f;   // leafy woods only on the farmland
                    int kind = pick >= broad ? 2 : pick < broad * 0.75f ? 0 : 1;
                    float sc = kind == 2 ? 1.3f + (float)size * 0.8f : 0.9f + (float)size * 0.6f;
                    trees.Add(new TreeSpot { x = x, y = y, z = z, scale = sc * Rand(rnd, 0.95f, 1.15f), tall = sc, yaw = (float)yaw * Mathf.PI * 2, kind = kind });
                }
            int forest = trees.Count;

            for (int tries = 0, more = 0; tries < 260000 && more < 9000; tries++)
            {
                float x = ((float)rnd.NextDouble() - 0.5f) * Size * 0.92f, z = ((float)rnd.NextDouble() - 0.5f) * Size * 0.92f;
                float y = SampleArr(h, x, z);
                float s = SouthOf(x, z), n = NorthOf(x, z);
                if (y < 25 || y > Mathf.Lerp(2100f, 1900f, n) || Slope(h, x, z) > RockSlope) continue;
                float clump = Mathf.PerlinNoise(x * 0.0012f + 7, z * 0.0012f + 3);
                bool farm = s > 0.6f && y < 450f;
                if (farm)
                {
                    FieldPatch(x, z, out float edge);
                    bool hedge = edge < 9f && rnd.NextDouble() < 0.55;
                    bool wood = clump > 0.64f;
                    if (!hedge && !wood) continue;
                }
                else
                {
                    if (y < Mathf.Lerp(220f, 60f, s)) continue;
                    GroundCover(h, x, z, w);
                    if (w[1] > 0.3f) continue;   // the forest has its own
                    if (w[0] < 0.5f && y < 1600f) continue;   // the meadows and clearings, and the high ground past the tree line
                    if (rnd.NextDouble() < 0.5) continue;
                }
                if (NearRail(rail, x, z, 25f) || InTown(x, z) || UnderMountain(x, z) || InLake(x, z) || RiverNear(x, z) || InVillage(x, z)) continue;
                float sc = 0.75f + (float)rnd.NextDouble() * 0.7f;
                // the hedgerows leafy; in the mountains' meadows pines, a few round ones low down
                int kind = farm ? (rnd.NextDouble() < 0.8 ? 0 : 1) : rnd.NextDouble() < 0.3f * (1 - Mathf.InverseLerp(300f, 600f, y)) ? 0 : 2;
                if (kind == 2) sc *= 1.3f;
                trees.Add(new TreeSpot { x = x, y = y, z = z, scale = sc, tall = sc * (0.9f + (float)rnd.NextDouble() * 0.3f), yaw = (float)rnd.NextDouble() * Mathf.PI * 2, kind = kind });
                more++;
            }
            ForestTrees = forest;
            return trees;
        }

        /// <summary>How many of the last TreeSpots stand in the forest (the rest are scattered).</summary>
        static int ForestTrees;

        // ---------- the village, the farmhouses ----------

        struct HouseSpot { public Vector3 pos; public float yaw, size; }

        /// <summary>The village: on the flat at the north end of the chalet lake, between its shore and the railway's viaduct, either side of the river.</summary>
        static readonly Vector2 VillageCentre = V(0, -480);
        static readonly Vector2 VillageRadii = V(330, 300);
        static bool InVillage(float x, float z) { float ex = (x - VillageCentre.x) / VillageRadii.x, ez = (z - VillageCentre.y) / VillageRadii.y; return ex * ex + ez * ez < 1.2f; }
        /// <summary>The church: on the village's east side, its tower to the north.</summary>
        static readonly Vector2 ChurchAt = V(150, -430);

        /// <summary>The square, with its fountain: on the west bank, clear of the river.</summary>
        static readonly Vector2 SquareAt = V(-100, -470);

        /// <summary>The village's chalets: 26 copies close together, most round the square on the west bank and the rest round the
        /// church on the east, each facing its square or church, turned to the street a little differently.</summary>
        static List<HouseSpot> VillageSpots(float[,] h, List<Vector3> rail)
        {
            var rnd = new System.Random(5);
            var placed = new List<HouseSpot>();
            for (int tries = 0; tries < 6000 && placed.Count < 26; tries++)
            {
                bool bySquare = placed.Count % 3 != 2;   // two in three round the square
                Vector2 c = bySquare ? SquareAt : ChurchAt;
                float a = Rand(rnd, 0, Mathf.PI * 2), r = Mathf.Lerp(0.3f, 1f, (float)rnd.NextDouble());
                var p = c + V(Mathf.Cos(a), Mathf.Sin(a)) * (bySquare ? 190f : 150f) * r;
                if (Vector2.Distance(p, SquareAt) < 52f || Vector2.Distance(p, ChurchAt) < 42f) continue;
                if (placed.Any(q => Vector2.Distance(p, V(q.pos.x, q.pos.z)) < 24f)) continue;
                if (RiverNear(p.x, p.y) || InLake(p.x, p.y) || NearRail(rail, p.x, p.y, 40f) || Slope(h, p.x, p.y) > 10) continue;
                float yaw = Mathf.Atan2(c.x - p.x, c.y - p.y) * Mathf.Rad2Deg + Rand(rnd, -20f, 20f);
                placed.Add(new HouseSpot { pos = new Vector3(p.x, SampleArr(h, p.x, p.y), p.y), yaw = yaw, size = Rand(rnd, 0.85f, 1.15f) });
            }
            return placed;
        }

        /// <summary>Farmhouses: single chalets dotted over the green slopes of the middle and the farmland, each with room round it.</summary>
        static List<HouseSpot> FarmSpots(float[,] h, List<Vector3> rail)
        {
            var rnd = new System.Random(77);
            var placed = new List<HouseSpot>();
            for (int tries = 0; tries < 40000 && placed.Count < 70; tries++)
            {
                float x = Rand(rnd, -7800f, 7800f), z = Rand(rnd, -7800f, 3200f);
                float y = SampleArr(h, x, z);
                if (y < 30f || y > 1300f || Slope(h, x, z) > 12f) continue;
                if (NorthOf(x, z) > 0.3f) continue;
                if (InTown(x, z) || InVillage(x, z) || InLake(x, z) || RiverNear(x, z) || UnderMountain(x, z) || NearRail(rail, x, z, 60f)) continue;
                if (MainDistance(x, z, out _) < 330f) continue;        // off the bombers' pass
                if (placed.Any(q => Vector2.Distance(V(x, z), V(q.pos.x, q.pos.z)) < 420f)) continue;
                placed.Add(new HouseSpot { pos = new Vector3(x, y, z), yaw = Rand(rnd, 0f, 360f), size = Rand(rnd, 0.9f, 1.2f) });
            }
            return placed;
        }

        // ---------- the river's stone bridges ----------

        struct BridgeSpot { public Vector3 at; public float yaw, span; }

        /// <summary>Little stone arch bridges over the river: in the village, on the farmland and up the pass (where the river is clear of the lake and the sea).</summary>
        static List<BridgeSpot> RiverBridges(float[,] h)
        {
            var r = River();
            var spots = new List<BridgeSpot>();
            foreach (float wantZ in new[] { -470f, 2700f, -4700f, -7100f })
            {
                int best = -1;
                for (int k = 2; k + 2 < r.Count; k++)
                    if (best < 0 || Mathf.Abs(r[k].p.y - wantZ) < Mathf.Abs(r[best].p.y - wantZ)) best = k;
                var rp = r[best];
                Vector2 dir = (r[best + 2].p - r[best - 2].p).normalized;
                if (InLake(rp.p.x, rp.p.y)) continue;
                // the deck runs across the river: forward (Unity's +z, turned by yaw) is (dir.y, -dir.x)
                float yaw = Mathf.Atan2(dir.y, -dir.x) * Mathf.Rad2Deg;
                spots.Add(new BridgeSpot { at = new Vector3(rp.p.x, rp.surface, rp.p.y), yaw = yaw, span = 2 * rp.half + 18f });
            }
            return spots;
        }
    }
}
