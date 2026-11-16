using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Ooda.Fx
{
    /// <summary>
    /// The finish on the picture, the same on every scene (put on as each loads, like ToonSky, so the builders and the
    /// saved scenes stay as they are):
    /// - the grade: a global post-processing volume made here (a neutral tone curve so snow, cloud and white paint keep
    ///   their shape instead of clipping, a soft bloom on what is brighter than white: sun glints, flames, tracers,
    ///   sunlit snow; warm highlights over cool shadows, a touch of contrast and a light vignette), and post-processing
    ///   switched on for the cameras that draw the game;
    /// - the light from the sky: a three-colour ambient (sky blue from above, the horizon's pale blue from the sides, a
    ///   warm green bounce off the land from below) in place of the default skybox's grey, so a shaded flank has colour;
    /// - the air: an aerial perspective in every toon shader (Fx/ToonAtmosphere.hlsl), thick in the valleys and thin up
    ///   high, hazing toward the horizon's colour and toward a warm glow under the sun. The scene's own distance fog is
    ///   pushed further out, since the air now does the near work.
    /// - the quality: Low, Medium, High (F9 steps through them; kept between runs).
    /// All the numbers are here. <see cref="Enabled"/> off (or -nolook on the command line) leaves the picture as it was.
    /// </summary>
    public static class ToonLook
    {
        /// <summary>Off: no grade, no ambient, no air (for comparing).</summary>
        public static bool Enabled = true;

        public enum Quality { Low = 0, Medium = 1, High = 2 }

        const string PrefKey = "ooda.gfx.quality";

        // ---- the light from the sky (before the shaders' own _Ambient). Kept low: the scenes had next to none, and the toon
        // shade wants to stay deep; this only gives it colour (blue on what looks up, olive on what looks down)
        public static readonly Color AmbientSky = Hex("#4f7fd0"), AmbientHorizon = Hex("#5c7c9c"), AmbientGround = Hex("#6f7038");

        // ---- the air
        /// <summary>How much of the view the air hides per metre in the valleys (0.00007: about 40% at 8 km on the valley floor, a quarter from 1,500 m up).</summary>
        public const float AirDensity = 0.000070f;
        /// <summary>The height (m) over which the air thins to a third.</summary>
        public const float AirScaleHeight = 2600f;
        /// <summary>The most the air ever hides, so the far range is pale, never gone (the fog takes the last of it).</summary>
        public const float AirMost = 0.9f;
        public const float AirBaseHeight = 0f;
        /// <summary>The scene's distance fog with the air on: from here to there (m).</summary>
        public const float FogStart = 9000f, FogEnd = 34000f;

        static Quality quality = Quality.High;
        static bool loaded;
        static Volume volume;
        static Bloom bloom;
        static Tonemapping tone;
        static ColorAdjustments adjust;
        static ShadowsMidtonesHighlights smh;
        static WhiteBalance balance;
        static Vignette vignette;

        // what the pipeline asset had before a lower quality changed it (an asset changed in the editor's play mode stays changed)
        static UniversalRenderPipelineAsset tuned;
        static int keepMsaa; static float keepShadow, keepScale;

        public static Quality Level
        {
            get { Load(); return quality; }
            set { Load(); quality = value; PlayerPrefs.SetInt(PrefKey, (int)value); ApplyQuality(); }
        }

        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            quality = (Quality)Mathf.Clamp(PlayerPrefs.GetInt(PrefKey, (int)Quality.High), 0, 2);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            // (statics outlive a play session when the editor does not reload the domain)
            loaded = false; volume = null; bloom = null; tone = null; adjust = null; smh = null; balance = null; vignette = null; tuned = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            ReadArgs();
            SceneManager.sceneLoaded -= OnLoaded;
            SceneManager.sceneLoaded += OnLoaded;
            Application.quitting -= Restore;
            Application.quitting += Restore;
        }

        static void OnLoaded(Scene scene, LoadSceneMode mode) => Apply();

        static bool argsRead;
        static void ReadArgs()
        {
            if (argsRead) return;
            argsRead = true;
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-nolook") >= 0) Enabled = false;
        }

        /// <summary>The look on a camera that draws into a texture (the editor's shot tools: the game's own cameras get it by themselves).</summary>
        public static void Dress(Camera cam)
        {
            if (cam == null || !Enabled) return;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.dithering = true;
        }

        /// <summary>The look on the scene that is open now (safe to call again; also clears it when the look is off).</summary>
        public static void Apply()
        {
            Load();
            ReadArgs();
            if (!Enabled)
            {
                Shader.SetGlobalVector("_ToonAtmoParams", Vector4.zero);
                if (volume != null) volume.enabled = false;
                return;
            }

            // the light from the sky
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = AmbientSky;
            RenderSettings.ambientEquatorColor = AmbientHorizon;
            RenderSettings.ambientGroundColor = AmbientGround;

            // the air: the horizon's colour, and a warm glow toward the sun
            Vector3 toSun = new Vector3(0.4f, 0.6f, 0.3f).normalized;
            var sun = RenderSettings.sun;
            if (sun == null)
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional && (sun == null || l.intensity > sun.intensity)) sun = l;
            if (sun != null) toSun = -sun.transform.forward;
            Shader.SetGlobalVector("_ToonAtmoSunDir", toSun);
            Shader.SetGlobalVector("_ToonAtmoParams", new Vector4(AirDensity, 1f / AirScaleHeight, AirMost, AirBaseHeight));
            Shader.SetGlobalColor("_ToonAtmoColor", Color.Lerp(ToonSky.Horizon, Color.white, 0.12f));
            var warm = Color.Lerp(ToonSky.Horizon, ToonSky.Glow, 0.55f); warm.a = 5f;   // (a: how tight the glow is round the sun)
            Shader.SetGlobalColor("_ToonAtmoSunColor", warm);
            if (RenderSettings.fog && RenderSettings.fogMode == FogMode.Linear && RenderSettings.fogEndDistance < FogEnd)
            {
                RenderSettings.fogStartDistance = FogStart;
                RenderSettings.fogEndDistance = FogEnd;
            }

            EnsureVolume();
            ApplyQuality();
            Cameras();
        }

        // ---------------------------------------------------------------------------------------------------------
        // the grade

        static void EnsureVolume()
        {
            if (volume != null) { volume.enabled = true; return; }
            // (in the editor's shot tools it must not be saved into the open scene)
            var go = new GameObject("ToonLook") { hideFlags = Application.isPlaying ? HideFlags.HideInHierarchy : HideFlags.HideAndDontSave };
            if (Application.isPlaying) Object.DontDestroyOnLoad(go);
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            p.name = "ToonLook (made at run time)";
            volume.sharedProfile = p;

            // a neutral curve: only rolls the brights off (sunlit snow and white paint are past white before it), no film look
            tone = p.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);

            // a soft glow on what is brighter than white, no more: a glint, a flame, a tracer, a snowfield in the sun
            bloom = p.Add<Bloom>(true);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.5f);
            bloom.scatter.Override(0.65f);
            bloom.clamp.Override(6f);
            bloom.tint.Override(new Color(1f, 0.97f, 0.92f));
            bloom.highQualityFiltering.Override(true);

            adjust = p.Add<ColorAdjustments>(true);
            adjust.postExposure.Override(0.18f);    // the curve takes a little off the upper tones: give it back
            adjust.contrast.Override(12f);
            adjust.saturation.Override(8f);

            // warm light, cool shade: the painter's rule the toon shaders follow, carried through the whole frame
            smh = p.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(new Vector4(0.93f, 0.97f, 1.10f, 0f));
            smh.midtones.Override(new Vector4(1f, 1f, 1f, 0f));
            smh.highlights.Override(new Vector4(1.06f, 1.01f, 0.93f, 0f));

            balance = p.Add<WhiteBalance>(true);
            balance.temperature.Override(3f);

            // a light frame round the picture, in the sky's own navy rather than black
            vignette = p.Add<Vignette>(true);
            vignette.color.Override(new Color(0.05f, 0.09f, 0.22f));
            vignette.intensity.Override(0.26f);
            vignette.smoothness.Override(0.5f);

            if (Application.isPlaying) go.AddComponent<Keeper>();
        }

        /// <summary>The steps of quality: what the picture costs. Low is for a laptop's built-in graphics.</summary>
        static void ApplyQuality()
        {
            if (bloom != null)
            {
                bloom.active = quality != Quality.Low;
                bloom.highQualityFiltering.Override(quality == Quality.High);
            }
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null) return;
            if (tuned != urp)
            {
                Restore();
                tuned = urp; keepMsaa = urp.msaaSampleCount; keepShadow = urp.shadowDistance; keepScale = urp.renderScale;
            }
            switch (quality)
            {
                case Quality.High: urp.msaaSampleCount = keepMsaa; urp.shadowDistance = keepShadow; urp.renderScale = keepScale; break;
                case Quality.Medium: urp.msaaSampleCount = Mathf.Min(keepMsaa, 2); urp.shadowDistance = Mathf.Min(keepShadow, 1800f); urp.renderScale = keepScale; break;
                default: urp.msaaSampleCount = 1; urp.shadowDistance = Mathf.Min(keepShadow, 900f); urp.renderScale = Mathf.Min(keepScale, 0.85f); break;
            }
            Cameras();
        }

        static void Restore()
        {
            if (tuned == null) return;
            tuned.msaaSampleCount = keepMsaa; tuned.shadowDistance = keepShadow; tuned.renderScale = keepScale;
            tuned = null;
        }

        /// <summary>Post-processing on for the cameras that draw the game to the screen (not the gauges' or the portals'
        /// own cameras, which draw into textures). Without multisampling (Low) the edges get FXAA instead.</summary>
        static void Cameras()
        {
            foreach (var cam in Camera.allCameras)
            {
                if (cam == null || cam.targetTexture != null || cam.cameraType != CameraType.Game) continue;
                if (!cam.CompareTag("MainCamera") && cam.GetComponent<Ooda.Turn.TurnCamera>() == null && cam.GetComponent<Ooda.CameraSystem.CameraRig>() == null) continue;
                var data = cam.GetUniversalAdditionalCameraData();
                if (data == null) continue;
                if (!data.renderPostProcessing) data.renderPostProcessing = Enabled;
                var aa = quality == Quality.Low ? AntialiasingMode.FastApproximateAntialiasing : AntialiasingMode.None;
                if (data.antialiasing != aa) data.antialiasing = aa;
                data.dithering = true;   // (no banding in the sky's long gradient)
            }
        }

        /// <summary>Rides with the volume: finds cameras made after the scene loaded, and steps the quality on F9.</summary>
        sealed class Keeper : MonoBehaviour
        {
            float next, toastUntil;
            GUIStyle style;

            void Update()
            {
                if (!Enabled) return;
                if (Time.unscaledTime >= next) { next = Time.unscaledTime + 0.5f; Cameras(); }
                var kb = Keyboard.current;
                if (kb != null && kb.f9Key.wasPressedThisFrame)
                {
                    Level = (Quality)(((int)Level + 1) % 3);
                    toastUntil = Time.unscaledTime + 2.5f;
                }
            }

            void OnGUI()
            {
                if (Time.unscaledTime > toastUntil) return;
                style ??= new GUIStyle(GUI.skin.box) { fontSize = Mathf.Max(14, Screen.height / 45), alignment = TextAnchor.MiddleCenter };
                string text = $"Graphics: {Level}  (F9)";
                var size = style.CalcSize(new GUIContent(text));
                GUI.Box(new Rect((Screen.width - size.x - 24) * 0.5f, Screen.height * 0.08f, size.x + 24, size.y + 12), text, style);
            }
        }
    }
}
