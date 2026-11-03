// Checks of the pieces online play stands on, without a network: a turn flown twice from the same plans comes out
// the same to the bit, a snapshot brings another battle exactly in step, two machines keeping the aircraft in
// different orders still fly the same turn, plans survive the trip to bytes, and the planes are shared out fairly.
// Usage: dotnet run   (exit code = failures)
using System;
using System.Linq;
using Ooda.Flight.Core;
using Ooda.Turn.Core;
using Ooda.Net;

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


        Console.WriteLine("\nOver the network (localhost)");
        {
            bool Until(Func<bool> done, double sec = 5)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (!done() && sw.Elapsed.TotalSeconds < sec) System.Threading.Thread.Sleep(5);
                return done();
            }
            var host = NetSession.Host("test", "Kenny", 0);
            var friend = NetSession.Join("127.0.0.1", host.link.Port, "test", "Sam");
            Check(Until(() => { host.Pump(); friend.Pump(); return host.Ready && friend.Ready; }), $"host and friend connect on port {host.link.Port} and say hello");
            Check(host.friendName == "Sam" && friend.friendName == "Kenny", "each knows the other's name");

            foreach (bool versus in new[] { false, true })
            {
                var dog = new DogfightSetup { ours = new[] { Craft.Mustang, Craft.Spitfire, Craft.Phantom }, theirs = new[] { Craft.Bf109, Craft.Bf109, Craft.Mustang }, randomStart = true, easyAimOurs = !versus }.Resolved(new Random(versus ? 3 : 4));
                var setup = new SetupMsg { dogfight = dog.ToBytes(), versus = versus, fight = versus ? 2 : 1 };
                host.Send(Msg.Setup, setup);
                Check(Until(() => { friend.Pump(); if (friend.TryNext(out var ty, out var d) && ty == Msg.Setup) friend.setup = NetSession.Read<SetupMsg>(d); else if (ty != 0) Console.WriteLine("got " + ty); return friend.setup != null && friend.setup.fight == setup.fight; }), $"the friend gets the {(versus ? "versus" : "co-op")} fight's setup");
                host.setup = setup;
                Battle Build(SetupMsg st) => Scenarios.Dogfight(DogfightSetup.FromBytes(st.dogfight), new Vec3d(0, 0, 0));
                var hb = Build(host.setup); var fb = Build(friend.setup);
                host.SeatFor(hb); friend.SeatFor(fb);
                // the friend sits in its own first plane, as the battle screen does
                int first = friend.seating.FirstMine(); fb.Swap(0, first); friend.seating.Swapped(0, first);
                Check(friend.seating.Mine(0) && fb.side[0] == (versus ? 1 : 0), $"the friend is in its own plane, a {fb.planes[0].numbers.name}");
                var hp = Pilots(hb); var fp = Pilots(fb);
                for (int i = 0; i < fb.Count; i++) fp[i].me = i;
                bool same = true, ordersCame = true; int turns = 0;
                for (int t = 0; t < 8 && !hb.Over; t++)
                {
                    // each plans its own planes (the host the AI's too); the friend changes an order on its plane
                    var fplans = Plan(fb, fp);
                    fb.orders[0] = t % 2 == 0 ? Order.YouControl : Order.AutoFight;
                    friend.Send(Msg.Plans, Lockstep.Mine(fb, friend.seating, fplans, false, setup.fight));
                    var hplans = Plan(hb, hp);
                    var mine = Lockstep.Mine(hb, host.seating, hplans, true, setup.fight);
                    PlansMsg got = null;
                    Until(() => { host.Pump(); if (host.TryNext(out var ty, out var d) && ty == Msg.Plans) got = NetSession.Read<PlansMsg>(d); return got != null; });
                    var turn = Lockstep.Merge(hb, host.seating, mine, got, 1, TurnSec);
                    Lockstep.Apply(hb, host.seating, turn);
                    host.seating.ResolveInWireOrder(hb, host.seating.PlansToLocal(turn.plans), turn.turnSec);
                    turn.hash = host.seating.Hash(hb);
                    host.Send(Msg.Turn, turn);
                    TurnMsg tm = null;
                    Until(() => { friend.Pump(); if (friend.TryNext(out var ty, out var d) && ty == Msg.Turn) tm = NetSession.Read<TurnMsg>(d); return tm != null; });
                    Lockstep.Apply(fb, friend.seating, tm);
                    ordersCame &= hb.orders[host.seating.LocalOf(friend.seating.wireOf[0])] == fb.orders[0];
                    friend.seating.ResolveInWireOrder(fb, friend.seating.PlansToLocal(tm.plans), tm.turnSec);
                    same &= friend.seating.Hash(fb) == tm.hash;
                    turns++;
                }
                Check(same && turns > 3, $"{(versus ? "versus" : "co-op")} 3 v 3 of mixed types ({dog.start}): {turns} turns through the sockets, the friend's hash matches the host's every turn");
                Check(ordersCame, "the friend's orders for its planes reach the host's battle");
            }

            // a resync: the friend's battle knocked out of step takes the host's snapshot
            {
                host.setup = new SetupMsg(); friend.setup = new SetupMsg();   // co-op
                var hb = Scenarios.Dogfight(Scenarios.Start.HeadOn, new Vec3d(0, 0, 0), ours: 2, theirs: 2); host.SeatFor(hb);
                var fb = hb.Clone(); friend.SeatFor(fb); fb.Swap(0, 2); friend.seating.Swapped(0, 2);
                fb.planes[2].state.pos = fb.planes[2].state.pos + new Vec3d(5, 0, 0);
                Check(friend.seating.Hash(fb) != host.seating.Hash(hb), "a friend's battle out of step by 5 ft hashes differently");
                host.Send(Msg.Snapshot, new SnapshotMsg { data = host.seating.Snapshot(hb) });
                SnapshotMsg sm = null;
                Until(() => { friend.Pump(); if (friend.TryNext(out var ty, out var d) && ty == Msg.Snapshot) sm = NetSession.Read<SnapshotMsg>(d); return sm != null; });
                friend.seating.Restore(fb, sm.data);
                Check(friend.seating.Hash(fb) == host.seating.Hash(hb) && friend.seating.Mine(0), $"after the {sm.data.Length} byte snapshot it is back in step, still in its own plane");
            }

            friend.Close();
            Check(Until(() => host.link.Lost, 3), $"the friend leaves and the host knows at once: \"{host.Problem}\"");
            host.Close();

            var other = NetSession.Host("build A", "Kenny", 0);
            var wrong = NetSession.Join("127.0.0.1", other.link.Port, "build B", "Sam");
            Until(() => { other.Pump(); wrong.Pump(); return other.Problem != null && wrong.Problem != null; });
            Check(!other.Online && !wrong.Online && other.Problem != null, $"two different builds refuse each other: \"{other.Problem}\"");
            other.Close(); wrong.Close();

            var nobody = NetSession.Join("127.0.0.1", 1, "test", "Sam");
            Check(Until(() => nobody.link.Lost, 12), $"joining where no one hosts gives up with a reason: \"{nobody.Problem}\"");
            Check(NetLink.ParseAddress("100.64.1.2:9000", out var hh, out var pp) && hh == "100.64.1.2" && pp == 9000 && NetLink.ParseAddress("kenny-pc", out hh, out pp) && pp == NetLink.DefaultPort, "addresses with and without a port");
        }

        Console.WriteLine("\nThe dogfight setup");
        {
            var setup = new DogfightSetup
            {
                ours = new[] { Craft.Spitfire, Craft.Phantom, Craft.Mustang }, theirs = new[] { Craft.Bf109, Craft.Mustang },
                randomStart = true, range = DogfightSetup.Range.Far, height = DogfightSetup.Height.High, speed = DogfightSetup.Pace.Fast, easyAimOurs = false,
                oursPilots = new[] { PilotKind.Wizard, PilotKind.Usual, PilotKind.Skeleton }, theirsPilots = new[] { PilotKind.Bear, PilotKind.Tiger },
            };
            var fixedOne = setup.Resolved(new Random(5));
            var back = DogfightSetup.FromBytes(fixedOne.ToBytes());
            Check(!fixedOne.randomStart && back.ToBytes().SequenceEqual(fixedOne.ToBytes()) && back.ours.SequenceEqual(setup.ours) && back.theirs.SequenceEqual(setup.theirs),
                  $"a setup survives the trip to bytes ({fixedOne.ToBytes().Length} bytes), its random start fixed ({fixedOne.start})");
            Check(back.oursPilots.SequenceEqual(setup.oursPilots) && back.theirsPilots.SequenceEqual(setup.theirsPilots),
                  $"the pilots survive it too ({string.Join(", ", back.oursPilots)} against {string.Join(", ", back.theirsPilots)})");
            var resized = setup.Copy(); resized.ours = DogfightSetup.Resize(resized.ours, 4); resized.theirs = DogfightSetup.Resize(resized.theirs, 1); resized.FitPilots();
            Check(resized.oursPilots.Length == 4 && resized.oursPilots[3] == PilotKind.Usual && resized.oursPilots[0] == PilotKind.Wizard && resized.theirsPilots.Length == 1,
                  "resizing a side keeps its pilots and gives new planes the usual one");
            var v1 = fixedOne.ToBytes().Take(1 + 1 + 3 + 1 + 2 + 6).ToArray(); v1[0] = 1;
            var old = DogfightSetup.FromBytes(v1);
            Check(old.oursPilots.Length == 3 && old.oursPilots.All(x => x == PilotKind.Usual) && old.theirs.SequenceEqual(setup.theirs),
                  "a setup saved by the build before the pilots still reads, everyone with the usual pilot");
            var centre = new Vec3d(0, -26000, 0);
            var a = Scenarios.Dogfight(fixedOne, centre, 3600, 300, 3000);
            var b = Scenarios.Dogfight(back, centre, 3600, 300, 3000);
            Check(Wire.Hash(a) == Wire.Hash(b), "the same bytes build the same battle on both machines");
            bool types = a.Count == 5 && DogfightSetup.Of(a.planes[0].numbers) == Craft.Spitfire && DogfightSetup.Of(a.planes[1].numbers) == Craft.Bf109
                      && DogfightSetup.Of(a.planes[2].numbers) == Craft.Phantom && DogfightSetup.Of(a.planes[3].numbers) == Craft.Mustang && DogfightSetup.Of(a.planes[4].numbers) == Craft.Mustang
                      && a.side[2] == 0 && a.side[3] == 0 && a.side[4] == 1 && a.missiles[2] == 4;
            Check(types, "each plane flies its own type, in the battle's order (you, their lead, your wingmen, their others), the F-4 with 4 missiles");
            Check(Math.Abs(a.planes[0].state.pos.z - (3600 + 4400)) < 1500 && Math.Abs(a.planes[0].Kias - 380) < 2 && a.combat[3].hitRadiusFt < 20,
                  $"high and fast: {a.planes[0].state.pos.z:F0} ft, {a.planes[0].Kias:F0} KIAS; no easy aim for side against side");
            var lightning = new DogfightSetup { ours = new[] { Craft.P38, Craft.Mustang }, theirs = new[] { Craft.Bf109, Craft.P38 }, randomStart = false,
                                                oursPilots = new[] { PilotKind.Usual, PilotKind.Wolf }, theirsPilots = new[] { PilotKind.Usual, PilotKind.Wizard } };
            var lb = DogfightSetup.FromBytes(lightning.ToBytes());
            var lf = Scenarios.Dogfight(lb, centre, 3600, 300, 3000);
            Check(lb.ours.SequenceEqual(lightning.ours) && lb.theirs.SequenceEqual(lightning.theirs) && lb.theirsPilots[1] == PilotKind.Wizard
                  && DogfightSetup.Of(lf.planes[0].numbers) == Craft.P38 && DogfightSetup.Of(lf.planes[3].numbers) == Craft.P38 && lf.combat[3].guns.StartsWith("20 mm + 4"),
                  "the P-38 survives the trip to bytes and flies as a P-38 with its nose guns, on either side");
            var easy = Scenarios.Dogfight(new DogfightSetup { ours = new[] { Craft.Bf109 }, theirs = new[] { Craft.Mustang }, randomStart = false }, centre);
            Check(easy.combat[0].hitRadiusFt == 20 && easy.combat[1].hitRadiusFt < 20, "against the AI your guns get the easy aim whatever you fly, and an enemy P-51 doesn't");

            // every start: the two leads apart, nobody on top of anybody, and each flies a few turns
            bool allOk = true; string worst = "";
            foreach (Scenarios.Start st in Enum.GetValues(typeof(Scenarios.Start)))
                foreach (DogfightSetup.Range rg in Enum.GetValues(typeof(DogfightSetup.Range)))
                {
                    var s4 = new DogfightSetup { ours = DogfightSetup.Resize(new[] { Craft.Mustang }, 4), theirs = DogfightSetup.Resize(new[] { Craft.Phantom }, 4), randomStart = false, start = st, range = rg };
                    var f = Scenarios.Dogfight(s4, centre, 3600, 300, 3000);
                    double closest = double.MaxValue;
                    for (int i = 0; i < f.Count; i++) for (int j = i + 1; j < f.Count; j++) closest = Math.Min(closest, (f.planes[i].state.pos - f.planes[j].state.pos).Length);
                    double apart = (f.planes[0].state.pos - f.planes[1].state.pos).Length;
                    for (int t = 0; t < 3; t++) f.Resolve(new TurnPlan?[f.Count], Scenarios.TurnSec, false);
                    bool ok = closest > 250 && apart > 800 && Enumerable.Range(0, f.Count).All(i => !f.Out(i));
                    if (!ok) { allOk = false; worst += $" {st}/{rg} (closest {closest:F0} ft, leads {apart:F0} ft)"; }
                }
            Check(allOk, $"all {Scenarios.StartCount} starts at every range, 4 v 4: nobody within 250 ft, the leads at least 800 ft apart, all still flying after 3 turns{worst}");
            Check(Scenarios.StartCount == Enum.GetValues(typeof(Scenarios.Start)).Length, "a random start can be any of them");
        }

        Console.WriteLine(fails == 0 ? "\nAll checks pass." : $"\n{fails} check(s) failed.");
        return fails;
    }
}
