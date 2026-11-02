using System.Linq;
using Ooda.Flight.Core;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.EditorTools
{
    /// <summary>
    /// Batch check of the flights and orders without a scene: the Squadron setup over flat ground, flown with no orders
    /// (everyone should hold formation), then Blue 2 sent off and called back, a formation change, and a bomber flown by
    /// hand. Logs "OODA squad:" lines. Unity -batchmode -executeMethod Ooda.EditorTools.SquadCheck.Run
    /// </summary>
    public static class SquadCheck
    {
        public static void Run()
        {
            var route = new[] { new Vec3d(0, 0, 8000), new Vec3d(0, 200000, 8000) };
            var b = Scenarios.Squadron(route, null, 8, 3, 135, 60000);   // the enemy far off, so nobody is shot meanwhile
            b.groundFt = (x, y) => 0;
            Debug.Log($"OODA squad: flights {string.Join(", ", b.flights.Select((f, k) => $"{f.name} {f.shape} x{Enumerable.Range(0, b.Count).Count(i => b.flightOf[i] == k)}"))}");
            Fly(b, 5, "no orders");
            int two = Enumerable.Range(0, b.Count).First(i => b.flightOf[i] == b.flightOf[0] && b.number[i] == 2);
            b.orders[two] = Order.YouControl;
            for (int t = 0; t < 3; t++)
            {
                var plans = new TurnPlan?[b.Count];
                var m = b.planes[two];
                plans[two] = new TurnPlan { aim = (m.Forward + m.Right * 0.8).Normalized, throttle = 1 };
                b.Resolve(plans, 3, false);
            }
            Debug.Log($"OODA squad: Blue 2 sent off, {Gap(b, two) * 0.3048:F0} m from its spot");
            b.orders[two] = Order.Rejoin;
            for (int t = 0; t < 16; t++)
            {
                var m = b.planes[two]; var s = b.FormationSlot(two); var l = b.planes[s.leader];
                Vec3d g = s.Where(l.state.pos, l.state.vel) - m.state.pos;
                var p = s.Plan(b, two, 3);
                Debug.Log($"OODA squad:   t{t} gap ({g.x:F0},{g.y:F0},{g.z:F0}) ft, vel ({m.state.vel.x:F0},{m.state.vel.y:F0},{m.state.vel.z:F0}) aim ({p.aim.x:F2},{p.aim.y:F2},{p.aim.z:F2}) thr {p.throttle:F2} kias {m.Kias:F0} g {m.g:F1}");
                b.Resolve(new TurnPlan?[b.Count], 3, false);
            }
            Fly(b, 0, "Blue 2 rejoining");
            b.flights[b.flightOf[0]].shape = Formation.Trail;
            Fly(b, 6, "Blue in trail");
            int bomber2 = Enumerable.Range(0, b.Count).First(i => b.routes[i] != null && b.number[i] == 2);
            b.Swap(0, bomber2);
            b.orders[0] = Order.YouControl;
            for (int t = 0; t < 2; t++)
            {
                var m = b.planes[0];
                var plans = new TurnPlan?[b.Count];
                plans[0] = new TurnPlan { aim = (m.Forward - m.Right * 0.3).Normalized, throttle = 1 };
                b.Resolve(plans, 3, false);
            }
            Debug.Log($"OODA squad: bomber 2 flown by hand, {Gap(b, 0) * 0.3048:F0} m from its spot; leads: {string.Join(", ", b.flights.Select((f, k) => $"{f.name} {b.Leader(k)}"))}");
            b.orders[0] = Order.Rejoin;
            for (int t = 0; t < 8; t++)
            {
                var m = b.planes[0]; var s = b.FormationSlot(0); var l = b.planes[s.leader];
                Vec3d g = s.Where(l.state.pos, l.state.vel) - m.state.pos;
                var p = s.Plan(b, 0, 3);
                Debug.Log($"OODA squad:   b{t} gap ({g.x:F0},{g.y:F0},{g.z:F0}) ft, vel ({m.state.vel.x:F0},{m.state.vel.y:F0},{m.state.vel.z:F0}) aim ({p.aim.x:F2},{p.aim.y:F2},{p.aim.z:F2}) thr {p.throttle:F2} kias {m.Kias:F0} lead kias {l.Kias:F0}");
                b.Resolve(new TurnPlan?[b.Count], 3, false);
            }
            Fly(b, 0, "bomber 2 rejoining");
            Debug.Log("OODA squad: done.");
        }

        static void Fly(Battle b, int turns, string what)
        {
            for (int t = 0; t < turns; t++) b.Resolve(new TurnPlan?[b.Count], 3, false);
            var gaps = Enumerable.Range(0, b.Count).Where(i => b.side[i] == 0 && b.FormationSlot(i) != null)
                .Select(i => $"{(b.flightOf[i] >= 0 ? b.flights[b.flightOf[i]].name : "?")}{b.number[i]}:{Gap(b, i) * 0.3048:F0}{(b.Out(i) ? " OUT" : "")}");
            Debug.Log($"OODA squad: after {what} ({turns} turns) gaps m {string.Join(" ", gaps)}");
        }

        static double Gap(Battle b, int i)
        {
            var s = b.FormationSlot(i);
            if (s == null) return -1;
            var l = b.planes[s.leader];
            return (s.Where(l.state.pos, l.state.vel) - b.planes[i].state.pos).Length;
        }
    }
}
