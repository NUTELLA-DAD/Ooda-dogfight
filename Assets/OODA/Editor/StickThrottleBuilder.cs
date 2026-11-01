using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ooda.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Ooda.EditorTools
{
    /// <summary>
    /// OODA > Build Stick and Throttle: makes the corner gauge (<see cref="StickThrottleGauge"/>) from Kenny's Hi3D
    /// joystick and throttle. Each FBX (40 MB, one fused mesh, not committed) is cut down with Unity's mesh LOD
    /// generator, scaled to 1 unit tall with its base centred on the origin, and skinned to two bones: the base, and
    /// the part that moves. The stick's shaft and grip follow the shaft bone, the base plate stays, and the rubber
    /// boot between them blends from one to the other with height, so it bends as the stick tips. The throttle's
    /// lever, grip and hub follow the lever bone round the hub's axle. The moving part is its own submesh, so the
    /// gauge can tint it. Then OODA > Stick and Throttle Shots renders it at a few poses to Shots~/controls.
    /// </summary>
    public static class StickThrottleBuilder
    {
        const string Art = "Assets/OODA/Art/Controls";
        const string Gen = "Assets/OODA/Generated";
        const string PrefabPath = "Assets/OODA/Resources/StickThrottleGauge.prefab";

        /// <summary>One control. Positions in the rules are in the model's height from its lowest corner (0..1 up).</summary>
        class Part
        {
            public string name; public int tris;
            public Func<Vector3, float> moving;   // 0 fixed, 1 follows the moving bone
            public Func<Vector3, Vector3, Vector3> pivot;   // (base centre, size) to the moving bone's place
        }

        static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }

        // The stick: base plate to 0.075, the boot's bellows from there to 0.235, then the straight shaft and the grip.
        const float BootBottom = 0.075f, BootTop = 0.235f;
        static readonly Part StickPart = new Part
        {
            name = "Joystick", tris = 45000,
            moving = p => Smooth(BootBottom, BootTop, p.y),
            pivot = (c, s) => new Vector3(c.x, BootBottom, c.z),
        };

        // The throttle: the quadrant's housing reaches 0.53; above it only the lever and grip. The lever comes down the
        // housing's outer (-x) side to the brass hub at height 0.34, 0.39 deep, and turns about the axle (x) through it.
        static readonly Part ThrottlePart = new Part
        {
            name = "Throttle", tris = 45000,
            moving = p => p.y > 0.535f || (p.x < LeverX && p.y > 0.2f) || (p.x < 0.235f && p.y > 0.49f) ? 1f : 0f,
            pivot = (c, s) => new Vector3(c.x, 0.34f, 0.39f),
        };
        static float LeverX = 0.178f;

        [MenuItem("OODA/Build Stick and Throttle")]
        public static void Build()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            Directory.CreateDirectory($"{Gen}/Controls");
            var stick = MakePart(StickPart, out var stickPivot, out var stickMats);
            var throttle = MakePart(ThrottlePart, out var leverPivot, out var throttleMats);
            if (stick == null || throttle == null) return;

            var root = new GameObject("StickThrottleGauge");
            var gauge = root.AddComponent<StickThrottleGauge>();
            gauge.stick = Rig(root.transform, "Stick", new Vector3(-3, 0, 0), stick, stickMats, stickPivot, out gauge.stickRoot, out gauge.stickShaft);
            gauge.throttle = Rig(root.transform, "Throttle", new Vector3(3, 0, 0), throttle, throttleMats, leverPivot, out gauge.throttleRoot, out gauge.throttleLever);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log("OODA: stick and throttle gauge built.");
        }

        static SkinnedMeshRenderer Rig(Transform parent, string name, Vector3 at, Mesh mesh, Material[] mats, Vector3 pivot, out Transform root, out Transform moving)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            root = new GameObject(name + "Base").transform;
            root.SetParent(go.transform, false);
            moving = new GameObject(name + "Moving").transform;
            moving.SetParent(root, false);
            moving.localPosition = pivot;
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.sharedMaterials = mats;
            smr.bones = new[] { root, moving };
            smr.rootBone = root;
            smr.localBounds = new Bounds(new Vector3(0, 0.5f, 0), Vector3.one * 2f);
            smr.updateWhenOffscreen = true;
            smr.shadowCastingMode = ShadowCastingMode.Off;
            smr.receiveShadows = false;
            return smr;
        }

        /// <summary>The part's skinned mesh (saved under Generated/Controls) and its two materials, base then moving.</summary>
        static Mesh MakePart(Part part, out Vector3 pivot, out Material[] mats)
        {
            pivot = Vector3.zero; mats = null;
            string fbx = $"{Art}/{part.name}.fbx", tex = $"{Art}/{part.name}_BaseColor.jpg";
            var imp = (ModelImporter)AssetImporter.GetAtPath(fbx);
            if (imp == null) { Debug.LogError($"OODA: no {fbx} (copy Kenny's Hi3D export there)."); return null; }
            if (imp.materialImportMode != ModelImporterMaterialImportMode.None || !imp.isReadable)
            {
                imp.materialImportMode = ModelImporterMaterialImportMode.None;
                imp.animationType = ModelImporterAnimationType.None;
                imp.importAnimation = false;
                imp.isReadable = true;
                imp.SaveAndReimport();
            }
            // the base colour is embedded in the FBX: take it out next to it (committed, the FBX is not)
            if (!File.Exists(tex))
            {
                string tmp = $"{Art}/{part.name}_Extracted";
                imp.ExtractTextures(tmp);
                AssetDatabase.Refresh();
                File.Copy($"{tmp}/0.jpg", tex);
                AssetDatabase.DeleteAsset(tmp);
                AssetDatabase.ImportAsset(tex);
            }
            var ti = (TextureImporter)AssetImporter.GetAtPath(tex);
            if (ti != null && ti.maxTextureSize != 1024) { ti.maxTextureSize = 1024; ti.SaveAndReimport(); }

            var src = Object.Instantiate(AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Mesh>().First());
            var v = src.vertices; var n = src.normals; var uv = src.uv;
            Vector3 lo = v[0], hi = v[0];
            foreach (var p in v) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
            float k = 1f / (hi.y - lo.y);
            var unit = v.Select(p => (p - lo) * k).ToArray();   // 0..1 up, from the lowest corner
            // the base's centre: the middle of the footprint
            var foot = unit.Where(p => p.y < 0.05f).ToArray();
            var centre = new Vector3((foot.Min(p => p.x) + foot.Max(p => p.x)) / 2, 0, (foot.Min(p => p.z) + foot.Max(p => p.z)) / 2);
            var size = (hi - lo) * k;

            MeshLodUtility.GenerateMeshLods(src, 14);
            var lods = Enumerable.Range(0, src.lodCount).Select(l => src.GetLod(0, l)).ToArray();
            var lod = lods.OrderBy(r => Mathf.Abs(Mathf.Log(r.indexCount / 3f / part.tris))).First();
            int bv; int[] all;   // the whole index buffer, which holds every LOD
            using (var data = Mesh.AcquireReadOnlyMeshData(src))
            {
                var d = data[0];
                bv = d.GetSubMesh(0).baseVertex;
                all = d.indexFormat == IndexFormat.UInt32 ? d.GetIndexData<uint>().Select(x => (int)x).ToArray() : d.GetIndexData<ushort>().Select(x => (int)x).ToArray();
            }
            Debug.Log($"OODA: {part.name} LODs (triangles): " + string.Join(", ", lods.Select(r => r.indexCount / 3)) + $"; using {lod.indexCount / 3}");

            var map = new Dictionary<int, int>();
            var pos = new List<Vector3>(); var nor = new List<Vector3>(); var tc = new List<Vector2>(); var w = new List<float>();
            var fixedTris = new List<int>(); var movingTris = new List<int>();
            int Use(int i)
            {
                if (map.TryGetValue(i, out int o)) return o;
                o = pos.Count; map[i] = o;
                pos.Add(unit[i] - centre); nor.Add(n[i]); tc.Add(uv[i]); w.Add(part.moving(unit[i]));
                return o;
            }
            for (int t = (int)lod.indexStart; t + 2 < lod.indexStart + lod.indexCount; t += 3)
            {
                int a = Use(all[t] + bv), b = Use(all[t + 1] + bv), c = Use(all[t + 2] + bv);
                var list = w[a] > 0.99f && w[b] > 0.99f && w[c] > 0.99f ? movingTris : fixedTris;
                list.Add(a); list.Add(b); list.Add(c);
            }
            Object.DestroyImmediate(src);

            pivot = part.pivot(centre, size) - centre;
            // refill the saved mesh in place, so the prefab keeps pointing at it (copying one mesh over another garbles it)
            string path = $"{Gen}/Controls/{part.name}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool fresh = mesh == null;
            if (fresh) mesh = new Mesh(); else mesh.Clear();
            mesh.name = part.name;
            mesh.indexFormat = pos.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(pos); mesh.SetNormals(nor); mesh.SetUVs(0, tc);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(fixedTris, 0);
            mesh.SetTriangles(movingTris, 1);
            mesh.boneWeights = w.Select(x => new BoneWeight { boneIndex0 = 0, weight0 = 1 - x, boneIndex1 = 1, weight1 = x }).ToArray();
            mesh.bindposes = new[] { Matrix4x4.identity, Matrix4x4.Translate(-pivot) };
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            if (fresh) AssetDatabase.CreateAsset(mesh, path); else EditorUtility.SetDirty(mesh);
            Debug.Log($"OODA: {part.name}: {pos.Count} vertices, {fixedTris.Count / 3} fixed and {movingTris.Count / 3} moving triangles, pivot {pivot:F3}, size {size:F3}");

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(tex);
            mats = new[] { PartMat($"Controls_{part.name}", texture), PartMat($"Controls_{part.name}Moving", texture) };
            return mesh;
        }

        static Material PartMat(string name, Texture2D tex)
        {
            var m = TestWorldBuilder.Mat(name, Color.white, 0.35f, 0f);
            m.SetTexture("_BaseMap", tex);
            m.SetTexture("_MainTex", tex);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.black);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            return m;
        }

        // ---------- shots ----------

        /// <summary>For batch runs: build, then the shots.</summary>
        public static void BuildAndShots() { Build(); Shots(); }

        [MenuItem("OODA/Stick and Throttle Shots")]
        public static void Shots()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) { Build(); prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath); }
            Directory.CreateDirectory("Shots~/controls");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.position = new Vector3(0, -20000, 0);
            var light = new GameObject("ShotLight").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);
            var gauge = go.GetComponent<StickThrottleGauge>();
            var awake = typeof(StickThrottleGauge).GetMethod("Awake", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            awake.Invoke(gauge, null);
            var poses = new (string name, Vector2 stick, float over, float thr, bool wep)[]
            {
                ("centre_idle", Vector2.zero, 0, 0, false),
                ("ring_back_half", new Vector2(0, StickThrottleGauge.RingTravel), 0, 0.5f, false),
                ("full_back_overpull_full", new Vector2(0, 1), 1, 1, true),
                ("right", new Vector2(StickThrottleGauge.RingTravel, 0), 0, 0.75f, false),
                ("left_forward", new Vector2(-0.6f, -0.6f), 0, 0.25f, false),
                ("back_right", new Vector2(0.55f, 0.55f), 0.4f, 1, false),
            };
            foreach (var p in poses)
            {
                gauge.Show(p.stick, p.over, p.thr, p.wep, true);
                var baked = Bake(go);
                Save(gauge.StickTexture, gauge.transform.Find("Stick").GetComponentInChildren<Camera>(), $"Shots~/controls/stick_{p.name}.png");
                Save(gauge.ThrottleTexture, gauge.transform.Find("Throttle").GetComponentInChildren<Camera>(), $"Shots~/controls/throttle_{p.name}.png");
                foreach (var b in baked) Object.DestroyImmediate(b);
            }
            // the cut: the moving part in plain red, seen from the side and from behind, at rest
            gauge.Show(Vector2.zero, 0, 0.5f, false, true);
            var red = new MaterialPropertyBlock();
            red.SetColor("_BaseColor", Color.red);
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>()) r.SetPropertyBlock(red, 1);
            foreach (var (part, tex) in new[] { ("Stick", gauge.StickTexture), ("Throttle", gauge.ThrottleTexture) })
            {
                var cam = gauge.transform.Find(part).GetComponentInChildren<Camera>();
                var root = cam.transform.parent;
                foreach (var (side, at) in new[] { ("side", new Vector3(-2.4f, 0.5f, 0f)), ("back", new Vector3(0f, 0.6f, -2.4f)), ("front", new Vector3(0f, 0.6f, 2.4f)) })
                {
                    cam.transform.localPosition = at;
                    cam.transform.LookAt(root.TransformPoint(new Vector3(0, 0.5f, 0)));
                    var baked = Bake(go);
                    Save(tex, cam, $"Shots~/controls/cut_{part.ToLower()}_{side}.png");
                    foreach (var b in baked) Object.DestroyImmediate(b);
                }
            }
            Object.DestroyImmediate(light.gameObject);
            Object.DestroyImmediate(go);
            Debug.Log("OODA: stick and throttle shots in Shots~/controls.");
        }

        /// <summary>Batch renders do not skin: stand a baked copy of each skinned part in its place for the shot.</summary>
        internal static List<GameObject> Bake(GameObject root)
        {
            var made = new List<GameObject>();
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.gameObject.name.EndsWith("Baked")) continue;
                var mesh = new Mesh();
                smr.BakeMesh(mesh, true);
                var go = new GameObject(smr.name + "Baked") { layer = smr.gameObject.layer };
                go.transform.SetParent(smr.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = smr.sharedMaterials;
                var block = new MaterialPropertyBlock();
                smr.GetPropertyBlock(block, 1);
                mr.SetPropertyBlock(block, 1);
                smr.enabled = false;
                made.Add(go);
            }
            return made;
        }

        /// <summary>Render the camera and save the texture over a checker, so a clear background shows as clear.</summary>
        static void Save(RenderTexture rt, Camera cam, string file)
        {
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var png = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            png.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            RenderTexture.active = prev;
            var px = png.GetPixels();
            for (int i = 0; i < px.Length; i++)
            {
                int x = i % rt.width, y = i / rt.width;
                Color bg = ((x / 32 + y / 32) % 2 == 0) ? new Color(0.35f, 0.5f, 0.7f) : new Color(0.55f, 0.7f, 0.85f);
                px[i] = Color.Lerp(bg, new Color(px[i].r, px[i].g, px[i].b), px[i].a);
            }
            png.SetPixels(px);
            File.WriteAllBytes(file, png.EncodeToPNG());
            Object.DestroyImmediate(png);
        }
    }
}
