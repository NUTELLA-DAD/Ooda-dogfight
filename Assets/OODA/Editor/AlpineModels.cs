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
    /// <summary>The Unity side of the land models (AlpineMountains places them): their meshes, made from the Hi3D FBXs the first
    /// time, and the copies in the scene with their LODs.</summary>
    internal static partial class AlpineWorld
    {
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

        /// <summary>The far pine in the model's own frame (as its meshes are): a six-sided cone the model's height, its skirt a
        /// tenth of the way up and as wide as the model, so it takes the same place in the tree's matrix.</summary>
        static Mesh PineCone(LandModel m)
        {
            string path = $"{Dir}/PineTree_Cone.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, path); }
            mesh.Clear();
            const int N = 6;
            float tall = m.size, rad = tall * 0.2f, skirt = tall * 0.1f;
            Vector3 M(float x, float y, float z) => new Vector3(x, y, z) / m.fit + m.centre;   // metres to the model's frame
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            float lean = rad / (tall - skirt);   // the side's normal tips up by the cone's slope
            for (int k = 0; k < N; k++)
            {
                float a0 = k * Mathf.PI * 2 / N, a1 = (k + 1) * Mathf.PI * 2 / N, am = (a0 + a1) / 2;
                var nm = new Vector3(Mathf.Sin(am), lean, Mathf.Cos(am)).normalized;
                int b = v.Count;
                v.Add(M(0, tall, 0)); v.Add(M(Mathf.Sin(a1) * rad, skirt, Mathf.Cos(a1) * rad)); v.Add(M(Mathf.Sin(a0) * rad, skirt, Mathf.Cos(a0) * rad));
                n.Add(nm); n.Add(nm); n.Add(nm);
                t.Add(b); t.Add(b + 2); t.Add(b + 1);   // clockwise seen from outside
            }
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }
    }
}
