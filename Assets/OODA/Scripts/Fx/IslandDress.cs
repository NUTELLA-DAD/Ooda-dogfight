using System.Collections.Generic;
using Ooda.Turn.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ooda.Fx
{
    /// <summary>
    /// What stands on the island outside the Alps (Turn/Core/Island.cs lays it out): the roads that tie the towns together,
    /// the three new airfields' runways with their hangars and towers, the three new towns (Harbour Town on the west bay,
    /// Crossroads where the farmland meets the desert, Oasis Town by the oasis lake), and the reservoir's water behind its
    /// dam. Made at run time with the lands themselves (Fx/EastlandScape), in their material, painted per vertex: nothing
    /// to merge into a scene. None of it is ground to the battle: the houses and hangars are low, and stand on Island's
    /// flat pads. The aircraft carrier at anchor off the north-west coast is here too (IslandDress.Carrier.cs), from its
    /// own model.
    /// </summary>
    public partial class IslandDress : MonoBehaviour
    {
        static IslandDress made;
        readonly List<Mesh> meshes = new List<Mesh>();
        Material water;
        bool ownWater;

        public static IslandDress Ensure(Transform parent, Material land)
        {
            if (made != null) return made;
            var go = new GameObject("Island Dress");
            go.transform.SetParent(parent, false);
            made = go.AddComponent<IslandDress>();
            made.Build(land);
            return made;
        }

        void OnDestroy()
        {
            if (made == this) made = null;
            foreach (var m in meshes) if (m != null) Destroy(m);
            if (ownWater && water != null) Destroy(water);
        }

        static Color32 C(int rgb) => EastlandScape.Plain(EastlandScape.Hex(rgb));

        static readonly Color32 Asphalt = C(0x4a4a4e), Apron = C(0x8c8a84), Paint = C(0xf0eee6), RoadMain = C(0x8a8478), RoadLane = C(0xb39a6c), Verge = C(0x8c8358);
        static readonly Color32 HangarWall = C(0x9aa0a0), HangarRoof = C(0x5d6f66), TowerWall = C(0xe6e2d6), TowerGlass = C(0x3e5c6a);

        /// <summary>The ground under a point: the Alps' terrain inside their square, else the lands round them.</summary>
        static float GroundY(float x, float z)
        {
            if (Eastland.InAlps(x, z))
            {
                var t = Terrain.activeTerrain;
                if (t != null) return t.SampleHeight(new Vector3(x, 0, z)) + t.transform.position.y;
            }
            return (float)Eastland.Land.GroundM(x, z);
        }

        void Build(Material land)
        {
            float t0 = Time.realtimeSinceStartup;
            var roads = new EastlandScape.Part();
            foreach (var r in Island.Roads) RoadMesh(roads, r);
            Add("Roads", roads, land, false);

            var fields = new EastlandScape.Part();
            foreach (var r in Island.Runways) Airfield(fields, r);
            Add("Airfields", fields, land, true);

            Town("Harbour Town", Island.Pads[Island.HarbourTown], land, 0);
            Town("Crossroads", Island.Pads[Island.Crossroads], land, 1);
            Town("Oasis Town", Island.Pads[Island.OasisTown], land, 2);

            Reservoir();
            Carrier();
            Debug.Log($"OODA: the island's dressing made in {(Time.realtimeSinceStartup - t0) * 1000f:F0} ms: {Island.Roads.Length} roads, {Island.Runways.Length} airfields, 3 towns, the reservoir{(CarrierAt != null ? ", the carrier" : "")}.");
        }

        GameObject Add(string name, EastlandScape.Part part, Material mat, bool shadows)
        {
            var mesh = part.ToMesh(name, readable: false);
            meshes.Add(mesh);
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.isStatic = true;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = true;
            return go;
        }

        // ---------- the roads ----------

        /// <summary>A road: a ribbon on the ground along its line (a smooth curve through its corners), a step every 12 m, a
        /// verge either side; lifted a little so the ground between the grid's samples doesn't show through.</summary>
        static void RoadMesh(EastlandScape.Part p, Island.Road road)
        {
            var line = new List<Vector2>();
            double[] xz = Island.RoadLine(road, 12);
            for (int k = 0; k + 1 < xz.Length; k += 2) line.Add(new Vector2((float)xz[k], (float)xz[k + 1]));
            float half = (float)road.widthM / 2, verge = half + 2.5f;
            Color32 col = road.main ? RoadMain : RoadLane;
            Vector3 Lift(Vector2 q, float up) => new Vector3(q.x, GroundY(q.x, q.y) + up, q.y);
            for (int k = 0; k + 1 < line.Count; k++)
            {
                Vector2 a = line[k], b = line[k + 1];
                Vector2 da = (line[Mathf.Min(k + 1, line.Count - 1)] - line[Mathf.Max(k - 1, 0)]).normalized, db = (line[Mathf.Min(k + 2, line.Count - 1)] - line[k]).normalized;
                Vector2 sa = new Vector2(da.y, -da.x), sb = new Vector2(db.y, -db.x);
                p.Quad(Lift(a - sa * verge, 0.55f), Lift(a + sa * verge, 0.55f), Lift(b + sb * verge, 0.55f), Lift(b - sb * verge, 0.55f), Vector3.up, Verge);
                p.Quad(Lift(a - sa * half, 0.8f), Lift(a + sa * half, 0.8f), Lift(b + sb * half, 0.8f), Lift(b - sb * half, 0.8f), Vector3.up, col);
                if (road.main && k % 4 < 2)
                    p.Quad(Lift(a - sa * 0.25f, 0.9f), Lift(a + sa * 0.25f, 0.9f), Lift(b + sb * 0.25f, 0.9f), Lift(b - sb * 0.25f, 0.9f), Vector3.up, Paint);
            }
        }

        // ---------- the airfields ----------

        /// <summary>An airfield on its pad: the runway with its centre line and threshold bars, a taxiway and an apron beside
        /// it, three hangars and a control tower.</summary>
        static void Airfield(EastlandScape.Part p, Island.Runway r)
        {
            Vector3 a = new Vector3((float)r.ax, (float)r.levelM, (float)r.az), b = new Vector3((float)r.bx, (float)r.levelM, (float)r.bz);
            Vector3 dir = (b - a).normalized, side = new Vector3(dir.z, 0, -dir.x);
            float len = Vector3.Distance(a, b), half = (float)r.widthM / 2;
            Vector3 up(float y) => Vector3.up * y;
            void Strip(float from, float to, float off, float h, float y, Color32 col) =>
                p.Quad(a + dir * from + side * (off - h) + up(y), a + dir * from + side * (off + h) + up(y), a + dir * to + side * (off + h) + up(y), a + dir * to + side * (off - h) + up(y), Vector3.up, col);

            Strip(0, len, 0, half, 0.25f, Asphalt);
            for (float d = 60; d < len - 80; d += 60) Strip(d, d + 30, 0, 0.9f, 0.32f, Paint);
            foreach (float end in new[] { 12f, len - 42f })
                for (int k = -3; k <= 3; k++)
                    if (k != 0) Strip(end, end + 30, k * half / 4.2f, 1.6f, 0.32f, Paint);
            // the apron and the taxiway to it, on the side with more room on the pad (the right of the take-off run)
            float apronOff = half + 70f, apronHalf = 46f;
            float a0 = len * 0.3f, a1 = len * 0.7f;
            Strip(a0, a1, apronOff, apronHalf, 0.22f, Apron);
            Strip(a0, a0 + 24, half + 12, 14, 0.22f, Apron);
            Strip(a1 - 24, a1, half + 12, 14, 0.22f, Apron);
            // the hangars along the apron's far edge, and the tower at its end
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            for (int k = 0; k < 3; k++)
            {
                Vector3 c = a + dir * (a0 + 40 + k * (a1 - a0 - 80) / 2f) + side * (apronOff + apronHalf + 20f);
                Shed(p, c, yaw, 26f, 17f, 9f, 6f, HangarWall, HangarRoof);
            }
            Vector3 tw = a + dir * (a1 + 26f) + side * (apronOff + 10f);
            Shed(p, tw, yaw, 5f, 5f, 15f, 0f, TowerWall, TowerWall);
            Shed(p, tw + up(15f), yaw, 6.5f, 6.5f, 4f, 1.5f, TowerGlass, HangarRoof);
        }

        /// <summary>A building: a box (its half length along its yaw, its half depth, its walls' height) under a gabled roof
        /// (its ridge along the length, this much over the eaves; 0 for a flat roof).</summary>
        static void Shed(EastlandScape.Part p, Vector3 c, float yawDeg, float halfLen, float halfDepth, float wall, float ridge, Color32 walls, Color32 roof)
        {
            var q = Quaternion.Euler(0, yawDeg, 0);
            Vector3 f = q * Vector3.forward * halfLen, s = q * Vector3.right * halfDepth;
            Vector3 y0 = Vector3.up * -1.5f, y1 = Vector3.up * wall, y2 = Vector3.up * (wall + ridge);
            Vector3 p00 = c - f - s, p10 = c + f - s, p11 = c + f + s, p01 = c - f + s;
            Vector3 nf = f.normalized, ns = s.normalized;
            p.Quad(p00 + y0, p10 + y0, p10 + y1, p00 + y1, -ns, walls);
            p.Quad(p01 + y0, p11 + y0, p11 + y1, p01 + y1, ns, walls);
            p.Quad(p00 + y0, p01 + y0, p01 + y1, p00 + y1, -nf, walls);
            p.Quad(p10 + y0, p11 + y0, p11 + y1, p10 + y1, nf, walls);
            if (ridge <= 0.01f) { p.Quad(p00 + y1, p10 + y1, p11 + y1, p01 + y1, Vector3.up, roof); return; }
            Vector3 r0 = c - f + y2, r1 = c + f + y2;
            p.Quad(p00 + y1, p10 + y1, r1, r0, (Vector3.up * halfDepth - ns * ridge).normalized, roof);
            p.Quad(p01 + y1, p11 + y1, r1, r0, (Vector3.up * halfDepth + ns * ridge).normalized, roof);
            p.Tri(p00 + y1, p01 + y1, r0, -nf, walls);
            p.Tri(p10 + y1, p11 + y1, r1, nf, walls);
        }

        // ---------- the towns ----------

        struct Style { public int[] walls, roofs; public float flat, wallLo, wallHi, ridge; public int street, square, tower, towerCap; }

        static readonly Style[] Styles =
        {
            // Harbour Town: whitewash and slate, steep roofs
            new Style { walls = new[] { 0xeeeae0, 0xe2ddd0, 0xd9e2e4, 0xf3e6c8 }, roofs = new[] { 0x4f6272, 0x5b6f7c, 0x7a4a3c }, flat = 0f, wallLo = 4.5f, wallHi = 8f, ridge = 3.6f, street = 0x8f8b84, square = 0xa7a39a, tower = 0xeeeae0, towerCap = 0xc8402c },
            // Crossroads: cream walls under terracotta
            new Style { walls = new[] { 0xe9d9b4, 0xdfc9a0, 0xf0e4c8, 0xd4b98c }, roofs = new[] { 0xb5573a, 0xa64c33, 0xc06a44 }, flat = 0.1f, wallLo = 4.5f, wallHi = 9f, ridge = 2.6f, street = 0x9a9080, square = 0xb3a78f, tower = 0xe9d9b4, towerCap = 0x4f6272 },
            // Oasis Town: mud brick and flat roofs
            new Style { walls = new[] { 0xcaa574, 0xd6b383, 0xbd9868, 0xe2c79a }, roofs = new[] { 0xc2a071, 0xb08f62, 0xe8e0cc }, flat = 0.85f, wallLo = 3.5f, wallHi = 7.5f, ridge = 1.6f, street = 0xb79d70, square = 0xc7b085, tower = 0xe2c79a, towerCap = 0xe0aa3c },
        };

        /// <summary>A town on its pad: two streets crossing at a square, a ring lane, and houses in rows facing them, thinner
        /// toward the edge; a tower by the square.</summary>
        void Town(string name, Island.Pad pad, Material land, int style)
        {
            var st = Styles[style];
            var p = new EastlandScape.Part();
            float cx = (float)pad.ax, cz = (float)pad.az, y = (float)pad.levelM, rad = (float)pad.radiusM;
            float turn = (float)Eastland.Hash(style * 17 + 3, 5) * 90f;
            var q = Quaternion.Euler(0, turn, 0);
            Vector3 c = new Vector3(cx, y, cz), ex = q * Vector3.right, ez = q * Vector3.forward;
            // the streets and the square
            void Flat(Vector3 m, Vector3 hx, Vector3 hz, float up, Color32 col) =>
                p.Quad(m - hx - hz + Vector3.up * up, m + hx - hz + Vector3.up * up, m + hx + hz + Vector3.up * up, m - hx + hz + Vector3.up * up, Vector3.up, col);
            Flat(c, ex * rad, ez * 5f, 0.2f, C(st.street));
            Flat(c, ex * 5f, ez * rad, 0.2f, C(st.street));
            Flat(c, ex * 34f, ez * 34f, 0.25f, C(st.square));
            // the houses: on a grid of plots, facing the nearer street, none on the streets or the square
            const float Plot = 30f;
            int cells = Mathf.FloorToInt(rad / Plot);
            for (int j = -cells; j <= cells; j++)
                for (int i = -cells; i <= cells; i++)
                {
                    float u = (i + 0.5f) * Plot, v = (j + 0.5f) * Plot, r = Mathf.Sqrt(u * u + v * v);
                    if (r > rad - 16f) continue;
                    if (Mathf.Abs(u) < 14f || Mathf.Abs(v) < 14f || (Mathf.Abs(u) < 46f && Mathf.Abs(v) < 46f)) continue;
                    double h0 = Eastland.Hash(i * 7 + style * 131, j * 13 - style * 57);
                    if (h0 > 0.92 - 0.5 * (r / rad) * (r / rad)) continue;     // gardens and yards, more of them toward the edge
                    double h1 = Eastland.Hash(i * 3 - 41, j * 5 + 19), h2 = Eastland.Hash(i * 11 + 9, j * 2 - 73), h3 = Eastland.Hash(i - 501, j + 777);
                    float hl = Mathf.Lerp(6.5f, 11f, (float)h1), hd = Mathf.Lerp(4.5f, 6.5f, (float)h2);
                    float wall = Mathf.Lerp(st.wallLo, st.wallHi, (float)(h3 * h3)) * Mathf.Lerp(1.25f, 0.85f, r / rad);
                    bool along = Mathf.Abs(u) > Mathf.Abs(v);                   // its ridge along the street it faces
                    Vector3 at = c + ex * (u + (float)(h1 - 0.5) * 6f) + ez * (v + (float)(h2 - 0.5) * 6f);
                    bool flat = h0 * 7 % 1 < st.flat;
                    Shed(p, at, turn + (along ? 0f : 90f), hl, hd, wall, flat ? 0f : st.ridge * (0.8f + 0.5f * (float)h2),
                         C(st.walls[(int)(h1 * st.walls.Length) % st.walls.Length]), C(st.roofs[(int)(h2 * st.roofs.Length) % st.roofs.Length]));
                }
            // the tower by the square: a clock tower, a harbour light or a minaret, by the town
            Vector3 tw = c + ex * 44f + ez * 44f;
            EastlandScape.Tower(p, tw, 4.2f, 3.4f, -1f, 19f, 8, C(st.tower));
            EastlandScape.Tower(p, tw, 4.6f, 4.6f, 19f, 20f, 8, C(st.towerCap));
            EastlandScape.Tower(p, tw, 3.2f, 0.2f, 20f, 26f, 8, C(st.towerCap));
            if (style == 0)
            {
                // the harbour: a quay along the water and two jetties out into the bay (west of the town)
                Vector3 west = Vector3.left;
                for (int k = -1; k <= 1; k += 2)
                {
                    Vector3 root = c + west * (rad + 40f) + Vector3.forward * (k * 120f);
                    float gy = Mathf.Max(1.8f, y * 0.25f);
                    p.Quad(root + new Vector3(0, gy - y, -5), root + new Vector3(0, gy - y, 5), root + new Vector3(-260, gy - y, 5), root + new Vector3(-260, gy - y, -5), Vector3.up, C(0x8a7a62));
                }
            }
            Add(name, p, land, true);
        }

        // ---------- the reservoir ----------

        /// <summary>The reservoir's water: a sheet at its level out past its shore (the ground rises through it), in the lakes'
        /// own water if the scene has them.</summary>
        void Reservoir()
        {
            var lakes = GameObject.Find("Lakes");
            var lr = lakes != null ? lakes.GetComponent<MeshRenderer>() : null;
            water = lr != null ? lr.sharedMaterial : null;
            if (water == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh == null) return;
                water = new Material(sh) { name = "Reservoir" };
                water.color = new Color(0.18f, 0.50f, 0.66f);
                ownWater = true;
            }
            const int Sides = 48;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var flow = new List<Vector2>(); var t = new List<int>();
            var mid = new Vector3(Eastland.ReservoirX, Eastland.ReservoirLevelM, Eastland.ReservoirZ);
            v.Add(mid); uv.Add(new Vector2(mid.x, mid.z) / 40f); flow.Add(Vector2.zero);
            for (int k = 0; k < Sides; k++)
            {
                float a = k * Mathf.PI * 2 / Sides;
                // out past the shore, but not past the dam's wall
                float x = Eastland.ReservoirX + Mathf.Cos(a) * Eastland.ReservoirRx * 1.25f, z = Mathf.Min(Eastland.ReservoirZ + Mathf.Sin(a) * Eastland.ReservoirRz * 1.25f, Eastland.DamZ);
                v.Add(new Vector3(x, Eastland.ReservoirLevelM, z)); uv.Add(new Vector2(x, z) / 40f); flow.Add(Vector2.zero);
            }
            for (int k = 0; k < Sides; k++) { t.Add(0); t.Add(1 + (k + 1) % Sides); t.Add(1 + k); }
            var mesh = new Mesh { name = "Reservoir" };
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetUVs(1, flow); mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            meshes.Add(mesh);
            var go = new GameObject("Reservoir");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = water;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }
    }
}
