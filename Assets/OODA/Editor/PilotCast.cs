using System.IO;
using System.Linq;
using Ooda.Pilot;
using Ooda.Turn.Core;
using UnityEditor;
using UnityEngine;

namespace Ooda.EditorTools
{
    /// <summary>
    /// Every pilot in every fighter: the tiger, the wolf, the wizard, the skeleton, the bear, the fox, the koi, the lion, the penguin and the spectre share one rig (the wolf's, fitted
    /// to each) and one set of clips, so each sits in a cockpit the same way. A fighter's builder seats its usual pilot;
    /// AddRoster then seats the others in the same place, on the same rails, inactive, and puts a PilotRoster on the
    /// plane that shows the one the dogfight briefing picks. The wizard's, skeleton's, bear's, fox's, koi's, lion's, penguin's and spectre's FBXs are made from the Hi3D
    /// models by Tools~/ArtPipeline/rig.py and pilot_anims.py.
    /// </summary>
    internal static class PilotCast
    {
        const string Art = "Assets/OODA/Art";
        const string Gen = "Assets/OODA/Generated";

        struct Who { public PilotKind kind; public string fbx, label, baseMap, metalSmooth, emission; }

        static readonly Who[] All =
        {
            new Who { kind = PilotKind.Tiger, label = "TigerPilot", fbx = Art + "/TigerPilot/TigerPilot.fbx", baseMap = Art + "/TigerPilot/Pilot_BaseColor.png", metalSmooth = Art + "/TigerPilot/Pilot_MetallicSmoothness.png" },
            new Who { kind = PilotKind.Wolf, label = "WolfPilot", fbx = Art + "/WolfPilot/WolfPilot.fbx", baseMap = Art + "/WolfPilot/Wolf_BaseColor.png", metalSmooth = Art + "/WolfPilot/Wolf_MetallicSmoothness.png" },
            new Who { kind = PilotKind.Wizard, label = "WizardPilot", fbx = Art + "/WizardPilot/WizardPilot.fbx", baseMap = Art + "/WizardPilot/Wizard_BaseColor.png", metalSmooth = Art + "/WizardPilot/Wizard_MetallicSmoothness.png" },
            new Who { kind = PilotKind.Skeleton, label = "SkeletonPilot", fbx = Art + "/SkeletonPilot/SkeletonPilot.fbx", baseMap = Art + "/SkeletonPilot/Skeleton_BaseColor.png", metalSmooth = Art + "/SkeletonPilot/Skeleton_MetallicSmoothness.png" },
            new Who { kind = PilotKind.Bear, label = "BearPilot", fbx = Art + "/BearPilot/BearPilot.fbx", baseMap = Art + "/BearPilot/Bear_BaseColor.png", metalSmooth = Art + "/BearPilot/Bear_MetallicSmoothness.png" },
            new Who { kind = PilotKind.Fox, label = "FoxPilot", fbx = Art + "/FoxPilot/FoxPilot.fbx", baseMap = Art + "/FoxPilot/Fox_BaseColor.png", metalSmooth = Art + "/FoxPilot/Fox_MetallicSmoothness.png" },
            new Who { kind = PilotKind.Koi, label = "KoiPilot", fbx = Art + "/KoiPilot/KoiPilot.fbx", baseMap = Art + "/KoiPilot/Koi_BaseColor.png", metalSmooth = Art + "/KoiPilot/Koi_MetallicSmoothness.png" },
            new Who { kind = PilotKind.Lion, label = "LionPilot", fbx = Art + "/LionPilot/LionPilot.fbx", baseMap = Art + "/LionPilot/Lion_BaseColor.png", metalSmooth = Art + "/LionPilot/Lion_MetallicSmoothness.png" },
            new Who { kind = PilotKind.Penguin, label = "PenguinPilot", fbx = Art + "/PenguinPilot/PenguinPilot.fbx", baseMap = Art + "/PenguinPilot/Penguin_BaseColor.png", metalSmooth = Art + "/PenguinPilot/Penguin_MetallicSmoothness.png" },
            new Who { kind = PilotKind.Spectre, label = "SpectrePilot", fbx = Art + "/SpectrePilot/SpectrePilot.fbx", baseMap = Art + "/SpectrePilot/Spectre_BaseColor.png", metalSmooth = Art + "/SpectrePilot/Spectre_MetallicSmoothness.png",
                      emission = Art + "/SpectrePilot/Spectre_Emission.png" },
        };

        static bool imported;

        /// <summary>Each pilot imported as a Humanoid with its clips, its metal-smoothness map linear, and its animator made
        /// (once a build: the same controller asset is kept, so prefabs made earlier still point at it).</summary>
        internal static void Prepare()
        {
            if (imported) return;
            foreach (var w in All)
            {
                if (!File.Exists(w.fbx)) { Debug.LogWarning($"OODA: no {w.fbx}; the {w.kind} can't fly."); continue; }
                TestWorldBuilder.ImportPilot(w.fbx, w.label);
                var ti = (TextureImporter)AssetImporter.GetAtPath(w.metalSmooth);
                if (ti != null && ti.sRGBTexture) { ti.sRGBTexture = false; ti.SaveAndReimport(); }
                TestWorldBuilder.MakePilotController(w.fbx, w.label);
            }
            imported = true;
            EditorApplication.delayCall += () => imported = false;   // the next menu run imports again
        }

