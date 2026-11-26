using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ooda.EditorTools
{
    /// <summary>
    /// OODA > Carrier Shots (or -executeMethod Ooda.EditorTools.CarrierShots.Capture): the aircraft carrier at anchor by
    /// Volcano Isle, built the way the game builds it (IslandDress), from four cameras: close over the bow, low along its
    /// side, a fighter's view at the Volcano Isle arena's height with the cone behind, and straight down on the deck.
    /// 1920x1080 JPGs in CarrierShots at the repo root.
    /// </summary>
    public static class CarrierShots
    {
        [MenuItem("OODA/Carrier Shots")]
        public static void Capture()
        {
            var scene = EditorSceneManager.OpenScene("Assets/OODA/Scenes/TurnBattle.unity");
            if (Ooda.Fx.ToonSky.Enabled) Ooda.Fx.ToonSky.Apply(scene);
            Ooda.Fx.ToonLook.Apply();
            Ooda.Fx.CityScape.Ensure();
            Ooda.Fx.EastlandScape.Ensure();
            if (Ooda.Fx.IslandDress.CarrierAt == null) { Debug.LogError("OODA carrier shots: no carrier was built (see the log)."); return; }
            Vector3 c = Ooda.Fx.IslandDress.CarrierAt.Value;
            var rot = Quaternion.Euler(0, Ooda.Fx.IslandDress.CarrierHeadingDeg, 0);
            Vector3 P(float right, float up, float fwd) => c + rot * new Vector3(right, up, fwd);

            string dir = Path.GetFullPath("CarrierShots");
            Directory.CreateDirectory(dir);
            var rt = new RenderTexture(1920, 1080, 24) { antiAliasing = 4 };
            var camGo = new GameObject("CarrierShotCam");
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.5f; cam.farClipPlane = 60000f; cam.fieldOfView = 50f;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.targetTexture = rt;
            Ooda.Fx.ToonLook.Dress(cam);

            Shot(cam, rt, dir, "carrier_1_bow", P(-150, 70, 230), P(0, 15, 10));
            Shot(cam, rt, dir, "carrier_2_side_low", P(260, 18, -60), P(0, 20, 20));
            Shot(cam, rt, dir, "carrier_3_fighter_view", P(500, 1100, -1300), P(0, 0, 200));
            Shot(cam, rt, dir, "carrier_4_volcano_behind", c + new Vector3(2200, 500, -2400), new Vector3(-12700, 250, 7700));
            Shot(cam, rt, dir, "carrier_5_deck_top", P(0, 420, 0.1f), P(0, 0, 0));

            cam.targetTexture = null;
            Object.DestroyImmediate(camGo);
            Debug.Log($"OODA carrier shots: saved to {dir}");
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
            File.WriteAllBytes(Path.Combine(dir, name + ".jpg"), tex.EncodeToJPG(90));
            Object.DestroyImmediate(tex);
        }
    }
}
