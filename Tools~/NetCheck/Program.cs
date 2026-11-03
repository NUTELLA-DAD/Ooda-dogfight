// Checks of the pieces online play stands on, without a network: a turn flown twice from the same plans comes out
// the same to the bit, a snapshot brings another battle exactly in step, two machines keeping the aircraft in
// different orders still fly the same turn, plans survive the trip to bytes, and the planes are shared out fairly.
// Usage: dotnet run   (exit code = failures)
using System;
using System.Linq;
using Ooda.Flight.Core;
using Ooda.Turn.Core;

static class Program
{
    static int fails;
    static void Check(bool ok, string what) { if (!ok) fails++; Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}"); }

    const double TurnSec = 3;

    static Battle Fight(int ours, int theirs) => Scenarios.Dogfight(Scenarios.Start.HeadOn, new Vec3d(0, 0, 0), ours: ours, theirs: theirs);

    /// <summary>An AI pilot in every seat (by index), as the host would run them.</summary>
    static EnemyAi[] Pilots(Battle b) => Enumerable.Range(0, b.Count).Select(i => new EnemyAi
    {
        me = i, rng = new Lcg(100u + (uint)i), ignoreObstacles = true,
        role = b.side[i] == 0 ? EnemyAi.Role.Fighter : EnemyAi.Role.Hunter,
    }).ToArray();

    static TurnPlan?[] Plan(Battle b, EnemyAi[] pilots)
    {
        var plans = new TurnPlan?[b.Count];
        for (int i = 0; i < b.Count; i++) if (!b.Out(i) && !b.InFormation(i)) plans[i] = pilots[i].Plan(b, TurnSec);
        return plans;
    }

