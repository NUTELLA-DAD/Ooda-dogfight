using System.IO;
using System.Linq;
using Ooda.CameraSystem;
using Ooda.Flight;
using Ooda.Flight.Core;
using Ooda.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ooda.EditorTools
{
    /// <summary>
    /// OODA > Build Jet Test: a side test, away from the WW2 missions. The Phantom (Art/Phantom: the Hi3D model cut to about
    /// 50k triangles, 19.2 m long) in free flight over the alpine test world, with jet numbers (AircraftNumbers.Phantom)
    /// and an afterburner on the WEP button. Saved as JetTest.unity and added to the build; the title's Jet Test button
    /// (or J) opens it. Run it after Build Test World (it starts from that scene) and Build Title.
    /// </summary>
    public static class JetTestBuilder
    {
        const string Dir = "Assets/OODA/Art/Phantom";
        const string Obj = Dir + "/Phantom.obj";
        const string BaseTex = Dir + "/Phantom_BaseColor.jpg";
        const string MetalTex = Dir + "/Phantom_MetallicSmoothness.png";
        const string PerfPath = "Assets/OODA/Settings/Phantom.asset";
        const string PrefabPath = "Assets/OODA/Prefabs/Phantom.prefab";
        const string ScenePath = "Assets/OODA/Scenes/JetTest.unity";
        const float Length = 19.2f;   // metres, nose to tail

        /// <summary>Where the jet starts: over the lake at the foot of the main valley, high enough to clear the peaks ahead, heading up it.</summary>
        static readonly Vector3 Spawn = new Vector3(0, 4300f, -12000f);

        [MenuItem("OODA/Build Jet Test")]
        public static void Build()
        {
            if (!File.Exists(TestWorldBuilder.ScenePath)) TestWorldBuilder.Build();
            Imports();
            var prefab = MakePrefab();
            MakeScene(prefab);
            AssetDatabase.SaveAssets();
            Debug.Log("OODA: jet test built (JetTest.unity, in the build after the title). Title > Jet Test, or open the scene and press Play.");
        }

        static void Imports()
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(Obj);
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importAnimation = false;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.indexFormat = ModelImporterIndexFormat.Auto;
            mi.SaveAndReimport();

            foreach (var (path, size, srgb) in new[] { (BaseTex, 4096, true), (MetalTex, 2048, false) })
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                ti.sRGBTexture = srgb;
                ti.mipmapEnabled = true;
                ti.anisoLevel = 8;
                ti.maxTextureSize = size;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.SaveAndReimport();
            }
        }

        static GameObject MakePrefab()
        {
            var root = new GameObject("Phantom");
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            var input = root.AddComponent<PlaneInput>();
            input.wepToggles = true;
            input.startThrottle = 0.8f;
            input.throttleRate = 0.5f;
            var ac = root.AddComponent<AircraftController>();
            ac.startKtas = 420;
            ac.slipDegPerG = 8;

            // the numbers are remade on every build, so fixes here reach the asset (tune in the Inspector while flying)
            var perf = AssetDatabase.LoadAssetAtPath<AircraftPerformance>(PerfPath);
            if (perf == null) { perf = ScriptableObject.CreateInstance<AircraftPerformance>(); AssetDatabase.CreateAsset(perf, PerfPath); }
            perf.numbers = AircraftNumbers.Phantom();
            EditorUtility.SetDirty(perf);
            ac.performance = perf;

            // the model, turned and scaled so its nose is +z, its fin up, and it is Length long, centred on its bounds
            var mesh = AssetDatabase.LoadAllAssetsAtPath(Obj).OfType<Mesh>().FirstOrDefault();
            var model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);
            model.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = model.AddComponent<MeshRenderer>();
            mr.sharedMaterial = TestWorldBuilder.Textured("Phantom", BaseTex, MetalTex);
            mr.shadowCastingMode = ShadowCastingMode.On;
            if (mesh != null) Fit(model.transform, mesh);
            else Debug.LogWarning("OODA: no mesh in " + Obj + ".");

            // the eye for the C camera: just above and behind the canopy, looking over the nose
            var eye = new GameObject("CockpitCamera").transform;
            eye.SetParent(root.transform, false);
            eye.localPosition = new Vector3(0, 1.4f, 1.5f);

            var burner = root.AddComponent<JetBurner>();
            burner.aircraft = ac;
            var hud = root.AddComponent<JetHud>();
            hud.aircraft = ac;

            var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return saved;
        }

        /// <summary>
        /// Point the mesh's nose along +z and its fin up, whatever axes the OBJ came in with: the long axis is the fuselage,
        /// the end with the smaller cross-section is the nose, and the fin stands on the side the tail reaches furthest.
        /// </summary>
        static void Fit(Transform model, Mesh mesh)
        {
            var v = mesh.vertices;
            var b = mesh.bounds;
            int along = b.size.z >= b.size.x && b.size.z >= b.size.y ? 2 : b.size.x >= b.size.y ? 0 : 1;
            if (along != 2) Debug.LogWarning($"OODA: the Phantom's long axis is {"xyz"[along]}, not z; turning it.");
            Quaternion turn = along == 2 ? Quaternion.identity : along == 0 ? Quaternion.Euler(0, -90, 0) : Quaternion.Euler(90, 0, 0);
            var p = v.Select(x => turn * (x - b.center)).ToArray();
            float half = p.Max(x => Mathf.Abs(x.z));
            float WidthNear(float end) => p.Where(x => Mathf.Abs(x.z - end) < half * 0.12f).Select(x => Mathf.Abs(x.x)).DefaultIfEmpty(0).Max();
            if (WidthNear(-half) < WidthNear(half) * 0.8f) { turn = Quaternion.Euler(0, 180, 0) * turn; p = p.Select(x => Quaternion.Euler(0, 180, 0) * x).ToArray(); }
            // the fin: the tail's highest point should be up
            var tail = p.Where(x => x.z < -half * 0.75f).ToArray();
            if (tail.Length > 0 && -tail.Min(x => x.y) > tail.Max(x => x.y)) { turn = Quaternion.Euler(0, 0, 180) * turn; }
            float k = Length / (2 * half);
            model.localRotation = turn;
            model.localScale = Vector3.one * k;
            model.localPosition = -(turn * b.center) * k;
            Debug.Log($"OODA: the Phantom's mesh: {mesh.vertexCount} vertices, {mesh.triangles.Length / 3} triangles, scaled {k:F3} to {Length} m.");
        }

        static void MakeScene(GameObject prefab)
        {
            var scene = EditorSceneManager.OpenScene(TestWorldBuilder.ScenePath, OpenSceneMode.Single);
            foreach (var old in Object.FindObjectsByType<AircraftController>(FindObjectsSortMode.None)) Object.DestroyImmediate(old.gameObject);

            var jet = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            jet.transform.SetPositionAndRotation(Spawn, Quaternion.identity);

            var cam = Camera.main;
            var rig = cam.GetComponent<CameraRig>();
            if (rig == null) rig = cam.gameObject.AddComponent<CameraRig>();
            rig.aircraft = jet.GetComponent<AircraftController>();
            rig.cockpitEye = TestWorldBuilder.FindDeep(jet.transform, "CockpitCamera");
            rig.pilot = null;
            rig.distance = 32f;
            rig.height = 6f;
            rig.lookAhead = 45f;
            rig.baseFov = 60f;
            rig.fastFov = 80f;
            rig.fastKias = 650f;
            rig.cockpitFov = 80f;
            cam.transform.position = Spawn - Vector3.forward * 32f + Vector3.up * 6f;

            // saved under its own name: the test world stays as it was
            EditorSceneManager.SaveScene(scene, ScenePath);
            var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            list.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
