using System.IO;
using System.Reflection;
using Ooda.Turn.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ooda.EditorTools
{
    /// <summary>
    /// OODA > City Shots (or -executeMethod Ooda.EditorTools.CityShots.Capture): the skyscraper city in the turn battle
    /// scene, rendered to PNGs in Shots~. First the dogfight over Skyline City as the planning view shows it (a head-on
    /// start, low, down the river), then the same with the arrow dragged hard right into the towers (the ghost path red,
    /// BUILDING in the log), then the city from a free camera: down the river at the low and the usual start heights, along
    /// a street canyon, downtown from above and the whole island. The dogfight setup kept for the game is left as it was.
    /// </summary>
    public static class CityShots
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;

        [MenuItem("OODA/City Shots")]
        public static void Capture()
        {
            var keepSetup = Ooda.UI.DogfightSetupScreen.Chosen;
            var keepMission = Ooda.UI.TitleScreen.ChosenMission;
            try
            {
                Ooda.UI.DogfightSetupScreen.Chosen = new DogfightSetup
                {
                    randomStart = false, start = Scenarios.Start.HeadOn, randomArena = false, arena = Arenas.CityIndex,
                    height = DogfightSetup.Height.Low, ours = new[] { Craft.Mustang }, theirs = new[] { Craft.Bf109 },
                };
                Ooda.UI.TitleScreen.ChosenMission = (int)Ooda.Turn.TurnBattle.Mission.Dogfight;
                Run();
            }
            finally
            {
                Ooda.UI.DogfightSetupScreen.Chosen = keepSetup;
                Ooda.UI.TitleScreen.ChosenMission = keepMission;
            }
        }

        static void Run()
        {
            var scene = EditorSceneManager.OpenScene("Assets/OODA/Scenes/TurnBattle.unity");
            if (Ooda.Fx.ToonSky.Enabled) Ooda.Fx.ToonSky.Apply(scene);
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
            var rt = new RenderTexture(1600, 1000, 24) { antiAliasing = 4 };

            // the planning view, over the city
            var cam = Camera.main;
            var tb = Object.FindFirstObjectByType<Ooda.Turn.TurnBattle>();
            if (cam != null && tb != null)
            {
                cam.targetTexture = rt;
                foreach (var a in Object.FindObjectsByType<Animator>(FindObjectsSortMode.None)) { a.Rebind(); for (int i = 0; i < 4; i++) a.Update(0.1f); }
                Save(cam, rt, dir, "city_planning");
                LogWarning(tb);
                // hard right, into the riverside towers
                var env = (Envelope)typeof(Ooda.Turn.TurnBattle).GetField("env", F).GetValue(tb);
                double phi = System.Math.PI / 2;   // to the right of the nose
                typeof(Ooda.Turn.TurnBattle).GetMethod("SetAim", F).Invoke(tb, new object[] { env.Direction(phi, env.MaxAngle(phi) * 0.95) });
                Redraw(behaviours, cam);
                Save(cam, rt, dir, "city_planning_into_tower");
                LogWarning(tb);
                cam.targetTexture = null;
            }
            else Debug.LogWarning("OODA city shots: no camera or turn battle in the scene; free camera shots only.");

            // the city from a free camera
            var camGo = new GameObject("CityShotCam");
            var free = camGo.AddComponent<Camera>();
            free.farClipPlane = 40000f; free.nearClipPlane = 0.5f; free.fieldOfView = 60f;
            free.clearFlags = CameraClearFlags.Skybox;
            free.targetTexture = rt;
            float cx = City.CentreX, cz = City.CentreZ;
            Shot(free, rt, dir, "city_river_low", new Vector3(cx - 20, 155, cz - 1300), new Vector3(cx, 120, cz + 1500));
            Shot(free, rt, dir, "city_river_medium", new Vector3(cx - 60, 335, cz - 3300), new Vector3(cx + 200, 180, cz + 800));
            Shot(free, rt, dir, "city_canyon", new Vector3(cx + 455, 70, cz - 700), new Vector3(cx + 455, 110, cz + 1500));
            Shot(free, rt, dir, "city_downtown_high", new Vector3(cx - 2600, 1200, cz - 3600), new Vector3(cx + 300, 150, cz + 600));
            Shot(free, rt, dir, "city_overview", new Vector3(cx + 4500, 3200, cz - 7000), new Vector3(cx, 0, cz));
            free.targetTexture = null;
            Object.DestroyImmediate(camGo);
            Debug.Log($"OODA city shots: saved to {dir}");
        }

        static void Shot(Camera cam, RenderTexture rt, string dir, string name, Vector3 from, Vector3 at)
        {
            cam.transform.position = from;
            cam.transform.LookAt(at);
            Save(cam, rt, dir, name);
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

        static void Redraw(MonoBehaviour[] behaviours, Camera cam)
        {
            foreach (var b in behaviours) if (b != null && b.gameObject != cam.gameObject) Call(b, "Update");
            foreach (var b in behaviours) if (b != null && b.gameObject != cam.gameObject) Call(b, "LateUpdate");
        }

        static void LogWarning(Ooda.Turn.TurnBattle tb)
        {
            var w = typeof(Ooda.Turn.TurnBattle).GetField("warning", F)?.GetValue(tb);
            Debug.Log($"OODA city shots: planning warning \"{w ?? "(none)"}\"");
        }

        static void Call(MonoBehaviour b, string method)
        {
            if (b == null) return;
            var m = b.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m == null || m.GetParameters().Length != 0) return;
            try { m.Invoke(b, null); }
            catch (TargetInvocationException e) { Debug.LogWarning($"OODA city shots: {b.GetType().Name}.{method} threw {e.InnerException?.GetType().Name}: {e.InnerException?.Message}"); }
        }
    }
}
