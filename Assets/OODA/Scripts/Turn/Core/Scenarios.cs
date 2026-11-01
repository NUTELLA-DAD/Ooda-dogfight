using System;
using Ooda.Flight.Core;

namespace Ooda.Turn.Core
{
    /// <summary>Starting setups. More complex ones come later.</summary>
    public static class Scenarios
    {
        public const double TurnSec = 3;

        /// <summary>
        /// Head-on: Mustang (player) heading north, Bf 109 heading south, 3 km apart at the same height and
        /// 300 KIAS, offset 300 ft sideways so flying straight is a pass, not a collision. centre is in feet.
        /// </summary>
        public static Battle HeadOn(Vec3d centre, double altFt = 5000, double kias = 300, double gapM = 3000)
        {
            double half = gapM / 0.3048 / 2;
            double ktas = Atmosphere.IasToTasKt(kias, altFt);
            var mustang = new FlightModel(AircraftNumbers.P51D(),
                PointMass.Make(new Vec3d(centre.x - 150, centre.y - half, altFt), ktas, Math.PI / 2));
            var foe = new FlightModel(AircraftNumbers.Bf109G(),
                PointMass.Make(new Vec3d(centre.x + 150, centre.y + half, altFt), ktas, -Math.PI / 2));
            mustang.throttle = foe.throttle = 1;
            return new Battle(mustang, CombatNumbers.P51D(), foe, CombatNumbers.Bf109G());
        }
    
        /// <summary>
        /// Escort: a B-17 flying its route (waypoints in feet; the first is where it starts) at about 135 KIAS,
        /// the player's Mustang tucked in 600 ft above, 300 ft out to the right and 800 ft behind, and the
        /// Bf 109 coming at them from aheadGapM along the route, 2,000 ft higher, at 300 KIAS.
        /// The bomber is aircraft 2; it is on the player's side.
        /// </summary>
        public static Battle Escort(Vec3d[] route, double bomberKias = 135, double aheadGapM = 5000)
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
            var foe = new FlightModel(AircraftNumbers.Bf109G(), PointMass.Make(sPos, Atmosphere.IasToTasKt(300, sPos.z), heading + Math.PI));
            foe.throttle = 1;

            var battle = new Battle(mustang, CombatNumbers.P51D(), foe, CombatNumbers.Bf109G());
            var r = new Route { kias = bomberKias, points = route, next = 1 };
            battle.Add(bomber, CombatNumbers.B17G(), 0, r);
            AddGunners(battle);
            return battle;
        }
    
