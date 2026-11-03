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
    internal static partial class AlpineWorld
    {
        const string Dir = "Assets/OODA/Generated/Alpine";

        /// <summary>
        /// The least clearance along the route, every 25 m and 150 m either side (the squadrons fly up to 130 m off the line):
        /// over the ground and anything on the Obstacles layer below, and under anything on it above (counted as negative).
        /// </summary>
        public static float CheckEscortRoute(Terrain terrain)
        {
            float worst = float.MaxValue;
            int mask = LayerMask.GetMask(ObstacleLayer);
            for (int i = 0; i + 1 < EscortRoute.Length; i++)
            {
                Vector3 a = EscortRoute[i], b = EscortRoute[i + 1];
                float leg = float.MaxValue, over = 0;
                Vector3 at = a;
                string what = "ground";
                int n = Mathf.CeilToInt(Vector3.Distance(a, b) / 25f);
                for (int k = 0; k <= n; k++)
                {
                    Vector3 p = Vector3.Lerp(a, b, k / (float)n);
                    foreach (float side in new[] { -150f, -100f, -50f, 0f, 50f, 100f, 150f })
                    {
                        Vector3 q = p + Vector3.Cross(Vector3.up, (b - a).normalized) * side;
                        float g = terrain.SampleHeight(q) + terrain.transform.position.y;
                        string w = "ground";
                        if (Physics.Raycast(q, Vector3.down, out var hit, 5000f, mask) && hit.point.y > g) { g = hit.point.y; w = hit.collider.name.Split('(')[0].Trim(); }
                        float c = p.y - g;
                        if (Physics.Raycast(q, Vector3.up, out var above, 3000f, mask)) { c = -above.distance; w = "UNDER " + above.collider.name.Split('(')[0].Trim(); }
                        if (c < leg) { leg = c; at = q; over = g; what = w; }
                    }
                }
                float turn = i + 2 < EscortRoute.Length ? Vector2.Angle(V(b.x - a.x, b.z - a.z), V(EscortRoute[i + 2].x - b.x, EscortRoute[i + 2].z - b.z)) : 0;
                Debug.Log($"OODA: escort leg {i} ({a.x:F0}, {a.y:F0}, {a.z:F0}) to ({b.x:F0}, {b.y:F0}, {b.z:F0}): least clearance {leg:F0} m over {what} at {over:F0} m, ({at.x:F0}, {at.z:F0}); then turns {turn:F0} degrees.");
                worst = Mathf.Min(worst, leg);
            }
            return worst;
        }

        // ---------- build ----------

        public static Terrain Build(TestWorldBuilder.Mats mats)
        {
            Directory.CreateDirectory(Dir);
            var rail = RailPath();                                   // by design, so the valleys can be carved round it
            float[,] h = Heights(rail);                              // metres, [z, x]
            var original = (float[,])h.Clone();
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
            Plant(data, h, rail);
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
            MakeMountains();

            var m = WorldMats(mats);
            MakeLakes(m);
            MakeRiver(h, m);
            MakeRiverBridges(h, m);
            BuildRailway(rail, states, original, m);
            MakeTrain(rail);
            MakeVillage(h, m, rail);
            MakeFarms(h, rail);
            MakeTown(h, m, rail);
            MakeFlakSites(h);
            MakeAirHazards(h, rail, m);

            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var v in h) { lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v); }
            Debug.Log($"OODA: alpine terrain built: {lo:F0} m to {hi:F0} m; {Peaks.Count} mountains and hills; floor under the Chalet Lake arena {SampleArr(h, FightCentre.x, FightCentre.z):F0} m, flying at {FightAltFt * 0.3048f:F0} m.");
            foreach (var a in Ooda.Turn.Core.Arenas.All)
                Debug.Log($"OODA: arena {a.name}: ground {SampleArr(h, a.x, a.z):F0} m under its centre, starting at {a.altFt * 0.3048f:F0} m.");
            return t;
        }

        // ---------- ground colours ----------

        static void Paint(TerrainData data, float[,] h)
        {
            var meadow = TestWorldBuilder.MakeNoiseTexture("Alpine_Meadow", new Color(0.34f, 0.55f, 0.21f), new Color(0.42f, 0.62f, 0.26f));
            var forest = TestWorldBuilder.MakeNoiseTexture("Alpine_Forest", new Color(0.16f, 0.36f, 0.18f), new Color(0.22f, 0.42f, 0.20f));
            var rock = TestWorldBuilder.MakeNoiseTexture("Alpine_Rock", new Color(0.38f, 0.39f, 0.46f), new Color(0.47f, 0.47f, 0.53f));
            var snow = TestWorldBuilder.MakeNoiseTexture("Alpine_Snow", new Color(0.90f, 0.93f, 0.98f), new Color(0.98f, 0.99f, 1f));
            var field = TestWorldBuilder.MakeNoiseTexture("Alpine_Field", new Color(0.80f, 0.68f, 0.31f), new Color(0.88f, 0.76f, 0.38f));
            var pasture = TestWorldBuilder.MakeNoiseTexture("Alpine_Pasture", new Color(0.48f, 0.72f, 0.24f), new Color(0.56f, 0.80f, 0.30f));
            TerrainLayer Layer(string name, Texture2D tex, float tile)
            {
                string path = $"{Dir}/{name}.terrainlayer";
                var l = new TerrainLayer { diffuseTexture = tex, tileSize = new Vector2(tile, tile) };
                AssetDatabase.CreateAsset(l, path);
                return l;
            }
            data.alphamapResolution = SplatRes;
            data.terrainLayers = new[]
            {
                Layer("Meadow", meadow, 80), Layer("Forest", forest, 80), Layer("Rock", rock, 120), Layer("Snow", snow, 120),
                Layer("Field", field, 60), Layer("Pasture", pasture, 60),
            };
            var a = new float[SplatRes, SplatRes, CoverLayers];
            var cover = new float[CoverLayers];
            var w = new float[CoverLayers];
            for (int j = 0; j < SplatRes; j++)
                for (int i = 0; i < SplatRes; i++)
                {
                    float x = -Size / 2 + (i + 0.5f) * Size / SplatRes, z = -Size / 2 + (j + 0.5f) * Size / SplatRes;
                    GroundCover(h, x, z, w);
                    for (int l = 0; l < CoverLayers; l++) { a[j, i, l] = w[l]; cover[l] += w[l]; }
                }
            data.SetAlphamaps(0, 0, a);
            float total = SplatRes * (float)SplatRes;
            Debug.Log($"OODA: ground cover meadow {cover[0] / total:P0}, forest {cover[1] / total:P0}, rock {cover[2] / total:P0}, snow {cover[3] / total:P0}, fields {cover[4] / total:P0}, pasture {cover[5] / total:P0} ({data.alphamapLayers} layers).");
        }

        // ---------- trees ----------

        static void Plant(TerrainData data, float[,] h, List<Vector3> rail)
        {
            data.treePrototypes = new[] { TreePrefab(RoundTree), TreePrefab(TallTree), TreePrefab(PineTree) }.Select(p => new TreePrototype { prefab = p }).ToArray();
            var spots = TreeSpots(h, rail);
            var trees = spots.Select(s => new TreeInstance
            {
                position = new Vector3((s.x + Size / 2) / Size, 0, (s.z + Size / 2) / Size),
                widthScale = s.scale, heightScale = s.tall, rotation = s.yaw,
                color = Color.white, lightmapColor = Color.white, prototypeIndex = s.kind,
            }).ToArray();
            data.SetTreeInstances(trees, true);
            Debug.Log($"OODA: planted {trees.Length} trees ({spots.Count(t => t.kind == 0)} round, {spots.Count(t => t.kind == 1)} tall, {spots.Count(t => t.kind == 2)} pines).");
        }

        class WMats { public Material stone, steel, rail, ballast, sleeper, dark, water, river, wood, plaster, roof, slate, cobbles; }

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
            river = TestWorldBuilder.Mat("Alpine_River", new Color(0.18f, 0.66f, 0.74f), 0.9f),
            slate = TestWorldBuilder.Mat("Alpine_Slate", new Color(0.33f, 0.34f, 0.38f), 0.2f),
            cobbles = TestWorldBuilder.Mat("Alpine_Cobbles", new Color(0.62f, 0.60f, 0.56f), 0.1f),
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
            var go = MeshObject(name, SaveMesh(mb, $"Bridge{n}"), viaduct ? new[] { m.stone, m.stone } : new[] { m.steel, m.stone }, root, true);
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
            MeshObject(name, SaveMesh(mb, name.Replace(" ", "")), new[] { m.stone, m.dark }, root, true);
        }

        // ---------- the lakes, the river and its bridges ----------

        static void MakeLakes(WMats m)
        {
            var mb = new MeshBuilder(1);
            foreach (var l in Lakes) mb.Disc(new Vector3(l.c.x, l.level, l.c.y), l.r * 1.05f, 48, 0);
            var go = MeshObject("Lakes", SaveMesh(mb, "Lakes"), new[] { m.river }, null);
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>
        /// The river's water: a ribbon at its surface down the channel CarveRiver cut, a little wider than the channel's floor
        /// so it laps the banks. Left out inside the lakes (their water is there already) and where the banks are lower than the
        /// water (at the sea, where the sea takes over).
        /// </summary>
        static void MakeRiver(float[,] h, WMats m)
        {
            var r = River();
            var mb = new MeshBuilder(1);
            bool Wet(int k)
            {
                var rp = r[k];
                if (InLake(rp.p.x, rp.p.y) || rp.surface < 1f) return false;
                Vector2 dir = (r[Mathf.Min(k + 1, r.Count - 1)].p - r[Mathf.Max(k - 1, 0)].p).normalized, across = V(dir.y, -dir.x);
                float off = rp.half + 14f;
                return SampleArr(h, rp.p.x + across.x * off, rp.p.y + across.y * off) > rp.surface + 0.3f
                    && SampleArr(h, rp.p.x - across.x * off, rp.p.y - across.y * off) > rp.surface + 0.3f;
            }
            Vector3 Edge(int k, float side)
            {
                var rp = r[k];
                Vector2 dir = (r[Mathf.Min(k + 1, r.Count - 1)].p - r[Mathf.Max(k - 1, 0)].p).normalized, across = V(dir.y, -dir.x);
                Vector2 e = rp.p + across * side * (rp.half + 4f);
                return new Vector3(e.x, rp.surface, e.y);
            }
            int quads = 0;
            for (int k = 0; k + 2 < r.Count; k += 2)
            {
                if (!Wet(k) || !Wet(k + 2)) continue;
                mb.Flat(Edge(k, -1), Edge(k, 1), Edge(k + 2, 1), Edge(k + 2, -1), 0);
                quads++;
            }
            var go = MeshObject("River", SaveMesh(mb, "River"), new[] { m.river }, null);
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            Debug.Log($"OODA: the river's water: {quads * 20 / 1000f:F1} km of it.");
        }

        /// <summary>Little stone arch bridges over the river: a single arch springing from each bank, parapets along the deck.</summary>
        static void MakeRiverBridges(float[,] h, WMats m)
        {
            var root = new GameObject("River bridges").transform;
            int n = 0;
            foreach (var b in RiverBridges(h))
            {
                var mb = new MeshBuilder(1);
                var rot = Quaternion.Euler(0, b.yaw, 0);
                float half = b.span / 2, rise = Mathf.Min(9f, b.span * 0.28f), deckW = 5f;
                Vector3 c = b.at;
                // the arch: boxes round a flattened half circle from bank to bank
                const int segs = 12;
                for (int s = 0; s < segs; s++)
                {
                    float t0 = Mathf.PI * s / segs, t1 = Mathf.PI * (s + 1) / segs, tm = (t0 + t1) / 2;
                    Vector3 P(float t) => c + rot * new Vector3(0, Mathf.Sin(t) * rise, -Mathf.Cos(t) * half);
                    Vector3 mid = P(tm), d = P(t1) - P(t0);
                    mb.Box(mid, Quaternion.LookRotation(d, rot * Vector3.up), new Vector3(deckW, 1.6f, d.magnitude + 0.3f), 0);
                }
                // the deck over it, the spandrels down to the arch, the parapets, and the abutments into the banks
                float deckY = rise + 1.4f;
                mb.Box(c + rot * new Vector3(0, deckY, 0), rot, new Vector3(deckW, 0.8f, b.span + 8f), 0);
                foreach (float side in new[] { -1f, 1f })
                {
                    mb.Box(c + rot * new Vector3(side * (deckW / 2 - 0.3f), deckY + 0.9f, 0), rot, new Vector3(0.6f, 1.1f, b.span + 8f), 0);
                    float u = 0.78f;
                    float spandrel = deckY - Mathf.Sin(Mathf.Acos(u)) * rise;
                    mb.Box(c + rot * new Vector3(0, deckY - spandrel / 2, side * half * u), rot, new Vector3(deckW, spandrel, half * 0.44f), 0);
                    Vector3 foot = c + rot * new Vector3(0, 0, side * (half + 3f));
                    float g = SampleArr(h, foot.x, foot.z);
                    mb.Box(new Vector3(foot.x, (g - 2f + c.y + deckY) / 2, foot.z), rot, new Vector3(deckW + 1f, c.y + deckY - g + 2f, 6f), 0);
                }
                MeshObject($"Stone bridge {n++}", SaveMesh(mb, $"RiverBridge{n}"), new[] { m.stone }, root, true);
                Debug.Log($"OODA: a stone bridge over the river at ({c.x:F0}, {c.z:F0}), {b.span:F0} m across.");
            }
        }

        /// <summary>Empty markers where the flak guns go, standing on the ground at the FlakSites (moved off the river if one would be in it).</summary>
        static void MakeFlakSites(float[,] h)
        {
            var parent = new GameObject("Flak").transform;
            for (int i = 0; i < FlakSites.Length; i++)
            {
                var s = FlakSites[i];
                Vector2 p = V(s.x, s.z);
                // step out sideways until it is on dry, gentle ground
                Vector2 away = V(1, 0);
                for (int k = 0; k < 40 && (RiverNear(p.x, p.y) || Slope(h, p.x, p.y) > 14f); k++) p += away * (k % 2 == 0 ? 1 : -1) * 15f * (k + 1);
                var t = new GameObject($"FlakSite {i}").transform;
                t.SetParent(parent, false);
                t.position = new Vector3(p.x, SampleArr(h, p.x, p.y), p.y);
            }
            Debug.Log("OODA: flak sites " + string.Join(", ", parent.Cast<Transform>().Select(t => $"({t.position.x:F0}, {t.position.y:F0}, {t.position.z:F0}) slope {Slope(h, t.position.x, t.position.z):F0}")) + ".");
        }

        // ---------- the village and the farmhouses ----------

        /// <summary>A copy of the Hi3D chalet at a spot, its near and far meshes, solid on the Obstacles layer as a box round it.</summary>
        static void PlaceChalet(HouseSpot s, string name, Transform root, int layer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(s.pos + Vector3.down * 0.4f, Quaternion.Euler(0, s.yaw, 0));    // sunk a little so a slope shows no gap under it
            go.transform.localScale = Vector3.one * s.size;
            go.isStatic = true;
            AddLods(go, ChaletModel, 0.08f, 0.002f);
            var bounds = ChaletModel.near.bounds;
            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(0, bounds.size.y * ChaletModel.fit / 2, 0);
            box.size = bounds.size * ChaletModel.fit * 0.9f;
            go.layer = layer;
        }

        static void MakeVillage(float[,] h, WMats m, List<Vector3> rail)
        {
            // on the flat at the north end of the chalet lake, either side of the river where it comes in: copies of the alpine
            // chalet model (Art/Chalet) round a cobbled square with a fountain, and the church with its tall spire
            var root = new GameObject("Village").transform;
            root.gameObject.isStatic = true;
            Load(ChaletModel, false);
            if (ChaletModel.near == null) Debug.LogError("OODA: no chalet model (Art/Chalet/Chalet_Near.obj); the village has only its church.");
            int layer = ObstacleLayerIndex();
            var spots = ChaletModel.near != null ? VillageSpots(h, rail) : new List<HouseSpot>();
            for (int k = 0; k < spots.Count; k++) PlaceChalet(spots[k], $"Chalet {k + 1}", root, layer);

            // the square and its fountain
            var sq = new MeshBuilder(2);           // 0 cobbles, 1 stone
            Vector3 c = new Vector3(VillageCentre.x, SampleArr(h, VillageCentre.x, VillageCentre.y), VillageCentre.y);
            sq.Prism(c + Vector3.up * 0.15f, 40f, 0.5f, 16, 0);
            sq.Prism(c + Vector3.up * 0.5f, 3.2f, 1.0f, 12, 1);
            sq.Prism(c + Vector3.up * 1.4f, 0.5f, 2.6f, 6, 1);
            sq.Prism(c + Vector3.up * 2.8f, 1.2f, 0.4f, 10, 1);
            MeshObject("Square", SaveMesh(sq, "VillageSquare"), new[] { m.cobbles, m.stone }, root);

            // the church: a white nave under a slate roof, a stone tower with its belfry open on all four sides, an eight-sided
            // spire and a cross, the tower to the north
            var mb = new MeshBuilder(5);           // 0 plaster, 1 stone, 2 slate, 3 dark (the openings), 4 wood
            Vector3 ch = new Vector3(ChurchAt.x, SampleArr(h, ChurchAt.x, ChurchAt.y) - 0.5f, ChurchAt.y);
            var face = Quaternion.Euler(0, 0, 0);
            mb.Box(ch + new Vector3(0, 6.5f, -4), face, new Vector3(12, 13, 24), 0);
            foreach (float side in new[] { -1f, 1f })
                mb.Box(ch + new Vector3(side * 3.7f, 15.4f, -4), Quaternion.Euler(0, 0, -side * 44), new Vector3(11f, 0.6f, 25f), 2);   // a positive z turn lifts +x: so -side, eaves down
            mb.Box(ch + new Vector3(0, 9f, -17f), face, new Vector3(9, 18, 5), 0);                   // the apse end
            Vector3 tw = ch + new Vector3(0, 0, 11.5f);
            mb.Box(tw + new Vector3(0, 13f, 0), face, new Vector3(7.5f, 26f, 7.5f), 1);
            mb.Box(tw + new Vector3(0, 26.8f, 0), face, new Vector3(8.4f, 1.0f, 8.4f), 1);
            foreach (var (dx, dz) in new[] { (0f, 3.8f), (0f, -3.8f), (3.8f, 0f), (-3.8f, 0f) })
            {
                mb.Box(tw + new Vector3(dx, 22.5f, dz), Quaternion.Euler(0, dx != 0 ? 90 : 0, 0), new Vector3(2.4f, 4.2f, 0.3f), 3);   // the belfry openings
                mb.Box(tw + new Vector3(dx * 1.01f, 15f, dz * 1.01f), Quaternion.Euler(0, dx != 0 ? 90 : 0, 0), new Vector3(1.0f, 1.6f, 0.3f), 3);
            }
            mb.Cone(tw + new Vector3(0, 27.3f, 0), 4.6f, 17f, 8, 2);
            mb.Box(tw + new Vector3(0, 45.5f, 0), face, new Vector3(0.35f, 3.2f, 0.35f), 4);
            mb.Box(tw + new Vector3(0, 46.2f, 0), face, new Vector3(1.6f, 0.35f, 0.35f), 4);
            mb.Box(tw + new Vector3(0, 2.2f, 3.8f), face, new Vector3(2.4f, 4.4f, 0.3f), 3);           // the door, in the tower's north face
            MeshObject("Church", SaveMesh(mb, "Church"), new[] { m.plaster, m.stone, m.slate, m.dark, m.wood }, root, true);
            Debug.Log($"OODA: a village of {spots.Count} chalets round a square, and a church with a {46:F0} m spire, at ({VillageCentre.x:F0}, {VillageCentre.y:F0}).");
        }

        /// <summary>The farmhouses: single chalets dotted over the green slopes and the farmland.</summary>
        static void MakeFarms(float[,] h, List<Vector3> rail)
        {
            if (ChaletModel.near == null) return;
            var root = new GameObject("Farmhouses").transform;
            root.gameObject.isStatic = true;
            int layer = ObstacleLayerIndex();
            var spots = FarmSpots(h, rail);
            for (int k = 0; k < spots.Count; k++) PlaceChalet(spots[k], $"Farmhouse {k + 1}", root, layer);
            Debug.Log($"OODA: {spots.Count} farmhouses.");
        }

        // ---------- things to fly round: barrage balloons, cable cars and radio masts ----------
        // All of it is solid (the Obstacles layer): aircraft crash on it and rounds stop on it. It is placed from a fixed
        // seed, and nothing goes inside the bombers' corridor (150 m either side of the escort route) unless it is at least
        // 120 m under the route there, so CheckEscortRoute still passes. The wires are 0.3 m thick to look at but have a
        // 4 m collider, so the battle's segment linecasts catch them reliably.

        /// <summary>Every wire placed (balloon tethers, cable-car ropes, the masts and their guys), start and end, metres: for the planning view.</summary>
        public static readonly List<(Vector3 a, Vector3 b)> HazardWires = new List<(Vector3 a, Vector3 b)>();
        /// <summary>The middle of each barrage balloon, metres.</summary>
        public static readonly List<Vector3> BalloonCentres = new List<Vector3>();
        /// <summary>The foot of each radio mast, metres.</summary>
        public static readonly List<Vector3> MastFeet = new List<Vector3>();
        public const float BalloonRadius = 12f;    // half the balloon's length
        const float WireThick = 4f;                // the wires' colliders
        const float Corridor = 150f, CorridorMargin = 40f, UnderRoute = 120f;
        const float PylonH = 24f;                  // a cable car's pylon, about, from the ground to the ropes

        static void MakeAirHazards(float[,] h, List<Vector3> rail, WMats m)
        {
            HazardWires.Clear(); BalloonCentres.Clear(); MastFeet.Clear();
            var root = new GameObject("Air hazards").transform;
            var rnd = new System.Random(1944);
            var wire = TestWorldBuilder.Mat("Alpine_Wire", new Color(0.22f, 0.23f, 0.25f), 0.5f, 0.7f);
            var wires = new MeshBuilder(1);        // every thin wire, to look at (the colliders are separate)

            int spans = MakeCableCars(h, root, wires, rnd, m);
            int balloons = MakeBalloons(h, rail, root, wires, rnd);
            int masts = MakeMasts(h, rail, root, wires, rnd);

            var go = MeshObject("Hazard wires", SaveMesh(wires, "HazardWires"), new[] { wire }, root);
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            // the scene keeps a copy for the turn battle's planning view, which runs long after these lists are gone
            var marks = root.gameObject.AddComponent<Ooda.Turn.HazardMarkers>();
            marks.wireStarts = HazardWires.Select(w => w.a).ToArray();
            marks.wireEnds = HazardWires.Select(w => w.b).ToArray();
            marks.balloons = BalloonCentres.ToArray();
            marks.balloonRadius = BalloonRadius;

            // the clearance it leaves the bombers, worked out from the shapes (CheckEscortRoute measures it with the colliders)
            float least = float.MaxValue;
            foreach (var (a, b) in HazardWires)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 10f));
                for (int k = 0; k <= n; k++)
                {
                    Vector3 p = Vector3.Lerp(a, b, k / (float)n);
                    if (RouteGap(p, out float ry) < Corridor) least = Mathf.Min(least, ry - p.y - WireThick / 2);
                }
            }
            Debug.Log($"OODA: air hazards: {spans} cable-car spans, {balloons} barrage balloons, {masts} radio masts, {HazardWires.Count} wires; "
                      + (least < float.MaxValue ? $"the nearest wire in the bombers' corridor is {least:F0} m under their route." : "no wire in the bombers' corridor."));
            if (balloons < 10) Debug.LogWarning($"OODA: only {balloons} barrage balloons found room (10 to 16 wanted).");
            if (spans < 2) Debug.LogWarning($"OODA: only {spans} cable-car spans found room (2 to 3 wanted).");
            if (masts < 4) Debug.LogWarning($"OODA: only {masts} radio masts found room (4 to 6 wanted).");
        }

        /// <summary>Two or three cable-car spans wall to wall across the narrow main valley, well under the bombers: two ropes,
        /// a car hanging in the middle, and a lattice pylon and a station hut at each end.</summary>
        static int MakeCableCars(float[,] h, Transform root, MeshBuilder wires, System.Random rnd, WMats m)
        {
            var mats = new[]
            {
                TestWorldBuilder.Mat("Alpine_Pylon", new Color(0.36f, 0.38f, 0.37f), 0.3f, 0.5f),
                m.wood,
                m.roof,
                TestWorldBuilder.Mat("Alpine_CableCar", new Color(0.78f, 0.13f, 0.10f), 0.4f, 0.2f),
            };
            var mb = new MeshBuilder(4);           // 0 steel, 1 wood, 2 roof, 3 the car
            // where to try (a leg of the main line and how far along it), in order: clear of the gorge and the railway's crossing,
            // and the first one between the Valley Run's start and the Bf 109s ahead of it
            var spots = new[] { (7, 0.45f), (9, 0.55f), (8, 0.50f), (10, 0.40f), (8, 0.20f), (10, 0.75f) };   // the pass: not leg 6, where the viaduct crosses
            int made = 0;
            foreach (var (s, u0) in spots)
            {
                if (made == 3) break;
                Vector2 a2 = MainLine[s], b2 = MainLine[s + 1];
                Vector2 c = Vector2.Lerp(a2, b2, Mathf.Clamp01(u0 + Rand(rnd, -0.06f, 0.06f)));
                if (InTown(c.x, c.y) || InVillage(c.x, c.y)) continue;
                Vector2 along = (b2 - a2).normalized, across = V(along.y, -along.x);
                float floor = MainFloorAt(c.y);
                RouteGap(new Vector3(c.x, 0, c.y), out float routeY);
                float ropeY = routeY - UnderRoute - 15f - Rand(rnd, 0f, 30f);   // 135 to 165 m under the bombers
                if (ropeY < floor + 60f) continue;
                // each end: out from the middle to where the wall comes up to a pylon's height under the ropes
                var feet = new Vector3[2];
                bool ok = true;
                for (int e = 0; e < 2 && ok; e++)
                {
                    float side = e == 0 ? -1f : 1f;
                    ok = false;
                    for (float d = 60f; d < 900f; d += 4f)
                    {
                        Vector2 q = c + across * side * d;
                        float g = SampleArr(h, q.x, q.y);
                        if (g >= ropeY - PylonH) { feet[e] = new Vector3(q.x, g - 1f, q.y); ok = true; break; }
                    }
                }
                if (!ok) { Debug.Log($"OODA: no cable car on main leg {s}: no wall to hang it from."); continue; }
                Vector3 al = new Vector3(along.x, 0, along.y);
                var tops = feet.Select(f => new Vector3(f.x, Mathf.Max(ropeY, f.y + 12f), f.z)).ToArray();
                float span = Vector3.Distance(tops[0], tops[1]);
                float sag = 0.025f * span;
                const int segs = 8;
                Vector3[] Rope(float off)
                {
                    var pts = new Vector3[segs + 1];
                    for (int k = 0; k <= segs; k++)
                    {
                        float t = k / (float)segs;
                        pts[k] = Vector3.Lerp(tops[0], tops[1], t) + al * off + Vector3.down * (4f * sag * t * (1 - t));
                    }
                    return pts;
                }
                var ropes = new[] { Rope(-3f), Rope(3f) };
                bool clear = span < 1600f;
                foreach (var r in ropes)
                    for (int k = 0; k < segs && clear; k++)
                        clear = ClearOfRoute(r[k], r[k + 1], WireThick / 2 + 8f)             // the car hangs 8 m under them
                                && WireAboveGround(r[k], r[k + 1], h, k == 0 ? 0.3f : 0f, k == segs - 1 ? 0.7f : 1f, 6f);
                if (!clear) { Debug.Log($"OODA: no cable car on main leg {s}: it would be in the bombers' way or in the ground."); continue; }

                string name = $"Cable car {made}";
                foreach (var r in ropes)
                    for (int k = 0; k < segs; k++)
                    {
                        Beam(wires, r[k], r[k + 1], 0.3f, 0);
                        WireCollider(r[k], r[k + 1], root, $"{name} rope", WireThick);
                        HazardWires.Add((r[k], r[k + 1]));
                    }
                // the pylons, their cross-arms and the stations
                for (int e = 0; e < 2; e++)
                {
                    Lattice(mb, feet[e], tops[e].y - feet[e].y, 3.5f, 1.5f, 4, 4f, 0.45f, 0);
                    Beam(mb, tops[e] - al * 4.5f, tops[e] + al * 4.5f, 0.8f, 0);
                    Vector3 outward = new Vector3(across.x, 0, across.y) * (e == 0 ? -1f : 1f);
                    Vector3 hp = feet[e] + outward * 10f;
                    float hg = SampleArr(h, hp.x, hp.z);
                    var hr = Quaternion.LookRotation(outward);
                    mb.Box(new Vector3(hp.x, hg + 1.75f, hp.z), hr, new Vector3(8f, 5.5f, 7f), 1);
                    mb.Box(new Vector3(hp.x, hg + 4.8f, hp.z), hr, new Vector3(9.2f, 0.6f, 8.2f), 2);
                }
                // the car, in the middle, hanging from both ropes
                Vector3 m0 = ropes[0][segs / 2], m1 = ropes[1][segs / 2], mid = (m0 + m1) / 2;
                var cr = Quaternion.LookRotation(Flat(tops[1] - tops[0]));
                Beam(mb, m0, m1, 0.4f, 0);
                Beam(mb, mid, mid + Vector3.down * 4.2f, 0.35f, 0);
                mb.Box(mid + Vector3.down * 4.2f, cr, new Vector3(3.2f, 0.3f, 4.6f), 0);
                mb.Box(mid + Vector3.down * 5.7f, cr, new Vector3(3f, 2.8f, 4.4f), 3);
                Debug.Log($"OODA: {name} across main leg {s} at ({c.x:F0}, {c.y:F0}): {span:F0} m wall to wall, ropes {ropeY:F0} m ({ropeY - floor:F0} m over the floor, {routeY - ropeY:F0} m under the bombers).");
                made++;
            }
            if (made > 0) MeshObject("Cable cars", SaveMesh(mb, "CableCars"), mats, root, true);
            return made;
        }

        /// <summary>Barrage balloons 400 to 900 m over the floor, over the basin and the main valley but off the bombers' line,
        /// each on a steel tether straight down to a winch on the ground.</summary>
        static int MakeBalloons(float[,] h, List<Vector3> rail, Transform root, MeshBuilder wires, System.Random rnd)
        {
            var silver = TestWorldBuilder.Mat("Alpine_Balloon", new Color(0.70f, 0.72f, 0.74f), 0.45f, 0.1f);
            var winchMat = TestWorldBuilder.Mat("Alpine_Winch", new Color(0.24f, 0.27f, 0.22f), 0.2f, 0.3f);
            // the blimp, nose to +z: a stretched sphere, about 24 m long, and three fins at the tail (one up, two down and out)
            var body = new MeshBuilder(1);
            body.Ellipsoid(Vector3.zero, Quaternion.identity, new Vector3(4.6f, 4.6f, BalloonRadius), 16, 10, 0);
            foreach (float roll in new[] { 0f, 120f, 240f })
            {
                var r = Quaternion.AngleAxis(roll, Vector3.forward);
                body.Box(r * new Vector3(0, 4.6f, -9f), r, new Vector3(0.8f, 5.5f, 6.5f), 0);
            }
            var mesh = SaveMesh(body, "Balloon");
            var winches = new MeshBuilder(1);

            float total = MainLength();
            float wind = 205f;                     // they all ride nose into the same breeze, give or take
            int made = 0;
            for (int tries = 0; tries < 6000 && made < 14; tries++)
            {
                Vector2 c = AlongMain(Rand(rnd, 300f, total - 300f), out Vector2 dir);
                Vector2 across = V(dir.y, -dir.x);
                float side = rnd.Next(2) == 0 ? -1f : 1f;
                Vector2 q = c + across * side * Rand(rnd, 210f, 480f);
                float y = MainFloorAt(c.y) + Rand(rnd, 400f, 900f);
                float g = SampleArr(h, q.x, q.y);
                if (g > y - 150f) continue;        // too near the hillside: it would be sitting on the wall
                bool clear = true;
                for (int k = 0; k < 8 && clear; k++)
                {
                    float a = k * Mathf.PI / 4;
                    if (SampleArr(h, q.x + Mathf.Cos(a) * 45f, q.y + Mathf.Sin(a) * 45f) > y - 30f) clear = false;
                }
                if (!clear) continue;
                var centre = new Vector3(q.x, y, q.y);
                var foot = new Vector3(q.x, g, q.y);
                if (BalloonCentres.Any(p => Vector2.Distance(V(p.x, p.z), q) < 550f)) continue;
                if (NearRail(rail, q.x, q.y, 40f) || InLake(q.x, q.y) || InTown(q.x, q.y) || InVillage(q.x, q.y) || UnderMountain(q.x, q.y)) continue;
                if (HazardWires.Any(w => SegDist(centre, w.a, w.b) < 250f)) continue;    // clear of the cable cars
                if (!ClearOfRoute(foot, centre + Vector3.up * BalloonRadius, BalloonRadius + 2f)) continue;

                var rot = Quaternion.Euler(-4f, wind + Rand(rnd, -15f, 15f), 0);
                var go = MeshObject($"Barrage balloon {made}", mesh, new[] { silver }, root, true);
                go.transform.SetPositionAndRotation(centre, rot);
                Vector3 belly = centre + rot * new Vector3(0, -4.4f, 1.5f), winchTop = foot + Vector3.up * 2f;
                Beam(wires, winchTop, belly, 0.3f, 0);
                WireCollider(winchTop, belly, root, $"Barrage balloon {made} cable", WireThick);
                HazardWires.Add((winchTop, belly));
                winches.Box(foot + Vector3.up * 0.8f, Quaternion.Euler(0, rot.eulerAngles.y, 0), new Vector3(3f, 2.4f, 4.5f), 0);
                BalloonCentres.Add(centre);
                made++;
            }
            if (made > 0) MeshObject("Balloon winches", SaveMesh(winches, "BalloonWinches"), new[] { winchMat }, root, true);
            Debug.Log($"OODA: {made} barrage balloons: " + string.Join(", ", BalloonCentres.Select(p => $"({p.x:F0}, {p.y:F0}, {p.z:F0})")) + ".");
            return made;
        }

        /// <summary>Red and white lattice radio masts, 120 to 200 m tall, on crests and spurs beside the main valley, each held by three guy wires.</summary>
        static int MakeMasts(float[,] h, List<Vector3> rail, Transform root, MeshBuilder wires, System.Random rnd)
        {
            var red = TestWorldBuilder.Mat("Alpine_MastRed", new Color(0.80f, 0.18f, 0.12f), 0.3f, 0.3f);
            var white = TestWorldBuilder.Mat("Alpine_MastWhite", new Color(0.92f, 0.92f, 0.90f), 0.3f, 0.3f);
            var mb = new MeshBuilder(2);
            float start = MainLength(NarrowFrom), total = MainLength();
            int made = 0;
            for (int tries = 0; tries < 8000 && made < 5; tries++)
            {
                Vector2 c = AlongMain(Rand(rnd, start, total - 200f), out Vector2 dir);
                Vector2 across = V(dir.y, -dir.x);
                float side = rnd.Next(2) == 0 ? -1f : 1f;
                Vector2 q = c + across * side * Rand(rnd, 450f, 1400f);
                float g = SampleArr(h, q.x, q.y), floor = MainFloorAt(c.y);
                if (g < floor + 350f || g > floor + 1600f || g > 2600f) continue;
                float around = 0;
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.PI / 4;
                    around += SampleArr(h, q.x + Mathf.Cos(a) * 150f, q.y + Mathf.Sin(a) * 150f) / 8f;
                }
                if (g - around < 12f) continue;   // a crest or the nose of a spur: higher than the ground round it
                if (Slope(h, q.x, q.y) > 30f) continue;
                if (MastFeet.Any(f => Vector2.Distance(V(f.x, f.z), q) < 1800f)) continue;
                if (NearRail(rail, q.x, q.y, 80f) || MainDistance(q.x, q.y, out _) < 350f || InTown(q.x, q.y) || UnderMountain(q.x, q.y)) continue;
                float tall = Rand(rnd, 120f, 200f);
                Vector3 foot = new Vector3(q.x, g - 1f, q.y), top = foot + Vector3.up * tall, guyTop = foot + Vector3.up * (tall * 0.8f);
                if (!ClearOfRoute(foot, top, 30f)) continue;
                // three guys from 80 % of the way up to anchors on the ground half the height out, turned till they're clear of it
                Vector3[] anchors = null;
                float a0 = Rand(rnd, 0f, 120f);
                foreach (float turn in new[] { 0f, 40f, 80f })
                {
                    var tryA = new Vector3[3];
                    bool fine = true;
                    for (int k = 0; k < 3 && fine; k++)
                    {
                        float a = (a0 + turn + 120f * k) * Mathf.Deg2Rad;
                        Vector2 p = q + V(Mathf.Cos(a), Mathf.Sin(a)) * tall * 0.5f;
                        tryA[k] = new Vector3(p.x, SampleArr(h, p.x, p.y), p.y);
                        fine = WireAboveGround(guyTop, tryA[k], h, 0f, 0.85f, 3f) && ClearOfRoute(guyTop, tryA[k], WireThick / 2);
                    }
                    if (fine) { anchors = tryA; break; }
                }
                if (anchors == null) continue;

                string name = $"Radio mast {made}";
                Lattice(mb, foot, tall, 1.7f, 1.7f, 3, 6f, 0.5f, 0, 1, 20f);
                mb.Box(top + Vector3.up * 1.5f, Quaternion.identity, new Vector3(1.2f, 3f, 1.2f), 0);
                WireCollider(foot, top + Vector3.up * 3f, root, name, 5f);
                HazardWires.Add((foot, top));
                foreach (var an in anchors)
                {
                    mb.Box(an + Vector3.up * 0.5f, Quaternion.identity, new Vector3(2f, 1.6f, 2f), 1);
                    Beam(wires, guyTop, an, 0.25f, 0);
                    WireCollider(guyTop, an, root, $"{name} guy", WireThick);
                    HazardWires.Add((guyTop, an));
                }
                MastFeet.Add(foot);
                Debug.Log($"OODA: {name} at ({foot.x:F0}, {foot.y:F0}, {foot.z:F0}), {tall:F0} m tall, {foot.y - floor:F0} m over the valley floor.");
                made++;
            }
            if (made > 0) MeshObject("Radio masts", SaveMesh(mb, "RadioMasts"), new[] { red, white }, root);   // solid by their box colliders
            return made;
        }

        /// <summary>A straight piece of something radius metres thick is out of the bombers' way: nowhere within the corridor
        /// (and a margin) of their route unless it is at least UnderRoute metres under it there.</summary>
        static bool ClearOfRoute(Vector3 a, Vector3 b, float radius)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 10f));
            for (int k = 0; k <= n; k++)
            {
                Vector3 p = Vector3.Lerp(a, b, k / (float)n);
                if (RouteGap(p, out float ry) < Corridor + radius + CorridorMargin && p.y + radius > ry - UnderRoute) return false;
            }
            return true;
        }

        /// <summary>A wire from a to b is at least clear metres over the ground between the fractions t0 and t1 of the way along it.</summary>
        static bool WireAboveGround(Vector3 a, Vector3 b, float[,] h, float t0, float t1, float clear)
        {
            int n = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(a, b) / 8f));
            for (int k = 0; k <= n; k++)
            {
                float t = Mathf.Lerp(t0, t1, k / (float)n);
                Vector3 p = Vector3.Lerp(a, b, t);
                if (SampleArr(h, p.x, p.z) > p.y - clear) return false;
            }
            return true;
        }

        static float SegDist(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            float u = d.sqrMagnitude < 1e-6f ? 0 : Mathf.Clamp01(Vector3.Dot(p - a, d) / d.sqrMagnitude);
            return Vector3.Distance(p, a + d * u);
        }

        /// <summary>A solid stand-in for a wire (or a lattice mast): a box thick metres square from a to b on the Obstacles layer, no mesh.</summary>
        static void WireCollider(Vector3 a, Vector3 b, Transform parent, string name, float thick)
        {
            Vector3 d = b - a;
            if (d.sqrMagnitude < 0.01f) return;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var up = Mathf.Abs(Vector3.Dot(d.normalized, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
            go.transform.SetPositionAndRotation((a + b) / 2, Quaternion.LookRotation(d, up));
            go.AddComponent<BoxCollider>().size = new Vector3(thick, thick, d.magnitude);
            go.layer = ObstacleLayerIndex();
            go.isStatic = true;
        }

        /// <summary>A lattice tower of beams: legs (3 or 4) from radius baseR at the foot to topR at the top, a ring and a
        /// diagonal brace every step metres; with bandSub, it changes colour every bandM metres (a radio mast's red and white).</summary>
        static void Lattice(MeshBuilder mb, Vector3 foot, float height, float baseR, float topR, int legs, float step, float thick, int sub, int bandSub = -1, float bandM = 0)
        {
            int levels = Mathf.Max(1, Mathf.CeilToInt(height / step));
            float turn = legs == 4 ? Mathf.PI / 4 : Mathf.PI / 2;
            Vector3 Corner(int k, int i)
            {
                float t = k / (float)levels, r = Mathf.Lerp(baseR, topR, t), a = turn + 2 * Mathf.PI * i / legs;
                return foot + new Vector3(Mathf.Cos(a) * r, height * t, Mathf.Sin(a) * r);
            }
            for (int k = 0; k < levels; k++)
            {
                int s = bandSub >= 0 && bandM > 0 && Mathf.FloorToInt((k + 0.5f) * height / levels / bandM) % 2 == 1 ? bandSub : sub;
                for (int i = 0; i < legs; i++)
                {
                    int j = (i + 1) % legs;
                    Beam(mb, Corner(k, i), Corner(k + 1, i), thick, s);
                    Beam(mb, Corner(k + 1, i), Corner(k + 1, j), thick * 0.6f, s);
                    if (k % 2 == 0) Beam(mb, Corner(k, i), Corner(k + 1, j), thick * 0.45f, s);
                    else Beam(mb, Corner(k, j), Corner(k + 1, i), thick * 0.45f, s);
                }
            }
        }

        // ---------- meshes ----------

        static Mesh SaveMesh(MeshBuilder mb, string name)
        {
            var mesh = mb.ToMesh(name);
            string path = $"{Dir}/{name}.asset";
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        /// <param name="obstacle">Solid: a MeshCollider on the Obstacles layer, which the turn battle's linecasts crash planes and stop rounds on.</param>
        static GameObject MeshObject(string name, Mesh mesh, Material[] mats, Transform parent, bool obstacle = false)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            go.isStatic = true;
            if (obstacle)
            {
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
                go.layer = ObstacleLayerIndex();
            }
            return go;
        }

        // ---------- the Obstacles layer ----------

        public const string ObstacleLayer = "Obstacles";

        /// <summary>The Obstacles layer's index, adding it to the project's layers (in the first free user slot) if it is missing.</summary>
        public static int ObstacleLayerIndex()
        {
            int have = LayerMask.NameToLayer(ObstacleLayer);
            if (have >= 0) return have;
            var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tags.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
                if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue))
                {
                    layers.GetArrayElementAtIndex(i).stringValue = ObstacleLayer;
                    tags.ApplyModifiedPropertiesWithoutUndo();
                    Debug.Log($"OODA: added the {ObstacleLayer} layer as layer {i}.");
                    return i;
                }
            Debug.LogError($"OODA: no free layer for {ObstacleLayer}.");
            return 0;
        }

        // ---------- clouds ----------

        const string CloudFbx = "Assets/OODA/Art/Cloud/Cloud.fbx";

        /// <summary>
        /// Cloud banks in three layers among the peaks, and some low ones over the low ground. Each bank is three to seven
        /// copies of the cloud model, each stretched, turned and tipped its own way and bunched together, so no two banks
        /// have the same shape; most have a haze round them, a few large soft billboards that thin out at their edges.
        /// </summary>
        public static void MakeClouds(Terrain terrain)
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(CloudFbx);
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.animationType = ModelImporterAnimationType.None;
            imp.importAnimation = false;
            imp.SaveAndReimport();
            var ms = (TextureImporter)AssetImporter.GetAtPath("Assets/OODA/Art/Cloud/Cloud_MetallicSmoothness.png");
            if (ms.sRGBTexture) { ms.sRGBTexture = false; ms.SaveAndReimport(); }
            var mat = TestWorldBuilder.Textured("Cloud3D", "Assets/OODA/Art/Cloud/Cloud_BaseColor.png", "Assets/OODA/Art/Cloud/Cloud_MetallicSmoothness.png");
            mat.SetFloat("_Metallic", 0f);         // vapour, not metal: the map's smoothness still shapes the highlights
            var haze = HazeMaterial();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(CloudFbx);
            // the model is meant to be 100 m long: measure what the import gives and make up the difference
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(model);
            var pb = probe.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
            Object.DestroyImmediate(probe);
            float length = Mathf.Max(pb.size.x, pb.size.z);
            float fit = length > 1f ? 100f / length : 1f;
            Debug.Log($"OODA: Cloud.fbx imports {pb.size.x:F1} x {pb.size.y:F1} x {pb.size.z:F1} m; scaled by {fit:F2} to 100 m long.");

            var parent = new GameObject("Clouds").transform;
            var rnd = new System.Random(7);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            float Ground(float x, float z) => terrain.SampleHeight(new Vector3(x, 0, z)) + terrain.transform.position.y;
            int banks = 0, pieces = 0, hazy = 0, low = 0;

            // clear of the rock under the whole bank: its middle and eight points round its edge
            bool Clear(Vector3 at, float radius, float margin)
            {
                if (Ground(at.x, at.z) > at.y - margin) return false;
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.PI / 4;
                    if (Ground(at.x + Mathf.Cos(a) * radius, at.z + Mathf.Sin(a) * radius) > at.y - margin) return false;
                }
                return true;
            }

            void Bank(Vector3 at, float piece, int count, float spread, bool withHaze)
            {
                var bank = new GameObject($"Cloud bank {banks++}").transform;
                bank.SetParent(parent, false);
                bank.SetPositionAndRotation(at, Quaternion.Euler(0, R(0, 360), 0));
                for (int k = 0; k < count; k++)
                {
                    // spread along the bank more than across it, the middle ones bigger and higher
                    float u = count == 1 ? 0 : k / (count - 1f) * 2 - 1;
                    var off = new Vector3(R(-0.35f, 0.35f) * spread, (1 - Mathf.Abs(u)) * piece * R(0.05f, 0.25f), u * spread + R(-0.15f, 0.15f) * spread);
                    float size = piece / 100f * fit * R(0.6f, 1.15f) * (1.2f - 0.4f * Mathf.Abs(u));
                    var c = (GameObject)PrefabUtility.InstantiatePrefab(model);
                    c.name = $"Cloud {pieces++}";
                    c.transform.SetParent(bank, false);
                    c.transform.localPosition = off;
                    c.transform.localRotation = Quaternion.Euler(R(-6, 6), R(0, 360), R(-6, 6));
                    c.transform.localScale = new Vector3(size * R(0.8f, 1.7f), size * R(0.75f, 1.25f), size * R(0.8f, 1.4f));
                    foreach (var r in c.GetComponentsInChildren<Renderer>())
                    {
                        r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
                        r.shadowCastingMode = ShadowCastingMode.Off;
                    }
                }
                if (withHaze) { Haze(bank, haze, piece, spread, rnd); hazy++; }
            }

            var bands = new[] { (2600f, 3300f, 24), (3500f, 4200f, 22), (4600f, 5400f, 16) };
            foreach (var (lo, hi, count) in bands)
                for (int n = 0, tries = 0; n < count && tries < 4000; tries++)
                {
                    float piece = R(140f, 380f);
                    int parts = rnd.Next(3, 8);
                    float spread = piece * R(0.35f, 0.6f) * parts / 3f;
                    var at = new Vector3(R(-15000, 15000), R(lo, hi), R(-15000, 15000));
                    if (!Clear(at, spread + piece, 150f)) continue;
                    Bank(at, piece, parts, spread, rnd.NextDouble() < 0.75);
                    n++;
                }
            // low cloud over the low ground: a few hundred metres up, smaller banks
            for (int tries = 0; low < 16 && tries < 6000; tries++)
            {
                float x = R(-8500, 8500), z = R(-8500, 8500);
                float g = Ground(x, z);
                if (g < 250 || g > 1100) continue;
                float piece = R(70f, 180f);
                int parts = rnd.Next(2, 5);
                float spread = piece * R(0.35f, 0.55f) * parts / 3f;
                var at = new Vector3(x, g + R(180, 450), z);
                if (!Clear(at, spread + piece, 70f)) continue;
                Bank(at, piece, parts, spread, rnd.NextDouble() < 0.6);
                low++;
            }
            Debug.Log($"OODA: {banks} cloud banks of {pieces} clouds, {hazy} with haze, {low} of them low.");
        }

        /// <summary>A haze round a cloud bank: two soft shells round it (see Haze.shader), the outer one larger and fainter.</summary>
        static void Haze(Transform bank, Material haze, float piece, float spread, System.Random rnd)
        {
            var sphere = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
            float across = Mathf.Max(spread, piece) * 1.5f + piece, tall = piece * 1.1f;
            foreach (var (grow, name) in new[] { (1f, "Haze"), (1.45f, "Haze outer") })
            {
                var go = new GameObject(name);
                go.transform.SetParent(bank, false);
                go.transform.localPosition = Vector3.up * piece * 0.15f;
                go.transform.localScale = new Vector3(across * 0.8f, tall, across * 1.2f) * grow;
                go.AddComponent<MeshFilter>().sharedMesh = sphere;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = haze;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                if (grow > 1) r.sharedMaterial = HazeOuter ??= MakeHazeOuter(haze);
            }
        }

        static Material HazeOuter;
        static Material MakeHazeOuter(Material inner)
        {
            var m = MatFrom(inner, "Assets/OODA/Generated/HazeOuter.mat");
            m.SetColor("_Color", new Color(0.96f, 0.97f, 1f, 0.12f));
            m.SetFloat("_Power", 4f);
            return m;
        }

        static Material MatFrom(Material src, string path)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(src); AssetDatabase.CreateAsset(m, path); }
            m.shader = src.shader;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>The haze's material, on OODA/Haze.</summary>
        static Material HazeMaterial()
        {
            HazeOuter = null;
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/OODA/Art/Cloud/Haze.shader");
            const string path = "Assets/OODA/Generated/Haze.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetColor("_Color", new Color(0.98f, 0.98f, 1f, 0.28f));
            m.SetFloat("_Power", 2.5f);
            m.SetFloat("_NearFade", 300f);
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
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

            /// <summary>A flat quad facing up (whichever way round its corners are given).</summary>
            public void Flat(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int sub)
            {
                if (Vector3.Cross(d - a, b - a).y > 0) Quad(a, b, c, d, sub); else Quad(a, d, c, b, sub);   // Quad faces Cross(d - a, b - a)
            }

            /// <summary>An upright prism (a cylinder of a few sides), its foot centred at c.</summary>
            public void Prism(Vector3 c, float radius, float height, int sides, int sub)
            {
                Vector3 up = Vector3.up * height;
                for (int s = 0; s < sides; s++)
                {
                    float a0 = 2 * Mathf.PI * s / sides, a1 = 2 * Mathf.PI * (s + 1) / sides;
                    Vector3 p0 = c + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radius, p1 = c + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius;
                    Quad(p1, p1 + up, p0 + up, p0, sub);   // a to d along the face, a to b up it: Cross(d - a, b - a) points out
                    Tri(c + up, p1 + up, p0 + up, sub);
                }
            }

            /// <summary>A cylinder of radius round the line from a to b, its ends capped.</summary>
            public void Tube(Vector3 a, Vector3 b, float radius, int sides, int sub)
            {
                Vector3 ax = b - a;
                var rot = Quaternion.LookRotation(ax, Mathf.Abs(Vector3.Dot(ax.normalized, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up);
                for (int s = 0; s < sides; s++)
                {
                    float a0 = 2 * Mathf.PI * s / sides, a1 = 2 * Mathf.PI * (s + 1) / sides;
                    Vector3 o0 = rot * new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0) * radius, o1 = rot * new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0) * radius;
                    Quad(a + o1, a + o0, b + o0, b + o1, sub);
                    Tri(a, a + o1, a + o0, sub);
                    Tri(b, b + o0, b + o1, sub);
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

            /// <summary>An ellipsoid with its long axis along local z (radii.z), segs round and rings from end to end.</summary>
            public void Ellipsoid(Vector3 c, Quaternion r, Vector3 radii, int segs, int rings, int sub)
            {
                Vector3 P(int i, int j)
                {
                    float th = Mathf.PI * j / rings, ph = 2 * Mathf.PI * i / segs;
                    return c + r * Vector3.Scale(radii, new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Sin(th) * Mathf.Sin(ph), Mathf.Cos(th)));
                }
                for (int j = 0; j < rings; j++)
                    for (int i = 0; i < segs; i++)
                    {
                        Vector3 a = P(i, j), b = P(i + 1, j), cc = P(i + 1, j + 1), d = P(i, j + 1);
                        if (j == 0) Tri(a, d, cc, sub);                 // a and b are the same point, the +z end
                        else if (j == rings - 1) Tri(a, cc, b, sub);    // cc and d are the -z end
                        else Quad(a, b, cc, d, sub);
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
