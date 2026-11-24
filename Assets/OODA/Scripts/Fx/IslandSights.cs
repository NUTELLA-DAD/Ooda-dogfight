using System.Collections.Generic;
using Ooda.Turn.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ooda.Fx
{
    /// <summary>
    /// The island's sights (Turn/Core/Sights.cs lays them out): the Harbour Bridge, the Rock Arch, the fleet at anchor and
    /// the barrage balloons round Skyline City. Made at run time with the lands (Fx/IslandDress), in their material,
    /// painted per vertex. Every box, solid cable and balloon also gets a collider on the Obstacles layer, which is what
    /// the turn battle crashes aircraft and stops rounds on; they stand still, so a game over the network stays in step.
    /// </summary>
    public class IslandSights : MonoBehaviour
    {
        static IslandSights made;
        readonly List<Mesh> meshes = new List<Mesh>();

        public static IslandSights Ensure(Transform parent, Material land)
        {
            if (made != null) return made;
            var go = new GameObject("Island Sights");
            go.transform.SetParent(parent, false);
            made = go.AddComponent<IslandSights>();
            made.Build(land);
            return made;
        }

        void OnDestroy()
        {
            if (made == this) made = null;
            foreach (var m in meshes) if (m != null) Destroy(m);
        }

        static Color32 Plain(int rgb) => EastlandScape.Plain(EastlandScape.Hex(rgb));
        static Color32 Stone(int rgb) => EastlandScape.Stone(EastlandScape.Hex(rgb));

        /// <summary>Each of Sights' paints as a vertex colour (stone and concrete are cut in courses).</summary>
        static readonly Color32[] Paints =
        {
            Plain(0x6f7a84),   // Hull
            Plain(0xa58f6a),   // Deck
            Plain(0xa9b2b8),   // Upper
            Plain(0x3c4046),   // Dark
            Plain(0xc2452f),   // BridgeRed
            Stone(0xb9b4a8),   // Concrete
            Stone(0x6e6a66),   // Rock
            Plain(0x6f9a4c),   // RockTop
            Plain(0xf2efe6),   // White
            Plain(0xc4c8cc),   // Silver
        };

        void Build(Material land)
        {
            float t0 = Time.realtimeSinceStartup;
            int layer = LayerMask.NameToLayer("Obstacles");
            if (layer < 0) Debug.LogWarning("OODA: no Obstacles layer: the island's sights are drawn but not solid.");

            var solids = new GameObject("Solids");
            solids.transform.SetParent(transform, false);
            int colliders = 0;
            GameObject Solid(string name, Vector3 at, Quaternion turn)
            {
                var go = new GameObject(name);
                go.transform.SetParent(solids.transform, false);
                go.transform.SetPositionAndRotation(at, turn);
                if (layer >= 0) go.layer = layer;
                go.isStatic = true;
                colliders++;
                return go;
            }

            // the boxes: one mesh for the bridge, one for the arch, one for the fleet
            var bridge = new EastlandScape.Part(); var arch = new EastlandScape.Part(); var fleet = new EastlandScape.Part();
            foreach (var b in Sights.Boxes)
            {
                var part = b.name.StartsWith("Harbour Bridge") ? bridge : b.name.StartsWith("Rock Arch") ? arch : fleet;
                var at = new Vector3((float)b.x, (float)b.y, (float)b.z);
                var turn = Quaternion.Euler(0, (float)b.yawDeg, 0);
                var half = new Vector3((float)b.hx, (float)b.hy, (float)b.hz);
                BoxMesh(part, at, turn, half, Paints[b.paint]);
                if (layer >= 0) Solid(b.name, at, turn).AddComponent<BoxCollider>().size = half * 2;
            }

            // the cables: each a thin square bar; the solid ones a box collider along it
            var cables = new EastlandScape.Part();
            foreach (var w in Sights.Wires)
            {
                Vector3 a = new Vector3((float)w.ax, (float)w.ay, (float)w.az), c = new Vector3((float)w.bx, (float)w.by, (float)w.bz);
                float len = Vector3.Distance(a, c);
                if (len < 0.01f) continue;
                var turn = Quaternion.LookRotation(c - a, Mathf.Abs(Vector3.Dot((c - a).normalized, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up);
                float drawn = w.tether ? 0.35f : (float)w.thickM * 0.5f;
                BoxMesh(w.tether ? cables : bridge, (a + c) / 2, turn, new Vector3(drawn, drawn, len / 2), w.tether ? Paints[Sights.Dark] : Paints[Sights.BridgeRed], ends: false);
                if (w.solid && layer >= 0)
                    Solid(w.name, (a + c) / 2, turn).AddComponent<BoxCollider>().size = new Vector3((float)w.thickM, (float)w.thickM, len);
            }

            // the balloons: a fat silver fish with three fins, nose to the west wind, over a winch on the ground
            var balloons = new EastlandScape.Part();
            foreach (var b in Sights.Balloons)
            {
                var at = new Vector3((float)b.x, (float)b.y, (float)b.z);
                var turn = Quaternion.Euler(-4, 262, 0);
                BalloonMesh(balloons, at, turn);
                EastlandScape.Box(cables, (float)b.x, (float)b.z, 3f, 3f, (float)b.groundM - 1f, (float)b.groundM + 2.4f, Paints[Sights.Dark], top: true);
                if (layer >= 0)
                {
                    var c = Solid("City balloon", at, turn).AddComponent<CapsuleCollider>();
                    c.direction = 2; c.radius = 4.8f; c.height = (float)Sights.BalloonRadiusM * 2;
                }
            }

            Add("Harbour Bridge", bridge, land);
            Add("Rock Arch", arch, land);
            Add("Fleet", fleet, land);
            Add("City balloons", balloons, land);
            Add("City balloon cables", cables, land);
            Debug.Log($"OODA: the island's sights made in {(Time.realtimeSinceStartup - t0) * 1000f:F0} ms: the Harbour Bridge, the Rock Arch, {Sights.Ships.Length} ships, {Sights.Balloons.Length} balloons; {colliders} colliders on the Obstacles layer.");
        }

        void Add(string name, EastlandScape.Part part, Material mat)
        {
            if (part.tris.Count == 0) return;
            var mesh = part.ToMesh(name, readable: false);
            meshes.Add(mesh);
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.isStatic = true;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.On;
            r.receiveShadows = true;
        }

        /// <summary>A box at a place, turned, with half sizes along its own axes.</summary>
        static void BoxMesh(EastlandScape.Part p, Vector3 at, Quaternion turn, Vector3 half, Color32 col, bool ends = true)
        {
            Vector3 X = turn * Vector3.right * half.x, Y = turn * Vector3.up * half.y, Z = turn * Vector3.forward * half.z;
            Vector3 nx = turn * Vector3.right, ny = turn * Vector3.up, nz = turn * Vector3.forward;
            p.Quad(at + X - Y - Z, at + X + Y - Z, at + X + Y + Z, at + X - Y + Z, nx, col);
            p.Quad(at - X - Y - Z, at - X + Y - Z, at - X + Y + Z, at - X - Y + Z, -nx, col);
            p.Quad(at - X + Y - Z, at + X + Y - Z, at + X + Y + Z, at - X + Y + Z, ny, col);
            p.Quad(at - X - Y - Z, at + X - Y - Z, at + X - Y + Z, at - X - Y + Z, -ny, col);
            if (!ends) return;
            p.Quad(at - X - Y + Z, at + X - Y + Z, at + X + Y + Z, at - X + Y + Z, nz, col);
            p.Quad(at - X - Y - Z, at + X - Y - Z, at + X + Y - Z, at - X + Y - Z, -nz, col);
        }

        /// <summary>A barrage balloon: an ellipsoid 24 m long with three tail fins.</summary>
        static void BalloonMesh(EastlandScape.Part p, Vector3 at, Quaternion turn)
        {
            const int Rings = 8, Sides = 10;
            float len = (float)Sights.BalloonRadiusM, fat = 4.6f;
            Color32 skin = Paints[Sights.Silver], belly = Plain(0x9aa0a6);
            Vector3 P(int ring, int side)
            {
                float v = Mathf.PI * ring / Rings, u = 2 * Mathf.PI * side / Sides;
                // fatter toward the nose
                float r = Mathf.Sin(v) * fat * (1f + 0.18f * Mathf.Cos(v));
                return at + turn * new Vector3(Mathf.Cos(u) * r, Mathf.Sin(u) * r, Mathf.Cos(v) * len);
            }
            for (int ring = 0; ring < Rings; ring++)
                for (int side = 0; side < Sides; side++)
                {
                    Vector3 a = P(ring, side), b = P(ring, side + 1), c = P(ring + 1, side + 1), d = P(ring + 1, side);
                    Vector3 n = ((a + b + c + d) / 4 - at).normalized;
                    var col = Mathf.Sin(2 * Mathf.PI * (side + 0.5f) / Sides) < -0.3f ? belly : skin;
                    if (ring == 0) p.Tri(a, c, d, n, col);
                    else if (ring == Rings - 1) p.Tri(a, b, d, n, col);
                    else p.Quad(a, b, c, d, n, col);
                }
            foreach (float roll in new[] { 0f, 125f, 235f })
            {
                var fin = turn * Quaternion.Euler(0, 0, roll);
                BoxMesh(p, at + turn * new Vector3(0, 0, -len * 0.72f) + fin * new Vector3(0, 4.2f, 0), fin, new Vector3(0.35f, 2.6f, 3.2f), skin);
            }
        }
    }
}
