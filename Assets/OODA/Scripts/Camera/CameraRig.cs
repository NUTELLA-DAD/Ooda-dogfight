using Ooda.Flight;
using UnityEngine;

namespace Ooda.CameraSystem
{
    /// <summary>
    /// The chase camera (behind and above, lagging a little in turns) and the cockpit camera (the pilot's eyes,
    /// free look). C or the gamepad's Y switches between them.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraRig : MonoBehaviour
    {
        public enum Mode { Chase, Cockpit }

        public AircraftController aircraft;
        public Transform cockpitEye;
        public Pilot.PilotHeadLook pilot;
        public Mode mode = Mode.Chase;

        [Header("Chase")]
        public float distance = 16f;
        public float height = 3.5f;
        public float lookAhead = 25f;
        [Tooltip("How quickly the camera catches up with the aircraft (higher is stiffer).")]
        public float followSharpness = 6f;
        public float rollSharpness = 4f;
        public float baseFov = 60f, fastFov = 72f, fastKias = 400f;

        [Header("Cockpit")]
        public float cockpitFov = 75f;
        public Vector2 lookLimits = new Vector2(160f, 80f);

        [Header("Buffet")]
        [Tooltip("Shake in degrees at the stall line, and in a departure or spin.")]
        public float stallBuffetDeg = 0.5f, departureBuffetDeg = 1.2f;

        Camera cam;
        Vector3 chasePos;
        Vector3 chaseUp = Vector3.up;
        Vector2 look;

        void Start()
        {
            cam = GetComponent<Camera>();
            cam.farClipPlane = 25000f;
            if (aircraft != null) chasePos = aircraft.transform.position - aircraft.transform.forward * distance;
            Apply();
        }

        void Apply()
        {
            cam.nearClipPlane = mode == Mode.Cockpit ? 0.05f : 0.3f;
            if (pilot != null) pilot.hideHead = mode == Mode.Cockpit;
            look = Vector2.zero;
        }

        /// <summary>The airframe shaking at the stall: a small fast wobble, stronger once it departs.</summary>
        Quaternion Buffet()
        {
            var m = aircraft.Model;
            if (m == null || aircraft.Crashed) return Quaternion.identity;
            float amp = m.Departed ? departureBuffetDeg : m.stalled ? stallBuffetDeg : 0f;
            if (amp <= 0) return Quaternion.identity;
            float t = Time.time * 18f;
            return Quaternion.Euler((Mathf.PerlinNoise(t, 1.3f) - 0.5f) * 2f * amp, (Mathf.PerlinNoise(2.7f, t) - 0.5f) * 2f * amp, 0);
        }

        void LateUpdate()
        {
            if (aircraft == null || aircraft.Input == null) return;
            var input = aircraft.Input;
            if (input.CameraPressed) { mode = mode == Mode.Chase ? Mode.Cockpit : Mode.Chase; Apply(); }

            float dt = Time.deltaTime;
            Transform plane = aircraft.transform;
            Vector2 lookIn = input.LookRate * dt;
            if (mode == Mode.Cockpit || input.LookHeld) lookIn += input.LookDelta;

            if (mode == Mode.Cockpit && cockpitEye != null)
            {
                look += new Vector2(lookIn.x, -lookIn.y);
                look.x = Mathf.Clamp(look.x, -lookLimits.x, lookLimits.x);
                look.y = Mathf.Clamp(look.y, -lookLimits.y * 0.5f, lookLimits.y);
                transform.SetPositionAndRotation(cockpitEye.position, plane.rotation * Quaternion.Euler(-look.y, look.x, 0) * Buffet());
                cam.fieldOfView = cockpitFov;
                return;
            }

            // Chase: orbit offset from look input springs back when released
            bool looking = lookIn.sqrMagnitude > 1e-6f || input.LookHeld;
            look += new Vector2(lookIn.x, -lookIn.y);
            if (!looking) look = Vector2.Lerp(look, Vector2.zero, 1 - Mathf.Exp(-3f * dt));
            look.y = Mathf.Clamp(look.y, -60, 80);

            chaseUp = Vector3.Slerp(chaseUp, plane.up, 1 - Mathf.Exp(-rollSharpness * dt)).normalized;
            Quaternion orbit = Quaternion.AngleAxis(look.x, chaseUp) * Quaternion.AngleAxis(-look.y, Vector3.Cross(chaseUp, plane.forward).normalized);
            Vector3 want = plane.position + orbit * (-plane.forward * distance + chaseUp * height);
            chasePos = Vector3.Lerp(chasePos, want, 1 - Mathf.Exp(-followSharpness * dt));
            // keep the lag from pulling the camera too far behind at speed
            Vector3 rel = chasePos - plane.position;
            if (rel.magnitude > distance * 1.6f) chasePos = plane.position + rel.normalized * distance * 1.6f;

            Vector3 aim = plane.position + plane.forward * lookAhead * (looking ? 0.2f : 1f);
            transform.SetPositionAndRotation(chasePos, Quaternion.LookRotation(aim - chasePos, chaseUp) * Buffet());
            cam.fieldOfView = Mathf.Lerp(baseFov, fastFov, Mathf.InverseLerp(150, fastKias, aircraft.Kias));
        }
    }
}
