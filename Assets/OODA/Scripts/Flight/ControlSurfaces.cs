using System.Collections.Generic;
using UnityEngine;

namespace Ooda.Flight
{
    /// <summary>
    /// Swings the ailerons, elevators and rudder with the flying: the elevators with the G pulled, the ailerons with
    /// the roll rate, the rudder with the pedal's side force. Like the pilot's stick, it reads them from the IFlightView
    /// above it, so it works in free flight and in the turn battle's planning and playback alike.
    /// The parts are found by name under the model (anything ending _AileronL/_AileronR, _ElevatorL/_ElevatorR,
    /// _Rudder, _RudderL/_RudderR, cut out by Tools~/ArtPipeline/control_surfaces.py). Each one's origin is one end of
    /// its hinge line and its child "..._Hinge" empty marks the other end, so the hinge axis comes from the model.
    /// </summary>
    public class ControlSurfaces : MonoBehaviour
    {
        [Tooltip("Elevator travel at full pull, degrees (trailing edge up).")]
        public float elevatorDeg = 18f;
        [Tooltip("Aileron travel at the full-stick roll rate, degrees.")]
        public float aileronDeg = 15f;
        [Tooltip("Rudder travel at full pedal, degrees.")]
        public float rudderDeg = 20f;
        [Tooltip("Roll rate (deg/s) that shows as full aileron.")]
        public float fullRollDps = 120f;

        enum Kind { Elevator, Aileron, Rudder }

        class Part
        {
            public Transform t;
            public Quaternion rest;
            public Vector3 axisParent;   // the hinge axis in the part's parent's space
            public Kind kind;
            public float sign;           // + angle about the axis moves the trailing edge the way that counts as +
            public bool rightSide;       // for an aileron: on the pilot's right
        }

        readonly List<Part> parts = new List<Part>();
        IFlightView source;
        float pitchS, rollS, yawS;

        void Start()
        {
            source = GetComponentInParent<IFlightView>();
            Transform plane = source != null ? source.transform : transform;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                Kind kind;
                if (t.name.EndsWith("_AileronL") || t.name.EndsWith("_AileronR")) kind = Kind.Aileron;
                else if (t.name.EndsWith("_ElevatorL") || t.name.EndsWith("_ElevatorR")) kind = Kind.Elevator;
                else if (t.name.EndsWith("_Rudder") || t.name.EndsWith("_RudderL") || t.name.EndsWith("_RudderR")) kind = Kind.Rudder;
                else continue;
                Transform hinge = t.Find(t.name + "_Hinge");
                if (hinge == null) { Debug.LogWarning($"OODA: {t.name} has no {t.name}_Hinge; it won't move."); continue; }
                Vector3 axis = (hinge.position - t.position).normalized;
                // the trailing edge: the middle of the part's mesh, seen from the hinge
                var r = t.GetComponent<Renderer>();
                Vector3 arm = (r != null ? r.bounds.center : t.position) - t.position;
                Vector3 moves = Vector3.Cross(axis, arm);   // which way a small + turn carries it
                Vector3 positive = kind == Kind.Rudder ? plane.right : plane.up;
                parts.Add(new Part
                {
                    t = t, rest = t.localRotation, kind = kind,
                    axisParent = t.parent.InverseTransformDirection(axis),
                    sign = Vector3.Dot(moves, positive) >= 0 ? 1f : -1f,
                    rightSide = Vector3.Dot(arm + t.position - plane.position, plane.right) > 0,
                });
            }
        }

        void Update()
        {
            var m = source?.Model;
            if (m == null || parts.Count == 0) return;
            bool still = source.Crashed;
            float pull = still ? 0 : Mathf.Clamp(((float)m.g - 1f) / 5f, -0.4f, 1f);
            float roll = still ? 0 : Mathf.Clamp((float)m.rollRateDps / fullRollDps, -1f, 1f);
            float side = m.numbers != null ? (float)m.numbers.rudderSideG : 0.3f;
            float yaw = still ? 0 : Mathf.Clamp((float)m.sideG / Mathf.Max(side, 0.05f), -1f, 1f);
            float k = 1 - Mathf.Exp(-8f * Time.deltaTime);
            pitchS = Mathf.Lerp(pitchS, pull, k); rollS = Mathf.Lerp(rollS, roll, k); yawS = Mathf.Lerp(yawS, yaw, k);
            foreach (var p in parts)
            {
                float up;   // degrees, trailing edge up (or, for a rudder, to the right)
                switch (p.kind)
                {
                    case Kind.Elevator: up = pitchS * elevatorDeg; break;
                    case Kind.Aileron: up = (p.rightSide ? rollS : -rollS) * aileronDeg; break;   // rolling right lifts the right one
                    default: up = yawS * rudderDeg; break;                                          // right pedal swings it right
                }
                p.t.localRotation = Quaternion.AngleAxis(up * p.sign, p.axisParent) * p.rest;
            }
        }
    }
}
