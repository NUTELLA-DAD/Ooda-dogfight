// The island outside Unity: builds the same heights as OODA > Build Test World (with Unity's noise swapped for Perlin's, so
// the hills differ in detail), checks what the design promises, and draws it.
//   dotnet run -- <out folder>
// Writes map.ppm (from above, with the routes, the rail and the arenas) and a few views as view_*.ppm, and prints the checks.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Ooda.Flight.Core;
using Ooda.Turn.Core;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;

namespace Ooda.EditorTools
{
    internal static partial class AlpineWorld
    {
        static string ArtRoot = "../../";

        /// <summary>The model's mesh from its committed OBJ (Unity's OBJ import mirrors x: so does this) and, for land, its height grid.</summary>
        static void Load(LandModel m, bool heights)
        {
            string path = Path.Combine(ArtRoot, m.NearObj);
            if (!File.Exists(path)) { Debug.LogWarning($"no {path}"); return; }
            var vs = new List<Vector3>(); var tris = new List<int>();
            foreach (var line in File.ReadLines(path))
            {
                if (line.StartsWith("v "))
                {
                    var p = line.Split(' ');
                    vs.Add(new Vector3(-float.Parse(p[1], CultureInfo.InvariantCulture), float.Parse(p[2], CultureInfo.InvariantCulture), float.Parse(p[3], CultureInfo.InvariantCulture)));
                }
                else if (line.StartsWith("f "))
                {
                    var p = line.Split(' ');
                    for (int k = 1; k <= 3; k++) tris.Add(int.Parse(p[k].Split('/')[0]) - 1);
                }
            }
            Vector3 lo = vs[0], hi = vs[0];
            foreach (var v in vs) { lo = Vector3.Min(lo, v); hi = Vector3.Max(hi, v); }
            var mesh = new Mesh { vertices = vs.ToArray(), triangles = tris.ToArray(), bounds = new Bounds { min = lo, max = hi } };
            m.near = m.far = mesh;
            var b = mesh.bounds;
            m.fit = m.byHeight ? m.size / b.size.y : m.size / Mathf.Max(b.size.x, b.size.z);
            m.centre = new Vector3(b.center.x, b.min.y, b.center.z);
            if (!heights) return;
            m.hf = ModelHeights(m.near, m.fit, m.centre);
            m.top = 0;
            foreach (var y in m.hf) m.top = Mathf.Max(m.top, y);
        }

