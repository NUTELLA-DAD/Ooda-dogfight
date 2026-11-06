using System.Collections.Generic;
using System.Linq;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.EditorTools
{
    /// <summary>
    /// Where the models the island is dressed with go (AlpineModels makes them). The snowy mountain is the big peaks: copies at
    /// several sizes, each turned, stretched and sometimes mirrored, crowding the north, standing in a ring round the Peak Maze,
    /// lining the main pass and rising out of the wooded middle, overlapping into massifs. The grass hills are the foothills
    /// round their feet and the green swells of the farmland. The three trees (round, tall and pine) replace
    /// the old cone pines. The ground under each mountain and hill is raised to just under its surface, so the battle's ground
    /// check, the AI, the clouds and the trees all see them. Nothing of a copy may stand above the ground near a valley (see
    /// Valley.guard), on the railway or round the basin where the dogfight starts: a copy that would has to sink until it
    /// is under the ground there, and one that would have to sink too far is not placed.
    /// </summary>
    internal static partial class AlpineWorld
    {
        const float ValleyGuard = 150f, RailGuard = 350f;
        const int Hf = 512;                        // a model's own height grid

        /// <summary>A model from Hi3D, made light enough to use many times: a near and a far mesh made from the FBX with
        /// Unity's mesh LOD generator and kept as OBJ (the FBX itself, 50 to 100 MB, is not committed).</summary>
        class LandModel
        {
            public string name, dir; public int nearTris, farTris;
            public bool byHeight; public float size;  // its frame: size metres across its longer side, or tall
            public Mesh near, far; public float fit; public Vector3 centre; public float[,] hf; public float top; public Material mat;
            public string Fbx => $"{dir}/{name}.fbx";
            public string NearObj => $"{dir}/{name}_Near.obj";
            public string FarObj => $"{dir}/{name}_Far.obj";
            public string Tex => $"{dir}/{name}_BaseColor.jpg";
        }

        static readonly LandModel MountainModel = new LandModel { name = "Mountain", dir = "Assets/OODA/Art/Mountain", nearTris = 60000, farTris = 8000, size = 1000 };
        static readonly LandModel FoothillModel = new LandModel { name = "Foothill", dir = "Assets/OODA/Art/Foothill", nearTris = 30000, farTris = 5000, size = 1000 };
        static readonly LandModel RoundTree = new LandModel { name = "RoundTree", dir = "Assets/OODA/Art/Tree", nearTris = 2000, farTris = 250, byHeight = true, size = 12 };
        static readonly LandModel TallTree = new LandModel { name = "TallTree", dir = "Assets/OODA/Art/Tree", nearTris = 2000, farTris = 250, byHeight = true, size = 15 };
        static readonly LandModel PineTree = new LandModel { name = "PineTree", dir = "Assets/OODA/Art/Tree", nearTris = 2000, farTris = 250, byHeight = true, size = 16 };
        // the Hi3D alpine chalet: its OBJs are made in Blender (Tools~/ArtPipeline/chalet.py), so it has no FBX to fall back on
        static readonly LandModel ChaletModel = new LandModel { name = "Chalet", dir = "Assets/OODA/Art/Chalet", nearTris = 12000, farTris = 1500, size = 14 };

        struct Peak { public LandModel model; public string kind; public Vector3 pos; public float yaw, width; public Vector3 scale; }
        static readonly List<Peak> Peaks = new List<Peak>();
        static bool[,] underModel;
        /// <summary>The highest model surface over each ground cell (float.MinValue where there is none): the trees stand on it.</summary>
        static float[,] modelTop;

        static bool UnderMountain(float x, float z) => underModel != null && underModel[Idx(z), Idx(x)];

        /// <summary>The model's top surface on a grid over its frame (x and z from -size/2 to size/2), -1 where it isn't.</summary>
        static float[,] ModelHeights(Mesh mesh, float fit, Vector3 centre)
        {
            var hf = new float[Hf, Hf];
            for (int j = 0; j < Hf; j++) for (int i = 0; i < Hf; i++) hf[j, i] = -1;
            var v = mesh.vertices.Select(p => (p - centre) * fit).ToArray();
            var t = mesh.triangles;
            const float W = 1000f, g = W / Hf;
            float G(float x) => (x + W / 2) / g - 0.5f;
            for (int k = 0; k < t.Length; k += 3)
            {
                Vector3 a = v[t[k]], b = v[t[k + 1]], c = v[t[k + 2]];
                float det = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(det) < 1e-6f) continue;
                int i0 = Mathf.Max(0, Mathf.CeilToInt(G(Mathf.Min(a.x, b.x, c.x)))), i1 = Mathf.Min(Hf - 1, Mathf.FloorToInt(G(Mathf.Max(a.x, b.x, c.x))));
                int j0 = Mathf.Max(0, Mathf.CeilToInt(G(Mathf.Min(a.z, b.z, c.z)))), j1 = Mathf.Min(Hf - 1, Mathf.FloorToInt(G(Mathf.Max(a.z, b.z, c.z))));
                for (int j = j0; j <= j1; j++)
                    for (int i = i0; i <= i1; i++)
                    {
                        float x = -W / 2 + (i + 0.5f) * g, z = -W / 2 + (j + 0.5f) * g;
                        float l1 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / det;
                        float l2 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / det;
                        float l3 = 1 - l1 - l2;
                        if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) continue;
                        hf[j, i] = Mathf.Max(hf[j, i], l1 * a.y + l2 * b.y + l3 * c.y);
                    }
            }
            return hf;
        }

        static float ModelAt(float[,] hf, float x, float z)
        {
            int i = Mathf.FloorToInt((x + 500f) / 1000f * Hf), j = Mathf.FloorToInt((z + 500f) / 1000f * Hf);
            return i < 0 || j < 0 || i >= Hf || j >= Hf ? -1 : hf[j, i];
        }

        // ---------- where they go ----------

        struct Kind
        {
            public LandModel model; public string name; public int want, tries; public float lo, hi;
            public float squat, tall;                  // the height stretch's range
            public float rise, summit, maxGround;      // at least rise over the ground there; summit and the ground at its middle at most this
            public bool besidePass;                    // put beside the main pass, so the bombers weave between them
            public bool ring;                          // in a ring round the Peak Maze's bowl
            public float zMin, zMax;                   // only this far north and south (0 and 0: anywhere)
        }

        static void PlaceMountains(float[,] h, bool[,] guard, List<Vector3> rail)
        {
            Peaks.Clear();
            underModel = new bool[Res, Res];
            modelTop = new float[Res, Res];
            for (int j = 0; j < Res; j++) for (int i = 0; i < Res; i++) modelTop[j, i] = float.MinValue;
            Load(MountainModel, true);
            Load(FoothillModel, true);
            if (MountainModel.near == null) { Debug.LogWarning("OODA: no mountain mesh: the land is noise only."); return; }

            // more places they must not show: the railway, and near the sea
            int rr = Mathf.CeilToInt(RailGuard / Cell) + 1;
            for (int k = 0; k < rail.Count; k += 4)
            {
                int ci = Idx(rail[k].x), cj = Idx(rail[k].z);
                for (int j = Mathf.Max(0, cj - rr); j <= Mathf.Min(Res - 1, cj + rr); j++)
                    for (int i = Mathf.Max(0, ci - rr); i <= Mathf.Min(Res - 1, ci + rr); i++)
                        if (Vector2.Distance(V(X(i), X(j)), V(rail[k].x, rail[k].z)) < RailGuard) guard[j, i] = true;
            }
            for (int j = 0; j < Res; j++)
                for (int i = 0; i < Res; i++)
                    if (Mathf.Max(Mathf.Abs(X(i)), Mathf.Abs(X(j))) > 8000f) guard[j, i] = true;

            // the biggest first, so the smaller ones fill in round them
            var kinds = new[]
            {
                new Kind { model = MountainModel, name = "great", want = 10, tries = 5000, lo = 4500, hi = 7500, squat = 1.0f, tall = 1.6f, rise = 400, summit = 6400, maxGround = 9999, zMin = 3000 },
                new Kind { model = MountainModel, name = "maze", want = 10, tries = 4000, lo = 2600, hi = 5000, squat = 1.0f, tall = 1.6f, rise = 600, summit = 6400, maxGround = 9999, ring = true },
                new Kind { model = MountainModel, name = "pass", want = 14, tries = 4000, lo = 3000, hi = 6000, squat = 0.9f, tall = 1.4f, rise = 350, summit = 6400, maxGround = 9999, besidePass = true },
                new Kind { model = MountainModel, name = "north", want = 24, tries = 9000, lo = 2800, hi = 5000, squat = 0.9f, tall = 1.5f, rise = 350, summit = 6400, maxGround = 9999, zMin = 2000 },
                new Kind { model = MountainModel, name = "middle", want = 9, tries = 3000, lo = 4500, hi = 7000, squat = 0.8f, tall = 1.25f, rise = 350, summit = 5200, maxGround = 9999, zMin = -2800, zMax = 2600 },
                new Kind { model = MountainModel, name = "small", want = 40, tries = 9000, lo = 2200, hi = 4000, squat = 0.85f, tall = 1.4f, rise = 350, summit = 6400, maxGround = 9999, zMin = -3600 },
                // the grass hills: round the mountains' feet in the middle, and the swells of the farmland
                new Kind { model = FoothillModel, name = "foothill", want = 50, tries = 8000, lo = 900, hi = 3200, squat = 0.7f, tall = 1.6f, rise = 50, summit = 2300, maxGround = 1300, zMin = -4200, zMax = 3000 },
                new Kind { model = FoothillModel, name = "farm hill", want = 30, tries = 6000, lo = 700, hi = 2200, squat = 0.45f, tall = 0.9f, rise = 30, summit = 700, maxGround = 400, zMax = -3200 },
            };
            var rnd = new System.Random(2026);
            float start = MainLength(NarrowFrom), total = MainLength();
            var maze = PeakMaze;
            foreach (var k in kinds)
            {
                if (k.model.near == null) continue;
                int made = 0;
                whyNot.Clear();
                for (int t = 0; t < k.tries && made < k.want; t++)
                {
                    float w = Rand(rnd, k.lo, k.hi);
                    var pos = V(Rand(rnd, -7800f, 7800f), Rand(rnd, -7800f, 7800f));
                    if (k.besidePass)
                    {
                        Vector2 c = AlongMain(Rand(rnd, start, total - 300f), out Vector2 dir);
                        pos = c + V(dir.y, -dir.x) * (rnd.Next(2) == 0 ? -1 : 1) * (Rand(rnd, 0.3f, 0.5f) * w + 450f);
                    }
                    else if (k.ring)
                    {
                        // round the bowl: either side of its axis and past its ends, their middles 1.4 to 2.6 km from its line
                        float along = Rand(rnd, -(float)maze.halfLengthM * 1.1f, (float)maze.halfLengthM * 1.1f);
                        Vector2 d = ArenaDir(maze), across = V(d.y, -d.x);
                        pos = ArenaAt(maze, along) + across * (rnd.Next(2) == 0 ? -1 : 1) * Rand(rnd, 1400f, 2600f);
                    }
                    else if ((k.zMin != 0 && pos.y < k.zMin) || (k.zMax != 0 && pos.y > k.zMax)) continue;
                    float s = w / k.model.size;
                    var p = new Peak
                    {
                        model = k.model, kind = k.name, width = w, yaw = Rand(rnd, 0f, 360f), pos = new Vector3(pos.x, 0, pos.y),
                        // stretched each way, taller or squatter, and every other one mirrored
                        scale = new Vector3(s * Rand(rnd, 0.85f, 1.2f) * (rnd.Next(2) == 0 ? -1 : 1), s * Rand(rnd, k.squat, k.tall), s * Rand(rnd, 0.85f, 1.2f)),
                    };
                    if (Mathf.Abs(p.pos.x) > 8200f || Mathf.Abs(p.pos.z) > 8200f) continue;
                    if (guard[Idx(p.pos.z), Idx(p.pos.x)]) { Why("its middle in a valley"); continue; }
                    if (h[Idx(p.pos.z), Idx(p.pos.x)] > k.maxGround) { Why("ground too high"); continue; }
                    if (Peaks.Any(q => q.model == k.model && Vector2.Distance(V(q.pos.x, q.pos.z), V(p.pos.x, p.pos.z)) < 0.25f * (q.width + w) / 2)) { Why("on another"); continue; }
                    if (!FitPeak(h, guard, k, ref p, 3) || !FitPeak(h, guard, k, ref p, 1)) continue;
                    Stamp(h, guard, p);
                    Peaks.Add(p);
                    made++;
                }
                Debug.Log($"OODA: {made} of {k.want} {k.name} {k.model.name.ToLower()}s found room" + (made < k.want ? " (the others: " + string.Join(", ", whyNot.Select(w => $"{w.Key} {w.Value}")) + ")." : "."));
            }
            Debug.Log($"OODA: {Peaks.Count} land models: " + string.Join(", ", Peaks.Select(q => $"{q.kind} ({q.pos.x:F0}, {q.pos.z:F0}) {q.width:F0} m")) + ".");
        }

        /// <summary>Visits the ground cells under a copy (every stride-th), with the model's height over its base there.</summary>
        static IEnumerable<(int i, int j, float rel)> Footprint(Peak p, int stride)
        {
            var inv = Quaternion.Euler(0, -p.yaw, 0);
            float rad = 0.72f * p.model.size * Mathf.Max(Mathf.Abs(p.scale.x), Mathf.Abs(p.scale.z));
            float unit = 1000f / p.model.size;
            int ci = Idx(p.pos.x), cj = Idx(p.pos.z), rc = Mathf.CeilToInt(rad / Cell) + 1;
            for (int j = Mathf.Max(0, cj - rc); j <= Mathf.Min(Res - 1, cj + rc); j += stride)
                for (int i = Mathf.Max(0, ci - rc); i <= Mathf.Min(Res - 1, ci + rc); i += stride)
                {
                    var d = inv * new Vector3(X(i) - p.pos.x, 0, X(j) - p.pos.z);
                    float m = ModelAt(p.model.hf, d.x / p.scale.x * unit, d.z / p.scale.z * unit);
                    if (m > 0.5f) yield return (i, j, m * p.scale.y);
                }
        }

        /// <summary>Sets the copy's base: on the lower ground round it, then down as far as it must go to stay under the ground
        /// in the guarded cells. False if that sinks it more than 40 %, takes it over the kind's summit, or leaves it hardly
        /// higher than the ground already there.</summary>
        static bool FitPeak(float[,] h, bool[,] guard, Kind k, ref Peak p, int stride)
        {
            var ground = new List<float>();
            float limit = float.MaxValue, peak = 0, underPeak = 0;
            foreach (var (i, j, rel) in Footprint(p, stride))
            {
                ground.Add(h[j, i]);
                if (guard[j, i]) limit = Mathf.Min(limit, h[j, i] - 15f - rel);
                if (rel > peak) { peak = rel; underPeak = h[j, i]; }
            }
            if (ground.Count < 20) return Why("off the land");
            ground.Sort();
            float natural = ground[ground.Count / 10] - 20f;
            float b = Mathf.Min(natural, limit);
            if (natural - b > peak * 0.4f) return Why("would sink too far");
            if (b + peak > k.summit) return Why("too tall");
            if (b + peak - underPeak < k.rise) return Why("too low");
            p.pos.y = b;
            return true;
        }

        static readonly Dictionary<string, int> whyNot = new Dictionary<string, int>();
        static bool Why(string reason) { whyNot[reason] = whyNot.TryGetValue(reason, out int n) ? n + 1 : 1; return false; }

        static void Stamp(float[,] h, bool[,] guard, Peak p)
        {
            foreach (var (i, j, rel) in Footprint(p, 1))
            {
                float surf = p.pos.y + rel;
                if (surf > h[j, i] + 3f) underModel[j, i] = true;
                modelTop[j, i] = Mathf.Max(modelTop[j, i], surf);
                if (!guard[j, i]) h[j, i] = Mathf.Max(h[j, i], surf - 6f);   // just under the model: it is what you see
            }
        }

    }
}
