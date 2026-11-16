using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Ooda.EditorTools
{
    /// <summary>
    /// OODA > Ghost Shots: opens the TurnBattle scene, runs its scripts by hand (batch mode has no play loop) and renders the
    /// planning ghosts up close, from three sides, with 4x MSAA like the game, to PNGs in Shots~/ghost.
    /// From the command line: -executeMethod Ooda.EditorTools.GhostShots.Capture -ghostTag before
    /// (the tag goes in front of each file name, so a before and an after set can sit side by side).
    /// </summary>
    public static class GhostShots
    {
        [MenuItem("OODA/Ghost Shots")]
        public static void Capture()
        {
            string tag = Arg("-ghostTag") ?? "ghost";
            EditorSceneManager.OpenScene("Assets/OODA/Scenes/TurnBattle.unity");
            var behaviours = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var name in new[] { "Awake", "OnEnable", "Start" })
                foreach (var b in behaviours) Call(b, name);
            for (int i = 0; i < 5; i++)
            {
                foreach (var b in behaviours) Call(b, "Update");
                foreach (var b in behaviours) Call(b, "LateUpdate");
            }

            string dir = Path.GetFullPath(Path.Combine("Shots~", "ghost"));
            Directory.CreateDirectory(dir);
            var cam = Camera.main;
            var rt = new RenderTexture(1600, 1000, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;
            foreach (var a in Object.FindObjectsByType<Animator>(FindObjectsSortMode.None)) { a.Rebind(); for (int i = 0; i < 4; i++) a.Update(0.1f); }
            foreach (var tc in cam.GetComponents<MonoBehaviour>()) tc.enabled = false;
            Save(cam, rt, dir, $"{tag}_planning");

            var ghosts = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                .Where(t => t.parent == null && t.gameObject.activeInHierarchy && t.name.StartsWith("Ghost"))
                .OrderBy(t => t.name).Take(3).ToArray();
            if (ghosts.Length == 0) Debug.LogWarning("OODA ghost shots: no active ghost in the planning view.");
            int n = 0;
            foreach (var g in ghosts)
            {
                // three views: behind and above, from the side against the sky, from above against the ground
                var views = new (string, Vector3)[]
                {
                    ("rear", -g.forward * 14f + g.up * 4f + g.right * 6f),
                    ("side", g.right * 13f - g.up * 2.5f + g.forward * 2f),
                    ("top", g.up * 15f - g.forward * 5f + g.right * 3f),
                };
                foreach (var (label, off) in views)
                {
                    cam.transform.position = g.position + off;
                    cam.transform.LookAt(g.position, label == "top" ? g.forward : Vector3.up);
                    Redraw(behaviours, cam);
                    Save(cam, rt, dir, $"{tag}_{n}_{label}");
                }
                Debug.Log($"OODA ghost shots: {g.name} saved as {tag}_{n}_*.png");
                n++;
            }
            foreach (var tc in cam.GetComponents<MonoBehaviour>()) tc.enabled = true;
            cam.targetTexture = null;
            Debug.Log($"OODA ghost shots: saved to {dir}");
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static void Redraw(MonoBehaviour[] behaviours, Camera cam)
        {
            foreach (var b in behaviours) if (b != null && b.gameObject != cam.gameObject) Call(b, "Update");
            foreach (var b in behaviours) if (b != null && b.gameObject != cam.gameObject) Call(b, "LateUpdate");
        }

        static void Call(MonoBehaviour b, string method)
        {
            if (b == null) return;
            var m = b.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m == null || m.GetParameters().Length != 0) return;
            try { m.Invoke(b, null); }
            catch (TargetInvocationException e) { Debug.LogWarning($"OODA ghost shots: {b.GetType().Name}.{method} threw {e.InnerException?.GetType().Name}: {e.InnerException?.Message}"); }
        }

        static void Save(Camera cam, RenderTexture rt, string dir, string name)
        {
            cam.Render();
            var flat = RenderTexture.GetTemporary(rt.width, rt.height, 0);
            Graphics.Blit(rt, flat);   // resolve the MSAA
            RenderTexture.active = flat;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(flat);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
