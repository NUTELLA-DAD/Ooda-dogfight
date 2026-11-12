using System.Collections.Generic;
using Ooda.Turn.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ooda.Fx
{
    /// <summary>
    /// Draws the cloud sea (Turn/Core/Skyscape.cs) in the toon look of the island's clouds: every puff of every bank is a
    /// quad for ToonCloud.shader (round, lobed, inked, cel-shaded), the same vertex layout ToonSky's banks use, on a copy of
    /// the cloud material with _Still on, so these clouds stand where Skyscape says they are. Made at run time when the
    /// battle starts (no scene or asset to merge): the banks are gathered into squares of ChunkM metres, one mesh (two draw
    /// calls, line and fill) a square, so what is behind the camera is culled.
    ///
    /// Flying in: Skyscape.Density at the camera turns the fog down to a few hundred metres and the sky and the fog to the
    /// cloud's own white, eased in and out over a fraction of a second, so punching through a tower whites the screen out
    /// and your foe vanishes into it, then the sky snaps back as you come out the other side. Shown only while the fight is
    /// over the cloud sea (Show): everywhere else the scene's fog and sky are left as they were.
    /// </summary>
    public class CloudScape : MonoBehaviour
    {
        public const float ChunkM = 3000f;
        /// <summary>How far you see, deep in cloud (m): the fog's far end there, the fog starting at 60% of it, so your own
        /// plane (the chase camera sits 160 to 180 m behind it) shows through the white while anything past the veil is gone.</summary>
        public const float WhiteoutM = 250f;
        /// <summary>How fast the white comes and goes (1/s).</summary>
        public const float EaseRate = 7f;
        /// <summary>The colour of the inside of a cloud: between the cumulus' lit and shade tones (ToonSky).</summary>
        /// <summary>How much of the screen's height a puff may fill before it thins away (ToonCloud's _NearFill; 0.6 on the
        /// island's clouds): a tower looms up big before you are in it.</summary>
        public const float NearFill = 1.3f;
        /// <summary>How far out the veil is drawn (m): past it, deep in cloud, nothing shows.</summary>
        public const float VeilM = 260f;
        public static readonly Color Inside = new Color(0.90f, 0.91f, 0.99f);

        static CloudScape made;
        Material mat;
        readonly List<Mesh> meshes = new List<Mesh>();
        readonly List<GameObject> squares = new List<GameObject>();
        // the veil: a sphere of the cloud's white round the camera, VeilM out, drawn over everything past it (the sky, the
        // sea, far planes, the far clouds) and nothing nearer (your own plane), thicker the deeper in you are
        GameObject veil;
        Material veilMat;
        Camera viewer;
        float FogAmount = 0.7f;
        bool shown;

        // the scene's own fog and sky, kept while the white is on so they can be put back exactly
        float inCloud;
        bool kept;
        float keepStart, keepEnd;
        Color keepFog;
        FogMode keepMode;
        bool keepFogOn;
        Material keepSky, whiteSky;
        static readonly string[] SkyColours = { "_Zenith", "_High", "_Horizon", "_Glow", "_Ground", "_Sun", "_SunRing" };

        /// <summary>The cloud sea in this scene, if it has been made.</summary>
        public static CloudScape Existing => made != null ? made : (made = FindFirstObjectByType<CloudScape>());

        /// <summary>The cloud sea in this scene, made if it isn't there yet (hidden until Show).</summary>
        public static CloudScape Ensure()
        {
            if (Existing != null) return made;
            var go = new GameObject("Cloud Sea");
            made = go.AddComponent<CloudScape>();
            made.Build(Skyscape.Sky);
            made.SetVisible(false);
            return made;
        }

        /// <summary>Show the clouds (and let them white the screen out) while a fight is over the cloud sea, or hide them.
        /// cam is the camera whose place decides the white (the main camera if null).</summary>
        public void Show(bool on, Camera cam)
        {
            viewer = cam;
            if (on == shown) return;
            shown = on;
            SetVisible(on);
            if (!on) { inCloud = 0f; Restore(); }
        }

        void SetVisible(bool on)
        {
            foreach (var go in squares) if (go != null) go.SetActive(on);
            if (!on && veil != null) veil.SetActive(false);
        }

        void OnDisable() => Restore();

        void OnDestroy()
        {
            Restore();
            if (made == this) made = null;
            foreach (var m in meshes) if (m != null) DestroyImmediate(m);
            if (mat != null) DestroyImmediate(mat);
            if (whiteSky != null) DestroyImmediate(whiteSky);
            if (veilMat != null) DestroyImmediate(veilMat);
        }

        // ---------- building ----------

        void Build(Skyscape s)
        {
            var src = Resources.Load<Material>("ToonCloud");
            if (src == null) { Debug.LogWarning("OODA: no Resources/ToonCloud material; no cloud sea."); return; }
            mat = new Material(src) { name = "ToonCloud (cloud sea)" };
            mat.SetFloat("_Still", 1f);
            FogAmount = mat.HasProperty("_FogAmount") ? mat.GetFloat("_FogAmount") : 0.7f;
            mat.SetFloat("_NearFill", NearFill);

            // each bank into the square its heart is in
            var squares = new Dictionary<long, List<int>>();
            for (int b = 0; b < s.banks.Length; b++)
            {
                var bk = s.banks[b];
                long key = ((long)Mathf.FloorToInt(bk.x / ChunkM) << 32) ^ (uint)Mathf.FloorToInt(bk.z / ChunkM);
                if (!squares.TryGetValue(key, out var list)) squares[key] = list = new List<int>();
                list.Add(b);
            }
            int puffs = 0;
            foreach (var kv in squares)
            {
                var mesh = SquareMesh(s, kv.Value, out int n);
                if (mesh == null) continue;
                meshes.Add(mesh);
                puffs += n;
                var go = new GameObject($"Clouds {kv.Key:X}");
                go.transform.SetParent(transform, false);
                this.squares.Add(go);   // (the field, not the local of the same name above)
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            Debug.Log($"OODA: cloud sea: {s.banks.Length} banks ({s.towers} towers, {s.storms} of them storms; {s.heaps} heaps) of {puffs} puffs in {meshes.Count} squares.");
        }

        static uint Hash(uint x) { x ^= x >> 16; x *= 0x7feb352dU; x ^= x >> 15; x *= 0x846ca68bU; x ^= x >> 16; return x; }
        static float R01(int a, int b) => (Hash((uint)a * 73856093U ^ Hash((uint)b * 19349663U + 0x9e3779b9U)) & 0xffffff) / 16777216f;

        /// <summary>One square's banks as quads for ToonCloud.shader: four corners a puff, each holding the puff's middle
        /// (uv0: corner, radius, shape; uv1: base, top, lobes, kind; uv2: stretch, phase, how far out; uv3: the bank's heart).</summary>
        static Mesh SquareMesh(Skyscape s, List<int> bankIds, out int count)
        {
            var verts = new List<Vector3>(2048); var uv0 = new List<Vector4>(2048); var uv1 = new List<Vector4>(2048);
            var uv2 = new List<Vector4>(2048); var uv3 = new List<Vector3>(2048); var tris = new List<int>(3072);
            var box = new Bounds();
            bool any = false;
            count = 0;
            foreach (int b in bankIds)
            {
                var bk = s.banks[b];
                var heart = new Vector3(bk.x, bk.y, bk.z);
                float phase = R01(b, 7), far = 1f;
                for (int k = bk.first; k < bk.first + bk.count; k++)
                {
                    var p = s.puffs[k];
                    far = Mathf.Max(far, Vector3.Distance(new Vector3(p.x, p.y, p.z), heart));
                }
                for (int k = bk.first; k < bk.first + bk.count; k++)
                {
                    var p = s.puffs[k];
                    var c = new Vector3(p.x, p.y, p.z);
                    float shape = R01(k, 1), lobes = 3 + (int)(R01(k, 2) * 2.99f);
                    float order = p.kind == Skyscape.Rain ? 0f : Vector3.Distance(c, heart) / far;
                    int m = verts.Count;
                    for (int i = 0; i < 4; i++)
                    {
                        verts.Add(c);
                        uv0.Add(new Vector4(i == 0 || i == 3 ? -1 : 1, i < 2 ? -1 : 1, p.r, shape));
                        uv1.Add(new Vector4(p.cutY, bk.topY, lobes, p.kind));
                        uv2.Add(new Vector4(p.stretch, phase, order, 0));
                        uv3.Add(heart);
                    }
                    tris.Add(m); tris.Add(m + 2); tris.Add(m + 1); tris.Add(m); tris.Add(m + 3); tris.Add(m + 2);
                    // (a rain shaft stands r tall and r * stretch wide; a puff billows a little past its radius)
                    var size = p.kind == Skyscape.Rain ? new Vector3(p.r * p.stretch, p.r, p.r * p.stretch) * 2.2f
                                                       : new Vector3(p.r * p.stretch, p.r, p.r * p.stretch) * 2.6f;
                    var pb = new Bounds(c, size);
                    if (!any) { box = pb; any = true; } else box.Encapsulate(pb);
                    count++;
                }
            }
            if (!any) return null;
            var mesh = new Mesh { name = "Cloud sea square" };
            if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetUVs(2, uv2);
            mesh.SetUVs(3, uv3);
            mesh.SetTriangles(tris, 0, false);
            mesh.bounds = box;
            mesh.UploadMeshData(true);
            return mesh;
        }

        // ---------- in the cloud ----------

        void LateUpdate() => Follow(false);

        /// <summary>Straight to the white the camera is in now, without easing (for still pictures).</summary>
        public void Settle() => Follow(true);

        void Follow(bool snap)
        {
            if (!shown) return;
            var cam = viewer != null && viewer.isActiveAndEnabled ? viewer : Camera.main;
            float target = 0f;
            if (cam != null)
            {
                var p = cam.transform.position;
                target = Skyscape.Sky.Density(p.x, p.y, p.z);
            }
            // (real time: the battle stops its clock while you plan, and the view still moves)
            inCloud = snap ? target : inCloud + (target - inCloud) * (1f - Mathf.Exp(-EaseRate * Time.unscaledDeltaTime));
            if (inCloud < 0.002f && target <= 0f) { inCloud = 0f; Restore(); return; }
            Apply(inCloud);
            Veil(cam, inCloud);
        }

        void Keep()
        {
            if (kept) return;
            kept = true;
            keepFogOn = RenderSettings.fog; keepMode = RenderSettings.fogMode;
            keepStart = RenderSettings.fogStartDistance; keepEnd = RenderSettings.fogEndDistance; keepFog = RenderSettings.fogColor;
            // (the white goes on a copy of the sky, so the scene's own sky material, maybe an asset, is never touched)
            keepSky = RenderSettings.skybox;
            if (keepSky != null)
            {
                if (whiteSky == null || whiteSky.shader != keepSky.shader) { if (whiteSky != null) DestroyImmediate(whiteSky); whiteSky = new Material(keepSky) { name = "Sky (in cloud)" }; }
                else whiteSky.CopyPropertiesFromMaterial(keepSky);
                RenderSettings.skybox = whiteSky;
            }
        }

        /// <summary>The white, k deep (0 clear, 1 deep in): the fog closes in (its far end from the scene's own down to
        /// WhiteoutM, evenly on a log scale, so it tightens fast as you go in) and takes the cloud's colour, and the sky
        /// whites over too (it isn't fogged).</summary>
        void Apply(float k)
        {
            Keep();
            bool linear = keepFogOn && keepMode == FogMode.Linear;
            float end0 = linear ? keepEnd : 24000f, start0 = linear ? keepStart : 2000f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            float kk = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k / 0.8f));   // (all the way in by 0.8: well inside, it is white)
            RenderSettings.fogEndDistance = end0 * Mathf.Pow(WhiteoutM / end0, kk);
            // (the island's clouds let 30% of their ink show through the fog; in here the fog takes them whole)
            if (mat != null) mat.SetFloat("_FogAmount", Mathf.Lerp(FogAmount, 1f, kk));
            RenderSettings.fogStartDistance = Mathf.Min(start0 * (1f - k), RenderSettings.fogEndDistance * 0.6f);
            RenderSettings.fogColor = Color.Lerp(keepFog, Inside, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k * 1.6f)));
            if (whiteSky != null && keepSky != null)
            {
                float w = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k * 1.3f));
                foreach (var name in SkyColours)
                    if (keepSky.HasProperty(name)) whiteSky.SetColor(name, Color.Lerp(keepSky.GetColor(name), Inside, w));
            }
        }

        /// <summary>The veil round the camera, k deep: none at the edge of a cloud, solid white past VeilM well inside.</summary>
        void Veil(Camera cam, float k)
        {
            float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.8f, k));
            if (cam == null || a <= 0.001f) { if (veil != null) veil.SetActive(false); return; }
            if (veil == null)
            {
                veil = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                veil.name = "Cloud veil";
                DestroyImmediate(veil.GetComponent<Collider>());
                veil.transform.SetParent(transform, false);
                var sh = Shader.Find("Sprites/Default");
                veilMat = new Material(sh) { name = "Cloud veil", renderQueue = 2995 };   // (after the water (2990), before the lines, smoke and tracers: your plan still shows over it)
                var r = veil.GetComponent<MeshRenderer>();
                r.sharedMaterial = veilMat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            veil.SetActive(true);
            veil.transform.position = cam.transform.position;
            veil.transform.localScale = Vector3.one * (2f * VeilM);   // (the sphere primitive is 1 across)
            veilMat.color = new Color(Inside.r, Inside.g, Inside.b, a);
        }

        void Restore()
        {
            if (veil != null) veil.SetActive(false);
            if (mat != null) mat.SetFloat("_FogAmount", FogAmount);
            if (!kept) return;
            kept = false;
            RenderSettings.fog = keepFogOn; RenderSettings.fogMode = keepMode;
            RenderSettings.fogStartDistance = keepStart; RenderSettings.fogEndDistance = keepEnd; RenderSettings.fogColor = keepFog;
            if (RenderSettings.skybox == whiteSky) RenderSettings.skybox = keepSky;
            keepSky = null;
        }
    }
}
