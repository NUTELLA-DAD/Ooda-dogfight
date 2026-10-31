using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ooda.EditorTools
{
    /// <summary>
    /// OODA > Turn Battle Shots: opens the TurnBattle scene, runs its scripts' Awake/Start and a few Update/LateUpdate
    /// frames by hand (batch mode has no play loop), and renders the planning view and the Spitfire to PNGs in Shots~.
    /// </summary>
    public static class TurnShots
    {
        [MenuItem("OODA/Turn Battle Shots")]
        public static void Capture()
        {
            EditorSceneManager.OpenScene("Assets/OODA/Scenes/TurnBattle.unity");
            var behaviours = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var name in new[] { "Awake", "OnEnable", "Start" })
                foreach (var b in behaviours) Call(b, name);
            for (int i = 0; i < 5; i++)
            {
                foreach (var b in behaviours) Call(b, "Update");
                foreach (var b in behaviours) Call(b, "LateUpdate");
            }

            string dir = Path.GetFullPath("Shots~");
            Directory.CreateDirectory(dir);
            var cam = Camera.main;
            var rt = new RenderTexture(1600, 1000, 24);
            cam.targetTexture = rt;
            Save(cam, rt, dir, "turn_planning");

            // Close-up of the Spitfire and its pilot, from its right and a little ahead.
            Transform spit = null;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.name.StartsWith("Spitfire") && t.parent == null) spit = t;
            if (spit == null)
                foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                    if (t.name.Contains("Spitfire")) { spit = t; break; }
            if (spit != null)
            {
                foreach (var tc in cam.GetComponents<MonoBehaviour>()) tc.enabled = false;
                cam.nearClipPlane = 0.05f;
                cam.fieldOfView = 35f;
                cam.transform.position = spit.position + spit.right * 7f + spit.up * 2.2f + spit.forward * 3f;
                cam.transform.LookAt(spit.position + spit.up * 0.6f + spit.forward * 0.5f);
                Save(cam, rt, dir, "turn_spitfire");
                cam.transform.position = spit.position + spit.right * 2.2f + spit.up * 1.6f - spit.forward * 0.3f;
                cam.transform.LookAt(spit.position + spit.up * 0.7f + spit.forward * 0.4f);
                Save(cam, rt, dir, "turn_wolf");
            }
            else Debug.LogWarning("OODA shots: no Spitfire in the scene.");
            cam.targetTexture = null;
            Debug.Log($"OODA shots: turn battle saved to {dir}");
        }

        static void Call(MonoBehaviour b, string method)
        {
            if (b == null) return;
            var m = b.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m == null || m.GetParameters().Length != 0) return;
            try { m.Invoke(b, null); }
            catch (TargetInvocationException e) { Debug.LogWarning($"OODA shots: {b.GetType().Name}.{method} threw {e.InnerException?.GetType().Name}: {e.InnerException?.Message}"); }
        }

        static void Save(Camera cam, RenderTexture rt, string dir, string name)
        {
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
