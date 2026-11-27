// PerfCheck: what the planning preview and the AI's planning cost in time and garbage, on a 4 v 4 dogfight.
// dotnet run -c Release
using System;
using System.Diagnostics;
using System.Linq;
using Ooda.Flight.Core;
using Ooda.Turn.Core;

static class Program
{
    static void Main()
    {
        var b = Scenarios.Dogfight(Scenarios.Start.Neutral, Vec3d.Zero, 5000, 300, 2000, 4, 4);
        var ais = new EnemyAi[b.Count];
        for (int i = 0; i < b.Count; i++) ais[i] = new EnemyAi { me = i, rng = new Lcg((uint)(100 + i)) };
        // fly a few turns so the fight is joined (rounds in the air)
        for (int t = 0; t < 3; t++)
        {
            var plans = new TurnPlan?[b.Count];
            for (int i = 0; i < b.Count; i++) if (!b.Out(i)) plans[i] = ais[i].Plan(b, 3);
            b.Resolve(plans, 3, true);
        }
        Console.WriteLine($"{b.Count} planes, {b.bullets.Count} things in the air");
        // a fingerprint of the fight so far (it must not change when only the speed of the code changes)
        double fp = 0; for (int i = 0; i < b.Count; i++) fp += b.planes[i].state.pos.x * (i + 1) + b.planes[i].state.pos.y * 3.1 + b.planes[i].state.pos.z * 7.7 + b.planes[i].state.vel.x;
        Console.WriteLine($"fingerprint {fp:R}, out: {string.Join("", Enumerable.Range(0, b.Count).Select(i => b.Out(i) ? "x" : "."))}");
        var mine = ais[0].Plan(b, 3);
        mine.burstStart = 0.5; mine.burstEnd = 2.0;

        Measure("preview (2 clones, 2 recorded turns)", 60, () =>
        {
            var sim = b.Clone(); sim.dispersion = false;
            var gsim = b.Clone(); gsim.dispersion = false;
            var p = new TurnPlan?[b.Count]; p[0] = mine;
            var full = mine; full.burstStart = 0; full.burstEnd = 3;
            var g = new TurnPlan?[b.Count]; g[0] = full;
            sim.Resolve(p, 3, true);
            gsim.Resolve(g, 3, true);
        });
        AircraftFrame[][] keepA = null, keepB = null;
        Measure("preview, frames handed back", 60, () =>
        {
            var sim = b.Clone(); sim.dispersion = false; sim.frameStore = keepA;
            var gsim = b.Clone(); gsim.dispersion = false; gsim.frameStore = keepB;
            var p = new TurnPlan?[b.Count]; p[0] = mine;
            var full = mine; full.burstStart = 0; full.burstEnd = 3;
            var g = new TurnPlan?[b.Count]; g[0] = full;
            keepA = sim.Resolve(p, 3, true).frames;
            keepB = gsim.Resolve(g, 3, true).frames;
        });
        {
            // the same answer either way
            var s1 = b.Clone(); s1.dispersion = false; var p = new TurnPlan?[b.Count]; p[0] = mine;
            var r1 = s1.Resolve(p, 3, true);
            var s2 = b.Clone(); s2.dispersion = false; s2.frameStore = keepA;
            var r2 = s2.Resolve(p, 3, true);
            bool same = r2.frames == keepA;
            for (int i = 0; i < b.Count; i++) for (int k = 0; k < r1.frames[i].Length; k++) same &= r1.frames[i][k].pos.x == r2.frames[i][k].pos.x && r1.frames[i][k].g == r2.frames[i][k].g && r1.frames[i][k].firing == r2.frames[i][k].firing;
            Console.WriteLine($"handed-back frames reused and give the same frames: {same}, hits {r1.hits.Count} / {r2.hits.Count}");
        }
        Measure("one unrecorded turn", 60, () =>
        {
            var sim = b.Clone(); sim.dispersion = false;
            var p = new TurnPlan?[b.Count]; p[0] = mine;
            sim.Resolve(p, 3, false);
        });
        Probe2.Run(b, mine);
        Measure("clone", 200, () => b.Clone());
        Measure("AI plans all 7 others", 5, () =>
        {
            for (int i = 1; i < b.Count; i++) if (!b.Out(i)) ais[i].Plan(b, 3);
        });
    }

    static void Measure(string what, int n, Action a)
    {
        a();   // warm
        long g0 = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (int k = 0; k < n; k++) a();
        sw.Stop();
        long g = GC.GetAllocatedBytesForCurrentThread() - g0;
        Console.WriteLine($"{what,-40} {sw.Elapsed.TotalMilliseconds / n,8:F2} ms  {g / n / 1024.0,9:F1} KB each");
    }
}
static class Probe2
{
    public static void Run(Battle b, TurnPlan mine)
    {
        var m = b.planes[0].Clone();
        var c = mine.ControlsFor(m, 0);
        long g0 = GC.GetAllocatedBytesForCurrentThread();
        for (int k = 0; k < 1000; k++) m.Step(c, Battle.StepSec);
        Console.WriteLine($"FlightModel.Step: {(GC.GetAllocatedBytesForCurrentThread() - g0) / 1000.0:F0} B each");
        g0 = GC.GetAllocatedBytesForCurrentThread();
        for (int k = 0; k < 1000; k++) c = mine.ControlsFor(m, k * 0.003);
        Console.WriteLine($"ControlsFor: {(GC.GetAllocatedBytesForCurrentThread() - g0) / 1000.0:F0} B each");
        g0 = GC.GetAllocatedBytesForCurrentThread();
        for (int k = 0; k < 1000; k++) m.systems.Step(Battle.StepSec);
        Console.WriteLine($"systems.Step: {(GC.GetAllocatedBytesForCurrentThread() - g0) / 1000.0:F0} B each");
    }
}
