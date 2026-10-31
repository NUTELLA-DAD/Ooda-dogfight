using System.IO;
using UnityEditor;
using UnityEngine;

namespace Ooda.EditorTools
{
    /// <summary>
    /// OODA > Plane Shots: renders the P-51 prefab from a few angles to PNGs in the project's Shots~ folder
    /// and logs each part's bounds, for checking the model without pressing Play.
    /// </summary>
    public static class PlaneShots
    {
        [MenuItem("OODA/Plane Shots")]
        public static void Capture()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/OODA/Prefabs/P51.prefab");
            var plane = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            plane.transform.position = new Vector3(0, 5000, 0);

            foreach (var r in plane.GetComponentsInChildren<Renderer>())
            {
                var mf = r.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : (r as SkinnedMeshRenderer)?.sharedMesh;
                Debug.Log($"OODA shots: {r.name} bounds centre {r.bounds.center - plane.transform.position} size {r.bounds.size} tris {(mesh != null ? mesh.triangles.Length / 3 : 0)} verts {(mesh != null ? mesh.vertexCount : 0)}");
            }

            MeasureNose(plane);

            var camGo = new GameObject("ShotCam");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 1000f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.65f, 0.75f);
            var light = new GameObject("ShotLight").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(40, -30, 0);

            var rt = new RenderTexture(1600, 1000, 24);
            cam.targetTexture = rt;
            string dir = Path.GetFullPath("Shots~");
            Directory.CreateDirectory(dir);
            Vector3 c = plane.transform.position;
            Shot(cam, rt, dir, "side", c + new Vector3(-16, 1, 0), c);
            Shot(cam, rt, dir, "front34", c + new Vector3(-8, 2, 10), c + new Vector3(0, 0, 3));
            Shot(cam, rt, dir, "nose", c + new Vector3(-3.5f, 0.8f, 7), c + new Vector3(0, 0, 4));
            Shot(cam, rt, dir, "noseside", c + new Vector3(-6, 0.3f, 4), c + new Vector3(0, 0, 4));
            Shot(cam, rt, dir, "top", c + new Vector3(0, 18, 0.01f), c);
            Shot(cam, rt, dir, "canopy", c + new Vector3(-3, 2.2f, -1.5f), c + new Vector3(0, 0.8f, 0.5f));

            var eye = FindDeep(plane.transform, "CockpitCamera");
            if (eye != null)
            {
                cam.fieldOfView = 70f;
                cam.nearClipPlane = 0.02f;
                Shot(cam, rt, dir, "cockpit", eye.position, eye.position + eye.forward * 10f);
                Shot(cam, rt, dir, "cockpit_left", eye.position, eye.position - eye.right * 10f + eye.up * 2f);
            }

            Object.DestroyImmediate(plane);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(light.gameObject);
            Debug.Log($"OODA shots: saved to {dir}");
        }

        // The body's radius about the propeller axis, slice by slice along the nose, and where the propeller's hub and blades sit.
        static void MeasureNose(GameObject plane)
        {
            Transform body = null, prop = null;
            foreach (var mf in plane.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.name == "P51") body = mf.transform;
                if (mf.name == "P51_Propeller") prop = mf.transform;
            }
            if (body == null || prop == null) { Debug.LogWarning("OODA shots: body or propeller not found."); return; }
            var root = plane.transform;
            Vector3 axis = root.InverseTransformPoint(prop.GetComponent<Renderer>().bounds.center);
            Debug.Log($"OODA shots: propeller pivot (root space) {root.InverseTransformPoint(prop.position)} rot {prop.localEulerAngles} bounds centre {axis}");

            var slices = new float[60];
            foreach (var v in body.GetComponent<MeshFilter>().sharedMesh.vertices)
            {
                var p = root.InverseTransformPoint(body.TransformPoint(v));
                int i = Mathf.FloorToInt((p.z - 2.0f) / 0.05f);
                if (i < 0 || i >= slices.Length) continue;
                float r = new Vector2(p.x - axis.x, p.y - axis.y).magnitude;
                if (r < 1.2f) slices[i] = Mathf.Max(slices[i], r);
            }
            var sb = new System.Text.StringBuilder("OODA shots: body radius by z:");
            for (int i = 0; i < slices.Length; i++) if (slices[i] > 0) sb.Append($" {2.0f + i * 0.05f:F2}:{slices[i]:F2}");
            Debug.Log(sb.ToString());

            float hubMin = 99, hubMax = -99, bladeMin = 99, bladeMax = -99, rootMin = 99, rootMax = -99;
            foreach (var v in prop.GetComponent<MeshFilter>().sharedMesh.vertices)
            {
                var p = root.InverseTransformPoint(prop.TransformPoint(v));
                float r = new Vector2(p.x - axis.x, p.y - axis.y).magnitude;
                if (r < 0.35f) { hubMin = Mathf.Min(hubMin, p.z); hubMax = Mathf.Max(hubMax, p.z); }
                else if (r < 0.6f) { rootMin = Mathf.Min(rootMin, p.z); rootMax = Mathf.Max(rootMax, p.z); }
                else { bladeMin = Mathf.Min(bladeMin, p.z); bladeMax = Mathf.Max(bladeMax, p.z); }
            }
            Debug.Log($"OODA shots: propeller z hub(r<0.35) {hubMin:F2}..{hubMax:F2} roots(0.35-0.6) {rootMin:F2}..{rootMax:F2} blades {bladeMin:F2}..{bladeMax:F2}");
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
