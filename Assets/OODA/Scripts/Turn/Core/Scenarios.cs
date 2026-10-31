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
    
        /// <summary>
        /// Escort: a B-17 flying its route (waypoints in feet; the first is where it starts) at about 160 KIAS,
        /// the player's Mustang tucked in 600 ft above, 300 ft out to the right and 800 ft behind, and the
        /// Spitfire coming at them from aheadGapM along the route, 2,000 ft higher, at 300 KIAS.
        /// The bomber is aircraft 2; it is on the player's side.
        /// </summary>
        public static Battle Escort(Vec3d[] route, double bomberKias = 160, double aheadGapM = 5000)
        {
            Vec3d start = route[0];
            Vec3d dir = new Vec3d(route[1].x - start.x, route[1].y - start.y, 0).Normalized;
            Vec3d right = new Vec3d(dir.y, -dir.x, 0);
            double heading = Math.Atan2(dir.y, dir.x);
            double alt = start.z;

            var bomber = new FlightModel(AircraftNumbers.B17G(), PointMass.Make(start, Atmosphere.IasToTasKt(bomberKias, alt), heading));
            bomber.throttle = 0.8;
            Vec3d pPos = start + dir * -800 + right * 300 + new Vec3d(0, 0, 600);
            var mustang = new FlightModel(AircraftNumbers.P51D(), PointMass.Make(pPos, Atmosphere.IasToTasKt(bomberKias + 10, pPos.z), heading));
            mustang.throttle = 0.6;
            Vec3d sPos = start + dir * (aheadGapM / 0.3048) + right * -400 + new Vec3d(0, 0, 2000);
            var spit = new FlightModel(AircraftNumbers.SpitfireIX(), PointMass.Make(sPos, Atmosphere.IasToTasKt(300, sPos.z), heading + Math.PI));
            spit.throttle = 1;

            var battle = new Battle(mustang, CombatNumbers.P51D(), spit, CombatNumbers.SpitfireIX());
            var r = new Route { kias = bomberKias, points = route, next = 1 };
            battle.Add(bomber, CombatNumbers.B17G(), 0, r);
            return battle;
        }
    }
}
