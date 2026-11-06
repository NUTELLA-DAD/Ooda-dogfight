using System;
using System.Collections.Generic;
using Ooda.Pilot;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.UI
{
    /// <summary>
    /// The squadron briefing's 3D set: an apron of concrete at the south end of the town's runway, looking north up it to
    /// the mountains, with a hangar either side (the Allied star-and-bar over ours, a black cross over theirs), the tower and its
    /// flag behind. Your flight parks on the left, the bandits on the right, each plane on its wheels and its pilot standing
    /// in front of it, lead nearest and the wingmen stepping back away from the middle. A gold ring marks the one picked.
    /// Built when the briefing opens, taken down when it closes; Sync puts the setup's planes and pilots on it, rebuilding
    /// only the places that changed. Plain C#: the title screen calls Open, Sync, Tick and Close.
    /// </summary>
    public class BriefingStage
    {
        /// <summary>The planes to park, by Craft (the dogfight's turn prefabs). A missing one shows the side's usual type.</summary>
        public GameObject[] craftPrefabs;

        public bool Built => root != null;
        public Camera Cam { get; private set; }

        /// <summary>One parked plane and its pilot (or the F-4's two crew).</summary>
        public class Place
        {
            public bool ours;
            public int index;
            public Craft craft;
            public PilotKind pilot;
            public GameObject plane;
            public readonly List<Standing> crew = new List<Standing>();
            public Vector3 feet;          // where the (first) pilot stands, world
            public Vector3 planeCentre;   // the middle of the plane, world
        }

        public readonly List<Place> ours = new List<Place>(), theirs = new List<Place>();

        Transform root, parked, holding;
        Vector3 origin;
        Quaternion facing;
        Material concrete, paint, shed, dark, rubber, strut, gold, glass, white;
        GameObject ring;
        Place ringOn;
        float ringT;

        // the camera, and where the planes park (stage frame: x right, z away from the camera, metres)
        static readonly Vector3 CamAt = new Vector3(0, 5.2f, -27f), CamLook = new Vector3(0, 3.2f, 20f);
        const float CamFov = 36f;
        /// <summary>Your lead's plane, and the step back and out to each wingman. The bandits mirror it.</summary>
        static readonly Vector3 LeadAt = new Vector3(-7.5f, 0, 13f), Step = new Vector3(-5.9f, 0, 6.5f);
        /// <summary>Where the pilot stands from his plane's centre, and which way the noses point (degrees from straight away, toward the middle).</summary>
        static readonly Vector3 PilotFromPlane = new Vector3(-1.2f, 0, -5.2f);
        const float NoseYaw = 112f;
        /// <summary>A standing pilot from his feet to the base of his skull; the chibi heads add about half again.</summary>
        const float PilotHeadHeight = 1.95f;

        public static bool Taildragger(Craft c) => c == Craft.Mustang || c == Craft.Spitfire || c == Craft.Bf109 || c == Craft.Zero;

        /// <summary>The pilot a type usually carries: the wolf in the Spitfire and the Bf 109, the fox in the Zero, the tiger in the rest.</summary>
        public static PilotKind UsualOf(Craft c) => c == Craft.Spitfire || c == Craft.Bf109 ? PilotKind.Wolf : c == Craft.Zero ? PilotKind.Fox : PilotKind.Tiger;

        // ---------- the set ----------

        public void Open(Camera cam)
        {
            if (Built) return;
            Cam = cam;
            root = new GameObject("BriefingStage").transform;
            holding = new GameObject("Holding").transform;
            holding.SetParent(root, false);
            holding.gameObject.SetActive(false);   // prefabs land here first, so nothing of theirs wakes before it is switched off
            parked = new GameObject("Parked").transform;
            parked.SetParent(root, false);

            // the south end of the town's runway, looking up it; failing that, in front of where the camera is
            var start = GameObject.Find("RunwayStart");
            var end = GameObject.Find("RunwayEnd");
            if (start != null && end != null)
            {
                Vector3 along = end.transform.position - start.transform.position;
                along.y = 0;
                facing = Quaternion.LookRotation(along.normalized);
                origin = start.transform.position + along.normalized * 70f;
            }
            else
            {
                Vector3 f = cam.transform.forward; f.y = 0;
                facing = Quaternion.LookRotation(f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward);
                origin = cam.transform.position + facing * new Vector3(0, 0, 40f);
            }
            origin.y = GroundUnder(origin, facing) + 0.45f;
            root.SetPositionAndRotation(origin, facing);

            Materials();
            BuildSet();
            ring = Ring();
            ring.SetActive(false);
            PlaceCamera();
        }

        public void Close()
        {
            if (!Built) return;
            UnityEngine.Object.Destroy(root.gameObject);
            root = null;
            ours.Clear(); theirs.Clear();
            ringOn = null;
        }

        public void PlaceCamera()
        {
            if (Cam == null || !Built) return;
            Cam.transform.position = World(CamAt);
            Cam.transform.LookAt(World(CamLook));
            Cam.fieldOfView = CamFov;
            Cam.nearClipPlane = 0.3f;
        }

        Vector3 World(Vector3 local) => origin + facing * local;

        /// <summary>The highest ground under the apron, so it sits on the ground everywhere (and buries itself in any rise).</summary>
        static float GroundUnder(Vector3 at, Quaternion r)
        {
            float best = float.MinValue;
            var t = Terrain.activeTerrain;
            for (float x = -50; x <= 50; x += 10)
                for (float z = -15; z <= 60; z += 15)
                {
                    Vector3 p = at + r * new Vector3(x, 0, z);
                    float y = t != null ? t.SampleHeight(p) + t.transform.position.y : at.y;
                    if (Physics.Raycast(p + Vector3.up * 500f, Vector3.down, out var hit, 1000f)) y = Mathf.Max(y, hit.point.y);
                    best = Mathf.Max(best, y);
                }
            return best == float.MinValue ? at.y : best;
        }

        void Materials()
        {
            // the scene's own runway materials when they are there, so the apron matches the strip it sits on
            Material template = null;
            var runway = GameObject.Find("Runway");
            var rr = runway != null ? runway.GetComponent<Renderer>() : null;
            if (rr != null && rr.sharedMaterials.Length >= 3) { concrete = rr.sharedMaterials[0]; paint = rr.sharedMaterials[1]; shed = rr.sharedMaterials[2]; template = concrete; }
            if (template == null)
            {
                foreach (var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                    if (r.sharedMaterial != null && r.sharedMaterial.HasProperty("_BaseColor")) { template = r.sharedMaterial; break; }
            }
            Material Mat(Color c, float smooth = 0.15f)
            {
                var m = template != null ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                m.SetTexture("_BaseMap", null);
                m.SetTexture("_MainTex", null);
                m.SetColor("_BaseColor", c);
                m.color = c;
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
                return m;
            }
            concrete = Mat(new Color(0.62f, 0.60f, 0.56f), 0.08f);   // a warm, sunlit grey: lighter than the runway's
            var slabs = Slabs();
            concrete.SetTexture("_BaseMap", slabs); concrete.SetTexture("_MainTex", slabs);
            concrete.SetTextureScale("_BaseMap", new Vector2(20f, 18.7f)); concrete.mainTextureScale = new Vector2(20f, 18.7f);
            if (paint == null) paint = Mat(new Color(0.92f, 0.92f, 0.88f));
            shed = Mat(new Color(0.66f, 0.68f, 0.66f), 0.45f);
            var ribs = Corrugated();
            shed.SetTexture("_BaseMap", ribs); shed.SetTexture("_MainTex", ribs);
            shed.SetTextureScale("_BaseMap", new Vector2(14f, 1f)); shed.mainTextureScale = new Vector2(14f, 1f);
            lamp = Mat(new Color(1f, 0.85f, 0.45f), 0.9f);
            dark = Mat(new Color(0.10f, 0.09f, 0.08f));
            rubber = Mat(new Color(0.07f, 0.07f, 0.07f), 0.3f);
            strut = Mat(new Color(0.55f, 0.56f, 0.55f), 0.6f);
            gold = Mat(new Color(1f, 0.82f, 0.25f), 0.7f);
            glass = Mat(new Color(0.20f, 0.30f, 0.38f), 0.9f);
            white = Mat(new Color(0.80f, 0.76f, 0.66f), 0.2f);
            yellow = Mat(new Color(0.95f, 0.75f, 0.15f), 0.2f);
            Textured = tex => { var m = Mat(Color.white); m.SetTexture("_BaseMap", tex); m.SetTexture("_MainTex", tex); return m; };
        }

        Material yellow, lamp;

        /// <summary>One concrete slab, tiled: a mottled grey with a dark seam on two edges and a few stains.</summary>
        static Texture2D Slabs()
        {
            const int N = 128;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float n = 0.86f + 0.08f * Mathf.PerlinNoise(x * 0.09f, y * 0.09f) + 0.06f * Mathf.PerlinNoise(x * 0.5f + 40, y * 0.5f) + 0.04f * (Hash(x, y) - 0.5f);
                    n -= 0.10f * Mathf.Clamp01(Mathf.PerlinNoise(x * 0.03f + 7, y * 0.03f + 3) * 2.2f - 1.4f);   // oil stains
                    if (x < 2 || y < 2) n = 0.45f;
                    else if (x < 3 || y < 3) n *= 0.8f;
                    t.SetPixel(x, y, new Color(n, n, n, 1));
                }
            t.Apply(true);
            return t;
        }

        /// <summary>Corrugated sheet: light and dark bands across, for the hangar roofs (tiled along the arc).</summary>
        static Texture2D Corrugated()
        {
            const int N = 32;
            var t = new Texture2D(N, 4, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
            for (int x = 0; x < N; x++)
            {
                float n = 0.78f + 0.22f * Mathf.Sin(x / (float)N * Mathf.PI * 4f);
                for (int y = 0; y < 4; y++) t.SetPixel(x, y, new Color(n, n, n, 1));
            }
            t.Apply(true);
            return t;
        }

        static float Hash(int x, int y) { uint h = (uint)(x * 374761393 + y * 668265263); h = (h ^ (h >> 13)) * 1274126177; return (h & 0xffff) / 65535f; }
        Func<Texture2D, Material> Textured;

        void BuildSet()
        {
            // the apron: one thick slab (its sides go down into any dip), seams every 7.5 m, and a taxi line up the middle
            Box("Apron", new Vector3(0, -2.5f, 45f), new Vector3(150f, 5f, 140f), concrete);
            for (float z = -12; z < 110; z += 6f) Box("TaxiLine", new Vector3(0, 0.012f, z), new Vector3(0.35f, 0.02f, 3.4f), yellow);
            Box("StopBar", new Vector3(0, 0.012f, 26f), new Vector3(16f, 0.02f, 0.45f), yellow);

            // the hangars, their doors toward the middle; the tower and its flag behind
            Hangar(new Vector3(-40f, 0, 58f), -32f, Emblem(true));
            Hangar(new Vector3(40f, 0, 58f), 32f, Emblem(false));
            Hangar(new Vector3(-66f, 0, 84f), -22f, null);
            Hangar(new Vector3(66f, 0, 84f), 22f, null);
            Box("Tower", new Vector3(4f, 6f, 98f), new Vector3(6f, 12f, 6f), white);
            for (int i = 0; i < 3; i++) Box("TowerWindow", new Vector3(4f, 3f + i * 3.2f, 94.95f), new Vector3(1.2f, 1.4f, 0.1f), glass);
            Box("TowerCab", new Vector3(4f, 13.8f, 98f), new Vector3(7.5f, 3.6f, 7.5f), glass);
            Box("TowerRoof", new Vector3(4f, 15.85f, 98f), new Vector3(8.5f, 0.5f, 8.5f), dark);
            Box("TowerHut", new Vector3(12f, 2f, 98f), new Vector3(12f, 4f, 7f), white);
            Box("FlagPole", new Vector3(-9f, 5.5f, 97f), new Vector3(0.18f, 11f, 0.18f), strut);   // low enough to fly under the VS badge
            var flag = Quad("Flag", new Vector3(-7.25f, 10.1f, 97f), Quaternion.identity, new Vector2(3.4f, 1.7f), Textured(Flag()));
            flag.transform.Rotate(0, 20f, 0);
            // a few crates and drums along the hangar fronts
            var r = new System.Random(7);
            foreach (float side in new[] { -1f, 1f })
                for (int k = 0; k < 5; k++)
                {
                    Vector3 c = new Vector3(side * (36f + (float)r.NextDouble() * 4f), 0, 34f + k * 3.1f);
                    float s = 0.9f + (float)r.NextDouble() * 0.5f;
                    Box("Crate", c + Vector3.up * s / 2, Vector3.one * s, k % 2 == 0 ? shed : white).transform.Rotate(0, (float)r.NextDouble() * 40f, 0);
                }
        }

        GameObject Box(string name, Vector3 local, Vector3 size, Material m)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.Destroy(g.GetComponent<Collider>());
            g.name = name;
            g.transform.SetParent(root, false);
            g.transform.localPosition = local;
            g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = m;
            return g;
        }

        /// <summary>A quad seen from both sides.</summary>
        GameObject Quad(string name, Vector3 local, Quaternion rot, Vector2 size, Material m)
        {
            var g = new GameObject(name);
            g.transform.SetParent(root, false);
            g.transform.localPosition = local;
            g.transform.localRotation = rot;
            var mesh = new Mesh();
            float w = size.x / 2, h = size.y / 2;
            mesh.vertices = new[] { new Vector3(-w, -h, 0), new Vector3(-w, h, 0), new Vector3(w, h, 0), new Vector3(w, -h, 0),
                                    new Vector3(-w, -h, 0), new Vector3(-w, h, 0), new Vector3(w, h, 0), new Vector3(w, -h, 0) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back, Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6 };
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            g.AddComponent<MeshRenderer>().sharedMaterial = m;
            return g;
        }

        /// <summary>A round-roofed hangar, its open door facing the camera turned yaw degrees (negative: toward the right), with
        /// an emblem over the door.</summary>
        void Hangar(Vector3 at, float yaw, Texture2D emblem)
        {
            const float R = 10f, L = 28f;
            const int Segs = 20;
            var g = new GameObject("Hangar");
            g.transform.SetParent(root, false);
            g.transform.localPosition = at;
            g.transform.localRotation = Quaternion.Euler(0, yaw, 0);   // its door (local -z) toward the camera, turned yaw toward the middle
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var shell = new List<int>();
            var ends = new List<int>();
            // the roof, out- and inside, with a rib every few metres
            for (int side = 0; side < 2; side++)
            {
                int b = verts.Count;
                for (int s = 0; s <= Segs; s++)
                {
                    float a = Mathf.PI * s / Segs;
                    Vector3 n = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                    verts.Add(n * R + new Vector3(0, 0, -L / 2)); norms.Add(side == 0 ? n : -n); uvs.Add(new Vector2((float)s / Segs * 6f, 0));
                    verts.Add(n * R + new Vector3(0, 0, L / 2)); norms.Add(side == 0 ? n : -n); uvs.Add(new Vector2((float)s / Segs * 6f, 1));
                }
                for (int s = 0; s < Segs; s++)
                {
                    int i = b + s * 2;
                    if (side == 0) shell.AddRange(new[] { i, i + 1, i + 3, i, i + 3, i + 2 });
                    else shell.AddRange(new[] { i, i + 3, i + 1, i, i + 2, i + 3 });
                }
            }
            // the back wall, closed, and the front's two side panels round a door 13 m wide and 7.5 m high
            {
                int c = verts.Count;
                verts.Add(new Vector3(0, 0, L / 2)); norms.Add(Vector3.forward); uvs.Add(Vector2.zero);
                for (int s = 0; s <= Segs; s++)
                {
                    float a = Mathf.PI * s / Segs;
                    verts.Add(new Vector3(Mathf.Cos(a) * R, Mathf.Sin(a) * R, L / 2)); norms.Add(Vector3.forward); uvs.Add(Vector2.zero);
                }
                for (int s = 0; s < Segs; s++) ends.AddRange(new[] { c, c + 2 + s, c + 1 + s, c, c + 1 + s, c + 2 + s });
            }
            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(shell, 0);
            mesh.SetTriangles(ends, 1);
            mesh.RecalculateBounds();
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            g.AddComponent<MeshRenderer>().sharedMaterials = new[] { shed, shed };
            // the front: a gable over the door, the dark inside through it
            Transform t = g.transform;
            GameObject Part(string name, Vector3 local, Vector3 size, Material m)
            {
                var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
                UnityEngine.Object.Destroy(p.GetComponent<Collider>());
                p.name = name;
                p.transform.SetParent(t, false);
                p.transform.localPosition = local;
                p.transform.localScale = size;
                p.GetComponent<Renderer>().sharedMaterial = m;
                return p;
            }
            Part("Inside", new Vector3(0, 3.75f, -L / 2 + 9f), new Vector3(13f, 7.5f, 0.2f), dark);
            Part("InsideL", new Vector3(-6.5f, 3.75f, -L / 2 + 4.5f), new Vector3(0.2f, 7.5f, 9f), dark);
            Part("InsideR", new Vector3(6.5f, 3.75f, -L / 2 + 4.5f), new Vector3(0.2f, 7.5f, 9f), dark);
            Part("InsideTop", new Vector3(0, 7.5f, -L / 2 + 4.5f), new Vector3(13f, 0.2f, 9f), dark);
            for (int i = 0; i < 3; i++) Part("Lamp", new Vector3(-4f + i * 4f, 7.2f, -L / 2 + 4f), new Vector3(1.4f, 0.25f, 0.6f), lamp);
            Part("CrateIn", new Vector3(-4f, 0.7f, -L / 2 + 6.5f), new Vector3(1.6f, 1.4f, 1.6f), white);
            Part("CrateIn", new Vector3(-2.2f, 0.5f, -L / 2 + 7f), new Vector3(1.1f, 1f, 1.1f), white);
            Part("CrateIn", new Vector3(4.2f, 0.6f, -L / 2 + 6f), new Vector3(1.3f, 1.2f, 2.4f), white);
            Part("WallL", new Vector3(-8.25f, 3.6f, -L / 2), new Vector3(3.5f, 7.2f, 0.3f), white);
            Part("WallR", new Vector3(8.25f, 3.6f, -L / 2), new Vector3(3.5f, 7.2f, 0.3f), white);
            Part("Lintel", new Vector3(0, 8.4f, -L / 2), new Vector3(15.5f, 1.8f, 0.3f), white);
            Part("Gable", new Vector3(0, 9.2f, -L / 2 + 0.1f), new Vector3(7.5f, 1.6f, 0.3f), white);
            if (emblem != null)
            {
                var q = Quad("Emblem", Vector3.zero, Quaternion.identity, new Vector2(4.8f, 2.4f), Textured(emblem));
                q.transform.SetParent(t, false);
                q.transform.localPosition = new Vector3(0f, 8.5f, -L / 2 - 0.35f);
                q.transform.localRotation = Quaternion.identity;
            }
        }

        /// <summary>The emblem over a hangar door: the Allied star-and-bar for us (a white star in a blue disc, white bars
        /// edged in blue), a black cross edged in white for them. Twice as wide as high.</summary>
        static Texture2D Emblem(bool ours) => ours ? StarAndBar(Wall) : Cross();
        static readonly Color Wall = new Color(0.80f, 0.76f, 0.66f);   // the hangar fronts' paint, so the emblem sits on it

        static Texture2D Cross()
        {
            const int W = 128, H = 64;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Color wht = new Color(0.95f, 0.95f, 0.92f), blk = new Color(0.05f, 0.05f, 0.05f), wall = Wall;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int dx = Mathf.Abs(x - W / 2), dy = Mathf.Abs(y - H / 2);
                    t.SetPixel(x, y, (dx < 6 && dy < 26) || (dy < 6 && dx < 26) ? blk : (dx < 10 && dy < 29) || (dy < 10 && dx < 29) ? wht : wall);
                }
            t.Apply();
            return t;
        }

        static Texture2D StarAndBar(Color ground)
        {
            const int W = 128, H = 64;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Color blue = new Color(0.10f, 0.16f, 0.42f), wht = new Color(0.95f, 0.95f, 0.92f);
            const float R = 28f;   // the disc
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float px = x + 0.5f - W / 2f, py = y + 0.5f - H / 2f;
                    float r = Mathf.Sqrt(px * px + py * py);
                    Color c = ground;
                    // the bars: white, a blue edge round them, either side of the disc
                    float ax = Mathf.Abs(px), ay = Mathf.Abs(py);
                    if (ax < 60f && ay < 10f) c = blue;
                    if (ax < 57f && ay < 7f) c = wht;
                    if (r < R + 0.5f) c = blue;
                    if (InStar(px / (R - 1f), py / (R - 1f))) c = wht;
                    t.SetPixel(x, y, c);
                }
            t.Apply();
            return t;
        }

        /// <summary>Inside a five-pointed star of radius 1 at the origin, one point straight up.</summary>
        static bool InStar(float x, float y)
        {
            float r = Mathf.Sqrt(x * x + y * y);
            if (r > 1f) return false;
            float a = Mathf.Atan2(x, y);   // from straight up, clockwise
            float seg = 2 * Mathf.PI / 5;
            float u = Mathf.Repeat(a + seg / 2, seg) - seg / 2;   // angle from the nearest point
            // the edge from a point (radius 1, angle 0) to the next notch (radius 0.382, angle seg/2), as a straight line
            float inner = 0.382f;
            Vector2 p0 = new Vector2(0, 1), p1 = new Vector2(Mathf.Sin(seg / 2) * inner, Mathf.Cos(seg / 2) * inner);
            Vector2 q = new Vector2(Mathf.Sin(Mathf.Abs(u)) * r, Mathf.Cos(Mathf.Abs(u)) * r);
            // inside if q is on the origin's side of the line p0-p1
            float side = (p1.x - p0.x) * (q.y - p0.y) - (p1.y - p0.y) * (q.x - p0.x);
            float origin = (p1.x - p0.x) * (0 - p0.y) - (p1.y - p0.y) * (0 - p0.x);
            return side * origin >= 0;
        }

        /// <summary>The tower's flag: the star-and-bar on olive drab.</summary>
        static Texture2D Flag() => StarAndBar(new Color(0.33f, 0.36f, 0.20f));

        // ---------- the ring ----------

        GameObject Ring()
        {
            var g = new GameObject("PickedRing");
            g.transform.SetParent(root, false);
            var mesh = new Mesh();
            const int N = 64;
            var v = new Vector3[N * 2];
            var tri = new int[N * 6];
            for (int i = 0; i < N; i++)
            {
                float a = 2 * Mathf.PI * i / N;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v[i * 2] = d * 1.0f; v[i * 2 + 1] = d * 1.18f;
                int j = (i + 1) % N;
                tri[i * 6] = i * 2; tri[i * 6 + 1] = j * 2; tri[i * 6 + 2] = i * 2 + 1;
                tri[i * 6 + 3] = i * 2 + 1; tri[i * 6 + 4] = j * 2; tri[i * 6 + 5] = j * 2 + 1;
            }
            mesh.vertices = v;
            mesh.triangles = tri;
            var n = new Vector3[v.Length];
            for (int i = 0; i < n.Length; i++) n[i] = Vector3.up;
            mesh.normals = n;
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            g.AddComponent<MeshRenderer>().sharedMaterial = gold;
            return g;
        }

        /// <summary>Put the gold ring round the feet of the picked place's pilot (null: no ring).</summary>
        public void Pick(Place p)
        {
            if (!Built) return;
            ringOn = p;
            ring.SetActive(p != null);
        }

        public void Tick(float dt)
        {
            if (!Built) return;
            ringT += dt;
            if (ringOn != null)
            {
                float s = 1.25f + 0.06f * Mathf.Sin(ringT * 4f);
                ring.transform.position = ringOn.feet + Vector3.up * 0.03f;
                ring.transform.rotation = facing;
                ring.transform.localScale = new Vector3(s, 1, s);
            }
            foreach (var list in new[] { ours, theirs })
                foreach (var p in list)
                    foreach (var c in p.crew) c.Tick(ringT, p == ringOn);
        }

        // ---------- the planes and pilots ----------

        /// <summary>Park the setup's planes and pilots, rebuilding only the places whose plane or pilot changed.</summary>
        public void Sync(DogfightSetup s)
        {
            if (!Built) return;
            s.FitPilots();
            SyncSide(ours, s.ours, s.oursPilots, true);
            SyncSide(theirs, s.theirs, s.theirsPilots, false);
            if (ringOn != null && !ours.Contains(ringOn) && !theirs.Contains(ringOn)) Pick(null);
        }

        void SyncSide(List<Place> places, Craft[] craft, PilotKind[] pilots, bool ourSide)
        {
            while (places.Count > craft.Length) { Remove(places[places.Count - 1]); places.RemoveAt(places.Count - 1); }
            for (int k = 0; k < craft.Length; k++)
            {
                PilotKind pk = k < pilots.Length ? pilots[k] : PilotKind.Usual;
                if (k < places.Count && places[k].craft == craft[k] && places[k].pilot == pk) continue;
                var p = Park(ourSide, k, craft[k], pk);
                if (k < places.Count) { if (ringOn == places[k]) ringOn = p; Remove(places[k]); places[k] = p; }
                else places.Add(p);
            }
        }

        static void Remove(Place p)
        {
            if (p.plane != null) UnityEngine.Object.Destroy(p.plane);
            foreach (var c in p.crew) if (c != null && c.gameObject != null) UnityEngine.Object.Destroy(c.gameObject);
        }

        GameObject PrefabFor(Craft c, bool ourSide)
        {
            int i = (int)c;
            if (craftPrefabs != null && i < craftPrefabs.Length && craftPrefabs[i] != null) return craftPrefabs[i];
            var fallback = ourSide ? Craft.Mustang : Craft.Bf109;
            return craftPrefabs != null && (int)fallback < craftPrefabs.Length ? craftPrefabs[(int)fallback] : null;
        }

        Place Park(bool ourSide, int k, Craft craft, PilotKind pilot)
        {
            var p = new Place { ours = ourSide, index = k, craft = craft, pilot = pilot };
            float mirror = ourSide ? 1f : -1f;
            Vector3 at = LeadAt + Step * k;
            at.x *= mirror;
            Quaternion yaw = Quaternion.Euler(0, NoseYaw * mirror, 0);
            var prefab = PrefabFor(craft, ourSide);
            if (prefab == null) return p;

            // the plane: switched off while it is still in the holding pen, so none of its flying parts start
            var g = UnityEngine.Object.Instantiate(prefab, holding);
            g.name = $"{(ourSide ? "Ours" : "Theirs")} {k} {craft}";
            foreach (var b in g.GetComponentsInChildren<MonoBehaviour>(true)) if (!(b is PilotRoster)) b.enabled = false;
            // the pilots in the cockpit come out to stand in front of it
            var seated = new List<GameObject>();
            var roster = g.GetComponent<PilotRoster>();
            if (roster != null && craft != Craft.Phantom) { var who = roster.For(pilot); if (who != null) seated.Add(who); }
            else foreach (var a in g.GetComponentsInChildren<Animator>(true)) if (a.isHuman || a.avatar != null) seated.Add(a.gameObject);
            var crewSources = new List<GameObject>(seated);
            foreach (var a in g.GetComponentsInChildren<Animator>(true)) a.gameObject.SetActive(false);

            g.transform.SetParent(parked, false);
            g.transform.localPosition = at;
            g.transform.localRotation = yaw * Quaternion.Euler(Taildragger(craft) ? -9f : 0f, 0, 0);
            OnWheels(g, craft);
            // its loadout on the rails (after the wheels: the missiles hang lower than the airframe sits)
            var tv = g.GetComponent<Ooda.Turn.TurnAircraftView>();
            if (tv != null) { int load = DogfightSetup.Guns(craft, false).missiles; tv.ShowMissiles(load, load); }
            p.plane = g;
            p.planeCentre = Centre(g);

            // the pilot (the F-4's crew side by side), facing the camera, a little turned toward the middle
            Vector3 stand = at + new Vector3(PilotFromPlane.x * mirror, 0, PilotFromPlane.z);
            Vector3 toCam = CamAt - stand; toCam.y = 0;
            Quaternion look = Quaternion.LookRotation(toCam.normalized) * Quaternion.Euler(0, 14f * mirror, 0);
            for (int c = 0; c < crewSources.Count; c++)
            {
                Vector3 spot = stand + new Vector3(-1.5f * mirror * c, 0, 1.1f * c);
                var st = Standing.From(crewSources[c], parked, spot, look, PilotHeadHeight * (k == 0 ? 1.06f : 1f), (k * 3 + c + (ourSide ? 0 : 7)) % 4);
                if (st != null) p.crew.Add(st);
            }
            p.feet = parked.TransformPoint(stand);
            return p;
        }

        static Vector3 Centre(GameObject g)
        {
            bool any = false;
            var b = new Bounds();
            foreach (var r in g.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any ? b.center : g.transform.position;
        }

        /// <summary>Lift a plane (posed on the apron) onto simple wheels: its lowest point just clear of the concrete, the main
        /// wheels under the wings (ahead of the middle for a taildragger, behind it for the P-38 and F-4), a tail or nose wheel.</summary>
        void OnWheels(GameObject g, Craft craft)
        {
            // its size along its own axes, from the meshes' local bounds
            var inv = g.transform.worldToLocalMatrix;
            Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
            foreach (var mf in g.GetComponentsInChildren<MeshFilter>())
            {
                var r = mf.GetComponent<Renderer>();
                if (r == null || !r.enabled || mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                var m = inv * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 q = m.MultiplyPoint3x4(c);
                    lo = Vector3.Min(lo, q); hi = Vector3.Max(hi, q);
                }
            }
            if (float.IsInfinity(lo.x)) return;
            float span = hi.x - lo.x, len = hi.z - lo.z, tall = hi.y - lo.y;
            float wheel = Mathf.Clamp(len * 0.042f, 0.32f, 0.6f);
            float clear = wheel * 2.3f;

            // the lowest point of it, as it stands, just over the concrete
            float minY = float.MaxValue;
            foreach (var r in g.GetComponentsInChildren<Renderer>()) if (r.enabled) minY = Mathf.Min(minY, r.bounds.min.y);
            float ground = parked.position.y;
            g.transform.position += Vector3.up * (ground + clear * 0.55f - minY);

            bool tail = Taildragger(craft);
            float midX = (lo.x + hi.x) / 2;
            var wheels = tail
                ? new[] { (new Vector3(midX - span * 0.15f, 0, hi.z - len * 0.30f), wheel), (new Vector3(midX + span * 0.15f, 0, hi.z - len * 0.30f), wheel), (new Vector3(midX, 0, lo.z + len * 0.07f), wheel * 0.45f) }
                : new[] { (new Vector3(midX - span * 0.16f, 0, hi.z - len * 0.55f), wheel), (new Vector3(midX + span * 0.16f, 0, hi.z - len * 0.55f), wheel), (new Vector3(midX, 0, hi.z - len * 0.16f), wheel * 0.8f) };
            foreach (var (local, r) in wheels)
            {
                Vector3 w = g.transform.TransformPoint(local);
                w.y = ground + r;
                Vector3 side = g.transform.right; side.y = 0; side.Normalize();
                // the tyre: a squat cylinder on its side
                var tyre = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                UnityEngine.Object.Destroy(tyre.GetComponent<Collider>());
                tyre.name = "Wheel";
                tyre.transform.SetParent(g.transform, true);
                tyre.transform.position = w;
                tyre.transform.rotation = Quaternion.FromToRotation(Vector3.up, side);
                tyre.transform.localScale = Vector3.one;
                SetWorldScale(tyre.transform, new Vector3(r * 2, r * 0.32f, r * 2));
                tyre.GetComponent<Renderer>().sharedMaterial = rubber;
                // the leg: from the hub up into the wing (its top hides inside it)
                float top = ground + clear * 0.55f + tall * 0.22f;
                var leg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                UnityEngine.Object.Destroy(leg.GetComponent<Collider>());
                leg.name = "Leg";
                leg.transform.SetParent(g.transform, true);
                leg.transform.position = new Vector3(w.x, (w.y + top) / 2, w.z);
                leg.transform.rotation = Quaternion.identity;
                SetWorldScale(leg.transform, new Vector3(r * 0.28f, (top - w.y) / 2, r * 0.28f));
                leg.GetComponent<Renderer>().sharedMaterial = strut;
            }
        }

        static void SetWorldScale(Transform t, Vector3 world)
        {
            t.localScale = Vector3.one;
            Vector3 l = t.lossyScale;
            t.localScale = new Vector3(world.x / Mathf.Max(1e-4f, l.x), world.y / Mathf.Max(1e-4f, l.y), world.z / Mathf.Max(1e-4f, l.z));
        }

        /// <summary>Screen rectangle (GUI coordinates, top left origin) round a place's plane and pilot, for clicks.</summary>
        public Rect ScreenBox(Place p)
        {
            if (Cam == null) return Rect.zero;
            bool any = false;
            var b = new Bounds();
            void Add(GameObject g) { if (g == null) return; foreach (var r in g.GetComponentsInChildren<Renderer>()) if (r.enabled) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); } }
            foreach (var c in p.crew) if (c != null) Add(c.gameObject);
            if (!any) Add(p.plane);
            if (!any) return Rect.zero;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 sp = Cam.WorldToScreenPoint(c);
                if (sp.z < 0) continue;
                x0 = Mathf.Min(x0, sp.x); x1 = Mathf.Max(x1, sp.x);
                y0 = Mathf.Min(y0, Screen.height - sp.y); y1 = Mathf.Max(y1, Screen.height - sp.y);
            }
            return x0 > x1 ? Rect.zero : Rect.MinMaxRect(x0, y0, x1, y1);
        }

        /// <summary>Where a world point is on the screen, GUI coordinates; z is how far in front of the camera.</summary>
        public Vector3 OnScreen(Vector3 world)
        {
            if (Cam == null) return new Vector3(-1, -1, -1);
            Vector3 sp = Cam.WorldToScreenPoint(world);
            return new Vector3(sp.x, Screen.height - sp.y, sp.z);
        }
    }

    /// <summary>
    /// A pilot taken out of a cockpit and stood on the apron: his animator off, and his Humanoid pose set straight from
    /// muscle values (standing, in one of a few stances), with a slow breath and a look round, and a little bounce when picked.
    /// </summary>
    public class Standing : MonoBehaviour
    {
        HumanPoseHandler handler;
        HumanPose pose;
        float[] baseMuscles;
        int stance;
        float seed, bounce;
        Vector3 home;
        static Dictionary<string, int> muscleIndex;

        static int M(string name)
        {
            if (muscleIndex == null)
            {
                muscleIndex = new Dictionary<string, int>();
                for (int i = 0; i < HumanTrait.MuscleCount; i++) muscleIndex[HumanTrait.MuscleName[i]] = i;
            }
            return muscleIndex.TryGetValue(name, out int k) ? k : -1;
        }

        /// <summary>A standing copy of a seated pilot, its feet at spot (local to parent), facing rot, sized so its head
        /// bone stands headHeight over its feet. Null if the pilot has no Humanoid avatar.</summary>
        public static Standing From(GameObject seated, Transform parent, Vector3 spot, Quaternion rot, float headHeight, int stance)
        {
            var src = seated.GetComponent<Animator>();
            if (src == null || src.avatar == null || !src.avatar.isHuman) return null;
            var g = Instantiate(seated, parent);
            g.name = seated.name + " (standing)";
            foreach (var b in g.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled = false;
            var anim = g.GetComponent<Animator>();
            anim.enabled = false;
            g.SetActive(true);
            foreach (var r in g.GetComponentsInChildren<SkinnedMeshRenderer>(true)) { r.updateWhenOffscreen = true; r.enabled = true; }
            g.transform.localPosition = spot;
            g.transform.localRotation = rot;
            g.transform.localScale = Vector3.one;
            var st = g.AddComponent<Standing>();
            st.stance = stance;
            st.seed = UnityEngine.Random.value * 10f;
            st.handler = new HumanPoseHandler(anim.avatar, g.transform);
            live.Add(st);
            st.handler.GetHumanPose(ref st.pose);
            st.baseMuscles = Stance(stance);
            st.Apply(0, 0);

            // sized by the head bone over the feet, then stood with its soles on the apron
            var head = anim.GetBoneTransform(HumanBodyBones.Head);
            var lf = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
            var rf = anim.GetBoneTransform(HumanBodyBones.RightFoot);
            if (head != null && lf != null && rf != null)
            {
                float h = head.position.y - Mathf.Min(lf.position.y, rf.position.y);
                g.transform.localScale = Vector3.one * (headHeight / Mathf.Max(1e-3f, h));
                st.Apply(0, 0);
                float feet = Mathf.Min(lf.position.y, rf.position.y);
                float ankle = headHeight * 0.075f;   // the ankle bone sits about this far over the sole
                g.transform.position += Vector3.up * (parent.TransformPoint(spot).y + ankle - feet);
            }
            st.home = g.transform.localPosition;
            return st;
        }

        /// <summary>The muscle values (index by HumanTrait) of a stance: 0 hands on hips, 1 arms folded, 2 one hand on hip, 3 at ease.</summary>
        static float[] Stance(int stance)
        {
            var m = new float[HumanTrait.MuscleCount];
            void S(string name, float v) { int k = M(name); if (k >= 0) m[k] = v; }
            void Both(string name, float v) { S("Left " + name, v); S("Right " + name, v); }
            // legs straight, a little apart, feet flat
            Both("Upper Leg Front-Back", 0.6f);
            Both("Upper Leg In-Out", 0.12f);
            Both("Lower Leg Stretch", 0.95f);
            Both("Foot Up-Down", 0.05f);
            S("Chest Front-Back", 0.05f);
            Both("Shoulder Down-Up", 0f);
            Both("Hand Down-Up", 0f);
            // fingers half curled
            foreach (var f in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
                for (int j = 1; j <= 3; j++) Both($"{f} {j} Stretched", f == "Thumb" ? 0.2f : -0.3f);
            switch (stance)
            {
                case 1: // arms folded across the chest
                    Both("Arm Down-Up", -0.15f);
                    Both("Arm Front-Back", 0.75f);
                    Both("Arm Twist In-Out", 0.3f);
                    Both("Forearm Stretch", -0.75f);
                    Both("Forearm Twist In-Out", 0.1f);
                    Both("Shoulder Front-Back", 0.3f);
                    S("Head Nod Down-Up", 0.1f);
                    break;
                case 2: // left hand on the hip, right arm down
                    S("Left Arm Down-Up", -0.25f); S("Left Arm Front-Back", -0.2f); S("Left Forearm Stretch", -0.1f); S("Left Arm Twist In-Out", -0.3f);
                    S("Right Arm Down-Up", -0.6f); S("Right Arm Front-Back", 0.1f); S("Right Forearm Stretch", 0.7f);
                    S("Head Tilt Left-Right", 0.15f);
                    break;
                case 3: // at ease: arms down, a little forward
                    Both("Arm Down-Up", -0.6f);
                    Both("Arm Front-Back", 0.15f);
                    Both("Forearm Stretch", 0.6f);
                    break;
                default: // both hands on the hips
                    Both("Arm Down-Up", -0.25f);
                    Both("Arm Front-Back", -0.2f);
                    Both("Arm Twist In-Out", -0.3f);
                    Both("Forearm Stretch", -0.1f);
                    Both("Hand In-Out", 0.3f);
                    S("Chest Front-Back", -0.05f);
                    break;
            }
            return m;
        }

        void Apply(float t, float picked)
        {
            for (int i = 0; i < pose.muscles.Length && i < baseMuscles.Length; i++) pose.muscles[i] = baseMuscles[i];
            // a slow breath, and the head turning a little now and then
            int chest = M("Chest Front-Back"), turn = M("Head Turn Left-Right"), nod = M("Head Nod Down-Up");
            if (chest >= 0) pose.muscles[chest] += 0.03f * Mathf.Sin(t * 1.6f + seed);
            if (turn >= 0) pose.muscles[turn] += 0.25f * Mathf.Sin(t * 0.37f + seed) * Mathf.Clamp01(Mathf.Sin(t * 0.21f + seed * 2) * 2);
            if (nod >= 0) pose.muscles[nod] += 0.05f * Mathf.Sin(t * 0.8f + seed) - 0.08f * picked;
            pose.bodyPosition = new Vector3(0, pose.bodyPosition.y, 0);
            pose.bodyRotation = Quaternion.identity;
            handler.SetHumanPose(ref pose);
        }

        public void Tick(float t, bool picked)
        {
            if (handler == null) return;
            bounce = Mathf.MoveTowards(bounce, picked ? 1f : 0f, Time.unscaledDeltaTime * 3f);
            Apply(t, bounce);
            transform.localPosition = home + Vector3.up * (picked ? 0.06f * Mathf.Abs(Mathf.Sin(t * 3f)) : 0f);
        }

        // every live handler, so they are all let go at quit while their avatars are still there (a handler left to the
        // finalizer after its avatar is gone can crash the player on the way out)
        static readonly List<Standing> live = new List<Standing>();
        [RuntimeInitializeOnLoadMethod]
        static void WatchQuit() => Application.quitting += () => { foreach (var s in live.ToArray()) s.Release(); };

        void Release()
        {
            handler?.Dispose();
            handler = null;
            live.Remove(this);
        }

        void OnDestroy() => Release();
    }
}
