using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ooda.EditorTools
{
    /// <summary>
    /// The models the Alps are dressed with. The snowy mountain is the big peaks: copies at four sizes, each turned,
    /// stretched and sometimes mirrored, on the land between the valleys and crowding in beside the main pass, overlapping
    /// into massifs. The grass hills are the foothills round their feet and on the low ground. The two trees replace the
    /// old cone pines. The ground under each mountain and hill is raised to just under its surface, so the battle's ground
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

        struct Peak { public LandModel model; public string kind; public Vector3 pos; public float yaw, width; public Vector3 scale; }
        static readonly List<Peak> Peaks = new List<Peak>();
        static bool[,] underModel;

        static bool UnderMountain(float x, float z) => underModel != null && underModel[Idx(z), Idx(x)];

        // ---------- the meshes ----------

        /// <summary>The model's meshes, material, and (for land) its height grid; the OBJs are made first if they are missing.</summary>
        static void Load(LandModel m, bool heights)
        {
            if (!File.Exists(m.NearObj) || !File.Exists(m.FarObj)) PrepareObjs(m);
            m.near = AssetDatabase.LoadAllAssetsAtPath(m.NearObj).OfType<Mesh>().FirstOrDefault();
            m.far = AssetDatabase.LoadAllAssetsAtPath(m.FarObj).OfType<Mesh>().FirstOrDefault() ?? m.near;
            if (m.near == null) return;
            var b = m.near.bounds;
            m.fit = m.byHeight ? m.size / b.size.y : m.size / Mathf.Max(b.size.x, b.size.z);
            m.centre = new Vector3(b.center.x, b.min.y, b.center.z);
            var ti = (TextureImporter)AssetImporter.GetAtPath(m.Tex);
            int max = m.byHeight ? 2048 : 4096;
            if (ti != null && ti.maxTextureSize != max) { ti.maxTextureSize = max; ti.SaveAndReimport(); }
            m.mat = TestWorldBuilder.Mat(m.name, Color.white, 0.12f);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(m.Tex);
            m.mat.SetTexture("_BaseMap", tex);
            m.mat.SetTexture("_MainTex", tex);
            if (!heights) return;
            m.hf = ModelHeights(m.near, m.fit, m.centre);
            m.top = 0; int cover = 0;
            foreach (var y in m.hf) { m.top = Mathf.Max(m.top, y); if (y > 0) cover++; }
            Debug.Log($"OODA: the {m.name} model is {m.size:F0} m across and {m.top:F0} m tall in its frame; it covers {cover * 100f / (Hf * Hf):F0} % of its square.");
        }

        /// <summary>Two LODs of the FBX as OBJs, in metres: centred, base at 0, size across (or tall, for trees).</summary>
        static void PrepareObjs(LandModel m)
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(m.Fbx);
            if (imp == null) { Debug.LogError($"OODA: no {m.NearObj} and no {m.Fbx} to make it from."); return; }
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.animationType = ModelImporterAnimationType.None;
            imp.importAnimation = false;
            imp.isReadable = true;
            imp.SaveAndReimport();
            // the base colour is embedded in the FBX: take it out next to the OBJs
            if (!File.Exists(m.Tex))
            {
                string tmp = $"{m.dir}/Extracted";
                imp.ExtractTextures(tmp);
                AssetDatabase.Refresh();
                File.Copy($"{tmp}/0.jpg", m.Tex);
                AssetDatabase.DeleteAsset(tmp);
                AssetDatabase.ImportAsset(m.Tex);
            }
            var mesh = Object.Instantiate(AssetDatabase.LoadAllAssetsAtPath(m.Fbx).OfType<Mesh>().First());
            MeshLodUtility.GenerateMeshLods(mesh, 14);
            var v = mesh.vertices; var n = mesh.normals; var uv = mesh.uv;
            Vector3 lo = v[0], hi = v[0];
            foreach (var p in v) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
            float k = m.byHeight ? m.size / (hi.y - lo.y) : m.size / Mathf.Max(hi.x - lo.x, hi.z - lo.z);
            var off = new Vector3(-(lo.x + hi.x) / 2, -lo.y, -(lo.z + hi.z) / 2);

            int[] idx;
            using (var data = Mesh.AcquireReadOnlyMeshData(mesh))
            {
                var d = data[0];
                int bv = d.GetSubMesh(0).baseVertex;
                idx = d.indexFormat == IndexFormat.UInt32 ? d.GetIndexData<uint>().Select(x => (int)x + bv).ToArray() : d.GetIndexData<ushort>().Select(x => x + bv).ToArray();
            }
            var lods = Enumerable.Range(0, mesh.lodCount).Select(l => mesh.GetLod(0, l)).ToArray();
            Debug.Log($"OODA: {m.name} LODs (triangles): " + string.Join(", ", lods.Select(r => r.indexCount / 3)));
            MeshLodRange Pick(int tris) => lods.OrderBy(r => Mathf.Abs(Mathf.Log(r.indexCount / 3f / tris))).First();
            WriteObj(m.NearObj, m.name, v, n, uv, idx, Pick(m.nearTris), k, off);
            WriteObj(m.FarObj, m.name, v, n, uv, idx, Pick(m.farTris), k, off);
            Object.DestroyImmediate(mesh);
            AssetDatabase.ImportAsset(m.NearObj);
            AssetDatabase.ImportAsset(m.FarObj);
        }

        /// <summary>One LOD of the mesh as an OBJ. Unity's OBJ import mirrors x and turns the triangles round, so write it the other way.</summary>
        static void WriteObj(string path, string name, Vector3[] v, Vector3[] n, Vector2[] uv, int[] idx, MeshLodRange r, float k, Vector3 off)
        {
            var map = new Dictionary<int, int>();
            var sb = new StringBuilder();
            var ci = CultureInfo.InvariantCulture;
            var faces = new StringBuilder();
            int Use(int i)
            {
                if (map.TryGetValue(i, out int o)) return o;
                o = map.Count + 1;
                map[i] = o;
                var p = (v[i] + off) * k;
                sb.Append("v ").Append((-p.x).ToString("F3", ci)).Append(' ').Append(p.y.ToString("F3", ci)).Append(' ').Append(p.z.ToString("F3", ci)).Append('\n');
                sb.Append("vt ").Append(uv[i].x.ToString("F5", ci)).Append(' ').Append(uv[i].y.ToString("F5", ci)).Append('\n');
                sb.Append("vn ").Append((-n[i].x).ToString("F4", ci)).Append(' ').Append(n[i].y.ToString("F4", ci)).Append(' ').Append(n[i].z.ToString("F4", ci)).Append('\n');
                return o;
            }
            for (int t = (int)r.indexStart; t + 2 < r.indexStart + r.indexCount; t += 3)
            {
                int a = Use(idx[t]), b = Use(idx[t + 1]), c = Use(idx[t + 2]);
                faces.Append($"f {a}/{a}/{a} {c}/{c}/{c} {b}/{b}/{b}\n");
            }
            File.WriteAllText(path, $"# OODA: {name}, metres\no {name}\n" + sb + faces);
            Debug.Log($"OODA: wrote {path}: {map.Count} vertices, {r.indexCount / 3} triangles.");
        }

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
        }

        static void PlaceMountains(float[,] h, bool[,] guard)
        {
            Peaks.Clear();
            underModel = new bool[Res, Res];
            Load(MountainModel, true);
            Load(FoothillModel, true);
            if (MountainModel.near == null) { Debug.LogWarning("OODA: no mountain mesh: the Alps are noise only."); return; }

            // more places they must not show: the railway, round the basin where the dogfight starts, and near the sea
            var rail = RailLine();
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
                {
                    float x = X(i), z = X(j);
                    float basin = Vector2.Distance(V(x, z), V(0, Mathf.Clamp(z, -10600, -6000)));
                    if (basin < 1600f || Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) > 8000f) guard[j, i] = true;
                }

            var kinds = new[]
            {
                new Kind { model = MountainModel, name = "pass", want = 16, tries = 4000, lo = 3000, hi = 6000, squat = 0.9f, tall = 1.4f, rise = 350, summit = 6400, maxGround = 9999, besidePass = true },
                new Kind { model = MountainModel, name = "great", want = 7, tries = 3000, lo = 7000, hi = 11000, squat = 0.85f, tall = 1.35f, rise = 350, summit = 6400, maxGround = 9999 },
                new Kind { model = MountainModel, name = "big", want = 14, tries = 4000, lo = 4500, hi = 7000, squat = 0.85f, tall = 1.35f, rise = 350, summit = 6400, maxGround = 9999 },
                new Kind { model = MountainModel, name = "small", want = 30, tries = 5000, lo = 2400, hi = 4200, squat = 0.85f, tall = 1.35f, rise = 350, summit = 6400, maxGround = 9999 },
                // the grass hills on the low ground and round the mountains' feet
                new Kind { model = FoothillModel, name = "foothill", want = 60, tries = 8000, lo = 900, hi = 3200, squat = 0.7f, tall = 1.6f, rise = 50, summit = 2300, maxGround = 1300 },
            };
            var rnd = new System.Random(2026);
            float start = MainLength(3), total = MainLength();
            foreach (var k in kinds)
            {
                if (k.model.near == null) continue;
                int made = 0;
                for (int t = 0; t < k.tries && made < k.want; t++)
                {
                    float w = Rand(rnd, k.lo, k.hi);
                    var pos = V(Rand(rnd, -7800f, 7800f), Rand(rnd, -7800f, 7800f));
                    if (k.besidePass)
                    {
                        Vector2 c = AlongMain(Rand(rnd, start, total - 300f), out Vector2 dir);
                        pos = c + V(dir.y, -dir.x) * (rnd.Next(2) == 0 ? -1 : 1) * (Rand(rnd, 0.3f, 0.5f) * w + 450f);
                    }
                    float s = w / k.model.size;
                    var p = new Peak
                    {
                        model = k.model, kind = k.name, width = w, yaw = Rand(rnd, 0f, 360f), pos = new Vector3(pos.x, 0, pos.y),
                        // stretched each way, taller or squatter, and every other one mirrored
                        scale = new Vector3(s * Rand(rnd, 0.85f, 1.2f) * (rnd.Next(2) == 0 ? -1 : 1), s * Rand(rnd, k.squat, k.tall), s * Rand(rnd, 0.85f, 1.2f)),
                    };
                    if (guard[Idx(p.pos.z), Idx(p.pos.x)] || h[Idx(p.pos.z), Idx(p.pos.x)] > k.maxGround) continue;
                    if (Peaks.Any(q => q.model == k.model && Vector2.Distance(V(q.pos.x, q.pos.z), V(p.pos.x, p.pos.z)) < 0.25f * (q.width + w) / 2)) continue;
                    if (!FitPeak(h, guard, k, ref p, 3) || !FitPeak(h, guard, k, ref p, 1)) continue;
                    Stamp(h, guard, p);
                    Peaks.Add(p);
                    made++;
                }
                Debug.Log($"OODA: {made} of {k.want} {k.name} {k.model.name.ToLower()}s found room.");
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
        /// in the guarded cells. False if that sinks it more than a third, takes it over the kind's summit, or leaves it hardly
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
            if (ground.Count < 20) return false;
            ground.Sort();
            float natural = ground[ground.Count / 10] - 20f;
            float b = Mathf.Min(natural, limit);
            if (natural - b > peak / 3) return false;
            if (b + peak > k.summit || b + peak - underPeak < k.rise) return false;
            p.pos.y = b;
            return true;
        }

        static void Stamp(float[,] h, bool[,] guard, Peak p)
        {
            foreach (var (i, j, rel) in Footprint(p, 1))
            {
                float surf = p.pos.y + rel;
                if (surf > h[j, i] + 3f) underModel[j, i] = true;
                if (!guard[j, i]) h[j, i] = Mathf.Max(h[j, i], surf - 6f);   // just under the model: it is what you see
            }
        }

        // ---------- the models ----------

        static void MakeMountains()
        {
            if (Peaks.Count == 0) return;
            var root = new GameObject("Mountains").transform;
            var hills = new GameObject("Foothills").transform;
            for (int n = 0; n < Peaks.Count; n++)
            {
                var p = Peaks[n];
                var go = new GameObject($"{p.model.name} {n} ({p.kind}, {p.width:F0} m across)");
                go.transform.SetParent(p.model == FoothillModel ? hills : root, false);
                go.transform.SetPositionAndRotation(p.pos, Quaternion.Euler(0, p.yaw, 0));
                go.transform.localScale = p.scale;
                go.isStatic = true;
                AddLods(go, p.model, 0.15f, 0f);
            }
        }

        /// <summary>The near and far meshes under go, base at its origin, with an LODGroup between them.</summary>
        static void AddLods(GameObject go, LandModel m, float nearToFar, float cull)
        {
            Renderer Part(string name, Mesh mesh)
            {
                var c = new GameObject(name);
                c.transform.SetParent(go.transform, false);
                c.transform.localPosition = -m.centre * m.fit;
                c.transform.localScale = Vector3.one * m.fit;
                c.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = c.AddComponent<MeshRenderer>();
                r.sharedMaterial = m.mat;
                c.isStatic = go.isStatic;
                return r;
            }
            var lod = go.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(nearToFar, new[] { Part("Near", m.near) }), new LOD(cull, new[] { Part("Far", m.far) }) });
            lod.RecalculateBounds();
        }

        /// <summary>A tree model as a prefab for the terrain's trees: about size metres tall, its foot at the origin.</summary>
        static GameObject TreePrefab(LandModel m)
        {
            Load(m, false);
            if (m.near == null) return null;
            var go = new GameObject(m.name);
            AddLods(go, m, 0.05f, 0.003f);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{Dir}/{m.name}.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }
    }
}
