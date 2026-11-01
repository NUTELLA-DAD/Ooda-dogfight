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
    /// The snowy mountain model as the Alps' big peaks: copies of it at three sizes, each turned, stretched and sometimes
    /// mirrored, standing on the land between the valleys and overlapping into massifs. The ground under each copy is raised
    /// to just under its surface, so the battle's ground check, the AI, the clouds and the trees all see the mountains.
    /// Nothing of a copy may stand above the ground in or beside a valley (ValleyGuard metres past its walls), on the
    /// railway or round the basin where the dogfight starts: a copy that would has to sink until it is under the ground
    /// there, and one that would have to sink too far is not placed.
    /// </summary>
    internal static partial class AlpineWorld
    {
        const float ValleyGuard = 150f, RailGuard = 350f;
        const string MountainDir = "Assets/OODA/Art/Mountain";
        const string MountainFbx = MountainDir + "/Mountain.fbx";       // the 655k-triangle original, not committed
        const string NearObj = MountainDir + "/Mountain_Near.obj", FarObj = MountainDir + "/Mountain_Far.obj";
        const string MountainTex = MountainDir + "/Mountain_BaseColor.jpg";
        const float ModelWidth = 1000f;            // the prepared meshes: 1 km across its longer side, base at 0
        const int Hf = 512;                        // the model's own height grid, about 2 m a sample at 1 km
        const float MaxSummit = 6400f;

        struct Peak { public string kind; public Vector3 pos; public float yaw, width; public Vector3 scale; }
        static readonly List<Peak> Peaks = new List<Peak>();
        static bool[,] underMountain;

        static bool UnderMountain(float x, float z) => underMountain != null && underMountain[Idx(z), Idx(x)];

        // ---------- the meshes ----------

        /// <summary>Mountain_Near.obj (about 60k triangles) and Mountain_Far.obj (about 8k) from the FBX, with Unity's mesh LOD
        /// generator, in metres: 1 km across, centred, base at 0. Skipped when both are there already.</summary>
        static void PrepareMountainMeshes()
        {
            if (File.Exists(NearObj) && File.Exists(FarObj)) return;
            var imp = (ModelImporter)AssetImporter.GetAtPath(MountainFbx);
            if (imp == null) { Debug.LogError($"OODA: no {NearObj} and no {MountainFbx} to make it from."); return; }
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.animationType = ModelImporterAnimationType.None;
            imp.importAnimation = false;
            imp.isReadable = true;
            imp.SaveAndReimport();
            var mesh = Object.Instantiate(AssetDatabase.LoadAllAssetsAtPath(MountainFbx).OfType<Mesh>().First());
            MeshLodUtility.GenerateMeshLods(mesh, 10);
            var v = mesh.vertices; var n = mesh.normals; var uv = mesh.uv;
            Vector3 lo = v[0], hi = v[0];
            foreach (var p in v) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
            float k = ModelWidth / Mathf.Max(hi.x - lo.x, hi.z - lo.z);
            var off = new Vector3(-(lo.x + hi.x) / 2, -lo.y, -(lo.z + hi.z) / 2);

            int[] idx;
            using (var data = Mesh.AcquireReadOnlyMeshData(mesh))
            {
                var d = data[0];
                int bv = d.GetSubMesh(0).baseVertex;
                idx = d.indexFormat == IndexFormat.UInt32 ? d.GetIndexData<uint>().Select(x => (int)x + bv).ToArray() : d.GetIndexData<ushort>().Select(x => x + bv).ToArray();
            }
            var lods = Enumerable.Range(0, mesh.lodCount).Select(l => mesh.GetLod(0, l)).ToArray();
            Debug.Log("OODA: mountain LODs (triangles): " + string.Join(", ", lods.Select(r => r.indexCount / 3)));
            MeshLodRange Pick(int tris) => lods.OrderBy(r => Mathf.Abs(Mathf.Log(r.indexCount / 3f / tris))).First();
            WriteObj(NearObj, v, n, uv, idx, Pick(60000), k, off);
            WriteObj(FarObj, v, n, uv, idx, Pick(8000), k, off);
            Object.DestroyImmediate(mesh);
            AssetDatabase.ImportAsset(NearObj);
            AssetDatabase.ImportAsset(FarObj);
        }

        /// <summary>One LOD of the mesh as an OBJ. Unity's OBJ import mirrors x and turns the triangles round, so write it the other way.</summary>
        static void WriteObj(string path, Vector3[] v, Vector3[] n, Vector2[] uv, int[] idx, MeshLodRange r, float k, Vector3 off)
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
                sb.Append("v ").Append((-p.x).ToString("F2", ci)).Append(' ').Append(p.y.ToString("F2", ci)).Append(' ').Append(p.z.ToString("F2", ci)).Append('\n');
                sb.Append("vt ").Append(uv[i].x.ToString("F5", ci)).Append(' ').Append(uv[i].y.ToString("F5", ci)).Append('\n');
                sb.Append("vn ").Append((-n[i].x).ToString("F4", ci)).Append(' ').Append(n[i].y.ToString("F4", ci)).Append(' ').Append(n[i].z.ToString("F4", ci)).Append('\n');
                return o;
            }
            for (int t = (int)r.indexStart; t + 2 < r.indexStart + r.indexCount; t += 3)
            {
                int a = Use(idx[t]), b = Use(idx[t + 1]), c = Use(idx[t + 2]);
                faces.Append($"f {a}/{a}/{a} {c}/{c}/{c} {b}/{b}/{b}\n");
            }
            File.WriteAllText(path, "# OODA: the snowy mountain, metres, 1 km across\no Mountain\n" + sb + faces);
            Debug.Log($"OODA: wrote {path}: {map.Count} vertices, {r.indexCount / 3} triangles.");
        }

        /// <summary>A prepared mesh and how to put it in the 1 km frame: its import may not be in metres.</summary>
        static Mesh LoadMountainMesh(string path, out float fit, out Vector3 centre)
        {
            var mesh = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().FirstOrDefault();
            fit = 1; centre = Vector3.zero;
            if (mesh == null) return null;
            var b = mesh.bounds;
            fit = ModelWidth / Mathf.Max(b.size.x, b.size.z);
            centre = new Vector3(b.center.x, b.min.y, b.center.z);
            return mesh;
        }

        /// <summary>The model's top surface on a grid over its 1 km frame (x and z from -500 to 500 m), -1 where it isn't.</summary>
        static float[,] ModelHeights(Mesh mesh, float fit, Vector3 centre)
        {
            var hf = new float[Hf, Hf];
            for (int j = 0; j < Hf; j++) for (int i = 0; i < Hf; i++) hf[j, i] = -1;
            var v = mesh.vertices.Select(p => (p - centre) * fit).ToArray();
            var t = mesh.triangles;
            float g = ModelWidth / Hf;
            float G(float x) => (x + ModelWidth / 2) / g - 0.5f;
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
                        float x = -ModelWidth / 2 + (i + 0.5f) * g, z = -ModelWidth / 2 + (j + 0.5f) * g;
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
            int i = Mathf.FloorToInt((x + ModelWidth / 2) / ModelWidth * Hf), j = Mathf.FloorToInt((z + ModelWidth / 2) / ModelWidth * Hf);
            return i < 0 || j < 0 || i >= Hf || j >= Hf ? -1 : hf[j, i];
        }

        // ---------- where they go ----------

        static void PlaceMountains(float[,] h, bool[,] guard)
        {
            Peaks.Clear();
            underMountain = new bool[Res, Res];
            PrepareMountainMeshes();
            var mesh = LoadMountainMesh(NearObj, out float fit, out Vector3 centre);
            if (mesh == null) { Debug.LogWarning("OODA: no mountain mesh: the Alps are noise only."); return; }
            var hf = ModelHeights(mesh, fit, centre);
            float top = 0; int cover = 0;
            foreach (var y in hf) { top = Mathf.Max(top, y); if (y > 0) cover++; }
            Debug.Log($"OODA: the mountain model is {ModelWidth:F0} m across and {top:F0} m tall in its frame; it covers {cover * 100f / (Hf * Hf):F0} % of its square.");

            // more places it must not show: the railway, round the basin where the dogfight starts, and near the sea
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

            var rnd = new System.Random(2026);
            var kinds = new[] { ("great", 7, 7000f, 11000f, 3000), ("big", 14, 4500f, 7000f, 4000), ("small", 30, 2400f, 4200f, 5000) };
            foreach (var (kind, want, lo, hi, tries) in kinds)
            {
                int made = 0;
                for (int t = 0; t < tries && made < want; t++)
                {
                    float w = Rand(rnd, lo, hi);
                    var p = new Peak
                    {
                        kind = kind, width = w, yaw = Rand(rnd, 0f, 360f),
                        pos = new Vector3(Rand(rnd, -7800f, 7800f), 0, Rand(rnd, -7800f, 7800f)),
                        // stretched each way, a little taller or squatter, and every other one mirrored
                        scale = new Vector3(w / ModelWidth * Rand(rnd, 0.85f, 1.2f) * (rnd.Next(2) == 0 ? -1 : 1), w / ModelWidth * Rand(rnd, 0.85f, 1.35f), w / ModelWidth * Rand(rnd, 0.85f, 1.2f)),
                    };
                    if (guard[Idx(p.pos.z), Idx(p.pos.x)]) continue;
                    if (Peaks.Any(q => Vector2.Distance(V(q.pos.x, q.pos.z), V(p.pos.x, p.pos.z)) < 0.25f * (q.width + w) / 2)) continue;
                    if (!FitPeak(h, guard, hf, ref p, 3) || !FitPeak(h, guard, hf, ref p, 1)) continue;
                    Stamp(h, guard, hf, p);
                    Peaks.Add(p);
                    made++;
                }
                if (made < want) Debug.Log($"OODA: only {made} of {want} {kind} mountains found room.");
            }
            Debug.Log($"OODA: {Peaks.Count} mountains: " + string.Join(", ", Peaks.Select(q => $"{q.kind} ({q.pos.x:F0}, {q.pos.z:F0}) {q.width:F0} m")) + ".");
        }

        /// <summary>Visits the ground cells under a peak (every stride-th), with the model's height over its base there.</summary>
        static IEnumerable<(int i, int j, float rel)> Footprint(float[,] hf, Peak p, int stride)
        {
            var inv = Quaternion.Euler(0, -p.yaw, 0);
            float rad = 0.72f * ModelWidth * Mathf.Max(Mathf.Abs(p.scale.x), Mathf.Abs(p.scale.z));
            int ci = Idx(p.pos.x), cj = Idx(p.pos.z), rc = Mathf.CeilToInt(rad / Cell) + 1;
            for (int j = Mathf.Max(0, cj - rc); j <= Mathf.Min(Res - 1, cj + rc); j += stride)
                for (int i = Mathf.Max(0, ci - rc); i <= Mathf.Min(Res - 1, ci + rc); i += stride)
                {
                    var d = inv * new Vector3(X(i) - p.pos.x, 0, X(j) - p.pos.z);
                    float m = ModelAt(hf, d.x / p.scale.x, d.z / p.scale.z);
                    if (m > 0.5f) yield return (i, j, m * p.scale.y);
                }
        }

        /// <summary>Sets the peak's base: on the lower ground round it, then down as far as it must go to stay under the ground
        /// in the guarded cells. False if that sinks it more than a third, takes it over MaxSummit, or leaves it hardly
        /// higher than the noise peaks already there.</summary>
        static bool FitPeak(float[,] h, bool[,] guard, float[,] hf, ref Peak p, int stride)
        {
            var ground = new List<float>();
            float limit = float.MaxValue, peak = 0, underPeak = 0;
            foreach (var (i, j, rel) in Footprint(hf, p, stride))
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
            if (b + peak > MaxSummit || b + peak - underPeak < 350f) return false;
            p.pos.y = b;
            return true;
        }

        static void Stamp(float[,] h, bool[,] guard, float[,] hf, Peak p)
        {
            foreach (var (i, j, rel) in Footprint(hf, p, 1))
            {
                float surf = p.pos.y + rel;
                if (surf > h[j, i] + 3f) underMountain[j, i] = true;
                if (!guard[j, i]) h[j, i] = Mathf.Max(h[j, i], surf - 6f);   // just under the model: it is what you see
            }
        }

        // ---------- the models ----------

        static void MakeMountains()
        {
            if (Peaks.Count == 0) return;
            var near = LoadMountainMesh(NearObj, out float fit, out Vector3 centre);
            var far = LoadMountainMesh(FarObj, out _, out _);
            var ti = (TextureImporter)AssetImporter.GetAtPath(MountainTex);
            if (ti.maxTextureSize != 4096) { ti.maxTextureSize = 4096; ti.SaveAndReimport(); }
            var mat = TestWorldBuilder.Mat("Mountain", Color.white, 0.12f);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(MountainTex);
            mat.SetTexture("_BaseMap", tex);
            mat.SetTexture("_MainTex", tex);

            var root = new GameObject("Mountains").transform;
            for (int n = 0; n < Peaks.Count; n++)
            {
                var p = Peaks[n];
                var go = new GameObject($"Mountain {n} ({p.kind}, {p.width:F0} m across)");
                go.transform.SetParent(root, false);
                go.transform.SetPositionAndRotation(p.pos, Quaternion.Euler(0, p.yaw, 0));
                go.transform.localScale = p.scale;
                go.isStatic = true;
                Renderer Part(string name, Mesh mesh)
                {
                    var c = new GameObject(name);
                    c.transform.SetParent(go.transform, false);
                    c.transform.localPosition = -centre * fit;
                    c.transform.localScale = Vector3.one * fit;
                    c.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var r = c.AddComponent<MeshRenderer>();
                    r.sharedMaterial = mat;
                    c.isStatic = true;
                    return r;
                }
                var lod = go.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(0.15f, new[] { Part("Near", near) }), new LOD(0f, new[] { Part("Far", far ?? near) }) });
                lod.RecalculateBounds();
            }
        }
    }
}
