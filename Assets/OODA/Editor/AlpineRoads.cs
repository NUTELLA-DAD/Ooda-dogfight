using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ooda.EditorTools
{
    /// <summary>
    /// The roads: what makes the village, the farms and the town one place instead of houses dropped on a lawn.
    /// - the village: a main street from the square over the stone bridge to the church door, and a path from every
    ///   chalet's front to the nearest lane already there, so they branch from house to house;
    /// - the town: cobbled streets between the rows of houses (the rows AlpineTown leaves clear) and only past houses,
    ///   round the factory's yard, and a road round the airfield to the hangars;
    /// - the country: a gravel road from the village to the town, a lane to each of the other stone bridges, and a farm
    ///   track from every farmhouse to the nearest road or track already there, so the tracks branch like real ones.
    ///   These find their own way over the ground (a search over the heightmap's grid): they keep off steep ground, the
    ///   lakes, the sea and the houses, go round the woods when they can, and cross the river only on a bridge.
    /// All of it is one mesh of ribbons laid on the ground (a hand's width above it, and a little more with distance), coloured
    /// per vertex with a darker verge, on the land's toon shader. No colliders: nothing about the flying changes.
    /// The houses are read from the open scene, so OODA > Add Roads puts roads into scenes that are already built, and
    /// AlpineWorld.Build calls the same code on a new one.
    /// </summary>
    internal static partial class AlpineWorld
    {
        const string RoadMeshPath = Dir + "/Roads.asset";
        const string RoadMatPath = "Assets/OODA/Generated/Alpine_Road.mat";

        // (as the shader reads them: linear, like the east lands' painted ground)
        static readonly Color RoadGravel = new Color(0.66f, 0.61f, 0.50f), RoadDirt = new Color(0.60f, 0.52f, 0.38f),
            RoadCobble = new Color(0.60f, 0.58f, 0.54f), RoadVerge = new Color(0.36f, 0.40f, 0.20f);

        sealed class RoadLine { public List<Vector2> pts; public float width, lift; public Color col; public bool strip; }

        /// <summary>The scenes the world is in (the test world and its copies).</summary>
        static readonly string[] RoadScenes =
        {
            "Assets/OODA/Scenes/TestWorld.unity", "Assets/OODA/Scenes/TurnBattle.unity",
            "Assets/OODA/Scenes/Title.unity", "Assets/OODA/Scenes/JetTest.unity",
        };

        /// <summary>OODA > Add Roads: the roads into every scene that has the world, without rebuilding it.</summary>
        [MenuItem("OODA/Add Roads")]
        public static void AddRoadsToScenes()
        {
            Mesh mesh = null;
            foreach (var path in RoadScenes)
            {
                if (!System.IO.File.Exists(path)) continue;
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var terrain = Object.FindFirstObjectByType<Terrain>();
                if (terrain == null) { Debug.LogWarning($"OODA roads: no terrain in {path}; skipped."); continue; }
                if (mesh == null)
                {
                    var data = terrain.terrainData;
                    var norm = data.GetHeights(0, 0, Res, Res);
                    var h = new float[Res, Res];
                    for (int j = 0; j < Res; j++)
                        for (int i = 0; i < Res; i++) h[j, i] = norm[j, i] * Height + Base;
                    mesh = RoadMesh(h, RailPath(), data);
                }
                PlaceRoads(mesh);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"OODA roads: added to {path}.");
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>The roads into the scene being built (after the village, the farms and the town are in it).</summary>
        static void MakeRoads(float[,] h, List<Vector3> rail, TerrainData data) => PlaceRoads(RoadMesh(h, rail, data));

        static void PlaceRoads(Mesh mesh)
        {
            var old = GameObject.Find("Roads");
            if (old != null) Object.DestroyImmediate(old);
            if (mesh == null) return;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(RoadMatPath);
            if (mat == null)
            {
                mat = new Material(ToonWorld.LandModel) { name = "Alpine_Road" };
                AssetDatabase.CreateAsset(mat, RoadMatPath);
            }
            mat.shader = ToonWorld.LandModel;
            mat.SetFloat("_VertexColour", 1f);
            mat.SetFloat("_PatchFrom", 1e9f);      // (no east-lands patches: the ribbons carry their own verge)
            mat.SetFloat("_FarLift", 0.004f);      // raised a little with distance, so the ground's far mesh never cuts through
            EditorUtility.SetDirty(mat);
            var go = MeshObject("Roads", mesh, new[] { mat }, null);
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        // ---------------------------------------------------------------------------------------------------------

        static Mesh RoadMesh(float[,] h, List<Vector3> rail, TerrainData data)
        {
            var village = SceneHouses("Village", "Chalet");
            var farms = SceneHouses("Farmhouses", "Farmhouse");
            var bridges = RiverBridges(h);
            var lines = new List<RoadLine>();
            var discs = new List<(Vector2 at, float r, Color col)>();

            // ---- what a road may not cross, on the heightmap's grid
            var blocked = new bool[Res, Res];
            var riverCore = new bool[Res, Res];     // the water itself: no ribbon is drawn there (the bridge's deck is the road)
            var slow = new float[Res, Res];         // extra cost: woods, a level crossing
            var isRoad = new bool[Res, Res];
            float[,,] cover = data != null && data.alphamapLayers > 1 ? data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight) : null;
            for (int j = 0; j < Res; j++)
                for (int i = 0; i < Res; i++)
                {
                    float x = X(i), z = X(j), y = h[j, i];
                    if (y < 4f || y > 1500f || InLake(x, z) || Mathf.Abs(x) > 9300f || Mathf.Abs(z) > 9300f) blocked[j, i] = true;
                    if (TownR(x, z) < 0.97f) blocked[j, i] = true;   // (the town's own streets are unblocked below)
                    if (cover != null)
                    {
                        int cj = Mathf.Clamp(j * cover.GetLength(0) / Res, 0, cover.GetLength(0) - 1), ci = Mathf.Clamp(i * cover.GetLength(1) / Res, 0, cover.GetLength(1) - 1);
                        slow[j, i] = cover[cj, ci, 1] * 5f + cover[cj, ci, 2] * 3f;      // woods, rock
                    }
                    // (and some ground is simply better going than other: on a flat a road wanders a little, it is not ruled)
                    slow[j, i] += 1.0f * Mathf.PerlinNoise(x * 0.0031f + 40f, z * 0.0031f + 90f) + 0.5f * Mathf.PerlinNoise(x * 0.011f + 7f, z * 0.011f + 3f);
                }
            foreach (var rp in River())
            {
                int r = Mathf.CeilToInt((rp.half + 30f) / Cell);
                int ci = Idx(rp.p.x), cj = Idx(rp.p.y);
                for (int j = Mathf.Max(cj - r, 0); j <= Mathf.Min(cj + r, Res - 1); j++)
                    for (int i = Mathf.Max(ci - r, 0); i <= Mathf.Min(ci + r, Res - 1); i++)
                    {
                        float d = Vector2.Distance(V(X(i), X(j)), rp.p);
                        if (d < rp.half + 26f) blocked[j, i] = true;
                        if (d < rp.half + 3f) riverCore[j, i] = true;
                    }
            }
            for (int k = 0; k < rail.Count; k++) slow[Idx(rail[k].z), Idx(rail[k].x)] += 8f;
            foreach (var s in village.Concat(farms))
                for (int dj = -1; dj <= 1; dj++)
                    for (int di = -1; di <= 1; di++)
                    {
                        int j = Mathf.Clamp(Idx(s.pos.z) + dj, 0, Res - 1), i = Mathf.Clamp(Idx(s.pos.x) + di, 0, Res - 1);
                        if (Vector2.Distance(V(X(i), X(j)), V(s.pos.x, s.pos.z)) < 16f) blocked[j, i] = true;
                    }
            // the bridges: open across the river, and the height a road climbs to at each end
            var ramps = new List<(Vector2 end, float top)>();
            var bridgeEnds = new List<(Vector2 a, Vector2 b)>();
            foreach (var b in bridges)
            {
                Vector3 f3 = Quaternion.Euler(0, b.yaw, 0) * Vector3.forward;
                Vector2 f = V(f3.x, f3.z), c = V(b.at.x, b.at.z);
                float half = b.span / 2 + 4f, rise = Mathf.Min(9f, b.span * 0.28f), top = b.at.y + rise + 1.4f + 0.45f;
                Vector2 e0 = c - f * half, e1 = c + f * half;
                bridgeEnds.Add((e0, e1));
                ramps.Add((e0, top)); ramps.Add((e1, top));
                for (float d = -half - 45f; d <= half + 45f; d += Cell * 0.4f)
                    for (float side = -Cell * 0.5f; side <= Cell * 0.51f; side += Cell * 0.5f)
                    {
                        Vector2 p = c + f * d + V(f.y, -f.x) * side;
                        blocked[Idx(p.y), Idx(p.x)] = false;
                    }
            }

            void Mark(List<Vector2> pts)
            {
                for (int k = 0; k + 1 < pts.Count; k++)
                {
                    float len = Vector2.Distance(pts[k], pts[k + 1]);
                    int n = Mathf.Max(1, Mathf.CeilToInt(len / (Cell * 0.5f)));
                    for (int s = 0; s <= n; s++)
                    {
                        Vector2 p = Vector2.Lerp(pts[k], pts[k + 1], s / (float)n);
                        int j = Idx(p.y), i = Idx(p.x);
                        isRoad[j, i] = true; blocked[j, i] = false;
                    }
                }
            }
            void Add(List<Vector2> pts, float width, Color colour, float lift, bool strip = false)
            {
                if (pts == null || pts.Count < 2) return;
                lines.Add(new RoadLine { pts = pts, width = width, col = colour, lift = lift, strip = strip });
                Mark(pts);
            }

            // ---- the village: the main street over its bridge, and a path from every door
            if (village.Count > 0 && bridgeEnds.Count > 0)
            {
                var vb = bridgeEnds.OrderBy(e => Vector2.Distance((e.a + e.b) / 2, SquareAt)).First();
                Vector2 west = Vector2.Distance(vb.a, SquareAt) < Vector2.Distance(vb.b, SquareAt) ? vb.a : vb.b, east = west == vb.a ? vb.b : vb.a;
                Vector2 door = ChurchAt + V(0, 23f);
                Vector2 sqEdge = SquareAt + (west - SquareAt).normalized * 38f;
                Add(Curve(new List<Vector2> { sqEdge, Vector2.Lerp(sqEdge, west, 0.5f) + V(0, 7f), west }), 6.5f, RoadCobble, 0.5f);
                Add(new List<Vector2> { west, east }, 6.5f, RoadCobble, 0.5f);
                Add(Curve(new List<Vector2> { east, Vector2.Lerp(east, door, 0.55f) + V(0, -8f), door }), 6.5f, RoadCobble, 0.5f);
                discs.Add((door, 6f, RoadCobble));

                // the paths: each door makes for the nearest lane already there (weighing how far that still is from the
                // square or the church), so the lanes branch from house to house instead of fanning out from one point
                var laid = new List<Vector2>();
                foreach (var l in lines) laid.AddRange(Resample(l.pts, 4f));
                for (int k = 0; k < 28; k++) { float a = k * Mathf.PI * 2 / 28; laid.Add(SquareAt + V(Mathf.Cos(a), Mathf.Sin(a)) * 38f); }
                bool Wet(Vector2 p) => riverCore[Idx(p.y), Idx(p.x)] || blocked[Idx(p.y), Idx(p.x)] && !isRoad[Idx(p.y), Idx(p.x)] && InRiverBand(p);
                bool Dry(Vector2 a, Vector2 b)
                {
                    int n = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(a, b) / 4f));
                    for (int k = 1; k < n; k++)
                    {
                        Vector2 p = Vector2.Lerp(a, b, k / (float)n);
                        if (Wet(p)) return false;
                        if (Mathf.Abs(p.x - ChurchAt.x) < 10f && p.y > ChurchAt.y - 24f && p.y < ChurchAt.y + 18f) return false;   // not through the church
                        foreach (var o in village) if ((V(o.pos.x, o.pos.z) - p).sqrMagnitude < 49f * o.size * o.size) return false;   // nor a neighbour's house
                    }
                    return true;
                }
                float Hub(Vector2 q) => Mathf.Min(Mathf.Max(0, Vector2.Distance(q, SquareAt) - 38f), Vector2.Distance(q, door));
                int paths = 0;
                foreach (var s in village.OrderBy(o => Hub(V(o.pos.x, o.pos.z))))
                {
                    Vector2 at = V(s.pos.x, s.pos.z), fwd = V(Mathf.Sin(s.yaw * Mathf.Deg2Rad), Mathf.Cos(s.yaw * Mathf.Deg2Rad));
                    Vector2 start = at + fwd * 8.5f * s.size;
                    bool any = false; Vector2 goal = start;
                    foreach (var q in laid.OrderBy(c => Vector2.Distance(start, c) + 0.75f * Hub(c)).Take(400))
                        if (Dry(start, q) && Vector2.Distance(at, q) > 9f * s.size) { goal = q; any = true; break; }
                    if (!any) continue;
                    // not ruled: a lane bows a little on its way
                    Vector2 along = goal - start, side = V(along.y, -along.x);
                    float bow = (Mathf.PerlinNoise(at.x * 0.37f, at.y * 0.37f) - 0.5f) * 0.28f;
                    var path = Curve(new List<Vector2> { start, start + along * 0.35f + side * bow, start + along * 0.7f + side * bow * 0.6f, goal });
                    Add(path, 2.6f, RoadDirt, 0.42f);
                    discs.Add((start, 4.2f, RoadDirt));
                    laid.AddRange(Resample(path, 4f));
                    paths++;
                }
                Debug.Log($"OODA roads: {paths} of {village.Count} village doors have a path.");
            }

            // ---- the town: streets between the rows of houses
            {
                float x0 = TownCentre.x - TownRadii.x, x1 = TownCentre.x + TownRadii.x, z0 = TownCentre.y - TownRadii.y, z1 = TownCentre.y + TownRadii.y;
                // a street is laid only as far as it has houses to serve, and ends at a corner with another, never in a field;
                // every house has a short drive to the nearest street
                bool Open(Vector2 p) =>
                    TownR(p.x, p.y) < 0.95f
                    && !(Mathf.Abs(p.x - RunwayStart.x) < 125f && p.y > RunwayStart.y - 120f && p.y < RunwayEnd.y + 120f)
                    && !(Mathf.Abs(p.x - FactoryAt.x) < 102f && Mathf.Abs(p.y - FactoryAt.y) < 102f);
                void Street(Vector2 a, Vector2 b)
                {
                    // cut into the runs that are open ground
                    float len = Vector2.Distance(a, b);
                    if (len < 1f) return;
                    List<Vector2> run = null;
                    for (float d = 0; d <= len + 0.1f; d += 10f)
                    {
                        Vector2 p = Vector2.Lerp(a, b, Mathf.Clamp01(d / len));
                        if (Open(p)) (run ??= new List<Vector2>()).Add(p);
                        else { if (run != null && run.Count > 3) Add(run, 7f, RoadCobble, 0.46f); run = null; }
                    }
                    if (run != null && run.Count > 3) Add(run, 7f, RoadCobble, 0.46f);
                }
                var xs = new List<float>(); var zs = new List<float>();
                for (int k = -12; k <= 12; k++)
                {
                    float x = TownCentre.x + k * 136f;          // (the rows AlpineTown keeps clear: every fourth, 34 m apart)
                    if (x > x0 && x < x1) xs.Add(x);
                }
                for (int k = -14; k <= 14; k++)
                {
                    float z = Mathf.Round((TownCentre.y + k * 120f) / 30f) * 30f + 15f;   // between two rows, 30 m apart
                    if (z > z0 && z < z1 && !zs.Contains(z)) zs.Add(z);
                }
                // how far along each street its houses are (lo..hi), then each end carried on to the next corner
                var xLo = xs.Select(_ => float.MaxValue).ToArray(); var xHi = xs.Select(_ => float.MinValue).ToArray();
                var zLo = zs.Select(_ => float.MaxValue).ToArray(); var zHi = zs.Select(_ => float.MinValue).ToArray();
                int Nearest(List<float> list, float val) { int best = 0; for (int k = 1; k < list.Count; k++) if (Mathf.Abs(list[k] - val) < Mathf.Abs(list[best] - val)) best = k; return best; }
                var townHouses = TownHouseSpots(rail);
                var drives = new List<(Vector2 house, Vector2 street)>();
                bool Through(Vector2 a, Vector2 b)      // would a drive from a to b run through another house?
                {
                    foreach (var o in townHouses)
                    {
                        if ((o - a).sqrMagnitude < 1f) continue;
                        float t = Mathf.Clamp01(Vector2.Dot(o - a, b - a) / Mathf.Max((b - a).sqrMagnitude, 0.01f));
                        if ((a + (b - a) * t - o).sqrMagnitude < 11f * 11f) return true;
                    }
                    return false;
                }
                if (xs.Count > 0 && zs.Count > 0)
                    foreach (var hp in townHouses)
                    {
                        int xi = Nearest(xs, hp.x), zi = Nearest(zs, hp.y);
                        bool toZ = Mathf.Abs(zs[zi] - hp.y) <= Mathf.Abs(xs[xi] - hp.x) + 6f;
                        if (Through(hp, toZ ? V(hp.x, zs[zi]) : V(xs[xi], hp.y)) && !Through(hp, toZ ? V(xs[xi], hp.y) : V(hp.x, zs[zi]))) toZ = !toZ;
                        if (toZ)
                        {
                            zLo[zi] = Mathf.Min(zLo[zi], hp.x); zHi[zi] = Mathf.Max(zHi[zi], hp.x);
                            drives.Add((hp, V(hp.x, zs[zi])));
                        }
                        else
                        {
                            xLo[xi] = Mathf.Min(xLo[xi], hp.y); xHi[xi] = Mathf.Max(xHi[xi], hp.y);
                            drives.Add((hp, V(xs[xi], hp.y)));
                        }
                    }
                for (int pass = 0; pass < 6; pass++)
                {
                    for (int k = 0; k < xs.Count; k++)
                    {
                        if (xLo[k] > xHi[k]) continue;
                        // on to the corner beyond each end, and that cross street reaches this one
                        int lo = -1, hi = -1;
                        for (int q = 0; q < zs.Count; q++)
                        {
                            if (zs[q] <= xLo[k] + 1f && (lo < 0 || zs[q] > zs[lo])) lo = q;
                            if (zs[q] >= xHi[k] - 1f && (hi < 0 || zs[q] < zs[hi])) hi = q;
                        }
                        if (lo >= 0) { xLo[k] = zs[lo]; zLo[lo] = Mathf.Min(zLo[lo], xs[k]); zHi[lo] = Mathf.Max(zHi[lo], xs[k]); }
                        if (hi >= 0) { xHi[k] = zs[hi]; zLo[hi] = Mathf.Min(zLo[hi], xs[k]); zHi[hi] = Mathf.Max(zHi[hi], xs[k]); }
                    }
                    for (int k = 0; k < zs.Count; k++)
                    {
                        if (zLo[k] > zHi[k]) continue;
                        int lo = -1, hi = -1;
                        for (int q = 0; q < xs.Count; q++)
                        {
                            if (xs[q] <= zLo[k] + 1f && (lo < 0 || xs[q] > xs[lo])) lo = q;
                            if (xs[q] >= zHi[k] - 1f && (hi < 0 || xs[q] < xs[hi])) hi = q;
                        }
                        if (lo >= 0) { zLo[k] = xs[lo]; xLo[lo] = Mathf.Min(xLo[lo], zs[k]); xHi[lo] = Mathf.Max(xHi[lo], zs[k]); }
                        if (hi >= 0) { zHi[k] = xs[hi]; xLo[hi] = Mathf.Min(xLo[hi], zs[k]); xHi[hi] = Mathf.Max(xHi[hi], zs[k]); }
                    }
                }
                for (int k = 0; k < xs.Count; k++) if (xLo[k] < xHi[k]) Street(V(xs[k], xLo[k]), V(xs[k], xHi[k]));
                for (int k = 0; k < zs.Count; k++) if (zLo[k] < zHi[k]) Street(V(zLo[k], zs[k]), V(zHi[k], zs[k]));
                foreach (var d in drives)
                {
                    Vector2 dir = d.street - d.house;
                    if (dir.magnitude < 12f || !Open(d.street) || Through(d.house, d.street)) continue;
                    Add(new List<Vector2> { d.house + dir.normalized * 7.5f, d.street }, 2.8f, RoadGravel, 0.44f);
                }
                Debug.Log($"OODA roads: the town has {townHouses.Count} houses on {xs.Count + zs.Count} streets.");
                // round the factory's yard
                Vector2 fa = FactoryAt;
                Add(new List<Vector2> { fa + V(-106, -106), fa + V(106, -106), fa + V(106, 106), fa + V(-106, 106), fa + V(-106, -106) }, 7f, RoadCobble, 0.46f);
                // the airfield: a road up the runway's east side, round its south end to the hangars, and one to the factory
                Vector2 rs = RunwayStart, re = RunwayEnd;
                Add(Curve(new List<Vector2> { re + V(52, 30), rs + V(52, -20), rs + V(40, -62), rs + V(-50, -62), rs + V(-70, -20), rs + V(-70, 300) }), 6f, RoadGravel, 0.46f);
                Add(new List<Vector2> { V(rs.x + 52, fa.y - 106), fa + V(-106, -106) }, 7f, RoadCobble, 0.46f);
            }

            // ---- the country: the road to the town, the lanes to the bridges, the farm tracks
            var search = new RoadSearch(h, blocked, slow, isRoad);
            if (village.Count > 0)
            {
                // from the town's streets out to the village's (the search stops at the first road it meets that is not its own)
                var townCells = new HashSet<int>();
                for (int j = 0; j < Res; j++) for (int i = 0; i < Res; i++) if (isRoad[j, i] && TownR(X(i), X(j)) < 1.0f) townCells.Add(j * Res + i);
                var path = search.Find(SquareAt + V(-42f, 0), 400000f, c => townCells.Contains(c));
                if (path != null) Add(Smooth(path), 6f, RoadGravel, 0.48f);
                else Debug.LogWarning("OODA roads: no way found from the village to the town.");
            }
            int lanes = 0;
            foreach (var e in bridgeEnds)
            {
                // (each end finds its own way on its own bank, or the second end's nearest road would be the first end's,
                // three steps away over the deck)
                foreach (var end in new[] { e.a, e.b })
                {
                    if (lines.Any(l => l.pts.Any(q => Vector2.Distance(q, end) < 12f))) continue;
                    Vector2 centre = (e.a + e.b) / 2, bank = (end - centre).normalized;
                    var path = search.Find(end, 90000f, null, c => Vector2.Dot(V(X(c % Res), X(c / Res)) - centre, bank) > 0f);
                    if (path != null) { Add(Smooth(path), 4.2f, RoadGravel, 0.45f); lanes++; }
                    else Debug.LogWarning($"OODA roads: no lane found from the bridge end at ({end.x:F0}, {end.y:F0}).");
                }
            }
            Debug.Log($"OODA roads: {lanes} lanes to the bridges.");
            int tracks = 0;
            foreach (var s in farms.OrderBy(o => Mathf.Min(Vector2.Distance(V(o.pos.x, o.pos.z), SquareAt), Vector2.Distance(V(o.pos.x, o.pos.z), TownCentre))))
            {
                Vector2 at = V(s.pos.x, s.pos.z), fwd = V(Mathf.Sin(s.yaw * Mathf.Deg2Rad), Mathf.Cos(s.yaw * Mathf.Deg2Rad));
                Vector2 start = at + fwd * 12f * s.size;
                var path = search.Find(start, 60000f, null);
                if (path == null) continue;
                Add(Smooth(path), 3.4f, RoadDirt, 0.42f, true);
                discs.Add((start, 6.5f, RoadDirt));
                tracks++;
            }

            // ---- the ribbons
            var v = new List<Vector3>(); var cols = new List<Color>(); var tris = new List<int>();
            float Ground(Vector2 p, float lift)
            {
                float y = SampleArr(h, p.x, p.y) + lift;
                foreach (var r in ramps) y = Mathf.Max(y, r.top - 0.09f * Vector2.Distance(p, r.end));   // up onto a bridge's deck
                return y;
            }
            void Quad(int a, int b, int c, int d)
            {
                // clockwise seen from above
                if (Vector3.Cross(v[d] - v[a], v[b] - v[a]).y > 0) tris.AddRange(new[] { a, c, b, a, d, c });
                else tris.AddRange(new[] { a, b, c, a, c, d });
            }
            float km = 0;
            foreach (var l in lines)
            {
                var pts = Resample(l.pts, 6f);
                Color mid = l.col, edge = Color.Lerp(l.col, RoadVerge, 0.6f), grass = Color.Lerp(l.col, RoadVerge, 0.75f);
                mid.a = 0; edge.a = 0; grass.a = 0;
                // across: the verge, the way, the verge; a farm track has grass up its middle between the wheel ruts
                float[] us = l.strip ? new[] { -1f, -0.74f, -0.2f, 0.2f, 0.74f, 1f } : new[] { -1f, -0.72f, 0.72f, 1f };
                int prev = -1;
                for (int k = 0; k < pts.Count; k++)
                {
                    Vector2 p = pts[k];
                    Vector2 dir = (pts[Mathf.Min(k + 1, pts.Count - 1)] - pts[Mathf.Max(k - 1, 0)]).normalized, across = V(dir.y, -dir.x);
                    // a road drawn by hand is never one width
                    float w = l.width * 0.5f * (0.9f + 0.22f * Mathf.PerlinNoise(p.x * 0.021f + 3.1f, p.y * 0.021f + 7.7f));
                    if (riverCore[Idx(p.y), Idx(p.x)]) { prev = -1; continue; }
                    int at = v.Count;
                    foreach (float u in us)
                    {
                        Vector2 q = p + across * w * u;
                        v.Add(new Vector3(q.x, Ground(q, l.lift), q.y));
                        cols.Add(Mathf.Abs(u) > 0.9f ? edge : Mathf.Abs(u) < 0.3f ? grass : mid);
                    }
                    if (prev >= 0)
                    {
                        for (int s = 0; s + 1 < us.Length; s++) Quad(prev + s, prev + s + 1, at + s + 1, at + s);
                        km += Vector2.Distance(pts[k - 1], p) / 1000f;
                    }
                    prev = at;
                }
            }
            foreach (var d in discs)
            {
                int c = v.Count;
                Color cc = d.col, ce = Color.Lerp(d.col, RoadVerge, 0.6f); cc.a = 0; ce.a = 0;
                v.Add(new Vector3(d.at.x, Ground(d.at, 0.44f), d.at.y)); cols.Add(cc);
                const int n = 10;
                for (int k = 0; k < n; k++)
                {
                    float a = k * Mathf.PI * 2 / n, r = d.r * (0.85f + 0.3f * Mathf.PerlinNoise(d.at.x * 0.3f + k * 1.7f, d.at.y * 0.3f));
                    Vector2 q = d.at + V(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    v.Add(new Vector3(q.x, Ground(q, 0.44f), q.y)); cols.Add(ce);
                }
                for (int k = 0; k < n; k++)
                {
                    int a = c + 1 + k, b = c + 1 + (k + 1) % n;
                    if (Vector3.Cross(v[b] - v[c], v[a] - v[c]).y > 0) tris.AddRange(new[] { c, b, a }); else tris.AddRange(new[] { c, a, b });
                }
            }
            if (v.Count == 0) { Debug.LogWarning("OODA roads: nothing to lay (no village, farms or town found in the scene)."); return null; }

            AssetDatabase.DeleteAsset(RoadMeshPath);
            var mesh = new Mesh { name = "Roads", indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(v);
            mesh.SetNormals(Enumerable.Repeat(Vector3.up, v.Count).ToList());
            mesh.SetColors(cols);
            mesh.SetUVs(0, v.Select(p => new Vector2(p.x, p.z)).ToList());
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, RoadMeshPath);
            Debug.Log($"OODA roads: {km:F1} km in {lines.Count} stretches ({village.Count} village houses, {tracks} of {farms.Count} farm tracks, {bridges.Count} bridges), {v.Count} vertices.");
            return mesh;
        }

        /// <summary>Where the town's houses stand. They are one mesh in the scene, so this draws the same numbers in the
        /// same order as AlpineTown.MakeTown does (keep the two in step).</summary>
        static List<Vector2> TownHouseSpots(List<Vector3> rail)
        {
            var rnd = new System.Random(11);
            var placed = new List<Vector2>();
            for (int tries = 0; tries < 8000 && placed.Count < 170; tries++)
            {
                var p = V(TownCentre.x - 250 + (float)rnd.NextDouble() * 1150, TownCentre.y - 900 + (float)rnd.NextDouble() * 1600);
                p = V(Mathf.Round(p.x / 34f) * 34f + Rand(rnd, -4, 4), Mathf.Round(p.y / 30f) * 30f + Rand(rnd, -4, 4));
                if (TownR(p.x, p.y) > 0.92f) continue;
                if (Vector2.Distance(p, FactoryAt) < 150 || Mathf.Abs(p.x - RunwayStart.x) < 150 || NearRail(rail, p.x, p.y, 30f)) continue;
                if (placed.Any(q => Vector2.Distance(p, q) < 24)) continue;
                if (Mathf.Abs(Mathf.Round((p.x - TownCentre.x) / 34f)) % 4 == 0) continue;
                placed.Add(p);
                rnd.NextDouble(); rnd.NextDouble(); rnd.NextDouble();      // its width, depth and height
                rnd.Next(2); Rand(rnd, -4, 4);                              // which way it faces
                rnd.NextDouble();                                           // plaster or wood
            }
            return placed;
        }

        /// <summary>Near the river's line at all (so a blocked square by a house is not taken for water).</summary>
        static bool InRiverBand(Vector2 p)
        {
            foreach (var rp in River()) if ((rp.p - p).sqrMagnitude < (rp.half + 30f) * (rp.half + 30f)) return true;
            return false;
        }

        /// <summary>The houses under a root in the open scene: where each stands, which way it faces, its size.</summary>
        static List<HouseSpot> SceneHouses(string root, string prefix)
        {
            var list = new List<HouseSpot>();
            var go = GameObject.Find(root);
            if (go == null) return list;
            foreach (Transform t in go.transform)
                if (t.name.StartsWith(prefix))
                    list.Add(new HouseSpot { pos = t.position, yaw = t.eulerAngles.y, size = t.localScale.x });
            return list;
        }

        // ---------- lines ----------

        static List<Vector2> Resample(List<Vector2> pts, float step)
        {
            var o = new List<Vector2> { pts[0] };
            for (int k = 0; k + 1 < pts.Count; k++)
            {
                float len = Vector2.Distance(pts[k], pts[k + 1]);
                int n = Mathf.Max(1, Mathf.CeilToInt(len / step));
                for (int s = 1; s <= n; s++) o.Add(Vector2.Lerp(pts[k], pts[k + 1], s / (float)n));
            }
            return o;
        }

        static List<Vector2> Thin(List<Vector2> pts, int every)
        {
            var o = new List<Vector2>();
            for (int k = 0; k < pts.Count; k += every) o.Add(pts[k]);
            if (o[o.Count - 1] != pts[pts.Count - 1]) o.Add(pts[pts.Count - 1]);
            return o;
        }

        /// <summary>Corners cut twice (Chaikin), the ends kept.</summary>
        static List<Vector2> Curve(List<Vector2> pts)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                var o = new List<Vector2> { pts[0] };
                for (int k = 0; k + 1 < pts.Count; k++)
                {
                    o.Add(Vector2.Lerp(pts[k], pts[k + 1], 0.25f));
                    o.Add(Vector2.Lerp(pts[k], pts[k + 1], 0.75f));
                }
                o.Add(pts[pts.Count - 1]);
                pts = o;
            }
            return pts;
        }

        /// <summary>A way found over the grid, as a lane: every third square kept, then the corners cut.</summary>
        static List<Vector2> Smooth(List<Vector2> cells) => cells.Count < 4 ? cells : Curve(Thin(cells, 3));

        // ---------- finding a way ----------

        /// <summary>The cheapest way over the heightmap's grid from a point to the nearest road (or to a goal): distance,
        /// made dearer by the grade (none steeper than 1 in 4), by woods and rock, and by a railway to cross.</summary>
        sealed class RoadSearch
        {
            readonly float[,] h; readonly bool[,] blocked; readonly float[,] slow; readonly bool[,] isRoad;
            readonly float[] cost = new float[Res * Res];
            readonly int[] from = new int[Res * Res];
            readonly List<int> touched = new List<int>();

            public RoadSearch(float[,] h, bool[,] blocked, float[,] slow, bool[,] isRoad)
            {
                this.h = h; this.blocked = blocked; this.slow = slow; this.isRoad = isRoad;
                for (int k = 0; k < cost.Length; k++) cost[k] = float.MaxValue;
            }

            /// <summary>From 'start' until a road is reached (or a square 'goal' accepts), spending no more than 'budget'
            /// metres of cost. The way runs from the start to the road; null if there is none.</summary>
            public List<Vector2> Find(Vector2 start, float budget, System.Func<int, bool> goal, System.Func<int, bool> allow = null)
            {
                int sj = Idx(start.y), si = Idx(start.x), s = sj * Res + si;
                foreach (int t in touched) cost[t] = float.MaxValue;
                touched.Clear();
                var open = new SortedSet<(float c, int id)>();
                cost[s] = 0; from[s] = -1; touched.Add(s);
                open.Add((0, s));
                int found = -1;
                // (roads within a few squares of the start are its own village's or its own forecourt: only a goal counts there)
                while (open.Count > 0)
                {
                    var top = open.Min; open.Remove(top);
                    int id = top.id, j = id / Res, i = id % Res;
                    if (top.c > cost[id]) continue;
                    if (id != s && (goal != null ? goal(id) : isRoad[j, i])) { found = id; break; }
                    if (top.c > budget) break;
                    for (int dj = -1; dj <= 1; dj++)
                        for (int di = -1; di <= 1; di++)
                        {
                            if (dj == 0 && di == 0) continue;
                            int nj = j + dj, ni = i + di;
                            if (nj < 1 || ni < 1 || nj >= Res - 1 || ni >= Res - 1) continue;
                            bool target = goal != null ? goal(nj * Res + ni) : isRoad[nj, ni];
                            bool atStart = Mathf.Abs(nj - sj) <= 3 && Mathf.Abs(ni - si) <= 3;   // (a door's or a bridge's own bank: the ribbon ramps there)
                            if (blocked[nj, ni] && !target) continue;
                            if (allow != null && !allow(nj * Res + ni)) continue;
                            float d = Cell * (dj != 0 && di != 0 ? 1.4142f : 1f);
                            float grade = Mathf.Abs(h[nj, ni] - h[j, i]) / d;
                            if (grade > 0.25f && !atStart) continue;
                            float g = grade / 0.07f;
                            float c = top.c + d * (1f + g * g + slow[nj, ni]);
                            int nid = nj * Res + ni;
                            if (c >= cost[nid]) continue;
                            if (cost[nid] == float.MaxValue) touched.Add(nid);
                            cost[nid] = c; from[nid] = id;
                            open.Add((c, nid));
                        }
                }
                if (found < 0) return null;
                var path = new List<Vector2>();
                for (int id = found; id >= 0; id = from[id]) path.Add(V(X(id % Res), X(id / Res)));
                path.Reverse();
                path[0] = start;
                return path;
            }
        }
    }
}
