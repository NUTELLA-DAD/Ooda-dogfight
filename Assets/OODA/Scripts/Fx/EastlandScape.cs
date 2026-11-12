using System.Collections.Generic;
using Ooda.Turn.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Ooda.Fx
{
    /// <summary>
    /// Draws the east lands (Turn/Core/Eastland.cs) from the same grid and the same stone the turn engine flies over, so
    /// what you see is what you hit: the green plain past the Alps, the desert's dunes and plateaus, the river's valley of
    /// fields, the oasis lake's and the coast's shallows, and the pyramids and the obelisks. Made at run time (no scene or
    /// asset to merge), a fraction of a second: the land is cut into squares of ChunkM metres, each one mesh in one material
    /// (the toon land's, Fx/ToonLandModel.shader, painted per vertex), with a LOD group of four: the full 25 m grid near, then
    /// 50, 100 and 200 m, the obelisks only in the near two, the pyramids in all; skirts under every edge hide the cracks
    /// where two squares (or a square and the Alps' terrain) are drawn at different steps. Squares wholly under deep sea
    /// are left out. A mesh collider on each (its 50 m mesh) lets a wreck or a parachute land on it and free flight crash
    /// into it. Every scene that has the Alps gets the east lands and the city, as it loads.
    /// </summary>
    public class EastlandScape : MonoBehaviour
    {
        public const float ChunkM = 2000f;
        /// <summary>Where each step of a square's LOD group gives way to the next, as a share of the screen's height.</summary>
        static readonly float[] LodHeights = { 0.45f, 0.2f, 0.09f, 0.0005f };
        const float SkirtM = 30f;

        static EastlandScape made;
        Material mat;
        readonly List<Mesh> meshes = new List<Mesh>();

        /// <summary>The east lands in this scene, made if they aren't there yet.</summary>
        public static EastlandScape Ensure()
        {
            if (made != null) return made;
            made = FindFirstObjectByType<EastlandScape>();
            if (made != null) return made;
            var go = new GameObject("East Lands");
            made = go.AddComponent<EastlandScape>();
            made.Build(Eastland.Land);
            return made;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            SceneManager.sceneLoaded -= Loaded;
            SceneManager.sceneLoaded += Loaded;
            Loaded(default, LoadSceneMode.Single);
        }

        /// <summary>A scene with the Alps in it gets the land past their east edge, and the city on it.</summary>
        static void Loaded(Scene s, LoadSceneMode mode)
        {
            foreach (var t in Terrain.activeTerrains)
            {
                var d = t != null ? t.terrainData : null;
                if (d == null || Mathf.Abs(t.transform.position.x + d.size.x - Eastland.X0) > 1f) continue;
                Ensure();
                CityScape.Ensure();
                return;
            }
        }

        void OnDestroy()
        {
            if (made == this) made = null;
            if (mat != null) Destroy(mat);
            foreach (var m in meshes) if (m != null) Destroy(m);
        }

        // ---------- the colours (as painted, before the toon light brightens the sunlit side about 1.5 times) ----------

        static readonly Color MeadowA = Hex(0x5f9a45), MeadowB = Hex(0x6ea94f);       // as ToonWorld's, so the Alps' edge matches
        static readonly Color SandA = Hex(0xcfae74), SandB = Hex(0xd8b97f), SandLee = Hex(0xc4a06a);
        static readonly Color Sandstone = Hex(0xb88d60), SeaBed = Hex(0xc9b88c), Shore = Hex(0xd9c79a);
        static readonly Color CityGround = Hex(0x8f8f8c), Palms = Hex(0x3e6a34);
        static readonly Color[] Crops = { Hex(0x6d9b3b), Hex(0x78a243), Hex(0x8ea84c), Hex(0x659640), Hex(0x9aa652) };   // close greens: a quiet patchwork, not a checkerboard
        static readonly Color Limestone = Hex(0xd5c194), StepStone = Hex(0xc9ad7c), Gold = Hex(0xe0aa3c);
        static readonly Color Granite = Hex(0xa5685a), PaleObelisk = Hex(0xcdb98e);

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 0f);
        static Color32 Stone(Color c) { var k = (Color32)c; k.a = 255; return k; }   // alpha 1: cut in courses
        static Color32 Plain(Color c) { var k = (Color32)c; k.a = 0; return k; }

        /// <summary>The ground's colour at a point: meadow by the Alps and round the city, sand in the desert (darker on the
        /// dunes' lee faces), sandstone on the steep, fields and palms in the river's valley, the beach and the sea bed.</summary>
        static Color32 GroundColour(double x, double z, float h, Vector3 n)
        {
            // the two-tone patches fade out toward the Alps' edge, whose ground has its own texture
            float patch = Mathf.Lerp(0.5f, (float)Eastland.Noise(x / 140 + 0.5, z / 140 + 9.1), Mathf.InverseLerp(Eastland.X0, Eastland.X0 + 1200, (float)x));
            float dw = (float)Eastland.DesertWeight(x, z), v = (float)Eastland.ValleyWeight(x, z);
            Color meadow = Color.Lerp(MeadowA, MeadowB, patch);
            // the lee faces of the dunes look away from the wind (north-west): a little darker
            float lee = Mathf.Clamp01(-(n.x * 0.8f + n.z * 0.6f) * 5f);
            Color sand = Color.Lerp(Color.Lerp(SandA, SandB, patch), SandLee, lee);
            Color c = Color.Lerp(meadow, sand, dw);
            if (v > 0.05f && h > 0.8f && h < 16f)
            {
                // the valley's fields: a patchwork of plots, and palm groves along the banks
                int fx = Mathf.FloorToInt((float)(x / 150)), fz = Mathf.FloorToInt((float)(z / 100));
                Color field = Crops[(int)(Eastland.Hash(fx, fz) * Crops.Length) % Crops.Length];
                double bank = System.Math.Abs(x - Eastland.RiverX(z)) - Eastland.RiverHalf(z);
                if (bank < 170 && Eastland.Hash(fx + 77, fz - 31) < 0.6) field = Palms;
                c = Color.Lerp(c, field, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.3f, 0.75f, v)));
            }
            if (n.y < 0.93f) c = Color.Lerp(c, Sandstone, Mathf.InverseLerp(0.93f, 0.8f, n.y) * Mathf.Max(dw, 0.4f));
            double dcx = System.Math.Abs(x - City.CentreX) - City.HalfX, dcz = System.Math.Abs(z - City.CentreZ) - City.HalfZ;
            if (dcx < 0 && dcz < 0) c = CityGround;
            if (h < 2.5f) c = Color.Lerp(Shore, c, Mathf.InverseLerp(1.2f, 2.5f, h));
            if (h < 0.3f) c = Color.Lerp(Shore, SeaBed * 0.8f, Mathf.InverseLerp(0.3f, -30f, h));
            return Plain(c);
        }

        // ---------- the build ----------

        Material MakeMaterial()
        {
            var shader = Shader.Find("Ooda/ToonLandModel");
            if (shader == null) { Debug.LogWarning("OODA: no Ooda/ToonLandModel shader in the build; the east lands are drawn on Lit, without their colours."); shader = Shader.Find("Universal Render Pipeline/Lit"); }
            var m = new Material(shader) { name = "East lands" };
            m.SetFloat("_VertexColour", 1f);
            m.SetFloat("_Course", 4f);
            m.SetColor("_LitTint", new Color(1f, 0.97f, 0.92f));
            m.SetColor("_ShadeColor", new Color(0.74f, 0.62f, 0.66f));   // a warm rose shade on the sand and stone, not the Alps' cool blue
            m.enableInstancing = true;
            return m;
        }

        void Build(Eastland land)
        {
            float t0 = Time.realtimeSinceStartup;
            mat = MakeMaterial();
            // every sample's colour once, from its own full-step slope (shared by the four steps of every square)
            const int N = Eastland.N;
            var cols = new Color32[N * N];
            var nrm = new Vector3[N * N];
            System.Threading.Tasks.Parallel.For(0, N, j =>
            {
                for (int i = 0; i < N; i++)
                {
                    float dx = (land.Sample(i + 1, j) - land.Sample(i - 1, j)) / (2 * Eastland.CellM);
                    float dz = (land.Sample(i, j + 1) - land.Sample(i, j - 1)) / (2 * Eastland.CellM);
                    nrm[j * N + i] = new Vector3(-dx, 1, -dz).normalized;
                    cols[j * N + i] = GroundColour(Eastland.X0 + i * Eastland.CellM, Eastland.Z0 + j * Eastland.CellM, land.Sample(i, j), nrm[j * N + i]);
                }
            });
            int cells = Mathf.RoundToInt(ChunkM / Eastland.CellM), per = Mathf.RoundToInt((Eastland.X1 - Eastland.X0) / ChunkM);
            int squares = 0, tris = 0;
            for (int cz = 0; cz < per; cz++)
                for (int cx = 0; cx < per; cx++)
                {
                    int i0 = cx * cells, j0 = cz * cells;
                    float hi = float.MinValue;
                    for (int j = j0; j <= j0 + cells; j++) for (int i = i0; i <= i0 + cells; i++) hi = Mathf.Max(hi, land.Sample(i, j));
                    if (hi < -35f) continue;   // deep sea: its floor is never seen
                    float x0 = Eastland.X0 + cx * ChunkM, z0 = Eastland.Z0 + cz * ChunkM;
                    var square = new GameObject($"East {cx},{cz}");
                    square.transform.SetParent(transform, false);
                    square.isStatic = true;
                    var lods = new LOD[LodHeights.Length];
                    Mesh collide = null;
                    for (int l = 0; l < lods.Length; l++)
                    {
                        var part = new Part();
                        Ground(part, land, cols, nrm, i0, j0, cells, 1 << l, cx == 0);
                        foreach (var p in Eastland.Pyramids)
                            if (p.x >= x0 && p.x < x0 + ChunkM && p.z >= z0 && p.z < z0 + ChunkM) PyramidMesh(part, p);
                        if (l <= 1)
                            foreach (var o in Eastland.Obelisks)
                                if (o.x >= x0 && o.x < x0 + ChunkM && o.z >= z0 && o.z < z0 + ChunkM) ObeliskMesh(part, o);
                        var mesh = part.ToMesh($"East {cx},{cz} LOD{l}", readable: l == 1);
                        meshes.Add(mesh);
                        if (l == 0) tris += part.tris.Count / 3;
                        if (l == 1) collide = mesh;
                        var go = new GameObject($"LOD{l}");
                        go.transform.SetParent(square.transform, false);
                        go.isStatic = true;
                        go.AddComponent<MeshFilter>().sharedMesh = mesh;
                        var r = go.AddComponent<MeshRenderer>();
                        r.sharedMaterial = mat;
                        r.shadowCastingMode = l <= 1 ? ShadowCastingMode.On : ShadowCastingMode.Off;
                        r.receiveShadows = true;
                        lods[l] = new LOD(LodHeights[l], new Renderer[] { r });
                    }
                    var group = square.AddComponent<LODGroup>();
                    group.SetLODs(lods);
                    group.RecalculateBounds();
                    square.AddComponent<MeshCollider>().sharedMesh = collide;
                    squares++;
                }
            Debug.Log($"OODA: the east lands drawn in {(Time.realtimeSinceStartup - t0) * 1000f:F0} ms: {squares} squares of {ChunkM / 1000f:F0} km, {tris / 1000} k triangles near, {Eastland.Pyramids.Length} pyramids and {Eastland.Obelisks.Length} obelisks; ground {land.lowestM:F0} to {land.highestM:F0} m.");
        }

        /// <summary>A square of the ground every step samples of the grid, with a skirt hanging under each edge.</summary>
        static void Ground(Part p, Eastland land, Color32[] cols, Vector3[] nrm, int i0, int j0, int cells, int step, bool atAlps)
        {
            int n = cells / step + 1;
            int start = p.verts.Count;
            for (int b = 0; b < n; b++)
                for (int a = 0; a < n; a++)
                {
                    int i = i0 + a * step, j = j0 + b * step;
                    float h = land.Sample(i, j);
                    var normal = nrm[j * Eastland.N + i];   // the full step's at every step, so two squares drawn at different steps shade alike where they meet
                    double x = Eastland.X0 + i * Eastland.CellM, z = Eastland.Z0 + j * Eastland.CellM;
                    p.verts.Add(new Vector3((float)x, h, (float)z));
                    p.normals.Add(normal);
                    p.colors.Add(cols[j * Eastland.N + i]);
                }
            for (int b = 0; b + 1 < n; b++)
                for (int a = 0; a + 1 < n; a++)
                {
                    int v00 = start + b * n + a, v10 = v00 + 1, v01 = v00 + n, v11 = v01 + 1;
                    p.tris.Add(v00); p.tris.Add(v01); p.tris.Add(v11);
                    p.tris.Add(v00); p.tris.Add(v11); p.tris.Add(v10);
                }
            // the skirts: each edge's vertices again, SkirtM lower, joined both ways round (seen from either side)
            void Skirt(System.Func<int, int> at)
            {
                int s = p.verts.Count;
                for (int k = 0; k < n; k++)
                {
                    int v = at(k);
                    p.verts.Add(p.verts[v] - new Vector3(0, SkirtM, 0));
                    p.normals.Add(p.normals[v]);
                    p.colors.Add(p.colors[v]);
                }
                for (int k = 0; k + 1 < n; k++)
                {
                    int a = at(k), b = at(k + 1), c = s + k, d = s + k + 1;
                    p.tris.Add(a); p.tris.Add(b); p.tris.Add(d); p.tris.Add(a); p.tris.Add(d); p.tris.Add(c);
                    p.tris.Add(a); p.tris.Add(d); p.tris.Add(b); p.tris.Add(a); p.tris.Add(c); p.tris.Add(d);
                }
            }
            Skirt(k => start + k);
            Skirt(k => start + (n - 1) * n + k);
            if (!atAlps) Skirt(k => start + k * n);   // none along the Alps' edge: the two meet without a step, and a skirt there was inked
            Skirt(k => start + k * n + n - 1);
        }

        /// <summary>A pyramid: four faces of dressed limestone in courses under a gilded capstone (its top 7 %), sunk 4 m into
        /// its pad; a step pyramid is its tiers, each a box of stone.</summary>
        static void PyramidMesh(Part p, Eastland.Pyramid py)
        {
            var c = new Vector3(py.x, py.baseM, py.z);
            if (py.steps > 0)
            {
                for (int k = 0; k < py.steps; k++)
                {
                    float half = py.half * (1 - k / (float)py.steps);
                    float y0 = k == 0 ? py.baseM - 4f : py.baseM + py.rise * k / py.steps, y1 = py.baseM + py.rise * (k + 1) / py.steps;
                    Box(p, py.x, py.z, half, half, y0, y1, Stone(StepStone), top: true);
                }
                return;
            }
            float sink = 4f / py.rise;
            float h0 = -4f, r0 = py.half * (1 + sink);
            float capU = 0.93f;                              // where the capstone starts, as a share of the height
            var apex = c + Vector3.up * py.rise;
            Vector3[] corner =
            {
                c + new Vector3(-r0, h0, -r0), c + new Vector3(r0, h0, -r0), c + new Vector3(r0, h0, r0), c + new Vector3(-r0, h0, r0),
            };
            for (int k = 0; k < 4; k++)
            {
                Vector3 a = corner[k], b = corner[(k + 1) % 4];
                Vector3 outward = Vector3.Cross(b - a, apex - a);
                Vector3 mid = (a + b) / 2 - c; mid.y = 0;
                if (Vector3.Dot(outward, mid) < 0) outward = -outward;
                outward.Normalize();
                // the share of the way up from the sunk foot to the apex where the capstone begins
                float u = Mathf.InverseLerp(h0, py.rise, py.rise * capU);
                Vector3 ca = Vector3.Lerp(a, apex, u), cb = Vector3.Lerp(b, apex, u);
                p.Quad(a, b, cb, ca, outward, Stone(Limestone));
                p.Tri(ca, cb, apex, outward, Plain(Gold));
            }
        }

        /// <summary>An obelisk: a plinth, a tapering square shaft of granite or limestone, and a gilded pyramidion at its tip.</summary>
        static void ObeliskMesh(Part p, Eastland.Obelisk o)
        {
            var stone = Stone(o.granite ? Granite : PaleObelisk);
            Box(p, o.x, o.z, 8.5f, 8.5f, o.baseM - 3f, o.baseM + 2.5f, Stone(PaleObelisk), top: true);
            float foot = 5f, neck = 3.3f, tip = 6.5f;
            float y0 = o.baseM + 2.5f, y1 = o.TopM - tip;
            var c = new Vector3(o.x, 0, o.z);
            Vector3 P(float sx, float sz, float half, float y) => c + new Vector3(sx * half, y, sz * half);
            (float, float)[] sides = { (-1, -1), (1, -1), (1, 1), (-1, 1) };
            for (int k = 0; k < 4; k++)
            {
                var (ax, az) = sides[k];
                var (bx, bz) = sides[(k + 1) % 4];
                Vector3 a0 = P(ax, az, foot, y0), b0 = P(bx, bz, foot, y0), a1 = P(ax, az, neck, y1), b1 = P(bx, bz, neck, y1);
                Vector3 outward = new Vector3(ax + bx, 0.06f, az + bz).normalized;
                p.Quad(a0, b0, b1, a1, outward, stone);
                Vector3 apex = c + Vector3.up * o.TopM;
                p.Tri(a1, b1, apex, new Vector3(ax + bx, 0.6f, az + bz).normalized, Plain(Gold));
            }
        }

        /// <summary>A box of stone from y0 to y1, its four sides and (if top) its lid.</summary>
        static void Box(Part p, float x, float z, float hx, float hz, float y0, float y1, Color32 col, bool top)
        {
            Vector3 A(float sx, float sz, float y) => new Vector3(x + sx * hx, y, z + sz * hz);
            p.Quad(A(-1, -1, y0), A(1, -1, y0), A(1, -1, y1), A(-1, -1, y1), Vector3.back, col);
            p.Quad(A(1, -1, y0), A(1, 1, y0), A(1, 1, y1), A(1, -1, y1), Vector3.right, col);
            p.Quad(A(1, 1, y0), A(-1, 1, y0), A(-1, 1, y1), A(1, 1, y1), Vector3.forward, col);
            p.Quad(A(-1, 1, y0), A(-1, -1, y0), A(-1, -1, y1), A(-1, 1, y1), Vector3.left, col);
            if (top) p.Quad(A(-1, -1, y1), A(1, -1, y1), A(1, 1, y1), A(-1, 1, y1), Vector3.up, col);
        }

        /// <summary>A mesh being put together: flat-shaded faces each get their own corners, turned to face their normal.</summary>
        class Part
        {
            public readonly List<Vector3> verts = new List<Vector3>(), normals = new List<Vector3>();
            public readonly List<Color32> colors = new List<Color32>();
            public readonly List<int> tris = new List<int>();

            public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 n, Color32 col)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0) { var t = b; b = c; c = t; }
                int s = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c);
                for (int k = 0; k < 3; k++) { normals.Add(n); colors.Add(col); }
                tris.Add(s); tris.Add(s + 1); tris.Add(s + 2);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Color32 col)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0) { var t = b; b = d; d = t; }
                int s = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                for (int k = 0; k < 4; k++) { normals.Add(n); colors.Add(col); }
                tris.Add(s); tris.Add(s + 1); tris.Add(s + 2);
                tris.Add(s); tris.Add(s + 2); tris.Add(s + 3);
            }

            public Mesh ToMesh(string name, bool readable)
            {
                var m = new Mesh { name = name };
                if (verts.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(verts);
                m.SetNormals(normals);
                m.SetColors(colors);
                m.SetTriangles(tris, 0);
                m.RecalculateBounds();
                m.UploadMeshData(!readable);   // the collider's step stays readable: the mesh collider cooks from it
                return m;
            }
        }
    }
}
