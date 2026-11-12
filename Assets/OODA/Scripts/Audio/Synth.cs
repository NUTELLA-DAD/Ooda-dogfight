using System;

namespace Ooda.Audio
{
    /// <summary>
    /// The game's sounds, made from arithmetic when the game starts: there are no sound files. Each method returns one
    /// sound as mono samples at Rate, peaking at 0.9. The loops (engines, wind) join end to start without a click: their
    /// tones fit a whole number of cycles and their noise is cross-faded round the join. Pure C#, no Unity, so the sounds
    /// can be written out as WAV files and listened to without the game (Tools~/SoundPreview).
    /// </summary>
    public static class Synth
    {
        public const int Rate = 44100;
        const double Tau = 2 * Math.PI;

        /// <summary>White noise, the same every time for the same seed.</summary>
        sealed class Noise
        {
            uint s;
            public Noise(uint seed) { s = seed * 2654435761u + 12345u; }
            public double Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return s / 2147483648.0 - 1.0; }
        }

        /// <summary>A one-pole low-pass: Step(x, hz) follows x, losing what is above hz.</summary>
        struct LowPass
        {
            double y;
            public double Step(double x, double hz) { double a = 1 - Math.Exp(-Tau * hz / Rate); y += a * (x - y); return y; }
        }

        static float[] Finish(double[] x, double peak = 0.9, double fadeOutSec = 0.01)
        {
            double max = 1e-9;
            foreach (double v in x) max = Math.Max(max, Math.Abs(v));
            var f = new float[x.Length];
            int fade = Math.Min(x.Length, (int)(fadeOutSec * Rate));
            for (int i = 0; i < x.Length; i++)
            {
                double g = peak / max;
                if (fade > 0 && i >= x.Length - fade) g *= (x.Length - 1 - i) / (double)fade;
                f[i] = (float)(x[i] * g);
            }
            return f;
        }

        /// <summary>Noise for a loop of n samples: made a little longer and its tail folded back over its start.</summary>
        static double[] LoopNoise(int n, uint seed, Func<double, int, double> shape)
        {
            int over = Rate / 4;
            var rng = new Noise(seed);
            var raw = new double[n + over];
            for (int i = 0; i < raw.Length; i++) raw[i] = shape(rng.Next(), i);
            var o = new double[n];
            for (int i = 0; i < n; i++)
            {
                if (i < over) { double u = i / (double)over; o[i] = raw[i] * Math.Sqrt(u) + raw[n + i] * Math.Sqrt(1 - u); }
                else o[i] = raw[i];
            }
            return o;
        }

        // ---------- the loops ----------

