using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Ooda.EditorTools
{
    /// <summary>
    /// OODA > Plane Shots: renders the P-51 and Spitfire prefabs, with their pilots posed (seated clip and IK), from a few
    /// angles to PNGs in the project's Shots~ folder, and logs the cockpit's measurements, for checking the models
    /// without pressing Play.
    /// </summary>
    public static class PlaneShots
    {
        [MenuItem("OODA/Plane Shots")]
        public static void Capture()
        {
            string dir = Path.GetFullPath("Shots~");
            Directory.CreateDirectory(dir);

            var camGo = new GameObject("ShotCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.65f, 0.75f);
            var light = new GameObject("ShotLight").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(40, -30, 0);
            var rt = new RenderTexture(1600, 1000, 24);
            cam.targetTexture = rt;

            foreach (var (path, tag) in new[] { ("Assets/OODA/Prefabs/P51.prefab", "p51"), ("Assets/OODA/Prefabs/Spitfire.prefab", "spit") })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { Debug.LogWarning($"OODA shots: no {path}"); continue; }
                var plane = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                plane.transform.position = new Vector3(0, 5000, 0);
                Pose(plane);
                Measure(plane, tag);

                Vector3 c = plane.transform.position;
                var mount = FindDeep(plane.transform, "PilotMount");
                Vector3 m = mount != null ? mount.position : c;
                cam.fieldOfView = 30f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 1000f;
                Shot(cam, rt, dir, tag + "_side", c + new Vector3(-16, 1, 0), c);
                Shot(cam, rt, dir, tag + "_front34", c + new Vector3(-8, 2, 10), c + new Vector3(0, 0, 3));
                Shot(cam, rt, dir, tag + "_pilot", m + new Vector3(-3.2f, 1.6f, 2.6f), m + new Vector3(0, 0.6f, 0));
                Shot(cam, rt, dir, tag + "_pilotfront", m + new Vector3(0.6f, 1.5f, 3.6f), m + new Vector3(0, 0.6f, 0));
                Shot(cam, rt, dir, tag + "_pilotback", m + new Vector3(1.4f, 1.8f, -3.4f), m + new Vector3(0, 0.6f, 0));

                var eye = FindDeep(plane.transform, "CockpitCamera");
                var look = plane.GetComponentInChildren<Pilot.PilotHeadLook>();
                if (eye != null)
                {
                    if (look != null) { look.hideHead = true; Call(look, "LateUpdate"); }
                    cam.fieldOfView = 70f;
                    cam.nearClipPlane = 0.02f;
                    Shot(cam, rt, dir, tag + "_cockpit", eye.position, eye.position + eye.forward * 10f);
                    Shot(cam, rt, dir, tag + "_cockpit_left", eye.position, eye.position - eye.right * 10f + eye.up * 2f);
                }
                Object.DestroyImmediate(plane);
            }

            cam.targetTexture = null;
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(light.gameObject);
            Debug.Log($"OODA shots: saved to {dir}");
        }

        // The seated clip and the hand IK, run by hand: there is no play loop in batch mode.
        static void Pose(GameObject plane)
        {
            foreach (var anim in plane.GetComponentsInChildren<Animator>())
            {
                anim.Rebind();
                for (int i = 0; i < 4; i++) anim.Update(0.1f);
            }
        }

        static void Call(MonoBehaviour b, string method) =>
            b.GetType().GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.Invoke(b, null);

        static void Measure(GameObject plane, string tag)
        {
            var root = plane.transform;
            string P(Vector3 world) { var v = root.InverseTransformPoint(world); return $"({v.x:F2},{v.y:F2},{v.z:F2})"; }
            var sb = new StringBuilder($"OODA shots [{tag}]:");
            var mount = FindDeep(root, "PilotMount");
            if (mount != null) sb.Append($" mount {P(mount.position)} scale {mount.lossyScale.x:F3}");
            foreach (var n in new[] { "RailL", "RailR", "CockpitCamera" })
            {
                var t = FindDeep(root, n);
                if (t != null) sb.Append($" {n} {P(t.position)}");
            }
            var anim = plane.GetComponentInChildren<Animator>();
            if (anim != null && anim.isHuman)
                foreach (var b in new[] { HumanBodyBones.Hips, HumanBodyBones.Head, HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
                {
                    var t = anim.GetBoneTransform(b);
                    if (t != null) sb.Append($" {b} {P(t.position)}");
                }
            foreach (var r in plane.GetComponentsInChildren<Renderer>(true))
                if (r.name.Contains("Canopy") || r.name.Contains("Cockpit"))
                    sb.Append($" {r.name}[{(r.enabled ? "on" : "off")}] min {P(r.bounds.min)} max {P(r.bounds.max)}");
            Debug.Log(sb.ToString());

            // The fuselage's top edge across the cockpit: the highest body point in each 5 cm strip out from the centre line.
            var body = plane.GetComponentsInChildren<MeshFilter>()
                .Where(f => f.sharedMesh != null && !f.name.Contains("Canopy") && !f.name.Contains("Cockpit") && !f.name.Contains("Propeller"))
                .OrderByDescending(f => f.sharedMesh.vertexCount).FirstOrDefault();
            if (body == null || mount == null) return;
            float mz = root.InverseTransformPoint(mount.position).z;
            foreach (float dz in new[] { -0.3f, 0f, 0.3f, 0.6f })
            {
                var top = new float[16];
                for (int i = 0; i < top.Length; i++) top[i] = float.NaN;
                foreach (var v in body.sharedMesh.vertices)
                {
                    var p = root.InverseTransformPoint(body.transform.TransformPoint(v));
                    if (Mathf.Abs(p.z - (mz + dz)) > 0.05f) continue;
                    int i = Mathf.FloorToInt(Mathf.Abs(p.x) / 0.05f);
                    if (i < top.Length && (float.IsNaN(top[i]) || p.y > top[i])) top[i] = p.y;
                }
                Debug.Log($"OODA shots [{tag}]: body top at z {mz + dz:F2} by |x|: " + string.Join(" ", top.Select((y, i) => $"{i * 0.05f:F2}:{(float.IsNaN(y) ? "-" : y.ToString("F2"))}")));
            }
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var f = FindDeep(c, name); if (f != null) return f; }
            return null;
        }

        static void Shot(Camera cam, RenderTexture rt, string dir, string name, Vector3 from, Vector3 at)
        {
            cam.transform.position = from;
            cam.transform.LookAt(at);
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
