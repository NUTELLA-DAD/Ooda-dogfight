using Ooda.Flight.Core;
using UnityEngine;

namespace Ooda.Flight
{
    /// <summary>An aircraft's numbers as an asset, so they can be tuned in the Inspector while flying.</summary>
    [CreateAssetMenu(menuName = "OODA/Aircraft Performance", fileName = "AircraftPerformance")]
    public class AircraftPerformance : ScriptableObject
    {
        public AircraftNumbers numbers = AircraftNumbers.P51D();

        [ContextMenu("Reset to P-51D")]
        void ResetToP51D() => numbers = AircraftNumbers.P51D();
    }

    /// <summary>The flight core's frame (feet; x east, y north, z up) to Unity's (metres; x east, y up, z north).</summary>
    public static class UnityFrame
    {
        public static Vector3 Dir(Vec3d v) => new Vector3((float)v.x, (float)v.z, (float)v.y);
        public static Vector3 Pos(Vec3d p) => Dir(p) * (float)Atmosphere.MPerFt;
        public static Vec3d Dir(Vector3 v) => new Vec3d(v.x, v.z, v.y);
        public static Vec3d Pos(Vector3 p) => Dir(p) * Atmosphere.FtPerM;
    }
}
