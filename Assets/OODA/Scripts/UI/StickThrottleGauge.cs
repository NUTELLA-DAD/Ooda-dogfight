using UnityEngine;

namespace Ooda.UI
{
    /// <summary>
    /// The flight stick and the throttle quadrant drawn in the bottom corners of the turn screen, so you see how far
    /// you are pulling while the ghost shows where it takes the plane. Each model sits far below the world on its own
    /// layer, and its own camera renders it into a texture with a clear background for the HUD to draw.
    /// The stick's base stays put, its straight shaft pivots at the base and the rubber boot bends between them
    /// (the mesh is skinned to the two bones by OODA > Build Stick and Throttle); the throttle lever swings on its hub.
    /// </summary>
    public class StickThrottleGauge : MonoBehaviour
    {
        /// <summary>The layer only the gauge cameras see.</summary>
        public const int Layer = 31;
        /// <summary>How far over the stick is at the ring (the hardest clean turn); the overpull band takes it the rest of the way.</summary>
        public const float RingTravel = 0.8f;

        [Header("Parts (set by the builder)")]
        public Transform stickRoot, stickShaft, throttleRoot, throttleLever;
        public Renderer stick, throttle;

        [Header("Travel")]
        public float stickTravelDeg = 20f;
        [Tooltip("The lever from the model's own pose, degrees forward: at idle and at full throttle.")]
        public float throttleIdleDeg = -28f, throttleFullDeg = 30f;
        [Tooltip("How fast the shown controls catch up (1/s).")]
        public float follow = 14f;

        [Header("Look")]
        public Color overpullColor = new Color(1f, 0.15f, 0.1f, 1f);
        public int textureHeight = 512;
        public Vector3 stickCamera = new Vector3(-0.75f, 1.05f, -2.2f), throttleCamera = new Vector3(0.75f, 1.05f, -2.2f);
        public Vector3 lookAt = new Vector3(0f, 0.5f, 0f);
        public float fieldOfView = 30f;

        public RenderTexture StickTexture { get; private set; }
        public RenderTexture ThrottleTexture { get; private set; }

        Quaternion shaftRest, leverRest;
        Camera stickCam, throttleCam;
        MaterialPropertyBlock block;
        Vector2 wantStick, shownStick;
        float wantOver, shownOver, wantThrottle, shownThrottle;
        bool wep, visible = true;

        /// <summary>The gauge from Resources, placed out of sight; null when it has not been built.</summary>
        public static StickThrottleGauge Create()
        {
            var prefab = Resources.Load<GameObject>("StickThrottleGauge");
            if (prefab == null) return null;
            var go = Instantiate(prefab, new Vector3(0, -20000, 0), Quaternion.identity);
            go.name = "StickThrottleGauge";
            return go.GetComponent<StickThrottleGauge>();
        }

        void Awake()
        {
            shaftRest = stickShaft.localRotation;
            leverRest = throttleLever.localRotation;
            block = new MaterialPropertyBlock();
            foreach (var t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            stickCam = MakeCamera("StickCamera", stickRoot, stickCamera, out var st);
            throttleCam = MakeCamera("ThrottleCamera", throttleRoot, throttleCamera, out var tt);
            StickTexture = st; ThrottleTexture = tt;
            Pose();
        }

        Camera MakeCamera(string name, Transform part, Vector3 at, out RenderTexture rt)
        {
            int h = textureHeight, w = Mathf.RoundToInt(h * 0.9f);
            rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = name };
            var go = new GameObject(name) { layer = Layer };
            go.transform.SetParent(part, false);
            go.transform.localPosition = at;
            go.transform.LookAt(part.TransformPoint(lookAt));
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.cullingMask = 1 << Layer;
            cam.fieldOfView = fieldOfView;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 10f;
            cam.depth = -10;
            cam.targetTexture = rt;
            return cam;
        }

        void OnDestroy()
        {
            if (StickTexture != null) StickTexture.Release();
            if (ThrottleTexture != null) ThrottleTexture.Release();
        }

        /// <summary>
        /// What to show: the stick (x right, y back, -1..1, with <see cref="RingTravel"/> at the ring), how far into
        /// the overpull band (0..1, tints the grip red), the throttle (0..1) and WEP. Snap jumps there at once.
        /// </summary>
        public void Show(Vector2 stickPos, float overpull, float throttlePos, bool warEmergency, bool snap = false)
        {
            wantStick = Vector2.ClampMagnitude(stickPos, 1f);
            wantOver = Mathf.Clamp01(overpull);
            wantThrottle = Mathf.Clamp01(throttlePos);
            wep = warEmergency;
            if (snap) { shownStick = wantStick; shownOver = wantOver; shownThrottle = wantThrottle; Pose(); }
        }

        /// <summary>Show or hide the gauge (its cameras stop rendering while hidden).</summary>
        public void SetVisible(bool on)
        {
            if (on == visible) return;
            visible = on;
            stickCam.enabled = throttleCam.enabled = on;
        }

        public bool Visible => visible;

        void Update()
        {
            float k = 1 - Mathf.Exp(-follow * Time.unscaledDeltaTime);
            shownStick = Vector2.Lerp(shownStick, wantStick, k);
            shownOver = Mathf.Lerp(shownOver, wantOver, k);
            shownThrottle = Mathf.Lerp(shownThrottle, wantThrottle, k);
            Pose();
        }

        /// <summary>Back tips the top toward the camera (-z), right toward +x; more throttle swings the lever forward (+z).</summary>
        public void Pose()
        {
            stickShaft.localRotation = Quaternion.AngleAxis(-shownStick.y * stickTravelDeg, Vector3.right)
                                     * Quaternion.AngleAxis(-shownStick.x * stickTravelDeg, Vector3.forward) * shaftRest;
            throttleLever.localRotation = Quaternion.AngleAxis(Mathf.Lerp(throttleIdleDeg, throttleFullDeg, shownThrottle), Vector3.right) * leverRest;
            if (block == null) block = new MaterialPropertyBlock();
            // the grip (submesh 1) reddens through the overpull band; the lever's knob glows with WEP
            Tint(stick, Color.Lerp(Color.white, overpullColor, shownOver * 0.85f), overpullColor * (shownOver * 0.6f));
            Tint(throttle, Color.white, wep ? new Color(1f, 0.25f, 0.1f) * 0.35f : Color.black);
        }

        void Tint(Renderer r, Color baseColor, Color emission)
        {
            r.GetPropertyBlock(block, 1);
            block.SetColor("_BaseColor", baseColor);
            block.SetColor("_EmissionColor", emission);
            r.SetPropertyBlock(block, 1);
        }
    }
}
