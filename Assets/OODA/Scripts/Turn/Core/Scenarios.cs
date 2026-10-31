using System;
using Ooda.Flight.Core;

namespace Ooda.Turn.Core
{
    /// <summary>Starting setups. More complex ones come later.</summary>
    public static class Scenarios
    {
        public const double TurnSec = 3;

        /// <summary>
        /// Head-on: Mustang (player) heading north, Spitfire heading south, 3 km apart at the same height and
        /// 300 KIAS, offset 300 ft sideways so flying straight is a pass, not a collision. centre is in feet.
        /// </summary>
        public static Battle HeadOn(Vec3d centre, double altFt = 5000, double kias = 300, double gapM = 3000)
        {
            double half = gapM / 0.3048 / 2;
            double ktas = Atmosphere.IasToTasKt(kias, altFt);
            var mustang = new FlightModel(AircraftNumbers.P51D(),
                PointMass.Make(new Vec3d(centre.x - 150, centre.y - half, altFt), ktas, Math.PI / 2));
            var spit = new FlightModel(AircraftNumbers.SpitfireIX(),
                PointMass.Make(new Vec3d(centre.x + 150, centre.y + half, altFt), ktas, -Math.PI / 2));
            mustang.throttle = spit.throttle = 1;
            return new Battle(mustang, CombatNumbers.P51D(), spit, CombatNumbers.SpitfireIX());
        }
    }
}
