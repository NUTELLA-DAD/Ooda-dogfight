using Ooda.Flight;
using UnityEngine;

namespace Ooda.Pilot
{
    /// <summary>
    /// The pilot flies the cockpit: his right paw stays on the stick and his left on the throttle (hand IK onto the
    /// wrist points the cockpit model carries), the stick leans with the G and the roll, the throttle lever follows the
    /// engine, and the needles read the airspeed, height, climb and RPM while the horizon turns with the bank.
    /// He also does things: works the instruments and checks six now and then, shakes his fist at an enemy that comes
    /// close, flinches when he is hit and cheers a kill (<see cref="Play"/>). The gestures are Animator states named
    /// like the triggers; the base state is Fly.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class PilotControls : MonoBehaviour
    {
        public enum Gesture { Instruments, ShakeFist, CheckSix, Cheer, Flinch }

        [Header("Cockpit parts (found by name under the aircraft when empty)")]
        public Transform stick, stickWrist, throttle, throttleWrist;
        public Transform needleAirspeed, needleAltimeter, needleClimb, needleRpm, horizon;

        [Header("Stick and throttle travel")]
        public float stickPitchDeg = 14f, stickRollDeg = 16f, throttleTravelDeg = 50f;
        [Range(0, 1)] public float handWeight = 1f;

        [Header("Gestures")]
        [Tooltip("Shake a fist at the enemy the head is watching when it comes this close (m).")]
        public float fistRange = 450f;
        public float fistCooldown = 14f;
        [Tooltip("Seconds between idle gestures (instruments, checking six), picked at random in this range.")]
        public Vector2 idleEvery = new Vector2(9f, 20f);

        IFlightView source;
        Animator anim;
        PilotHeadLook look;
        Quaternion stickRest, throttleRest, aspRest, altRest, climbRest, rpmRest, horizonRest;
        float pitchS, rollS, thrS, nextIdle, lastFist = -99f, leftFree, rightFree, lookFree;
        float gestureEnd;

        static readonly int[] Triggers =
        {
            Animator.StringToHash("Instruments"), Animator.StringToHash("ShakeFist"), Animator.StringToHash("CheckSix"),
            Animator.StringToHash("Cheer"), Animator.StringToHash("Flinch"),
        };

        void Awake()
        {
            anim = GetComponent<Animator>();
            look = GetComponent<PilotHeadLook>();
        }

        void Start()
        {
            source = GetComponentInParent<IFlightView>();
            Transform root = source != null ? source.transform : transform.root;
            stick = stick ? stick : Find(root, "Pit_Stick");
            stickWrist = stickWrist ? stickWrist : Find(root, "StickWrist");
            throttle = throttle ? throttle : Find(root, "Pit_Throttle");
            throttleWrist = throttleWrist ? throttleWrist : Find(root, "ThrottleWrist");
            needleAirspeed = needleAirspeed ? needleAirspeed : Find(root, "Pit_Needle_Airspeed");
            needleAltimeter = needleAltimeter ? needleAltimeter : Find(root, "Pit_Needle_Altimeter");
            needleClimb = needleClimb ? needleClimb : Find(root, "Pit_Needle_Climb");
            needleRpm = needleRpm ? needleRpm : Find(root, "Pit_Needle_RPM");
            horizon = horizon ? horizon : Find(root, "Pit_Horizon");
            if (stick) stickRest = stick.localRotation;
            if (throttle) throttleRest = throttle.localRotation;
            if (needleAirspeed) aspRest = needleAirspeed.localRotation;
            if (needleAltimeter) altRest = needleAltimeter.localRotation;
            if (needleClimb) climbRest = needleClimb.localRotation;
            if (needleRpm) rpmRest = needleRpm.localRotation;
            if (horizon) horizonRest = horizon.localRotation;
            // with a stick to hold, the hands leave the cockpit rails
            var rails = GetComponent<PilotRails>();
            if (rails != null && stickWrist != null) { rails.weight = 0; rails.enabled = false; }
            nextIdle = Time.time + Random.Range(idleEvery.x, idleEvery.y);
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var f = Find(c, name); if (f != null) return f; }
            return null;
        }

