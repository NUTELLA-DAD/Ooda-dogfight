using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ooda.Turn;
using UnityEditor;
using UnityEngine;

namespace Ooda.EditorTools
{
    /// <summary>
    /// OODA > Fit Missile Racks: fits the wing hardpoints of every turn prefab (Prefabs/*.prefab with a TurnAircraftView)
    /// from its airframe mesh and writes them to Resources/MissileMounts, keyed by the mesh's name and vertex count, so a
    /// game build (which cannot read the meshes) hangs the missiles in the right place. A model with MissileMount_ empties
    /// needs no line. GameBuild runs it before every build, so a re-exported airframe is refitted.
    /// OODA > Missile Shots: each fighter with its missiles on, to Shots~/missiles_*.png.
    /// </summary>
    public static class MissileRacks
    {
        const string Prefabs = "Assets/OODA/Prefabs";
        const string TablePath = "Assets/OODA/Resources/MissileMounts.txt";

        static IEnumerable<string> TurnPrefabs() =>
            Directory.GetFiles(Prefabs, "*.prefab").Select(p => p.Replace('\\', '/')).OrderBy(p => p)
                .Where(p => { var g = AssetDatabase.LoadAssetAtPath<GameObject>(p); return g != null && g.GetComponent<TurnAircraftView>() != null; });

        [MenuItem("OODA/Fit Missile Racks")]
        public static void Fit()
        {
            var sb = new StringBuilder("# Missile hardpoints by airframe mesh (name#vertices|x,y,z;... in that mesh's own frame), in firing order L1;R1;L2;R2.\n# Written by OODA > Fit Missile Racks (Editor/MissileRacks.cs) from the turn prefabs; GameBuild refits it before every build.\n");
            var done = new HashSet<string>();
            foreach (var path in TurnPrefabs())
            {
                var plane = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                try
                {
                    plane.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    var mounts = MissileRack.Mounts(plane.transform, out var src);
                    if (src == "empties") { Debug.Log($"OODA racks: {path}: {mounts.Count} MissileMount_ empties in the model"); continue; }
                    var pts = MissileRack.FitPoints(plane.transform, out var anchor);
                    if (pts == null || anchor == null) { Debug.Log($"OODA racks: {path}: no wing found (no missiles hung, or the bounds' guess)"); continue; }
                    string key = MissileRack.Key(anchor.sharedMesh);
                    if (!done.Add(key)) continue;
                    sb.Append(MissileRack.TableLine(anchor, plane.transform, pts)).Append('\n');
                    Debug.Log($"OODA racks: {path}: {key}: " + string.Join("  ", pts.Select(p => $"({p.x:F2},{p.y:F2},{p.z:F2})")));
                }
                finally { Object.DestroyImmediate(plane); }
            }
            string text = sb.ToString();
            if (!File.Exists(TablePath) || File.ReadAllText(TablePath) != text)
            {
                File.WriteAllText(TablePath, text);
                AssetDatabase.ImportAsset(TablePath);
            }
            MissileRack.ReloadTable();
            Debug.Log($"OODA racks: {done.Count} airframes in {TablePath}");
        }

        [MenuItem("OODA/Missile Shots")]
        public static void Shots()
        {
            Fit();
            string dir = Path.GetFullPath("Shots~");
            Directory.CreateDirectory(dir);
            var camGo = new GameObject("ShotCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.65f, 0.75f);
            cam.fieldOfView = 30f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 1000f;
            var light = new GameObject("ShotLight").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(40, -30, 0);
            var fill = new GameObject("ShotFill").AddComponent<Light>();
            fill.type = LightType.Directional; fill.intensity = 0.5f;
            fill.transform.rotation = Quaternion.Euler(-35, 150, 0);
            var rt = new RenderTexture(1600, 1000, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;
            foreach (var path in TurnPrefabs())
            {
                string tag = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                if (tag.Contains("b17")) continue;
                var plane = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                plane.transform.SetPositionAndRotation(new Vector3(0, 5000, 0), Quaternion.identity);
                var view = plane.GetComponent<TurnAircraftView>();
                int load = tag.Contains("phantom") ? 4 : 2;
                view.ShowMissiles(load, load);
                var b = view.LocalBounds();
                float s = Mathf.Max(b.size.x, b.size.z) / 11.5f;
                Vector3 c = plane.transform.TransformPoint(b.center);
                Shot(cam, rt, dir, $"missiles_{tag}_front", c + new Vector3(-9, -3.5f, 14) * s, c + new Vector3(0, -0.8f, 0));
                Shot(cam, rt, dir, $"missiles_{tag}_below", c + new Vector3(-12, -10, -6) * s, c);
                Shot(cam, rt, dir, $"missiles_{tag}_side", c + new Vector3(-20, -1.2f, 2) * s, c + new Vector3(0, -0.5f, 0));
                view.ShowMissiles(load, load - 1);   // the first one fired: L1 gone
                Shot(cam, rt, dir, $"missiles_{tag}_onefired", c + new Vector3(-9, -3.5f, 14) * s, c + new Vector3(0, -0.8f, 0));
                Object.DestroyImmediate(plane);
            }
            cam.targetTexture = null;
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(light.gameObject);
            Object.DestroyImmediate(fill.gameObject);
            Debug.Log($"OODA racks: shots saved to {dir}");
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
