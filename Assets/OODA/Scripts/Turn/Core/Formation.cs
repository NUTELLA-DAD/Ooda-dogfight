using System;
using System.Collections.Generic;
using Ooda.Flight.Core;

namespace Ooda.Turn.Core
{
    /// <summary>The shapes a flight of up to four can fly. Each number (1 the lead to 4) has its own spot in every shape.</summary>
    public enum Formation { FingerFour, SpreadFour, Vic, Diamond, Box, EchelonRight, EchelonLeft, Trail }

    /// <summary>
    /// What one of your planes does when you don't plan its turn. Rejoin: hold its spot in the flight's formation (or
    /// fly back to it); a flight lead holds its station on the bombers, or flies itself when there is none. AutoFight:
    /// its AI pilot fights (a bomber flies its route). YouControl: it flies the plans you give it, and is asked about
    /// when you press Fly without planning it.
    /// </summary>
    public enum Order { Rejoin, AutoFight, YouControl }

    /// <summary>
    /// A flight: up to four of one side's aircraft numbered 1 to 4 (Battle.flightOf, Battle.number), flying a shape on
    /// whichever of them has the lowest number and is still flying. The shape's spots are in units of unit (lateral,
    /// back, up, feet). A flight with stationOn set escorts that flight: its lead holds the spot stationLat/Back/Up from
    /// the other flight's lead.
    /// </summary>
    public class FlightGroup
    {
        public string name;
        public Formation shape = Formation.FingerFour;
        public Vec3d unit = new Vec3d(60, 60, 15);
        public int stationOn = -1;
        public double stationLat, stationBack, stationUp;

        public FlightGroup Copy() => (FlightGroup)MemberwiseClone();
    }

    public static class Formations
    {
        public static readonly Formation[] All = (Formation[])Enum.GetValues(typeof(Formation));

        public static string Name(Formation f)
        {
            switch (f)
            {
                case Formation.FingerFour: return "Finger Four";
                case Formation.SpreadFour: return "Spread Four";
                case Formation.EchelonRight: return "Echelon R";
                case Formation.EchelonLeft: return "Echelon L";
                case Formation.Trail: return "Trail";
                case Formation.Diamond: return "Diamond";
                case Formation.Vic: return "Vic";
                default: return "Box";
            }
        }

        public static string Name(Order o) => o == Order.Rejoin ? "Rejoin" : o == Order.AutoFight ? "Auto Fight" : "You Control";

        /// <summary>Number n's spot (1 to 4) in a shape, in units: lateral (right positive), back, up.</summary>
        public static Vec3d Spot(Formation f, int n)
        {
            n = Math.Max(1, Math.Min(4, n));
            if (n == 1) return new Vec3d(0, 0, 0);
            switch (f)
            {
                case Formation.FingerFour:   // the lead's wingman on the left, the element of two on the right
                    return n == 2 ? new Vec3d(-1, 1, 1) : n == 3 ? new Vec3d(1, 1, -1) : new Vec3d(2, 2, -2);
                case Formation.SpreadFour:   // two elements line abreast, well apart, each wingman a little back and out
                    return n == 2 ? new Vec3d(-1.5, 0.5, 0.5) : n == 3 ? new Vec3d(4, 0, 0) : new Vec3d(5.5, 0.5, 0.5);
                case Formation.EchelonRight: return new Vec3d(n - 1, n - 1, -(n - 1));
                case Formation.EchelonLeft: return new Vec3d(-(n - 1), n - 1, -(n - 1));
                case Formation.Trail: return new Vec3d(0, 1.6 * (n - 1), -(n - 1));
                case Formation.Diamond:      // 4 in the slot, behind and a little below the lead
                    return n == 2 ? new Vec3d(-1, 1, 0) : n == 3 ? new Vec3d(1, 1, 0) : new Vec3d(0, 2, -1);
                case Formation.Vic:          // a vee of three, 4 trailing well back
                    return n == 2 ? new Vec3d(-1, 1, 0) : n == 3 ? new Vec3d(1, 1, 0) : new Vec3d(0, 3, -1);
                default:                     // the offset box: 2 high left, 3 low right, 4 low behind the lead
                    return n == 2 ? new Vec3d(-1, 1, 2) : n == 3 ? new Vec3d(1, 1, -2) : new Vec3d(0, 2, -4);
            }
        }

