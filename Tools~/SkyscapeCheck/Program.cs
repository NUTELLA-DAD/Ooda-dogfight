// Checks of the cloud sea (Turn/Core/Skyscape.cs) without Unity: the layout is the same every time and within the
// drawing budget, every dogfight start over it begins in clear air, there is cloud to fly in and out of at the fight's
// heights, and Density is cheap enough to ask every frame.
// Usage: dotnet run   (exit code = failures). dotnet run -- dump <file> writes the puffs as JSON (for previews).
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Ooda.Flight.Core;
using Ooda.Turn.Core;

static class Program
{
    static int fails;
    static void Check(bool ok, string what) { if (!ok) fails++; Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}"); }

    static int Main(string[] args)
    {
        var sky = Skyscape.Sky;
        if (args.Length >= 2 && args[0] == "dump") { Dump(sky, args[1]); return 0; }

        Console.WriteLine("The layout");
        Console.WriteLine($"  {sky.puffs.Length} puffs in {sky.banks.Length} banks: {sky.towers} towers ({sky.storms} storms), {sky.heaps} heaps");
        Check(sky.puffs.Length <= 10000, $"{sky.puffs.Length} puffs: within the 10,000 budget (four corners and two passes each)");
        Check(sky.towers >= 40 && sky.storms >= 2 && sky.heaps >= 150, "towers, thunderheads and heaps all round");
        var again = Skyscape.Sky;
        Check(ReferenceEquals(again, sky), "made once and shared");

        Console.WriteLine("\nThe starts: every start, range and height, 4 v 4, over the cloud sea");
        var a = Arenas.Get(Arenas.CloudIndex);
        Check(a.name == "Cloud Sea" && Arenas.IsCloud(Arenas.CloudIndex) && Campaign.OfArena(Arenas.CloudIndex) >= 0, "the Cloud Sea arena, with its campaign mission");
        float worst = 0, lowest = float.MaxValue; string worstWhat = "";
        int n = 0;
        foreach (Scenarios.Start st in Enum.GetValues(typeof(Scenarios.Start)))
            foreach (DogfightSetup.Range rg in Enum.GetValues(typeof(DogfightSetup.Range)))
                foreach (DogfightSetup.Height hg in Enum.GetValues(typeof(DogfightSetup.Height)))
                {
                    var s = new DogfightSetup { ours = DogfightSetup.Resize(new[] { Craft.Mustang }, 4), theirs = DogfightSetup.Resize(new[] { Craft.Bf109 }, 4), randomStart = false, start = st, range = rg, height = hg, randomArena = false, arena = Arenas.CloudIndex };
                    var b = Scenarios.Dogfight(s, a.CentreFt, a.altFt, 300, 3000, true, a.axisDeg);
                    for (int i = 0; i < b.Count; i++)
                    {
                        var p = b.planes[i].state.pos;
                        float x = (float)(p.x * Atmosphere.MPerFt), z = (float)(p.y * Atmosphere.MPerFt), y = (float)(p.z * Atmosphere.MPerFt);
                        lowest = Math.Min(lowest, y);
                        Check1(Skyscape.Over(x, z), $"{st}/{rg}/{hg} plane {i} over the cloud sea");
                        // clear air round it: where it is and a turn's flying ahead (about 450 m)
                        var v = b.planes[i].state.vel;
                        double sp = Math.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);
                        for (double ahead = 0; ahead <= 460; ahead += 20)
                        {
                            float ax = x + (float)(v.x / sp * ahead), az = z + (float)(v.y / sp * ahead), ay = y + (float)(v.z / sp * ahead);
                            float d = sky.Density(ax, ay, az);
                            if (d > worst) { worst = d; worstWhat = $"{st}/{rg}/{hg} plane {i}, {ahead:F0} m ahead"; }
                        }
                    }
                    n++;
                }
        Check(worst == 0, $"all {n} starts begin in clear air, and fly a turn in it (worst {worst:F2}{(worst > 0 ? " at " + worstWhat : "")})");
        Console.WriteLine($"  the lowest start is {lowest:F0} m up, the deck's tops about {Skyscape.DeckTopM:F0} m");

        Console.WriteLine("\nCloud to fly in and out of");
        var r = new Random(1);
        double Share(float y0, float y1)
        {
            int inn = 0, N = 100000;
            for (int i = 0; i < N; i++)
            {
                double an = r.NextDouble() * 2 * Math.PI, d = Math.Sqrt(r.NextDouble()) * (Skyscape.RadiusM - 2000);
                float y = (float)(y0 + r.NextDouble() * (y1 - y0));
                if (sky.Density((float)(Skyscape.CentreX + Math.Cos(an) * d), y, (float)(Skyscape.CentreZ + Math.Sin(an) * d)) > 0.3f) inn++;
            }
            return inn / (double)N;
        }
        double fight = Share(1600, 3400), deck = Share(Skyscape.DeckBaseM + 150, Skyscape.DeckTopM - 150), under = Share(50, Skyscape.DeckBaseM - 60), high = Share(8500, 9500);
        Check(fight > 0.12 && fight < 0.35, $"{fight:P0} of the air at the fight's heights (1,600 to 3,400 m) is in cloud: plenty to hide in, room to turn");
        Check(deck > 0.45, $"{deck:P0} of the deck's middle is cloud (the rest holes down to the sea)");
        Check(under < 0.01 && high < 0.01, $"clear under the deck ({under:P1}) and over the cirrus ({high:P1})");

        Console.WriteLine("\nDensity, every frame");
        var sw = Stopwatch.StartNew();
        float acc = 0;
        for (int i = 0; i < 200000; i++) acc += sky.Density(Skyscape.CentreX - 6000 + (i * 37) % 12000, 800 + (i * 13) % 3000, Skyscape.CentreZ - 6000 + (i * 53) % 12000);
        double us = sw.Elapsed.TotalMilliseconds * 1000 / 200000;
        Check(us < 20, $"{us:F2} us a call");

        Console.WriteLine(fails == 0 ? "\nAll good." : $"\n{fails} failed.");
        return fails;
    }

    static void Check1(bool ok, string what) { if (!ok) Check(false, what); }

    static void Dump(Skyscape s, string file)
    {
        var sb = new StringBuilder("{\"centre\":[");
        sb.Append(F(Skyscape.CentreX)).Append(',').Append(F(Skyscape.CentreZ)).Append("],\"axis\":").Append(F((float)Skyscape.AxisDeg)).Append(",\"puffs\":[");
        for (int i = 0; i < s.puffs.Length; i++)
        {
            var p = s.puffs[i];
            if (i > 0) sb.Append(',');
            sb.Append('[').Append(F(p.x)).Append(',').Append(F(p.y)).Append(',').Append(F(p.z)).Append(',').Append(F(p.r)).Append(',').Append(F(p.stretch)).Append(',')
              .Append(F(float.IsInfinity(p.cutY) || p.cutY < -1e6f ? -99999 : p.cutY)).Append(',').Append(p.kind).Append(',').Append(p.bank).Append(']');
        }
        sb.Append("]}");
        File.WriteAllText(file, sb.ToString());
        Console.WriteLine($"wrote {s.puffs.Length} puffs to {file}");
    }

    static string F(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}
