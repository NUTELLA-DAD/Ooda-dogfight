// Checks the C# flight core against the OODA tool (ooda_reference.mjs) and prints the P-51D's numbers.
// Usage: dotnet run -- [path to dads-debreif/src/core]
using System;
using System.Diagnostics;
using Ooda.Flight.Core;

static class Program
{
    static int Main(string[] args)
    {
        var t6 = AircraftNumbers.T6A();
        PointMass.ExcessFn ex = (ktas, alt, g) => t6.ThrustPerWeight(ktas, alt) - t6.DragPerWeight(Atmosphere.TasToIasKt(ktas, alt), g);
        string Run(string name, double ktas, double g, double bankDeg, double secs, bool body)
        {
            var s = PointMass.Make(new Vec3d(0, 0, 5000), ktas, Math.PI / 2);
            if (body) { var r = Vec3d.Cross(s.vel.Normalized, s.up); double b = bankDeg * Math.PI / 180; s.up = s.up * Math.Cos(b) + r * Math.Sin(b); }
            for (int i = 0; i < (int)Math.Round(secs / 0.02); i++)
                s = body ? PointMass.StepBody(s, g, 0, 0.02, ex) : PointMass.StepBank(s, g, bankDeg * Math.PI / 180, 0.02, ex);
            return $"{name} pos {s.pos.x:F2} {s.pos.y:F2} {s.pos.z:F2} vel {s.vel.x:F3} {s.vel.y:F3} {s.vel.z:F3}";
        }
        string[] mine = { Run("level", 200, 1, 0, 30, false), Run("loop", 250, 4, 0, 8, false), Run("turn", 250, 3, 70.5288, 20, false), Run("climbturn", 250, 2, 45, 15, false) };
        int fails = 0;
        if (args.Length > 0)
        {
            var psi = new ProcessStartInfo("node", $"ooda_reference.mjs {args[0]}") { RedirectStandardOutput = true };
            var refLines = Process.Start(psi).StandardOutput.ReadToEnd().Trim().Split('\n');
            for (int i = 0; i < mine.Length; i++)
            {
                bool same = refLines[i].Trim() == mine[i];
                if (!same) fails++;
                Console.WriteLine($"{(same ? "SAME" : "DIFF")}  ooda: {refLines[i].Trim()}\n      c#:   {mine[i]}");
            }
        }
        Console.WriteLine("body-frame (player) step, no roll, vs bank step:");
        Console.WriteLine("  " + Run("loop", 250, 4, 0, 8, true));

        // The P-51D: top speeds, climb, sustained and instantaneous turns
        var p = AircraftNumbers.P51D();
        Console.WriteLine($"\n{p.name}");
        foreach (double alt in new[] { 0.0, 10000, 20000, 25000, 30000 })
        {
            foreach (bool wep in new[] { false, true })
            {
                double v = 100; double k = wep ? p.wepFactor : 1;
                for (double t = 100; t < 600; t += 0.5) { double kias = Atmosphere.TasToIasKt(t, alt); if (k * p.ThrustPerWeight(t, alt) - p.DragPerWeight(kias, 1) > 0) v = t; }
                Console.Write($"  {alt,6} ft top speed {(wep ? "WEP" : "mil")} {v:F0} KTAS ({v * 1.15078:F0} mph)");
            }
            double best = 0, bestV = 0;
            for (double t = 100; t < 450; t += 1) { double ps = (p.ThrustPerWeight(t, alt) - p.DragPerWeight(Atmosphere.TasToIasKt(t, alt), 1)) * t * Atmosphere.KtToFtps * 60; if (ps > best) { best = ps; bestV = t; } }
            Console.WriteLine($"  best climb {best:F0} ft/min at {bestV:F0} KTAS");
        }
        foreach (double kias in new[] { 150.0, 200, 250, 300, 350 })
        {
            double gs = 1; for (double g = 1; g <= p.AvailableG(kias); g += 0.01) if (p.ThrustPerWeight(kias, 0) - p.DragPerWeight(kias, g) >= 0) gs = g;
            double vfps = kias * Atmosphere.KtToFtps;
            double rate(double g) => g > 1 ? Math.Sqrt(g * g - 1) * Atmosphere.GFtps2 / vfps * 180 / Math.PI : 0;
            Console.WriteLine($"  SL {kias} KIAS: sustained {gs:F1} G {rate(gs):F1} deg/s, max {p.AvailableG(kias):F1} G {rate(p.AvailableG(kias)):F1} deg/s, roll {p.MaxRollRateDps(kias):F0} deg/s");
        }

        // The player model flies: full power level from 200 kt, a full-aft pull, a full-stick roll
        var fm = new FlightModel(p, PointMass.Make(new Vec3d(0, 0, 5000), 250, Math.PI / 2));
        var c = new FlightControls { throttle = 1 };
        for (int i = 0; i < 50 * 60; i++) fm.Step(c, 0.02);
        Console.WriteLine($"  60 s neutral stick, full power, from 250 KTAS: {fm.Ktas:F0} KTAS, {fm.AltFt:F0} ft");
        c.roll = 1; double rolled = 0; int n = 0;
        while (rolled < 360 && n < 1000) { fm.Step(c, 0.02); rolled += fm.rollRateDps * 0.02; n++; }
        Console.WriteLine($"  full-stick 360 deg roll at {fm.Ktas:F0} KTAS: {n * 0.02:F2} s");
        c.roll = 0; c.pitch = 1; double maxG = 0;
        for (int i = 0; i < 50 * 10; i++) { fm.Step(c, 0.02); maxG = Math.Max(maxG, fm.g); }
        Console.WriteLine($"  10 s full aft stick: peaked {maxG:F1} G, now {fm.Ktas:F0} KTAS, {fm.g:F1} G, stalled {fm.stalled}");

        // Departure: slow, full aft stick and right rudder, then recovery
        var sp = new FlightModel(p, PointMass.Make(new Vec3d(0, 0, 10000), 150, Math.PI / 2));
        var sc = new FlightControls { throttle = 0.5, pitch = 1, yaw = 1 };
        double clock = 0; string seen = "";
        void Fly(double secs) { for (int i = 0; i < (int)(secs / 0.02); i++) { sp.Step(sc, 0.02); clock += 0.02; string d = sp.departure.ToString(); if (!seen.EndsWith(d)) { seen += $" {clock:F1}s:{d}"; } } }
        Fly(8);
        Console.WriteLine($"  spin entry (full aft, right rudder, from 150 KTAS): {seen.Trim()}");
        Console.WriteLine($"    after 8 s: {sp.departure} dir {sp.spinDir}, {sp.Kias:F0} KIAS, roll {sp.rollRateDps:F0} deg/s, path {Math.Asin(sp.Forward.z) * 180 / Math.PI:F0} deg, lost {10000 - sp.AltFt:F0} ft");
        seen = ""; double alt0 = sp.AltFt; sc = new FlightControls { throttle = 0, pitch = -0.3, yaw = -1 };
        Fly(6);
        Console.WriteLine($"  recovery (throttle idle, left rudder, stick forward): {seen.Trim()}, lost {alt0 - sp.AltFt:F0} ft, {sp.Kias:F0} KIAS");
        sc = new FlightControls { throttle = 1, pitch = 0.3 }; Fly(6);
        Console.WriteLine($"  pull out: {sp.departure}, {sp.Kias:F0} KIAS, path {Math.Asin(sp.Forward.z) * 180 / Math.PI:F0} deg");
        // Holding pro-spin without rudder: does it still depart (wing drop)?
        var ws = new FlightModel(p, PointMass.Make(new Vec3d(0, 0, 10000), 150, Math.PI / 2));
        for (int i = 0; i < 400; i++) ws.Step(new FlightControls { throttle = 0.5, pitch = 1 }, 0.02);
        Console.WriteLine($"  full aft, no rudder, 8 s: {ws.departure} dir {ws.spinDir}");
        var fast = new FlightModel(p, PointMass.Make(new Vec3d(0, 0, 10000), 300, Math.PI / 2));
        for (int i = 0; i < 150; i++) fast.Step(new FlightControls { throttle = 1, pitch = 1 }, 0.02);
        Console.WriteLine($"  full aft at 300 KTAS, 3 s (fast, should not depart yet): {fast.departure}, {fast.g:F1} G, {fast.Kias:F0} KIAS");
        return fails;
    }
}
