using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Ooda.Look
{
    /// <summary>
    /// The Clean &amp; Pretty scorecard: the automatic half of the score, worked out from the golden shots
    /// (<see cref="ScorecardRunner"/> takes them) and from the UI scripts, and the report: a JSON file, a row in
    /// history.csv and a rating page for the half Kenny scores by eye. The checks and their targets are the
    /// ones in the graphics polish plan; <see cref="Grade"/> holds the thresholds.
    /// </summary>
    public static class Scorecard
    {
        /// <summary>Set while a shot is taken without the HUD: the battle's OnGUI draws nothing.</summary>
        public static bool HideUi;
        /// <summary>Set while a shot is taken: the battle's Update does nothing, so the frame holds still.</summary>
        public static bool Frozen;
        /// <summary>The editor menu leaves the run's folder here before it presses Play.</summary>
        public const string RequestKey = "OODA.ScorecardDir";

        public class Shot
        {
            public string id, title;
            public Texture2D ui, noUi, noPlanes;   // noUi: HUD hidden; noPlanes: HUD and the aircraft hidden
            public bool usePlanes, useSaturation, useFootprint, useClipped;
            public float uiCoverage = float.NaN, saturation = float.NaN, clipped = float.NaN, pop = float.NaN, depthRatio = float.NaN;
            public DepthSamples depth;
        }

        public class Result
        {
            public string name, value, target;
            public float points, max;
            public string Grade => points >= max ? "pass" : points > 0 ? "near" : "fail";
        }

        // ---------- pixel checks ----------

        static float Lum(Color32 c) => (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) / 255f;

        /// <summary>Share of the screen the HUD changes (with it against without it).</summary>
        public static float UiCoverage(Texture2D ui, Texture2D noUi)
        {
            var a = ui.GetPixels32(); var b = noUi.GetPixels32();
            int n = Math.Min(a.Length, b.Length), hit = 0;
            for (int i = 0; i < n; i++)
            {
                int d = Math.Max(Math.Abs(a[i].r - b[i].r), Math.Max(Math.Abs(a[i].g - b[i].g), Math.Abs(a[i].b - b[i].b)));
                if (d > 20) hit++;
            }
            return n == 0 ? 0 : (float)hit / n;
        }

        /// <summary>Share of pixels more saturated than 0.75 (HSV), not counting the near-black.</summary>
        public static float Saturation(Texture2D t)
        {
            var p = t.GetPixels32(); int hit = 0;
            foreach (var c in p)
            {
                int mx = Math.Max(c.r, Math.Max(c.g, c.b)), mn = Math.Min(c.r, Math.Min(c.g, c.b));
                if (mx < 40) continue;
                if ((mx - mn) / (float)mx > 0.75f) hit++;
            }
            return p.Length == 0 ? 0 : (float)hit / p.Length;
        }

        /// <summary>Share of pixels that are pure white or pure black.</summary>
        public static float Clipped(Texture2D t)
        {
            var p = t.GetPixels32(); int hit = 0;
            foreach (var c in p)
                if ((c.r >= 250 && c.g >= 250 && c.b >= 250) || (c.r <= 5 && c.g <= 5 && c.b <= 5)) hit++;
            return p.Length == 0 ? 0 : (float)hit / p.Length;
        }

        /// <summary>
        /// How far the aircraft stand out: the colour difference (CIE76 delta E) between the aircraft's pixels
        /// (where hiding them changes the picture) and a ring 20 px round them. NaN when no aircraft is on screen.
        /// </summary>
        public static float Pop(Texture2D withPlanes, Texture2D noPlanes)
        {
            int w = withPlanes.width, h = withPlanes.height;
            var a = withPlanes.GetPixels32(); var b = noPlanes.GetPixels32();
            var mask = new bool[w * h];
            int x0 = w, y0 = h, x1 = -1, y1 = -1, count = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    int d = Math.Max(Math.Abs(a[i].r - b[i].r), Math.Max(Math.Abs(a[i].g - b[i].g), Math.Abs(a[i].b - b[i].b)));
                    if (d <= 24) continue;
                    mask[i] = true; count++;
                    if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y;
                }
            if (count < 30) return float.NaN;
            const int R = 20;
            x0 = Math.Max(0, x0 - R); y0 = Math.Max(0, y0 - R); x1 = Math.Min(w - 1, x1 + R); y1 = Math.Min(h - 1, y1 + R);
            int bw = x1 - x0 + 1, bh = y1 - y0 + 1;
            // grow the mask R px each way (a square), in two passes
            var rowGrown = new bool[bw * bh];
            for (int y = 0; y < bh; y++)
            {
                int last = int.MinValue / 2;
                for (int x = 0; x < bw; x++) { if (mask[(y + y0) * w + x + x0]) last = x; if (x - last <= R) rowGrown[y * bw + x] = true; }
                last = int.MaxValue / 2;
                for (int x = bw - 1; x >= 0; x--) { if (mask[(y + y0) * w + x + x0]) last = x; if (last - x <= R) rowGrown[y * bw + x] = true; }
            }
            var grown = new bool[bw * bh];
            for (int x = 0; x < bw; x++)
            {
                int last = int.MinValue / 2;
                for (int y = 0; y < bh; y++) { if (rowGrown[y * bw + x]) last = y; if (y - last <= R) grown[y * bw + x] = true; }
                last = int.MaxValue / 2;
                for (int y = bh - 1; y >= 0; y--) { if (rowGrown[y * bw + x]) last = y; if (last - y <= R) grown[y * bw + x] = true; }
            }
            Vector3 inSum = Vector3.zero, ringSum = Vector3.zero; int inN = 0, ringN = 0;
            for (int y = 0; y < bh; y++)
                for (int x = 0; x < bw; x++)
                {
                    int i = (y + y0) * w + x + x0;
                    if (mask[i]) { inSum += Lab(a[i]); inN++; }
                    else if (grown[y * bw + x]) { ringSum += Lab(a[i]); ringN++; }
                }
            if (inN == 0 || ringN == 0) return float.NaN;
            return Vector3.Distance(inSum / inN, ringSum / ringN);
        }

        static Vector3 Lab(Color32 c)
        {
            float Lin(byte v) { float s = v / 255f; return s <= 0.04045f ? s / 12.92f : Mathf.Pow((s + 0.055f) / 1.055f, 2.4f); }
            float r = Lin(c.r), g = Lin(c.g), b = Lin(c.b);
            float X = (0.4124f * r + 0.3576f * g + 0.1805f * b) / 0.95047f;
            float Y = 0.2126f * r + 0.7152f * g + 0.0722f * b;
            float Z = (0.0193f * r + 0.1192f * g + 0.9505f * b) / 1.08883f;
            float F(float t) => t > 0.008856f ? Mathf.Pow(t, 1f / 3f) : 7.787f * t + 16f / 116f;
            float fx = F(X), fy = F(Y), fz = F(Z);
            return new Vector3(116f * fy - 16f, 500f * (fx - fy), 200f * (fy - fz));
        }

        /// <summary>What a depth check reads off a shot: each ground pixel sampled (a ray every 16 px) with its distance and colour, and the sky pixels with their height on screen.</summary>
        public class DepthSamples
        {
            public readonly List<(float dist, Vector3 lab)> ground = new List<(float, Vector3)>();
            public readonly List<(int y, Vector3 lab)> sky = new List<(int, Vector3)>();
        }

        public static DepthSamples SampleDepth(Camera cam, Texture2D t)
        {
            var d = new DepthSamples();
            if (cam == null || t == null) return d;
            var p = t.GetPixels32();
            for (int y = 8; y < t.height; y += 16)
                for (int x = 8; x < t.width; x += 16)
                {
                    var c = Lab(p[y * t.width + x]);
                    var ray = cam.ScreenPointToRay(new Vector3(x + 0.5f, y + 0.5f, 0f));
                    if (Physics.Raycast(ray, out var hit, 60000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) d.ground.Add((hit.distance, c));
                    else d.sky.Add((y, c));
                }
            return d;
        }

        /// <summary>
        /// Aerial perspective: how far the ground's colours stay from the horizon sky's. The mean colour difference
        /// (delta E) between the farthest quarter of the ground and the sky just above the horizon, over the same for
        /// the nearest quarter. Haze pulls far ground towards the sky colour; 0.4 or less reads as real distance.
        /// </summary>
        public static float DepthRatio(DepthSamples d)
        {
            if (d == null || d.ground.Count < 40 || d.sky.Count < 10) return float.NaN;
            var low = d.sky.OrderBy(v => v.y).Take(Math.Max(5, d.sky.Count / 4)).ToList();
            Vector3 horizon = Vector3.zero;
            foreach (var v in low) horizon += v.lab;
            horizon /= low.Count;
            var g = d.ground.OrderBy(v => v.dist).ToList();
            int q = g.Count / 4;
            float near = g.Take(q).Average(v => Vector3.Distance(v.lab, horizon));
            float far = g.Skip(g.Count - q).Average(v => Vector3.Distance(v.lab, horizon));
            return near < 1e-3f ? float.NaN : far / near;
        }

        /// <summary>Share of pixels with HSV saturation over 0.5 that aren't dark: the strong colour that reads as oversaturated.</summary>
        public static float StrongColour(Texture2D t)
        {
            var p = t.GetPixels32(); int hit = 0;
            foreach (var c in p)
            {
                int mx = Math.Max(c.r, Math.Max(c.g, c.b)), mn = Math.Min(c.r, Math.Min(c.g, c.b));
                if (mx < 77) continue;
                if ((mx - mn) / (float)mx > 0.5f) hit++;
            }
            return p.Length == 0 ? 0 : (float)hit / p.Length;
        }

        // ---------- code checks ----------

        /// <summary>Distinct colour literals in the UI and overlay scripts outside the tokens file (BriefingStyle), and distinct font sizes.
        /// Only where the scripts are on disk (the editor); -1 otherwise.</summary>
        public static (int strayColours, int fontSizes) CodeChecks()
        {
            string root = Path.Combine(Application.dataPath, "OODA", "Scripts");
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-scorecardsrc");   // a build: where the project's Assets/OODA/Scripts is
            if (at >= 0 && at + 1 < args.Length) root = args[at + 1];
            if (!Directory.Exists(root)) return (-1, -1);
            var colours = new HashSet<string>();
            var sizes = new HashSet<string>();
            var colourRx = new Regex(@"new\s+Color(32)?\s*\(([^()]*)\)");
            var sizeRx = new Regex(@"fontSize\s*=\s*(\d+)");
            foreach (var dir in new[] { "UI", "Turn" })
            {
                string d = Path.Combine(root, dir);
                if (!Directory.Exists(d)) continue;
                foreach (var f in Directory.GetFiles(d, "*.cs", SearchOption.AllDirectories))
                {
                    if (f.Replace('\\', '/').Contains("/Turn/Core/")) continue;   // the flight and battle maths: no drawing
                    string text = File.ReadAllText(f);
                    if (!Path.GetFileName(f).StartsWith("BriefingStyle"))
                        foreach (Match m in colourRx.Matches(text)) colours.Add(Regex.Replace(m.Groups[2].Value, @"\s", ""));
                    foreach (Match m in sizeRx.Matches(text)) sizes.Add(m.Groups[1].Value);
                }
            }
            return (colours.Count, sizes.Count);
        }

        // ---------- grading ----------

        static string Pct(float v) => float.IsNaN(v) ? "n/a" : (v * 100f).ToString("0.0", CultureInfo.InvariantCulture) + "%";
        static string Num(float v, string f = "0.00") => float.IsNaN(v) ? "n/a" : v.ToString(f, CultureInfo.InvariantCulture);

        // full points at or under (over) pass, half at or under (over) near, else none
        static float Low(float v, float pass, float near, float max) => float.IsNaN(v) ? 0 : v <= pass ? max : v <= near ? max / 2 : 0;
        static float High(float v, float pass, float near, float max) => float.IsNaN(v) ? 0 : v >= pass ? max : v >= near ? max / 2 : 0;

        static float Mean(IEnumerable<float> v) { var l = v.Where(x => !float.IsNaN(x)).ToList(); return l.Count == 0 ? float.NaN : l.Average(); }

        /// <summary>The automatic checks: 37 of the 50 Clean points (the other 13, text legibility and overlaps, are ticked on the rating page).</summary>
        public static List<Result> Grade(List<Shot> shots, float medianFps, float lowFps, (int strayColours, int fontSizes) code)
        {
            foreach (var s in shots)
            {
                if (s.ui != null && s.noUi != null && s.useFootprint) s.uiCoverage = UiCoverage(s.ui, s.noUi);
                if (s.noUi != null && s.useSaturation) s.saturation = StrongColour(s.noUi);
                if (s.noUi != null && s.useClipped) s.clipped = Clipped(s.noUi);
                if (s.noUi != null && s.noPlanes != null && s.usePlanes) s.pop = Pop(s.noUi, s.noPlanes);
                if (s.depth != null) s.depthRatio = DepthRatio(s.depth);
            }
            var r = new List<Result>();
            int stray = code.strayColours, fonts = code.fontSizes;
            r.Add(new Result { name = "Palette discipline", max = 6, value = stray < 0 ? "n/a (run in the editor)" : $"{stray} colour literals outside the tokens", target = "0 (12 or fewer tokens)",
                               points = stray < 0 ? 0 : stray == 0 ? 6 : stray <= 20 ? 3 : 0 });
            r.Add(new Result { name = "Type discipline", max = 4, value = fonts < 0 ? "n/a (run in the editor)" : $"{fonts} font sizes", target = "4 sizes",
                               points = fonts < 0 ? 0 : fonts <= 4 ? 4 : fonts <= 6 ? 2 : 0 });
            float foot = Mean(shots.Where(s => s.useFootprint).Select(s => s.uiCoverage));
            r.Add(new Result { name = "UI footprint", max = 6, value = Pct(foot) + " of the screen (planning shots)", target = "18% or less", points = Low(foot, 0.18f, 0.25f, 6) });
            float sat = Mean(shots.Where(s => s.useSaturation).Select(s => s.saturation));
            r.Add(new Result { name = "Saturation ceiling", max = 5, value = Pct(sat) + " of pixels over 0.5", target = "12% or less", points = Low(sat, 0.12f, 0.22f, 5) });
            float depth = Mean(shots.Select(s => s.depthRatio));
            r.Add(new Result { name = "Aerial depth", max = 5, value = Num(depth) + " far/near colour distance from the horizon sky", target = "0.40 or less", points = Low(depth, 0.40f, 0.60f, 5) });
            var pops = shots.Where(s => s.usePlanes && !float.IsNaN(s.pop)).Select(s => s.pop).ToList();
            float pop = pops.Count == 0 ? float.NaN : pops.Min();
            r.Add(new Result { name = "Aircraft pop", max = 6, value = Num(pop, "0.0") + " delta E (worst shot)", target = "20 or more", points = High(pop, 20f, 12f, 6) });
            float clip = Mean(shots.Where(s => s.useClipped).Select(s => s.clipped));
            r.Add(new Result { name = "Clipped pixels", max = 2, value = Pct(clip), target = "0.5% or less", points = Low(clip, 0.005f, 0.015f, 2) });
            r.Add(new Result { name = "Frame time", max = 3, value = float.IsNaN(medianFps) ? "n/a" : $"{medianFps:0} fps median, {lowFps:0} fps 1% low" + (Application.isEditor ? " (editor)" : ""),
                               target = "60 median, 45 1% low", points = float.IsNaN(medianFps) ? 0 : medianFps >= 60 && lowFps >= 45 ? 3 : medianFps >= 45 ? 1.5f : 0 });
            return r;
        }

        // ---------- the report ----------

        static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");

        /// <summary>Writes the shots (JPEG), score.json, the rating page, and a row in history.csv next to the run's folder.</summary>
        public static void Write(string dir, List<Shot> shots, List<Result> results)
        {
            Directory.CreateDirectory(Path.Combine(dir, "shots"));
            foreach (var s in shots)
                if (s.ui != null) File.WriteAllBytes(Path.Combine(dir, "shots", s.id + ".jpg"), s.ui.EncodeToJPG(90));

            string run = Path.GetFileName(dir.TrimEnd('/', '\\'));
            string parent = Path.GetDirectoryName(dir.TrimEnd('/', '\\'));
            string prev = Directory.GetDirectories(parent)
                .Select(Path.GetFileName)
                .Where(n => n != run && string.CompareOrdinal(n, run) < 0 && File.Exists(Path.Combine(parent, n, "score.json")))
                .OrderBy(n => n, StringComparer.Ordinal).LastOrDefault();

            float auto = results.Sum(x => x.points);
            var ci = CultureInfo.InvariantCulture;
            var json = new StringBuilder();
            json.Append("{\"run\":\"").Append(Esc(run)).Append("\",\"prev\":").Append(prev == null ? "null" : "\"" + Esc(prev) + "\"");
            json.Append(",\"resolution\":\"").Append(shots.Count > 0 && shots[0].ui != null ? $"{shots[0].ui.width}x{shots[0].ui.height}" : "").Append('"');
            json.Append(",\"mood\":\"").Append(WorldLook.OldLight ? "old light" : WorldLook.Current.ToString()).Append('"');
            json.Append(",\"autoPoints\":").Append(auto.ToString("0.0", ci)).Append(",\"autoMax\":37");
            json.Append(",\"checks\":[");
            json.Append(string.Join(",", results.Select(x =>
                $"{{\"name\":\"{Esc(x.name)}\",\"value\":\"{Esc(x.value)}\",\"target\":\"{Esc(x.target)}\",\"points\":{x.points.ToString("0.0", ci)},\"max\":{x.max.ToString("0", ci)},\"grade\":\"{x.Grade}\"}}")));
            json.Append("],\"shots\":[");
            json.Append(string.Join(",", shots.Select(s =>
                $"{{\"id\":\"{Esc(s.id)}\",\"title\":\"{Esc(s.title)}\",\"ui\":\"{Pct(s.uiCoverage)}\",\"sat\":\"{Pct(s.saturation)}\",\"pop\":\"{Num(s.pop, "0.0")}\",\"depth\":\"{Num(s.depthRatio)}\"}}")));
            json.Append("]}");
            File.WriteAllText(Path.Combine(dir, "score.json"), json.ToString());
            File.WriteAllText(Path.Combine(dir, "rate.html"), ScorecardPage.Html.Replace("__DATA__", json.ToString()));

            string csv = Path.Combine(parent, "history.csv");
            bool fresh = !File.Exists(csv);
            var row = new StringBuilder();
            if (fresh) row.Append("run,resolution,auto_of_37," + string.Join(",", results.Select(x => x.name.Replace(' ', '_').ToLowerInvariant())) + "\n");
            row.Append(run).Append(',').Append(shots.Count > 0 && shots[0].ui != null ? $"{shots[0].ui.width}x{shots[0].ui.height}" : "").Append(',').Append(auto.ToString("0.0", ci));
            foreach (var x in results) row.Append(',').Append(x.points.ToString("0.0", ci));
            row.Append('\n');
            File.AppendAllText(csv, row.ToString());
            Debug.Log($"OODA scorecard: {auto:0.0} of 37 automatic Clean points. " + string.Join("; ", results.Select(x => $"{x.name} {x.Grade} ({x.value})")) + $". Report: {dir}");
        }
    }
}
