using UnityEngine;

namespace Ooda.Flight
{
    /// <summary>Turns the propeller about the aircraft's nose axis, faster with more throttle.</summary>
    public class PropellerSpin : MonoBehaviour
    {
        public Transform propeller;
        public AircraftController aircraft;
        public float idleRpm = 400;
        public float maxRpm = 1400; // the Merlin's 3,000 rpm through the 0.479 reduction gear

        void Update()
        {
            if (propeller == null || aircraft == null || aircraft.Model == null) return;
            float rpm = aircraft.Crashed ? 0 : Mathf.Lerp(idleRpm, maxRpm, (float)aircraft.Model.throttle);
            propeller.Rotate(aircraft.transform.forward, rpm * 6f * Time.deltaTime, Space.World);
        }
    }
}
