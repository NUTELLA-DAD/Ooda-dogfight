using System.IO;
using System.Linq;
using Ooda.CameraSystem;
using Ooda.Flight;
using Ooda.Pilot;
using Ooda.Turn;
using Ooda.UI;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ooda.EditorTools
{
    /// <summary>
    /// OODA > Build Turn Battle: imports the Spitfire and its wolf pilot, makes the Spitfire prefab, and makes
    /// the turn-based scene from a copy of the test world: the P-51 there loses its free-flight parts and
    /// gains a turn view, the Spitfire joins it, and the camera becomes the turn camera. The free-flight
    /// test world stays as it is, as the sandbox. Safe to run again.
    /// </summary>
    public static class TurnBattleBuilder
    {
        const string Root = "Assets/OODA";
        const string Art = Root + "/Art";
        const string Gen = Root + "/Generated";
        const string SpitFbx = Art + "/Spitfire/Spitfire.fbx";
        const string WolfFbx = Art + "/WolfPilot/WolfPilot.fbx";
        const string SpitPrefabPath = Root + "/Prefabs/Spitfire.prefab";
        const string ScenePath = Root + "/Scenes/TurnBattle.unity";

        // Cartoon size, no glass: the wolf nearly overfills the cockpit with his forearms on the rails (0.82 fitted under the canopy).
        static readonly TestWorldBuilder.Seat WolfSeat = new TestWorldBuilder.Seat { scale = 1.35f, lift = -0.34f, back = 0f, railHalfWidth = 0.37f, railHeight = 0.42f, railForward = 0.22f };
        const float PlaneSmoothingAngle = 80f;

        [MenuItem("OODA/Build Turn Battle")]
        public static void Build()
        {
            if (!File.Exists(TestWorldBuilder.ScenePath)) TestWorldBuilder.Build();
            ConfigureImports();
            var mats = TestWorldBuilder.MakeMaterials();
            var spitMat = TestWorldBuilder.Textured("Spitfire", Art + "/Spitfire/Spit_BaseColor.png", Art + "/Spitfire/Spit_MetallicSmoothness.png");
            var wolfMat = TestWorldBuilder.Textured("WolfPilot", Art + "/WolfPilot/Wolf_BaseColor.png", Art + "/WolfPilot/Wolf_MetallicSmoothness.png");
            var wolfCtrl = MakeSeatedAnimator(WolfFbx, "WolfPilot");
            MakeSpitfirePrefab(mats, spitMat, wolfMat, wolfCtrl);
            var bf109 = MakeBf109Prefab(mats, wolfMat, wolfCtrl);
            var b17 = MakeB17Prefab();
            var p51Turn = MakeP51TurnPrefab();
            MakeScene(bf109, p51Turn, b17);
            AssetDatabase.SaveAssets();
            Debug.Log("OODA: turn battle built. Press Play.");
        }

        static void ConfigureImports()
        {
            var plane = (ModelImporter)AssetImporter.GetAtPath(SpitFbx);
            plane.materialImportMode = ModelImporterMaterialImportMode.None;
            plane.animationType = ModelImporterAnimationType.None;
            plane.importAnimation = false;
            plane.importNormals = ModelImporterNormals.Calculate;
            plane.normalSmoothingSource = ModelImporterNormalSmoothingSource.FromAngle;
            plane.normalSmoothingAngle = PlaneSmoothingAngle;
            plane.importTangents = ModelImporterTangents.CalculateMikk;
            plane.SaveAndReimport();

            var pilot = (ModelImporter)AssetImporter.GetAtPath(WolfFbx);
            pilot.materialImportMode = ModelImporterMaterialImportMode.None;
            pilot.animationType = ModelImporterAnimationType.Human;
            pilot.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            pilot.importAnimation = true;
            var clips = pilot.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.name = "Seated";
                c.loopTime = true;
                c.lockRootRotation = c.lockRootHeightY = c.lockRootPositionXZ = true;
                c.keepOriginalOrientation = c.keepOriginalPositionY = c.keepOriginalPositionXZ = true;
            }
            pilot.clipAnimations = clips;
            pilot.SaveAndReimport();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(WolfFbx).OfType<Avatar>().FirstOrDefault();
            if (avatar != null && avatar.isValid && avatar.isHuman) Debug.Log("OODA: WolfPilot avatar is valid (Humanoid).");
            else Debug.LogError($"OODA: WolfPilot avatar is not a valid humanoid (found: {avatar != null}, valid: {avatar?.isValid}, human: {avatar?.isHuman}).");

            foreach (var t in new[] { Art + "/Spitfire/Spit_MetallicSmoothness.png", Art + "/WolfPilot/Wolf_MetallicSmoothness.png" })
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(t);
                ti.sRGBTexture = false;
                ti.SaveAndReimport();
            }
        }

        static RuntimeAnimatorController MakeSeatedAnimator(string fbx, string name)
        {
            string path = $"{Gen}/{name}.controller";
            AssetDatabase.DeleteAsset(path);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            var clip = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
            var layers = ctrl.layers;
            layers[0].iKPass = true;
            ctrl.layers = layers;
            ctrl.layers[0].stateMachine.AddState("Seated").motion = clip;
            if (clip == null) Debug.LogWarning($"OODA: no Seated clip found in {fbx}.");
            return ctrl;
        }

        static GameObject MakeSpitfirePrefab(TestWorldBuilder.Mats mats, Material body, Material pilotMat, RuntimeAnimatorController pilotCtrl) =>
            MakeWolfFighter("Spitfire", SpitFbx, "Spit_Propeller", SpitPrefabPath, WolfSeat, mats, body, pilotMat, pilotCtrl);

        /// <summary>An enemy fighter for the turn battle: the model with its canopy hidden, the wolf seated in it, and its propeller turning.</summary>
        static GameObject MakeWolfFighter(string name, string fbx, string propeller, string prefabPath, TestWorldBuilder.Seat seat,
                                          TestWorldBuilder.Mats mats, Material body, Material pilotMat, RuntimeAnimatorController pilotCtrl)
        {
            var root = new GameObject(name);
            root.AddComponent<TurnAircraftView>();
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
            model.transform.SetParent(root.transform, false);
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                Material m = r.name.Contains("Canopy") ? mats.canopy : r.name.Contains("Cockpit") ? mats.cockpit : body;
                r.sharedMaterials = Enumerable.Repeat(m, r.sharedMaterials.Length).ToArray();
                if (r.name.Contains("Canopy")) r.shadowCastingMode = ShadowCastingMode.Off;
            }

            var spin = root.AddComponent<PropellerSpin>();
            spin.propeller = TestWorldBuilder.FindDeep(model.transform, propeller);
            if (spin.propeller == null) Debug.LogWarning($"OODA: no {propeller} in {fbx}.");

            var mount = TestWorldBuilder.FindDeep(model.transform, "PilotMount");
            if (mount == null) { Debug.LogWarning($"OODA: no PilotMount in {fbx}."); mount = root.transform; }
            var pilot = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(WolfFbx));
            TestWorldBuilder.HideCanopy(model);
            TestWorldBuilder.SeatPilot(pilot, mount, seat);
            foreach (var r in pilot.GetComponentsInChildren<Renderer>())
                r.sharedMaterials = Enumerable.Repeat(pilotMat, r.sharedMaterials.Length).ToArray();
            var anim = pilot.GetComponent<Animator>();
            if (anim == null) anim = pilot.AddComponent<Animator>();
            anim.runtimeAnimatorController = pilotCtrl;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            pilot.AddComponent<PilotHeadLook>();

            var saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            return saved;
        }

        // ---------- the Bf 109, the wingmen's P-51 and the flak ----------

        const string Bf109Fbx = Art + "/Bf109/Bf109.fbx";
        const string Bf109PrefabPath = Root + "/Prefabs/Bf109.prefab";
        const string P51TurnPrefabPath = Root + "/Prefabs/P51Turn.prefab";
        const string FlakFbx = Art + "/Flak/Flak.fbx";
        // The 109's cockpit is narrow with high sills: a bigger wolf, raised a little, so his head and shoulders are out.
        static readonly TestWorldBuilder.Seat Bf109Seat = new TestWorldBuilder.Seat { scale = 1.55f, lift = -0.25f, back = 0f, railHalfWidth = 0.36f, railHeight = 0.48f, railForward = 0.22f };

        static void ImportPlane(string fbx, string metallicSmoothness)
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(fbx);
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.animationType = ModelImporterAnimationType.None;
            imp.importAnimation = false;
            imp.importNormals = ModelImporterNormals.Calculate;
            imp.normalSmoothingSource = ModelImporterNormalSmoothingSource.FromAngle;
            imp.normalSmoothingAngle = PlaneSmoothingAngle;
            imp.importTangents = ModelImporterTangents.CalculateMikk;
            imp.SaveAndReimport();
            if (metallicSmoothness == null) return;
            var ms = (TextureImporter)AssetImporter.GetAtPath(metallicSmoothness);
            if (ms.sRGBTexture) { ms.sRGBTexture = false; ms.SaveAndReimport(); }
        }

        static GameObject MakeBf109Prefab(TestWorldBuilder.Mats mats, Material pilotMat, RuntimeAnimatorController pilotCtrl)
        {
            ImportPlane(Bf109Fbx, Art + "/Bf109/Bf109_MetallicSmoothness.png");
            var body = TestWorldBuilder.Textured("Bf109", Art + "/Bf109/Bf109_BaseColor.png", Art + "/Bf109/Bf109_MetallicSmoothness.png");
            return MakeWolfFighter("Bf109", Bf109Fbx, "Bf109_Propeller", Bf109PrefabPath, Bf109Seat, mats, body, pilotMat, pilotCtrl);
        }

        /// <summary>The P-51 and its tiger for the wingmen: the test world's prefab without its free-flight parts, flown by a TurnAircraftView.</summary>
        static GameObject MakeP51TurnPrefab()
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/OODA/Prefabs/P51.prefab"));
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            go.name = "P51Turn";
            Object.DestroyImmediate(go.GetComponent<FlightHud>());
            Object.DestroyImmediate(go.GetComponent<AircraftController>());
            Object.DestroyImmediate(go.GetComponent<PlaneInput>());
            Object.DestroyImmediate(go.GetComponent<Rigidbody>());
            foreach (var s in go.GetComponentsInChildren<PropellerSpin>()) s.aircraft = null;      // they find the IFlightView above them
            foreach (var l in go.GetComponentsInChildren<PilotHeadLook>()) l.aircraft = null;
            go.AddComponent<TurnAircraftView>();
            var saved = PrefabUtility.SaveAsPrefabAsset(go, P51TurnPrefabPath);
            Object.DestroyImmediate(go);
            return saved;
        }

        /// <summary>A flak gun on each of AlpineWorld's flak sites, on the ground facing up the valley, solid on the Obstacles layer.</summary>
        static Transform[] PlaceFlak()
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(FlakFbx);
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.animationType = ModelImporterAnimationType.None;
            imp.importAnimation = false;
            imp.SaveAndReimport();
            var mat = TestWorldBuilder.Mat("Flak", new Color(0.24f, 0.28f, 0.22f), 0.25f, 0.2f);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(FlakFbx);
            int layer = AlpineWorld.ObstacleLayerIndex();
            var route = AlpineWorld.EscortRoute;
            var sites = GameObject.Find("Flak").transform.Cast<Transform>().OrderBy(t => int.Parse(t.name.Split(' ')[1])).ToList();
            var guns = new Transform[sites.Count];
            for (int i = 0; i < sites.Count; i++)
            {
                var site = sites[i];
                // up the valley: along the route leg nearest the site
                int leg = Enumerable.Range(0, route.Length - 1).OrderBy(k => DistToSegment(site.position, route[k], route[k + 1])).First();
                Vector3 up = route[leg + 1] - route[leg];
                var gun = (GameObject)PrefabUtility.InstantiatePrefab(model);
                gun.name = $"Flak {i}";
                gun.transform.SetParent(site, false);
                gun.transform.rotation = Quaternion.LookRotation(new Vector3(up.x, 0, up.z));
                foreach (var t in gun.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
                foreach (var r in gun.GetComponentsInChildren<Renderer>())
                {
                    r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) r.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                }
                guns[i] = gun.transform;
            }
            Debug.Log($"OODA: {guns.Length} flak guns placed: " + string.Join(", ", guns.Select(g => $"({g.position.x:F0}, {g.position.y:F0}, {g.position.z:F0}) facing {g.eulerAngles.y:F0}")) + ".");
            return guns;
        }

        static float DistToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector2 q = new Vector2(p.x, p.z), a2 = new Vector2(a.x, a.z), b2 = new Vector2(b.x, b.z);
            float u = Mathf.Clamp01(Vector2.Dot(q - a2, b2 - a2) / (b2 - a2).sqrMagnitude);
            return Vector2.Distance(q, Vector2.Lerp(a2, b2, u));
        }

        // ---------- the B-17 ----------

        const string B17Fbx = Art + "/B17/B17.fbx";
        const string B17PrefabPath = Root + "/Prefabs/B17.prefab";

        /// <summary>The bomber for the escort: no crew yet, four propellers each turned by its own PropellerSpin.</summary>
        static GameObject MakeB17Prefab()
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(B17Fbx);
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.animationType = ModelImporterAnimationType.None;
            imp.importAnimation = false;
            imp.importNormals = ModelImporterNormals.Calculate;
            imp.normalSmoothingSource = ModelImporterNormalSmoothingSource.FromAngle;
            imp.normalSmoothingAngle = PlaneSmoothingAngle;
            imp.importTangents = ModelImporterTangents.CalculateMikk;
            imp.SaveAndReimport();
            var ms = (TextureImporter)AssetImporter.GetAtPath(Art + "/B17/B17_MetallicSmoothness.png");
            if (ms.sRGBTexture) { ms.sRGBTexture = false; ms.SaveAndReimport(); }
            var body = TestWorldBuilder.Textured("B17", Art + "/B17/B17_BaseColor.png", Art + "/B17/B17_MetallicSmoothness.png");

            var root = new GameObject("B17");
            root.AddComponent<TurnAircraftView>();
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(B17Fbx));
            model.transform.SetParent(root.transform, false);
            foreach (var r in model.GetComponentsInChildren<Renderer>())
                r.sharedMaterials = Enumerable.Repeat(body, r.sharedMaterials.Length).ToArray();
            for (int i = 0; i < 4; i++)
            {
                var spin = root.AddComponent<PropellerSpin>();
                spin.propeller = TestWorldBuilder.FindDeep(model.transform, $"B17_Prop{i}");
                spin.maxRpm = 1400;   // the Cyclone's 2,500 rpm through the 0.5625 reduction gear
                if (spin.propeller == null) Debug.LogWarning($"OODA: no B17_Prop{i} in B17.fbx.");
            }
            float propZ = Enumerable.Range(0, 4).Select(i => TestWorldBuilder.FindDeep(model.transform, $"B17_Prop{i}")).Where(p => p != null).Average(p => p.position.z);
            var b = model.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
            Debug.Log($"OODA: B-17 prefab made: {b.size.x:F1} m span, {b.size.z:F1} m long, propellers {(propZ > b.center.z ? "ahead (+z)" : "behind (-z)")} of its middle, so the nose points {(propZ > b.center.z ? "+z like the Spitfire" : "-z: CHECK")}.");
            var saved = PrefabUtility.SaveAsPrefabAsset(root, B17PrefabPath);
            Object.DestroyImmediate(root);
            return saved;
        }

        static void MakeScene(GameObject enemyPrefab, GameObject wingmanPrefab, GameObject bomberPrefab)
        {
            AssetDatabase.DeleteAsset(ScenePath);
            AssetDatabase.CopyAsset(TestWorldBuilder.ScenePath, ScenePath);
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // The P-51: from free flight to a turn view
            var ac = Object.FindFirstObjectByType<AircraftController>();
            if (ac == null) { Debug.LogError("OODA: no P-51 in the test world; run OODA > Build Test World first."); return; }
            var p51 = ac.gameObject;
            if (PrefabUtility.IsPartOfPrefabInstance(p51))
                PrefabUtility.UnpackPrefabInstance(p51, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            Object.DestroyImmediate(p51.GetComponent<FlightHud>());
            Object.DestroyImmediate(p51.GetComponent<AircraftController>());
            Object.DestroyImmediate(p51.GetComponent<PlaneInput>());
            Object.DestroyImmediate(p51.GetComponent<Rigidbody>());
            var pSpin = p51.GetComponent<PropellerSpin>();
            if (pSpin != null) pSpin.aircraft = null;
            var pLook = p51.GetComponentInChildren<PilotHeadLook>();
            if (pLook != null) pLook.aircraft = null;
            var pView = p51.AddComponent<TurnAircraftView>();

            // the enemy the battle starts with: a Bf 109
            var enemy = (GameObject)PrefabUtility.InstantiatePrefab(enemyPrefab);
            enemy.transform.position = p51.transform.position + Vector3.forward * 3000f;

            // The camera
            var cam = Camera.main;
            var rig = cam.GetComponent<CameraRig>();
            if (rig != null) Object.DestroyImmediate(rig);
            var tc = cam.gameObject.AddComponent<TurnCamera>();
            tc.cockpitEye = TestWorldBuilder.FindDeep(p51.transform, "CockpitCamera");

            var go = new GameObject("TurnBattle");
            var tb = go.AddComponent<TurnBattle>();
            tb.player = pView;
            tb.enemy = enemy.GetComponent<TurnAircraftView>();
            tb.view = tc;
            tb.terrain = Object.FindFirstObjectByType<Terrain>();
            tb.playerPilot = pLook;
            tb.enemyPilot = enemy.GetComponentInChildren<PilotHeadLook>();
            // head-on up the main valley: under the peaks, 3 km apart with the valley open between them
            tb.startCentre = AlpineWorld.FightCentre;
            tb.startAltFt = AlpineWorld.FightAltFt;
            tb.startKias = 300;
            tb.startGapM = 3000;
            // the escort: the B-17 up the main valley
            var b17 = (GameObject)PrefabUtility.InstantiatePrefab(bomberPrefab);
            b17.transform.position = AlpineWorld.EscortRoute[0];
            tb.bomber = b17.GetComponent<TurnAircraftView>();
            tb.escortRoute = (Vector3[])AlpineWorld.EscortRoute.Clone();
            tb.bomberKias = 135;
            tb.squadronGapM = 8000;   // the 109s start 8 km up the valley: a few turns in formation before they arrive
            // the squadron battle: the rest of each side is made from these at the start
            tb.wingmanPrefab = wingmanPrefab;
            tb.enemyPrefab = enemyPrefab;
            tb.bomberPrefab = bomberPrefab;
            tb.flakSites = PlaceFlak();
            Physics.SyncTransforms();
            float clear = AlpineWorld.CheckEscortRoute(tb.terrain);
            if (clear < 350f) Debug.LogWarning($"OODA: the escort route comes within {clear:F0} m of the ground.");
            if (Object.FindFirstObjectByType<HazardMarkers>() == null)
                Debug.LogWarning("OODA: no air hazards (barrage balloons, cable cars, radio masts) in the test world: run OODA > Build Test World again, then this.");

            EditorSceneManager.SaveScene(scene, ScenePath);
            // The turn battle is the game; the free-flight world stays in the build as the sandbox
            var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