        static int fails;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) fails++; }

        public static int Preview(string outDir)
        {
            Directory.CreateDirectory(outDir);
            var rail = RailPath();
            var h = Heights(rail);
            var original = (float[,])h.Clone();
            var states = Classify(rail, original);
            Flatten(h, rail, states);
            double GroundFt(double xFt, double yFt) => Math.Max(0, SampleArr(h, (float)(xFt * Atmosphere.MPerFt), (float)(yFt * Atmosphere.MPerFt))) * Atmosphere.FtPerM;

            Console.WriteLine("\nThe railway");
            float len = rail.Count * RailStep, grade = 0;
            for (int k = 1; k < rail.Count; k++) grade = Math.Max(grade, Math.Abs(rail[k].y - rail[k - 1].y) / RailStep);
            Console.WriteLine($"  {len / 1000f:F1} km, rail {rail.Min(p => p.y):F0} to {rail.Max(p => p.y):F0} m, steepest {grade * 100:F1} %");
            var runs = new List<(State s, int a, int b)>();
            for (int k = 0; k < states.Length;) { int e = k; while (e + 1 < states.Length && states[e + 1] == states[k]) e++; runs.Add((states[k], k, e)); k = e + 1; }
            foreach (var r in runs.Where(r => r.s != State.Ground))
            {
                float depth = 0;
                for (int k = r.a; k <= r.b; k++) depth = Math.Max(depth, Math.Abs(rail[k].y - SampleArr(original, rail[k].x, rail[k].z)));
                var c = rail[(r.a + r.b) / 2];
                Console.WriteLine($"  {r.s} {(r.b - r.a) * RailStep:F0} m at ({c.x:F0}, {c.z:F0}), {(r.s == State.Bridge ? "deck" : "rail")} {c.y:F0} m, {(r.s == State.Bridge ? "up to " + depth.ToString("F0") + " m high" : depth.ToString("F0") + " m of rock over it")}, {100f * r.a / rail.Count:F0}-{100f * r.b / rail.Count:F0} % along");
            }
            int bridges = runs.Count(r => r.s == State.Bridge), tunnels = runs.Count(r => r.s == State.Tunnel);
            Check(bridges >= 3 && tunnels >= 2, $"{bridges} bridges and {tunnels} tunnels");
            Check(grade <= RailGrade + 0.001f, $"the grade stays within {RailGrade * 100:F0} %");
            var cross = runs.Where(r => r.s == State.Bridge).OrderBy(r => Enumerable.Range(r.a, r.b - r.a + 1).Min(k => MainDistance(rail[k].x, rail[k].z, out _))).First();
            var cc = rail[(cross.a + cross.b) / 2];
            RouteGap(cc, out float ry);
            Check(MainDistance(cc.x, cc.z, out _) < 400 && cc.y < ry - 120, $"the viaduct over the main valley at ({cc.x:F0}, {cc.z:F0}), deck {cc.y:F0} m, {ry - cc.y:F0} m under the bombers");

            Console.WriteLine("\nThe escort route");
            float least = float.MaxValue; Vector3 leastAt = default;
            for (int i = 0; i + 1 < EscortRoute.Length; i++)
            {
                Vector3 a = EscortRoute[i], b = EscortRoute[i + 1];
                int n = Mathf.CeilToInt(Vector3.Distance(a, b) / 25f);
                for (int k = 0; k <= n; k++)
                {
                    var p = Vector3.Lerp(a, b, k / (float)n);
                    var side = Vector3.Cross(Vector3.up, (b - a).normalized);
                    foreach (float o in new[] { -150f, -75f, 0f, 75f, 150f })
                    {
                        var q = p + side * o;
                        float c = p.y - SampleArr(h, q.x, q.z);
                        if (c < least) { least = c; leastAt = q; }
                    }
                }
            }
            Check(least > 200, $"the bombers' corridor is at least {least:F0} m over the ground (least at {leastAt.x:F0}, {leastAt.z:F0}); {EscortRoute.Length} points, Valley Run from {ValleyRunStartM:F0} m");

            Console.WriteLine("\nThe arenas: every start, range and height, 4 v 4");
            foreach (var a in Arenas.All)
            {
                double worst = 0; int lifted = 0, n = 0; string worstWhat = "";
                double minFloor = double.MaxValue, maxFloor = 0;
                foreach (Scenarios.Start st in Enum.GetValues(typeof(Scenarios.Start)))
                    foreach (DogfightSetup.Range rg in Enum.GetValues(typeof(DogfightSetup.Range)))
                        foreach (DogfightSetup.Height hg in Enum.GetValues(typeof(DogfightSetup.Height)))
                        {
                            var s = new DogfightSetup { ours = DogfightSetup.Resize(new[] { Craft.Mustang }, 4), theirs = DogfightSetup.Resize(new[] { Craft.Bf109 }, 4), randomStart = false, start = st, range = rg, height = hg, randomArena = false };
                            var b = Scenarios.Dogfight(s, a.CentreFt, a.altFt, 300, 3000, true, a.axisDeg);
                            for (int i = 0; i < b.Count; i++) { var p = b.planes[i].state.pos; double g = GroundFt(p.x, p.y); minFloor = Math.Min(minFloor, p.z - g); maxFloor = Math.Max(maxFloor, g); }
                            double lift = Scenarios.LiftClear(b, GroundFt);
                            n++;
                            if (lift > 0) lifted++;
                            if (lift > worst) { worst = lift; worstWhat = $"{st}/{rg}/{hg}"; }
                        }
                float cg = SampleArr(h, (float)a.x, (float)a.z);
                Console.WriteLine($"  {a.name}: ground at the middle {cg:F0} m, start {a.altFt * Atmosphere.MPerFt:F0} m; highest ground under anyone {maxFloor * Atmosphere.MPerFt:F0} m; lifted in {lifted} of {n}, at most {worst:F0} ft ({worstWhat})");
                Check(lifted < n / 3, $"{a.name}: starts are clear (lifted in {lifted} of {n}, at most {worst:F0} ft)");
            }

            Console.WriteLine("\nThe dressing");
            var village = VillageSpots(h, rail); var farms = FarmSpots(h, rail); var trees = TreeSpots(h, rail); var footbridges = RiverBridges(h);
            Check(village.Count >= 20, $"{village.Count} chalets in the village");
            Check(farms.Count >= 40, $"{farms.Count} farmhouses");
            Check(trees.Count >= 11000, $"{trees.Count} trees ({trees.Count(t => t.kind == 0)} round, {trees.Count(t => t.kind == 1)} tall, {trees.Count(t => t.kind == 2)} pines)");
            Check(footbridges.Count >= 3, $"{footbridges.Count} stone bridges over the river");
            Check(Peaks.Count(p => p.model == MountainModel) >= 70, $"{Peaks.Count(p => p.model == MountainModel)} mountains and {Peaks.Count(p => p.model == FoothillModel)} grass hills");
            var cover = new double[CoverLayers]; var w = new float[CoverLayers];
            for (int j = 0; j < 256; j++) for (int i = 0; i < 256; i++) { GroundCover(h, -Size / 2 + (i + 0.5f) * Size / 256, -Size / 2 + (j + 0.5f) * Size / 256, w); for (int k = 0; k < CoverLayers; k++) cover[k] += w[k] / 65536.0; }
            Console.WriteLine($"  ground: meadow {cover[0]:P0}, forest {cover[1]:P0}, rock {cover[2]:P0}, snow {cover[3]:P0}, field {cover[4]:P0}, pasture {cover[5]:P0}");

            Draw(outDir, h, rail, states, village, farms, trees, footbridges);
            Console.WriteLine(fails == 0 ? "\nAll checks pass." : $"\n{fails} check(s) failed.");
            return fails;
        }

        // ---------- pictures ----------

        static readonly Vector3[] CoverColour =
        {
            new Vector3(0.40f, 0.62f, 0.24f), new Vector3(0.18f, 0.38f, 0.19f), new Vector3(0.50f, 0.48f, 0.52f),
            new Vector3(0.94f, 0.96f, 0.99f), new Vector3(0.84f, 0.72f, 0.34f), new Vector3(0.52f, 0.76f, 0.27f),
        };
        static readonly Vector3 Turquoise = new Vector3(0.22f, 0.72f, 0.80f), Sea = new Vector3(0.10f, 0.30f, 0.48f), Sky = new Vector3(0.62f, 0.78f, 0.95f);

        static Vector3 SunDir()
        {
            var f = Quaternion.Euler(42f, -35f, 0f) * Vector3.forward;   // where the light goes
            return -f;
        }

        /// <summary>The water at a point, if any: its level (the sea at 0, a lake's, the river's).</summary>
        static bool WaterAt(float[,] h, float x, float z, out float level)
        {
            float g = SampleArr(h, x, z);
            level = 0;
            if (g < 0) return true;
            foreach (var l in Lakes) { float ex = (x - l.c.x) / l.r.x, ez = (z - l.c.y) / l.r.y; if (ex * ex + ez * ez < 1.1f && g < l.level) { level = l.level; return true; } }
            if (RiverNear(x, z))
            {
                // the nearest river point's surface
                var r = River(); float best = float.MaxValue; RiverPoint bp = default;
                for (int k = 0; k < r.Count; k += 3) { float d = Vector2.Distance(r[k].p, V(x, z)); if (d < best) { best = d; bp = r[k]; } }
                if (best < bp.half && g < bp.surface) { level = bp.surface; return true; }
            }
            return false;
        }

        static Vector3 Shade(float[,] h, float x, float z, float[] w)
        {
            GroundCover(h, x, z, w);
            Vector3 c = Vector3.zero;
            for (int k = 0; k < CoverLayers; k++) c += CoverColour[k] * w[k];
            // under a mountain model its own look shows: rock, snow high up (a grass hill is green)
            if (UnderMountain(x, z))
            {
                float y = SampleArr(h, x, z);
                bool hill = Peaks.Any(p => p.model == FoothillModel && Vector2.Distance(V(p.pos.x, p.pos.z), V(x, z)) < p.width * 0.5f);
                c = hill ? new Vector3(0.42f, 0.62f, 0.26f) : y > 2300 ? new Vector3(0.93f, 0.95f, 0.99f) : y > 1600 ? new Vector3(0.62f, 0.62f, 0.66f) : new Vector3(0.46f, 0.50f, 0.42f);
            }
            float d = Cell;
            var n = new Vector3(SampleArr(h, x - d, z) - SampleArr(h, x + d, z), 2 * d, SampleArr(h, x, z - d) - SampleArr(h, x, z + d)).normalized;
            float lit = 0.45f + 0.6f * Math.Max(0, Vector3.Dot(n, SunDir()));
            return c * lit;
        }

        static void Draw(string dir, float[,] h, List<Vector3> rail, State[] states, List<HouseSpot> village, List<HouseSpot> farms, List<TreeSpot> trees, List<BridgeSpot> bridges)
        {
            const int N = 1400;
            var img = new Vector3[N, N];
            var w = new float[CoverLayers];
            float Px(float m) => (m + Size / 2) / Size * N;
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    float x = -Size / 2 + (i + 0.5f) * Size / N, z = Size / 2 - (j + 0.5f) * Size / N;
                    img[j, i] = WaterAt(h, x, z, out float lv) ? (lv <= 0 ? Sea : Turquoise) : Shade(h, x, z, w);
                }
            void Dot(float x, float z, Vector3 col, int r)
            {
                int ci = (int)Px(x), cj = (int)(N - Px(z));
                for (int jj = cj - r; jj <= cj + r; jj++) for (int ii = ci - r; ii <= ci + r; ii++)
                    if (ii >= 0 && jj >= 0 && ii < N && jj < N && (ii - ci) * (ii - ci) + (jj - cj) * (jj - cj) <= r * r) img[jj, ii] = col;
            }
            void Line(Vector2 a, Vector2 b, Vector3 col, int r, float dash = 0)
            {
                float L = Vector2.Distance(a, b); int n = Math.Max(1, (int)(L / (Size / N * 0.5f)));
                for (int k = 0; k <= n; k++) { if (dash > 0 && ((int)(k * L / n / dash)) % 2 == 1) continue; var p = Vector2.Lerp(a, b, k / (float)n); Dot(p.x, p.y, col, r); }
            }
            foreach (var t in trees) Dot(t.x, t.z, t.kind == 2 ? new Vector3(0.08f, 0.25f, 0.12f) : new Vector3(0.12f, 0.33f, 0.10f), 0);
            for (int i = 0; i + 1 < EscortRoute.Length; i++) Line(V(EscortRoute[i].x, EscortRoute[i].z), V(EscortRoute[i + 1].x, EscortRoute[i + 1].z), new Vector3(1f, 0.9f, 0.1f), 1, 120);
            for (int k = 0; k + 1 < rail.Count; k++)
            {
                var col = states[k] == State.Bridge ? new Vector3(0.9f, 0.1f, 0.1f) : states[k] == State.Tunnel ? new Vector3(0.35f, 0.1f, 0.35f) : new Vector3(0.1f, 0.08f, 0.06f);
                if (states[k] == State.Tunnel && (k / 8) % 2 == 1) continue;
                Line(V(rail[k].x, rail[k].z), V(rail[k + 1].x, rail[k + 1].z), col, 1);
            }
            foreach (var a in Arenas.All)
            {
                Vector2 d = ArenaDir(a), across = V(d.y, -d.x), c = V((float)a.x, (float)a.z);
                float L = (float)a.halfLengthM, W = (float)a.halfWidthM;
                var col = new Vector3(1f, 0.35f, 0.9f);
                Line(c - d * L - across * W, c + d * L - across * W, col, 1, 200); Line(c - d * L + across * W, c + d * L + across * W, col, 1, 200);
                Line(c - d * L - across * W, c - d * L + across * W, col, 1, 200); Line(c + d * L - across * W, c + d * L + across * W, col, 1, 200);
                Line(c - d * L * 0.5f, c + d * L * 0.5f, col, 2);
            }
            foreach (var v in village) Dot(v.pos.x, v.pos.z, new Vector3(0.7f, 0.35f, 0.15f), 2);
            foreach (var f in farms) Dot(f.pos.x, f.pos.z, new Vector3(0.75f, 0.25f, 0.12f), 3);
            foreach (var b in bridges) Dot(b.at.x, b.at.z, new Vector3(0.95f, 0.95f, 0.85f), 4);
            Dot(TownCentre.x, TownCentre.y, new Vector3(0.3f, 0.3f, 0.3f), 9);
            Line(RunwayStart, RunwayEnd, new Vector3(0.85f, 0.85f, 0.85f), 2);
            foreach (var p in Peaks.Where(p => p.model == MountainModel)) Dot(p.pos.x, p.pos.z, new Vector3(0.2f, 0.2f, 0.25f), 1);
            Save(Path.Combine(dir, "map.ppm"), img);

            // views from the air
            View(dir, "view_lake", h, new Vector3(0, 650, -4400), new Vector3(0, 300, 0));
            View(dir, "view_village", h, new Vector3(-700, 420, -1300), new Vector3(150, 230, 300));
            View(dir, "view_pass", h, (Vector3)ArenaAt3(HighPass, -3400, 1500), (Vector3)ArenaAt3(HighPass, 1500, 1000));
            View(dir, "view_maze", h, (Vector3)ArenaAt3(PeakMaze, -3300, 1700), (Vector3)ArenaAt3(PeakMaze, 800, 1500));
            View(dir, "view_farmland", h, new Vector3(-3500, 900, -8200), new Vector3(3400, 100, -5000));
            View(dir, "view_north", h, new Vector3(0, 2600, -2000), new Vector3(0, 1200, 5600));
            View(dir, "view_canyon", h, new Vector3(MainLine[9].x - 300, MainFloorAt(MainLine[9].y) + 250, MainLine[9].y - 300), new Vector3(2650, 1150, 3400));
        }

        static Vector3 ArenaAt3(Arena a, float along, float y) { var p = ArenaAt(a, along); return new Vector3(p.x, y, p.y); }

        static void View(string dir, string name, float[,] h, Vector3 eye, Vector3 at)
        {
            const int W = 960, H = 540;
            var img = new Vector3[H, W];
            var f = (at - eye).normalized; var r = Vector3.Cross(Vector3.up, f).normalized; var u = Vector3.Cross(f, r);
            float tan = Mathf.Tan(30f * Mathf.Deg2Rad);
            var w = new float[CoverLayers];
            System.Threading.Tasks.Parallel.For(0, H, j =>
            {
                var ww = new float[CoverLayers];
                for (int i = 0; i < W; i++)
                {
                    float sx = (2 * (i + 0.5f) / W - 1) * tan * W / H, sy = (1 - 2 * (j + 0.5f) / H) * tan;
                    var d = (f + r * sx + u * sy).normalized;
                    float t = 1; Vector3 col = Sky * (0.85f + 0.15f * Math.Max(0, d.y * 4)); bool hit = false;
                    while (t < 30000)
                    {
                        var p = eye + d * t;
                        if (Math.Abs(p.x) > Size / 2 || Math.Abs(p.z) > Size / 2) { if (p.y < 0) { col = Sea; hit = true; } break; }
                        float g = SampleArr(h, p.x, p.z);
                        bool wet = WaterAt(h, p.x, p.z, out float lv);
                        float top = wet ? Math.Max(g, lv) : g;
                        if (p.y <= top) { col = wet && lv >= g ? (lv <= 0 ? Sea : Turquoise) : Shade(h, p.x, p.z, ww); hit = true; break; }
                        t += Math.Max(2f, (p.y - top) * 0.35f) * (1 + t / 6000f);
                    }
                    if (hit) { float fog = Mathf.InverseLerp(2000, 24000, t); col = col * (1 - fog) + new Vector3(0.68f, 0.78f, 0.90f) * fog; }
                    img[j, i] = col;
                }
            });
            Save(Path.Combine(dir, name + ".ppm"), img);
        }

        static void Save(string path, Vector3[,] img)
        {
            int H = img.GetLength(0), W = img.GetLength(1);
            using var fs = File.Create(path);
            var head = System.Text.Encoding.ASCII.GetBytes($"P6\n{W} {H}\n255\n");
            fs.Write(head);
            var row = new byte[W * 3];
            for (int j = 0; j < H; j++)
            {
                for (int i = 0; i < W; i++) { var c = img[j, i]; row[i * 3] = B(c.x); row[i * 3 + 1] = B(c.y); row[i * 3 + 2] = B(c.z); }
                fs.Write(row);
            }
        }
        static byte B(float v) => (byte)Math.Clamp((int)(v * 255), 0, 255);
    }

    static class Program
    {
        static int Main(string[] args) => AlpineWorld.Preview(args.Length > 0 ? args[0] : "out");
    }
}
