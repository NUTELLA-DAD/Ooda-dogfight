using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// The turn battle's camera: an orbit around one aircraft (or the middle of the fight), turned by dragging
    /// and zoomed with the wheel, or the pilot's own view from the cockpit. TurnBattle feeds it the input.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class TurnCamera : MonoBehaviour
    {
        public enum Focus { Player, Enemy, Both, Cockpit }

        public Focus focus = Focus.Player;
        public Transform player, enemy, cockpitEye;
        public float yaw, pitch = 18f, distance = 160f;
        public float minDistance = 15f, maxDistance = 4000f;
        public float sharpness = 8f;

        /// <summary>Set by the battle while it shows the gunsight view: the camera sits at sightPos looking along sightRot.</summary>
        public bool sightPose;
        public Vector3 sightPos;
        public Quaternion sightRot = Quaternion.identity;
        public float sightFov = 55f;

        Camera cam;
        Vector3 centre;
        bool placed;

        public Camera Cam => cam != null ? cam : cam = GetComponent<Camera>();

        void Awake()
        {
            Cam.farClipPlane = 30000f;
        }

        /// <summary>Look from behind the player, as at the start of a turn.</summary>
        public void BehindPlayer()
        {
            if (player == null) return;
            Vector3 f = player.forward; f.y = 0;
            if (f.sqrMagnitude > 1e-4f) yaw = Quaternion.LookRotation(f).eulerAngles.y;
        }

        Vector2 look;   // cockpit: head turned (yaw, pitch)

        public void Orbit(Vector2 degrees)
        {
            if (focus == Focus.Cockpit)
            {
                look.x = Mathf.Clamp(look.x + degrees.x, -160f, 160f);
                look.y = Mathf.Clamp(look.y + degrees.y, -40f, 80f);
                return;
            }
            yaw += degrees.x;
            pitch = Mathf.Clamp(pitch - degrees.y, -85f, 85f);
        }

        public void Zoom(float steps) => distance = Mathf.Clamp(distance * Mathf.Pow(1.15f, -steps), minDistance, maxDistance);

        public void NextFocus()
        {
            focus = (Focus)(((int)focus + 1) % 4);
            look = Vector2.zero;
            if (focus == Focus.Cockpit && cockpitEye == null) focus = Focus.Player;
        }

        float joltNow;

        /// <summary>A sudden yank (a grapple's line snapping): the view kicks, by 0 to 1, and settles over half a second.</summary>
        public void Jolt(float strength) => joltNow = Mathf.Max(joltNow, Mathf.Clamp01(strength));

        /// <summary>The jolt's shake on top of where the view sits, fading away.</summary>
        void ApplyJolt()
        {
            if (joltNow <= 0.001f) { joltNow = 0; return; }
            float t = Time.unscaledTime * 47f, deg = 6f * joltNow * joltNow;
            transform.rotation = transform.rotation * Quaternion.Euler(Mathf.Sin(t) * deg, Mathf.Sin(t * 1.37f + 1f) * deg, Mathf.Sin(t * 0.71f + 2f) * deg * 0.5f);
            joltNow = Mathf.Max(0, joltNow - Time.unscaledDeltaTime * 2f);
        }

        float boostAge = -1f, lastBoostAge = -1f, boost, punch;
        int boostFrame = -1;

        /// <summary>
        /// The rocket you ride, each frame it plays: seconds since it lit, or -1 when it isn't burning. The view kicks as it
        /// lights, widens while it burns (speed) and shudders with it.
        /// </summary>
        public void Boost(float rocketAge) { boostAge = rocketAge; boostFrame = Time.frameCount; }

        void ApplyBoost()
        {
            float age = boostFrame >= Time.frameCount - 1 ? boostAge : -1f;
            bool lit = age >= 0f;
            if (lit && (lastBoostAge < 0f || age < lastBoostAge)) { Jolt(0.85f); punch = 1f; }
            lastBoostAge = age;
            float dt = Time.unscaledDeltaTime;
            boost = Mathf.MoveTowards(boost, lit ? 1f : 0f, dt / (lit ? 0.2f : 0.6f));
            punch = Mathf.MoveTowards(punch, 0f, dt / 0.45f);
            if (boost <= 0.001f && punch <= 0.001f) return;
            if (!sightPose) Cam.fieldOfView += 9f * boost + 10f * punch * punch;
            float t = Time.unscaledTime * 31f, deg = 0.5f * boost;
            transform.rotation = transform.rotation * Quaternion.Euler(Mathf.Sin(t * 1.21f) * deg, Mathf.Sin(t + 0.7f) * deg, Mathf.Sin(t * 0.63f + 2f) * deg * 0.6f);
        }

        void LateUpdate()
        {
            Place();
            ApplyBoost();
            ApplyJolt();
        }

        void Place()
        {
            if (sightPose)
            {
                Cam.nearClipPlane = 0.3f;
                Cam.fieldOfView = Mathf.Lerp(Cam.fieldOfView, sightFov, 1 - Mathf.Exp(-10f * Time.unscaledDeltaTime));
                float k = 1 - Mathf.Exp(-12f * Time.unscaledDeltaTime);   // eases onto the gun line rather than jumping
                transform.SetPositionAndRotation(Vector3.Lerp(transform.position, sightPos, k), Quaternion.Slerp(transform.rotation, sightRot, k));
                return;
            }
            if (player == null) return;
            if (focus == Focus.Cockpit && cockpitEye != null)
            {
                Cam.nearClipPlane = 0.05f;
                Cam.fieldOfView = 75f;
                transform.SetPositionAndRotation(cockpitEye.position, player.rotation * Quaternion.Euler(-look.y, look.x, 0));
                return;
            }
            Cam.nearClipPlane = 0.5f;
            Cam.fieldOfView = 60f;
            Vector3 want = focus == Focus.Enemy && enemy != null ? enemy.position
                         : focus == Focus.Both && enemy != null ? (player.position + enemy.position) * 0.5f
                         : player.position;
            centre = placed ? Vector3.Lerp(centre, want, 1 - Mathf.Exp(-sharpness * Time.deltaTime)) : want;
            // Keep up with fast-moving aircraft: the lag is only there to smooth focus changes
            if ((centre - want).sqrMagnitude > 400f * 400f || Time.deltaTime == 0) centre = want;
            placed = true;
            float d = distance;
            if (focus == Focus.Both && enemy != null) d = Mathf.Max(distance, Vector3.Distance(player.position, enemy.position) * 0.9f);
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0);
            transform.SetPositionAndRotation(centre - rot * Vector3.forward * d, rot);
        }
    }
}