        /// <summary>
        /// The escort battle: a vic of bombers (B-17s, the lead on the route, the others 350 ft out to each side
        /// and 300 ft back) flying the route at bomberKias; the player and wingmen (P-51s) around and above them,
        /// a little faster; and a squadron of Bf 109s coming down the route from aheadM metres away, the first four
        /// 1,500 ft above the route and the second four 1,000 ft higher still. Flak guns sit at flakSites.
        /// Order: 0 the player, 1 the enemy leader, then the bombers, then the wingmen, then the other enemies.
        /// </summary>
        public static Battle Squadron(Vec3d[] route, Vec3d[] flakSites = null, int fighters = 8, int bombers = 3,
                                      double bomberKias = 135, double aheadM = 8000)
        {
            Vec3d start = route[0];
            Vec3d dir = Flat(route[1] - start);
            Vec3d right = new Vec3d(dir.y, -dir.x, 0);
            double heading = Math.Atan2(dir.y, dir.x);
            FlightModel Make(AircraftNumbers n, Vec3d pos, double kias, double hdg, double thr)
            {
                var m = new FlightModel(n, PointMass.Make(pos, Atmosphere.IasToTasKt(kias, pos.z), hdg)) { throttle = thr };
                return m;
            }
            Vec3d Around(Vec3d c, Vec3d d, Vec3d r, double lateral, double back, double up) => c + r * lateral - d * back + new Vec3d(0, 0, up);

            // the escorts (P-51s), in close formation over the bombers: lateral, back, up (ft) from the lead bomber.
            // Two finger-fours, wingtips about a span apart: yours on the right (you lead it), the other on the left, higher.
            // They hold it until the Bf 109s arrive (Slot.BreakFt) or one of ours is hit, then break and fight.
            var escort = new[] { (250.0, 450.0, 350.0), (190, 510, 350), (310, 510, 350), (370, 570, 350),
                                 (-250, 650, 550), (-190, 710, 550), (-310, 710, 550), (-370, 770, 550) };
            // the enemy: along the route ahead, coming the other way
            Vec3d at = PointAlong(route, aheadM / 0.3048, out Vec3d edir);
            Vec3d eright = new Vec3d(-edir.y, edir.x, 0);   // their right, flying back down the route
            double ehead = Math.Atan2(-edir.y, -edir.x);
            var foe = new[] { (150.0, 0.0, 1500.0), (-150, 0, 1500), (450, 600, 1500), (-450, 600, 1500), (150, 1500, 2500), (-150, 1500, 2500), (450, 2100, 2500), (-450, 2100, 2500) };

            var battle = new Battle(
                Make(AircraftNumbers.P51D(), Around(start, dir, right, escort[0].Item1, escort[0].Item2, escort[0].Item3), bomberKias, heading, 0.6), CombatNumbers.P51D(),
                Make(AircraftNumbers.Bf109G(), Around(at, -edir, eright, foe[0].Item1, foe[0].Item2, foe[0].Item3), 300, ehead, 1), CombatNumbers.Bf109G());
            var vic = new[] { (0.0, 0.0), (-220, 250), (220, 250) };   // a tight vic: the valley is narrow
            for (int b = 0; b < bombers; b++)
            {
                var (lat, back) = vic[b % vic.Length];
                if (b >= vic.Length) back += 600 * (b / vic.Length);
                var r = new Route { kias = bomberKias, points = Offset(route, lat), next = 1 };
                battle.Add(Make(AircraftNumbers.B17G(), Around(start, dir, right, lat, back, 0), bomberKias, heading, 0.8), CombatNumbers.B17G(), 0, r);
            }
            for (int w = 1; w < fighters; w++)
            {
                var e = escort[w % escort.Length];
                double back = e.Item2 + 900 * (w / escort.Length);
                int k = battle.Add(Make(AircraftNumbers.P51D(), Around(start, dir, right, e.Item1, back, e.Item3), bomberKias, heading, 0.6), CombatNumbers.P51D(), 0);
                if (bombers > 0) battle.slots[k] = new Slot { leader = 2, lateralFt = e.Item1, backFt = back, upFt = e.Item3 };
            }
            for (int w = 1; w < fighters; w++)
            {
                var e = foe[w % foe.Length];
                battle.Add(Make(AircraftNumbers.Bf109G(), Around(at, -edir, eright, e.Item1, e.Item2 + 900 * (w / foe.Length), e.Item3), 300, ehead, 1), CombatNumbers.Bf109G(), 1);
            }
            if (flakSites != null)
            {
                battle.flak = new FlakSite[flakSites.Length];
                for (int k = 0; k < flakSites.Length; k++)
                {
                    var g = new FlakSite { pos = flakSites[k] };
                    g.phase = g.burstSec + g.pauseSec - 0.05 - 0.37 * k % (g.burstSec + g.pauseSec - 0.1);   // staggered, all ready early in the first turn
                    battle.flak[k] = g;
                }
            }
            AddGunners(battle);
            return battle;
        }

        /// <summary>Three gun positions on each bomber (the ones flying routes), after any flak guns, staggered so they don't all fire at once.</summary>
        public static void AddGunners(Battle battle, int perBomber = 3)
        {
            var list = new System.Collections.Generic.List<FlakSite>(battle.flak);
            for (int i = 0; i < battle.Count; i++)
            {
                if (battle.routes[i] == null) continue;
                for (int k = 0; k < perBomber; k++)
                {
                    var g = FlakSite.B17Gunner(i, battle.side[i]);
                    g.pos = battle.planes[i].state.pos; g.vel = battle.planes[i].state.vel;
                    g.phase = (g.burstSec + g.pauseSec) * (k + 0.5) / perBomber;
                    list.Add(g);
                }
            }
            battle.flak = list.ToArray();
        }

        static Vec3d Flat(Vec3d d) => new Vec3d(d.x, d.y, 0).Normalized;

        /// <summary>The point distFt along a route from its start (height included), and the route's direction there.</summary>
        public static Vec3d PointAlong(Vec3d[] route, double distFt, out Vec3d dir)
        {
            for (int i = 0; i + 1 < route.Length; i++)
            {
                Vec3d leg = route[i + 1] - route[i];
                double len = Math.Sqrt(leg.x * leg.x + leg.y * leg.y);
                dir = Flat(leg);
                if (distFt <= len || i + 2 == route.Length) return route[i] + leg * Math.Min(1, distFt / Math.Max(1, len));
                distFt -= len;
            }
            dir = new Vec3d(0, 1, 0);
            return route[0];
        }

        /// <summary>The route moved sideways by lateralFt (right positive), each point along the average of its legs' normals.</summary>
        public static Vec3d[] Offset(Vec3d[] route, double lateralFt)
        {
            if (lateralFt == 0) return route;
            var o = new Vec3d[route.Length];
            for (int i = 0; i < route.Length; i++)
            {
                Vec3d a = Flat(route[Math.Min(i + 1, route.Length - 1)] - route[Math.Max(i - 1, 0)]);
                o[i] = route[i] + new Vec3d(a.y, -a.x, 0) * lateralFt;
            }
            return o;
        }
    }
}
