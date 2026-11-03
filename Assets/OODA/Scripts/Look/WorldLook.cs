using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Ooda.Look
{
    /// <summary>The light a mission is flown in.</summary>
    public enum Mood { Afternoon, OvercastMorning }

    /// <summary>
    /// The look of the world: the sun, the sky, the ambient light, the haze and the colour grade (one global
    /// post-processing volume made in code). Every scene gets the afternoon look as it loads; the turn battle
    /// asks for its mission's mood on each restart (<see cref="Apply"/>). All the numbers are in <see cref="For"/>.
    /// </summary>
    public static class WorldLook
    {
        struct Settings
        {
            public float sunPitch, sunYaw, sunIntensity, shadowStrength;
            public Color sunColor;
            public Color ambientSky, ambientEquator, ambientGround;
            public Color fogColor;
            public float fogDensity;                 // exponential squared, per metre
            public float skyThickness, skyExposure, sunSize;
            public Color skyTint, skyGround;
            public bool sunDisk;
            public float exposure, contrast, saturation, temperature, tint;
            public Vector4 shadows, highlights;      // shadows, midtones, highlights balance: rgb tint, w offset
            public float bloomThreshold, bloomIntensity;
            public float vignette;
        }

        static Settings For(Mood mood)
        {
            switch (mood)
            {
                case Mood.OvercastMorning:
                    return new Settings
                    {
                        sunPitch = 52f, sunYaw = -20f, sunIntensity = 0.85f, shadowStrength = 0.45f,
                        sunColor = new Color(0.90f, 0.94f, 1.00f),
                        ambientSky = new Color(0.66f, 0.70f, 0.76f), ambientEquator = new Color(0.58f, 0.60f, 0.62f), ambientGround = new Color(0.34f, 0.34f, 0.33f),
                        fogColor = new Color(0.70f, 0.74f, 0.78f), fogDensity = 0.000085f,
                        skyThickness = 1.7f, skyExposure = 0.95f, sunSize = 0.03f, sunDisk = false,
                        skyTint = new Color(0.58f, 0.60f, 0.63f), skyGround = new Color(0.42f, 0.42f, 0.42f),
                        exposure = 0f, contrast = 4f, saturation = -24f, temperature = -8f, tint = 0f,
                        shadows = new Vector4(0.96f, 0.99f, 1.06f, 0f), highlights = new Vector4(1f, 1f, 1f, 0f),
                        bloomThreshold = 1.2f, bloomIntensity = 0.15f,
                        vignette = 0.24f,
                    };
                default:   // late afternoon: a low warm sun, blue shadows and blue distance
                    return new Settings
                    {
                        sunPitch = 30f, sunYaw = -35f, sunIntensity = 1.55f, shadowStrength = 0.85f,
                        sunColor = new Color(1.00f, 0.92f, 0.80f),
                        ambientSky = new Color(0.56f, 0.66f, 0.84f), ambientEquator = new Color(0.60f, 0.60f, 0.60f), ambientGround = new Color(0.30f, 0.28f, 0.25f),
                        fogColor = new Color(0.69f, 0.78f, 0.90f), fogDensity = 0.000055f,
                        skyThickness = 0.9f, skyExposure = 1.15f, sunSize = 0.035f, sunDisk = true,
                        skyTint = new Color(0.50f, 0.56f, 0.66f), skyGround = new Color(0.40f, 0.38f, 0.35f),
                        exposure = 0f, contrast = 10f, saturation = -12f, temperature = 10f, tint = 2f,
                        shadows = new Vector4(0.93f, 0.97f, 1.10f, 0f), highlights = new Vector4(1.05f, 1.01f, 0.95f, 0f),
                        bloomThreshold = 1.15f, bloomIntensity = 0.25f,
                        vignette = 0.2f,
                    };
            }
        }

        static VolumeProfile profile;
        static Tonemapping tone;
        static ColorAdjustments color;
        static WhiteBalance white;
        static ShadowsMidtonesHighlights smh;
        static Bloom bloom;
        static Vignette vignette;
        static Volume volume;
        static Material sky;
        static int appliedScene = -1;
        static bool hooked;

        /// <summary>The mood last applied.</summary>
        public static Mood Current { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!hooked) { SceneManager.sceneLoaded += (s, m) => DefaultFor(s); hooked = true; }
            DefaultFor(SceneManager.GetActiveScene());
        }

        // A scene the battle has already set a mood in keeps it.
        static void DefaultFor(Scene s)
        {
            if (s.handle != appliedScene) Apply(Mood.Afternoon);
        }

        /// <summary>Light the active scene in this mood.</summary>
        public static void Apply(Mood mood)
        {
            var st = For(mood);
            Current = mood;
            appliedScene = SceneManager.GetActiveScene().handle;

            Light sun = null;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { sun = l; break; }
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(st.sunPitch, st.sunYaw, 0f);
                sun.intensity = st.sunIntensity;
                sun.color = st.sunColor;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = st.shadowStrength;
                RenderSettings.sun = sun;
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = st.ambientSky;
            RenderSettings.ambientEquatorColor = st.ambientEquator;
            RenderSettings.ambientGroundColor = st.ambientGround;

            // haze that thickens with distance instead of a hard linear start: near is clear, far ridges go blue
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = st.fogDensity;
            RenderSettings.fogColor = st.fogColor;

            if (sky == null)
            {
                var shader = Shader.Find("Skybox/Procedural");
                if (shader != null) sky = new Material(shader) { name = "WorldLookSky", hideFlags = HideFlags.DontSave };
            }
            if (sky != null)
            {
                sky.SetFloat("_SunDisk", st.sunDisk ? 2f : 0f);
                sky.SetFloat("_SunSize", st.sunSize);
                sky.SetFloat("_SunSizeConvergence", 6f);
                sky.SetFloat("_AtmosphereThickness", st.skyThickness);
                sky.SetColor("_SkyTint", st.skyTint);
                sky.SetColor("_GroundColor", st.skyGround);
                sky.SetFloat("_Exposure", st.skyExposure);
                RenderSettings.skybox = sky;
            }
            DynamicGI.UpdateEnvironment();

            MakeProfile();
            tone.mode.value = TonemappingMode.Neutral;
            color.postExposure.value = st.exposure;
            color.contrast.value = st.contrast;
            color.saturation.value = st.saturation;
            white.temperature.value = st.temperature;
            white.tint.value = st.tint;
            smh.shadows.value = st.shadows;
            smh.highlights.value = st.highlights;
            bloom.threshold.value = st.bloomThreshold;
            bloom.intensity.value = st.bloomIntensity;
            bloom.scatter.value = 0.6f;
            vignette.intensity.value = st.vignette;
            vignette.smoothness.value = 0.45f;

            if (volume == null)
            {
                var go = new GameObject("WorldLook");
                volume = go.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 10f;
            }
            volume.sharedProfile = profile;

            // post-processing on every camera that draws to the screen (not the stick and throttle's little cameras)
            foreach (var cam in Camera.allCameras)
                if (cam.targetTexture == null) cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        }

        static void MakeProfile()
        {
            if (profile != null) return;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "WorldLook";
            profile.hideFlags = HideFlags.DontSave | HideFlags.DontUnloadUnusedAsset;
            tone = profile.Add<Tonemapping>(true);
            color = profile.Add<ColorAdjustments>(true);
            white = profile.Add<WhiteBalance>(true);
            smh = profile.Add<ShadowsMidtonesHighlights>(true);
            bloom = profile.Add<Bloom>(true);
            vignette = profile.Add<Vignette>(true);
            color.colorFilter.overrideState = false;
            color.hueShift.overrideState = false;
            smh.midtones.overrideState = false;
            smh.shadowsStart.overrideState = smh.shadowsEnd.overrideState = false;
            smh.highlightsStart.overrideState = smh.highlightsEnd.overrideState = false;
            vignette.color.overrideState = vignette.center.overrideState = vignette.rounded.overrideState = false;
            bloom.tint.overrideState = bloom.clamp.overrideState = bloom.dirtTexture.overrideState = bloom.dirtIntensity.overrideState = false;
        }
    }
}