        /// <summary>
        /// Seat the other pilots beside usualPilot (already seated in its cockpit): the same place, scale and rails,
        /// inactive; and a PilotRoster on root that shows one of them.
        /// </summary>
        internal static void AddRoster(GameObject root, GameObject usualPilot, PilotKind usual)
        {
            Prepare();
            var roster = root.GetComponent<PilotRoster>();
            if (roster == null) roster = root.AddComponent<PilotRoster>();
            roster.usual = usual;
            Set(roster, usual, usualPilot);
            if (usual == PilotKind.Koi) KoiFishbowl.Fit(usualPilot);
            var rails = usualPilot.GetComponent<PilotRails>();
            var look0 = usualPilot.GetComponent<PilotHeadLook>();
            bool controls = usualPilot.GetComponent<PilotControls>() != null;
            foreach (var w in All)
            {
                if (w.kind == usual) continue;
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(w.fbx);
                var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>($"{Gen}/{w.label}.controller");
                if (asset == null || ctrl == null) continue;
                var p = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                p.transform.SetParent(usualPilot.transform.parent, false);
                p.transform.localPosition = usualPilot.transform.localPosition;
                p.transform.localRotation = usualPilot.transform.localRotation;
                p.transform.localScale = usualPilot.transform.localScale;
                var mat = TestWorldBuilder.Textured(w.label, w.baseMap, w.metalSmooth, toon: w.kind == PilotKind.Tiger);
                if (w.emission != null) Glow(mat, w.emission);
                foreach (var r in p.GetComponentsInChildren<Renderer>())
                    r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
                var anim = p.GetComponent<Animator>();
                if (anim == null) anim = p.AddComponent<Animator>();
                anim.runtimeAnimatorController = ctrl;
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                if (w.kind == PilotKind.Koi) KoiFishbowl.Fit(p);   // the koi flies in its fishbowl helmet
                if (rails != null)
                {
                    var pr = p.AddComponent<PilotRails>();
                    pr.railL = rails.railL;
                    pr.railR = rails.railR;
                }
                var look = p.AddComponent<PilotHeadLook>();
                if (look0 != null) look.aircraft = look0.aircraft;
                if (controls) TestWorldBuilder.AddPilotControls(p);
                if (w.kind == PilotKind.Spectre) AddFlames(p);
                p.SetActive(false);
                Set(roster, w.kind, p);
            }
        }

        /// <summary>The spectre's eyes and gloves lit by his emission map, bright enough to glow in daylight.</summary>
        static void Glow(Material m, string map)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(map);
            if (tex == null) { Debug.LogWarning($"OODA: no {map}; the spectre won't glow."); return; }
            m.SetTexture("_EmissionMap", tex);
            m.SetColor("_EmissionColor", new Color(0.8f, 2.2f, 3.0f));   // HDR: cyan, over 1 so it reads white-hot
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(m);
        }

        /// <summary>The spectre's ghost fire on his head, hands and ankles (SpectreFlames), its bones found through his avatar.</summary>
        static void AddFlames(GameObject p)
        {
            var anim = p.GetComponent<Animator>();
            if (anim == null || anim.avatar == null || !anim.avatar.isHuman) { Debug.LogWarning("OODA: the spectre has no humanoid avatar; no flames."); return; }
            var f = p.AddComponent<SpectreFlames>();
            f.look = SpectreFlames.Look.Aura;
            Transform B(HumanBodyBones b) => anim.GetBoneTransform(b);
            f.hips = B(HumanBodyBones.Hips); f.head = B(HumanBodyBones.Head);
            f.handL = B(HumanBodyBones.LeftHand); f.handR = B(HumanBodyBones.RightHand);
            f.lowerArmL = B(HumanBodyBones.LeftLowerArm); f.lowerArmR = B(HumanBodyBones.RightLowerArm);
            f.upperLegL = B(HumanBodyBones.LeftUpperLeg); f.upperLegR = B(HumanBodyBones.RightUpperLeg);
            f.footL = B(HumanBodyBones.LeftFoot); f.footR = B(HumanBodyBones.RightFoot); f.toesL = B(HumanBodyBones.LeftToes);
            f.glowing = p.GetComponentsInChildren<Renderer>(true);
        }

        static void Set(PilotRoster r, PilotKind k, GameObject g)
        {
            if (k == PilotKind.Tiger) r.tiger = g;
            else if (k == PilotKind.Wolf) r.wolf = g;
            else if (k == PilotKind.Wizard) r.wizard = g;
            else if (k == PilotKind.Skeleton) r.skeleton = g;
            else if (k == PilotKind.Bear) r.bear = g;
            else if (k == PilotKind.Fox) r.fox = g;
            else if (k == PilotKind.Koi) r.koi = g;
            else if (k == PilotKind.Lion) r.lion = g;
            else if (k == PilotKind.Penguin) r.penguin = g;
            else if (k == PilotKind.Spectre) r.spectre = g;
        }
    }
}
