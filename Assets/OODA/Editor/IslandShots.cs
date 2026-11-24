using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ooda.EditorTools
{
    /// <summary>
    /// OODA > Island Shots (or -executeMethod Ooda.EditorTools.IslandShots.Capture): the whole island from free cameras,
    /// with the land drawn at run time (the east lands, islets, towns, roads and airfields) made the way the game makes it,
    /// rendered to 1920x1080 JPGs in IslandShots at the repo root.
    /// </summary>
    public static class IslandShots
    {
        [MenuItem("OODA/Island Shots")]
        public static void Capture()
        {
            var scene = EditorSceneManager.OpenScene("Assets/OODA/Scenes/TurnBattle.unity");
            if (Ooda.Fx.ToonSky.Enabled) Ooda.Fx.ToonSky.Apply(scene);
            Ooda.Fx.ToonLook.Apply();
            Ooda.Fx.CityScape.Ensure();
            Ooda.Fx.EastlandScape.Ensure();

            string dir = Path.GetFullPath("IslandShots");
            Directory.CreateDirectory(dir);
            var rt = new RenderTexture(1920, 1080, 24) { antiAliasing = 4 };
            var camGo = new GameObject("IslandShotCam");
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 1f; cam.fieldOfView = 55f;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.targetTexture = rt;
            Ooda.Fx.ToonLook.Dress(cam);

            // the whole island: from that far the haze and the clouds would hide it, so both are put away for it
            var clouds = GameObject.Find("Toon clouds");
            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            if (clouds != null) clouds.SetActive(false);
            cam.farClipPlane = 160000f;
            Shot(cam, rt, dir, "a_whole_island", new Vector3(8000, 24000, -40000), new Vector3(8000, 0, -1000));
            Shot(cam, rt, dir, "a2_whole_island_low", new Vector3(8000, 9000, -30000), new Vector3(8000, 0, 0));
            RenderSettings.fog = fog;
            if (clouds != null) clouds.SetActive(true);
            cam.farClipPlane = 60000f;
            Shot(cam, rt, dir, "b_volcano_isle", new Vector3(-10500, 1000, 5500), new Vector3(-12700, 300, 7700));
            Shot(cam, rt, dir, "c_red_canyon", new Vector3(19600, 300, -1900), new Vector3(25800, 40, -5300));
            Shot(cam, rt, dir, "d_dam_reservoir", new Vector3(10750, 380, 6200), new Vector3(10750, 46, 4000));
            Shot(cam, rt, dir, "d2_dam_close", new Vector3(10750, 160, 5500), new Vector3(10750, 40, 4580));
            Shot(cam, rt, dir, "e_harbour_town", new Vector3(-10600, 750, -2600), new Vector3(-7600, 9, -2550));
            Shot(cam, rt, dir, "f_crossroads", new Vector3(10100, 330, -3800), new Vector3(11000, 26, -2700));
            Shot(cam, rt, dir, "g_oasis_desert_strip", new Vector3(16300, 900, -9900), new Vector3(18600, 30, -6800));
            Shot(cam, rt, dir, "h_city_airport", new Vector3(20300, 450, 3300), new Vector3(21650, 14, 4800));
            Shot(cam, rt, dir, "i_island_strip", new Vector3(-11400, 520, -10100), new Vector3(-12200, 12, -8300));
            Shot(cam, rt, dir, "j_causeway", new Vector3(25800, 420, -3300), new Vector3(28000, 5, -1900));
            Shot(cam, rt, dir, "k_west_coast", new Vector3(-17500, 2600, -9500), new Vector3(-10000, 0, -500));
            Shot(cam, rt, dir, "k2_west_coast_north", new Vector3(-17500, 2600, 9500), new Vector3(-10000, 0, 500));
            Shot(cam, rt, dir, "n_east_road_join", new Vector3(11800, 1100, -1200), new Vector3(9400, 0, -4200));
            Shot(cam, rt, dir, "m_islets_south_west_top", new Vector3(-12000, 7000, -6600), new Vector3(-12000, 0, -6500));
            Shot(cam, rt, dir, "l_alps_east_seam", new Vector3(10000, 1800, -12500), new Vector3(10000, 0, 0));
            // the sights: the bridge, the arch, the fleet, the city's balloons
            Shot(cam, rt, dir, "p_harbour_bridge", new Vector3(-10500, 220, -3600), new Vector3(-9050, 70, -2610));
            Shot(cam, rt, dir, "p2_under_the_bridge", new Vector3(-10200, 30, -2610), new Vector3(-7600, 30, -2550));
            Shot(cam, rt, dir, "q_rock_arch", new Vector3(-12650, 60, 700), new Vector3(-12650, 55, 1650));
            Shot(cam, rt, dir, "q2_rock_arch_high", new Vector3(-13500, 420, 900), new Vector3(-12650, 60, 1650));
            Shot(cam, rt, dir, "r_fleet", new Vector3(1900, 260, -14100), new Vector3(3000, 10, -12900));
            Shot(cam, rt, dir, "r2_carrier", new Vector3(2650, 70, -13150), new Vector3(3000, 18, -12900));
            Shot(cam, rt, dir, "s_city_balloons", new Vector3(10300, 560, 1300), new Vector3(11800, 470, 4600));
            Shot(cam, rt, dir, "s2_balloon_close", new Vector3(11250, 590, 2700), new Vector3(11350, 606, 2800));

            cam.targetTexture = null;
            Object.DestroyImmediate(camGo);
            Debug.Log($"OODA island shots: saved to {dir}");
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
