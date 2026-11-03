using System.Collections.Generic;
using System.Linq;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.EditorTools
{
    /// <summary>
    /// The island's design: where everything goes and the shape of the ground, with no Unity objects in it (only its
    /// maths), so Tools~/WorldPreview can build the same heights outside Unity and check them.
    ///
    /// Three zones from south to north. The south is farmland: low rolling fields and pasture down to the coast, the
    /// river winding across it to the sea, farmhouses dotted about, and the factory town with its runway. The middle is
    /// pine valleys: the chalet lake with its village and church at the north end, the main valley weaving north from it
    /// between mountains, and the railway crossing it on a stone viaduct. The north is the snowy peaks: tall rock and snow,
    /// the bowl of the Peak Maze with canyons leading out of it, and the high pass the railway climbs to, through tunnels
    /// and over viaducts. The dogfight's four arenas (Ooda.Turn.Core.Arenas) are carved and kept clear here.
    /// Units are metres, Unity x east and z north; heights are above the sea.
    /// </summary>
    internal static partial class AlpineWorld
    {
        public const float Size = 20000f, Base = -80f, Height = 7000f;   // room for the mountain models over the noise peaks
        const int Res = 1025;                      // heightmap: about 20 m a sample
        const int SplatRes = 1024;
        const float Cell = Size / (Res - 1);

        static Vector2 V(float x, float y) => new Vector2(x, y);

        // ---------- the zones ----------

        /// <summary>How much of the north (the snowy peaks) a point is in, 0 to 1: from about 2.4 km north of the middle to 4.4 km, the line wandering.</summary>
        static float NorthOf(float x, float z) => Step(2400f, 4400f, z + Wander(x, z));
        /// <summary>How much of the south (the farmland) a point is in, 0 to 1: south of about 4.8 km south of the middle, fading out by 2.6 km.</summary>
        static float SouthOf(float x, float z) => 1 - Step(-4800f, -2600f, z + Wander(x, z));
        static float Wander(float x, float z) => 1800f * (Mathf.PerlinNoise(x / 5200f + 31.7f, z / 5200f + 8.3f) - 0.5f);

        // ---------- the main valley and the river ----------
        // From the sea at the south coast north across the farmland (the river's plain), up a short climb to the chalet
        // lake, past the village at its north end, then a pass that turns 15 to 25 degrees every 1.5 km, weaving between
        // the mountain models into the snowy north. The escort route flies its centreline; the river runs down it.

        const float MainLeg = 1500f;
        static readonly float[] MainTurns = { 20, -20, 0, 25, -15 };   // headings, degrees east of north
        /// <summary>The main line's points from the sea (0) north; from NarrowFrom on it is the narrow pass.</summary>
        static readonly Vector2[] MainLine = MakeMainLine();
        const int NarrowFrom = 6;

        static Vector2[] MakeMainLine()
        {
            var pts = new List<Vector2> { V(0, -9300), V(0, -8300), V(-500, -6800), V(300, -5200), V(0, -3700), V(0, -2800), V(0, -600) };
            Vector2 p = pts[pts.Count - 1];
            foreach (float hdg in MainTurns)
            {
                p += MainLeg * V(Mathf.Sin(hdg * Mathf.Deg2Rad), Mathf.Cos(hdg * Mathf.Deg2Rad));
                pts.Add(p);
            }
            return pts.ToArray();
        }

        static readonly float[] FloorZ = { -9300, -8300, -3700, -2800, -600, 6700 };
        static readonly float[] FloorY = { 12, 20, 110, 172, 180, 770 };

        /// <summary>The main valley's floor at a point on its line: 20 m at the coast, 110 m at the head of the farmland, the lake at
        /// about 175 m, then rising up the pass to about 770 m at its north end.</summary>
        static float MainFloorAt(float z)
        {
            if (z <= FloorZ[0]) return FloorY[0];
            for (int k = 0; k + 1 < FloorZ.Length; k++)
                if (z <= FloorZ[k + 1]) return Mathf.Lerp(FloorY[k], FloorY[k + 1], Mathf.InverseLerp(FloorZ[k], FloorZ[k + 1], z));
            return FloorY[FloorY.Length - 1];
        }

        /// <summary>How far a point is from the narrow part of the main valley's line, and the floor height there.</summary>
        static float MainDistance(float x, float z, out float floor)
        {
            float best = float.MaxValue; floor = 0;
            var q = V(x, z);
            for (int s = NarrowFrom; s + 1 < MainLine.Length; s++)
            {
                Vector2 a = MainLine[s], b = MainLine[s + 1];
                float u = Mathf.Clamp01(Vector2.Dot(q - a, b - a) / (b - a).sqrMagnitude);
                var c = Vector2.Lerp(a, b, u);
                float d = Vector2.Distance(q, c);
                if (d < best) { best = d; floor = MainFloorAt(c.y); }
            }
            return best;
        }

        // The old single fight spot: now the Chalet Lake arena (the dogfight picks from all four, Arenas.All).
        public static readonly Vector3 FightCentre = new Vector3((float)Arenas.All[2].x, 0, (float)Arenas.All[2].z);
        public static readonly float FightAltFt = (float)Arenas.All[2].altFt;
        /// <summary>Free flight starts low over the south end of the lake, heading north up it toward the village.</summary>
        public static readonly Vector3 FreeFlightSpawn = new Vector3(0, 520f, -3300f);

        /// <summary>The bombers' way up the main valley (x east, height, z north; the first point is the start): from the coast
        /// its centreline at 300 m over the floor, over the lake and the village and up the pass, turning 25 degrees at most.</summary>
        public static readonly Vector3[] EscortRoute = MainLine.Skip(1).Select(p => new Vector3(p.x, MainFloorAt(p.y) + 300f, p.y)).ToArray();

        /// <summary>How far along the escort route (from its start at the coast) the narrow pass begins, m: where the Valley Run starts.</summary>
        public static readonly float ValleyRunStartM = Enumerable.Range(1, NarrowFrom - 1).Sum(i => Vector2.Distance(MainLine[i], MainLine[i + 1])) + 300f;

        /// <summary>Flak along the route: on the valley floor beside the line, alternating sides, over the farmland and up the pass (none over the lake or the village).</summary>
        public static readonly Vector3[] FlakSites = Enumerable.Range(0, 8).Select(i =>
        {
            int s = new[] { 1, 2, 3, 4, 7, 8, 9, 10 }[i];
            Vector2 a = MainLine[s], b = MainLine[s + 1], mid = Vector2.Lerp(a, b, 0.55f);
            Vector2 side = V(b.y - a.y, a.x - b.x).normalized * (i % 2 == 0 ? 60f : -60f);
            Vector2 p = mid + side;
            return new Vector3(p.x, MainFloorAt(p.y), p.y);
        }).ToArray();

        // ---------- valleys, lakes ----------

        class Valley
        {
            public string name;
            public Vector2[] pts; public float[] floor; public float halfFloor, wall; public bool gorge;
            public float minWall;   // raise the land beside the valley to at least this over its floor first, so its walls are tall
            public float guard;     // how far from its line the mountain models must stay under the ground; 0: ValleyGuard past its walls
            public float bumps = 12f;   // how far its floor rises and falls, m, either way
        }

        static Vector2 ArenaDir(Arena a) => V(Mathf.Sin((float)a.axisDeg * Mathf.Deg2Rad), Mathf.Cos((float)a.axisDeg * Mathf.Deg2Rad));
        static Vector2 ArenaAt(Arena a, float along) => V((float)a.x, (float)a.z) + ArenaDir(a) * along;
        static Arena PeakMaze => Arenas.All[0];
        static Arena HighPass => Arenas.All[1];

        /// <summary>The valleys, canyons and passes, carved in this order (each only ever lowers the ground, except where minWall raises its rims).</summary>
        static Valley[] MakeValleys()
        {
            var list = new List<Valley>();

            // the Peak Maze's two canyons first (their raised rims are then cut back where the bowl and the pass open them):
            // one west from the bowl down to the main pass, one east out to the sea. Slots 100 m wide with walls raised to at
            // least 500 m over their floors.
            var maze = PeakMaze;
            float ml = (float)maze.halfLengthM * 0.9f;
            Vector2 sw = ArenaAt(maze, -ml), mazeEast = ArenaAt(maze, 0) + V(ArenaDir(maze).y, -ArenaDir(maze).x) * 1300f;
            Vector2 mouth = MainLine[9] + V(700, 120);
            list.Add(new Valley { name = "west canyon", pts = new[] { mouth, V(1500, 3950), sw }, floor = new[] { MainFloorAt(MainLine[9].y) + 60f, 840f, 1080f }, halfFloor = 50, wall = 230, gorge = true, minWall = 500, bumps = 0 });
            list.Add(new Valley { name = "east canyon", pts = new[] { mazeEast, V(6900, 4500), V(8700, 3700) }, floor = new[] { 1090f, 760f, 300f }, halfFloor = 55, wall = 240, gorge = true, minWall = 500, bumps = 0 });
            // the Peak Maze: a bowl along its axis among the north's peaks, its floor about 1,100 m; models may stand from 1 km off its line
            list.Add(new Valley { name = "Peak Maze bowl", pts = new[] { ArenaAt(maze, -ml), ArenaAt(maze, 0), ArenaAt(maze, ml) }, floor = new[] { 1080f, 1100f, 1060f }, halfFloor = 800, wall = 1000, guard = 1000, bumps = 60 });

            // the High Pass: a broad saddle along its axis, the railway up the middle of it, its top 880 m at the arena's middle
            var pass = HighPass;
            float pl = (float)pass.halfLengthM * 0.95f;
            list.Add(new Valley { name = "High Pass", pts = new[] { ArenaAt(pass, -pl), ArenaAt(pass, 0), ArenaAt(pass, pl) }, floor = new[] { 700f, 870f, 800f }, halfFloor = 450, wall = 1100, guard = 800, bumps = 45 });

            // the railway's own valleys, where it climbs the east side and heads west across the middle: it runs along their
            // floors (the spur between the two east ones it tunnels through)
            Valley RailValley(string name, int from, int to, Vector2? start = null, Vector2? end = null)
            {
                var pts = Rail.Skip(from).Take(to - from + 1).Select(p => V(p.x, p.z)).ToList();
                var fl = Rail.Skip(from).Take(to - from + 1).Select(p => p.y - BedDepth - 1f).ToList();
                if (start != null) { pts[0] = start.Value; }
                if (end != null) { pts[pts.Count - 1] = end.Value; }
                return new Valley { name = name, pts = pts.ToArray(), floor = fl.ToArray(), halfFloor = 110, wall = 650, guard = 350, bumps = 3 };
            }
            list.Add(RailValley("east rail valley", 0, 2, end: V(6250, -1850)));
            list.Add(RailValley("east rail valley north", 3, 5, start: V(5250, -380)));
            list.Add(RailValley("west rail valley", 7, 9));

            // the farmland's river plain: wide and gentle, the river down the middle of it
            list.Add(new Valley { name = "river plain", pts = MainLine.Skip(1).Take(5).ToArray(), floor = MainLine.Skip(1).Take(5).Select(p => MainFloorAt(p.y)).ToArray(), halfFloor = 450, wall = 1500, guard = 600, bumps = 20 });
            // the lake's basin: the chalet lake and, at its north end, the village on the flat
            list.Add(new Valley { name = "lake basin", pts = new[] { V(0, -3100), V(0, -1750), V(0, -250) }, floor = new[] { 172f, 175f, 186f }, halfFloor = 380, wall = 1000, guard = 700 });
            // the main valley's twisting part: a broad, gentle pass. Only its middle 450 m either side is kept clear of the
            // mountain models, so they crowd in beside it and the bombers weave between them.
            list.Add(new Valley { name = "main pass", pts = MainLine.Skip(NarrowFrom).ToArray(), floor = MainLine.Skip(NarrowFrom).Select(p => MainFloorAt(p.y)).ToArray(), halfFloor = 260, wall = 900, guard = 450 });
            return list.ToArray();
        }

        /// <summary>
        /// Cut the railway into the ground, after the models are in: where the rock over the rail bed is less than CutDepth, down
        /// to the bed 30 m wide with walls easing back as far as they need (40 to 200 m); deeper than that, and in the tunnels set
        /// by RailTunnels, the rock is left alone and Classify makes it a tunnel. Where the ground is lower than the rail it isn't
        /// touched: that is a bridge.
        /// </summary>
        static void CarveRail(float[,] h, List<Vector3> rail)
        {
            const float CutDepth = 140f;
            var cut = new bool[rail.Count];
            for (int k = 0; k < rail.Count; k++)
                cut[k] = !InRailTunnel(rail[k]) && SampleArr(h, rail[k].x, rail[k].z) - (rail[k].y - BedDepth) < CutDepth;
            // no runs under 300 m either way: they take their neighbours' state
            for (int pass = 0; pass < 2; pass++)
                for (int k = 0; k < cut.Length;)
                {
                    int e = k;
                    while (e + 1 < cut.Length && cut[e + 1] == cut[k]) e++;
                    if (e - k + 1 < 30 && k > 0 && !InRailTunnel(rail[k])) for (int q = k; q <= e; q++) cut[q] = cut[k - 1];
                    k = e + 1;
                }
            var target = new float[Res, Res];
            var has = new bool[Res, Res];
            for (int k = 0; k < rail.Count; k += 2)
            {
                if (!cut[k]) continue;
                float bed = rail[k].y - BedDepth - 0.4f;
                float over = SampleArr(h, rail[k].x, rail[k].z) - bed;
                if (over <= 0) continue;
                float wall = Mathf.Clamp(over * 1.4f, 40f, 200f), reach = 15f + wall;
                int ci = Idx(rail[k].x), cj = Idx(rail[k].z), rc = Mathf.CeilToInt(reach / Cell) + 1;
                for (int j = Mathf.Max(0, cj - rc); j <= Mathf.Min(Res - 1, cj + rc); j++)
                    for (int i = Mathf.Max(0, ci - rc); i <= Mathf.Min(Res - 1, ci + rc); i++)
                    {
                        float d = Vector2.Distance(V(X(i), X(j)), V(rail[k].x, rail[k].z));
                        if (d >= reach) continue;
                        float t = Mathf.Clamp01((d - 15f) / wall);
                        float y = bed + over * 1.3f * t * t * (3 - 2 * t);
                        if (!has[j, i] || y < target[j, i]) { target[j, i] = y; has[j, i] = true; }
                    }
            }
            for (int j = 0; j < Res; j++)
                for (int i = 0; i < Res; i++)
                    if (has[j, i]) h[j, i] = Mathf.Min(h[j, i], target[j, i]);
        }

        struct Lake { public string name; public Vector2 c, r; public float level; }
        static readonly Lake[] Lakes =
        {
            new Lake { name = "Chalet lake", c = V(0, -1750), r = V(340, 880), level = 175 },     // long and thin up the basin, the village at its north end
            new Lake { name = "Maze tarn", c = V(4350, 5820), r = V(240, 150), level = 1098 },    // a small tarn on the bowl's floor
        };

        static bool InLake(float x, float z) => Lakes.Any(l => { float ex = (x - l.c.x) / l.r.x, ez = (z - l.c.y) / l.r.y; return ex * ex + ez * ez < 1.3f; });

        // ---------- the railway: its line and its rail height (x, rail top, z) ----------
        // From the station by the town up the east side of the island, across the main valley on a stone viaduct just north
        // of the village (low enough to pass under the bombers), then west and north up to the High Pass and over it to a
        // station at the head of the main valley through the ridge beside it. Its heights are set here and kept to a 6 % grade at most (a rack railway); the
        // ground decides where it needs a bridge (more than 10 m of air under it) or a tunnel (more than 30 m of rock over it).
        // It is cut into the ground (CarveRail) where the rock over it is shallow.

        static readonly Vector3[] Rail =
        {
            new Vector3(4600, 92, -5400), new Vector3(5800, 150, -4300), new Vector3(6400, 270, -2600), new Vector3(5700, 380, -900),
            new Vector3(4000, 430, 300), new Vector3(2000, 380, 500), new Vector3(0, 340, 300), new Vector3(-2000, 400, 500),
            new Vector3(-3800, 470, 600), new Vector3(-5000, 560, 1100),
            new Vector3(-5458, 700, 1568), new Vector3(-4500, 870, 4200), new Vector3(-3542, 830, 6831), new Vector3(-2400, 840, 7300),
            new Vector3(-900, 820, 7150), new Vector3(250, 790, 6600),
        };
        const float RailGrade = 0.06f;

        /// <summary>Where the railway always goes through the rock in a tunnel (a circle round each, m): a spur on the east climb,
        /// and the ridge between the High Pass and the head of the main valley. Elsewhere deep rock makes a tunnel too.</summary>
        static readonly (Vector2 c, float r)[] RailTunnels = { (V(5900, -1100), 650f), (V(-1750, 7250), 650f) };

        static bool InRailTunnel(Vector3 p) => RailTunnels.Any(t => Vector2.Distance(V(p.x, p.z), t.c) < t.r);

        enum State { Ground, Bridge, Tunnel }
        const float RailStep = 10f;
        const float BedDepth = 0.93f;              // from the rail top down to the ground or deck under the ballast

        /// <summary>The line every RailStep metres with its rail height: the designed heights along a smooth curve, kept to the grade,
        /// and across the main valley no more than 110 m over the floor, so its viaduct passes under the bombers.</summary>
        static List<Vector3> RailPath()
        {
            var path = RailLine();
            var y = path.Select(p => p.y).ToArray();
            float rise = RailGrade * RailStep;
            for (int k = 0; k < y.Length; k++)
                if (MainDistance(path[k].x, path[k].z, out float mainFloor) < 900f) y[k] = Mathf.Min(y[k], mainFloor + 110f);
            for (int pass = 0; pass < 3; pass++)
            {
                for (int k = 1; k < y.Length; k++) y[k] = Mathf.Clamp(y[k], y[k - 1] - rise, y[k - 1] + rise);
                for (int k = y.Length - 2; k >= 0; k--) y[k] = Mathf.Clamp(y[k], y[k + 1] - rise, y[k + 1] + rise);
            }
            // the cap again, then only ever lowering either side to meet it at the grade
            for (int k = 0; k < y.Length; k++)
                if (MainDistance(path[k].x, path[k].z, out float mainFloor) < 900f) y[k] = Mathf.Min(y[k], mainFloor + 110f);
            for (int k = 1; k < y.Length; k++) y[k] = Mathf.Min(y[k], y[k - 1] + rise);
            for (int k = y.Length - 2; k >= 0; k--) y[k] = Mathf.Min(y[k], y[k + 1] + rise);
            for (int k = 0; k < path.Count; k++) path[k] = new Vector3(path[k].x, y[k], path[k].z);
            return path;
        }

        /// <summary>The railway's control points as a smooth curve, evenly every RailStep metres along the ground.</summary>
        static List<Vector3> RailLine()
        {
            var dense = new List<Vector3>();
            for (int s = 0; s + 1 < Rail.Length; s++)
            {
                Vector3 p0 = Rail[Mathf.Max(0, s - 1)], p1 = Rail[s], p2 = Rail[s + 1], p3 = Rail[Mathf.Min(Rail.Length - 1, s + 2)];
                for (int k = 0; k < 200; k++)
                {
                    float t = k / 200f;
                    dense.Add(0.5f * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t));
                }
            }
            dense.Add(Rail[Rail.Length - 1]);
            var path = new List<Vector3> { dense[0] };
            float carry = 0;
            for (int k = 1; k < dense.Count; k++)
            {
                Vector3 a = dense[k - 1], b = dense[k];
                float seg = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
                while (carry + seg >= RailStep)
                {
                    float u = (RailStep - carry) / seg;
                    a = Vector3.Lerp(a, b, u);
                    seg = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
                    path.Add(a);
                    carry = 0;
                }
                carry += seg;
            }
            return path;
        }

        static State[] Classify(List<Vector3> rail, float[,] h)
        {
            var s = new State[rail.Count];
            for (int k = 0; k < rail.Count; k++)
            {
                float g = SampleArr(h, rail[k].x, rail[k].z);
                s[k] = g < rail[k].y - 10 ? State.Bridge : g > rail[k].y + 30 ? State.Tunnel : State.Ground;
            }
            // no runs shorter than 4 steps: they take their neighbours' state
            for (int pass = 0; pass < 2; pass++)
                for (int k = 0; k < s.Length;)
                {
                    int e = k;
                    while (e + 1 < s.Length && s[e + 1] == s[k]) e++;
                    if (e - k + 1 < 4 && k > 0) for (int q = k; q <= e; q++) s[q] = s[k - 1];
                    k = e + 1;
                }
            // bridges land on solid ground: two more steps at each end
            var b = (State[])s.Clone();
            for (int k = 0; k < s.Length; k++)
                if (s[k] == State.Bridge)
                    for (int q = Mathf.Max(0, k - 2); q <= Mathf.Min(s.Length - 1, k + 2); q++)
                        if (b[q] == State.Ground) b[q] = State.Bridge;
            return b;
        }

        static void Flatten(float[,] h, List<Vector3> rail, State[] states)
        {
            var weight = new float[Res, Res];
            var target = new float[Res, Res];
            int rc = Mathf.CeilToInt(60f / Cell) + 1;
            for (int k = 0; k < rail.Count; k++)
            {
                bool nearEnd = states[k] != State.Ground && ((k > 0 && states[k - 1] == State.Ground) || (k + 1 < states.Length && states[k + 1] == State.Ground));
                if (states[k] != State.Ground && !nearEnd) continue;
                Vector3 p = rail[k];
                int ci = Idx(p.x), cj = Idx(p.z);
                for (int j = Mathf.Max(0, cj - rc); j <= Mathf.Min(Res - 1, cj + rc); j++)
                    for (int i = Mathf.Max(0, ci - rc); i <= Mathf.Min(Res - 1, ci + rc); i++)
                    {
                        float dx = X(i) - p.x, dz = X(j) - p.z;
                        float w = 1 - Step(14f, 60f, Mathf.Sqrt(dx * dx + dz * dz));
                        if (w > weight[j, i]) { weight[j, i] = w; target[j, i] = p.y - BedDepth; }
                    }
            }
            for (int j = 0; j < Res; j++)
                for (int i = 0; i < Res; i++)
                    if (weight[j, i] > 0) h[j, i] = Mathf.Lerp(h[j, i], target[j, i], weight[j, i]);
        }

        static bool NearRail(List<Vector3> rail, float x, float z, float r)
        {
            for (int k = 0; k < rail.Count; k += 2)
            {
                float dx = rail[k].x - x, dz = rail[k].z - z;
                if (dx * dx + dz * dz < r * r) return true;
            }
            return false;
        }

        // ---------- the river ----------
        // Down the main valley from the north end of the pass into the lake by the village, out of its south end, and across
        // the farmland to the sea, meandering either side of the valley's line. Its surface is 7 m under the valley floor (under
        // the floor's little bumps), in a channel that widens from 28 m up the pass to 80 m at the coast.

        struct RiverPoint { public Vector2 p; public float surface, half; }

        static List<RiverPoint> river;

        static List<RiverPoint> River()
        {
            if (river != null) return river;
            river = new List<RiverPoint>();
            // the line from north to south: the main line's points in reverse, a smooth curve, every 10 m
            var ctrl = MainLine.Reverse().Take(MainLine.Length - 1).ToList();   // to the coast point; the sea does the rest
            ctrl.Add(V(0, -9000));
            var dense = new List<Vector2>();
            for (int s = 0; s + 1 < ctrl.Count; s++)
            {
                Vector2 p0 = ctrl[Mathf.Max(0, s - 1)], p1 = ctrl[s], p2 = ctrl[s + 1], p3 = ctrl[Mathf.Min(ctrl.Count - 1, s + 2)];
                int n = Mathf.CeilToInt(Vector2.Distance(p1, p2) / 10f);
                for (int k = 0; k < n; k++)
                {
                    float t = k / (float)n;
                    dense.Add(0.5f * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t));
                }
            }
            dense.Add(ctrl[ctrl.Count - 1]);
            float along = 0;
            for (int k = 0; k < dense.Count; k++)
            {
                if (k > 0) along += Vector2.Distance(dense[k - 1], dense[k]);
                Vector2 dir = (dense[Mathf.Min(k + 1, dense.Count - 1)] - dense[Mathf.Max(k - 1, 0)]).normalized;
                Vector2 across = V(dir.y, -dir.x);
                float z = dense[k].y;
                float farm = Step(-2600f, -4200f, z);   // 1 on the farmland
                float meander = Mathf.Lerp(45f, 150f, farm) * Mathf.Sin(along / Mathf.Lerp(380f, 700f, farm));
                if (z < -2700f && z > -3100f || z > -900f && z < -500f) meander *= 0.2f;   // straight into and out of the lake
                river.Add(new RiverPoint
                {
                    p = dense[k] + across * meander,
                    surface = MainFloorAt(z) - 7f,
                    half = Mathf.Lerp(14f, Mathf.Lerp(26f, 40f, Mathf.InverseLerp(-3700f, -9000f, z)), farm),
                });
            }
            Debug.Log($"OODA: the river is {along / 1000f:F1} km from the north end of the pass to the sea.");
            return river;
        }

        /// <summary>Cut the river's channel: its bed 3 m under the surface, the banks rising 1 m in 7 out to the ground beside it.</summary>
        static void CarveRiver(float[,] h, bool[,] guard)
        {
            var dist = new float[Res, Res];
            var surf = new float[Res, Res];
            var half = new float[Res, Res];
            for (int j = 0; j < Res; j++) for (int i = 0; i < Res; i++) dist[j, i] = float.MaxValue;
            riverNear = new bool[Res, Res];
            var r = River();
            for (int k = 0; k < r.Count; k += 2)
            {
                var rp = r[k];
                float reach = rp.half + 80f;
                int ci = Idx(rp.p.x), cj = Idx(rp.p.y), rc = Mathf.CeilToInt(reach / Cell) + 1;
                for (int j = Mathf.Max(0, cj - rc); j <= Mathf.Min(Res - 1, cj + rc); j++)
                    for (int i = Mathf.Max(0, ci - rc); i <= Mathf.Min(Res - 1, ci + rc); i++)
                    {
                        float d = Vector2.Distance(V(X(i), X(j)), rp.p);
                        if (d < dist[j, i]) { dist[j, i] = d; surf[j, i] = rp.surface; half[j, i] = rp.half; }
                    }
            }
            for (int j = 0; j < Res; j++)
                for (int i = 0; i < Res; i++)
                {
                    float d = dist[j, i];
                    if (d == float.MaxValue) continue;
                    if (d < half[j, i] + 80f) guard[j, i] = true;
                    if (d < half[j, i] + 25f) riverNear[j, i] = true;
                    float target = d < half[j, i] ? surf[j, i] - 3f : surf[j, i] - 3f + (d - half[j, i]) / 7f * 1.5f;
                    h[j, i] = Mathf.Min(h[j, i], target);
                }
        }

        // ---------- the town: the Town Strike's target, on the farmland ----------
        // A flat basin opened out of the fields east of the river, with the factory, streets of houses round it, and a runway
        // strip on its west side, its take-off end to the south so the fighters climb out north. The railway starts at its
        // station just east of it. Three radar sites stand on high ground round the island.

        public static readonly Vector2 TownCentre = V(3400, -5000);
        static readonly Vector2 TownRadii = V(1150, 1450);           // the flat basin, an ellipse
        public static readonly Vector2 FactoryAt = V(3800, -4950);
        public static readonly Vector2 RunwayStart = V(2600, -5700), RunwayEnd = V(2600, -4500);   // 1.2 km, take-off to the north
        const float TownFloor = 90f;
        /// <summary>Where to look for each radar site: the highest ground within RadarSearch of each that is no higher than RadarMaxY.</summary>
        static readonly Vector2[] RadarNear = { V(-5600, -3600), V(6900, -1400), V(-1700, 2700) };
        const float RadarSearch = 1800f, RadarMaxY = 2600f, RadarMinY = 300f;

        static float TownR(float x, float z)
        {
            float ex = (x - TownCentre.x) / TownRadii.x, ez = (z - TownCentre.y) / TownRadii.y;
            return Mathf.Sqrt(ex * ex + ez * ez);
        }

        /// <summary>In the town's basin or just round it: keep the balloons, the cable cars, the masts and the farmhouses out.</summary>
        static bool InTown(float x, float z) => TownR(x, z) < 1.25f;

        /// <summary>Open the basin: flat floor inside the ellipse, easing to the ground round it; and keep the mountain models out.</summary>
        static void CarveTown(float[,] h, bool[,] guard)
        {
            for (int j = 0; j < Res; j++)
                for (int i = 0; i < Res; i++)
                {
                    float r = TownR(X(i), X(j));
                    if (r < 1.6f) guard[j, i] = true;
                    if (r >= 1.5f) continue;
                    float t = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1f, 1.5f, r));
                    h[j, i] = Mathf.Lerp(TownFloor, h[j, i], t);   // level both ways: low ground round it is raised to the floor too
                }
        }

        // ---------- the land's shape ----------

        static float Ridged(float x, float z)
        {
            // warp the domain so the ridges wander, then a ridged multifractal: sharp crests, weighted by the octave below
            float wx = x + 1100f * (Mathf.PerlinNoise(x * 0.00011f + 3.1f, z * 0.00011f + 7.3f) - 0.5f) * 2f;
            float wz = z + 1100f * (Mathf.PerlinNoise(x * 0.00011f + 11.7f, z * 0.00011f + 1.9f) - 0.5f) * 2f;
            float sum = 0, amp = 1, freq = 1f / 4200f, norm = 0, weight = 1;
            for (int o = 0; o < 6; o++)
            {
                float n = 1 - Mathf.Abs(2 * Mathf.PerlinNoise(wx * freq + 17.3f * o + 5.2f, wz * freq + 9.1f * o + 3.4f) - 1);
                n *= n;
                sum += n * amp * weight;
                norm += amp;
                weight = Mathf.Clamp01(n * 1.8f);
                amp *= 0.5f;
                freq *= 2.03f;
            }
            return sum / norm;
        }

        /// <summary>The ground before the valleys and the models: rolling farmland in the south (30 to 300 m), wooded hills in the
        /// middle (squashed above 1.2 km: the mountain models are its peaks), and higher, rockier ground in the north (squashed above
        /// 1.5 km: there too the mountain models are the peaks, crowded together).</summary>
        static float Mountains(float x, float z)
        {
            float massif = Mathf.Lerp(0.7f, 1.15f, Mathf.PerlinNoise(x / 9000f + 41.1f, z / 9000f + 13.7f));
            float r = Mathf.InverseLerp(0.15f, 0.95f, Ridged(x, z));
            float mid = Mathf.Min(4400f, 250f + 2600f * massif * Mathf.Pow(r, 1.6f));
            mid = mid < 1200f ? mid : 1200f + (mid - 1200f) * 0.3f;
            float north = Mathf.Min(4400f, 350f + 2100f * massif * Mathf.Pow(r, 1.3f));
            north = north < 1500f ? north : 1500f + (north - 1500f) * 0.35f;
            // the farmland: two octaves of gentle swell and a few low ridges
            float swell = Mathf.PerlinNoise(x / 3400f + 71.3f, z / 3400f + 2.9f);
            float farm = 30f + 190f * swell * swell + 45f * Mathf.PerlinNoise(x / 900f + 5.5f, z / 900f + 61.2f) + 220f * Mathf.Pow(r, 3f);
            float s = SouthOf(x, z), n = NorthOf(x, z);
            return Mathf.Lerp(Mathf.Lerp(mid, farm, s), north, n);
        }

        static float[,] Heights(List<Vector3> rail)
        {
            var h = new float[Res, Res];
            for (int j = 0; j < Res; j++)
                for (int i = 0; i < Res; i++)
                    h[j, i] = Mountains(X(i), X(j));

            var dist = new float[Res, Res];
            var floor = new float[Res, Res];
            var guard = new bool[Res, Res];         // in or beside a valley: the mountain models must stay under the ground here
            foreach (var v in MakeValleys())
            {
                for (int j = 0; j < Res; j++) for (int i = 0; i < Res; i++) dist[j, i] = float.MaxValue;
                float reach = v.halfFloor + v.wall;
                float shoulder = v.minWall > 0 ? 700f : 0f;    // how far past the wall the raised land eases back down
                int rc = Mathf.CeilToInt((reach + shoulder) / Cell) + 1;
                float step = v.gorge ? 15f : 40f;
                for (int s = 0; s + 1 < v.pts.Length; s++)
                {
                    Vector2 a = v.pts[s], b = v.pts[s + 1];
                    int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / step));
                    for (int k = 0; k <= n; k++)
                    {
                        float u = k / (float)n;
                        Vector2 p = Vector2.Lerp(a, b, u);
                        float f = Mathf.Lerp(v.floor[s], v.floor[s + 1], u);
                        int ci = Idx(p.x), cj = Idx(p.y);
                        for (int jj = Mathf.Max(0, cj - rc); jj <= Mathf.Min(Res - 1, cj + rc); jj++)
                            for (int ii = Mathf.Max(0, ci - rc); ii <= Mathf.Min(Res - 1, ci + rc); ii++)
                            {
                                float dx = X(ii) - p.x, dz = X(jj) - p.y;
                                float d = dx * dx + dz * dz;
                                if (d < dist[jj, ii]) { dist[jj, ii] = d; floor[jj, ii] = f; }
                            }
                    }
                }
                for (int j = 0; j < Res; j++)
                    for (int i = 0; i < Res; i++)
                    {
                        if (dist[j, i] == float.MaxValue) continue;
                        float d = Mathf.Sqrt(dist[j, i]);
                        if (d < (v.guard > 0 ? v.guard : reach + ValleyGuard)) guard[j, i] = true;
                        if (d >= reach + shoulder) continue;
                        if (v.minWall > 0)
                            h[j, i] = Mathf.Max(h[j, i], floor[j, i] + v.minWall * (0.8f + 0.7f * Ridged(X(i) * 1.7f + 900, X(j) * 1.7f)) * (1 - Step(reach, reach + shoulder, d)));   // not a flat terrace: crests along the rim
                        if (d >= reach) continue;
                        float t = Mathf.Clamp01((d - v.halfFloor) / v.wall);
                        float shape = v.gorge ? Mathf.Pow(t, 0.5f) : t * t * (3 - 2 * t);
                        float bump = v.bumps * 2f * (Mathf.PerlinNoise(X(i) * 0.002f + 5, X(j) * 0.002f + 9) - 0.5f);
                        if (v.bumps > 20f) bump += v.bumps * (Mathf.PerlinNoise(X(i) * 0.0007f + 15, X(j) * 0.0007f + 3) - 0.5f);   // and broad swells across a wide floor
                        float f = floor[j, i] + bump;
                        float carved = f + (h[j, i] - f) * shape;
                        h[j, i] = Mathf.Min(h[j, i], carved);
                    }
            }

            CarveTown(h, guard);
            CarveRiver(h, guard);
            GuardArenas(guard);
            PlaceMountains(h, guard, rail);
            CarveRail(h, rail);

            // a lake sits a little under its lowest shore, so the water never stands above the ground round it
            for (int k = 0; k < Lakes.Length; k++)
            {
                float shore = float.MaxValue;
                for (int a = 0; a < 64; a++)
                {
                    float t = a * Mathf.PI / 32;
                    shore = Mathf.Min(shore, SampleArr(h, Lakes[k].c.x + Mathf.Cos(t) * Lakes[k].r.x * 1.15f, Lakes[k].c.y + Mathf.Sin(t) * Lakes[k].r.y * 1.15f));
                }
                Lakes[k].level = Mathf.Min(Lakes[k].level, shore - 3f);
            }

            foreach (var l in Lakes)
                for (int j = 0; j < Res; j++)
                    for (int i = 0; i < Res; i++)
                    {
                        float ex = (X(i) - l.c.x) / l.r.x, ez = (X(j) - l.c.y) / l.r.y;
                        float r = Mathf.Sqrt(ex * ex + ez * ez);
                        if (r > 1.6f) continue;
                        float target = r < 1 ? l.level - 25f * (1 - r * r) : l.level + (r - 1) * 500f;
                        h[j, i] = Mathf.Min(h[j, i], target);
                    }

            // the island: down to the sea floor at the edges
            for (int j = 0; j < Res; j++)
                for (int i = 0; i < Res; i++)
                {
                    float d = Mathf.Max(Mathf.Abs(X(i)), Mathf.Abs(X(j))) / (Size / 2);
                    d += 0.04f * (Mathf.PerlinNoise(X(i) * 0.0004f + 2, X(j) * 0.0004f + 6) - 0.5f);
                    float e = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.84f, 0.98f, d));
                    h[j, i] = Mathf.Lerp(h[j, i], Base + 20f, e);
                }
            return h;
        }

        /// <summary>Keep the mountain models down along each arena's start line, out to its half width either side.</summary>
        static void GuardArenas(bool[,] guard)
        {
            foreach (var a in Arenas.All)
            {
                Vector2 p0 = ArenaAt(a, -(float)a.halfLengthM), p1 = ArenaAt(a, (float)a.halfLengthM);
                float w = (float)a.halfWidthM;
                for (int j = 0; j < Res; j++)
                    for (int i = 0; i < Res; i++)
                        if (SegDist2(V(X(i), X(j)), p0, p1) < w) guard[j, i] = true;
            }
        }

        static float SegDist2(Vector2 q, Vector2 a, Vector2 b)
        {
            float u = Mathf.Clamp01(Vector2.Dot(q - a, b - a) / (b - a).sqrMagnitude);
            return Vector2.Distance(q, Vector2.Lerp(a, b, u));
        }

        static float X(int i) => -Size / 2 + i * Cell;
        static int Idx(float x) => Mathf.Clamp(Mathf.RoundToInt((x + Size / 2) / Cell), 0, Res - 1);

        static float SampleArr(float[,] h, float x, float z)
        {
            float fx = Mathf.Clamp((x + Size / 2) / Cell, 0, Res - 1.001f), fz = Mathf.Clamp((z + Size / 2) / Cell, 0, Res - 1.001f);
            int i = (int)fx, j = (int)fz;
            float u = fx - i, w = fz - j;
            return Mathf.Lerp(Mathf.Lerp(h[j, i], h[j, i + 1], u), Mathf.Lerp(h[j + 1, i], h[j + 1, i + 1], u), w);
        }

        static float Step(float a, float b, float x) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, x));

        static float Slope(float[,] h, float x, float z)
        {
            float d = Cell;
            float gx = (SampleArr(h, x + d, z) - SampleArr(h, x - d, z)) / (2 * d);
            float gz = (SampleArr(h, x, z + d) - SampleArr(h, x, z - d)) / (2 * d);
            return Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
        }

        static float Rand(System.Random rnd, float a, float b) => a + (float)rnd.NextDouble() * (b - a);

        // ---------- along the main valley ----------

        /// <summary>How far it is along the main valley's line from its south end to its point upTo (to its north end by default), metres on the map.</summary>
        static float MainLength(int upTo = -1)
        {
            int last = upTo < 0 ? MainLine.Length - 1 : Mathf.Min(upTo, MainLine.Length - 1);
            float total = 0;
            for (int s = 0; s < last; s++) total += Vector2.Distance(MainLine[s], MainLine[s + 1]);
            return total;
        }

        /// <summary>The point dist metres along the main valley's line from its south end, and the line's direction there.</summary>
        static Vector2 AlongMain(float dist, out Vector2 dir)
        {
            for (int s = 0; s + 1 < MainLine.Length; s++)
            {
                float len = Vector2.Distance(MainLine[s], MainLine[s + 1]);
                dir = (MainLine[s + 1] - MainLine[s]).normalized;
                if (dist <= len || s + 2 == MainLine.Length) return Vector2.Lerp(MainLine[s], MainLine[s + 1], Mathf.Clamp01(dist / len));
                dist -= len;
            }
            dir = V(0, 1);
            return MainLine[0];
        }

        /// <summary>How far a point is from the escort route on the map, and the route's height at the nearest point.</summary>
        static float RouteGap(Vector3 p, out float routeY)
        {
            float best = float.MaxValue;
            routeY = 0;
            var q = V(p.x, p.z);
            for (int i = 0; i + 1 < EscortRoute.Length; i++)
            {
                Vector3 a = EscortRoute[i], b = EscortRoute[i + 1];
                Vector2 a2 = V(a.x, a.z), b2 = V(b.x, b.z);
                float u = Mathf.Clamp01(Vector2.Dot(q - a2, b2 - a2) / (b2 - a2).sqrMagnitude);
                float d = Vector2.Distance(q, Vector2.Lerp(a2, b2, u));
                if (d < best) { best = d; routeY = Mathf.Lerp(a.y, b.y, u); }
            }
            return best;
        }
    }
}
