using System.Collections.Generic;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// The portal pairs on screen (the rules are Portal and Specials in Turn/Core/Specials.cs): each end a riveted brass ring
    /// with a glowing inner rim, the entry's green and the exit's magenta, and across it a window onto what lies past the
    /// other end. The window is a second camera at the other end, placed as the main camera is to this end (turned from one
    /// ring's frame to the other's, as the planes are) and clipped at that ring's face, its picture drawn on the disc by
    /// screen position (Fx/PortalView.shader), so you see through it as you would fly through it. Only the nearest few pairs
    /// get a live window; the rest swirl. Fed each frame: Begin, then Pair for each pair to show; it places everything after
    /// the camera has moved (a late LateUpdate).
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class PortalRings : MonoBehaviour
    {
        /// <summary>The camera the windows are seen from.</summary>
        public Camera main;
        /// <summary>The entry's and the exit's rim colours.</summary>
        public Color entryColor = new Color(0.36f, 1f, 0.48f, 1f), exitColor = new Color(1f, 0.31f, 0.85f, 1f);
        /// <summary>How many pairs get live windows (two cameras each), and out to how far (m).</summary>
        public int livePairs = 2;
        public float liveRangeM = 9000f;
        /// <summary>The layer the windows are on: the portal cameras leave it out, so a window never draws into itself.</summary>
        public const int WindowLayer = 30;

        struct Want { public Vector3 a, fa, b, fb; public float open, radius; }

        class End
        {
            public Transform root;
            public MeshRenderer disc;
            public Material mat;
            public Camera cam;
            public RenderTexture rt;
        }

        class Rig { public GameObject go; public End a, b; }

        readonly List<Want> wants = new List<Want>();
        readonly List<Rig> rigs = new List<Rig>();
        Mesh ringMesh, rimMesh, discMesh;
        Material brass, rimA, rimB, windowSrc;
        float meshRadius;

        public static PortalRings Make(Transform parent, Camera main)
        {
            var go = new GameObject("Portals");
            go.transform.SetParent(parent, false);
            var pr = go.AddComponent<PortalRings>();
            pr.main = main;
            return pr;
        }

        /// <summary>Start the frame's list (what isn't given again this frame is hidden).</summary>
        public void Begin() => wants.Clear();

        /// <summary>
        /// Show a pair: the entry at a facing fa, the exit at b facing fb (Unity space), open 0..1 (the iris), radius in m.
        /// </summary>
        public void Pair(Vector3 a, Vector3 fa, Vector3 b, Vector3 fb, float open, float radius)
        {
            if (open <= 0.001f) return;
            wants.Add(new Want { a = a, fa = fa.normalized, b = b, fb = fb.normalized, open = Mathf.Clamp01(open), radius = radius });
        }

        void LateUpdate()
        {
            if (main == null) main = Camera.main;
            // the nearest pairs first: they get the live windows
            Vector3 eye = main != null ? main.transform.position : Vector3.zero;
            wants.Sort((x, y) => Mathf.Min((x.a - eye).sqrMagnitude, (x.b - eye).sqrMagnitude).CompareTo(Mathf.Min((y.a - eye).sqrMagnitude, (y.b - eye).sqrMagnitude)));
            for (int i = 0; i < wants.Count; i++)
            {
                if (i == rigs.Count) rigs.Add(MakeRig(wants[i].radius));
                var r = rigs[i];
                r.go.SetActive(true);
                var w = wants[i];
                bool live = i < livePairs;
                Place(r.a, w.a, w.fa, w.open, w.radius, entryColor);
                Place(r.b, w.b, w.fb, w.open, w.radius, exitColor);
                Window(r.a, w.a, w.fa, w.b, w.fb, w.open, live);
                Window(r.b, w.b, w.fb, w.a, w.fa, w.open, live);
            }
            for (int i = wants.Count; i < rigs.Count; i++)
                if (rigs[i].go.activeSelf)
                {
                    rigs[i].go.SetActive(false);
                    rigs[i].a.cam.enabled = rigs[i].b.cam.enabled = false;
                }
        }

        static Quaternion Facing(Vector3 f)
        {
            Vector3 up = Mathf.Abs(f.y) > 0.98f ? Vector3.forward : Vector3.up;
            return Quaternion.LookRotation(f, up);
        }

        void Place(End e, Vector3 at, Vector3 face, float open, float radius, Color rim)
        {
            e.root.SetPositionAndRotation(at, Facing(face));
            float s = radius / meshRadius * Mathf.Lerp(0.35f, 1f, Mathf.SmoothStep(0, 1, open));
            e.root.localScale = new Vector3(s, s, s);
            e.mat.SetFloat("_Open", Mathf.Clamp01(open * 1.15f));
            e.mat.SetColor("_Tint", rim);
        }

        /// <summary>The window on end e (at, facing face): the view out of the other end (to, facing toFace).</summary>
        void Window(End e, Vector3 at, Vector3 face, Vector3 to, Vector3 toFace, float open, bool live)
        {
            bool show = live && main != null && open > 0.2f && (main.transform.position - at).magnitude < liveRangeM && Seen(e.disc);
            e.mat.SetFloat("_Live", show ? 1f : 0f);
            if (!show) { if (e.cam.enabled) e.cam.enabled = false; return; }
            int w = Mathf.Max(64, Screen.width / 2), h = Mathf.Max(64, Screen.height / 2);
            if (e.rt == null || e.rt.width != w || e.rt.height != h)
            {
                if (e.rt != null) { e.cam.targetTexture = null; e.rt.Release(); Destroy(e.rt); }
                e.rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "PortalView" };
                e.cam.targetTexture = e.rt;
                e.mat.SetTexture("_MainTex", e.rt);
            }
            // the other end's camera: where the main camera would be, turned through from this ring to that one
            Quaternion turn = Facing(toFace) * Quaternion.Inverse(Facing(face));
            Transform m = main.transform;
            var c = e.cam;
            c.transform.SetPositionAndRotation(to + turn * (m.position - at), turn * m.rotation);
            c.fieldOfView = main.fieldOfView;
            c.nearClipPlane = main.nearClipPlane; c.farClipPlane = main.farClipPlane;
            c.clearFlags = main.clearFlags; c.backgroundColor = main.backgroundColor;
            c.cullingMask = main.cullingMask & ~(1 << WindowLayer);
            c.aspect = main.aspect;
            c.ResetProjectionMatrix();
            // clipped at the other ring's face, so nothing between it and the camera gets in the way
            Vector3 n = toFace * Mathf.Sign(Vector3.Dot(toFace, to - c.transform.position));
            float dist = Mathf.Abs(Vector3.Dot(toFace, to - c.transform.position));
            if (dist > 0.5f)
            {
                Matrix4x4 v = c.worldToCameraMatrix;
                Vector3 cp = v.MultiplyPoint(to), cn = v.MultiplyVector(n).normalized;
                c.projectionMatrix = c.CalculateObliqueMatrix(new Vector4(cn.x, cn.y, cn.z, -Vector3.Dot(cp, cn) + 0.05f));
            }
            if (!c.enabled) c.enabled = true;
        }

        readonly Plane[] frustum = new Plane[6];

        bool Seen(Renderer r)
        {
            GeometryUtility.CalculateFrustumPlanes(main, frustum);
            return GeometryUtility.TestPlanesAABB(frustum, r.bounds);
        }

        // ---------- building ----------

        Rig MakeRig(float radius)
        {
            if (ringMesh == null) BuildShared(radius);
            var rig = new Rig { go = new GameObject("PortalPair") };
            rig.go.transform.SetParent(transform, false);
            rig.a = MakeEnd(rig.go.transform, "Entry", rimA);
            rig.b = MakeEnd(rig.go.transform, "Exit", rimB);
            return rig;
        }

        End MakeEnd(Transform parent, string name, Material rim)
        {
            var e = new End { root = new GameObject(name).transform };
            e.root.SetParent(parent, false);
            Part(e.root, "Ring", ringMesh, brass, 0);
            Part(e.root, "Rim", rimMesh, rim, 0);
            e.mat = new Material(windowSrc);
            e.disc = Part(e.root, "Window", discMesh, e.mat, WindowLayer);
            var cg = new GameObject(name + "Camera");
            cg.transform.SetParent(transform, false);
            e.cam = cg.AddComponent<Camera>();
            e.cam.enabled = false;
            e.cam.allowHDR = false; e.cam.allowMSAA = false;
            e.cam.depth = (main != null ? main.depth : 0) - 5;
            return e;
        }

        static MeshRenderer Part(Transform parent, string name, Mesh mesh, Material mat, int layer)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mr;
        }

        void BuildShared(float radius)
        {
            meshRadius = radius;
            float tube = radius * 0.075f;
            // the brass ring: a fat torus, toon-shaded in its vertex colours (lit from above), with rivets round its face
            ringMesh = Torus(radius + tube * 0.4f, tube, 96, 14, (n, a) =>
            {
                float lit = 0.55f + 0.45f * Mathf.Clamp01(Vector3.Dot(n, new Vector3(0.3f, 0.8f, -0.5f).normalized));
                lit = lit > 0.85f ? 1f : lit > 0.6f ? 0.8f : 0.6f;   // cel bands
                Color c = new Color(0.86f, 0.64f, 0.27f) * lit;
                // a dark seam in the middle of the face and rivets along it
                float rivet = Mathf.Repeat(a / (2 * Mathf.PI) * 32f, 1f);
                if (Mathf.Abs(n.z) > 0.7f && rivet < 0.18f) c = new Color(1f, 0.9f, 0.6f);
                c.a = 1f;
                return c;
            }, rivetBump: tube * 0.16f);
            rimMesh = Torus(radius - tube * 0.55f, tube * 0.32f, 96, 8, (n, a) => Color.white, 0f);
            discMesh = Disc(radius - tube * 0.5f, 64);
            var sprites = Shader.Find("Sprites/Default");
            brass = new Material(sprites) { name = "PortalBrass" };
            rimA = new Material(sprites) { name = "PortalRimEntry", color = entryColor };
            rimB = new Material(sprites) { name = "PortalRimExit", color = exitColor };
            windowSrc = Resources.Load<Material>("PortalView");
            if (windowSrc == null) windowSrc = new Material(Shader.Find("Ooda/PortalView") ?? sprites);
        }

        /// <summary>A torus in the XY plane (its axis +Z), coloured by a function of the outward normal and the angle round it.</summary>
        static Mesh Torus(float major, float minor, int segs, int sides, System.Func<Vector3, float, Color> colour, float rivetBump)
        {
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            for (int i = 0; i <= segs; i++)
            {
                float a = i * 2 * Mathf.PI / segs;
                Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                bool rivet = rivetBump > 0 && Mathf.Repeat(a / (2 * Mathf.PI) * 32f, 1f) < 0.18f;
                for (int j = 0; j <= sides; j++)
                {
                    float b = j * 2 * Mathf.PI / sides;
                    Vector3 n = dir * Mathf.Cos(b) + Vector3.forward * Mathf.Sin(b);
                    float r = minor + (rivet && Mathf.Abs(Mathf.Sin(b)) > 0.7f ? rivetBump : 0f);
                    v.Add(dir * major + n * r);
                    c.Add(colour(n, a));
                }
            }
            for (int i = 0; i < segs; i++)
                for (int j = 0; j < sides; j++)
                {
                    int p = i * (sides + 1) + j, q = (i + 1) * (sides + 1) + j;
                    t.Add(p); t.Add(q); t.Add(p + 1);
                    t.Add(p + 1); t.Add(q); t.Add(q + 1);
                }
            var m = new Mesh { name = "PortalTorus" };
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>A flat disc in the XY plane, its UVs 0..1 across it (the shader reads the radius from them).</summary>
        static Mesh Disc(float radius, int segs)
        {
            var v = new List<Vector3> { Vector3.zero }; var uv = new List<Vector2> { new Vector2(0.5f, 0.5f) }; var t = new List<int>();
            for (int i = 0; i <= segs; i++)
            {
                float a = i * 2 * Mathf.PI / segs;
                v.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * radius);
                uv.Add(new Vector2(0.5f + 0.5f * Mathf.Cos(a), 0.5f + 0.5f * Mathf.Sin(a)));
            }
            for (int i = 1; i <= segs; i++) { t.Add(0); t.Add(i + 1); t.Add(i); }
            var m = new Mesh { name = "PortalDisc" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        void OnDestroy()
        {
            foreach (var r in rigs)
                foreach (var e in new[] { r.a, r.b })
                    if (e.rt != null) { e.cam.targetTexture = null; e.rt.Release(); Destroy(e.rt); }
        }
    }
}
