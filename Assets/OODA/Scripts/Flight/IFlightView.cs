using Ooda.Flight.Core;
using UnityEngine;

namespace Ooda.Flight
{
    /// <summary>
    /// Something showing an aircraft that the propeller, the pilot's head and the cameras can follow:
    /// the free-flight AircraftController, or a turn-battle aircraft playing back its frames.
    /// </summary>
    public interface IFlightView
    {
        FlightModel Model { get; }
        bool Crashed { get; }
        Transform transform { get; }
    }
}
