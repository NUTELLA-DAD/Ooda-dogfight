using UnityEngine;

namespace Ooda.Pilot
{
    /// <summary>
    /// The rocket's shove on the pilot. As it lights he is slammed back into the seat, nearly blown out of it: his body
    /// whips back past where it settles and his head snaps back, his eyes squeeze shut, his ears are blown flat and his
    /// tail thrashes; while it burns he stays pinned back, head pressed into the headrest, everything shuddering, his arms
    /// straining to keep his paws on the stick and throttle; when it burns out he lurches forward and settles.
    /// A spring (an underdamped one, kicked as it lights) drives it all, on top of whatever clip is playing: the spine,
    /// chest, neck and head are turned back about the aircraft's wing axis after the Animator, and each arm is bent again
    /// (two-bone IK) so its hand stays where the clip and PilotControls put it. It reads only the Humanoid bones, and the
    /// ear and tail bones when a rig has them (LeftEar, RightEar, Tail1..Tail4), so it fits every pilot.
    /// RocketPlume adds it to each pilot of an aircraft the first time its rocket lights.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class PilotThrust : MonoBehaviour
    {
        [Header("Pinned back while the rocket burns")]
        [Tooltip("The torso's lean back once it settles, degrees (split over the spine and chest).")]
        public float leanDeg = 17f;
        [Tooltip("The head's tilt back on top of the torso's, degrees (split over the neck and head).")]
        public float headDeg = 32f;
        [Header("The kick as it lights")]
        [Tooltip("How hard it kicks as it lights: the spring's speed then (1/s). About 14 throws him back to nearly twice where he settles.")]
        public float kick = 12f;
        [Tooltip("The lurch forward as it burns out (1/s).")]
        public float lurch = 7f;
        public float stiffness = 70f, damping = 7f;
        [Header("Shudder, ears and tail")]
        public float shudderDeg = 2.2f, shudderHz = 16f;
        [Tooltip("How far the ears are blown back, degrees.")]
        public float earsDeg = 65f;
        public float tailDeg = 22f;

        Animator anim;
        Ooda.Turn.TurnAircraftView view;
        PilotFace face;
        Transform spine, chest, neck, head;
        Transform[] armL, armR;   // upper arm, forearm, hand
        Transform[] ears, tail;
        Quaternion[] extraBase, extraWritten;   // for ear and tail bones no clip drives: their rest, so the turns don't pile up
        float x, v, shake, faceNext;
        bool was;

        void Start()
        {
            anim = GetComponent<Animator>();
            view = GetComponentInParent<Ooda.Turn.TurnAircraftView>();
            if (anim == null || !anim.isHuman || view == null) { enabled = false; return; }
            spine = anim.GetBoneTransform(HumanBodyBones.Spine);
            chest = anim.GetBoneTransform(HumanBodyBones.Chest);
            neck = anim.GetBoneTransform(HumanBodyBones.Neck);
            head = anim.GetBoneTransform(HumanBodyBones.Head);
            armL = new[] { anim.GetBoneTransform(HumanBodyBones.LeftUpperArm), anim.GetBoneTransform(HumanBodyBones.LeftLowerArm), anim.GetBoneTransform(HumanBodyBones.LeftHand) };
            armR = new[] { anim.GetBoneTransform(HumanBodyBones.RightUpperArm), anim.GetBoneTransform(HumanBodyBones.RightLowerArm), anim.GetBoneTransform(HumanBodyBones.RightHand) };
            ears = new[] { FindEnd(transform, "LeftEar"), FindEnd(transform, "RightEar") };
            tail = new[] { FindEnd(transform, "Tail1"), FindEnd(transform, "Tail2"), FindEnd(transform, "Tail3"), FindEnd(transform, "Tail4") };
            extraBase = new Quaternion[ears.Length + tail.Length];
            extraWritten = new Quaternion[extraBase.Length];
            for (int i = 0; i < extraBase.Length; i++) { var t = Extra(i); if (t != null) extraBase[i] = extraWritten[i] = t.localRotation; }
        }

        Transform Extra(int i) => i < ears.Length ? ears[i] : tail[i - ears.Length];

        static Transform FindEnd(Transform t, string name)
        {
            if (t.name == name || t.name.EndsWith(":" + name) || t.name.EndsWith("_" + name)) return t;
            foreach (Transform c in t) { var f = FindEnd(c, name); if (f != null) return f; }
            return null;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            float age = view.RocketAge;
            bool burning = age >= 0f && !view.Crashed;
            if (burning && !was) v += kick;          // slammed back
            if (!burning && was) v -= lurch;         // thrown forward as the shove stops
            was = burning;
            float target = burning ? 1f : 0f;
            // an underdamped spring, in small steps so a slow frame can't blow it up
            int n = Mathf.Clamp(Mathf.CeilToInt(dt / (1f / 120f)), 1, 8);
            float h = dt / n;
            for (int i = 0; i < n; i++) { v += (stiffness * (target - x) - damping * v) * h; x += v * h; }
            shake = Mathf.MoveTowards(shake, burning ? 1f : 0f, dt / (burning ? 0.05f : 0.4f));

            if (burning && Time.time >= faceNext)
            {
                if (face == null) face = GetComponent<PilotFace>();
                if (face != null) face.Express(age < 0.6f ? 1f : 0.7f, 0.4f);   // eyes squeezed shut, then a hard squint
                faceNext = Time.time + 0.3f;
            }
        }

        // After the Animator and its IK (the hands on the controls, the head look): turn him back, then bend the arms again
        void LateUpdate()
        {
            // ear and tail bones no clip writes would keep what we turned them by last frame: put them back first
            for (int i = 0; i < extraBase.Length; i++)
            {
                var t = Extra(i);
                if (t == null) continue;
                if (Quaternion.Angle(t.localRotation, extraWritten[i]) < 0.01f) t.localRotation = extraBase[i];
                else extraBase[i] = t.localRotation;   // (a clip drives it: this frame's pose is the base)
            }
            if (Mathf.Abs(x) < 0.002f && Mathf.Abs(v) < 0.01f && shake < 0.01f) { Remember(); return; }

            Transform plane = view.transform;
            Vector3 side = plane.right, fwd = plane.forward, up = plane.up;
            float tt = Time.time * shudderHz * Mathf.PI * 2f;
            float j1 = Mathf.Sin(tt) + 0.5f * Mathf.Sin(tt * 2.3f + 1f), j2 = Mathf.Sin(tt * 1.37f + 2f) + 0.5f * Mathf.Sin(tt * 3.1f), j3 = Mathf.Sin(tt * 0.83f + 4f);
            float jit = shudderDeg * shake;

            // where the hands are now (on the stick and throttle, or mid-gesture): they stay there
            Vector3 hl = armL[2] ? armL[2].position : Vector3.zero, hr = armR[2] ? armR[2].position : Vector3.zero;
            Quaternion ql = armL[2] ? armL[2].rotation : Quaternion.identity, qr = armR[2] ? armR[2].rotation : Quaternion.identity;
            Vector3 elbowL = armL[1] ? armL[1].position : Vector3.zero, elbowR = armR[1] ? armR[1].position : Vector3.zero;

            // the torso back (about the wings' axis: back is a negative turn), shuddering
            float lean = leanDeg * x;
            Bend(spine, -lean * 0.55f + jit * 0.5f * j1, side, jit * 0.35f * j2, fwd);
            Bend(chest, -lean * 0.45f + jit * 0.4f * j2, side, jit * 0.3f * j3, up);
            // the head pressed back further (and forward when he lurches), rattling
            float tilt = headDeg * x;
            Bend(neck, -tilt * 0.4f + jit * 0.6f * j3, side, jit * 0.5f * j1, fwd);
            Bend(head, -tilt * 0.6f + jit * 0.9f * j2, side, jit * 0.8f * j3, up);

            // the arms bent again so the hands keep their grip
            Arm(armL, hl, ql, elbowL);
            Arm(armR, hr, qr, elbowR);

            // the ears blown flat back and fluttering, the tail thrashing
            float blown = Mathf.Clamp(x, 0f, 1.6f);
            for (int i = 0; i < ears.Length; i++)
            {
                var e = ears[i];
                if (e == null || head == null) continue;
                Vector3 dir = (e.position - head.position).normalized;   // (from the head out to the ear's root: about the way the ear points)
                Vector3 axis = Vector3.Cross(dir, -fwd);
                if (axis.sqrMagnitude < 1e-6f) continue;
                float flap = Mathf.Sin(tt * 1.7f + i * 2.1f) * 9f * shake;
                e.rotation = Quaternion.AngleAxis(earsDeg * blown * 0.7f + flap, axis.normalized) * e.rotation;
            }
            for (int i = 0; i < tail.Length; i++)
            {
                var t = tail[i];
                if (t == null) continue;
                float lash = Mathf.Sin(tt * 0.9f - i * 0.9f) * tailDeg * shake * (0.5f + 0.2f * i) + Mathf.Clamp(x - 1f, 0f, 1f) * tailDeg;
                t.rotation = Quaternion.AngleAxis(lash, up) * t.rotation;
            }
            Remember();
        }

        void Remember()
        {
            for (int i = 0; i < extraBase.Length; i++) { var t = Extra(i); if (t != null) extraWritten[i] = t.localRotation; }
        }

        static void Bend(Transform t, float deg, Vector3 axis, float deg2, Vector3 axis2)
        {
            if (t == null) return;
            t.rotation = Quaternion.AngleAxis(deg, axis) * Quaternion.AngleAxis(deg2, axis2) * t.rotation;
        }

        // two-bone IK: bend the elbow so shoulder to hand is as far as shoulder to target, swing the arm onto the
        // target with the elbow kept on the side it was, then give the hand back its turn
        static void Arm(Transform[] arm, Vector3 target, Quaternion handRot, Vector3 elbowWas)
        {
            Transform a = arm[0], b = arm[1], c = arm[2];
            if (a == null || b == null || c == null) return;
            float ab = Vector3.Distance(a.position, b.position), bc = Vector3.Distance(b.position, c.position);
            if (ab < 1e-5f || bc < 1e-5f) return;
            float want = Mathf.Clamp(Vector3.Distance(a.position, target), Mathf.Abs(ab - bc) + 1e-4f, ab + bc - 1e-4f);
            Vector3 ba = a.position - b.position, bcv = c.position - b.position;
            Vector3 n = Vector3.Cross(ba, bcv);
            if (n.sqrMagnitude < 1e-10f) n = Vector3.Cross(ba, elbowWas - (a.position + c.position) * 0.5f);
            if (n.sqrMagnitude < 1e-10f) return;
            float cur = Vector3.Angle(ba, bcv);
            float desired = Mathf.Acos(Mathf.Clamp((ab * ab + bc * bc - want * want) / (2f * ab * bc), -1f, 1f)) * Mathf.Rad2Deg;
            b.rotation = Quaternion.AngleAxis(desired - cur, n.normalized) * b.rotation;
            a.rotation = Quaternion.FromToRotation(c.position - a.position, target - a.position) * a.rotation;
            c.rotation = handRot;
        }
    }
}
