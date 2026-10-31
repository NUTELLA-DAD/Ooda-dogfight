using Ooda.Flight.Core;
using UnityEngine;

namespace Ooda.Flight
{
    /// <summary>
    /// Flies this GameObject with the OODA flight core. The core owns the motion (the transform only shows it):
    /// every physics step it reads PlaneInput, steps the point mass, and moves the kinematic Rigidbody there,
    /// turned to the path plus the angle of attack. Hitting anything with a collider (ground, water, a tower)
    /// is a crash, and the aircraft starts again where it began.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(PlaneInput))]
    public class AircraftController : MonoBehaviour
    {
        public AircraftPerformance performance;
        [Tooltip("True airspeed at the start, knots.")]
        public float startKtas = 260;
        [Tooltip("Seconds before starting again after a crash.")]
        public float respawnDelay = 2.5f;
        [Tooltip("Sideslip shown per G of rudder side force, degrees.")]
        public float slipDegPerG = 12;

        public FlightModel Model { get; private set; }
        public PlaneInput Input { get; private set; }
        public bool Crashed { get; private set; }
        public float Kias => Model == null ? 0 : (float)Model.Kias;
        public float Ktas => Model == null ? 0 : (float)Model.Ktas;
        public float AltitudeFt => Model == null ? 0 : (float)Model.AltFt;
        public float G => Model == null ? 1 : (float)Model.g;
        /// <summary>The flight path direction (Unity), not the nose.</summary>
        public Vector3 PathForward => UnityFrame.Dir(Model.Forward);
        public Vector3 LiftUp => UnityFrame.Dir(Model.Up);
        public Vector3 Velocity => UnityFrame.Dir(Model.state.vel) * (float)Atmosphere.MPerFt;

        Rigidbody body;
        Vector3 spawnPos;
        Quaternion spawnRot;
        float crashTimer;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            Input = GetComponent<PlaneInput>();
            if (performance == null) performance = ScriptableObject.CreateInstance<AircraftPerformance>();
            spawnPos = transform.position;
            spawnRot = transform.rotation;
        }

        void Start() => Restart(); // after PlaneInput has set the starting throttle

        public void Restart()
        {
            Vector3 f = spawnRot * Vector3.forward;
            var start = PointMass.Make(UnityFrame.Pos(spawnPos), startKtas, System.Math.Atan2(f.z, f.x));
            Model = new FlightModel(performance.numbers, start) { throttle = Input.Throttle };
            Crashed = false;
            transform.SetPositionAndRotation(spawnPos, Attitude());
            body.position = spawnPos;
            body.rotation = transform.rotation;
        }

        /// <summary>The aircraft's attitude: along the path, up along the lift, nose raised by the angle of attack.</summary>
        Quaternion Attitude()
        {
            var path = Quaternion.LookRotation(UnityFrame.Dir(Model.Forward), UnityFrame.Dir(Model.Up));
            return path * Quaternion.Euler(-(float)Model.AlphaDeg, (float)Model.sideG * slipDegPerG, 0);
        }

        void Update()
        {
            if (Input.ResetPressed) Restart();
        }

        void FixedUpdate()
        {
            if (Model == null) return;
            Model.numbers = performance.numbers; // so Inspector tuning takes effect while flying
            if (Crashed)
            {
                crashTimer -= Time.fixedDeltaTime;
                if (crashTimer <= 0) Restart();
                return;
            }
            var c = new FlightControls
            {
                pitch = Input.Pitch,
                roll = Input.Roll,
                yaw = Input.Yaw,
                throttle = Input.Throttle,
                wep = Input.Wep,
            };
            Vector3 from = body.position;
            Model.Step(c, Time.fixedDeltaTime);
            Vector3 to = UnityFrame.Pos(Model.state.pos);

            Vector3 d = to - from;
            if (Physics.Raycast(from, d.normalized, out RaycastHit hit, d.magnitude + 1f, ~0, QueryTriggerInteraction.Ignore)
                && !hit.transform.IsChildOf(transform))
            {
                Crashed = true;
                crashTimer = respawnDelay;
                body.MovePosition(hit.point);
                return;
            }
            body.MovePosition(to);
            body.MoveRotation(Attitude());
        }
    }
}
