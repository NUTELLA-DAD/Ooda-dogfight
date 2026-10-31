using UnityEngine;

namespace Ooda.Flight
{
    /// <summary>Turns the propeller about the aircraft's nose axis, faster with more throttle. Without an AircraftController it follows any IFlightView above it.</summary>
    public class PropellerSpin : MonoBehaviour
    {
        public Transform propeller;
        public AircraftController aircraft;
        public float idleRpm = 400;
        public float maxRpm = 1400; // the Merlin's 3,000 rpm through the 0.479 reduction gear

        IFlightView source;

        void Start()
        {
            source = aircraft != null ? (IFlightView)aircraft : GetComponentInParent<IFlightView>();
        }

        void Update()
        {
            if (propeller == null || source == null || source.Model == null) return;
            float rpm = source.Crashed ? 0 : Mathf.Lerp(idleRpm, maxRpm, (float)source.Model.throttle);
            propeller.Rotate(source.transform.forward, rpm * 6f * Time.deltaTime, Space.World);
        }
    }
}
