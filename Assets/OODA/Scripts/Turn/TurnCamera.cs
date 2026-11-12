using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// The turn battle's camera: an orbit around one aircraft (or the middle of the fight), turned by dragging
    /// and zoomed with the wheel, or the pilot's own view from the cockpit. TurnBattle feeds it the input.
    /// The orbit eases to where you put it, pivots a little above the aircraft so it sits low in the frame with
    /// the sky and its path ahead in view, follows the aircraft's heading as it turns (so a hard turn in the
    /// playback doesn't leave you looking at its side), can be panned off the aircraft, and stays above the ground.
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

        [Tooltip("How quickly the orbit eases to where the drag, wheel or recenter put it (higher is snappier).")]
        public float orbitSharpness = 14f;
        [Tooltip("How far above the aircraft the orbit pivots, as a fraction of the distance: the aircraft sits that much below the middle of the view.")]
        public float framingLift = 0.14f;
        [Tooltip("Swing the orbit round with the aircraft's heading as it turns, so the view keeps its place relative to the nose.")]
        public bool followHeading = true;
        [Tooltip("The least height the camera keeps above the ground.")]
        public float groundClearance = 6f;

        /// <summary>Where recenter puts the orbit: behind, this far back and this far above.</summary>
        public float homeDistance = 160f, homePitch = 18f;

        // what the view shows, easing toward yaw, pitch and distance
        float shownYaw, shownPitch, shownDistance;
        Vector3 pan, shownPan;   // the pivot moved off the aircraft, in the world, by panning
        Transform followed;
        float followedHeading;

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
            homeDistance = distance; homePitch = pitch;
            Snap();
        }

        /// <summary>Jump straight to where the orbit is set, without easing (for screenshots and cuts).</summary>
        public void Snap()
        {
            shownYaw = yaw; shownPitch = pitch; shownDistance = distance; shownPan = pan;
        }

        /// <summary>Back behind the aircraft at the usual distance and height, the pan cleared, the head turned forward.</summary>
        public void Recenter()
        {
            pitch = homePitch;
            distance = homeDistance;
            pan = Vector3.zero;
            look = Vector2.zero;
            if (focus == Focus.Enemy && enemy != null) yaw = HeadingOf(enemy, yaw);
            else BehindPlayer();
        }

        /// <summary>Move the orbit's pivot across the view by a drag of this many pixels (it scales with the distance).</summary>
        public void Pan(Vector2 pixels)
        {
            if (focus == Focus.Cockpit) return;
            float perPixel = 2f * shownDistance * Mathf.Tan(Cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, Screen.height);
            pan -= (transform.right * pixels.x + transform.up * pixels.y) * perPixel;
            float most = Mathf.Max(distance, 200f) * 1.5f;   // never so far off that the aircraft is lost
            if (pan.magnitude > most) pan = pan.normalized * most;
        }

        /// <summary>The heading (degrees about up) a transform points along, or fallback when it points straight up or down.</summary>
        static float HeadingOf(Transform t, float fallback)
        {
            Vector3 f = t.forward; f.y = 0;
            return f.sqrMagnitude > 0.04f ? Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg : fallback;
        }

        /// <summary>
        /// Swing the orbit by however far the aircraft in view has turned since last frame. Near the vertical (the top of
        /// a loop) the heading is held, and caught up with once the nose comes down, so the view swings round once rather
        /// than spinning.
        /// </summary>
        void FollowHeading()
        {
            Transform t = focus == Focus.Enemy && enemy != null ? enemy : player;
            if (t != followed) { followed = t; followedHeading = t != null ? HeadingOf(t, yaw) : 0; return; }
            if (t == null || !followHeading || focus == Focus.Cockpit) { if (t != null) followedHeading = HeadingOf(t, followedHeading); return; }
            Vector3 f = t.forward; f.y = 0;
            if (f.sqrMagnitude < 0.04f) return;
            float h = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            yaw = Mathf.Repeat(yaw + Mathf.DeltaAngle(followedHeading, h), 360f);
            followedHeading = h;
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
            pan = Vector3.zero;
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
                float ease = 1 - Mathf.Exp(-12f * Time.unscaledDeltaTime);   // eases onto the gun line rather than jumping
                transform.SetPositionAndRotation(Vector3.Lerp(transform.position, sightPos, ease), Quaternion.Slerp(transform.rotation, sightRot, ease));
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
            FollowHeading();
            Vector3 want = focus == Focus.Enemy && enemy != null ? enemy.position
                         : focus == Focus.Both && enemy != null ? (player.position + enemy.position) * 0.5f
                         : player.position;
            centre = placed ? Vector3.Lerp(centre, want, 1 - Mathf.Exp(-sharpness * Time.deltaTime)) : want;
            // Keep up with fast-moving aircraft: the lag is only there to smooth focus changes
            if ((centre - want).sqrMagnitude > 400f * 400f || Time.deltaTime == 0) centre = want;
            if (!placed) Snap();
            placed = true;

            // ease the orbit toward where it was put (unscaled: it turns while the playback is paused too)
            float k = 1 - Mathf.Exp(-orbitSharpness * Time.unscaledDeltaTime);
            shownYaw += Mathf.DeltaAngle(shownYaw, yaw) * k;
            shownPitch = Mathf.Lerp(shownPitch, pitch, k);
            shownDistance = Mathf.Lerp(shownDistance, distance, k);
            shownPan = Vector3.Lerp(shownPan, pan, k);
            if (Mathf.Abs(Mathf.DeltaAngle(shownYaw, yaw)) < 0.01f) shownYaw = yaw;

            float d = shownDistance;
            if (focus == Focus.Both && enemy != null) d = Mathf.Max(d, Vector3.Distance(player.position, enemy.position) * 0.9f);
            Quaternion rot = Quaternion.Euler(shownPitch, shownYaw, 0);
            // the pivot sits a little above the aircraft (along the view's up), so the aircraft rides low in the frame
            Vector3 pivot = centre + shownPan + rot * Vector3.up * (d * framingLift);
            Vector3 pos = pivot - rot * Vector3.forward * d;

            // never under the ground: lift the camera clear and look back down at the pivot
            float ground = GroundUnder(pos) + groundClearance;
            if (pos.y < ground)
            {
                pos.y = ground;
                Vector3 to = pivot - pos;
                if (to.sqrMagnitude > 1f) rot = Quaternion.LookRotation(to);
            }
            transform.SetPositionAndRotation(pos, rot);
        }

        /// <summary>The height of the ground (any terrain) under a point; very low when there is none.</summary>
        static Terrain[] terrains;
        static int terrainsFrame = -1000;

        static float GroundUnder(Vector3 at)
        {
            float best = -1e6f;
            if (terrains == null || Time.frameCount - terrainsFrame > 120) { terrains = Terrain.activeTerrains; terrainsFrame = Time.frameCount; }
            var ts = terrains;
            for (int i = 0; i < ts.Length; i++)
            {
                var t = ts[i];
                if (t == null || t.terrainData == null) continue;
                Vector3 o = t.transform.position, size = t.terrainData.size;
                if (at.x < o.x || at.z < o.z || at.x > o.x + size.x || at.z > o.z + size.z) continue;
                best = Mathf.Max(best, t.SampleHeight(at) + o.y);
            }
            return best;
        }
    }
}
