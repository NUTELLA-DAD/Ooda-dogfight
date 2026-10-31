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
            var spit = MakeSpitfirePrefab(mats, spitMat, wolfMat, wolfCtrl);
            MakeB17Prefab();
            MakeScene(spit);
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

        static GameObject MakeSpitfirePrefab(TestWorldBuilder.Mats mats, Material body, Material pilotMat, RuntimeAnimatorController pilotCtrl)
        {
            var root = new GameObject("Spitfire");
            root.AddComponent<TurnAircraftView>();
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SpitFbx));
            model.transform.SetParent(root.transform, false);
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                Material m = r.name.Contains("Canopy") ? mats.canopy : r.name.Contains("Cockpit") ? mats.cockpit : body;
                r.sharedMaterials = Enumerable.Repeat(m, r.sharedMaterials.Length).ToArray();
                if (r.name.Contains("Canopy")) r.shadowCastingMode = ShadowCastingMode.Off;
            }

            var spin = root.AddComponent<PropellerSpin>();
            spin.propeller = TestWorldBuilder.FindDeep(model.transform, "Spit_Propeller");
            if (spin.propeller == null) Debug.LogWarning("OODA: no Spit_Propeller in Spitfire.fbx.");

            var mount = TestWorldBuilder.FindDeep(model.transform, "PilotMount");
            if (mount == null) { Debug.LogWarning("OODA: no PilotMount in Spitfire.fbx."); mount = root.transform; }
            var pilot = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(WolfFbx));
            TestWorldBuilder.HideCanopy(model);
            TestWorldBuilder.SeatPilot(pilot, mount, WolfSeat);
            foreach (var r in pilot.GetComponentsInChildren<Renderer>())
                r.sharedMaterials = Enumerable.Repeat(pilotMat, r.sharedMaterials.Length).ToArray();
            var anim = pilot.GetComponent<Animator>();
            if (anim == null) anim = pilot.AddComponent<Animator>();
            anim.runtimeAnimatorController = pilotCtrl;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            pilot.AddComponent<PilotHeadLook>();

            var saved = PrefabUtility.SaveAsPrefabAsset(root, SpitPrefabPath);
            Object.DestroyImmediate(root);
            return saved;
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

        static void MakeScene(GameObject spitPrefab)
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

            var spit = (GameObject)PrefabUtility.InstantiatePrefab(spitPrefab);
            spit.transform.position = p51.transform.position + Vector3.forward * 3000f;

            // The camera
            var cam = Camera.main;
            var rig = cam.GetComponent<CameraRig>();
            if (rig != null) Object.DestroyImmediate(rig);
            var tc = cam.gameObject.AddComponent<TurnCamera>();
            tc.cockpitEye = TestWorldBuilder.FindDeep(p51.transform, "CockpitCamera");

            var go = new GameObject("TurnBattle");
            var tb = go.AddComponent<TurnBattle>();
            tb.player = pView;
            tb.enemy = spit.GetComponent<TurnAircraftView>();
            tb.view = tc;
            tb.terrain = Object.FindFirstObjectByType<Terrain>();
            tb.playerPilot = pLook;
            tb.enemyPilot = spit.GetComponentInChildren<PilotHeadLook>();
            // head-on up the main valley: under the peaks, 3 km apart with the valley open between them
            tb.startCentre = AlpineWorld.FightCentre;
            tb.startAltFt = AlpineWorld.FightAltFt;
            tb.startKias = 300;
            tb.startGapM = 3000;
            // the escort: the B-17 up the main valley
            var b17 = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(B17PrefabPath));
            b17.transform.position = AlpineWorld.EscortRoute[0];
            tb.bomber = b17.GetComponent<TurnAircraftView>();
            tb.escortRoute = (Vector3[])AlpineWorld.EscortRoute.Clone();
            Physics.SyncTransforms();
            float clear = AlpineWorld.CheckEscortRoute(tb.terrain);
            if (clear < 350f) Debug.LogWarning($"OODA: the escort route comes within {clear:F0} m of the ground.");

            EditorSceneManager.SaveScene(scene, ScenePath);
            // The turn battle is the game; the free-flight world stays in the build as the sandbox
            var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
