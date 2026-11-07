using System.Collections.Generic;
using Ooda.Turn.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ooda.Fx
{
    /// <summary>
    /// Draws the skyscraper city (Turn/Core/City.cs) from the same boxes the turn engine crashes you into, so what you see
    /// is exactly what you hit. Made at run time when the battle starts (no scene or asset to merge), in a few tens of
    /// milliseconds: the city is cut into squares of ChunkM metres, and each square is one mesh with a sub-mesh per material
    /// (five facades with their windows, and one flat palette for the roofs, streets, pavements, parks and bridges), so a
    /// square is a handful of draw calls whatever the number of towers on it. Each square has a LOD group: the whole of it
    /// near; far off only its taller towers and its ground, without shadows; past that nothing. The facades are small
    /// procedural textures, one bay by one storey, repeated up the walls, the toon look of the rest of the game: flat
    /// pastel walls, punched windows on the low blocks, glass curtain walls on the towers. Box colliders (on the Default
    /// layer, not Obstacles: the collisions in the fight come from City's height field) let a parachute land on a roof.
    /// </summary>
    public class CityScape : MonoBehaviour
    {
        public const float ChunkM = 900f;
        /// <summary>How tall (m) a tower must be to stay in a far square's light mesh.</summary>
        public const float FarTowerM = 50f;
        const float BayM = 3.4f, FloorM = 3.8f;

        static CityScape made;
        Material[] mats;

        /// <summary>The city in this scene, made if it isn't there yet.</summary>
        public static CityScape Ensure()
        {
            if (made != null) return made;
            made = FindFirstObjectByType<CityScape>();
            if (made != null) return made;
            var go = new GameObject("Skyline City");
            made = go.AddComponent<CityScape>();
            made.Build(City.Skyline);
            return made;
        }

        void OnDestroy()
        {
            if (made == this) made = null;
            if (mats != null) foreach (var m in mats) if (m != null) { Destroy(m.mainTexture); Destroy(m); }
        }

        // ---------- the materials ----------

        // the flat palette: one texel per colour, read at its centre
        const int PalW = 4;
        enum Pal { RoofLight, RoofDark, Asphalt, Pavement, Park, Plaza, Concrete, Tree, Trunk, Bank, Count }
        static readonly Color[] Palette =
        {
            new Color(0.70f, 0.71f, 0.74f), new Color(0.52f, 0.54f, 0.58f), new Color(0.31f, 0.33f, 0.38f), new Color(0.72f, 0.71f, 0.68f),
            new Color(0.42f, 0.64f, 0.31f), new Color(0.82f, 0.78f, 0.68f), new Color(0.76f, 0.75f, 0.72f), new Color(0.25f, 0.47f, 0.23f),
            new Color(0.42f, 0.30f, 0.20f), new Color(0.58f, 0.56f, 0.52f),
        };
        static Vector2 PalUv(Pal p) { int i = (int)p; return new Vector2((i % PalW + 0.5f) / PalW, (i / PalW + 0.5f) / PalW); }

        struct Facade { public Color wall, glass, trim; public bool curtain; public float smooth; }
        static readonly Facade[] Facades =
        {
            new Facade { wall = new Color(0.82f, 0.88f, 0.90f), glass = new Color(0.26f, 0.52f, 0.64f), trim = new Color(0.66f, 0.74f, 0.78f), curtain = true, smooth = 0.45f },   // teal glass
            new Facade { wall = new Color(0.80f, 0.82f, 0.88f), glass = new Color(0.24f, 0.34f, 0.58f), trim = new Color(0.62f, 0.66f, 0.76f), curtain = true, smooth = 0.45f },   // blue glass
            new Facade { wall = new Color(0.94f, 0.87f, 0.72f), glass = new Color(0.30f, 0.40f, 0.52f), trim = new Color(0.98f, 0.95f, 0.86f), curtain = false, smooth = 0.15f },  // cream stone
            new Facade { wall = new Color(0.80f, 0.46f, 0.36f), glass = new Color(0.27f, 0.32f, 0.42f), trim = new Color(0.93f, 0.84f, 0.74f), curtain = false, smooth = 0.12f },  // terracotta brick
            new Facade { wall = new Color(0.72f, 0.72f, 0.70f), glass = new Color(0.30f, 0.37f, 0.47f), trim = new Color(0.84f, 0.84f, 0.82f), curtain = false, smooth = 0.15f },  // warm concrete
        };
        static int FlatSub => Facades.Length;

        Material[] MakeMaterials()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var list = new Material[Facades.Length + 1];
            for (int s = 0; s < Facades.Length; s++)
            {
                var m = new Material(lit) { name = $"City facade {s}" };
                var tex = FacadeTexture(Facades[s]);
                Set(m, tex, Facades[s].smooth);
                list[s] = m;
            }
            var pal = new Texture2D(PalW, PalW, TextureFormat.RGBA32, false) { name = "City palette", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[PalW * PalW];
            for (int i = 0; i < px.Length; i++) px[i] = i < Palette.Length ? Palette[i] : Color.magenta;
            pal.SetPixels(px); pal.Apply(false, true);
            var flat = new Material(lit) { name = "City flat" };
            Set(flat, pal, 0.1f);
            list[FlatSub] = flat;
            return list;
        }

        static void Set(Material m, Texture2D tex, float smooth)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            m.mainTexture = tex;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            m.enableInstancing = true;
        }

        /// <summary>One bay by one storey, repeated up and along the walls: a curtain wall's glass between thin mullions and
        /// a spandrel band, or a punched window with a sill in a plain wall. The glass is lighter at the top (the sky in it).</summary>
        static Texture2D FacadeTexture(Facade f)
        {
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "City facade", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    Color c = f.wall;
                    if (f.curtain)
                    {
                        bool spandrel = y < 12, mullion = x < 3 || x >= N - 3 || (x >= N / 2 - 1 && x <= N / 2);
                        if (spandrel) c = f.trim;
                        else if (!mullion)
                        {
                            float up = (y - 12f) / (N - 12f);
                            c = Color.Lerp(f.glass * 0.85f, Color.Lerp(f.glass, Color.white, 0.45f), up * up);
                            if ((x + y) % 23 == 0) c = Color.Lerp(c, Color.white, 0.25f);   // a glint
                        }
                    }
                    else
                    {
                        bool window = x >= 16 && x < 48 && y >= 18 && y < 54, sill = x >= 13 && x < 51 && y >= 14 && y < 18;
                        bool lintel = x >= 16 && x < 48 && y >= 54 && y < 57;
                        if (window)
                        {
                            float up = (y - 18f) / 36f;
                            c = Color.Lerp(f.glass * 0.9f, Color.Lerp(f.glass, Color.white, 0.4f), up);
                            if (x == 31 || x == 32) c = f.trim;   // the glazing bar
                        }
                        else if (sill || lintel) c = f.trim;
                        else
                        {
                            // a little grain in the wall so it isn't a plastic slab close up
                            float n = Mathf.PerlinNoise(x * 0.31f, y * 0.29f) - 0.5f;
                            c = f.wall * (1 + 0.06f * n);
                        }
                    }
                    c.a = 1;
                    px[y * N + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply(true, true);
            return tex;
        }

        // ---------- the meshes ----------

        class Part
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int>[] tris;
            public Part(int subs) { tris = new List<int>[subs]; for (int s = 0; s < subs; s++) tris[s] = new List<int>(); }

            public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                int k = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                n.Add(normal); n.Add(normal); n.Add(normal); n.Add(normal);
                uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
                var t = tris[sub];
                t.Add(k); t.Add(k + 1); t.Add(k + 2); t.Add(k); t.Add(k + 2); t.Add(k + 3);
            }

            public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c, Vector2 u)
            {
                int k = v.Count;
                var normal = Vector3.Cross(b - a, c - a).normalized;
                v.Add(a); v.Add(b); v.Add(c);
                n.Add(normal); n.Add(normal); n.Add(normal);
                uv.Add(u); uv.Add(u); uv.Add(u);
                var t = tris[sub];
                t.Add(k); t.Add(k + 1); t.Add(k + 2);
            }

            /// <summary>A flat box in one palette colour: its top and its four sides, and its underside if it hangs in the air.</summary>
            public void FlatBox(float x0, float z0, float x1, float z1, float y0, float y1, Pal top, Pal side, bool bottom = false)
            {
                Vector2 ut = PalUv(top), us = PalUv(side);
                Quad(FlatSub, new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), Vector3.up, ut, ut, ut, ut);
                Sides(FlatSub, x0, z0, x1, z1, y0, y1, (_, __, ___, ____) => (us, us, us, us));
                if (bottom) Quad(FlatSub, new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), Vector3.down, us, us, us, us);
            }

            public delegate (Vector2, Vector2, Vector2, Vector2) UvFor(float along0, float along1, float y0, float y1);

            /// <summary>The four walls of a box, facing out; uv gives each wall's corners from its run along the wall (m) and its heights.</summary>
            public void Sides(int sub, float x0, float z0, float x1, float z1, float y0, float y1, UvFor uv)
            {
                // south (facing -z), east, north, west: each as seen from outside, left to right
                Wall(sub, new Vector3(x0, 0, z0), new Vector3(x1, 0, z0), Vector3.back, 0, y0, y1, uv);
                Wall(sub, new Vector3(x1, 0, z0), new Vector3(x1, 0, z1), Vector3.right, (x1 - x0), y0, y1, uv);
                Wall(sub, new Vector3(x1, 0, z1), new Vector3(x0, 0, z1), Vector3.forward, (x1 - x0) + (z1 - z0), y0, y1, uv);
                Wall(sub, new Vector3(x0, 0, z1), new Vector3(x0, 0, z0), Vector3.left, 2 * (x1 - x0) + (z1 - z0), y0, y1, uv);
            }

            void Wall(int sub, Vector3 a, Vector3 b, Vector3 normal, float along0, float y0, float y1, UvFor uv)
            {
                float len = Vector3.Distance(a, b);
                var (ua, ub, uc, ud) = uv(along0, along0 + len, y0, y1);
                Quad(sub, new Vector3(a.x, y0, a.z), new Vector3(a.x, y1, a.z), new Vector3(b.x, y1, b.z), new Vector3(b.x, y0, b.z), normal, ua, ub, uc, ud);
            }

            public Mesh ToMesh(string name, Vector3 origin)
            {
                var m = new Mesh { name = name };
                if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                var local = new List<Vector3>(v.Count);
                foreach (var p in v) local.Add(p - origin);
                m.SetVertices(local); m.SetNormals(n); m.SetUVs(0, uv);
                m.subMeshCount = tris.Length;
                for (int s = 0; s < tris.Length; s++) m.SetTriangles(tris[s], s, false);
                m.RecalculateBounds();
                m.UploadMeshData(true);
                return m;
            }
        }

        /// <summary>A tower tier: its walls in its facade (whole bays and storeys, the bays started at a place of its own so the
        /// towers don't all line up), its roof flat (darker on the tall ones).</summary>
        static void TowerBox(Part p, City.Box b)
        {
            if (b.style < 0) { p.FlatBox(b.x0, b.z0, b.x1, b.z1, b.baseM, b.topM, Pal.Concrete, Pal.Concrete, bottom: b.baseM > City.StreetM + 1); return; }
            float shift = (b.tower * 0.618f) % 1f;
            float floorShift = City.StreetM;
            p.Sides(b.style, b.x0, b.z0, b.x1, b.z1, b.baseM, b.topM, (a0, a1, y0, y1) =>
            {
                float u0 = a0 / BayM + shift, u1 = a1 / BayM + shift, v0 = (y0 - floorShift) / FloorM, v1 = (y1 - floorShift) / FloorM;
                return (new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0));
            });
            var roof = PalUv(b.topM > 150 ? Pal.RoofDark : Pal.RoofLight);
            p.Quad(FlatSub, new Vector3(b.x0, b.topM, b.z0), new Vector3(b.x0, b.topM, b.z1), new Vector3(b.x1, b.topM, b.z1), new Vector3(b.x1, b.topM, b.z0), Vector3.up, roof, roof, roof, roof);
        }

        /// <summary>A park tree: a four-sided cone of leaves on a short trunk, about 10 to 15 m tall.</summary>
        static void Tree(Part p, Vector3 at, float h, float yaw)
        {
            float r = h * 0.35f, trunk = h * 0.22f;
            var leaf = PalUv(Pal.Tree);
            var top = at + Vector3.up * h;
            var ring = new Vector3[4];
            for (int k = 0; k < 4; k++) { float a = yaw + k * Mathf.PI / 2; ring[k] = at + new Vector3(Mathf.Cos(a) * r, trunk, Mathf.Sin(a) * r); }
            for (int k = 0; k < 4; k++) p.Tri(FlatSub, ring[k], top, ring[(k + 1) % 4], leaf);
            p.Tri(FlatSub, ring[0], ring[1], ring[2], leaf); p.Tri(FlatSub, ring[0], ring[2], ring[3], leaf);
            p.FlatBox(at.x - 0.6f, at.z - 0.6f, at.x + 0.6f, at.z + 0.6f, at.y, at.y + trunk, Pal.Trunk, Pal.Trunk);
        }

        void Build(City city)
        {
            float t0 = Time.realtimeSinceStartup;
            mats = MakeMaterials();
            float minX = City.CentreX - City.HalfX, minZ = City.CentreZ - City.HalfZ;
            int nx = Mathf.CeilToInt(2 * City.HalfX / ChunkM), nz = Mathf.CeilToInt(2 * City.HalfZ / ChunkM);
            int subs = mats.Length;
            var near = new Part[nx * nz];
            var far = new Part[nx * nz];
            for (int k = 0; k < near.Length; k++) { near[k] = new Part(subs); far[k] = new Part(subs); }
            int Chunk(float x, float z) => Mathf.Clamp(Mathf.FloorToInt((z - minZ) / ChunkM), 0, nz - 1) * nx + Mathf.Clamp(Mathf.FloorToInt((x - minX) / ChunkM), 0, nx - 1);

            // the ground: the island's top in the streets' asphalt, cut by the river, with walls down to the sea at its edges
            // and along the banks; each square gets its own piece of it, near and far
            float riverW = City.CentreX - City.RiverHalfM, riverE = City.CentreX + City.RiverHalfM;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    float x0 = minX + i * ChunkM, x1 = Mathf.Min(minX + 2 * City.HalfX, x0 + ChunkM), z0 = minZ + j * ChunkM, z1 = Mathf.Min(minZ + 2 * City.HalfZ, z0 + ChunkM);
                    foreach (var (a, b) in new[] { (x0, Mathf.Min(x1, riverW)), (Mathf.Max(x0, riverE), x1) })
                    {
                        if (b - a < 0.5f) continue;
                        var u = PalUv(Pal.Asphalt);
                        foreach (var p in new[] { near[j * nx + i], far[j * nx + i] })
                            p.Quad(FlatSub, new Vector3(a, City.StreetM, z0), new Vector3(a, City.StreetM, z1), new Vector3(b, City.StreetM, z1), new Vector3(b, City.StreetM, z0), Vector3.up, u, u, u, u);
                    }
                }
            // the island's edges and the river's banks, down below the sea
            var bank = PalUv(Pal.Bank);
            void Skirt(Vector3 a, Vector3 b, Vector3 normal)
            {
                var p = far[Chunk((a.x + b.x) / 2, (a.z + b.z) / 2)];
                var q = near[Chunk((a.x + b.x) / 2, (a.z + b.z) / 2)];
                foreach (var part in new[] { p, q })
                    part.Quad(FlatSub, new Vector3(a.x, -3, a.z), new Vector3(a.x, City.StreetM, a.z), new Vector3(b.x, City.StreetM, b.z), new Vector3(b.x, -3, b.z), normal, bank, bank, bank, bank);
            }
            float maxX = minX + 2 * City.HalfX, maxZ = minZ + 2 * City.HalfZ;
            for (float z = minZ; z < maxZ - 1; z += ChunkM)
            {
                float z1 = Mathf.Min(maxZ, z + ChunkM);
                Skirt(new Vector3(minX, 0, z1), new Vector3(minX, 0, z), Vector3.left);
                Skirt(new Vector3(maxX, 0, z), new Vector3(maxX, 0, z1), Vector3.right);
                Skirt(new Vector3(riverW, 0, z), new Vector3(riverW, 0, z1), Vector3.right);
                Skirt(new Vector3(riverE, 0, z1), new Vector3(riverE, 0, z), Vector3.left);
            }
            for (float x = minX; x < maxX - 1; x += ChunkM)
            {
                float x1 = Mathf.Min(maxX, x + ChunkM);
                foreach (var (a, b) in new[] { (x, Mathf.Min(x1, riverW)), (Mathf.Max(x, riverE), x1) })
                {
                    if (b - a < 0.5f) continue;
                    Skirt(new Vector3(a, 0, minZ), new Vector3(b, 0, minZ), Vector3.back);
                    Skirt(new Vector3(b, 0, maxZ), new Vector3(a, 0, maxZ), Vector3.forward);
                }
            }

            // the blocks' pavements and the parks (near only), with trees in the parks
            var rng = new Lcg(4111);
            foreach (var pad in city.pads)
            {
                var p = near[Chunk((pad.x0 + pad.x1) / 2, (pad.z0 + pad.z1) / 2)];
                var kind = pad.kind == 1 ? Pal.Park : pad.kind == 2 ? Pal.Plaza : Pal.Pavement;
                p.FlatBox(pad.x0, pad.z0, pad.x1, pad.z1, City.StreetM, City.StreetM + 0.25f, kind, Pal.Pavement);
                if (pad.kind != 1) continue;
                for (float z = pad.z0 + 12; z < pad.z1 - 8; z += 22)
                    for (float x = pad.x0 + 12; x < pad.x1 - 8; x += 22)
                    {
                        if (rng.Next() < 0.3) continue;
                        var at = new Vector3(x + (float)(rng.Next() - 0.5) * 12, City.StreetM + 0.25f, z + (float)(rng.Next() - 0.5) * 12);
                        Tree(p, at, 10 + 6 * (float)rng.Next(), (float)rng.Next() * Mathf.PI);
                    }
            }

            // the towers and the bridges: near all of them, far only the tall ones
            var colliders = new GameObject("Colliders").transform;
            colliders.SetParent(transform, false);
            foreach (var b in city.boxes)
            {
                int c = Chunk((b.x0 + b.x1) / 2, (b.z0 + b.z1) / 2);
                TowerBox(near[c], b);
                if (b.topM >= FarTowerM && (b.x1 - b.x0) > 8) TowerBox(far[c], b);
                var bc = colliders.gameObject.AddComponent<BoxCollider>();
                bc.center = new Vector3((b.x0 + b.x1) / 2, (b.baseM + b.topM) / 2, (b.z0 + b.z1) / 2);
                bc.size = new Vector3(b.x1 - b.x0, b.topM - b.baseM, b.z1 - b.z0);
            }

            // one object per square, with its LOD group
            int verts = 0;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int k = j * nx + i;
                    if (near[k].v.Count == 0) continue;
                    var origin = new Vector3(minX + (i + 0.5f) * ChunkM, 0, minZ + (j + 0.5f) * ChunkM);
                    var sq = new GameObject($"City {i},{j}");
                    sq.transform.SetParent(transform, false);
                    sq.transform.position = origin;
                    var r0 = Piece(sq.transform, "Near", near[k].ToMesh($"City {i},{j} near", origin), ShadowCastingMode.On);
                    var r1 = Piece(sq.transform, "Far", far[k].ToMesh($"City {i},{j} far", origin), ShadowCastingMode.Off);
                    verts += near[k].v.Count;
                    var lod = sq.AddComponent<LODGroup>();
                    lod.SetLODs(new[] { new LOD(0.09f, new[] { r0 }), new LOD(0.03f, new[] { r1 }) });
                    lod.RecalculateBounds();
                }
            Debug.Log($"OODA: Skyline City drawn: {city.towers} towers ({city.boxes.Length} boxes, tallest {city.tallestM:F0} m), {verts / 1000}k vertices near in {nx * nz} squares, {(Time.realtimeSinceStartup - t0) * 1000:F0} ms.");
        }

        MeshRenderer Piece(Transform parent, string name, Mesh mesh, ShadowCastingMode shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            r.shadowCastingMode = shadows;
            r.receiveShadows = true;
            return r;
        }
    }
}