        /// <summary>
        /// A big piston engine at cruise, 2 s: the exhaust's firing note and its harmonics (uneven, as twelve stacks are), a
        /// rumble an octave under it, the propeller's beat, and the roar of the exhaust as noise that pulses with the firing.
        /// Played faster and slower for the throttle.
        /// </summary>
        public static float[] PistonLoop()
        {
            int n = Rate * 2;
            const double f0 = 62;   // a whole number of cycles in 2 s
            var rng = new Noise(7);
            var amp = new double[36]; var ph = new double[36];
            for (int k = 1; k < amp.Length; k++)
            {
                amp[k] = Math.Pow(k, -0.85) * (0.55 + 0.45 * Math.Abs(rng.Next())) * (k % 3 == 0 ? 1.5 : 1.0);
                ph[k] = rng.Next() * Math.PI;
            }
            var lp = new LowPass(); var lp2 = new LowPass();
            var roar = LoopNoise(n, 11, (w, i) => lp2.Step(lp.Step(w, 1400), 1400));
            var x = new double[n];
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate, s = 0;
                for (int k = 1; k < amp.Length; k++) s += amp[k] * Math.Sin(Tau * f0 * k * t + ph[k]);
                s += 0.9 * Math.Sin(Tau * f0 / 2 * t) + 0.5 * Math.Sin(Tau * f0 * 1.5 * t + 1.0);
                double fire = 0.5 + 0.5 * Math.Sin(Tau * f0 * t);            // the exhaust pulses
                double prop = 1 + 0.12 * Math.Sin(Tau * 20.5 * t);           // the blades going by
                x[i] = Math.Tanh((s * 0.22 + roar[i] * 3.2 * (0.35 + 0.65 * fire)) * prop * 1.3);
            }
            return Finish(x, 0.9, 0);
        }

        /// <summary>A jet engine, 2 s: a deep roar of noise and a thin whine over it.</summary>
        public static float[] JetLoop()
        {
            int n = Rate * 2;
            var a = new LowPass(); var b = new LowPass(); var c = new LowPass();
            var roar = LoopNoise(n, 23, (w, i) => b.Step(a.Step(w, 900), 900));
            var hiss = LoopNoise(n, 29, (w, i) => w - c.Step(w, 3000));
            var x = new double[n];
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                double whine = 0.05 * Math.Sin(Tau * 2350 * t) + 0.035 * Math.Sin(Tau * 3125 * t + 0.07 * Math.Sin(Tau * 6 * t)) + 0.02 * Math.Sin(Tau * 4700 * t);
                double swell = 1 + 0.08 * Math.Sin(Tau * 1.5 * t);
                x[i] = Math.Tanh((roar[i] * 5.5 * swell + hiss[i] * 0.10 + whine) * 1.2);
            }
            return Finish(x, 0.9, 0);
        }

        /// <summary>The air going by, 4 s: soft noise that rises and falls slowly.</summary>
        public static float[] WindLoop()
        {
            int n = Rate * 4;
            var a = new LowPass(); var b = new LowPass();
            var air = LoopNoise(n, 31, (w, i) => b.Step(a.Step(w, 700), 500));
            var x = new double[n];
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                x[i] = air[i] * (1 + 0.30 * Math.Sin(Tau * 0.25 * t) + 0.15 * Math.Sin(Tau * 0.75 * t + 1));
            }
            return Finish(x, 0.9, 0);
        }

        // ---------- one-shots ----------

        /// <summary>A short burst of heavy machine guns: three rounds at 13 a second, each a crack and a thump.</summary>
        public static float[] Gun(uint seed = 1)
        {
            int n = (int)(Rate * 0.30);
            var rng = new Noise(seed);
            var x = new double[n];
            var lp = new LowPass();
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                double w = rng.Next();
                x[i] = Math.Tanh(lp.Step(w, 3800) * Crack(t) * 2.2 + Thumps(t) * 1.1);
            }
            return Finish(x);

            double Crack(double t) { double s = 0; for (int r = 0; r < 3; r++) { double u = t - r * 0.075; if (u >= 0) s += Math.Exp(-u / 0.014); } return s; }
            double Thumps(double t) { double s = 0; for (int r = 0; r < 3; r++) { double u = t - r * 0.075; if (u >= 0) s += Math.Sin(Tau * (150 * u - 380 * u * u)) * Math.Exp(-u / 0.035); } return s; }
        }

        /// <summary>A round striking metal: a click and a short ring.</summary>
        public static float[] Hit(uint seed = 1)
        {
            int n = (int)(Rate * 0.28);
            var rng = new Noise(seed + 40);
            var lp = new LowPass();
            double[] hz = { 1730 + 90 * rng.Next(), 2810 + 150 * rng.Next(), 4150 + 200 * rng.Next(), 640 };
            double[] decay = { 0.050, 0.035, 0.022, 0.030 };
            var x = new double[n];
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate, s = 0;
                for (int k = 0; k < hz.Length; k++) s += Math.Sin(Tau * hz[k] * t) * Math.Exp(-t / decay[k]) * (k == 3 ? 0.9 : 0.5);
                double w = rng.Next();
                x[i] = s + (w - lp.Step(w, 1500)) * Math.Exp(-t / 0.006) * 1.4;
            }
            return Finish(x);
        }

        /// <summary>An aircraft blowing up: a bang, then a rumble that falls away over a second and a half.</summary>
        public static float[] Blast(uint seed = 1)
        {
            int n = (int)(Rate * 1.9);
            var rng = new Noise(seed + 80);
            var a = new LowPass(); var b = new LowPass();
            var x = new double[n];
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                double cut = 90 + 4200 * Math.Exp(-t / 0.16);
                double body = b.Step(a.Step(rng.Next(), cut), cut);
                double env = Math.Min(1, t / 0.004) * (0.75 * Math.Exp(-t / 0.42) + 0.25 * Math.Exp(-t / 0.9));
                double sub = Math.Sin(Tau * (38 * t + 9 * (1 - Math.Exp(-t / 0.15)))) * Math.Exp(-t / 0.5);   // 98 Hz falling to 38
                x[i] = Math.Tanh(body * env * 7 + sub * 0.8);
            }
            return Finish(x, 0.9, 0.2);
        }

        /// <summary>A missile leaving the rail: a rush of noise that rises and thins out.</summary>
        public static float[] MissileLaunch()
        {
            int n = (int)(Rate * 1.5);
            var rng = new Noise(90);
            var lo = new LowPass(); var hi = new LowPass();
            var x = new double[n];
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                double w = rng.Next();
                double top = 900 + 5200 * Math.Min(1, t / 0.5);
                double band = lo.Step(w, top) - hi.Step(w, top * 0.35);
                double env = Math.Min(1, t / 0.03) * Math.Exp(-t / 0.55);
                x[i] = band * env + Math.Sin(Tau * (70 * t)) * Math.Exp(-t / 0.08) * 0.5;
            }
            return Finish(x, 0.9, 0.3);
        }

        /// <summary>A button: a small soft tick.</summary>
        public static float[] Click()
        {
            int n = (int)(Rate * 0.05);
            var x = new double[n];
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                x[i] = Math.Sin(Tau * 1150 * t) * Math.Exp(-t / 0.008) + 0.5 * Math.Sin(Tau * 2300 * t) * Math.Exp(-t / 0.004);
            }
            return Finish(x);
        }

        /// <summary>A plain tone with a soft edge, as a radio or a warning horn makes: hz for sec, with a little of its third harmonic.</summary>
        static void Tone(double[] x, double at, double sec, double hz, double level, double third = 0.25)
        {
            int i0 = (int)(at * Rate), n = (int)(sec * Rate);
            for (int i = 0; i < n && i0 + i < x.Length; i++)
            {
                double t = i / (double)Rate;
                double edge = Math.Min(1, Math.Min(t, sec - t) / 0.006);
                x[i0 + i] += (Math.Sin(Tau * hz * t) + third * Math.Sin(Tau * hz * 3 * t)) * edge * level;
            }
        }

        /// <summary>Fly turn: two rising radio pips.</summary>
        public static float[] Commit()
        {
            var x = new double[(int)(Rate * 0.26)];
            Tone(x, 0.00, 0.085, 660, 1);
            Tone(x, 0.11, 0.13, 990, 1);
            return Finish(x);
        }

        /// <summary>A warning horn: two flat beeps.</summary>
        public static float[] Warning()
        {
            var x = new double[(int)(Rate * 0.34)];
            Tone(x, 0.00, 0.12, 880, 1, 0.5);
            Tone(x, 0.17, 0.12, 880, 1, 0.5);
            return Finish(x);
        }

        /// <summary>A brass-like note: a bright tone with a slow attack and a little vibrato, for the fanfares.</summary>
        static void Brass(double[] x, double at, double sec, double hz, double level)
        {
            int i0 = (int)(at * Rate), n = (int)(sec * Rate);
            var lp = new LowPass();
            for (int i = 0; i < n && i0 + i < x.Length; i++)
            {
                double t = i / (double)Rate, s = 0;
                double f = hz * (1 + 0.004 * Math.Sin(Tau * 5.5 * t) * Math.Min(1, t / 0.2));
                for (int k = 1; k <= 8; k++) s += Math.Sin(Tau * f * k * t) / k;
                double env = Math.Min(1, t / 0.025) * Math.Min(1, (sec - t) / 0.08) * (0.75 + 0.25 * Math.Exp(-t / 0.15));
                x[i0 + i] += lp.Step(s, 900 + 2600 * Math.Min(1, t / 0.06)) * env * level;
            }
        }

        /// <summary>You won: a short rising call, the last note held.</summary>
        public static float[] Win()
        {
            var x = new double[(int)(Rate * 1.5)];
            Brass(x, 0.00, 0.16, 392.0, 1);     // G4
            Brass(x, 0.16, 0.16, 523.3, 1);     // C5
            Brass(x, 0.32, 0.16, 659.3, 1);     // E5
            Brass(x, 0.48, 0.95, 784.0, 1);     // G5
            Brass(x, 0.48, 0.95, 523.3, 0.5);
            return Finish(x, 0.9, 0.1);
        }

        /// <summary>You lost: three falling notes, low and slow.</summary>
        public static float[] Lose()
        {
            var x = new double[(int)(Rate * 1.9)];
            Brass(x, 0.00, 0.34, 329.6, 1);     // E4
            Brass(x, 0.36, 0.34, 261.6, 1);     // C4
            Brass(x, 0.72, 1.10, 220.0, 1);     // A3
            Brass(x, 0.72, 1.10, 130.8, 0.5);
            return Finish(x, 0.9, 0.2);
        }

        /// <summary>The sound as a 16-bit mono WAV file's bytes, to listen to it outside the game.</summary>
        public static byte[] Wav(float[] samples)
        {
            var b = new byte[44 + samples.Length * 2];
            void Str(int at, string s) { for (int i = 0; i < s.Length; i++) b[at + i] = (byte)s[i]; }
            void I32(int at, int v) { b[at] = (byte)v; b[at + 1] = (byte)(v >> 8); b[at + 2] = (byte)(v >> 16); b[at + 3] = (byte)(v >> 24); }
            void I16(int at, int v) { b[at] = (byte)v; b[at + 1] = (byte)(v >> 8); }
            Str(0, "RIFF"); I32(4, 36 + samples.Length * 2); Str(8, "WAVEfmt "); I32(16, 16); I16(20, 1); I16(22, 1);
            I32(24, Rate); I32(28, Rate * 2); I16(32, 2); I16(34, 16); Str(36, "data"); I32(40, samples.Length * 2);
            for (int i = 0; i < samples.Length; i++) I16(44 + i * 2, (short)Math.Round(Math.Max(-1, Math.Min(1, samples[i])) * 32767));
            return b;
        }
    }
}
