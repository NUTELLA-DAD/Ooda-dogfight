using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ooda.EditorTools
{
    /// <summary>
    /// OODA > World Shots: opens the test world and renders the Alps to PNGs in Shots~: an overview, the main valley
    /// at fight height, a gorge, each bridge, the village and the view from the P-51's cockpit.
    /// </summary>
    public static class WorldShots
    {
        [MenuItem("OODA/World Shots")]
        public static void Capture()
        {
            EditorSceneManager.OpenScene(TestWorldBuilder.ScenePath);
            string dir = Path.GetFullPath("Shots~");
            Directory.CreateDirectory(dir);
            var camGo = new GameObject("ShotCam");
            var cam = camGo.AddComponent<Camera>();
            cam.farClipPlane = 60000f;
            cam.nearClipPlane = 0.5f;
            cam.fieldOfView = 55f;
            cam.clearFlags = CameraClearFlags.Skybox;
            var rt = new RenderTexture(1600, 1000, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;
            var terrain = Object.FindFirstObjectByType<Terrain>();
            float Ground(float x, float z) => terrain.SampleHeight(new Vector3(x, 0, z)) + terrain.transform.position.y;

            Shot(cam, rt, dir, "world_overview", new Vector3(-9000, 9000, -17000), new Vector3(0, 800, -1000));
            Shot(cam, rt, dir, "world_overview_east", new Vector3(16000, 7000, -6000), new Vector3(0, 1200, 0));
            var fight = AlpineWorld.FightCentre;
            float alt = AlpineWorld.FightAltFt * 0.3048f;
            Shot(cam, rt, dir, "world_valley", new Vector3(fight.x - 40, alt, fight.z - 1500), new Vector3(fight.x, alt - 120, fight.z + 2000));
            Shot(cam, rt, dir, "world_valley_back", new Vector3(fight.x + 40, alt, fight.z + 1500), new Vector3(fight.x, alt - 150, fight.z - 2500));
            // into gorge one from the west, low between its walls
            float gz(float x) => -2600 + 500 * Mathf.Sin(x / 1700f);
            Shot(cam, rt, dir, "world_gorge", new Vector3(-7000, Ground(-7000, gz(-7000)) + 150, gz(-7000)), new Vector3(-6000, Ground(-6000, gz(-6000)) + 120, gz(-6000)));

            var marks = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t => t.name.StartsWith("Bridge ") && t.name.EndsWith(" centre")).OrderBy(t => t.name).ToList();
            foreach (var m in marks)
            {
                var bridge = m.parent;
                float len = bridge.GetComponent<MeshRenderer>().bounds.size.magnitude;
                Vector3 side = m.right * Mathf.Clamp(len * 0.55f, 120, 900);
                Vector3 from = m.position + side + m.forward * len * 0.15f;
                from.y = Mathf.Max(Ground(from.x, from.z) + 40, m.position.y - 60);
                string n = m.name.Split(' ')[1];
                Shot(cam, rt, dir, $"world_bridge{n}", from, m.position + Vector3.down * 40);
                Vector3 low = m.position - m.right * 90 + m.forward * 60;
                low.y = Mathf.Max(Ground(low.x, low.z) + 15, m.position.y - 25);
                Shot(cam, rt, dir, $"world_bridge{n}_close", low, m.position);
            }

            var village = GameObject.Find("Village");
            if (village != null)
            {
                Vector3 c = village.GetComponent<MeshRenderer>().bounds.center;
                Shot(cam, rt, dir, "world_village", c + new Vector3(-420, 160, -520), c);
            }

            var eye = TestWorldBuilder.FindDeep(Object.FindFirstObjectByType<Flight.AircraftController>().transform, "CockpitCamera");
            var look = Object.FindFirstObjectByType<Pilot.PilotHeadLook>();
            if (look != null)
            {
                look.hideHead = true;
                typeof(Pilot.PilotHeadLook).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(look, null);
            }
            if (eye != null)
            {
                cam.nearClipPlane = 0.03f;
                cam.fieldOfView = 70f;
                Shot(cam, rt, dir, "world_cockpit", eye.position, eye.position + eye.forward * 100f + Vector3.down * 8f);
            }

            cam.targetTexture = null;
            Object.DestroyImmediate(camGo);
            Debug.Log($"OODA shots: world saved to {dir} ({marks.Count} bridges).");
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