        /// <summary>Number n's spot in feet, from the flight's lead flying number lead.</summary>
        public static Vec3d SpotFt(FlightGroup fl, int n, int lead)
        {
            Vec3d d = Spot(fl.shape, n) - Spot(fl.shape, lead);
            return new Vec3d(d.x * fl.unit.x, d.y * fl.unit.y, d.z * fl.unit.z);
        }

        /// <summary>
        /// Put a side's aircraft into flights: the route flyers (bombers) in one, the fighters in fours in index order
        /// (so aircraft 0, the player, leads the first). Each flight takes the shape nearest to where its planes start.
        /// When there are bombers, each fighter flight's station on them is where its lead starts.
        /// </summary>
        public static void Organise(Battle b, int onSide, Vec3d fighterUnit, Vec3d bomberUnit)
        {
            var fighters = new List<int>();
            var bombers = new List<int>();
            for (int i = 0; i < b.Count; i++)
                if (b.side[i] == onSide) (b.routes[i] != null ? bombers : fighters).Add(i);
            int bomberFlight = -1;
            if (bombers.Count > 0)
            {
                bomberFlight = b.flights.Count;
                for (int k = 0; k < bombers.Count; k += 4) Make(b, bombers.GetRange(k, Math.Min(4, bombers.Count - k)), bomberUnit, k == 0 ? "Bombers" : "Bombers " + (k / 4 + 1), -1);
            }
            string[] names = { "Blue", "Red", "Green", "Gold" };
            for (int k = 0; k < fighters.Count; k += 4)
                Make(b, fighters.GetRange(k, Math.Min(4, fighters.Count - k)), fighterUnit, names[(k / 4) % names.Length], bomberFlight);
        }

        static void Make(Battle b, List<int> who, Vec3d unit, string name, int stationOn)
        {
            int f = b.flights.Count;
            var fl = new FlightGroup { name = name, unit = unit };
            b.flights.Add(fl);
            for (int k = 0; k < who.Count; k++) { b.flightOf[who[k]] = f; b.number[who[k]] = k + 1; b.orders[who[k]] = Order.Rejoin; }
            var lead = b.planes[who[0]];
            Vec3d Local(Vec3d p, FlightModel l)
            {
                Vec3d d = new Vec3d(l.state.vel.x, l.state.vel.y, 0);
                d = d.Length < 1e-6 ? new Vec3d(0, 1, 0) : d.Normalized;
                Vec3d r = new Vec3d(d.y, -d.x, 0), rel = p - l.state.pos;
                return new Vec3d(Vec3d.Dot(rel, r), -Vec3d.Dot(rel, d), rel.z);
            }
            // the shape nearest to where they start
            double best = double.PositiveInfinity;
            var nearest = Formation.FingerFour;
            foreach (var shape in All)
            {
                fl.shape = shape;
                double err = 0;
                for (int k = 1; k < who.Count; k++) err += (SpotFt(fl, k + 1, 1) - Local(b.planes[who[k]].state.pos, lead)).Length;
                if (err < best - 1e-6) { best = err; nearest = shape; }
            }
            fl.shape = nearest;
            if (stationOn >= 0)
            {
                int on = b.Leader(stationOn);
                if (on >= 0)
                {
                    Vec3d at = Local(lead.state.pos, b.planes[on]);
                    fl.stationOn = stationOn; fl.stationLat = at.x; fl.stationBack = at.y; fl.stationUp = at.z;
                }
            }
        }
    }
}
