using UnityEngine;

namespace Ooda.Pilot
{
    /// <summary>
    /// The pilot's forearms rest on the cockpit rails: hand IK onto two points on the sills, with the elbows
    /// pushed out and back. PilotHeadLook does the look-at on the same Animator; this only sets the hand goals.
    /// </summary>
    [ExecuteAlways] // so the editor's shot tools see the pose too
    [RequireComponent(typeof(Animator))]
    public class PilotRails : MonoBehaviour
    {
        public Transform railL, railR;
        [Range(0, 1)] public float weight = 1f;
        [Tooltip("Elbow hints: how far out and back from the hands, in metres of the pilot's own scale.")]
        public float elbowOut = 0.35f, elbowBack = 0.25f, elbowDown = 0.05f;

        Animator anim;

        void Awake() => anim = GetComponent<Animator>();

        void OnAnimatorIK(int layer)
        {
            if (anim == null) anim = GetComponent<Animator>();
            Hand(AvatarIKGoal.LeftHand, AvatarIKHint.LeftElbow, railL, -1);
            Hand(AvatarIKGoal.RightHand, AvatarIKHint.RightElbow, railR, 1);
        }

        void Hand(AvatarIKGoal goal, AvatarIKHint hint, Transform rail, float side)
        {
            if (rail == null) { anim.SetIKPositionWeight(goal, 0); anim.SetIKHintPositionWeight(hint, 0); return; }
            float s = transform.lossyScale.x;
            Transform plane = rail.parent != null ? rail.parent : transform;
            anim.SetIKPositionWeight(goal, weight);
            anim.SetIKRotationWeight(goal, weight);
            anim.SetIKPosition(goal, rail.position);
            // palm down on the rail, fingers forward
            anim.SetIKRotation(goal, Quaternion.LookRotation(plane.forward, plane.up) * Quaternion.Euler(0, 0, -90f * side));
            anim.SetIKHintPositionWeight(hint, weight);
            anim.SetIKHintPosition(hint, rail.position + plane.right * (side * elbowOut * s) - plane.forward * (elbowBack * s) - plane.up * (elbowDown * s));
        }
    }
}
