using System.IO;
using System.Reflection;
using Ooda.Flight.Core;
using Ooda.Turn.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ooda.EditorTools
{
    /// <summary>
    /// OODA > Cloud Sea Shots (or -executeMethod Ooda.EditorTools.CloudShots.Capture): the cloud sea in the turn battle
    /// scene, rendered to PNGs in Shots~. First the dogfight over the Cloud Sea as the planning view shows it (a head-on
    /// start at the usual height), then the clouds from a free camera: down the start corridor, out among the towers, a
    /// thunderhead, from high over the tower tops, skimming the deck, under the deck looking up, and from inside a tower
    /// (the whiteout). The dogfight setup kept for the game is left as it was.
    /// </summary>
    public static class CloudShots
    {
        [MenuItem("OODA/Cloud Sea Shots")]
        public static void Capture()
        {
            var keepSetup = Ooda.UI.DogfightSetupScreen.Chosen;
            var keepMission = Ooda.UI.TitleScreen.ChosenMission;
            try
            {
                Ooda.UI.DogfightSetupScreen.Chosen = new DogfightSetup
                {
                    randomStart = false, start = Scenarios.Start.HeadOn, randomArena = false, arena = Arenas.CloudIndex,
                    height = DogfightSetup.Height.Medium, ours = new[] { Craft.Mustang }, theirs = new[] { Craft.Bf109 },
                };
                Ooda.UI.TitleScreen.ChosenMission = (int)Ooda.Turn.TurnBattle.Mission.Dogfight;
                Run();
            }
            finally
            {
                Ooda.UI.DogfightSetupScreen.Chosen = keepSetup;
                Ooda.UI.TitleScreen.ChosenMission = keepMission;
                var clouds = Ooda.Fx.CloudScape.Existing;
                if (clouds != null) Object.DestroyImmediate(clouds.gameObject);   // (puts the fog and the sky back)
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
            var clouds = Ooda.Fx.CloudScape.Ensure();

            // the planning view, over the cloud sea
            var cam = Camera.main;
            if (cam != null)
            {
                cam.targetTexture = rt;
                clouds.Show(true, cam);
                clouds.Settle();
                Save(cam, rt, dir, "clouds_planning");
                cam.targetTexture = null;
            }
            else Debug.LogWarning("OODA cloud shots: no camera in the scene; free camera shots only.");

            // the clouds from a free camera
            var camGo = new GameObject("CloudShotCam");
            var free = camGo.AddComponent<Camera>();
            free.farClipPlane = 30000f; free.nearClipPlane = 0.5f; free.fieldOfView = 60f;
            free.clearFlags = CameraClearFlags.Skybox;
            free.targetTexture = rt;
            clouds.Show(true, free);
            var sky = Skyscape.Sky;
            float ax = (float)(Skyscape.AxisDeg * Mathf.Deg2Rad);
            Vector3 c = new Vector3(Skyscape.CentreX, 0, Skyscape.CentreZ), along = new Vector3(Mathf.Sin(ax), 0, Mathf.Cos(ax)), across = Vector3.Cross(Vector3.up, along);
            float y = (float)(Skyscape.StartAltFt * Atmosphere.MPerFt);
            Shot(free, clouds, rt, dir, "clouds_corridor", c - along * 3000 + Vector3.up * y, c + along * 3000 + Vector3.up * (y - 150));
            Shot(free, clouds, rt, dir, "clouds_corridor_back", c + along * 3000 + Vector3.up * y, c - along * 3000 + Vector3.up * (y - 150));
            Shot(free, clouds, rt, dir, "clouds_among_towers", c + across * 2500 + Vector3.up * (y - 200), c + across * 9000 + along * 1500 + Vector3.up * (y + 300));
            Shot(free, clouds, rt, dir, "clouds_high", c - along * 6000 + across * 3000 + Vector3.up * 5600, c + Vector3.up * 1500);
            Shot(free, clouds, rt, dir, "clouds_deck_skim", c - along * 2500 + Vector3.up * (Skyscape.DeckTopM + 120), c + along * 4000 + Vector3.up * (Skyscape.DeckTopM + 250));
            Shot(free, clouds, rt, dir, "clouds_under_deck", c + Vector3.up * 350, c + along * 3000 + across * 800 + Vector3.up * 1400);
            // the nearest thunderhead and the nearest tower, from outside, and from inside the tower
            Skyscape.Bank storm = default, tower = default;
            float bestStorm = float.MaxValue, bestTower = float.MaxValue;
            foreach (var b in sky.banks)
            {
                float d = new Vector2(b.x - c.x, b.z - c.z).magnitude;
                if (b.kind == Skyscape.Storm && d < bestStorm) { bestStorm = d; storm = b; }
                if (b.kind == Skyscape.Cumulus && b.topY > 3500f && d < bestTower) { bestTower = d; tower = b; }
            }
            if (bestStorm < float.MaxValue)
            {
                var at = new Vector3(storm.x, 0, storm.z);
                var from = c + (at - c) * 0.35f + Vector3.up * y;
                Shot(free, clouds, rt, dir, "clouds_thunderhead", from, at + Vector3.up * 3500);
            }
            if (bestTower < float.MaxValue)
            {
                var at = new Vector3(tower.x, y, tower.z);
                Vector3 toward = (at - new Vector3(c.x, y, c.z)).normalized;
                Shot(free, clouds, rt, dir, "clouds_tower_outside", at - toward * 2600, at);
                Shot(free, clouds, rt, dir, "clouds_tower_entering", at - toward * 650, at + toward * 1000);
                Shot(free, clouds, rt, dir, "clouds_tower_inside", at, at + toward * 1000);
            }
            free.targetTexture = null;
            Object.DestroyImmediate(camGo);
            Debug.Log($"OODA cloud shots: saved to {dir}");
        }

        static void Shot(Camera cam, Ooda.Fx.CloudScape clouds, RenderTexture rt, string dir, string name, Vector3 from, Vector3 at)
        {
            cam.transform.position = from;
            cam.transform.LookAt(at);
            clouds.Settle();
            float d = Skyscape.Sky.Density(from.x, from.y, from.z);
            Debug.Log($"OODA cloud shots: {name}: in cloud {d:F2}, fog to {RenderSettings.fogEndDistance:F0} m");
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

        static void Call(MonoBehaviour b, string method)
        {
            if (b == null) return;
            var m = b.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m == null || m.GetParameters().Length != 0) return;
            try { m.Invoke(b, null); }
            catch (TargetInvocationException e) { Debug.LogWarning($"OODA cloud shots: {b.GetType().Name}.{method} threw {e.InnerException?.GetType().Name}: {e.InnerException?.Message}"); }
        }
    }
}
