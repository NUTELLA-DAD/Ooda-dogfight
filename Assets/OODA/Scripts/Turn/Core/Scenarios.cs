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

        /// <summary>The ways a one on one can begin.</summary>
        public enum Start { HeadOn, Neutral, Offensive, Defensive }

        public static string Name(Start s) => s == Start.HeadOn ? "head-on" : s == Start.Neutral ? "neutral, side by side"
                                            : s == Start.Offensive ? "you on their tail" : "them on your tail";

        /// <summary>
        /// A dogfight at 300 KIAS by how it begins: head-on 3 km apart; neutral, side by side 1,500 ft apart, both heading
        /// north; offensive, the Mustang 3,000 ft behind the Bf 109 and a little to one side; defensive, the other way round.
        /// ours and theirs (1 to 4 each) are the aircraft on each side: you and your P-51 wingmen, and the Bf 109s, each
        /// side flying a finger-four on its leader (side by side, an echelon away from the other side instead).
        /// Order: 0 you, 1 their leader, then your wingmen, then their others.
        /// </summary>
        public static Battle Dogfight(Start s, Vec3d centre, double altFt = 5000, double kias = 300, double gapM = 3000, int ours = 1, int theirs = 1)
        {
            ours = Math.Max(1, Math.Min(MaxSide, ours));
            theirs = Math.Max(1, Math.Min(MaxSide, theirs));
            double ktas = Atmosphere.IasToTasKt(kias, altFt);
            double half = gapM / 0.3048 / 2;
            Vec3d me, foe;
            double myHdg = Math.PI / 2, foeHdg = Math.PI / 2;
            switch (s)
            {
                case Start.HeadOn: me = new Vec3d(centre.x - 150, centre.y - half, altFt); foe = new Vec3d(centre.x + 150, centre.y + half, altFt); foeHdg = -Math.PI / 2; break;
                case Start.Neutral: me = new Vec3d(centre.x - 750, centre.y, altFt); foe = new Vec3d(centre.x + 750, centre.y, altFt); break;
                case Start.Offensive: me = new Vec3d(centre.x - 120, centre.y - 1500, altFt); foe = new Vec3d(centre.x + 120, centre.y + 1500, altFt + 100); break;
                default: me = new Vec3d(centre.x + 120, centre.y + 1500, altFt + 100); foe = new Vec3d(centre.x - 120, centre.y - 1500, altFt); break;
            }
            FlightModel Make(AircraftNumbers n, Vec3d pos, double hdg) => new FlightModel(n, PointMass.Make(pos, ktas, hdg)) { throttle = 1 };
            // the k-th aircraft's place on its leader: lateral (right positive), back, up, ft
            bool echelon = s == Start.Neutral;
            Vec3d Place(Vec3d lead, double hdg, int k, double away)
            {
                Vec3d dir = new Vec3d(Math.Cos(hdg), Math.Sin(hdg), 0), right = new Vec3d(dir.y, -dir.x, 0);
                var (lat, back, up) = echelon ? (500.0 * k * away, 300.0 * k, 50.0 * k) : FingerFour[k];
                return lead + right * lat - dir * back + new Vec3d(0, 0, up);
            }
            var battle = new Battle(Make(AircraftNumbers.P51D(), me, myHdg), CombatNumbers.P51D(), Make(AircraftNumbers.Bf109G(), foe, foeHdg), CombatNumbers.Bf109G());
            for (int k = 1; k < ours; k++) battle.Add(Make(AircraftNumbers.P51D(), Place(me, myHdg, k, -1), myHdg), CombatNumbers.P51D(), 0);
            for (int k = 1; k < theirs; k++) battle.Add(Make(AircraftNumbers.Bf109G(), Place(foe, foeHdg, k, 1), foeHdg), CombatNumbers.Bf109G(), 1);
            return battle;
        }

        /// <summary>The most aircraft on each side of a dogfight.</summary>
        public const int MaxSide = 4;

        /// <summary>A finger-four on its leader: lateral (right positive), back, up, ft. Wide, so the AI has room to fight.</summary>
        static readonly (double lat, double back, double up)[] FingerFour = { (0, 0, 0), (-450, 350, 60), (450, 350, -60), (900, 700, -120) };

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
            if (bombers > 0) battle.playerSlot = new Slot { leader = 2, lateralFt = escort[0].Item1, backFt = escort[0].Item2, upFt = escort[0].Item3 };
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

        /// <summary>
        /// Valley Run: the player and one P-51 wingman low in the main valley, belowRouteFt under the escort route
        /// (about 250 m over the floor, among the cable-car ropes and the barrage balloons' tethers), startAlongM up the
        /// route and heading up it, chasing two Bf 109s gapM further up the valley at the route's height, heading the same
        /// way. Flak guns sit at flakSites if given. Order: 0 the player, 1 the lead Bf 109, 2 the wingman, 3 the other
        /// Bf 109. Win when both 109s are down. route is in feet, like Escort's.
        /// </summary>
        public static Battle ValleyRun(Vec3d[] route, Vec3d[] flakSites = null, double startAlongM = 4400, double gapM = 1800,
                                       double belowRouteFt = 165, double kias = 280)
        {
            Vec3d start = PointAlong(route, startAlongM / 0.3048, out Vec3d dir);
            Vec3d right = new Vec3d(dir.y, -dir.x, 0);
            double heading = Math.Atan2(dir.y, dir.x);
            Vec3d ahead = PointAlong(route, (startAlongM + gapM) / 0.3048, out Vec3d edir);
            Vec3d eright = new Vec3d(edir.y, -edir.x, 0);
            double ehead = Math.Atan2(edir.y, edir.x);
            FlightModel Make(AircraftNumbers n, Vec3d pos, double ias, double hdg, double thr) =>
                new FlightModel(n, PointMass.Make(pos, Atmosphere.IasToTasKt(ias, pos.z), hdg)) { throttle = thr };

            Vec3d me = start + new Vec3d(0, 0, -belowRouteFt);
            Vec3d wing = me + right * 250 - dir * 400 + new Vec3d(0, 0, 60);              // on your right, a little back and up
            Vec3d lead = ahead + eright * -120 + new Vec3d(0, 0, -100);
            Vec3d second = ahead + eright * 200 - edir * 450 + new Vec3d(0, 0, 50);
            var battle = new Battle(
                Make(AircraftNumbers.P51D(), me, kias, heading, 0.9), CombatNumbers.P51D(),
                Make(AircraftNumbers.Bf109G(), lead, kias - 10, ehead, 0.85), CombatNumbers.Bf109G());
            battle.Add(Make(AircraftNumbers.P51D(), wing, kias, heading, 0.9), CombatNumbers.P51D(), 0);
            battle.Add(Make(AircraftNumbers.Bf109G(), second, kias - 10, ehead, 0.85), CombatNumbers.Bf109G(), 1);
            if (flakSites != null)
            {
                battle.flak = new FlakSite[flakSites.Length];
                for (int k = 0; k < flakSites.Length; k++)
                {
                    var g = new FlakSite { pos = flakSites[k] };
                    g.phase = g.burstSec + g.pauseSec - 0.05 - 0.37 * k % (g.burstSec + g.pauseSec - 0.1);
                    battle.flak[k] = g;
                }
            }
            return battle;
        }

        /// <summary>
        /// Town Strike: the Squadron's formation (3 B-17s, you and 7 P-51s) flying route, the path planned on the map,
        /// from the coast to the factory and on past it. Of the 8 Bf 109s, the first patrolFighters fly patrolLoop (a
        /// ring round the town, in feet) in a finger-four, starting on the far side of it from route's start; the rest
        /// wait on the runway (from runwayStart toward runwayEnd) until the strike's look-out sees you. Flak at flakSites.
        /// </summary>
        public static Battle TownStrike(Vec3d[] route, Vec3d factory, Vec3d[] radars, Vec3d[] patrolLoop, Vec3d runwayStart, Vec3d runwayEnd,
                                        Vec3d[] flakSites = null, double bomberKias = 160, int patrolFighters = 4, double patrolKias = 260)
        {
            var battle = Squadron(route, flakSites, 8, 3, bomberKias, 8000);
            for (int i = 0; i < battle.Count; i++)
                if (battle.routes[i] != null) { battle.routes[i].maxClimbDeg = StrikeClimbDeg; battle.routes[i].maxDescentDeg = StrikeDescentDeg; }
            Vec3d along = runwayEnd - runwayStart;
            var s = new Strike
            {
                target = factory, radars = radars ?? new Vec3d[0], runwayStart = runwayStart, runwayDir = Flat(along),
                runwayFt = Math.Sqrt(along.x * along.x + along.y * along.y),
            };
            s.Size(battle.Count);
            battle.strike = s;

            var foes = new System.Collections.Generic.List<int>();
            for (int i = 0; i < battle.Count; i++) if (battle.side[i] != 0) foes.Add(i);

            // the patrol: on the loop, starting at the point furthest from where the bombers come in, flying round it
            int far = 0; double farD = -1;
            for (int k = 0; k < patrolLoop.Length; k++)
            {
                double d = (patrolLoop[k] - route[0]).Length;
                if (d > farD) { farD = d; far = k; }
            }
            var loop = new Vec3d[patrolLoop.Length];
            for (int k = 0; k < loop.Length; k++) loop[k] = patrolLoop[(far + 1 + k) % loop.Length];
            Vec3d at = patrolLoop[far];
            Vec3d dir = Flat(loop[0] - at), right = new Vec3d(dir.y, -dir.x, 0);
            var four = new[] { (0.0, 0.0, 0.0), (-300, 250, 30), (450, 300, -30), (750, 550, -60) };   // a finger-four: lateral, back, up (ft)
            var scramble = new System.Collections.Generic.List<int>();
            for (int f = 0; f < foes.Count; f++)
            {
                int i = foes[f];
                var m = battle.planes[i];
                if (f < patrolFighters)
                {
                    var (lat, back, up) = four[f % four.Length];
                    Vec3d p = at + right * lat - dir * (back + 1200 * (f / four.Length)) + new Vec3d(0, 0, up);
                    m.state = PointMass.Make(p, Atmosphere.IasToTasKt(patrolKias, p.z), Math.Atan2(dir.y, dir.x));
                    m.throttle = 0.8;
                    s.patrols[i] = new Route { points = Offset(loop, lat), kias = patrolKias, maxTurnDeg = 30, maxClimbDeg = 5, reachFt = 2500, loop = true };
                }
                else
                {
                    s.held[i] = true;
                    s.Park(m, scramble.Count);
                    scramble.Add(i);
                }
            }
            s.scramble = scramble.ToArray();
            return battle;
        }

        /// <summary>How steeply the strike's bombers climb (about what a B-17 holds at 160 KIAS) and come down toward the next point's height, degrees.</summary>
        public const double StrikeClimbDeg = 3, StrikeDescentDeg = 6;

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
