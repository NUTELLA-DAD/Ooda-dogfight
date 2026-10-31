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

        const float WolfScale = 0.82f;          // fitted under the Spitfire's canopy in Blender
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
            pilot.transform.SetParent(mount, false);
            pilot.transform.localPosition = Vector3.zero;
            pilot.transform.localRotation = Quaternion.identity;
            pilot.transform.localScale = Vector3.one * WolfScale;
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

            EditorSceneManager.SaveScene(scene, ScenePath);
            // The turn battle is the game; the free-flight world stays in the build as the sandbox
            var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
