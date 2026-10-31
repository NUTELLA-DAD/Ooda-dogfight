using Ooda.Flight;
using UnityEngine;

namespace Ooda.Pilot
{
    /// <summary>
    /// The pilot looks around, with the Humanoid look-at IK (the Animator layer needs IK Pass on).
    /// With a target he watches it while it is anywhere he could turn his head to; otherwise he looks
    /// into the turn (up through the canopy as the G builds, toward the low wing as he rolls), with slow
    /// glances to check six. In the cockpit camera his head is shrunk away so it does not block the view.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class PilotHeadLook : MonoBehaviour
    {
        public AircraftController aircraft;
        [Tooltip("Something to watch, such as a bandit. Empty: he looks into the turn.")]
        public Transform target;
        [Range(0, 1)] public float bodyWeight = 0.25f;
        [Range(0, 1)] public float headWeight = 1f;
        [Range(0, 1)] public float clampWeight = 0.5f;
        public float turnSharpness = 4f;
        public float glanceDegrees = 55f;

        [HideInInspector] public bool hideHead;

        Animator anim;
        Transform head;
        Vector3 lookDir = Vector3.forward;
        float seed;

        void Awake()
        {
            anim = GetComponent<Animator>();
            head = anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
            seed = Random.value * 100f;
        }

        void OnAnimatorIK(int layer)
        {
            if (aircraft == null || aircraft.Model == null || head == null) return;
            Transform plane = aircraft.transform;
            Vector3 want;
            Vector3 toTarget = target != null ? target.position - head.position : Vector3.zero;
            if (target != null && Vector3.Angle(plane.forward, toTarget) < 150f)
            {
                want = toTarget.normalized;
            }
            else
            {
                var m = aircraft.Model;
                float pull = Mathf.Clamp01(((float)m.g - 1f) / 5f);
                float roll = Mathf.Clamp((float)m.rollRateDps / 90f, -1, 1);
                // slow glances left and right, as if checking six
                float t = Time.time * 0.25f + seed;
                float glance = (Mathf.PerlinNoise(t, 0) - 0.5f) * 2f * glanceDegrees * (1 - pull);
                float nod = (Mathf.PerlinNoise(0, t) - 0.5f) * 20f;
                Vector3 local = Quaternion.Euler(-60f * pull + nod, glance + 35f * roll, 0) * Vector3.forward;
                want = plane.rotation * local;
            }
            lookDir = Vector3.Slerp(lookDir, want, 1 - Mathf.Exp(-turnSharpness * Time.deltaTime));
            anim.SetLookAtWeight(1f, bodyWeight, headWeight, 0f, clampWeight);
            anim.SetLookAtPosition(head.position + lookDir * 10f);
        }

        void LateUpdate()
        {
            if (head != null) head.localScale = hideHead ? Vector3.one * 0.001f : Vector3.one;
        }
    }
}
