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

        void LateUpdate()
        {
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
