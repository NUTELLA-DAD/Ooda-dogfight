using System.IO;
using System.Reflection;
using Ooda.Fx;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ooda.EditorTools
{
    /// <summary>
    /// OODA > Effects Shots: opens the TurnBattle scene, wakes its scripts by hand (batch mode has no play loop) and
    /// stages the toon effects (ToonFx) at known ages on a held clock: a blast in the air as it goes from white-hot to
    /// smoke, a hit, tracers and a missile's smoke, a hurt and a falling aircraft's trails, a wreck burning on the ground and a bombed factory's
    /// columns. PNGs go to Shots~ (fx_*.png).
    /// </summary>
    public static class FxShots
    {
        const float T = 100f;   // the held clock, s

        [MenuItem("OODA/Effects Shots")]
        public static void Capture()
        {
            EditorSceneManager.OpenScene("Assets/OODA/Scenes/TurnBattle.unity");
            var behaviours = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var name in new[] { "Awake", "OnEnable", "Start" })
                foreach (var b in behaviours) Call(b, name);
            for (int i = 0; i < 3; i++)
            {
                foreach (var b in behaviours) Call(b, "Update");
                foreach (var b in behaviours) Call(b, "LateUpdate");
            }

            string dir = Path.GetFullPath("Shots~");
            Directory.CreateDirectory(dir);
            var cam = Camera.main;
            foreach (var mb in cam.GetComponents<MonoBehaviour>()) mb.enabled = false;
            var rt = new RenderTexture(1600, 1000, 24);
            cam.targetTexture = rt;
            cam.fieldOfView = 50f;
            var fx = ToonFx.Get();
            ToonFx.ClockOverride = T;

            // a spot in open sky ahead of the player, and the camera off to its side
            var plane = Object.FindFirstObjectByType<Ooda.Turn.TurnAircraftView>();
            Vector3 sky = plane != null ? plane.transform.position + plane.transform.forward * 60f : new Vector3(0, 900, 0);
            Vector3 fwd = plane != null ? Vector3.ProjectOnPlane(plane.transform.forward, Vector3.up).normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, fwd);

            // 1. a blast in the air (an aircraft going up), up close and from the chase distance
            foreach (var (age, tag) in new[] { (0.02f, "a0"), (0.2f, "a1"), (0.45f, "a2"), (0.9f, "a3"), (2.0f, "a4") })
            {
                fx.Clear();
                fx.Blast(sky, 2.5f, t0: T - age);
                Look(cam, sky - side * 45f + Vector3.up * 6f, sky + Vector3.up * 8f);
                Save(fx, cam, rt, dir, "fx_blast_" + tag);
            }
            fx.Clear();
            fx.Blast(sky, 2.5f, t0: T - 0.3f);
            Look(cam, sky - side * 260f + Vector3.up * 40f, sky);
            Save(fx, cam, rt, dir, "fx_blast_far");

            // 2. hits along a line
            fx.Clear();
            for (int k = 0; k < 4; k++) fx.Hit(sky + fwd * (k * 6f) + Vector3.up * (k * 1.5f), 1f, T - k * 0.07f);
            Look(cam, sky - side * 30f + fwd * 9f, sky + fwd * 9f);
            Save(fx, cam, rt, dir, "fx_hits");

            // 2b. a burst of tracers going past, and a missile's smoke, as playback lays them (frames 0.02 s apart)
            fx.Clear();
            const float Dt = 0.02f;
            for (int j = 0; j < 8; j++)   // a packet every 0.05 s, 880 m/s, the oldest furthest on
            {
                float age = j * 0.05f;
                Vector3 origin = sky - fwd * 120f + side * 8f + Vector3.up * 3f;
                Vector3 Pos(float a) => origin + fwd * (880f * (age - a + 0.06f)) + Vector3.down * (4.9f * (age - a) * (age - a));
                fx.Tracer(Pos(3 * Dt), Pos(0f), new Color(1f, 0.75f, 0.2f, 0.9f), 100 + j);
            }
            var path = new System.Collections.Generic.List<Vector3>();
            for (int k = 0; k * Dt < ToonFx.MissileSmokeLife; k++) path.Add(sky + Vector3.up * 30f + fwd * (60f - k * Dt * 320f) + side * (0.0005f * k * k));
            fx.Tracer(path[0] - fwd * 5f, path[0], new Color(1f, 0.6f, 0.2f, 1f), 900);
            fx.MissileSmoke(path, 0f, Dt, 900, 60000, 1000);
            Look(cam, sky - side * 120f + Vector3.up * 15f, sky + Vector3.up * 12f);
            Save(fx, cam, rt, dir, "fx_tracers");

            // 3. trails: a hurt aircraft's grey, a falling one's black, laid down as the view does at 150 m/s
            fx.Clear();
            Trail(fx, sky + Vector3.up * 20f, fwd, side, false, 1);
            Trail(fx, sky - Vector3.up * 25f, fwd, side, true, 2);
            Look(cam, sky - side * 220f + Vector3.up * 10f - fwd * 120f, sky + fwd * 60f);
            Save(fx, cam, rt, dir, "fx_trails");

            // 4. a wreck burning on the ground, and a bombed factory: blasts going off and columns that stay
            var terrain = Terrain.activeTerrain;
            Vector3 ground = sky + fwd * 400f;
            if (terrain != null) ground.y = terrain.SampleHeight(ground) + terrain.transform.position.y;
            fx.Clear();
            fx.Blast(ground, 3f, ground: true, t0: T - 0.25f);
            fx.SmokeColumn(ground + side * 30f, 4f, 25f, T - 6f);
            Look(cam, ground - fwd * 110f + Vector3.up * 35f, ground + Vector3.up * 8f + side * 15f);
            Save(fx, cam, rt, dir, "fx_wreck");
            fx.Clear();
            for (int k = 0; k < 8; k++) fx.Blast(ground + side * (k * 22f - 80f) + fwd * ((k % 3) * 20f), 6.5f, ground: true, t0: T - 0.1f - (7 - k) * 0.18f);
            for (int k = 0; k < 3; k++) fx.SmokeColumn(ground + side * (k * 50f - 50f) - fwd * 40f, 13f, -1f, T - 8f - k);
            Look(cam, ground - fwd * 520f + Vector3.up * 140f, ground + Vector3.up * 60f);
            Save(fx, cam, rt, dir, "fx_factory");

            ToonFx.ClockOverride = -1f;
            cam.targetTexture = null;
            Debug.Log($"OODA shots: effects saved to {dir}, {fx.Triangles} triangles in the last");
        }

        // a trail as TurnAircraftView lays it: a puff every 0.9 r metres along a gentle curve, the newest at the end
        static void Trail(ToonFx fx, Vector3 end, Vector3 fwd, Vector3 side, bool heavy, int cluster)
        {
            float r = heavy ? 3.2f : 1.6f, spacing = r * 0.9f, speed = 150f;
            int n = Mathf.FloorToInt(450f / spacing);
            for (int k = 0; k < n; k++)
            {
                float back = k * spacing;
                Vector3 p = end - fwd * back + side * (0.0004f * back * back) - Vector3.up * (heavy ? -0.08f * back : 0f);
                fx.TrailPuff(p, new Vector3(0f, 0.6f, 0f), r, heavy, cluster, k, T - back / speed);
            }
        }

        static void Look(Camera cam, Vector3 from, Vector3 at)
        {
            cam.transform.position = from;
            cam.transform.LookAt(at);
        }

        static void Save(ToonFx fx, Camera cam, RenderTexture rt, string dir, string name)
        {
            fx.Draw(cam);
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
            catch (TargetInvocationException e) { Debug.LogWarning($"OODA shots: {b.GetType().Name}.{method} threw {e.InnerException?.GetType().Name}: {e.InnerException?.Message}"); }
        }
    }
}
