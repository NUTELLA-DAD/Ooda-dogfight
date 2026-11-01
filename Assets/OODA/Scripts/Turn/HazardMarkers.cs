using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// Where the air hazards are, kept in the scene for the turn battle's planning view: every wire (barrage balloon
    /// tethers, cable-car ropes, radio masts and their guys) as a start and an end, and the barrage balloons' middles.
    /// Filled when the world is built (AlpineWorld, OODA > Build Test World); the solid parts are the colliders on the
    /// Obstacles layer, not these.
    /// </summary>
    public class HazardMarkers : MonoBehaviour
    {
        [Tooltip("Each wire's start, metres (Unity x east, y height, z north); wireEnds holds the matching ends.")]
        public Vector3[] wireStarts = new Vector3[0];
        public Vector3[] wireEnds = new Vector3[0];
        [Tooltip("The middle of each barrage balloon, metres.")]
        public Vector3[] balloons = new Vector3[0];
        public float balloonRadius = 12f;

        public int WireCount => wireStarts == null || wireEnds == null ? 0 : Mathf.Min(wireStarts.Length, wireEnds.Length);
    }
}
