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
    /// - the town: laid out on a plan (TownPlan, below): a high street from the airfield road to the square under the
    ///   church and on past the factory's gate, side streets off it, and the houses in rows along them, facing them;
    ///   a road round the airfield to the hangars;
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
            Mesh mesh = null, houses = null;
            float[,] h = null;
            foreach (var path in RoadScenes)
            {
                if (!System.IO.File.Exists(path)) continue;
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var terrain = Object.FindFirstObjectByType<Terrain>();
                if (terrain == null) { Debug.LogWarning($"OODA roads: no terrain in {path}; skipped."); continue; }
                var data = terrain.terrainData;
                if (h == null)
                {
                    var norm = data.GetHeights(0, 0, Res, Res);
                    h = new float[Res, Res];
                    for (int j = 0; j < Res; j++)
                        for (int i = 0; i < Res; i++) h[j, i] = norm[j, i] * Height + Base;
                }
                ReplaceTownHouses(h, ref houses);
                if (mesh == null) mesh = RoadMesh(h, RailPath(), data);
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
                    if (y < 0.6f || y > 1500f || InLake(x, z) || Mathf.Abs(x) > 9300f || Mathf.Abs(z) > 9300f) blocked[j, i] = true;
                    if ((Mathf.Abs(x - RunwayStart.x) < 60f && z > RunwayStart.y - 40f && z < RunwayEnd.y + 40f)
                        || (Mathf.Abs(x - FactoryAt.x) < 110f && Mathf.Abs(z - FactoryAt.y) < 110f)) blocked[j, i] = true;
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
            for (int j = 0; j < Res; j++)
                for (int i = 0; i < Res; i++)
                    if (Mathf.Abs(X(i) - ChurchAt.x) < 17f && X(j) > ChurchAt.y - 32f && X(j) < ChurchAt.y + 20f) blocked[j, i] = true;   // the village church
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
                for (float d = -half - 90f; d <= half + 90f; d += Cell * 0.4f)
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
                Add(Curve(new List<Vector2> { east, Vector2.Lerp(east, door, 0.6f) + V(0, 14f), door + V(-10f, 6f), door }), 6.5f, RoadCobble, 0.5f);
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

            // ---- the town: the streets of its plan, its square, the airfield's road
            {
                var plan = TownPlan();
                foreach (var st in plan.streets) Add(st.pts, st.width, RoadCobble, 0.46f);
                discs.Add((TownSquareAt, 31f, RoadCobble));
                foreach (var hs in plan.houses) blocked[Idx(hs.at.y), Idx(hs.at.x)] = true;
                foreach (var st in plan.streets) Mark(st.pts);
                // a road up the runway's east side and round its south end to the hangars
                Vector2 rs = RunwayStart, re = RunwayEnd;
                Add(Curve(new List<Vector2> { re + V(52, 30), rs + V(52, -20), rs + V(40, -62), rs + V(-50, -62), rs + V(-70, -20), rs + V(-70, 300) }), 6f, RoadGravel, 0.46f);
            }

            // a way found ends at the middle of a grid square the road runs through: carry it on to the road itself
            List<Vector2> Join(List<Vector2> path)
            {
                Vector2 end = path[path.Count - 1], best = end; float bd = 45f * 45f;
                foreach (var l in lines)
                    for (int k = 0; k + 1 < l.pts.Count; k++)
                    {
                        Vector2 a = l.pts[k], b = l.pts[k + 1];
                        float t = Mathf.Clamp01(Vector2.Dot(end - a, b - a) / Mathf.Max((b - a).sqrMagnitude, 0.01f));
                        Vector2 q = a + (b - a) * t;
                        if ((q - end).sqrMagnitude < bd) { bd = (q - end).sqrMagnitude; best = q; }
                    }
                path[path.Count - 1] = best;
                return path;
            }

            // ---- the country: the road to the town, the lanes to the bridges, the farm tracks
            var search = new RoadSearch(h, blocked, slow, isRoad);
            if (village.Count > 0)
            {
                // from the town's streets out to the village's (the search stops at the first road it meets that is not its own)
                var townCells = new HashSet<int>();
                for (int j = 0; j < Res; j++) for (int i = 0; i < Res; i++) if (isRoad[j, i] && TownR(X(i), X(j)) < 1.0f) townCells.Add(j * Res + i);
                var path = search.Find(SquareAt + V(-42f, 0), 400000f, c => townCells.Contains(c));
                if (path != null) Add(Join(Smooth(path)), 6f, RoadGravel, 0.48f);
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
                    if (path != null) { Add(Join(Smooth(path)), 4.2f, RoadGravel, 0.45f); lanes++; }
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
                Add(Join(Smooth(path)), 3.4f, RoadDirt, 0.42f, true);
                discs.Add((start, 6.5f, RoadDirt));
                tracks++;
            }

            if (data != null) GroundUnderRoads(data, lines);

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
                // the middle, a ring of the same colour most of the way out, and the darker rim
                int c = v.Count;
                Color cc = d.col, ce = Color.Lerp(d.col, RoadVerge, 0.6f); cc.a = 0; ce.a = 0;
                v.Add(new Vector3(d.at.x, Ground(d.at, 0.44f), d.at.y)); cols.Add(cc);
                const int n = 14;
                for (int ring = 0; ring < 2; ring++)
                    for (int k = 0; k < n; k++)
                    {
                        float a = k * Mathf.PI * 2 / n, r = d.r * (0.88f + 0.24f * Mathf.PerlinNoise(d.at.x * 0.3f + k * 1.7f, d.at.y * 0.3f)) * (ring == 0 ? 0.86f : 1f);
                        Vector2 q = d.at + V(Mathf.Cos(a), Mathf.Sin(a)) * r;
                        v.Add(new Vector3(q.x, Ground(q, 0.44f), q.y)); cols.Add(ring == 0 ? cc : ce);
                    }
                for (int k = 0; k < n; k++)
                {
                    int a = c + 1 + k, b = c + 1 + (k + 1) % n;
                    if (Vector3.Cross(v[b] - v[c], v[a] - v[c]).y > 0) tris.AddRange(new[] { c, b, a }); else tris.AddRange(new[] { c, a, b });
                    Quad(a, b, b + n, a + n);
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

        // ---------- the town's plan ----------

        struct TownHouse { public Vector2 at; public float yaw, w, d, tall; public bool wood; }
        sealed class TownStreet { public List<Vector2> pts; public float width; }
        sealed class TownLayout { public List<TownStreet> streets = new List<TownStreet>(); public List<TownHouse> houses = new List<TownHouse>(); }

        /// <summary>The town's church, at the head of its square.</summary>
        static readonly Vector2 TownChurchAt = V(TownCentre.x + 120, TownCentre.y + 420);
        static readonly Vector2 TownSquareAt = TownChurchAt + V(-15, -56);
        static TownLayout townPlan;

        /// <summary>The town as someone would have built it: a high street from the airfield road to the square under the
        /// church, on past the factory's gate and out to the south; side streets off it; and the houses in rows along
        /// them, close together near the square and thinning toward the fields, each facing its street. (Always the same
        /// town: the numbers are drawn from one seed.)</summary>
        static TownLayout TownPlan()
        {
            if (townPlan != null) return townPlan;
            var plan = new TownLayout();
            Vector2 sq = TownSquareAt, fa = FactoryAt;
            void Street(float width, params Vector2[] p) => plan.streets.Add(new TownStreet { pts = Resample(Curve(p.ToList()), 6f), width = width });
            Vector2 west = V(RunwayStart.x + 52, sq.y - 125), gate = V(fa.x - 135, fa.y), south = V(fa.x - 110, fa.y - 250);
            Vector2 inn = V(sq.x - 175, sq.y + 20), bend = V(sq.x + 115, sq.y - 105);
            // the high street
            Street(7.5f, west, V(sq.x - 500, sq.y - 60), inn, sq, bend, gate, south, V(fa.x, fa.y - 470), V(fa.x + 160, fa.y - 690));
            Street(6f, gate, V(fa.x - 96, fa.y));                                                         // the factory's gate
            // the side streets
            Street(5.5f, sq, V(sq.x - 25, sq.y + 190), V(sq.x + 15, sq.y + 360));                         // north, past the church
            Street(5.5f, bend, V(sq.x + 350, sq.y - 70), V(sq.x + 580, sq.y - 130), V(sq.x + 700, sq.y - 270));   // east, north of the factory
            Street(5.5f, inn, V(inn.x - 30, inn.y - 190), V(inn.x + 30, inn.y - 390), V(inn.x + 10, inn.y - 590));   // south from the inn
            Street(5.5f, V(inn.x + 10, inn.y - 590), V(inn.x + 180, inn.y - 610), south);                 // a back lane round to the high street
            Street(5.5f, V(fa.x, fa.y - 470), V(fa.x + 250, fa.y - 430), V(fa.x + 450, fa.y - 300));      // east, south of the factory
            Street(5.5f, V(sq.x - 500, sq.y - 60), V(sq.x - 460, sq.y + 130), V(sq.x - 320, sq.y + 280)); // north from the west end
            Street(5f, V(inn.x - 30, inn.y - 190), V(inn.x - 280, inn.y - 240), V(inn.x - 430, inn.y - 390));   // west toward the airfield

            var rnd = new System.Random(11);
            var rail = RailPath();
            var centre = new List<Vector2>();       // every street's line, to keep houses off the carriageways
            foreach (var st in plan.streets) centre.AddRange(st.pts);
            // round the square: tall houses shoulder to shoulder, facing it, leaving the streets' mouths and the church's front
            for (int k = 0; k < 14; k++)
            {
                float a = (k + 0.5f) * Mathf.PI * 2 / 14;
                Vector2 dir = V(Mathf.Sin(a), Mathf.Cos(a)), at = sq + dir * 45f;
                if (centre.Any(c => (c - at).sqrMagnitude < 11f * 11f)) continue;
                if (Mathf.Abs(at.x - TownChurchAt.x) < 19f && at.y > TownChurchAt.y - 40f) continue;
                plan.houses.Add(new TownHouse { at = at, yaw = a * Mathf.Rad2Deg + 90f, w = 12.5f + Rand(rnd, 0, 2), d = 9.5f, tall = 9f, wood = rnd.NextDouble() > 0.75 });
            }
            foreach (var st in plan.streets)
            {
                float carry = 0;
                foreach (float side in new[] { -1f, 1f })
                    for (int k = 1; k + 1 < st.pts.Count; k++)
                    {
                        carry += Vector2.Distance(st.pts[k - 1], st.pts[k]);
                        if (carry < 19f) continue;
                        carry = Rand(rnd, -3f, 2f);
                        Vector2 dir = (st.pts[k + 1] - st.pts[k - 1]).normalized, across = V(dir.y, -dir.x);
                        float w = 9 + (float)rnd.NextDouble() * 5, d = 8 + (float)rnd.NextDouble() * 4, tall = rnd.NextDouble() < 0.3 ? 9f : 6f;
                        bool gable = rnd.NextDouble() < 0.35;                         // gable or eaves to the street
                        bool wood = rnd.NextDouble() > 0.7;
                        double keep = rnd.NextDouble();
                        float back = st.width / 2 + 3.5f + (gable ? w : d) / 2 + Rand(rnd, 0f, 2.5f);
                        Vector2 at = st.pts[k] + across * side * back;
                        // close-built near the square, with gaps and gardens toward the fields
                        if (keep > Mathf.Lerp(0.97f, 0.45f, Mathf.Clamp01((Vector2.Distance(at, sq) - 150f) / 900f))) continue;
                        if (TownR(at.x, at.y) > 0.92f || NearRail(rail, at.x, at.y, 30f)) continue;
                        if (Mathf.Abs(at.x - fa.x) < 112f && Mathf.Abs(at.y - fa.y) < 112f) continue;
                        if (Mathf.Abs(at.x - RunwayStart.x) < 150f) continue;
                        if (Vector2.Distance(at, sq) < 36f || (Mathf.Abs(at.x - TownChurchAt.x) < 17f && Mathf.Abs(at.y - TownChurchAt.y + 5f) < 30f)) continue;
                        if (plan.houses.Any(o => Vector2.Distance(o.at, at) < 15.5f)) continue;
                        float clear = Mathf.Max(w, d) * 0.5f + 3.2f;
                        if (centre.Any(c => (c - at).sqrMagnitude < clear * clear)) continue;
                        float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg + (gable ? 0f : 90f) + Rand(rnd, -3f, 3f);
                        plan.houses.Add(new TownHouse { at = at, yaw = yaw, w = w, d = d, tall = tall, wood = wood });
                    }
            }
            return townPlan = plan;
        }

        /// <summary>The ground made ready for the roads: the town's own ground is meadow, not the crops the fields round it
        /// are sown with (it stood in the wheat), and no tree is left standing in a road or a town house.</summary>
        static void GroundUnderRoads(TerrainData data, List<RoadLine> lines)
        {
            var plan = TownPlan();
            int aw = data.alphamapWidth, ah = data.alphamapHeight;
            if (data.alphamapLayers >= 8)
            {
                float texel = Size / aw;
                var near = new float[ah, aw];
                for (int j = 0; j < ah; j++) for (int i = 0; i < aw; i++) near[j, i] = 1e9f;
                void Stamp(Vector2 p)
                {
                    int ci = Mathf.FloorToInt((p.x + Size / 2) / texel), cj = Mathf.FloorToInt((p.y + Size / 2) / texel), r = Mathf.CeilToInt(90f / texel);
                    for (int j = Mathf.Max(cj - r, 0); j <= Mathf.Min(cj + r, ah - 1); j++)
                        for (int i = Mathf.Max(ci - r, 0); i <= Mathf.Min(ci + r, aw - 1); i++)
                            near[j, i] = Mathf.Min(near[j, i], Vector2.Distance(p, V((i + 0.5f) * texel - Size / 2, (j + 0.5f) * texel - Size / 2)));
                }
                foreach (var st in plan.streets) for (int k = 0; k < st.pts.Count; k += 2) Stamp(st.pts[k]);
                foreach (var hs in plan.houses) Stamp(hs.at);
                Stamp(TownSquareAt); Stamp(TownChurchAt);
                var a = data.GetAlphamaps(0, 0, aw, ah);
                for (int j = 0; j < ah; j++)
                    for (int i = 0; i < aw; i++)
                    {
                        float t = 1f - Mathf.SmoothStep(42f, 78f, near[j, i]);
                        if (t <= 0) continue;
                        float crops = a[j, i, 4] + a[j, i, 5] + a[j, i, 6] + a[j, i, 7];
                        a[j, i, 0] += crops * t;
                        for (int l = 4; l < 8; l++) a[j, i, l] *= 1f - t;
                    }
                data.SetAlphamaps(0, 0, a);
            }

            // the trees
            var taken = new HashSet<(int, int)>();
            void Take(Vector2 p, float r)
            {
                int n = Mathf.CeilToInt(r / 3f), ci = Mathf.FloorToInt(p.x / 3f), cj = Mathf.FloorToInt(p.y / 3f);
                for (int dj = -n; dj <= n; dj++) for (int di = -n; di <= n; di++) if ((di * di + dj * dj) * 9f <= (r + 1.5f) * (r + 1.5f)) taken.Add((ci + di, cj + dj));
            }
            foreach (var l in lines) foreach (var p in Resample(l.pts, 3f)) Take(p, l.width / 2 + 3.5f);
            foreach (var hs in plan.houses) Take(hs.at, 11f);
            Take(TownSquareAt, 34f);
            // (the trees are a file of seven numbers each: x, y, z, yaw, scale, height, kind; see Plant and Fx/Forest)
            string forest = $"{Dir}/Forest.bytes";
            int felled = 0, stood = 0;
            if (System.IO.File.Exists(forest))
            {
                var bytes = System.IO.File.ReadAllBytes(forest);
                var keep = new System.IO.MemoryStream(bytes.Length);
                for (int o = 0; o + 28 <= bytes.Length; o += 28)
                {
                    float x = System.BitConverter.ToSingle(bytes, o), z = System.BitConverter.ToSingle(bytes, o + 8);
                    stood++;
                    if (taken.Contains((Mathf.FloorToInt(x / 3f), Mathf.FloorToInt(z / 3f)))) { felled++; continue; }
                    keep.Write(bytes, o, 28);
                }
                if (felled > 0)
                {
                    System.IO.File.WriteAllBytes(forest, keep.ToArray());
                    AssetDatabase.ImportAsset(forest, ImportAssetOptions.ForceUpdate);
                }
            }
            EditorUtility.SetDirty(data);
            Debug.Log($"OODA roads: the town's ground cleared of crops, and {felled} of {stood} trees felled where a road or a house is.");
        }

        /// <summary>The town's houses moved onto its plan, in a scene built before there was one.</summary>
        static void ReplaceTownHouses(float[,] h, ref Mesh mesh)
        {
            var town = GameObject.Find("Town");
            var old = town != null ? town.transform.Find("Houses") : null;
            if (old == null) return;
            var mats = old.GetComponent<MeshRenderer>().sharedMaterials;
            Object.DestroyImmediate(old.gameObject);
            if (mesh == null) mesh = TownHousesMesh(h);
            MeshObject("Houses", mesh, mats, town.transform, true);
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
        /// made dearer by the grade (few steeper than 1 in 4, none than 1 in 2), by woods and rock, and by a railway to cross.</summary>
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
                            if (grade > 0.45f && !atStart) continue;
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
