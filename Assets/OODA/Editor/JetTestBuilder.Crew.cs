using System.Linq;
using Ooda.Pilot;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ooda.EditorTools
{
    /// <summary>
    /// The Phantom's crew, as in the prop planes: the tiger flies it from the front seat with his paws on the stick and
    /// throttle (PilotControls), and the wolf rides in the back as the WSO, forearms on the rails. Each sits in a copy
    /// of the Spitfire's cockpit (its tub, panel and needles, stick and throttle), dropped into the F-4's canopy where
    /// the seat is, so the clear canopy shows a cockpit and not the inside of the fuselage. The front seat's head is the
    /// cockpit camera's eye.
    /// </summary>
    public static partial class JetTestBuilder
    {
        const string SpitFbx = "Assets/OODA/Art/Spitfire/Spitfire.fbx";
        const string TigerFbx = "Assets/OODA/Art/TigerPilot/TigerPilot.fbx";
        const string WolfFbx = "Assets/OODA/Art/WolfPilot/WolfPilot.fbx";

        /// <summary>Where a seat is along the canopy (0 its back end, 1 its front), how far below the sill its cockpit tub's top sits, and how the pilot sits in it.</summary>
        struct CrewSeat { public float along, tubDrop; public TestWorldBuilder.Seat seat; }

        // Fitted under the glass (the F-4 keeps its canopy): head and shoulders above the sill, the helmet clear of the frame.
        static readonly CrewSeat FrontSeat = new CrewSeat { along = 0.66f, tubDrop = 0.04f, seat = new TestWorldBuilder.Seat { scale = 1.1f, lift = -0.1f, back = 0f, railHalfWidth = 0.36f, railHeight = 0.36f, railForward = 0.22f } };
        static readonly CrewSeat RearSeat = new CrewSeat { along = 0.27f, tubDrop = 0.04f, seat = new TestWorldBuilder.Seat { scale = 1.0f, lift = -0.12f, back = 0f, railHalfWidth = 0.34f, railHeight = 0.34f, railForward = 0.2f } };

        /// <summary>The two seats, their cockpits and pilots, under root; returns the front pilot's head look.</summary>
        static PilotHeadLook AddCrew(Transform root, Mesh mesh, Transform model)
        {
            // the canopy glass, in the plane's frame
            var glass = mesh.GetTriangles(1).Distinct().Select(i => root.InverseTransformPoint(model.TransformPoint(mesh.vertices[i]))).ToArray();
            if (glass.Length == 0) { Debug.LogWarning("OODA: the Phantom has no canopy glass; no crew."); return null; }
            float z0 = glass.Min(p => p.z), z1 = glass.Max(p => p.z);

            var tigerMat = TestWorldBuilder.Textured("TigerPilot", "Assets/OODA/Art/TigerPilot/Pilot_BaseColor.png", "Assets/OODA/Art/TigerPilot/Pilot_MetallicSmoothness.png", toon: true);
            var wolfMat = TestWorldBuilder.Textured("WolfPilot", "Assets/OODA/Art/WolfPilot/Wolf_BaseColor.png", "Assets/OODA/Art/WolfPilot/Wolf_MetallicSmoothness.png");
            var tigerCtrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/OODA/Generated/TigerPilot.controller");
            var wolfCtrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/OODA/Generated/WolfPilot.controller");

            Transform Seat(string mountName, string prefix, CrewSeat s, string fbx, Material mat, RuntimeAnimatorController ctrl, bool flies)
            {
                float z = Mathf.Lerp(z0, z1, s.along);
                // the sill: the glass's lower edge beside the seat
                float sill = glass.Where(p => Mathf.Abs(p.z - z) < 0.35f).Select(p => p.y).DefaultIfEmpty(0).Min();
                float halfWidth = glass.Where(p => Mathf.Abs(p.z - z) < 0.35f).Select(p => Mathf.Abs(p.x)).DefaultIfEmpty(0.5f).Max();
                var mount = new GameObject(mountName).transform;
                mount.SetParent(root, false);
                mount.localPosition = new Vector3(0, sill, z);

                // the Spitfire's cockpit, kept where it sits about the Spitfire's PilotMount
                var spit = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SpitFbx));
                PrefabUtility.UnpackPrefabInstance(spit, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var spitMount = TestWorldBuilder.FindDeep(spit.transform, "PilotMount");
                var pit = TestWorldBuilder.FindDeep(spit.transform, "Pit");
                pit.SetParent(spitMount, true);
                pit.SetParent(mount, false);
                Object.DestroyImmediate(spit);
                foreach (var r in pit.GetComponentsInChildren<Renderer>())
                {
                    r.sharedMaterials = Enumerable.Repeat(TestWorldBuilder.CockpitMat(), r.sharedMaterials.Length).ToArray();
                    r.shadowCastingMode = ShadowCastingMode.Off;
                }
                // wide enough to close the canopy's opening, and its top just under the sill
                var tub = pit.GetComponentsInChildren<Renderer>().FirstOrDefault(r => r.name == "Pit_Static");
                if (tub != null)
                {
                    float w = tub.bounds.size.x;
                    if (w > 0.01f) pit.localScale = new Vector3(Mathf.Max(1f, 2.1f * halfWidth / w), 1, 1);
                    float top = root.InverseTransformPoint(tub.bounds.max).y;
                    mount.localPosition += Vector3.up * (sill - s.tubDrop - top);
                }
                if (prefix != "")
                    foreach (var t in pit.GetComponentsInChildren<Transform>()) t.name = prefix + t.name;   // the front pilot's PilotControls finds its parts by name

                var pilot = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
                TestWorldBuilder.SeatPilot(pilot, mount, s.seat);
                foreach (var r in pilot.GetComponentsInChildren<Renderer>())
                    r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
                var anim = pilot.GetComponent<Animator>();
                if (anim == null) anim = pilot.AddComponent<Animator>();
                anim.runtimeAnimatorController = ctrl;
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                pilot.AddComponent<PilotHeadLook>();
                if (flies) TestWorldBuilder.AddPilotControls(pilot);
                Debug.Log($"OODA: the Phantom's {mountName} at z {z:F2}, sill {sill:F2}, canopy half-width {halfWidth:F2}.");
                return pilot.transform;
            }

            var front = Seat("PilotMount", "", FrontSeat, TigerFbx, tigerMat, tigerCtrl, true);
            Seat("RearSeatMount", "Rear", RearSeat, WolfFbx, wolfMat, wolfCtrl, false);
            return front.GetComponent<PilotHeadLook>();
        }

        /// <summary>The cockpit camera at the front pilot's eyes, with him posed in his seat (on a copy, so the prefab keeps its rest pose).</summary>
        static void EyeAtHead(Transform root, Transform eye)
        {
            var copy = Object.Instantiate(root.gameObject);
            var anim = copy.GetComponentInChildren<Animator>();
            if (anim != null && anim.isHuman)
            {
                anim.Rebind();
                for (int i = 0; i < 4; i++) anim.Update(0.1f);
                var head = anim.GetBoneTransform(HumanBodyBones.Head);
                if (head != null)
                    eye.localPosition = copy.transform.InverseTransformPoint(head.position) + new Vector3(0, 0.1f, 0.1f);
            }
            Object.DestroyImmediate(copy);
        }
    }
}
