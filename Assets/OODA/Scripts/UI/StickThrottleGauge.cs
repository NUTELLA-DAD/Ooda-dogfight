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
        [Tooltip("The stick shaker at full: how far the stick jitters (degrees) and how fast (Hz).")]
        public float shakeDeg = 6f, shakeHz = 19f;

        [Header("Look")]
        public Color overpullColor = new Color(1f, 0.15f, 0.1f, 1f);
        public int textureHeight = 512;
        public Vector3 stickCamera = new Vector3(0f, 1.1f, -2.28f), throttleCamera = new Vector3(1.89f, 1.2f, -1.89f);
        public Vector3 lookAt = new Vector3(0f, 0.5f, 0f);
        public float fieldOfView = 25f;

        public RenderTexture StickTexture { get; private set; }
        public RenderTexture ThrottleTexture { get; private set; }
        /// <summary>The rudder pedals (Kenny's Hi3D model, Resources/Pedals), or null when they are not in the build.</summary>
        public RenderTexture PedalsTexture { get; private set; }

        [Header("Pedals")]
        [Tooltip("How far the pedal bar swings at full rudder (degrees about the vertical), and where its camera sits.")]
        public float pedalYawDeg = 14f;
        public Vector3 pedalsCamera = new Vector3(0f, 0.75f, -1.7f), pedalsLookAt = new Vector3(0f, 0.22f, 0f);
        /// <summary>The pictures' width over height: the pedals are wide and low.</summary>
        public const float PedalsAspect = 1.8f;
        Transform pedalsRoot;
        Camera pedalsCam;
        float wantRudder, shownRudder;

        Quaternion shaftRest, leverRest;
        Camera stickCam, throttleCam;
        MaterialPropertyBlock block;
        Vector2 wantStick, shownStick;
        float wantOver, shownOver, wantThrottle, shownThrottle;
        bool wep, visible = true;
        float shake;

        /// <summary>The stick shaker, 0 still to 1 hardest: the wing near its limit. Read by the screen to jiggle the picture too.</summary>
        public float Shake { get => shake; set => shake = Mathf.Clamp01(value); }

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
            MakePedals();
            Pose();
        }

        /// <summary>
        /// The rudder pedals beside the stick and the throttle, on the same layer with their own camera: the mesh from
        /// Resources/Pedals (an OBJ, one unit wide, base on the origin, facing the camera) in the stick's material with the
        /// pedals' own colour map. Missing from the build, the screen keeps its pedal bar.
        /// </summary>
        void MakePedals()
        {
            var model = Resources.Load<GameObject>("Pedals");
            var tex = Resources.Load<Texture2D>("Pedals_BaseColor") ?? Resources.Load("Pedals_BaseColor") as Texture2D;
            if (model == null || stick == null) return;
            // a big OBJ comes in as several meshes (Unity splits it at 65k vertices): take every part
            var parts = model.GetComponentsInChildren<MeshFilter>(true);
            if (parts.Length == 0) return;
            var root = new GameObject("Pedals") { layer = Layer };
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(9, 0, 0);
            var mat = new Material(stick.sharedMaterials[0]) { name = "Pedals" };
            if (tex != null) { mat.SetTexture("_BaseMap", tex); mat.SetTexture("_MainTex", tex); }
            foreach (var part in parts)
            {
                if (part.sharedMesh == null) continue;
                var body = new GameObject(part.name) { layer = Layer };
                body.transform.SetParent(root.transform, false);
                var from = model.transform.InverseTransformPoint(part.transform.position);
                body.transform.localPosition = from;
                body.transform.localRotation = Quaternion.Inverse(model.transform.rotation) * part.transform.rotation;
                body.transform.localScale = part.transform.lossyScale;
                body.AddComponent<MeshFilter>().sharedMesh = part.sharedMesh;
                body.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
            Debug.Log($"OODA: pedals: {parts.Length} mesh parts, {(tex != null ? $"colour map {tex.width}x{tex.height}" : "NO colour map (Resources/Pedals_BaseColor)")}, shader {mat.shader.name}");
            pedalsRoot = root.transform;
            pedalsCam = MakeCamera("PedalsCamera", pedalsRoot, pedalsCamera, out var pt, PedalsAspect, pedalsLookAt);
            PedalsTexture = pt;
        }

        /// <summary>The rudder to show, -1 full left to 1 full right: the bar swings, the right pedal forward for right rudder.</summary>
        public float Rudder { get => wantRudder; set => wantRudder = Mathf.Clamp(value, -1f, 1f); }

        Camera MakeCamera(string name, Transform part, Vector3 at, out RenderTexture rt, float aspect = 0.9f, Vector3? look = null)
        {
            int h = textureHeight, w = Mathf.RoundToInt(h * aspect);
            if (aspect > 1) { w = textureHeight; h = Mathf.RoundToInt(w / aspect); }
            rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = name };
            var go = new GameObject(name) { layer = Layer };
            go.transform.SetParent(part, false);
            go.transform.localPosition = at;
            go.transform.LookAt(part.TransformPoint(look ?? lookAt));
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
            if (PedalsTexture != null) PedalsTexture.Release();
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

        /// <summary>Jump straight to what was last asked for.</summary>
        public void Snap() => Show(wantStick, wantOver, wantThrottle, wep, true);

        /// <summary>Show or hide the gauge (its cameras stop rendering while hidden).</summary>
        public void SetVisible(bool on)
        {
            if (on == visible) return;
            visible = on;
            stickCam.enabled = throttleCam.enabled = on;
            if (pedalsCam != null) pedalsCam.enabled = on;
        }

        public bool Visible => visible;

        void Update()
        {
            float k = 1 - Mathf.Exp(-follow * Time.unscaledDeltaTime);
            shownStick = Vector2.Lerp(shownStick, wantStick, k);
            shownOver = Mathf.Lerp(shownOver, wantOver, k);
            shownThrottle = Mathf.Lerp(shownThrottle, wantThrottle, k);
            shownRudder = Mathf.Lerp(shownRudder, wantRudder, k);
            Pose();
        }

        /// <summary>Back tips the top toward the camera (-z), right toward +x; more throttle swings the lever forward (+z).</summary>
        public void Pose()
        {
            // the shaker: a fast buzz on top of where the stick sits, two out-of-step waves so it rattles rather than swings
            float tm = Time.unscaledTime * shakeHz * 2 * Mathf.PI;
            float jx = shake * shakeDeg * Mathf.Sin(tm * 1.13f), jy = shake * shakeDeg * Mathf.Sin(tm + 1.7f);
            stickShaft.localRotation = Quaternion.AngleAxis(-shownStick.y * stickTravelDeg + jy, Vector3.right)
                                     * Quaternion.AngleAxis(-shownStick.x * stickTravelDeg + jx, Vector3.forward) * shaftRest;
            throttleLever.localRotation = Quaternion.AngleAxis(Mathf.Lerp(throttleIdleDeg, throttleFullDeg, shownThrottle), Vector3.right) * leverRest;
            if (pedalsRoot != null) pedalsRoot.localRotation = Quaternion.AngleAxis(-shownRudder * pedalYawDeg, Vector3.up);   // right rudder: the right pedal away
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
