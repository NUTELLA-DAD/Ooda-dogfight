// Checks of the skyscraper city (Turn/Core/City.cs) without Unity: the layout is the same every time, the height
// lookups match the boxes, the river is clear to start in, every dogfight start over it begins clear of the roofs,
// flying into a tower crashes you, and AI pilots fight among the towers without flying into them much.
// Usage: dotnet run   (exit code = failures). dotnet run -- dump <file> writes the boxes as JSON (for previews).
using System;
using System.Diagnostics;
using System.Linq;
using Ooda.Flight.Core;
using Ooda.Turn.Core;

static class Program
{
    static int fails;
    static void Check(bool ok, string what) { if (!ok) fails++; Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}"); }
    const double TurnSec = 3;

    static double Ground(double x, double y)
    {
        double c = City.Skyline.GroundFt(x, y);
        if (!double.IsNegativeInfinity(c)) return c;
        double e = Eastland.Land.GroundFt(x, y);   // the east lands round the city (the Alps are past their west edge)
        return double.IsNegativeInfinity(e) ? 0 : e;
    }

    static int Main(string[] args)
    {
        var city = City.Skyline;
        if (args.Length >= 2 && args[0] == "dump") { Dump(city, args[1]); return 0; }

        Console.WriteLine("The layout");
        {
            var again = City.Make(1937);
            bool same = again.boxes.Length == city.boxes.Length;
            for (int k = 0; same && k < city.boxes.Length; k++)
            {
                var a = city.boxes[k]; var b = again.boxes[k];
                same = a.x0 == b.x0 && a.x1 == b.x1 && a.z0 == b.z0 && a.z1 == b.z1 && a.topM == b.topM;
            }
            Check(same, $"made twice from the seed, the same {city.boxes.Length} boxes ({city.towers} towers and bridges)");
            int tall = city.boxes.Where(b => b.style >= 0).Select(b => b.tower).Distinct().Count(t => city.boxes.Where(b => b.tower == t).Max(b => b.topM) > 150);
            Check(city.towers > 1500 && tall > 60, $"a dense city: {city.towers} towers, {tall} of them over 150 m, the tallest {city.tallestM:F0} m");
            bool inRiver = city.boxes.Any(b => b.style >= 0 && b.x0 < City.CentreX + City.RiverRoadM && b.x1 > City.CentreX - City.RiverRoadM);
            Check(!inRiver, "no tower stands in the river or on its roads (only the bridges and their pylons)");
            var heights = Enumerable.Range(0, city.towers).Select(t => 0.0).ToArray();
            foreach (var b in city.boxes) if (b.style >= 0) heights[b.tower] = Math.Max(heights[b.tower], b.topM);
            var hs = heights.Where(h => h > 0).OrderBy(h => h).ToArray();
            Console.WriteLine($"    heights: median {hs[hs.Length / 2]:F0} m, 90th {hs[hs.Length * 9 / 10]:F0} m, 99th {hs[hs.Length * 99 / 100]:F0} m");
        }

        Console.WriteLine("\nThe height field");
        {
            var rng = new Lcg(5);
            int bad = 0, over = 0;
            for (int k = 0; k < 40000; k++)
            {
                double x = City.CentreX + (rng.Next() * 2 - 1) * City.HalfX, z = City.CentreZ + (rng.Next() * 2 - 1) * City.HalfZ;
                double brute = Math.Abs(x - City.CentreX) < City.RiverHalfM ? 0 : City.StreetM;
                foreach (var b in city.boxes) if (b.Contains(x, z)) brute = Math.Max(brute, b.topM);
                if (Math.Abs(brute - city.HeightM(x, z)) > 1e-6) bad++;
                if (brute > City.StreetM + 1) over++;
            }
            Check(bad == 0, $"40,000 random points: the lookup matches every box ({over / 400.0:F0} % of the city is under a roof)");
            Check(double.IsNegativeInfinity(city.HeightM(0, 0)) && double.IsNegativeInfinity(city.HeightM(City.CentreX + City.HalfX + 10, 0)), "off the city's island it leaves the ground to the rest of the world");
            var sw = Stopwatch.StartNew(); double sum = 0;
            for (int k = 0; k < 2_000_000; k++) sum += city.GroundFt((City.CentreX + (k % 7000) - 3500) * Atmosphere.FtPerM, ((k * 7) % 8000 - 4000) * Atmosphere.FtPerM);
            Check(sw.Elapsed.TotalMilliseconds / 2000 < 1.0, $"a lookup takes {sw.Elapsed.TotalMilliseconds * 1e3 / 2_000_000:F3} us");
        }

        var arena = Arenas.Get(Arenas.CityIndex);
        Console.WriteLine($"\nThe starts over {arena.name}");
        {
            int fights = 0, lifted = 0, inCanyon = 0, clearOk = 0;
            foreach (Scenarios.Start s in Enum.GetValues(typeof(Scenarios.Start)))
                foreach (DogfightSetup.Height h in Enum.GetValues(typeof(DogfightSetup.Height)))
                    foreach (DogfightSetup.Range r in Enum.GetValues(typeof(DogfightSetup.Range)))
                        foreach (int n in new[] { 1, 4 })
                        {
                            var setup = new DogfightSetup { randomStart = false, start = s, height = h, range = r, randomArena = false, arena = Arenas.CityIndex,
                                                            ours = DogfightSetup.Resize(new[] { Craft.Mustang }, n), theirs = DogfightSetup.Resize(new[] { Craft.Bf109 }, n) };
                            var b = Scenarios.Dogfight(setup, arena.CentreFt, arena.altFt, 300, 3000, true, arena.axisDeg);
                            if (Scenarios.LiftClear(b, Ground) > 0) lifted++;
                            fights++;
                            bool ok = true, canyon = false;
                            for (int i = 0; i < b.Count; i++)
                            {
                                var p = b.planes[i].state.pos;
                                ok &= city.Over(p.x * Atmosphere.MPerFt, p.y * Atmosphere.MPerFt) && p.z - Ground(p.x, p.y) >= 499;
                                // towers round it reaching above it: it starts down among them
                                for (int d = 0; d < 8 && !canyon; d++)
                                {
                                    double a = d * Math.PI / 4;
                                    double q = Ground(p.x + 1500 * Math.Cos(a), p.y + 1500 * Math.Sin(a));
                                    if (q > p.z) canyon = true;
                                }
                            }
                            if (ok) clearOk++;
                            if (canyon) inCanyon++;
                        }
            Check(clearOk == fights, $"all {fights} starts (7 starts, 3 heights, 3 ranges, 1 v 1 and 4 v 4) begin over the city, 500 ft or more over what is under them ({lifted} lifted to clear a roof)");
            Check(inCanyon > fights / 3, $"{inCanyon} of them begin with towers rising above someone within 500 m");
        }

        Console.WriteLine("\nFlying into a tower");
        {
            // from the river at 250 ft, straight east into the riverside blocks
            var setup = new DogfightSetup { randomStart = false, start = Scenarios.Start.HeadOn };
            var b = Scenarios.Dogfight(setup, arena.CentreFt, 250, 300, 3000, false, 90);   // turned 90: you fly east
            b.groundFt = Ground;
            var me = b.planes[0];
            me.state.pos = new Vec3d((City.CentreX + 100) * Atmosphere.FtPerM, (City.CentreZ + 300) * Atmosphere.FtPerM, 250);
            bool crashed = false; double at = 0;
            for (int t = 0; t < 10 && !crashed; t++)
            {
                var plans = new TurnPlan?[b.Count];
                plans[0] = TurnPlan.Straight(b.planes[0]);
                for (int i = 1; i < b.Count; i++) plans[i] = TurnPlan.Straight(b.planes[i]);
                b.Resolve(plans, TurnSec, false);
                crashed = b.crashed[0]; at = b.time;
            }
            var p = b.planes[0].state.pos;
            Check(crashed && city.OverBuildingFt(p.x, p.y), $"held straight and low it hits a tower ({at:F0} s in, {p.x * Atmosphere.MPerFt - City.CentreX:F0} m east of the river's middle)");
            // the same, a preview copy (as the ghost path is flown) crashes too
            var b2 = Scenarios.Dogfight(setup, arena.CentreFt, 250, 300, 3000, false, 90);
            b2.groundFt = Ground; b2.planes[0].state.pos = new Vec3d((City.CentreX + 100) * Atmosphere.FtPerM, (City.CentreZ + 300) * Atmosphere.FtPerM, 250);
            var sim = b2.Clone(); sim.dispersion = false;
            bool previewCrash = false;
            for (int t = 0; t < 10 && !previewCrash; t++) { sim.Resolve(new TurnPlan?[] { TurnPlan.Straight(sim.planes[0]) }, TurnSec, true); previewCrash = sim.crashed[0]; }
            Check(previewCrash, "the ghost path's copy of the battle crashes the same way (the preview turns red)");
            // and a round fired into a tower stops there
            int blocked = 0;
            for (int k = 0; k < 50; k++)
            {
                double z = City.CentreZ - 3000 + 120 * k;
                if (!Strike.Clear(Ground, new Vec3d((City.CentreX + 100) * Atmosphere.FtPerM, z * Atmosphere.FtPerM, 100),
                                          new Vec3d((City.CentreX + 2500) * Atmosphere.FtPerM, z * Atmosphere.FtPerM, 100))) blocked++;
            }
            Check(blocked >= 30, $"at 100 ft across the blocks, {blocked} of 50 lines of sight are cut by a tower (the radar's look, in 400 ft steps; rounds stop at them step by step)");
        }

        Console.WriteLine("\nAI pilots among the towers");
        {
            foreach (var (label, clear, step) in (Environment.GetEnvironmentVariable("CITY_SWEEP") != null
                                                ? new[] { ("as in the Alps (600 ft, 8 points ahead)", 600.0, 0.0), ("300 ft, every 90 ft ahead", 300.0, 90.0), ("tuned for the city (400 ft, every 90 ft ahead)", 400.0, 90.0) }
                                                : new[] { ("tuned for the city (400 ft, every 90 ft ahead)", 400.0, 90.0) }))
            {
                int fights = 0, towerCrashes = 0, kills = 0, turnsFlown = 0, planes = 0;
                double planMs = 0; int plansMade = 0;
                foreach (Scenarios.Start s in Enum.GetValues(typeof(Scenarios.Start)))
                    foreach (DogfightSetup.Height h in new[] { DogfightSetup.Height.Low, DogfightSetup.Height.Medium })
                        foreach (int n in new[] { 1, 2 })
                        {
                            var setup = new DogfightSetup { randomStart = false, start = s, height = h, randomArena = false, arena = Arenas.CityIndex,
                                                            ours = DogfightSetup.Resize(new[] { Craft.Mustang }, n), theirs = DogfightSetup.Resize(new[] { Craft.Bf109 }, n) };
                            var b = Scenarios.Dogfight(setup, arena.CentreFt, arena.altFt, 300, 3000, true, arena.axisDeg);
                            Scenarios.LiftClear(b, Ground);
                            b.groundFt = Ground;
                            var ais = Enumerable.Range(0, b.Count).Select(i => new EnemyAi
                            {
                                me = i, rng = new Lcg(300u + (uint)(i + 7 * fights)), ignoreObstacles = true, safeClearFt = clear, lookStepFt = step,
                                role = b.side[i] == 0 ? EnemyAi.Role.Fighter : EnemyAi.Role.Hunter,
                            }).ToArray();
                            planes += b.Count;
                            for (int t = 0; t < 14 && !b.Over; t++)
                            {
                                var plans = new TurnPlan?[b.Count];
                                var sw = Stopwatch.StartNew();
                                for (int i = 0; i < b.Count; i++) if (!b.Out(i) && !b.InFormation(i)) { plans[i] = ais[i].Plan(b, TurnSec); plansMade++; }
                                planMs += sw.Elapsed.TotalMilliseconds;
                                var wasDown = (bool[])b.down.Clone();
                                b.Resolve(plans, TurnSec, false);
                                for (int i = 0; i < b.Count; i++)
                                {
                                    if (wasDown[i] || !b.down[i]) continue;
                                    var p = b.planes[i].state.pos;
                                    if (b.crashed[i] && city.OverBuildingFt(p.x, p.y))
                                    {
                                        towerCrashes++;
                                        if (Environment.GetEnvironmentVariable("CITY_VERBOSE") != null)
                                            Console.WriteLine($"      tower: plane {i} turn {t} {s} {h} n{n} formation {(plans[i] == null ? "yes" : "no")} alt {p.z:F0} ft kias {b.planes[i].Kias:F0} under {Ground(p.x, p.y):F0}");
                                    }
                                    else kills++;
                                }
                                turnsFlown++;
                            }
                            fights++;
                        }
                Console.WriteLine($"    {label}: {fights} fights, {turnsFlown} turns, {planes} planes: {towerCrashes} flew into a tower, {kills} shot down or crashed otherwise; {planMs / Math.Max(1, plansMade):F0} ms a plan");
                if (label.StartsWith("tuned")) Check(towerCrashes <= planes / 10, $"tuned, at most one plane in ten flies into a tower ({towerCrashes} of {planes})");
            }
        }

        Console.WriteLine(fails == 0 ? "\nAll good." : $"\n{fails} failed.");
        return fails;
    }

    static void Dump(City c, string file)
    {
        using var w = new System.IO.StreamWriter(file);
        w.Write("{\"boxes\":[");
        for (int k = 0; k < c.boxes.Length; k++)
        {
            var b = c.boxes[k];
            w.Write($"{(k > 0 ? "," : "")}[{b.x0:F1},{b.z0:F1},{b.x1:F1},{b.z1:F1},{b.baseM:F1},{b.topM:F1},{b.style},{b.tower}]");
        }
        w.Write("],\"pads\":[");
        for (int k = 0; k < c.pads.Length; k++)
        {
            var p = c.pads[k];
            w.Write($"{(k > 0 ? "," : "")}[{p.x0:F1},{p.z0:F1},{p.x1:F1},{p.z1:F1},{p.kind}]");
        }
        w.Write("]}");
    }
}