        /// <summary>Do something now: the hand it needs lets go of its control for as long as it takes.</summary>
        public void Play(Gesture g)
        {
            if (anim == null || anim.runtimeAnimatorController == null) return;
            if (Time.time < gestureEnd && g != Gesture.Flinch) return;   // one at a time, but a hit always flinches
            anim.SetTrigger(Triggers[(int)g]);
            float len = g switch { Gesture.Instruments => 3f, Gesture.ShakeFist => 2.5f, Gesture.CheckSix => 3f, Gesture.Cheer => 2f, _ => 1f };
            gestureEnd = Time.time + len;
            if (g == Gesture.Instruments || g == Gesture.ShakeFist || g == Gesture.Cheer) leftFree = len;
            if (g == Gesture.Flinch) { leftFree = Mathf.Max(leftFree, 0.6f); rightFree = 0.6f; }
            if (g == Gesture.CheckSix || g == Gesture.Instruments) lookFree = len;
            if (g == Gesture.ShakeFist) lastFist = Time.time;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            leftFree = Mathf.Max(0, leftFree - dt); rightFree = Mathf.Max(0, rightFree - dt); lookFree = Mathf.Max(0, lookFree - dt);
            // the head-look would fight a look back over the shoulder or down at the panel
            if (look != null) look.headWeight = Mathf.MoveTowards(look.headWeight, lookFree > 0 ? 0f : 1f, dt * 3f);

            var m = source?.Model;
            if (m == null) return;
            bool dead = source.Crashed;

            // controls: the stick from the G asked and the roll rate, the lever from the engine
            float pull = Mathf.Clamp(((float)m.g - 1f) / 5f, -0.4f, 1f);
            float roll = Mathf.Clamp((float)m.rollRateDps / 120f, -1f, 1f);
            float k = 1 - Mathf.Exp(-8f * dt);
            pitchS = Mathf.Lerp(pitchS, pull, k); rollS = Mathf.Lerp(rollS, roll, k);
            thrS = Mathf.Lerp(thrS, Mathf.Clamp01((float)m.throttle), 1 - Mathf.Exp(-4f * dt));
            Transform plane = source.transform;
            if (stick)   // back is toward the pilot (plane aft), right rolls right
            {
                Quaternion world = Quaternion.AngleAxis(-pitchS * stickPitchDeg, plane.right) * Quaternion.AngleAxis(-rollS * stickRollDeg, plane.forward);
                stick.localRotation = Quaternion.Inverse(stick.parent.rotation) * world * stick.parent.rotation * stickRest;
            }
            if (throttle)
            {
                Quaternion world = Quaternion.AngleAxis((thrS - 0.5f) * throttleTravelDeg, plane.right);
                throttle.localRotation = Quaternion.Inverse(throttle.parent.rotation) * world * throttle.parent.rotation * throttleRest;
            }

            // instruments: needles turn about the panel's normal, which faces aft
            float kias = (float)m.Kias, alt = (float)m.AltFt, climb = (float)(m.state.vel.z * 60.0), rpm = dead ? 0 : 1200f + 1800f * thrS;
            Needle(needleAirspeed, aspRest, Mathf.Clamp(kias, 0, 500) / 500f * 330f, plane);
            Needle(needleAltimeter, altRest, Mathf.Repeat(alt, 1000f) / 1000f * 360f, plane);
            Needle(needleClimb, climbRest, -90f + Mathf.Clamp(climb / 4000f, -1, 1) * 170f, plane);
            Needle(needleRpm, rpmRest, -135f + Mathf.Clamp01(rpm / 5000f) * 270f, plane);
            if (horizon)
            {
                float bank = Vector3.SignedAngle(Vector3.ProjectOnPlane(Vector3.up, plane.forward), plane.up, plane.forward);
                Quaternion world = Quaternion.AngleAxis(-bank, plane.forward);   // stays level with the world
                horizon.localRotation = Quaternion.Inverse(horizon.parent.rotation) * world * horizon.parent.rotation * horizonRest;
            }

            if (dead) return;
            // a bandit close by gets a fist shaken at it
            if (look != null && look.target != null && Time.time - lastFist > fistCooldown &&
                Vector3.Distance(look.target.position, transform.position) < fistRange)
                Play(Gesture.ShakeFist);
            // now and then, when the G is light: tap the gauges or check six
            if (Time.time > nextIdle)
            {
                nextIdle = Time.time + Random.Range(idleEvery.x, idleEvery.y);
                if (Mathf.Abs(pull) < 0.4f) Play(Random.value < 0.5f ? Gesture.Instruments : Gesture.CheckSix);
            }
        }

        static void Needle(Transform n, Quaternion rest, float deg, Transform plane)
        {
            if (n == null) return;
            // clockwise as the pilot sees it, looking forward (Unity's positive turn is clockwise seen from the axis tip, so negate)
            Quaternion world = Quaternion.AngleAxis(-deg, plane.forward);
            n.localRotation = Quaternion.Inverse(n.parent.rotation) * world * n.parent.rotation * rest;
        }

        void OnAnimatorIK(int layer)
        {
            if (anim == null) return;
            float wl = handWeight * (1 - Mathf.Clamp01(leftFree * 3f)), wr = handWeight * (1 - Mathf.Clamp01(rightFree * 3f));
            // ease back onto the controls as the gesture ends rather than snapping
            Hand(AvatarIKGoal.LeftHand, throttleWrist, wl);
            Hand(AvatarIKGoal.RightHand, stickWrist, wr);
        }

        void Hand(AvatarIKGoal goal, Transform at, float w)
        {
            if (at == null) { anim.SetIKPositionWeight(goal, 0); return; }
            anim.SetIKPositionWeight(goal, w);
            anim.SetIKPosition(goal, at.position);
        }
    }
}
