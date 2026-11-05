using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Ooda.Fx
{
    /// <summary>
    /// The sky and its clouds in the toon look of the smoke (ToonFx): a saturated gradient sky with a flat cartoon sun
    /// (Fx/ToonSky.shader), and every cloud bank redrawn as a cluster of round, inked, cel-shaded puffs with a flat base
    /// (Fx/ToonCloud.shader) in white, periwinkle and violet. Put on every scene as it loads, so the world builders and
    /// the saved scenes stay as they are: the cloud model's copies under "Clouds" are kept for their shapes and hidden,
    /// and the puffs are laid out to fill each one. The palette is all here.
    /// </summary>
    public static class ToonSky
    {
        // ---- the palette
        public static readonly Color Zenith = Hex("#1a4ed6"), High = Hex("#2b8ef4"), Horizon = Hex("#a3e8ff"), Glow = Hex("#ffd39e"),
            Ground = Hex("#74b8dc"), Sun = Hex("#fffbe6"), SunRing = Hex("#fff0ad");
        public static readonly Color CloudLit = Hex("#fffdf4"), CloudShade = Hex("#b4c3ff"), CloudBelly = Hex("#9b8ce8"), CloudLine = Hex("#4a50c2");
        public static readonly Color HazeTint = new Color(0.86f, 0.95f, 1f, 0.1f);

        /// <summary>Off: leave the scenes' own sky and cloud models alone (for comparing).</summary>
        public static bool Enabled = true;

        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            SceneManager.sceneLoaded -= OnLoaded;
            SceneManager.sceneLoaded += OnLoaded;
        }

        static void OnLoaded(Scene scene, LoadSceneMode mode) { if (Enabled) Apply(scene); }

        /// <summary>The toon sky and clouds on this scene (safe to call again: it does the clouds once).</summary>
        public static void Apply(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            var sun = SunOf(roots);
            Vector3 toSun = sun != null ? -sun.transform.forward : new Vector3(0.4f, 0.6f, 0.3f).normalized;
            Shader.SetGlobalVector("_ToonSunDir", toSun);
            Shader.SetGlobalColor("_CloudLit", CloudLit);
            Shader.SetGlobalColor("_CloudShade", CloudShade);
            Shader.SetGlobalColor("_CloudBelly", CloudBelly);
            Shader.SetGlobalColor("_CloudLine", CloudLine);

            var skySrc = Resources.Load<Material>("ToonSky");
            if (skySrc != null && (RenderSettings.skybox == null || RenderSettings.skybox.shader != skySrc.shader))
            {
                var sky = new Material(skySrc) { name = "ToonSky (scene)" };
                sky.SetColor("_Zenith", Zenith); sky.SetColor("_High", High); sky.SetColor("_Horizon", Horizon);
                sky.SetColor("_Glow", Glow); sky.SetColor("_Ground", Ground); sky.SetColor("_Sun", Sun); sky.SetColor("_SunRing", SunRing);
                RenderSettings.skybox = sky;
                if (RenderSettings.fog) RenderSettings.fogColor = Horizon;   // (far hills melt into the horizon's colour)
            }

            foreach (var go in roots)
                if (go.name == "Clouds") Clouds(go.transform);
        }

        static Light SunOf(GameObject[] roots)
        {
            if (RenderSettings.sun != null) return RenderSettings.sun;
            Light best = null;
            foreach (var go in roots)
                foreach (var l in go.GetComponentsInChildren<Light>())
                    if (l.type == LightType.Directional && (best == null || l.intensity > best.intensity)) best = l;
            return best;
        }

        // ---------------------------------------------------------------------------------------------------------
        // the clouds

        static Material hazeInner;

        static void Clouds(Transform root)
        {
            if (root.Find("Toon clouds") != null) return;
            var mat = Resources.Load<Material>("ToonCloud");
            if (mat == null) { Debug.LogWarning("OODA: no Resources/ToonCloud material; the clouds stay as models."); return; }
            var holder = new GameObject("Toon clouds").transform;
            holder.SetParent(root, false);
            holder.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            holder.localScale = Vector3.one;
            if (holder.lossyScale != Vector3.one) { holder.SetParent(null, true); holder.localScale = Vector3.one; }
            int banks = 0, puffs = 0;
            var pieces = new List<Bounds>(8);
            for (int b = 0; b < root.childCount; b++)
            {
                var bank = root.GetChild(b);
                if (bank == holder) continue;
                pieces.Clear();
                foreach (var r in bank.GetComponentsInChildren<Renderer>())
                {
                    if (r.name.StartsWith("Haze")) { Rehaze(r); continue; }
                    pieces.Add(r.bounds);
                    r.enabled = false;
                }
                if (pieces.Count == 0) continue;
                var mesh = BankMesh(pieces, b * 7919 + 17, out int n);
                var go = new GameObject($"Toon {bank.name}");
                go.transform.SetParent(holder, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                banks++; puffs += n;
            }
            Debug.Log($"OODA: toon clouds: {banks} banks of {puffs} puffs.");
        }

        // the soft haze round a bank, a touch of the sky's cyan
        static void Rehaze(Renderer r)
        {
            var src = r.sharedMaterial;
            if (src == null || !src.HasProperty("_Color")) return;
            // (only the inner shell, and faint: the clouds are crisp like the smoke, a wide glow round them blurs that)
            if (r.name.Contains("outer")) { r.enabled = false; return; }
            if (hazeInner == null || hazeInner.shader != src.shader)
            {
                hazeInner = new Material(src) { name = src.name + " (toon)" };
                hazeInner.SetColor("_Color", HazeTint);
            }
            r.sharedMaterial = hazeInner;
        }

        // stable hashes, so the clouds are the same every run
        static uint H(uint x) { x ^= x >> 16; x *= 0x7feb352dU; x ^= x >> 15; x *= 0x846ca68bU; x ^= x >> 16; return x; }
        static float R01(int a, int b) => (H((uint)a * 73856093U ^ H((uint)b * 19349663U + 0x9e3779b9U)) & 0xffffff) / 16777216f;
        static float Rng(int a, int b, float lo, float hi) => lo + (hi - lo) * R01(a, b);

        /// <summary>
        /// One bank's puffs, laid out to fill each of its pieces (the cloud model's copies, by their bounds) like a cumulus:
        /// a row of smaller puffs along the base and bigger ones heaped up toward the middle, all cut flat at the piece's
        /// base. Four corners a puff, each holding the puff's centre; the shader turns them to face the camera.
        /// </summary>
        static Mesh BankMesh(List<Bounds> pieces, int seed, out int count)
        {
            var verts = new List<Vector3>(512); var uv0 = new List<Vector4>(512); var uv1 = new List<Vector4>(512); var tris = new List<int>(768);
            float top = float.MinValue, maxR = 0f;
            float bankBase = float.MaxValue;
            foreach (var p in pieces) { top = Mathf.Max(top, p.max.y); bankBase = Mathf.Min(bankBase, p.min.y + p.size.y * 0.18f); }
            var box = pieces[0];
            int made = 0;

            // (one flat base for the whole bank: cut at each piece's own height, a bank seen from below is a stack of slices)
            void Puff(Vector3 c, float r, int s)
            {
                int m = verts.Count;
                float shape = R01(s, 1), lobes = 3 + (int)(R01(s, 2) * 2.99f);
                for (int k = 0; k < 4; k++)
                {
                    verts.Add(c);
                    uv0.Add(new Vector4(k == 0 || k == 3 ? -1 : 1, k < 2 ? -1 : 1, r, shape));
                    uv1.Add(new Vector4(bankBase, top, lobes, 0));
                }
                tris.Add(m); tris.Add(m + 2); tris.Add(m + 1); tris.Add(m); tris.Add(m + 3); tris.Add(m + 2);
                box.Encapsulate(new Bounds(c, Vector3.one * (r * 2.4f)));
                maxR = Mathf.Max(maxR, r);
                made++;
            }

            for (int pi = 0; pi < pieces.Count; pi++)
            {
                var pb = pieces[pi];
                int ps = seed * 31 + pi * 977;
                float ex = pb.extents.x, ez = pb.extents.z, height = pb.size.y;
                float baseY = pb.min.y + height * 0.18f;
                float rb = Mathf.Max(1f, Mathf.Min(Mathf.Min(ex, ez) * 0.55f, height * 0.85f));
                rb = Mathf.Max(rb, Mathf.Sqrt(Mathf.PI * ex * ez * 0.9f / 36f));   // (a flat piece: fewer, bigger puffs, never gappy)
                int n = Mathf.Clamp(Mathf.RoundToInt(Mathf.PI * ex * ez / (rb * rb) * 0.9f), 5, 36);
                // the base row: smaller puffs round the foot, sitting on the base
                int rim = Mathf.Max(3, n / 3);
                for (int k = 0; k < rim; k++)
                {
                    int s = ps + k * 13;
                    float a = (k + Rng(s, 3, -0.3f, 0.3f)) / rim * Mathf.PI * 2f, rr = Rng(s, 4, 0.7f, 0.92f);
                    float r = rb * Rng(s, 5, 0.5f, 0.72f);
                    Puff(new Vector3(pb.center.x + Mathf.Cos(a) * rr * ex, bankBase + r * Rng(s, 6, 0.3f, 0.55f), pb.center.z + Mathf.Sin(a) * rr * ez), r, s);
                }
                // the heap: bigger and higher toward the middle
                for (int k = 0; k < n; k++)
                {
                    int s = ps + 5000 + k * 17;
                    float a = Rng(s, 3, 0f, Mathf.PI * 2f), rr = Mathf.Sqrt(R01(s, 4)) * 0.8f, mid = 1f - rr * rr;
                    float r = rb * (0.55f + 0.6f * mid) * Rng(s, 5, 0.85f, 1.15f);
                    float y = baseY + r * 0.35f + mid * Mathf.Max(0f, top - baseY - r) * Rng(s, 6, 0.3f, 0.85f);
                    Puff(new Vector3(pb.center.x + Mathf.Cos(a) * rr * ex, y, pb.center.z + Mathf.Sin(a) * rr * ez), r, s);
                }
            }

            count = made;
            var mesh = new Mesh { name = "Toon cloud bank" };
            if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetTriangles(tris, 0, false);
            box.Expand(maxR * 2f);
            mesh.bounds = box;
            mesh.UploadMeshData(true);
            return mesh;
        }
    }
}
