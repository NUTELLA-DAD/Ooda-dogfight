using System.Collections.Generic;
using UnityEngine;

namespace Ooda.UI
{
    /// <summary>
    /// The mission briefing's map table, on the squadron briefing's apron: a wooden table in front of the camera with the
    /// campaign map laid on it, a lamp hanging over it, and your flight's pilots walked over from their planes to stand
    /// round it leaning over the map (the bandits' pilots stay out of the way until it is put away). A pin stands on the
    /// map at each mission; the picked one stands taller, red, and bobs. ShowTable puts it all up, HideTable puts everyone
    /// back where they stood. The camera looks down over the table from the near side with the pilots beyond it.
    /// </summary>
    public partial class BriefingStage
    {
        public bool TableUp => table != null;

        Transform table;
        readonly List<Standing> gathered = new List<Standing>();
        readonly List<GameObject> stoodDown = new List<GameObject>();
        readonly List<Transform> pins = new List<Transform>();
        Material pinMat, pinHot, wood, woodDark;

        // the table (stage frame: x right, z away from the camera, metres); the map lies on its top, north away from you
        static readonly Vector3 TableAt = new Vector3(0, 0, 4.6f);
        public const float TableW = 5.8f, TableD = 3.7f, TableH = 1.1f;
        /// <summary>The map on the table, a little inside its edge.</summary>
        public const float MapW = 5.4f, MapD = 3.375f;
        static readonly Vector3 TableCamAt = new Vector3(0, 4.7f, -1.5f), TableCamLook = new Vector3(0, 1.45f, 5.1f);
        const float TableFov = 42f;

        /// <summary>Put up the table with the map on it, a pin at each of pinsUv (0 to 1 across the map, v north), and gather
        /// your flight round it.</summary>
        public void ShowTable(Texture2D map, Vector2[] pinsUv)
        {
            if (!Built) return;
            HideTable();
            table = new GameObject("MapTable").transform;
            table.SetParent(root, false);
            table.localPosition = TableAt;
            if (wood == null)
            {
                wood = Textured(Grain(new Color(0.48f, 0.30f, 0.17f)));
                woodDark = Textured(Grain(new Color(0.30f, 0.18f, 0.10f)));
                pinMat = new Material(gold);
                pinHot = new Material(gold);
                pinHot.SetColor("_BaseColor", new Color(0.85f, 0.12f, 0.08f)); pinHot.color = new Color(0.85f, 0.12f, 0.08f);
            }

            // the top, its rim and four legs
            TablePart("Top", new Vector3(0, TableH - 0.06f, 0), new Vector3(TableW, 0.12f, TableD), wood);
            TablePart("Rim", new Vector3(0, TableH - 0.16f, 0), new Vector3(TableW - 0.1f, 0.1f, TableD - 0.1f), woodDark);
            foreach (var (sx, sz) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
                TablePart("Leg", new Vector3(sx * (TableW / 2 - 0.25f), (TableH - 0.12f) / 2, sz * (TableD / 2 - 0.25f)), new Vector3(0.16f, TableH - 0.12f, 0.16f), woodDark);

            // the map, flat on the top
            var m = Textured(map);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.05f);
            var q = Quad("Map", Vector3.zero, Quaternion.Euler(90, 0, 0), new Vector2(MapW, MapD), m);
            q.transform.SetParent(table, false);
            q.transform.localPosition = new Vector3(0, TableH + 0.004f, 0);

            // the lamp over it: a dark shade on a cord, a warm bulb, and its light
            var lampAt = new Vector3(0.6f, 4.05f, -0.4f);   // over the near half of the table, clear of the pilots' heads
            TablePart("Cord", lampAt + Vector3.up * 1.2f, new Vector3(0.03f, 2.4f, 0.03f), dark);
            var shade = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(shade.GetComponent<Collider>());
            shade.name = "Shade";
            shade.transform.SetParent(table, false);
            shade.transform.localPosition = lampAt;
            shade.transform.localScale = new Vector3(0.9f, 0.12f, 0.9f);
            shade.GetComponent<Renderer>().sharedMaterial = dark;
            var bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(bulb.GetComponent<Collider>());
            bulb.name = "Bulb";
            bulb.transform.SetParent(table, false);
            bulb.transform.localPosition = lampAt + Vector3.down * 0.16f;
            bulb.transform.localScale = Vector3.one * 0.26f;
            bulb.GetComponent<Renderer>().sharedMaterial = lamp;
            var lightGo = new GameObject("LampLight");
            lightGo.transform.SetParent(table, false);
            lightGo.transform.localPosition = lampAt + Vector3.down * 0.4f;
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.78f, 0.45f);
            light.range = 7f;
            light.intensity = 2.2f;
            light.shadows = LightShadows.None;

