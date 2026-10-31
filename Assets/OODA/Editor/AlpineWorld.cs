using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ooda.EditorTools
{
    /// <summary>
    /// The toon Alps the test world and the turn battle fly over: ridged peaks of 3 to 4 km, broad U-shaped valleys,
    /// slot gorges to fly through, two lakes, meadow/forest/rock/snow by height and slope, pine forests, a chalet
    /// village, and a railway whose bridges, tunnels and cuttings follow from the ground under it: stone arch viaducts
    /// over the long crossings, red steel trusses over the gorges. The island still drops to the sea at the edges.
    /// Units are metres, Unity x east and z north; heights are above the sea.
    /// </summary>
    internal static class AlpineWorld
    {
        public const float Size = 20000f, Base = -80f, Height = 4500f;
        const int Res = 1025;                      // heightmap: about 20 m a sample
        const int SplatRes = 1024;
        const float Cell = Size / (Res - 1);
        const string Dir = "Assets/OODA/Generated/Alpine";

        // The head-on fight starts in the main valley, which runs straight north for 15 km: x = 0.
        public static readonly Vector3 FightCentre = new Vector3(0, 0, -4500);
        public const float FightAltFt = 3600f;     // about 1100 m: 650 m over the valley floor, well under the peaks
        public static readonly Vector3 FreeFlightSpawn = new Vector3(0, 1100f, -9000f);

        // ---------- the land's shape ----------

        class Valley
        {
            public Vector2[] pts; public float[] floor; public float halfFloor, wall; public bool gorge;
        }

        static readonly Valley[] Valleys =
        {
            // The main valley, where the fight starts: straight north, then bending north-east.
            new Valley { pts = new[] { V(0, -10600), V(0, -6500), V(0, 5000), V(2500, 8000), V(4000, 10600) },
                         floor = new[] { 360f, 380f, 700f, 900f, 950f }, halfFloor = 650, wall = 1900 },
            // The rail valley, winding east-west across the north.
            new Valley { pts = Enumerable.Range(0, 15).Select(i => { float x = -10600 + i * 1514f; return V(x, 2600 + 1300 * Mathf.Sin(x / 2600f)); }).ToArray(),
                         floor = Enumerable.Range(0, 15).Select(i => 950f + 60 * Mathf.Sin(i * 0.7f)).ToArray(), halfFloor = 300, wall = 1300 },
            // A side valley from the south-west.
            new Valley { pts = new[] { V(-10600, -7000), V(-4000, -3500), V(-2500, 1500) }, floor = new[] { 400f, 700f, 900f }, halfFloor = 350, wall = 1400 },
            // Gorge one: a winding slot canyon east-west, cut right across the main valley's floor.
            new Valley { pts = Enumerable.Range(0, 43).Select(i => { float x = -10600 + i * 505f; return V(x, -2600 + 500 * Mathf.Sin(x / 1700f)); }).ToArray(),
                         floor = Enumerable.Repeat(280f, 43).ToArray(), halfFloor = 90, wall = 450, gorge = true },
            // Gorge two: north-south in the east.
            new Valley { pts = Enumerable.Range(0, 43).Select(i => { float z = -10600 + i * 505f; return V(5200 + 400 * Mathf.Sin(z / 1500f), z); }).ToArray(),
                         floor = Enumerable.Repeat(350f, 43).ToArray(), halfFloor = 80, wall = 420, gorge = true },
        };

        struct Lake { public Vector2 c, r; public float level; }
        static readonly Lake[] Lakes =
        {
            new Lake { c = V(0, -8400), r = V(800, 1300), level = 372 },
            new Lake { c = V(-6400, 3150), r = V(650, 380), level = 955 },
        };

        // The railway's line and its rail height (x, rail top, z): it keeps a gentle grade and lets the ground decide
        // where it needs a bridge (more than 10 m of air under it) or a tunnel (more than 30 m of rock over it).
        static readonly Vector3[] Rail =
        {
            new Vector3(-8000, 580, -4600), new Vector3(-7500, 600, -4300), new Vector3(-4800, 700, -3200),
            new Vector3(-2200, 760, -1800), new Vector3(0, 790, -1300), new Vector3(2400, 830, -700),
            new Vector3(4300, 870, 300), new Vector3(5200, 880, 900), new Vector3(6500, 900, 1800),
            new Vector3(7600, 920, 2650), new Vector3(8200, 930, 2900),
        };

        static Vector2 V(float x, float y) => new Vector2(x, y);

        static float Ridged(float x, float z)
        {
            // warp the domain so the ridges wander, then a ridged multifractal: sharp crests, weighted by the octave below
            float wx = x + 1100f * (Mathf.PerlinNoise(x * 0.00011f + 3.1f, z * 0.00011f + 7.3f) - 0.5f) * 2f;
            float wz = z + 1100f * (Mathf.PerlinNoise(x * 0.00011f + 11.7f, z * 0.00011f + 1.9f) - 0.5f) * 2f;
            float sum = 0, amp = 1, freq = 1f / 4200f, norm = 0, weight = 1;
            for (int o = 0; o < 6; o++)
            {
                float n = 1 - Mathf.Abs(2 * Mathf.PerlinNoise(wx * freq + 17.3f * o + 5.2f, wz * freq + 9.1f * o + 3.4f) - 1);
                n *= n;
                sum += n * amp * weight;
                norm += amp;
                weight = Mathf.Clamp01(n * 1.8f);
                amp *= 0.5f;
                freq *= 2.03f;
            }
            return sum / norm;
        }

        static float Mountains(float x, float z)
        {
            float massif = Mathf.Lerp(0.7f, 1.15f, Mathf.PerlinNoise(x / 9000f + 41.1f, z / 9000f + 13.7f));
            // the noise runs about 0.2 to 0.95: spread it over valley-side hills to 4 km spires, without capping the tops flat
            float r = Mathf.InverseLerp(0.15f, 0.95f, Ridged(x, z));
            return Mathf.Min(4400f, 500f + 3500f * massif * Mathf.Pow(r, 1.6f));
        }

        // ---------- build ----------

        public static Terrain Build(TestWorldBuilder.Mats mats)
        {
            Directory.CreateDirectory(Dir);
            float[,] h = Heights();                                  // metres, [z, x]
            var original = (float[,])h.Clone();
            var rail = RailPath(original);
            var states = Classify(rail, original);
            Flatten(h, rail, states);

            // the asset first: the splat maps are its sub-assets and only get saved if it already exists
            var data = new TerrainData { heightmapResolution = Res };
            data.size = new Vector3(Size, Height, Size);
            AssetDatabase.CreateAsset(data, "Assets/OODA/Generated/TestWorld_Terrain.asset");
            var norm = new float[Res, Res];
            for (int j = 0; j < Res; j++)
                for (int i = 0; i < Res; i++)
                    norm[j, i] = Mathf.Clamp01((h[j, i] - Base) / Height);
            data.SetHeights(0, 0, norm);
            Paint(data, h);
            Plant(data, h, rail, states);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Alps";
            go.transform.position = new Vector3(-Size / 2, Base, -Size / 2);
            var t = go.GetComponent<Terrain>();
            t.materialTemplate = mats.terrain;
            t.heightmapPixelError = 3;
            t.basemapDistance = 6000;
            t.treeDistance = 7000;
            t.treeBillboardDistance = 7000;
            t.drawInstanced = true;

            var m = WorldMats(mats);
            MakeLakes(m);
            BuildRailway(rail, states, original, m);
            MakeVillage(h, m);

            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var v in h) { lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v); }
            Debug.Log($"OODA: alpine terrain built: {lo:F0} m to {hi:F0} m; floor under the fight {SampleArr(h, FightCentre.x, FightCentre.z):F0} m, flying at {FightAltFt * 0.3048f:F0} m.");
            return t;
        }

        static float[,] Heights()
        {
            var h = new float[Res, Res];
            var rs = new List<float>();
            for (int j = 0; j < Res; j++)
                for (int i = 0; i < Res; i++)
                {
                    h[j, i] = Mountains(X(i), X(j));
                    if (((i * 7 + j * 13) & 255) == 0) rs.Add(Ridged(X(i), X(j)));
                }
            rs.Sort();
            Debug.Log($"OODA: ridged noise p5 {rs[rs.Count / 20]:F3} p50 {rs[rs.Count / 2]:F3} p95 {rs[rs.Count * 19 / 20]:F3} max {rs[rs.Count - 1]:F3}.");

            var dist = new float[Res, Res];
            var floor = new float[Res, Res];
            foreach (var v in Valleys)
            {
                for (int j = 0; j < Res; j++) for (int i = 0; i < Res; i++) dist[j, i] = float.MaxValue;
                float reach = v.halfFloor + v.wall;
                int rc = Mathf.CeilToInt(reach / Cell) + 1;
                float step = v.gorge ? 15f : 40f;
                for (int s = 0; s + 1 < v.pts.Length; s++)
                {
                    Vector2 a = v.pts[s], b = v.pts[s + 1];
                    int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / step));
                    for (int k = 0; k <= n; k++)
                    {
                        float u = k / (float)n;
                        Vector2 p = Vector2.Lerp(a, b, u);
                        float f = Mathf.Lerp(v.floor[s], v.floor[s + 1], u);
                        int ci = Idx(p.x), cj = Idx(p.y);
                        for (int jj = Mathf.Max(0, cj - rc); jj <= Mathf.Min(Res - 1, cj + rc); jj++)
                            for (int ii = Mathf.Max(0, ci - rc); ii <= Mathf.Min(Res - 1, ci + rc); ii++)
                            {
                                float dx = X(ii) - p.x, dz = X(jj) - p.y;
                                float d = Mathf.Sqrt(dx * dx + dz * dz);
                                if (d < dist[jj, ii]) { dist[jj, ii] = d; floor[jj, ii] = f; }
                            }
                    }
                }
                for (int j = 0; j < Res; j++)
                    for (int i = 0; i < Res; i++)
                    {
                        float d = dist[j, i];
                        if (d >= reach) continue;
                        float t = Mathf.Clamp01((d - v.halfFloor) / v.wall);
                        float shape = v.gorge ? Mathf.Pow(t, 0.5f) : t * t * (3 - 2 * t);
                        float f = floor[j, i] + 12f * (Mathf.PerlinNoise(X(i) * 0.002f + 5, X(j) * 0.002f + 9) - 0.5f);
                        float carved = f + (h[j, i] - f) * shape;
                        h[j, i] = Mathf.Min(h[j, i], carved);
                    }
            }

            foreach (var l in Lakes)
                for (int j = 0; j < Res; j++)
                    for (int i = 0; i < Res; i++)
                    {
                        float ex = (X(i) - l.c.x) / l.r.x, ez = (X(j) - l.c.y) / l.r.y;
                        float r = Mathf.Sqrt(ex * ex + ez * ez);
                        if (r > 1.6f) continue;
                        float target = r < 1 ? l.level - 25f * (1 - r * r) : l.level + (r - 1) * 500f;
                        h[j, i] = Mathf.Min(h[j, i], target);
                    }

            // the island: down to the sea floor at the edges
            for (int j = 0; j < Res; j++)
                for (int i = 0; i < Res; i++)
                {
                    float d = Mathf.Max(Mathf.Abs(X(i)), Mathf.Abs(X(j))) / (Size / 2);
                    d += 0.04f * (Mathf.PerlinNoise(X(i) * 0.0004f + 2, X(j) * 0.0004f + 6) - 0.5f);
                    float e = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.84f, 0.98f, d));
                    h[j, i] = Mathf.Lerp(h[j, i], Base + 20f, e);
                }
            return h;
        }

        static float X(int i) => -Size / 2 + i * Cell;
        static int Idx(float x) => Mathf.Clamp(Mathf.RoundToInt((x + Size / 2) / Cell), 0, Res - 1);

        static float SampleArr(float[,] h, float x, float z)
        {
            float fx = Mathf.Clamp((x + Size / 2) / Cell, 0, Res - 1.001f), fz = Mathf.Clamp((z + Size / 2) / Cell, 0, Res - 1.001f);
            int i = (int)fx, j = (int)fz;
            float u = fx - i, w = fz - j;
            return Mathf.Lerp(Mathf.Lerp(h[j, i], h[j, i + 1], u), Mathf.Lerp(h[j + 1, i], h[j + 1, i + 1], u), w);
        }

        // ---------- ground colours ----------

        static void Paint(TerrainData data, float[,] h)
        {
            var meadow = TestWorldBuilder.MakeNoiseTexture("Alpine_Meadow", new Color(0.34f, 0.55f, 0.21f), new Color(0.42f, 0.62f, 0.26f));
            var forest = TestWorldBuilder.MakeNoiseTexture("Alpine_Forest", new Color(0.16f, 0.36f, 0.18f), new Color(0.22f, 0.42f, 0.20f));
            var rock = TestWorldBuilder.MakeNoiseTexture("Alpine_Rock", new Color(0.38f, 0.39f, 0.46f), new Color(0.47f, 0.47f, 0.53f));
            var snow = TestWorldBuilder.MakeNoiseTexture("Alpine_Snow", new Color(0.90f, 0.93f, 0.98f), new Color(0.98f, 0.99f, 1f));
            TerrainLayer Layer(string name, Texture2D tex, float tile)
            {
                string path = $"{Dir}/{name}.terrainlayer";
                var l = new TerrainLayer { diffuseTexture = tex, tileSize = new Vector2(tile, tile) };
                AssetDatabase.CreateAsset(l, path);
                return l;
            }
            data.alphamapResolution = SplatRes;
            data.terrainLayers = new[] { Layer("Meadow", meadow, 80), Layer("Forest", forest, 80), Layer("Rock", rock, 120), Layer("Snow", snow, 120) };
            var a = new float[SplatRes, SplatRes, 4];
            var cover = new float[4];
            for (int j = 0; j < SplatRes; j++)
                for (int i = 0; i < SplatRes; i++)
                {
                    float x = -Size / 2 + (i + 0.5f) * Size / SplatRes, z = -Size / 2 + (j + 0.5f) * Size / SplatRes;
                    float y = SampleArr(h, x, z);
                    float slope = Slope(h, x, z);
                    float jitter = 120f * (Mathf.PerlinNoise(x * 0.003f + 1, z * 0.003f + 4) - 0.5f);
                    // toon: narrow transitions between flat colours
                    float snowW = Step(2150 + jitter, 2250 + jitter, y) * (1 - Step(72, 80, slope));
                    float rockW = Mathf.Max(Step(38, 44, slope), Step(2050 + jitter, 2200 + jitter, y)) * (1 - snowW);
                    float forestW = Step(520, 600, y) * (1 - Step(1650 + jitter, 1750 + jitter, y)) * (1 - Step(32, 38, slope))
                                    * Step(0.38f, 0.46f, Mathf.PerlinNoise(x * 0.0012f + 7, z * 0.0012f + 3)) * (1 - rockW) * (1 - snowW);
                    float meadowW = Mathf.Max(0, 1 - snowW - rockW - forestW);
                    a[j, i, 0] = meadowW; a[j, i, 1] = forestW; a[j, i, 2] = rockW; a[j, i, 3] = snowW;
                    for (int l = 0; l < 4; l++) cover[l] += a[j, i, l];
                }
            data.SetAlphamaps(0, 0, a);
            float total = SplatRes * (float)SplatRes;
            var back = data.GetAlphamaps(0, 0, SplatRes, SplatRes);
            float rockBack = 0;
            for (int j = 0; j < SplatRes; j += 4) for (int i = 0; i < SplatRes; i += 4) rockBack += back[j, i, 2];
            Debug.Log($"OODA: ground cover meadow {cover[0] / total:P0}, forest {cover[1] / total:P0}, rock {cover[2] / total:P0}, snow {cover[3] / total:P0} (rock read back {rockBack * 16 / total:P0}, {data.alphamapLayers} layers).");
        }

        static float Step(float a, float b, float x) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, x));

        static float Slope(float[,] h, float x, float z)
        {
            float d = Cell;
            float gx = (SampleArr(h, x + d, z) - SampleArr(h, x - d, z)) / (2 * d);
            float gz = (SampleArr(h, x, z + d) - SampleArr(h, x, z - d)) / (2 * d);
            return Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
        }

        // ---------- pines ----------

        static void Plant(TerrainData data, float[,] h, List<Vector3> rail, State[] states)
        {
            var mb = new MeshBuilder(2);
            mb.Box(new Vector3(0, 2f, 0), Quaternion.identity, new Vector3(0.8f, 4f, 0.8f), 0);
            mb.Cone(new Vector3(0, 3f, 0), 3.6f, 7f, 8, 1);
            mb.Cone(new Vector3(0, 7f, 0), 2.9f, 6.5f, 8, 1);
            mb.Cone(new Vector3(0, 11f, 0), 2.1f, 6f, 8, 1);
            var mesh = SaveMesh(mb, "Pine");
            var trunk = TestWorldBuilder.Mat("Alpine_Trunk", new Color(0.36f, 0.23f, 0.13f), 0.1f);
            var needles = TestWorldBuilder.Mat("Alpine_Pine", new Color(0.10f, 0.32f, 0.17f), 0.1f);
            var go = new GameObject("Pine");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { trunk, needles };
            string prefabPath = $"{Dir}/Pine.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);

            data.treePrototypes = new[] { new TreePrototype { prefab = prefab } };
            var rnd = new System.Random(23);
            var trees = new List<TreeInstance>();
            for (int tries = 0; tries < 160000 && trees.Count < 14000; tries++)
            {
                float x = ((float)rnd.NextDouble() - 0.5f) * Size * 0.92f, z = ((float)rnd.NextDouble() - 0.5f) * Size * 0.92f;
                float y = SampleArr(h, x, z);
                if (y < 450 || y > 1800 || Slope(h, x, z) > 36) continue;
                // in clumps: the same noise as the forest colour, a little wider
                if (Mathf.PerlinNoise(x * 0.0012f + 7, z * 0.0012f + 3) < 0.38f) continue;
                if (NearRail(rail, x, z, 25f)) continue;
                if (Lakes.Any(l => { float ex = (x - l.c.x) / l.r.x, ez = (z - l.c.y) / l.r.y; return ex * ex + ez * ez < 1.3f; })) continue;
                float s = 0.8f + (float)rnd.NextDouble() * 0.7f;
                trees.Add(new TreeInstance
                {
                    position = new Vector3((x + Size / 2) / Size, 0, (z + Size / 2) / Size),
                    widthScale = s, heightScale = s * (0.9f + (float)rnd.NextDouble() * 0.3f),
                    rotation = (float)rnd.NextDouble() * Mathf.PI * 2, color = Color.white, lightmapColor = Color.white, prototypeIndex = 0,
                });
            }
            data.SetTreeInstances(trees.ToArray(), true);
            Debug.Log($"OODA: planted {trees.Count} pines.");
        }

        static bool NearRail(List<Vector3> rail, float x, float z, float r)
        {
            for (int k = 0; k < rail.Count; k += 2)
            {
                float dx = rail[k].x - x, dz = rail[k].z - z;
                if (dx * dx + dz * dz < r * r) return true;
            }
            return false;
        }

        // ---------- the railway ----------

        enum State { Ground, Bridge, Tunnel }
        const float RailStep = 10f;

        static List<Vector3> RailPath(float[,] ground)
        {
            var path = RailLine();
            // the rail height: the ground averaged over 2.4 km, so it bridges gorges and tunnels through ridges, then no
            // steeper than 3.5 % either way
            var g = path.Select(p => Mathf.Max(SampleArr(ground, p.x, p.z), 40f)).ToArray();
            int half = Mathf.RoundToInt(1200f / RailStep);
            var y = new float[g.Length];
            for (int k = 0; k < g.Length; k++)
            {
                int a = Mathf.Max(0, k - half), b = Mathf.Min(g.Length - 1, k + half);
                float sum = 0;
                for (int q = a; q <= b; q++) sum += g[q];
                y[k] = sum / (b - a + 1);
            }
            float rise = 0.035f * RailStep;
            for (int pass = 0; pass < 3; pass++)
            {
                for (int k = 1; k < y.Length; k++) y[k] = Mathf.Clamp(y[k], y[k - 1] - rise, y[k - 1] + rise);
                for (int k = y.Length - 2; k >= 0; k--) y[k] = Mathf.Clamp(y[k], y[k + 1] - rise, y[k + 1] + rise);
            }
            for (int k = 0; k < path.Count; k++) path[k] = new Vector3(path[k].x, y[k], path[k].z);
            return path;
        }

        static List<Vector3> RailLine()
        {
            var dense = new List<Vector3>();
            for (int s = 0; s + 1 < Rail.Length; s++)
            {
                Vector3 p0 = Rail[Mathf.Max(0, s - 1)], p1 = Rail[s], p2 = Rail[s + 1], p3 = Rail[Mathf.Min(Rail.Length - 1, s + 2)];
                for (int k = 0; k < 200; k++)
                {
                    float t = k / 200f;
                    dense.Add(0.5f * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t));
                }
            }
            dense.Add(Rail[Rail.Length - 1]);
            // even steps along the ground
            var path = new List<Vector3> { dense[0] };
            float carry = 0;
            for (int k = 1; k < dense.Count; k++)
            {
                Vector3 a = dense[k - 1], b = dense[k];
                float seg = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
                while (carry + seg >= RailStep)
                {
                    float u = (RailStep - carry) / seg;
                    a = Vector3.Lerp(a, b, u);
                    seg = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
                    path.Add(a);
                    carry = 0;
                }
                carry += seg;
            }
            return path;
        }

        static State[] Classify(List<Vector3> rail, float[,] h)
        {
            var s = new State[rail.Count];
            for (int k = 0; k < rail.Count; k++)
            {
                float g = SampleArr(h, rail[k].x, rail[k].z);
                s[k] = g < rail[k].y - 10 ? State.Bridge : g > rail[k].y + 30 ? State.Tunnel : State.Ground;
            }
            // no runs shorter than 4 steps: they take their neighbours' state
            for (int pass = 0; pass < 2; pass++)
                for (int k = 0; k < s.Length;)
                {
                    int e = k;
                    while (e + 1 < s.Length && s[e + 1] == s[k]) e++;
                    if (e - k + 1 < 4 && k > 0) for (int q = k; q <= e; q++) s[q] = s[k - 1];
                    k = e + 1;
                }
            // bridges land on solid ground: two more steps at each end
            var b = (State[])s.Clone();
            for (int k = 0; k < s.Length; k++)
                if (s[k] == State.Bridge)
                    for (int q = Mathf.Max(0, k - 2); q <= Mathf.Min(s.Length - 1, k + 2); q++)
                        if (b[q] == State.Ground) b[q] = State.Bridge;
            return b;
        }

        const float BedDepth = 0.93f;              // from the rail top down to the ground or deck under the ballast

        static void Flatten(float[,] h, List<Vector3> rail, State[] states)
        {
            var weight = new float[Res, Res];
            var target = new float[Res, Res];
            int rc = Mathf.CeilToInt(60f / Cell) + 1;
            for (int k = 0; k < rail.Count; k++)
            {
                bool nearEnd = states[k] != State.Ground && ((k > 0 && states[k - 1] == State.Ground) || (k + 1 < states.Length && states[k + 1] == State.Ground));
                if (states[k] != State.Ground && !nearEnd) continue;
                Vector3 p = rail[k];
                int ci = Idx(p.x), cj = Idx(p.z);
                for (int j = Mathf.Max(0, cj - rc); j <= Mathf.Min(Res - 1, cj + rc); j++)
                    for (int i = Mathf.Max(0, ci - rc); i <= Mathf.Min(Res - 1, ci + rc); i++)
                    {
                        float dx = X(i) - p.x, dz = X(j) - p.z;
                        float w = 1 - Step(14f, 60f, Mathf.Sqrt(dx * dx + dz * dz));
                        if (w > weight[j, i]) { weight[j, i] = w; target[j, i] = p.y - BedDepth; }
                    }
            }
            for (int j = 0; j < Res; j++)
                for (int i = 0; i < Res; i++)
                    if (weight[j, i] > 0) h[j, i] = Mathf.Lerp(h[j, i], target[j, i], weight[j, i]);
        }

        class WMats { public Material stone, steel, rail, ballast, sleeper, dark, water, wood, plaster, roof; }

        static WMats WorldMats(TestWorldBuilder.Mats mats) => new WMats
        {
            stone = TestWorldBuilder.Mat("Alpine_Stone", new Color(0.80f, 0.75f, 0.66f), 0.15f),
            steel = TestWorldBuilder.Mat("Alpine_Steel", new Color(0.70f, 0.16f, 0.10f), 0.35f, 0.3f),
            rail = TestWorldBuilder.Mat("Alpine_Rail", new Color(0.38f, 0.38f, 0.42f), 0.6f, 0.8f),
            ballast = TestWorldBuilder.Mat("Alpine_Ballast", new Color(0.48f, 0.44f, 0.40f), 0.05f),
            sleeper = TestWorldBuilder.Mat("Alpine_Sleeper", new Color(0.30f, 0.21f, 0.14f), 0.05f),
            dark = TestWorldBuilder.Mat("Alpine_Tunnel", new Color(0.03f, 0.03f, 0.04f), 0f),
            water = mats.water,
            wood = TestWorldBuilder.Mat("Alpine_ChaletWood", new Color(0.48f, 0.29f, 0.15f), 0.15f),
            plaster = TestWorldBuilder.Mat("Alpine_Plaster", new Color(0.94f, 0.92f, 0.86f), 0.1f),
            roof = TestWorldBuilder.Mat("Alpine_Roof", new Color(0.30f, 0.20f, 0.17f), 0.1f),
        };

        static void BuildRailway(List<Vector3> rail, State[] states, float[,] ground, WMats m)
        {
            var root = new GameObject("Railway").transform;
            // the track on the ground and on the bridges; inside the mountains it is hidden
            var track = new MeshBuilder(3);
            float sleeperCarry = 0;
            for (int k = 0; k + 1 < rail.Count; k++)
            {
                if (states[k] == State.Tunnel || states[k + 1] == State.Tunnel) continue;
                Vector3 a = rail[k], b = rail[k + 1];
                Vector3 dir = b - a;
                var rot = Quaternion.LookRotation(dir);
                Vector3 mid = (a + b) / 2;
                float len = dir.magnitude + 0.05f;
                track.Box(mid + rot * new Vector3(0, -0.63f, 0), rot, new Vector3(3.6f, 0.6f, len), 0);
                foreach (float side in new[] { -0.75f, 0.75f })
                    track.Box(mid + rot * new Vector3(side, -0.09f, 0), rot, new Vector3(0.14f, 0.18f, len), 1);
                for (sleeperCarry += len; sleeperCarry >= 2.5f; sleeperCarry -= 2.5f)
                {
                    Vector3 p = Vector3.Lerp(b, a, (sleeperCarry - 2.5f) / len);
                    track.Box(p + rot * new Vector3(0, -0.28f, 0), rot, new Vector3(2.6f, 0.2f, 0.25f), 2);
                }
            }
            MeshObject("Track", SaveMesh(track, "Track"), new[] { m.ballast, m.rail, m.sleeper }, root);

            int bridges = 0, tunnels = 0;
            for (int k = 0; k < states.Length;)
            {
                int e = k;
                while (e + 1 < states.Length && states[e + 1] == states[k]) e++;
                if (states[k] == State.Bridge) BuildBridge(rail, k, e, ground, m, root, bridges++);
                if (states[k] == State.Tunnel)
                {
                    if (k > 0) Portal(rail[k], rail[k] - rail[k - 1], m, root, $"Tunnel {tunnels} west");
                    if (e + 1 < rail.Count) Portal(rail[e], rail[e] - rail[e + 1], m, root, $"Tunnel {tunnels} east");
                    tunnels++;
                }
                k = e + 1;
            }
            Debug.Log($"OODA: railway of {rail.Count * RailStep / 1000f:F1} km with {bridges} bridges and {tunnels} tunnels.");
        }

        static void BuildBridge(List<Vector3> rail, int k0, int k1, float[,] ground, WMats m, Transform root, int n)
        {
            float length = (k1 - k0) * RailStep;
            float depth = 0;
            for (int k = k0; k <= k1; k++) depth = Mathf.Max(depth, rail[k].y - SampleArr(ground, rail[k].x, rail[k].z));
            bool viaduct = length >= 350f && depth < 350f;   // deep gorges get steel, long valley crossings stone
            var mb = new MeshBuilder(2);           // 0: stone or steel, 1: stone (piers under a truss)
            float deckW = viaduct ? 7.5f : 6f;

            // the deck
            for (int k = k0; k < k1; k++)
            {
                Vector3 a = rail[k], b = rail[k + 1];
                var rot = Quaternion.LookRotation(b - a);
                Vector3 mid = (a + b) / 2 + Vector3.down * (BedDepth + 1.25f);
                mb.Box(mid, rot, new Vector3(deckW, 2.5f, (b - a).magnitude + 0.1f), 0);
                foreach (float side in new[] { -1f, 1f })
                    mb.Box((a + b) / 2 + rot * new Vector3(side * (deckW / 2 - 0.3f), 0.1f, 0), rot, new Vector3(0.6f, 1.2f, (b - a).magnitude + 0.1f), 0);
            }

            if (viaduct)
            {
                // stone piers and arches, about 55 m apart, like the Landwasser and Wiesen viaducts
                int spans = Mathf.Max(2, Mathf.RoundToInt(length / 55f));
                var piers = Enumerable.Range(0, spans + 1).Select(i => k0 + Mathf.RoundToInt((k1 - k0) * i / (float)spans)).ToList();
                for (int i = 0; i < piers.Count; i++)
                {
                    Vector3 top = rail[piers[i]] + Vector3.down * (BedDepth + 2.5f);
                    float g = SampleArr(ground, top.x, top.z) - 8f;
                    float tall = top.y - g;
                    if (tall < 4f) continue;
                    var rot = Quaternion.LookRotation(rail[Mathf.Min(piers[i] + 1, k1)] - rail[Mathf.Max(piers[i] - 1, k0)]);
                    rot = Quaternion.Euler(0, rot.eulerAngles.y, 0);
                    float flare = Mathf.Clamp(tall * 0.04f, 0, 6);
                    mb.Box(new Vector3(top.x, g + tall * 0.3f, top.z), rot, new Vector3(deckW + 2 + flare, tall * 0.6f, 6 + flare), 0);
                    mb.Box(new Vector3(top.x, g + tall * 0.75f, top.z), rot, new Vector3(deckW + 1, tall * 0.5f, 5), 0);
                }
                for (int i = 0; i + 1 < piers.Count; i++)
                {
                    Vector3 a = rail[piers[i]], b = rail[piers[i + 1]];
                    Vector3 chord = b - a;
                    var rot = Quaternion.LookRotation(new Vector3(chord.x, 0, chord.z));
                    float r = new Vector2(chord.x, chord.z).magnitude / 2 - 2.5f;
                    Vector3 c = (a + b) / 2 + Vector3.down * (BedDepth + 2.5f + r * 0.85f);
                    const int segs = 14;
                    for (int s = 0; s < segs; s++)
                    {
                        float t0 = Mathf.PI * s / segs, t1 = Mathf.PI * (s + 1) / segs, tm = (t0 + t1) / 2;
                        Vector3 p = c + rot * new Vector3(0, Mathf.Sin(tm) * r * 0.85f, -Mathf.Cos(tm) * r);
                        Vector3 tangent = rot * new Vector3(0, Mathf.Cos(tm) * 0.85f, Mathf.Sin(tm)).normalized;
                        float segLen = Vector3.Distance(c + rot * new Vector3(0, Mathf.Sin(t0) * r * 0.85f, -Mathf.Cos(t0) * r),
                                                        c + rot * new Vector3(0, Mathf.Sin(t1) * r * 0.85f, -Mathf.Cos(t1) * r));
                        mb.Box(p, Quaternion.LookRotation(tangent, rot * Vector3.up), new Vector3(deckW, 3f, segLen + 0.4f), 0);
                    }
                    // the spandrel walls over the arch's haunches
                    foreach (float u in new[] { 0.12f, 0.88f })
                    {
                        Vector3 p = Vector3.Lerp(a, b, u) + Vector3.down * (BedDepth + 2.5f);
                        float hgt = r * 0.85f * (1 - Mathf.Sin(Mathf.PI * u)) + 2f;
                        mb.Box(p + Vector3.down * hgt / 2, rot, new Vector3(deckW, hgt, r * 0.4f), 0);
                    }
                }
            }
            else
            {
                // a red steel truss either side, with stone abutments and, over 160 m, stone piers
                const float trussH = 8f, half = 3.3f;
                for (int k = k0; k < k1; k++)
                {
                    Vector3 a = rail[k] + Vector3.down * BedDepth, b = rail[k + 1] + Vector3.down * BedDepth;
                    var rot = Quaternion.LookRotation(b - a);
                    Vector3 right = rot * Vector3.right;
                    foreach (float side in new[] { -half, half })
                    {
                        Vector3 ab = a + right * side, bb = b + right * side, at = ab + Vector3.up * trussH, bt = bb + Vector3.up * trussH;
                        Beam(mb, ab, bb, 0.7f, 0);
                        Beam(mb, at, bt, 0.7f, 0);
                        Beam(mb, ab, at, 0.5f, 0);
                        if ((k - k0) % 2 == 0) Beam(mb, ab, bt, 0.45f, 0); else Beam(mb, at, bb, 0.45f, 0);
                    }
                    if ((k - k0) % 2 == 0) Beam(mb, a - right * half + Vector3.up * trussH, a + right * half + Vector3.up * trussH, 0.4f, 0);
                }
                foreach (int k in new[] { k0, k1 })
                {
                    Vector3 p = rail[k] + Vector3.down * BedDepth;
                    float g = SampleArr(ground, p.x, p.z) - 6f;
                    var rot = Quaternion.Euler(0, Quaternion.LookRotation(rail[Mathf.Min(k + 1, k1)] - rail[Mathf.Max(k - 1, k0)]).eulerAngles.y, 0);
                    mb.Box(new Vector3(p.x, (p.y + g) / 2, p.z), rot, new Vector3(9f, p.y - g, 6f), 1);
                }
                if (length > 160f)
                {
                    int spans = Mathf.RoundToInt(length / 120f);
                    for (int i = 1; i < spans; i++)
                    {
                        int k = k0 + (k1 - k0) * i / spans;
                        Vector3 p = rail[k] + Vector3.down * (BedDepth + 2.5f);
                        float g = SampleArr(ground, p.x, p.z) - 8f;
                        var rot = Quaternion.Euler(0, Quaternion.LookRotation(rail[k + 1] - rail[k - 1]).eulerAngles.y, 0);
                        float tall = p.y - g;
                        mb.Box(new Vector3(p.x, g + tall / 2, p.z), rot, new Vector3(8f + tall * 0.03f, tall, 5f + tall * 0.02f), 1);
                    }
                }
            }

            Vector3 centre = rail[(k0 + k1) / 2];
            string name = $"Bridge {n} ({(viaduct ? "stone viaduct" : "steel truss")}, {length:F0} m long, {depth:F0} m high)";
            var go = MeshObject(name, SaveMesh(mb, $"Bridge{n}"), viaduct ? new[] { m.stone, m.stone } : new[] { m.steel, m.stone }, root);
            // a marker for the shot tools: where the middle is and which way the bridge runs
            var mark = new GameObject($"Bridge {n} centre").transform;
            mark.SetParent(go.transform, false);
            mark.position = centre;
            mark.rotation = Quaternion.LookRotation(Flat(rail[k1] - rail[k0]));
            Debug.Log($"OODA: {name} at ({centre.x:F0}, {centre.z:F0}), deck {centre.y:F0} m.");
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

        static void Beam(MeshBuilder mb, Vector3 a, Vector3 b, float thick, int sub)
        {
            Vector3 d = b - a;
            if (d.sqrMagnitude < 0.01f) return;
            var up = Mathf.Abs(Vector3.Dot(d.normalized, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
            mb.Box((a + b) / 2, Quaternion.LookRotation(d, up), new Vector3(thick, thick, d.magnitude + thick), sub);
        }

        static void Portal(Vector3 at, Vector3 outward, WMats m, Transform root, string name)
        {
            var rot = Quaternion.LookRotation(Flat(outward));
            var mb = new MeshBuilder(2);
            Vector3 b = at + Vector3.down * BedDepth;
            mb.Box(b + rot * new Vector3(0, 5f, -1.5f), rot, new Vector3(11f, 10f, 3f), 0);
            mb.Box(b + rot * new Vector3(0, 3f, -1.2f), rot, new Vector3(5.5f, 6.2f, 3.1f), 1);
            MeshObject(name, SaveMesh(mb, name.Replace(" ", "")), new[] { m.stone, m.dark }, root);
        }

        // ---------- lakes and the village ----------

        static void MakeLakes(WMats m)
        {
            var mb = new MeshBuilder(1);
            foreach (var l in Lakes) mb.Disc(new Vector3(l.c.x, l.level, l.c.y), l.r * 1.05f, 48, 0);
            var go = MeshObject("Lakes", SaveMesh(mb, "Lakes"), new[] { m.water }, null);
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        static void MakeVillage(float[,] h, WMats m)
        {
            // on the meadow at the north shore of the lower lake, by the main valley's road-less floor
            var mb = new MeshBuilder(3);          // 0 plaster, 1 wood, 2 roof
            var rnd = new System.Random(5);
            var placed = new List<Vector2>();
            for (int tries = 0; tries < 400 && placed.Count < 24; tries++)
            {
                var p = new Vector2(-300 + (float)rnd.NextDouble() * 600, -6850 + (float)rnd.NextDouble() * 500);
                if (placed.Any(q => Vector2.Distance(p, q) < 32)) continue;
                if (Slope(h, p.x, p.y) > 8) continue;
                placed.Add(p);
                float y = SampleArr(h, p.x, p.y);
                float w = 9 + (float)rnd.NextDouble() * 4, d = 8 + (float)rnd.NextDouble() * 3;
                var rot = Quaternion.Euler(0, (rnd.Next(2) * 90) + ((float)rnd.NextDouble() - 0.5f) * 20, 0);
                Vector3 c = new Vector3(p.x, y, p.y);
                mb.Box(c + rot * new Vector3(0, 0.5f, 0), rot, new Vector3(w, 3f, d), 0);                    // stone ground floor
                mb.Box(c + rot * new Vector3(0, 4.5f, 0), rot, new Vector3(w, 5f, d), 1);                    // wooden upper floors
                float pitch = 30f, run = (w / 2 + 1.2f) / Mathf.Cos(pitch * Mathf.Deg2Rad);
                foreach (float side in new[] { -1f, 1f })
                {
                    var rr = rot * Quaternion.Euler(0, 0, side * pitch);
                    Vector3 rc = c + rot * new Vector3(side * (w / 4 + 0.3f), 7f + Mathf.Tan(pitch * Mathf.Deg2Rad) * w / 4, 0);
                    mb.Box(rc, rr, new Vector3(run, 0.5f, d + 2.4f), 2);
                }
                // the gable ends
                mb.Box(c + rot * new Vector3(0, 7.9f, 0), rot, new Vector3(w * 0.5f, 1.8f, d - 0.1f), 1);
            }
            // the church
            {
                Vector3 c = new Vector3(250, SampleArr(h, 250, -6500), -6500);
                mb.Box(c + new Vector3(0, 6, 0), Quaternion.identity, new Vector3(12, 12, 22), 0);
                mb.Box(c + new Vector3(0, 13, -16), Quaternion.identity, new Vector3(6, 26, 6), 0);
                mb.Cone(c + new Vector3(0, 26, -16), 4.6f, 12f, 4, 2);
                foreach (float side in new[] { -1f, 1f })
                    mb.Box(c + new Vector3(side * 3.6f, 14.3f, 0), Quaternion.Euler(0, 0, side * 40), new Vector3(10.5f, 0.5f, 23f), 2);
            }
            MeshObject("Village", SaveMesh(mb, "Village"), new[] { m.plaster, m.wood, m.roof }, null);
            Debug.Log($"OODA: a village of {placed.Count} chalets and a church.");
        }

        // ---------- meshes ----------

        static Mesh SaveMesh(MeshBuilder mb, string name)
        {
            var mesh = mb.ToMesh(name);
            string path = $"{Dir}/{name}.asset";
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static GameObject MeshObject(string name, Mesh mesh, Material[] mats, Transform parent)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            go.isStatic = true;
            return go;
        }

        /// <summary>Boxes, cones and discs with flat normals, gathered into one mesh with a submesh per material.</summary>
        class MeshBuilder
        {
            readonly List<Vector3> verts = new List<Vector3>();
            readonly List<Vector3> normals = new List<Vector3>();
            readonly List<int>[] tris;

            public MeshBuilder(int submeshes) { tris = Enumerable.Range(0, submeshes).Select(_ => new List<int>()).ToArray(); }

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int sub)
            {
                // listed a, b, c, d round the face; Unity's front faces wind clockwise, so the triangles go a, c, b and a, d, c
                Vector3 n = Vector3.Cross(d - a, b - a).normalized;
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                for (int k = 0; k < 4; k++) normals.Add(n);
                tris[sub].AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            }

            void Tri(Vector3 a, Vector3 b, Vector3 c, int sub)
            {
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c);
                for (int k = 0; k < 3; k++) normals.Add(n);
                tris[sub].AddRange(new[] { i, i + 1, i + 2 });
            }

            public void Box(Vector3 c, Quaternion r, Vector3 size, int sub)
            {
                Vector3 hx = r * new Vector3(size.x / 2, 0, 0), hy = r * new Vector3(0, size.y / 2, 0), hz = r * new Vector3(0, 0, size.z / 2);
                Vector3 P(int x, int y, int z) => c + hx * x + hy * y + hz * z;
                Quad(P(-1, -1, 1), P(-1, 1, 1), P(1, 1, 1), P(1, -1, 1), sub);     // +z
                Quad(P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), P(-1, -1, -1), sub); // -z
                Quad(P(1, -1, 1), P(1, 1, 1), P(1, 1, -1), P(1, -1, -1), sub);     // +x
                Quad(P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), P(-1, -1, 1), sub); // -x
                Quad(P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), sub);     // +y
                Quad(P(-1, -1, 1), P(1, -1, 1), P(1, -1, -1), P(-1, -1, -1), sub); // -y
            }

            public void Cone(Vector3 baseCentre, float radius, float height, int sides, int sub)
            {
                Vector3 apex = baseCentre + Vector3.up * height;
                for (int s = 0; s < sides; s++)
                {
                    float a0 = 2 * Mathf.PI * s / sides, a1 = 2 * Mathf.PI * (s + 1) / sides;
                    Vector3 p0 = baseCentre + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radius;
                    Vector3 p1 = baseCentre + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius;
                    Tri(p0, apex, p1, sub);
                    Tri(p1, baseCentre, p0, sub);
                }
            }

            public void Disc(Vector3 c, Vector2 radii, int sides, int sub)
            {
                for (int s = 0; s < sides; s++)
                {
                    float a0 = 2 * Mathf.PI * s / sides, a1 = 2 * Mathf.PI * (s + 1) / sides;
                    Tri(c, c + new Vector3(Mathf.Cos(a1) * radii.x, 0, Mathf.Sin(a1) * radii.y), c + new Vector3(Mathf.Cos(a0) * radii.x, 0, Mathf.Sin(a0) * radii.y), sub);
                }
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(verts);
                mesh.SetNormals(normals);
                mesh.subMeshCount = tris.Length;
                for (int s = 0; s < tris.Length; s++) mesh.SetTriangles(tris[s], s);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
