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
        // 14 cm forward (back -0.14) so his right paw reaches the stick: his arm is shorter than the tiger's.
        static readonly TestWorldBuilder.Seat WolfSeat = new TestWorldBuilder.Seat { scale = 1.35f, lift = -0.55f, back = -0.14f, railHalfWidth = 0.37f, railHeight = 0.42f, railForward = 0.22f };
        const float PlaneSmoothingAngle = 80f;

        [MenuItem("OODA/Build Turn Battle")]
        public static void Build()
        {
            if (!File.Exists(TestWorldBuilder.ScenePath)) TestWorldBuilder.Build();
            ConfigureImports();
            PilotCast.Prepare();
            var mats = TestWorldBuilder.MakeMaterials();
            var spitMat = TestWorldBuilder.Textured("Spitfire", Art + "/Spitfire/Spit_BaseColor.png", Art + "/Spitfire/Spit_MetallicSmoothness.png");
            var wolfMat = TestWorldBuilder.Textured("WolfPilot", Art + "/WolfPilot/Wolf_BaseColor.png", Art + "/WolfPilot/Wolf_MetallicSmoothness.png", toon: true);
            var wolfCtrl = MakeSeatedAnimator(WolfFbx, "WolfPilot");
            var spit = MakeSpitfirePrefab(mats, spitMat, wolfMat, wolfCtrl);
            var bf109 = MakeBf109Prefab(mats, wolfMat, wolfCtrl);
            var b17 = MakeB17Prefab();
            var p51Turn = MakeP51TurnPrefab();
            var phantom = JetTestBuilder.MakeTurnPrefab();
            var p38 = MakeP38Prefab(mats);
            var zero = MakeZeroPrefab(mats);
            var f15 = MakeF15Prefab(mats);
            var su27 = MakeSu27Prefab(mats);
            MakeScene(bf109, p51Turn, b17, spit, phantom, p38, zero, f15, su27);
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

            TestWorldBuilder.ImportPilot(WolfFbx, "WolfPilot");

            foreach (var t in new[] { Art + "/Spitfire/Spit_MetallicSmoothness.png", Art + "/WolfPilot/Wolf_MetallicSmoothness.png" })
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(t);
                ti.sRGBTexture = false;
                ti.SaveAndReimport();
            }
        }

        static RuntimeAnimatorController MakeSeatedAnimator(string fbx, string name) => TestWorldBuilder.MakePilotController(fbx, name);

        static GameObject MakeSpitfirePrefab(TestWorldBuilder.Mats mats, Material body, Material pilotMat, RuntimeAnimatorController pilotCtrl) =>
            MakeWolfFighter("Spitfire", SpitFbx, "Spit_Propeller", SpitPrefabPath, WolfSeat, mats, body, pilotMat, pilotCtrl);

        /// <summary>An enemy fighter for the turn battle: the model with its canopy hidden, the wolf seated in it, and its propeller turning.</summary>
        static GameObject MakeWolfFighter(string name, string fbx, string propeller, string prefabPath, TestWorldBuilder.Seat seat,
                                          TestWorldBuilder.Mats mats, Material body, Material pilotMat, RuntimeAnimatorController pilotCtrl) =>
            MakeFighter(name, fbx, new[] { propeller }, prefabPath, seat, mats, body, WolfFbx, Ooda.Turn.Core.PilotKind.Wolf, pilotMat, pilotCtrl);

        /// <summary>A fighter for the turn battle: the model with its canopy hidden, its usual pilot seated in it (the others
        /// beside him, hidden), and each propeller turning (every one after the first the other way, for the P-38's pair).</summary>
        static GameObject MakeFighter(string name, string fbx, string[] propellers, string prefabPath, TestWorldBuilder.Seat seat,
                                      TestWorldBuilder.Mats mats, Material body, string pilotFbx, Ooda.Turn.Core.PilotKind usual,
                                      Material pilotMat, RuntimeAnimatorController pilotCtrl, float maxRpm = 1400,
                                      System.Action<GameObject, GameObject> extra = null)
        {
            var root = new GameObject(name);
            root.AddComponent<TurnAircraftView>();
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
            model.transform.SetParent(root.transform, false);
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                Material m = r.name.StartsWith("Pit_") ? TestWorldBuilder.CockpitMat() : r.name.Contains("Canopy") ? mats.canopy : r.name.Contains("Cockpit") ? mats.cockpit : body;
                if (r.name.StartsWith("Pit_")) r.shadowCastingMode = ShadowCastingMode.Off;
                r.sharedMaterials = Enumerable.Repeat(m, r.sharedMaterials.Length).ToArray();
                if (r.name.Contains("Canopy")) r.shadowCastingMode = ShadowCastingMode.Off;
            }

            for (int i = 0; i < propellers.Length; i++)
            {
                var spin = root.AddComponent<PropellerSpin>();
                spin.propeller = TestWorldBuilder.FindDeep(model.transform, propellers[i]);
                spin.maxRpm = maxRpm;
                spin.reverse = i > 0;
                if (spin.propeller == null) Debug.LogWarning($"OODA: no {propellers[i]} in {fbx}.");
            }
            root.AddComponent<ControlSurfaces>();   // ailerons, elevators and rudder swing with the flying

            var mount = TestWorldBuilder.FindDeep(model.transform, "PilotMount");
            if (mount == null) { Debug.LogWarning($"OODA: no PilotMount in {fbx}."); mount = root.transform; }
            var pilot = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(pilotFbx));
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
            TestWorldBuilder.AddPilotControls(pilot);
            PilotCast.AddRoster(root, pilot, usual);   // the other pilots too, hidden
            extra?.Invoke(root, model);

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
        // 20 cm forward so his right paw reaches the stick, and 8 cm up so his eyes clear the windscreen frame.
        static readonly TestWorldBuilder.Seat Bf109Seat = new TestWorldBuilder.Seat { scale = 1.55f, lift = -0.47f, back = -0.20f, railHalfWidth = 0.36f, railHeight = 0.48f, railForward = 0.22f };
        // Seated that far forward, the throttle is beside his hip and his left elbow swung out through the cockpit side: point it down and back.
        static readonly Vector3 Bf109ThrottleElbowHint = new Vector3(0f, -0.20f, -0.10f);

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
            var body = TestWorldBuilder.Textured("Bf109", Art + "/Bf109/Bf109_BaseColor.png", Art + "/Bf109/Bf109_MetallicSmoothness.png", toon: true);
            return MakeFighter("Bf109", Bf109Fbx, new[] { "Bf109_Propeller" }, Bf109PrefabPath, Bf109Seat, mats, body, WolfFbx, Ooda.Turn.Core.PilotKind.Wolf, pilotMat, pilotCtrl,
                extra: (root, model) =>
                {
                    foreach (var pc in root.GetComponentsInChildren<PilotControls>(true)) pc.throttleElbowHint = Bf109ThrottleElbowHint;   // every pilot in the roster sits there
                });
        }

        // ---------- the P-38 ----------

        const string P38Fbx = Art + "/P38/P38.fbx";
        const string P38PrefabPath = Root + "/Prefabs/P38.prefab";
        const string TigerFbx = Art + "/TigerPilot/TigerPilot.fbx";
        // The bubble is long and wide (Tools~/ArtPipeline/p38.py and build_cockpit.py P38): the tiger at the P-51's size,
        // a little lower, so his head and shoulders stand over the sill and his forearms lie on the rails.
        static readonly TestWorldBuilder.Seat P38Seat = new TestWorldBuilder.Seat { scale = 1.55f, lift = -0.40f, back = 0f, railHalfWidth = 0.47f, railHeight = 0.63f, railForward = 0.25f };

        /// <summary>The P-38 Lightning with the tiger in it, its two propellers turning outward at the top (the Allisons'
        /// 3,000 rpm through the 0.5 reduction gear). Null, with a warning, if its FBX isn't there.</summary>
        static GameObject MakeP38Prefab(TestWorldBuilder.Mats mats)
        {
            if (!File.Exists(P38Fbx)) { Debug.LogWarning($"OODA: no {P38Fbx}; the dogfight's P-38s show as P-51s (or Bf 109s on their side)."); return null; }
            ImportPlane(P38Fbx, Art + "/P38/P38_MetallicSmoothness.png");
            var body = TestWorldBuilder.Textured("P38", Art + "/P38/P38_BaseColor.png", Art + "/P38/P38_MetallicSmoothness.png");
            var tigerMat = TestWorldBuilder.Textured("TigerPilot", Art + "/TigerPilot/Pilot_BaseColor.png", Art + "/TigerPilot/Pilot_MetallicSmoothness.png", toon: true);
            var tigerCtrl = TestWorldBuilder.MakePilotController(TigerFbx, "TigerPilot");
            return MakeFighter("P38", P38Fbx, new[] { "P38_PropR", "P38_PropL" }, P38PrefabPath, P38Seat, mats, body,
                               TigerFbx, Ooda.Turn.Core.PilotKind.Tiger, tigerMat, tigerCtrl, maxRpm: 1500);
        }

        // ---------- the Zero ----------

        const string ZeroFbx = Art + "/Zero/Zero.fbx";
        const string ZeroPrefabPath = Root + "/Prefabs/Zero.prefab";
        const string FoxFbx = Art + "/FoxPilot/FoxPilot.fbx";
        // A roomy cartoon greenhouse (Tools~/ArtPipeline/zero.py and build_cockpit.py Zero): the pilot at the P-51's size,
        // seated as in the P-38, the rails a little narrower and lower on the Zero's lower sill.
        static readonly TestWorldBuilder.Seat ZeroSeat = new TestWorldBuilder.Seat { scale = 1.55f, lift = -0.40f, back = 0f, railHalfWidth = 0.45f, railHeight = 0.58f, railForward = 0.25f };

        /// <summary>The A6M2 Zero with the fox in it (the koi and the others beside her, hidden), its three-blade propeller
        /// turning (the Sakae's 2,550 rpm through the 0.58 reduction gear). Null, with a warning, if its FBX isn't there.</summary>
        static GameObject MakeZeroPrefab(TestWorldBuilder.Mats mats)
        {
            if (!File.Exists(ZeroFbx)) { Debug.LogWarning($"OODA: no {ZeroFbx}; the dogfight's Zeros show as P-51s (or Bf 109s on their side)."); return null; }
            ImportPlane(ZeroFbx, Art + "/Zero/Zero_MetallicSmoothness.png");
            var body = TestWorldBuilder.Textured("Zero", Art + "/Zero/Zero_BaseColor.png", Art + "/Zero/Zero_MetallicSmoothness.png");
            var foxMat = TestWorldBuilder.Textured("FoxPilot", Art + "/FoxPilot/Fox_BaseColor.png", Art + "/FoxPilot/Fox_MetallicSmoothness.png", toon: true);
            var foxCtrl = TestWorldBuilder.MakePilotController(FoxFbx, "FoxPilot");
            return MakeFighter("Zero", ZeroFbx, new[] { "Zero_Propeller" }, ZeroPrefabPath, ZeroSeat, mats, body,
                               FoxFbx, Ooda.Turn.Core.PilotKind.Fox, foxMat, foxCtrl, maxRpm: 1480);
        }

        // ---------- the F-15 ----------

        const string F15Fbx = Art + "/F15/F15.fbx";
        const string F15PrefabPath = Root + "/Prefabs/F15.prefab";
        // The two-seat bubble opened whole (Tools~/ArtPipeline/f15.py and build_cockpit.py F15): the pilot under its front
        // half, a little smaller than in the Zero for the jet's narrower cockpit, head and shoulders over the sill.
        static readonly TestWorldBuilder.Seat F15Seat = new TestWorldBuilder.Seat { scale = 1.35f, lift = -0.40f, back = 0f, railHalfWidth = 0.40f, railHeight = 0.50f, railForward = 0.22f };

        /// <summary>The F-15C Eagle with the tiger in it (the others beside him, hidden): no propeller, its afterburner
        /// flames at the model's NozzleL and NozzleR, lit with its WEP. Null, with a warning, if its FBX isn't there.</summary>
        static GameObject MakeF15Prefab(TestWorldBuilder.Mats mats)
        {
            if (!File.Exists(F15Fbx)) { Debug.LogWarning($"OODA: no {F15Fbx}; the dogfight's F-15s show as P-51s (or Bf 109s on their side)."); return null; }
            ImportPlane(F15Fbx, Art + "/F15/F15_MetallicSmoothness.png");
            var body = TestWorldBuilder.Textured("F15", Art + "/F15/F15_BaseColor.png", Art + "/F15/F15_MetallicSmoothness.png");
            var tigerMat = TestWorldBuilder.Textured("TigerPilot", Art + "/TigerPilot/Pilot_BaseColor.png", Art + "/TigerPilot/Pilot_MetallicSmoothness.png", toon: true);
            var tigerCtrl = TestWorldBuilder.MakePilotController(TigerFbx, "TigerPilot");
            return MakeFighter("F15", F15Fbx, new string[0], F15PrefabPath, F15Seat, mats, body,
                               TigerFbx, Ooda.Turn.Core.PilotKind.Tiger, tigerMat, tigerCtrl, extra: (root, model) =>
            {
                var burner = root.AddComponent<JetBurner>();   // it finds the TurnAircraftView itself
                var nozzles = new[] { "NozzleL", "NozzleR" }.Select(n => TestWorldBuilder.FindDeep(model.transform, n)).Where(n => n != null).ToArray();
                if (nozzles.Length == 2) burner.nozzles = nozzles.Select(n => root.transform.InverseTransformPoint(n.position)).ToArray();
                else Debug.LogWarning("OODA: no NozzleL and NozzleR in F15.fbx; the burner flames sit where the F-4's do.");
                burner.nozzleRadius = 0.42f;
                burner.burnerLength = 9f;
                Debug.Log("OODA: the F-15's nozzles at " + string.Join(", ", burner.nozzles.Select(n => $"({n.x:F2}, {n.y:F2}, {n.z:F2})")) + ".");
            });
        }

        // ---------- the Su-27 ----------

        const string Su27Fbx = Art + "/Su27/Su27.fbx";
        const string Su27PrefabPath = Root + "/Prefabs/Su27.prefab";
        const string BearFbx = Art + "/BearPilot/BearPilot.fbx";
        // The bubble opened whole (Tools~/ArtPipeline/su27.py and build_cockpit.py Su27), seated as in the F-15: the pilot
        // under its front half, head and shoulders over the sill, the rails a little wider for the Flanker's wider cockpit.
        static readonly TestWorldBuilder.Seat Su27Seat = new TestWorldBuilder.Seat { scale = 1.35f, lift = -0.40f, back = 0f, railHalfWidth = 0.42f, railHeight = 0.50f, railForward = 0.22f };

        /// <summary>The Su-27 Flanker with the bear in it (the others beside him, hidden): no propeller, its afterburner
        /// flames at the model's NozzleL and NozzleR, lit with its WEP. Null, with a warning, if its FBX isn't there.</summary>
        static GameObject MakeSu27Prefab(TestWorldBuilder.Mats mats)
        {
            if (!File.Exists(Su27Fbx)) { Debug.LogWarning($"OODA: no {Su27Fbx}; the dogfight's Su-27s show as P-51s (or Bf 109s on their side)."); return null; }
            ImportPlane(Su27Fbx, Art + "/Su27/Su27_MetallicSmoothness.png");
            var body = TestWorldBuilder.Textured("Su27", Art + "/Su27/Su27_BaseColor.png", Art + "/Su27/Su27_MetallicSmoothness.png");
            var bearMat = TestWorldBuilder.Textured("BearPilot", Art + "/BearPilot/Bear_BaseColor.png", Art + "/BearPilot/Bear_MetallicSmoothness.png", toon: true);
            var bearCtrl = TestWorldBuilder.MakePilotController(BearFbx, "BearPilot");
            return MakeFighter("Su27", Su27Fbx, new string[0], Su27PrefabPath, Su27Seat, mats, body,
                               BearFbx, Ooda.Turn.Core.PilotKind.Bear, bearMat, bearCtrl, extra: (root, model) =>
            {
                var burner = root.AddComponent<JetBurner>();   // it finds the TurnAircraftView itself
                var nozzles = new[] { "NozzleL", "NozzleR" }.Select(n => TestWorldBuilder.FindDeep(model.transform, n)).Where(n => n != null).ToArray();
                if (nozzles.Length == 2) burner.nozzles = nozzles.Select(n => root.transform.InverseTransformPoint(n.position)).ToArray();
                else Debug.LogWarning("OODA: no NozzleL and NozzleR in Su27.fbx; the burner flames sit where the F-4's do.");
                burner.nozzleRadius = 0.45f;
                burner.burnerLength = 9.5f;
                Debug.Log("OODA: the Su-27's nozzles at " + string.Join(", ", burner.nozzles.Select(n => $"({n.x:F2}, {n.y:F2}, {n.z:F2})")) + ".");
            });
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
            // painted and shaded now (field grey, gunmetal barrels, black tyres); the map's smoothness and metal do the rest
            var ms = (TextureImporter)AssetImporter.GetAtPath(Art + "/Flak/Flak_MetallicSmoothness.png");
            if (ms != null && ms.sRGBTexture) { ms.sRGBTexture = false; ms.SaveAndReimport(); }
            var mat = TestWorldBuilder.Textured("Flak", Art + "/Flak/Flak_BaseColor.png", Art + "/Flak/Flak_MetallicSmoothness.png");
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

        static void MakeScene(GameObject enemyPrefab, GameObject wingmanPrefab, GameObject bomberPrefab, GameObject spitfirePrefab, GameObject phantomPrefab, GameObject p38Prefab, GameObject zeroPrefab, GameObject f15Prefab, GameObject su27Prefab)
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
            tb.valleyRunStartM = AlpineWorld.ValleyRunStartM;
            tb.bomberKias = 135;
            tb.squadronGapM = 8000;   // the 109s start 8 km up the valley: a few turns in formation before they arrive
            // the squadron battle: the rest of each side is made from these at the start
            tb.wingmanPrefab = wingmanPrefab;
            tb.enemyPrefab = enemyPrefab;
            tb.bomberPrefab = bomberPrefab;
            // the dogfight's setup screen can put any of the eight types on either side
            tb.spitfirePrefab = spitfirePrefab;
            tb.phantomPrefab = phantomPrefab;
            tb.p38Prefab = p38Prefab;
            tb.zeroPrefab = zeroPrefab;
            tb.f15Prefab = f15Prefab;
            tb.su27Prefab = su27Prefab;
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