            // a pin at each mission: a gold stem with a ball on top
            pins.Clear();
            for (int i = 0; i < pinsUv.Length; i++)
            {
                var pin = new GameObject("Pin " + (i + 1)).transform;
                pin.SetParent(table, false);
                pin.localPosition = MapLocal(pinsUv[i]);
                var stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.Destroy(stem.GetComponent<Collider>());
                stem.transform.SetParent(pin, false);
                stem.transform.localPosition = new Vector3(0, 0.11f, 0);
                stem.transform.localScale = new Vector3(0.03f, 0.11f, 0.03f);
                stem.GetComponent<Renderer>().sharedMaterial = strut;
                var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.Destroy(ball.GetComponent<Collider>());
                ball.name = "Head";
                ball.transform.SetParent(pin, false);
                ball.transform.localPosition = new Vector3(0, 0.25f, 0);
                ball.transform.localScale = Vector3.one * 0.13f;
                ball.GetComponent<Renderer>().sharedMaterial = pinMat;
                pins.Add(pin);
            }

            // your flight round the table: along the far side facing you, then at the ends; the bandits stand down
            var crew = new List<Standing>();
            foreach (var p in ours) foreach (var c in p.crew) if (c != null) crew.Add(c);
            int far = Mathf.Min(crew.Count, 4);
            for (int i = 0; i < crew.Count; i++)
            {
                Vector3 spot; float yaw;
                if (i < far)
                {
                    float x = (i - (far - 1) / 2f) * 1.35f;
                    spot = TableAt + new Vector3(x, 0, TableD / 2 + 0.55f);
                    yaw = 180f - x * 6f;   // a little turned toward the middle of the map
                }
                else
                {
                    float side = (i - far) % 2 == 0 ? -1f : 1f;
                    float along = (i - far) / 2 * 1.3f;
                    spot = TableAt + new Vector3(side * (TableW / 2 + 0.55f), 0, 0.6f - along);
                    yaw = side < 0 ? 115f : -115f;
                }
                crew[i].Gather(spot, Quaternion.Euler(0, yaw, 0), Standing.LeanStance);
                gathered.Add(crew[i]);
            }
            foreach (var p in theirs)
                foreach (var c in p.crew)
                    if (c != null && c.gameObject.activeSelf) { c.gameObject.SetActive(false); stoodDown.Add(c.gameObject); }
            Pick(null);
            PlaceTableCamera();
        }

        /// <summary>Take the table down and send everyone back to their planes.</summary>
        public void HideTable()
        {
            foreach (var s in gathered) if (s != null) s.Disperse();
            gathered.Clear();
            foreach (var g in stoodDown) if (g != null) g.SetActive(true);
            stoodDown.Clear();
            pins.Clear();
            if (table != null) Object.Destroy(table.gameObject);
            table = null;
        }

        public void PlaceTableCamera()
        {
            if (Cam == null || !Built) return;
            Cam.transform.position = World(TableCamAt);
            Cam.transform.LookAt(World(TableCamLook));
            Cam.fieldOfView = TableFov;
            Cam.nearClipPlane = 0.2f;
        }

        /// <summary>The picked pin stands taller, turns red and bobs; the others are gold.</summary>
        public void TablePins(int picked, float t)
        {
            for (int i = 0; i < pins.Count; i++)
            {
                bool on = i == picked;
                var head = pins[i].Find("Head");
                if (head != null) head.GetComponent<Renderer>().sharedMaterial = on ? pinHot : pinMat;
                pins[i].localScale = Vector3.one * (on ? 1.45f + 0.08f * Mathf.Sin(t * 4f) : 1f);
            }
        }

        /// <summary>The top of pin i in the world, for its number badge on the screen.</summary>
        public Vector3 PinTop(int i) => i >= 0 && i < pins.Count ? pins[i].TransformPoint(new Vector3(0, 0.36f, 0)) : Vector3.zero;

        /// <summary>A point on the map (0 to 1 across it, v north) in the world.</summary>
        public Vector3 MapWorld(Vector2 uv) => World(TableAt + MapLocal(uv));

        static Vector3 MapLocal(Vector2 uv) => new Vector3((uv.x - 0.5f) * MapW, TableH + 0.006f, (uv.y - 0.5f) * MapD);

        GameObject TablePart(string name, Vector3 local, Vector3 size, Material m)
        {
            var g = Box(name, Vector3.zero, size, m);
            g.transform.SetParent(table, false);
            g.transform.localPosition = local;
            return g;
        }

        /// <summary>Planks of wood: streaks of grain along x, a dark seam between boards.</summary>
        static Texture2D Grain(Color c)
        {
            const int N = 128;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float g = 0.82f + 0.18f * Mathf.PerlinNoise(x * 0.02f, y * 0.45f) + 0.06f * Mathf.Sin(y * 0.9f + Mathf.PerlinNoise(x * 0.05f, y * 0.1f) * 6f);
                    if (y % 32 < 2) g *= 0.55f;
                    t.SetPixel(x, y, new Color(c.r * g, c.g * g, c.b * g, 1));
                }
            t.Apply(true);
            return t;
        }
    }
}
