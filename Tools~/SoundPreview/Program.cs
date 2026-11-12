// Writes every sound the game makes (Audio/Synth.cs) as a WAV file, to listen to them without the game, and checks
// each one: no clipping, no silence, and the loops join end to start without a jump.
//   dotnet run [folder]     (the folder defaults to ./out)
using System;
using System.Collections.Generic;
using System.IO;
using Ooda.Audio;

static class Program
{
    static int Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : "out";
        Directory.CreateDirectory(dir);
        var sounds = new List<(string name, float[] s, bool loop)>
        {
            ("engine_piston_loop", Synth.PistonLoop(), true), ("engine_jet_loop", Synth.JetLoop(), true), ("wind_loop", Synth.WindLoop(), true),
            ("gun_burst", Synth.Gun(), false), ("hit", Synth.Hit(), false), ("explosion", Synth.Blast(), false),
            ("missile_launch", Synth.MissileLaunch(), false), ("click", Synth.Click(), false), ("fly_turn", Synth.Commit(), false),
            ("warning", Synth.Warning(), false), ("win", Synth.Win(), false), ("lose", Synth.Lose(), false),
        };
        int fails = 0;
        foreach (var (name, s, loop) in sounds)
        {
            double peak = 0, sum = 0, step = 0; bool bad = false;
            for (int i = 0; i < s.Length; i++)
            {
                if (float.IsNaN(s[i]) || float.IsInfinity(s[i])) bad = true;
                peak = Math.Max(peak, Math.Abs(s[i])); sum += s[i] * (double)s[i];
                if (i > 0) step = Math.Max(step, Math.Abs(s[i] - s[i - 1]));
            }
            double rms = Math.Sqrt(sum / s.Length);
            double join = Math.Abs(s[0] - s[s.Length - 1]);
            bool ok = !bad && peak <= 0.95 && peak > 0.5 && rms > 0.02 && (!loop || join <= step * 1.5 + 1e-3) && (loop || Math.Abs(s[s.Length - 1]) < 0.01);
            if (!ok) fails++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {name,-20} {s.Length / (double)Synth.Rate,5:F2} s  peak {peak:F2}  rms {rms:F3}{(loop ? $"  join {join:F4} (largest step {step:F4})" : "")}");
            // a loop is written three times over, so the join can be heard
            var w = s;
            if (loop) { w = new float[s.Length * 3]; for (int k = 0; k < 3; k++) Array.Copy(s, 0, w, k * s.Length, s.Length); }
            File.WriteAllBytes(Path.Combine(dir, name + ".wav"), Synth.Wav(w));
        }
        Console.WriteLine(fails == 0 ? "All sounds pass." : $"{fails} sound(s) FAILED.");
        return fails;
    }
}
