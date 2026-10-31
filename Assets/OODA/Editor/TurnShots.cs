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
            foreach (var a in Object.FindObjectsByType<Animator>(FindObjectsSortMode.None)) { a.Rebind(); for (int i = 0; i < 4; i++) a.Update(0.1f); }
            Save(cam, rt, dir, "turn_planning");
            LogWarning();

            // The holographic ghosts up close, where the plan ends.
            foreach (var (ghostName, shot) in new[] { ("GhostP51", "turn_holo"), ("GhostSpitfire", "turn_holo_enemy") })
            {
                var ghost = GameObject.Find(ghostName);
                if (ghost == null || !ghost.activeInHierarchy) { Debug.LogWarning($"OODA shots: no active {ghostName}."); continue; }
                foreach (var tc in cam.GetComponents<MonoBehaviour>()) tc.enabled = false;
                var g = ghost.transform;
                cam.transform.position = g.position - g.forward * 14f + g.up * 4f + g.right * 6f;
                cam.transform.LookAt(g.position);
                Redraw(behaviours, cam);
                Save(cam, rt, dir, shot);
                foreach (var tc in cam.GetComponents<MonoBehaviour>()) tc.enabled = true;
            }

            // The arrow dragged well past the ring: the overpull band, the red arrow and the warning.
            var tb = Object.FindFirstObjectByType<Ooda.Turn.TurnBattle>();
            if (tb != null)
            {
                const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic;
                var env = (Ooda.Turn.Core.Envelope)typeof(Ooda.Turn.TurnBattle).GetField("env", f).GetValue(tb);
                var plan = (Ooda.Turn.Core.TurnPlan)typeof(Ooda.Turn.TurnBattle).GetField("plan", f).GetValue(tb);
                typeof(Ooda.Turn.TurnBattle).GetMethod("SetAim", f).Invoke(tb, new object[] { plan.aim + env.up * 3.0 });
                Redraw(behaviours, cam, true);
                Save(cam, rt, dir, "turn_overpull");
                LogWarning();
            }

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
                Redraw(behaviours, cam);
                Save(cam, rt, dir, "turn_spitfire");
                cam.transform.position = spit.position + spit.right * 2.2f + spit.up * 1.6f - spit.forward * 0.3f;
                cam.transform.LookAt(spit.position + spit.up * 0.7f + spit.forward * 0.4f);
                Redraw(behaviours, cam);
                Save(cam, rt, dir, "turn_wolf");
            }
            else Debug.LogWarning("OODA shots: no Spitfire in the scene.");
            cam.targetTexture = null;
            Debug.Log($"OODA shots: turn battle saved to {dir}");
        }

        // Lines are sized from the camera's position, so lay them out again once the camera has moved (but don't let its own scripts move it back).
        static void Redraw(MonoBehaviour[] behaviours, Camera cam, bool withCamera = false)
        {
            foreach (var b in behaviours) if (b != null && (withCamera || b.gameObject != cam.gameObject)) Call(b, "Update");
            foreach (var b in behaviours) if (b != null && (withCamera || b.gameObject != cam.gameObject)) Call(b, "LateUpdate");
        }

        // The planning warning is drawn with OnGUI, which a camera render doesn't include: log it instead.
        static void LogWarning()
        {
            var tb = Object.FindFirstObjectByType<Ooda.Turn.TurnBattle>();
            if (tb == null) return;
            var f = BindingFlags.Instance | BindingFlags.NonPublic;
            var w = typeof(Ooda.Turn.TurnBattle).GetField("warning", f)?.GetValue(tb);
            var o = typeof(Ooda.Turn.TurnBattle).GetField("plan", f)?.GetValue(tb) is Ooda.Turn.Core.TurnPlan p ? p.overpull : 0;
            Debug.Log($"OODA shots: planning warning \"{w ?? "(none)"}\", overpull {o:F2}");
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