    static int Main()
    {
        Console.WriteLine("Same plans, same turn");
        {
            var b = Fight(3, 3); var pilots = Pilots(b);
            bool same = true; int turns = 0, hits = 0;
            for (int t = 0; t < 12 && !b.Over; t++)
            {
                var plans = Plan(b, pilots);
                var again = b.Clone();
                var r = b.Resolve(plans, TurnSec, true);
                again.Resolve(plans, TurnSec, false);
                same &= Wire.Hash(b) == Wire.Hash(again);
                hits += r.hits.Count; turns++;
            }
            Check(same, $"{turns} turns of a 3 v 3, each flown twice (recorded and not), hash the same every time ({hits} hits along the way)");
            var c = b.Clone(); c.planes[2].state.pos = c.planes[2].state.pos + new Vec3d(0, 0, 1e-9);
            Check(Wire.Hash(c) != Wire.Hash(b), "a billionth of a foot on one plane changes the hash");
        }

        Console.WriteLine("\nSnapshot");
        {
            var host = Fight(2, 2); var pilots = Pilots(host);
            for (int t = 0; t < 12 && (t < 2 || host.bullets.Count == 0); t++) host.Resolve(Plan(host, pilots), TurnSec, true);   // until rounds are in the air
            var snap = Wire.Encode(host);
            var client = Fight(2, 2);   // the same mission, still at the start
            var myNumbers = client.planes[0].numbers;
            Wire.Restore(client, snap);
            Check(Wire.Hash(client) == Wire.Hash(host), $"a fresh battle restored from a {snap.Length / 1024.0:F1} KB snapshot hashes the same as the host's");
            Check(ReferenceEquals(client.planes[0].numbers, myNumbers), "the client keeps its own aircraft numbers");
            Check(host.bullets.Count > 0 && client.bullets.Count == host.bullets.Count && client.bullets.Select(x => x.GetType()).SequenceEqual(host.bullets.Select(x => x.GetType())), $"the {host.bullets.Count} rounds in the air come across");
            bool same = true;
            for (int t = 0; t < 6 && !host.Over; t++)
            {
                var plans = Plan(host, pilots);
                host.Resolve(plans, TurnSec, true); client.Resolve(plans, TurnSec, true);
                same &= Wire.Hash(client) == Wire.Hash(host);
            }
            Check(same, "and both fly on in step for 6 more turns from the same plans");
            bool threw = false;
            try { Wire.Restore(Fight(2, 3), snap); } catch (System.IO.InvalidDataException) { threw = true; }
            Check(threw, "a snapshot of a 2 v 2 refuses to land on a 2 v 3");
        }

        Console.WriteLine("\nTwo orders, one turn");
        {
            // the host keeps wire order; the friend has Tabbed about, so its lists are in another order
            var host = Fight(4, 4); var pilots = Pilots(host);
            var friend = host.Clone();   // Swap gives the friend its own side and combat arrays: a clone shares them until then
            var seats = new Seating(friend.Count, 1);
            var rnd = new Random(7);
            for (int k = 0; k < 20; k++) { int a = rnd.Next(friend.Count), c = rnd.Next(friend.Count); friend.Swap(a, c); seats.Swapped(a, c); }
            Check(!Enumerable.Range(0, seats.Count).All(i => seats.wireOf[i] == i), "the friend's order is shuffled");
            var hostSeats = new Seating(host.Count, 0);
            bool same = true, framesMatch = true, hitsMatch = true; int turns = 0;
            for (int t = 0; t < 10 && !host.Over; t++)
            {
                var wirePlans = Plan(host, pilots);   // by wire id
                var rh = host.Resolve(wirePlans, TurnSec, true);
                var rf = seats.ResolveInWireOrder(friend, seats.PlansToLocal(wirePlans), TurnSec, true);
                same &= hostSeats.Hash(host) == seats.Hash(friend);
                for (int l = 0; l < friend.Count; l++)
                    framesMatch &= rf.frames[l].Last().pos.Equals(rh.frames[seats.wireOf[l]].Last().pos);
                hitsMatch &= rf.hits.Count == rh.hits.Count && rf.hits.Zip(rh.hits, (f, h) => seats.wireOf[f.target] == h.target).All(x => x);
                turns++;
            }
            Check(same, $"{turns} turns of a 4 v 4, the friend's in its own order: the wire-order hash agrees every turn");
            Check(framesMatch && hitsMatch, "the friend's playback frames and hits come back under its own numbering");
            var snap = seats.Snapshot(friend);
            var fresh = Fight(4, 4);
            new Seating(fresh.Count, 0).Restore(fresh, snap);
            Check(Wire.Hash(fresh) == Wire.Hash(host), "the friend's snapshot, taken in wire order, lands on a battle in wire order");
        }

        Console.WriteLine("\nPlans on the wire");
        {
            var b = Fight(2, 2);
            var plans = new TurnPlan?[]
            {
                new TurnPlan { aim = new Vec3d(0.6, 0.8, 0), throttle = 0.75, wep = true, burstStart = 0.5, burstEnd = 1.25, missile = true, missileTarget = 3, overpull = 0.2 },
                null,
                new TurnPlan { throttle = 1, marks = new[] { new Mark(0, 0, 1), new Mark(1.0) { bankDeg = 60 }, new Mark(2.0) { throttle = 0, wep = false } } },
                TurnPlan.Holding(),
            };
            var bytes = Wire.Encode(plans);
            var back = Wire.Decode<TurnPlan?[]>(bytes);
            Check(Wire.Encode(back).SequenceEqual(bytes) && back[1] == null && back[2].Value.marks[1].bankDeg == 60 && back[2].Value.marks[2].g == null && back[0].Value.missileTarget == 3,
                  $"four plans (one empty, one with timeline points) in {bytes.Length} bytes and back");
            var r1 = b.Clone(); r1.Resolve(plans, TurnSec, false);
            var r2 = b.Clone(); r2.Resolve(back, TurnSec, false);
            Check(Wire.Hash(r1) == Wire.Hash(r2), "they fly the same turn as the originals");
            var seats = new Seating(b.Count, 0); seats.Swapped(0, 3);
            var w = seats.PlansToWire(seats.PlansToLocal(plans));
            Check(w[0].Value.missileTarget == 3 && seats.PlansToLocal(plans)[3].Value.missileTarget == 0, "a missile's target is renamed with the planes");
        }

        Console.WriteLine("\nSharing the planes");
        {
            string Seats(Battle b, Seating s) => string.Join(" ", Enumerable.Range(0, b.Count).Select(i => s.SeatOf(i) == Seating.Ai ? "ai" : $"p{s.SeatOf(i)}"));
            var four = Fight(4, 4); var s4 = new Seating(four.Count, 0); s4.ShareOurs(four, 2);
            int Of(Seating s, int p) => s.seat.Count(x => x == p);
            Check(Of(s4, 0) == 2 && Of(s4, 1) == 2 && Of(s4, Seating.Ai) == 4, $"4 v 4 co-op between two: two each, the 109s to the AI ({Seats(four, s4)})");
            var three = Fight(3, 2); var s3 = new Seating(three.Count, 1); s3.ShareOurs(three, 2);
            Check(Of(s3, 0) == 2 && Of(s3, 1) == 1 && s3.Mine(s3.FirstMine()) && three.side[s3.FirstMine()] == 0, $"3 v 2 co-op: the host two, the friend one ({Seats(three, s3)})");
            var vs = Fight(2, 3); var sv = new Seating(vs.Count, 1); sv.SideAgainstSide(vs);
            Check(Enumerable.Range(0, vs.Count).All(i => sv.SeatOf(i) == vs.side[i]), $"2 v 3 player against player: each flies a side ({Seats(vs, sv)})");
            vs.Swap(0, 1); sv.Swapped(0, 1);
            Check(sv.Mine(0) && vs.side[0] == 1 && !sv.Mine(1), "after a swap (Tab) the seats follow the planes");
        }

        Console.WriteLine(fails == 0 ? "\nAll checks pass." : $"\n{fails} check(s) failed.");
        return fails;
    }
}
