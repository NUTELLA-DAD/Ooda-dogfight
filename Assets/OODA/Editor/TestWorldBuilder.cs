using System.IO;
using System.Linq;
using Ooda.CameraSystem;
using Ooda.Flight;
using Ooda.Pilot;
using Ooda.UI;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Ooda.EditorTools
{
    /// <summary>
    /// OODA > Build Test World: sets up URP if the project has no pipeline yet, the import settings for the
    /// P-51 and the tiger pilot, their materials, the pilot's animator, the P-51 prefab, and a test scene
    /// (island terrain, sea, clouds, towers, sun, the camera rig). Safe to run again: it rebuilds what it made.
    /// </summary>
    public static class TestWorldBuilder
    {
        const string Root = "Assets/OODA";
        const string Art = Root + "/Art";
        const string Gen = Root + "/Generated";
        const string P51Fbx = Art + "/P51/P51.fbx";
        const string PilotFbx = Art + "/TigerPilot/TigerPilot.fbx";
        const string ScenePath = Root + "/Scenes/TestWorld.unity";
        const string PrefabPath = Root + "/Prefabs/P51.prefab";

        const float PilotScale = 1.2f;
        const float WorldSize = 20000f, WorldHeight = 1400f, SeaFloor = -80f;

        [MenuItem("OODA/Build Test World")]
        public static void Build()
        {
            foreach (var d in new[] { Gen, Root + "/Scenes", Root + "/Prefabs", Root + "/Settings" })
                Directory.CreateDirectory(d);
            EnsureUrp();
            ConfigureImports();
            var mats = MakeMaterials();
            var controller = MakePilotAnimator();
            var prefab = MakePlanePrefab(mats, controller);
            MakeScene(prefab);
            AssetDatabase.SaveAssets();
            Debug.Log("OODA: test world built. Press Play.");
        }

        // ---------- URP ----------

        static void EnsureUrp()
        {
            if (GraphicsSettings.defaultRenderPipeline != null) return;
            var data = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(data, Root + "/Settings/OODA_Renderer.asset");
            var urp = UniversalRenderPipelineAsset.Create(data);
            AssetDatabase.CreateAsset(urp, Root + "/Settings/OODA_URP.asset");
            GraphicsSettings.defaultRenderPipeline = urp;
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urp;
            }
            Debug.Log("OODA: created a URP asset and set it as the render pipeline.");
        }

        // ---------- imports ----------

        static void ConfigureImports()
        {
            var plane = (ModelImporter)AssetImporter.GetAtPath(P51Fbx);
            plane.materialImportMode = ModelImporterMaterialImportMode.None;
            plane.animationType = ModelImporterAnimationType.None;
            plane.importAnimation = false;
            plane.SaveAndReimport();

            var pilot = (ModelImporter)AssetImporter.GetAtPath(PilotFbx);
            pilot.materialImportMode = ModelImporterMaterialImportMode.None;
            pilot.animationType = ModelImporterAnimationType.Human;
            pilot.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            pilot.importAnimation = true;
            var clips = pilot.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.name = "Seated";
                c.loopTime = true;
                c.lockRootRotation = true;
                c.lockRootHeightY = true;
                c.lockRootPositionXZ = true;
                c.keepOriginalOrientation = true;
                c.keepOriginalPositionY = true;
                c.keepOriginalPositionXZ = true;
            }
            pilot.clipAnimations = clips;
            pilot.SaveAndReimport();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(PilotFbx).OfType<Avatar>().FirstOrDefault();
            if (avatar != null && avatar.isValid && avatar.isHuman) Debug.Log("OODA: TigerPilot avatar is valid (Humanoid).");
            else Debug.LogError($"OODA: TigerPilot avatar is not a valid humanoid (found: {avatar != null}, valid: {avatar?.isValid}, human: {avatar?.isHuman}).");

            foreach (var t in new[] { Art + "/P51/P51_MetallicSmoothness.png", Art + "/TigerPilot/Pilot_MetallicSmoothness.png" })
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(t);
                ti.sRGBTexture = false;
                ti.SaveAndReimport();
            }
        }

        // ---------- materials ----------

        class Mats { public Material plane, canopy, cockpit, pilot, terrain, water, cloud, towerRed, towerWhite; }

        static Shader Lit => Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        static Material Mat(string name, Color color, float smooth = 0.3f, float metal = 0f)
        {
            string path = $"{Gen}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Lit); AssetDatabase.CreateAsset(m, path); }
            m.shader = Lit;
            m.SetColor("_BaseColor", color);
            m.SetColor("_Color", color);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Textured(string name, string baseMap, string metalSmooth)
        {
            var m = Mat(name, Color.white, 1f, 1f);
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(baseMap));
            m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(baseMap));
            m.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(metalSmooth));
            m.SetFloat("_SmoothnessTextureChannel", 0);
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            return m;
        }

        static void MakeTransparent(Material m)
        {
            m.SetFloat("_Surface", 1);
            m.SetFloat("_Blend", 0);
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        static Mats MakeMaterials()
        {
            var r = new Mats
            {
                plane = Textured("P51", Art + "/P51/P51_BaseColor.png", Art + "/P51/P51_MetallicSmoothness.png"),
                pilot = Textured("TigerPilot", Art + "/TigerPilot/Pilot_BaseColor.png", Art + "/TigerPilot/Pilot_MetallicSmoothness.png"),
                canopy = Mat("P51_CanopyGlass", new Color(0.85f, 0.92f, 1f, 0.22f), 0.95f),
                cockpit = Mat("P51_Cockpit", new Color(0.13f, 0.15f, 0.10f), 0.2f),
                water = Mat("Sea", new Color(0.08f, 0.22f, 0.35f), 0.85f),
                cloud = Mat("Cloud", new Color(1f, 1f, 1f), 0f),
                towerRed = Mat("TowerRed", new Color(0.8f, 0.12f, 0.1f), 0.3f),
                towerWhite = Mat("TowerWhite", new Color(0.92f, 0.92f, 0.9f), 0.3f),
            };
            r.canopy.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "/P51/P51_BaseColor.png"));
            MakeTransparent(r.canopy);
            r.cloud.SetColor("_EmissionColor", new Color(0.35f, 0.35f, 0.38f));
            r.cloud.EnableKeyword("_EMISSION");

            var terrainShader = Shader.Find("Universal Render Pipeline/Terrain/Lit") ?? Shader.Find("Nature/Terrain/Standard");
            string tp = $"{Gen}/Terrain.mat";
            r.terrain = AssetDatabase.LoadAssetAtPath<Material>(tp);
            if (r.terrain == null) { r.terrain = new Material(terrainShader); AssetDatabase.CreateAsset(r.terrain, tp); }
            return r;
        }

        // ---------- pilot animator ----------

        static RuntimeAnimatorController MakePilotAnimator()
        {
            string path = $"{Gen}/TigerPilot.controller";
            AssetDatabase.DeleteAsset(path);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            var clip = AssetDatabase.LoadAllAssetsAtPath(PilotFbx).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
            var layers = ctrl.layers;
            layers[0].iKPass = true; // the head look needs it
            ctrl.layers = layers;
            var state = ctrl.layers[0].stateMachine.AddState("Seated");
            state.motion = clip;
            if (clip == null) Debug.LogWarning("OODA: no Seated clip found in TigerPilot.fbx.");
            return ctrl;
        }

        // ---------- plane prefab ----------

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var f = FindDeep(c, name); if (f != null) return f; }
            return null;
        }

        static GameObject MakePlanePrefab(Mats mats, RuntimeAnimatorController pilotCtrl)
        {
            var root = new GameObject("P51");
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            root.AddComponent<PlaneInput>();
            var ac = root.AddComponent<AircraftController>();

            string perfPath = Root + "/Settings/P51D.asset";
            var perf = AssetDatabase.LoadAssetAtPath<AircraftPerformance>(perfPath);
            if (perf == null) { perf = ScriptableObject.CreateInstance<AircraftPerformance>(); AssetDatabase.CreateAsset(perf, perfPath); }
            ac.performance = perf;

            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(P51Fbx));
            model.transform.SetParent(root.transform, false);
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                Material m = r.name.Contains("Canopy") ? mats.canopy : r.name.Contains("Cockpit") ? mats.cockpit : mats.plane;
                r.sharedMaterials = Enumerable.Repeat(m, r.sharedMaterials.Length).ToArray();
                if (r.name.Contains("Canopy")) r.shadowCastingMode = ShadowCastingMode.Off;
            }

            var spin = root.AddComponent<PropellerSpin>();
            spin.aircraft = ac;
            spin.propeller = FindDeep(model.transform, "P51_Propeller");

            var mount = FindDeep(model.transform, "PilotMount");
            if (mount == null) { Debug.LogWarning("OODA: no PilotMount in P51.fbx."); mount = root.transform; }
            var pilot = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PilotFbx));
            pilot.transform.SetParent(mount, false);
            pilot.transform.localPosition = Vector3.zero;
            pilot.transform.localRotation = Quaternion.identity;
            pilot.transform.localScale = Vector3.one * PilotScale;
            foreach (var r in pilot.GetComponentsInChildren<Renderer>())
                r.sharedMaterials = Enumerable.Repeat(mats.pilot, r.sharedMaterials.Length).ToArray();
            var anim = pilot.GetComponent<Animator>();
            if (anim == null) anim = pilot.AddComponent<Animator>();
            anim.runtimeAnimatorController = pilotCtrl;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var look = pilot.AddComponent<PilotHeadLook>();
            look.aircraft = ac;

            var hud = root.AddComponent<FlightHud>();
            hud.aircraft = ac;

            var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return saved;
        }

        // ---------- scene ----------

        static void MakeScene(GameObject planePrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var mats = MakeMaterials();

            var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).First(l => l.type == LightType.Directional);
            sun.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
            sun.intensity = 1.3f;
            sun.shadows = LightShadows.Soft;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 2500f;
            RenderSettings.fogEndDistance = 22000f;
            RenderSettings.fogColor = new Color(0.66f, 0.76f, 0.86f);

            var terrain = MakeTerrain(mats);
            MakeSea(mats);
            MakeClouds(mats);
            MakeTowers(mats, terrain);

            var plane = (GameObject)PrefabUtility.InstantiatePrefab(planePrefab);
            plane.transform.SetPositionAndRotation(new Vector3(0, 1500f, -6000f), Quaternion.identity);

            var cam = Camera.main;
            cam.transform.position = plane.transform.position - Vector3.forward * 16f + Vector3.up * 4f;
            var rig = cam.gameObject.AddComponent<CameraRig>();
            rig.aircraft = plane.GetComponent<AircraftController>();
            rig.cockpitEye = FindDeep(plane.transform, "CockpitCamera");
            rig.pilot = plane.GetComponentInChildren<PilotHeadLook>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        static Terrain MakeTerrain(Mats mats)
        {
            const int res = 513;
            var data = new TerrainData { heightmapResolution = res };
            data.size = new Vector3(WorldSize, WorldHeight, WorldSize);
            var h = new float[res, res];
            float sea = -SeaFloor / WorldHeight;
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    float u = x / (res - 1f), v = y / (res - 1f);
                    float n = 0, amp = 1, freq = 3, norm = 0;
                    for (int o = 0; o < 5; o++) { n += amp * Mathf.PerlinNoise(u * freq + 13.1f, v * freq + 7.7f); norm += amp; amp *= 0.5f; freq *= 2.1f; }
                    n /= norm;
                    float dist = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f; // 0 centre, 1 edge
                    float island = Mathf.Clamp01(1.25f - dist * 1.4f);
                    float ridges = Mathf.Pow(Mathf.Clamp01((n - 0.35f) * 1.8f), 1.6f);
                    h[y, x] = Mathf.Clamp01(sea * 0.6f + island * (0.06f + 0.75f * ridges));
                }
            data.SetHeights(0, 0, h);

            var grass = MakeNoiseTexture("Grass", new Color(0.30f, 0.42f, 0.20f), new Color(0.42f, 0.48f, 0.25f));
            var layer = new TerrainLayer { diffuseTexture = grass, tileSize = new Vector2(60, 60) };
            AssetDatabase.CreateAsset(layer, $"{Gen}/Grass.terrainlayer");
            data.terrainLayers = new[] { layer };
            AssetDatabase.CreateAsset(data, $"{Gen}/TestWorld_Terrain.asset");

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Island";
            go.transform.position = new Vector3(-WorldSize / 2, SeaFloor, -WorldSize / 2);
            var t = go.GetComponent<Terrain>();
            t.materialTemplate = mats.terrain;
            t.heightmapPixelError = 4;
            t.basemapDistance = 4000;
            return t;
        }

        static Texture2D MakeNoiseTexture(string name, Color a, Color b)
        {
            string path = $"{Gen}/{name}.png";
            var tex = new Texture2D(256, 256, TextureFormat.RGB24, true);
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 256; x++)
                {
                    float n = Mathf.PerlinNoise(x * 0.05f, y * 0.05f) * 0.6f + Mathf.PerlinNoise(x * 0.31f, y * 0.29f) * 0.4f;
                    tex.SetPixel(x, y, Color.Lerp(a, b, n));
                }
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void MakeSea(Mats mats)
        {
            var sea = GameObject.CreatePrimitive(PrimitiveType.Plane);
            sea.name = "Sea";
            sea.transform.position = Vector3.zero;
            sea.transform.localScale = new Vector3(10000f, 1f, 10000f); // 100 km
            sea.GetComponent<Renderer>().sharedMaterial = mats.water;
            sea.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        static void MakeClouds(Mats mats)
        {
            var parent = new GameObject("Clouds").transform;
            var rnd = new System.Random(7);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            for (int i = 0; i < 160; i++)
            {
                var cloud = new GameObject($"Cloud {i}").transform;
                cloud.SetParent(parent);
                cloud.position = new Vector3(R(-15000, 15000), R(1500, 3200), R(-15000, 15000));
                int puffs = rnd.Next(4, 8);
                for (int p = 0; p < puffs; p++)
                {
                    var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Object.DestroyImmediate(s.GetComponent<Collider>()); // fly through clouds
                    s.transform.SetParent(cloud, false);
                    float size = R(90, 260);
                    s.transform.localPosition = new Vector3(R(-250, 250), R(-30, 60), R(-180, 180));
                    s.transform.localScale = new Vector3(size * 1.4f, size * 0.7f, size);
                    var rend = s.GetComponent<Renderer>();
                    rend.sharedMaterial = mats.cloud;
                    rend.shadowCastingMode = ShadowCastingMode.Off;
                }
            }
        }

        static void MakeTowers(Mats mats, Terrain terrain)
        {
            var parent = new GameObject("Towers").transform;
            var rnd = new System.Random(11);
            int made = 0;
            for (int tries = 0; tries < 400 && made < 30; tries++)
            {
                float x = (float)(rnd.NextDouble() - 0.5) * WorldSize * 0.6f, z = (float)(rnd.NextDouble() - 0.5) * WorldSize * 0.6f;
                float ground = terrain.SampleHeight(new Vector3(x, 0, z)) + terrain.transform.position.y;
                if (ground < 5f) continue; // on land only
                var tower = new GameObject($"Tower {made}").transform;
                tower.SetParent(parent);
                tower.position = new Vector3(x, ground, z);
                float height = 60f + (float)rnd.NextDouble() * 140f;
                int bands = 6;
                for (int b = 0; b < bands; b++)
                {
                    var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    c.transform.SetParent(tower, false);
                    c.transform.localScale = new Vector3(14f, height / bands, 14f);
                    c.transform.localPosition = new Vector3(0, height / bands * (b + 0.5f), 0);
                    c.GetComponent<Renderer>().sharedMaterial = b % 2 == 0 ? mats.towerRed : mats.towerWhite;
                }
                made++;
            }
        }
    }
}
