using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Ooda.Fx
{
    /// <summary>
    /// The sky and its clouds in the toon look of the smoke (ToonFx): a saturated gradient sky with a flat cartoon sun
    /// (Fx/ToonSky.shader), and every cloud bank redrawn as a cluster of round, inked, cel-shaded puffs with a flat base
    /// (Fx/ToonCloud.shader) in white, periwinkle and violet. Each height has its own weather: low grey-lavender banks in
    /// the valleys, cumulus in the middle, storm towers with rain and lightning higher up, and thin pink cirrus streaks
    /// above the peaks; every bank drifts on the wind, forming and dissolving, and billows as it goes. Put on every scene
    /// as it loads, so the world builders and the saved scenes stay as they are: the cloud model's copies under "Clouds"
    /// are kept for their shapes and hidden, and the puffs are laid out to fill each one. The palette is all here.
    /// </summary>
    public static class ToonSky
    {
        // ---- the palette
        public static readonly Color Zenith = Hex("#1a4ed6"), High = Hex("#2b8ef4"), Horizon = Hex("#a3e8ff"), Glow = Hex("#ffd39e"),
            Ground = Hex("#74b8dc"), Sun = Hex("#fffbe6"), SunRing = Hex("#fff0ad");

        /// <summary>The kinds of cloud, by height (see ToonCloud.shader).</summary>
        public enum Kind { Cumulus = 0, Storm = 1, Cirrus = 2, Low = 3, Rain = 4 }

        // per kind: lit, shade, belly, ink line
        static readonly Color[] KindLit = { Hex("#fffdf4"), Hex("#e2deff"), Hex("#ffffff"), Hex("#f3f5ff") };
        static readonly Color[] KindShade = { Hex("#b4c3ff"), Hex("#958fdc"), Hex("#ffd2ec"), Hex("#c1c7ec") };
        static readonly Color[] KindBelly = { Hex("#9b8ce8"), Hex("#5847a6"), Hex("#ffd2ec"), Hex("#a1a3d8") };
        static readonly Color[] KindLine = { Hex("#4a50c2"), Hex("#2b2672"), Hex("#b3a2ee"), Hex("#6a6db8") };
        public static readonly Color RainCol = Hex("#7f88e6"), FlashCol = Hex("#fffbd2");
        /// <summary>The wind the banks ride (they all drift this way).</summary>
        public static readonly Vector3 Wind = new Vector3(0.8f, 0f, 0.6f);
        // per kind: drift speed (m/s), the span it drifts over while it forms and dissolves (m), billow rate, billow size
        static readonly Vector4[] KindDrift = { new Vector4(9f, 3500f, 0.45f, 0.07f), new Vector4(6f, 2500f, 0.8f, 0.09f), new Vector4(24f, 9000f, 0.3f, 0.04f), new Vector4(4f, 1200f, 0.35f, 0.06f) };

        // heights (m) the kinds live at: AlpineWorld.MakeClouds puts the low banks a few hundred metres over the low ground,
        // and the three bands at 2600-3300, 3500-4200 and 4600-5400
        const float LowBelow = 2200f, StormFrom = 3400f, CirrusFrom = 4400f, StormShare = 0.45f;

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
            // (colour arrays go up as they are, so they go up in the colour space the shaders work in)
            bool lin = QualitySettings.activeColorSpace == ColorSpace.Linear;
            Vector4[] V(Color[] cs) { var v = new Vector4[cs.Length]; for (int i = 0; i < cs.Length; i++) v[i] = lin ? cs[i].linear : cs[i]; return v; }
            Shader.SetGlobalVectorArray("_KindLit", V(KindLit));
            Shader.SetGlobalVectorArray("_KindShade", V(KindShade));
            Shader.SetGlobalVectorArray("_KindBelly", V(KindBelly));
            Shader.SetGlobalVectorArray("_KindLine", V(KindLine));
            Shader.SetGlobalVectorArray("_KindDrift", KindDrift);
            Shader.SetGlobalColor("_RainCol", RainCol);
            Shader.SetGlobalColor("_FlashCol", FlashCol);
            Shader.SetGlobalVector("_ToonWind", new Vector4(Wind.normalized.x, 0f, Wind.normalized.z, 1f));

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
            if (Application.isPlaying && Clock.Instance == null)
                Object.DontDestroyOnLoad(new GameObject("ToonSky clock", typeof(Clock)) { hideFlags = HideFlags.HideInHierarchy });
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
            var kinds = new int[4];
            var pieces = new List<Bounds>(8);
            for (int b = 0; b < root.childCount; b++)
            {
                var bank = root.GetChild(b);
                if (bank == holder) continue;
                pieces.Clear();
                foreach (var r in bank.GetComponentsInChildren<Renderer>())
                {
                    // (the soft haze shells round a bank stay off: the banks drift now, and crisp puffs read like the smoke)
                    if (r.name.StartsWith("Haze")) { r.enabled = false; continue; }
                    pieces.Add(r.bounds);
                    r.enabled = false;
                }
                if (pieces.Count == 0) continue;
                int seed = b * 7919 + 17;
                float y = bank.position.y;
                var kind = y < LowBelow ? Kind.Low : y >= CirrusFrom ? Kind.Cirrus : y >= StormFrom && R01(seed, 99) < StormShare ? Kind.Storm : Kind.Cumulus;
                var mesh = BankMesh(pieces, seed, kind, y >= StormFrom, out int n);
                kinds[(int)kind]++;
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
            Debug.Log($"OODA: toon clouds: {banks} banks of {puffs} puffs: {kinds[0]} cumulus, {kinds[1]} storms, {kinds[2]} cirrus, {kinds[3]} low.");
        }

        /// <summary>
        /// The clouds' clock, in real time: the battle stops the game's clock while you plan, and the sky should not.
        /// </summary>
        sealed class Clock : MonoBehaviour
        {
            public static Clock Instance;
            void Awake() => Instance = this;
            void OnDestroy() { if (Instance == this) Instance = null; }
            void Update() => Shader.SetGlobalFloat("_ToonTime", Time.unscaledTime);
        }

        // stable hashes, so the clouds are the same every run
        static uint H(uint x) { x ^= x >> 16; x *= 0x7feb352dU; x ^= x >> 15; x *= 0x846ca68bU; x ^= x >> 16; return x; }
        static float R01(int a, int b) => (H((uint)a * 73856093U ^ H((uint)b * 19349663U + 0x9e3779b9U)) & 0xffffff) / 16777216f;
        static float Rng(int a, int b, float lo, float hi) => lo + (hi - lo) * R01(a, b);

        /// <summary>
        /// One bank's puffs, laid out to fill each of its pieces (the cloud model's copies, by their bounds) as its kind:
        /// - cumulus: a row of smaller puffs along one flat base and bigger ones heaped toward the middle (taller up high);
        /// - storm: a tall tower with an anvil spread wide at its top, and shafts of rain down to the ground under it;
        /// - cirrus: thin streaks of small stretched puffs laid along the wind, never cut flat;
        /// - low: flat, wide, stretched puffs in a layer or two.
        /// Four corners a puff, each holding the puff's centre; the shader turns them to face the camera and moves them.
        /// </summary>
        static Mesh BankMesh(List<Bounds> pieces, int seed, Kind kind, bool tall, out int count)
        {
            var verts = new List<Vector3>(512); var uv0 = new List<Vector4>(512); var uv1 = new List<Vector4>(512); var uv2 = new List<Vector4>(512);
            var tris = new List<int>(768);
            float top = float.MinValue, maxR = 0f, bankBase = float.MaxValue, widest = 0f;
            foreach (var p in pieces)
            {
                top = Mathf.Max(top, p.max.y);
                bankBase = Mathf.Min(bankBase, p.min.y + p.size.y * 0.18f);
                widest = Mathf.Max(widest, Mathf.Max(p.extents.x, p.extents.z));
            }
            // a storm is its own shape, far bigger than the bank it grew from: a tower several times as tall as it is wide
            float T = widest * 2.6f, towerH = T * 5.5f;
            if (kind == Kind.Storm) top = bankBase + towerH;
            else if (kind == Kind.Cumulus && tall) top = bankBase + (top - bankBase) * 1.6f;
            Vector3 mid0 = Vector3.zero;
            foreach (var p in pieces) mid0 += p.center;
            mid0 /= pieces.Count;
            // how much bigger it is drawn: across, and up
            Vector2 grow = kind == Kind.Cumulus ? Vector2.one * (tall ? 3.5f : 3f) : kind == Kind.Low ? new Vector2(2.5f, 1.6f) : kind == Kind.Cirrus ? new Vector2(4f, 2f) : Vector2.one;
            float topOut = bankBase + (top - bankBase) * grow.y;
            var box = new Bounds(new Vector3(mid0.x, bankBase, mid0.z), Vector3.one);
            float phase = R01(seed, 7);
            int made = 0;
            Vector3 wind = Wind.normalized;

            // (one flat base for the whole bank: cut at each piece's own height, a bank seen from below is a stack of slices)
            // (the layouts work at the size of the cloud model's copies; the clouds are drawn several times bigger, as big as
            // the mountains: grown about the middle of the bank's base, wider than tall for the flat kinds)
            void Puff(Vector3 c, float r, int s, Kind k, float stretch = 1f, float cut = float.NaN)
            {
                int m = verts.Count;
                float shape = R01(s, 1), lobes = 3 + (int)(R01(s, 2) * 2.99f);
                float baseY = float.IsNaN(cut) ? bankBase : cut;
                c = new Vector3(mid0.x + (c.x - mid0.x) * grow.x, bankBase + (c.y - bankBase) * grow.y, mid0.z + (c.z - mid0.z) * grow.x);
                if (cut > float.MinValue) baseY = bankBase + (baseY - bankBase) * grow.y;
                r *= grow.y;
                stretch *= grow.x / grow.y;
                for (int i = 0; i < 4; i++)
                {
                    verts.Add(c);
                    uv0.Add(new Vector4(i == 0 || i == 3 ? -1 : 1, i < 2 ? -1 : 1, r, shape));
                    uv1.Add(new Vector4(baseY, topOut, lobes, (float)k));
                    uv2.Add(new Vector4(stretch, phase, 0, 0));
                }
                tris.Add(m); tris.Add(m + 2); tris.Add(m + 1); tris.Add(m); tris.Add(m + 3); tris.Add(m + 2);
                box.Encapsulate(new Bounds(c, new Vector3(r * stretch, r, r * stretch) * 2.4f));
                maxR = Mathf.Max(maxR, r * stretch);
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

                if (kind == Kind.Storm) continue;   // (built whole below)

                if (kind == Kind.Cirrus)
                {
                    // a streak or two along the wind, small puffs stretched out, a little ragged
                    int streaks = 1 + (int)(R01(ps, 1) * 1.99f);
                    for (int st = 0; st < streaks; st++)
                    {
                        Vector3 side = Vector3.Cross(Vector3.up, wind) * Rng(ps + st, 2, -0.6f, 0.6f) * Mathf.Min(ex, ez);
                        float len = Mathf.Max(ex, ez) * Rng(ps + st, 3, 1.6f, 2.6f);
                        int m = Mathf.Clamp(Mathf.RoundToInt(len / (rb * 0.9f)), 4, 12);
                        for (int k = 0; k < m; k++)
                        {
                            int s = ps + 9000 + st * 101 + k * 7;
                            float u = m == 1 ? 0 : k / (m - 1f) * 2f - 1f;
                            float r = rb * 0.55f * (1.1f - 0.6f * Mathf.Abs(u)) * Rng(s, 5, 0.8f, 1.2f);
                            Vector3 c = pb.center + side + wind * (u * len * 0.5f) + Vector3.up * Rng(s, 6, -0.15f, 0.15f) * rb;
                            Puff(c, r, s, Kind.Cirrus, Rng(s, 7, 2.6f, 3.6f), c.y - r * 10f);
                        }
                    }
                    continue;
                }

                if (kind == Kind.Low)
                {
                    // a flat layer: wide stretched puffs sitting on the base, a few heaped a little higher
                    for (int k = 0; k < n; k++)
                    {
                        int s = ps + 7000 + k * 11;
                        float a = Rng(s, 3, 0f, Mathf.PI * 2f), rr = Mathf.Sqrt(R01(s, 4)) * 0.85f;
                        float r = rb * Rng(s, 5, 0.5f, 0.75f);
                        float y = bankBase + r * Rng(s, 6, 0.25f, 0.5f) + (1f - rr) * r * 0.5f;
                        Puff(new Vector3(pb.center.x + Mathf.Cos(a) * rr * ex, y, pb.center.z + Mathf.Sin(a) * rr * ez), r, s, Kind.Low, Rng(s, 7, 1.6f, 2.2f));
                    }
                    continue;
                }

                // cumulus and storm: the base row, smaller puffs round the foot, sitting on the base
                int rim = Mathf.Max(3, n / 3);
                for (int k = 0; k < rim; k++)
                {
                    int s = ps + k * 13;
                    float a = (k + Rng(s, 3, -0.3f, 0.3f)) / rim * Mathf.PI * 2f, rr = Rng(s, 4, 0.7f, 0.92f);
                    float r = rb * Rng(s, 5, 0.5f, 0.72f);
                    Puff(new Vector3(pb.center.x + Mathf.Cos(a) * rr * ex, bankBase + r * Rng(s, 6, 0.3f, 0.55f), pb.center.z + Mathf.Sin(a) * rr * ez), r, s, kind);
                }
                // the heap: bigger and higher toward the middle (a storm's narrower and far higher)
                float pull = kind == Kind.Storm ? 0.55f : 0.8f;
                int heap = kind == Kind.Storm ? Mathf.Min(48, n + 10) : n;
                for (int k = 0; k < heap; k++)
                {
                    int s = ps + 5000 + k * 17;
                    float a = Rng(s, 3, 0f, Mathf.PI * 2f), rr = Mathf.Sqrt(R01(s, 4)) * pull, mid = 1f - rr * rr / (pull * pull) * 0.8f;
                    float r = rb * (0.55f + 0.6f * mid) * Rng(s, 5, 0.85f, 1.15f);
                    float y = baseY + r * 0.35f + mid * Mathf.Max(0f, top - baseY - r) * (kind == Kind.Storm ? Rng(s, 6, 0.1f, 0.95f) : Rng(s, 6, 0.3f, 0.85f));
                    Puff(new Vector3(pb.center.x + Mathf.Cos(a) * rr * ex, y, pb.center.z + Mathf.Sin(a) * rr * ez), r, s, kind);
                }
            }

            if (kind == Kind.Storm)
            {
                Vector3 c0 = Vector3.zero;
                foreach (var p in pieces) c0 += p.center;
                c0 /= pieces.Count;
                // the tower: rings of big puffs from the base up, narrowing a little as it climbs and leaning downwind
                const int Levels = 11;
                for (int j = 0; j < Levels; j++)
                {
                    float f = j / (Levels - 1f), ring = T * (1f - 0.3f * f);
                    int per = j == 0 ? 7 : 5;
                    for (int k = 0; k < per; k++)
                    {
                        int s = seed * 41 + j * 97 + k * 13;
                        float a = (k + Rng(s, 3, -0.35f, 0.35f)) / per * Mathf.PI * 2f, rr = ring * Rng(s, 4, 0.25f, 0.75f);
                        float r = T * Rng(s, 5, 0.42f, 0.62f) * (j == 0 ? 0.85f : 1f);
                        var c = new Vector3(c0.x + Mathf.Cos(a) * rr, bankBase + r * 0.45f + f * (towerH * 0.82f), c0.z + Mathf.Sin(a) * rr) + wind * (f * f * T * 0.6f);
                        Puff(c, r, s, Kind.Storm);
                    }
                }
                // the anvil: a wide flat shelf of stretched puffs at the top, cut flat underneath, swept downwind
                for (int k = 0; k < 22; k++)
                {
                    int s = seed * 53 + k * 19;
                    float a = Rng(s, 3, 0f, Mathf.PI * 2f), rr = Mathf.Sqrt(R01(s, 4)) * T * 2.4f;
                    Vector3 c = new Vector3(c0.x + Mathf.Cos(a) * rr, top - T * Rng(s, 6, 0.1f, 0.35f), c0.z + Mathf.Sin(a) * rr) + wind * T * 1.4f;
                    Puff(c, T * Rng(s, 5, 0.38f, 0.55f), s, Kind.Storm, Rng(s, 7, 1.5f, 2.2f), top - T * 0.6f);
                }
                // rain under it: wide shafts falling a long way, thinning out before the ground if the ground is far
                int shafts = 3 + (int)(R01(seed, 8) * 1.99f);
                for (int k = 0; k < shafts; k++)
                {
                    int s = seed * 61 + k * 23;
                    float a = (k + Rng(s, 3, -0.3f, 0.3f)) / shafts * Mathf.PI * 2f, rr = Rng(s, 4, 0.2f, 0.6f) * T;
                    var foot = new Vector3(c0.x + Mathf.Cos(a) * rr, bankBase, c0.z + Mathf.Sin(a) * rr);
                    float ground = Physics.Raycast(foot + Vector3.up, Vector3.down, out var hit, 8000f) ? hit.point.y : bankBase - 2500f;
                    float h = Mathf.Clamp(bankBase + 30f - ground, 200f, 1800f);
                    float w = T * Rng(s, 5, 0.55f, 0.85f);
                    Puff(new Vector3(foot.x, bankBase + 30f - h * 0.5f, foot.z), h * 0.5f, s, Kind.Rain, w / (h * 0.5f), float.MinValue);
                }
            }

            count = made;
            var mesh = new Mesh { name = $"Toon {kind} bank" };
            if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetUVs(2, uv2);
            mesh.SetTriangles(tris, 0, false);
            box.Expand(maxR * 2f);
            float span = KindDrift[kind == Kind.Rain ? 1 : (int)kind].y;
            box.Expand(new Vector3(span * 1.1f, 0f, span * 1.1f));   // (it drifts half its span either way)
            mesh.bounds = box;
            mesh.UploadMeshData(true);
            return mesh;
        }
    }
}
