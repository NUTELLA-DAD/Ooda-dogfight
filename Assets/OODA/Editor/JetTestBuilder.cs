using System.IO;
using System.Linq;
using Ooda.CameraSystem;
using Ooda.Flight;
using Ooda.Flight.Core;
using Ooda.Pilot;
using Ooda.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ooda.EditorTools
{
    /// <summary>
    /// OODA > Build Jet Test: a side test, away from the WW2 missions. The Phantom (Art/Phantom: the Hi3D model cut to about
    /// 50k triangles, 19.2 m long, with its ailerons, stabilators and rudder cut out to move: Phantom.fbx, made from
    /// Phantom.obj by Tools~/ArtPipeline/control_surfaces.py with specs/phantom_controls.json) in free flight over the alpine test world, with jet numbers (AircraftNumbers.Phantom)
    /// and an afterburner on the WEP button, painted in SEA camo with a tiger and a wolf for its crew (JetTestBuilder.Paint,
    /// .Crew). Saved as JetTest.unity and added to the build; the title's Jet Test button
    /// (or J) opens it. Run it after Build Test World (it starts from that scene) and Build Title.
    /// </summary>
    public static partial class JetTestBuilder
    {
        const string Dir = "Assets/OODA/Art/Phantom";
        const string Obj = Dir + "/Phantom.fbx";
        const string BodyName = "Phantom_Body";
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
            MakeTurnPrefab();   // the turn battle's Phantom gets the same paint and crew
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
            mi.preserveHierarchy = true;   // the body stays a node of its own, its moving surfaces under it
            mi.SaveAndReimport();

            var liveryMaps = Liveries.Select(n => ($"{Dir}/Phantom_{n}_BaseColor.jpg", 4096, true));
            foreach (var (path, size, srgb) in new[] { (BaseTex, 4096, true), (MetalTex, 2048, false) }.Concat(liveryMaps))
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                if (ti == null) continue;
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

            var look = AddModel(root.transform);
            if (look != null) look.aircraft = ac;

            var burner = root.AddComponent<JetBurner>();
            burner.aircraft = ac;
            var hud = root.AddComponent<JetHud>();
            hud.aircraft = ac;

            var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return saved;
        }

        /// <summary>The Phantom's other paint jobs (Tools~/ArtPipeline/phantom_recolor.py repaints the SEA camo): the Navy's
        /// light gull grey over white, the USAF's later Hill Grey, and Europe One's greens and grey. Handed out in turn.</summary>
        static readonly string[] Liveries = { "Navy", "HillGrey", "EuropeOne" };

        /// <summary>
        /// The model (camo body and clear canopy, its moving surfaces under it), turned and scaled so its nose is +z, its
        /// fin up, and it is Length long, centred on its bounds; its crew; the cockpit eye at the pilot's; and the parts
        /// that swing the surfaces and hand out the paint jobs.
        /// </summary>
        static PilotHeadLook AddModel(Transform root)
        {
            var (mesh, body, glass) = Paint();
            if (mesh == null) return null;
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Obj));
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "Model";
            model.transform.SetParent(root, false);
            var bodyT = BodyOf(model.transform);
            bodyT.GetComponent<MeshFilter>().sharedMesh = mesh;
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                r.sharedMaterials = r.transform == bodyT ? new[] { body, glass } : new[] { body };
                r.shadowCastingMode = ShadowCastingMode.On;
            }
            var (turn, k, centre) = FitOf(mesh);
            model.transform.localRotation = turn;
            model.transform.localScale = Vector3.one * k;
            model.transform.localPosition = -(turn * centre) * k;
            Debug.Log($"OODA: the Phantom's mesh: {mesh.vertexCount} vertices, {mesh.triangles.Length / 3} triangles, scaled {k:F3} to {Length} m, " +
                      $"{model.GetComponentsInChildren<Renderer>().Length - 1} moving surfaces.");

            root.gameObject.AddComponent<ControlSurfaces>();
            AddLiveries(root.gameObject, body);

            var look = AddCrew(root, mesh, bodyT);
            // the eye for the C camera: the front pilot's (his head is hidden in that view)
            var eye = new GameObject("CockpitCamera").transform;
            eye.SetParent(root, false);
            eye.localPosition = new Vector3(0, 1.4f, 1.5f);
            if (look != null) EyeAtHead(root, eye);
            return look;
        }

        /// <summary>The body's node in the model: the mesh the canopy is split from and the crew are fitted to. FitOf and the
        /// paint work in its space, so it must sit on the model's root unmoved (as unity_export.py writes it).</summary>
        static Transform BodyOf(Transform model)
        {
            var t = TestWorldBuilder.FindDeep(model, BodyName) ?? model;
            if (t != model && (t.localPosition.sqrMagnitude > 1e-6f || Quaternion.Angle(t.localRotation, Quaternion.identity) > 0.01f || (t.localScale - Vector3.one).sqrMagnitude > 1e-6f))
                Debug.LogWarning($"OODA: {BodyName} is moved within Phantom.fbx; the canopy and crew may sit off the model.");
            return t;
        }

        /// <summary>The paint jobs, each with the camo's flat-paint gloss map (the repaint keeps where the paint is).</summary>
        static void AddLiveries(GameObject root, Material body)
        {
            var livery = root.AddComponent<PlaneLivery>();
            livery.standard = body;
            livery.others = Liveries.Select(n => TestWorldBuilder.Textured("Phantom_" + n, $"{Dir}/Phantom_{n}_BaseColor.jpg", CamoMetalTex, toon: true)).ToArray();
        }

        const string TurnPrefabPath = "Assets/OODA/Prefabs/PhantomTurn.prefab";

        /// <summary>
        /// The Phantom for the turn battle's dogfight (Build Turn Battle calls this): the same model and eye, flown by a
        /// TurnAircraftView, with its afterburner flame following the turn view (lit when its WEP is in), and its crew.
        /// </summary>
        public static GameObject MakeTurnPrefab()
        {
            Imports();
            var root = new GameObject("PhantomTurn");
            root.AddComponent<Ooda.Turn.TurnAircraftView>();
            AddModel(root.transform);
            root.AddComponent<JetBurner>();   // it finds the TurnAircraftView itself
            var saved = PrefabUtility.SaveAsPrefabAsset(root, TurnPrefabPath);
            Object.DestroyImmediate(root);
            return saved;
        }

        /// <summary>
        /// Point the mesh's nose along +z and its fin up, whatever axes the OBJ came in with: the long axis is the fuselage,
        /// the end with the smaller cross-section is the nose, and the fin stands on the side the tail reaches furthest.
        /// </summary>
        static (Quaternion turn, float k, Vector3 centre) FitOf(Mesh mesh)
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
            return (turn, Length / (2 * half), b.center);
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
            rig.pilot = jet.GetComponentInChildren<Ooda.Pilot.PilotHeadLook>();
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
