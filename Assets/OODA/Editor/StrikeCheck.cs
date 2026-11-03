using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ooda.Flight;
using Ooda.Flight.Core;
using Ooda.Turn;
using Ooda.Turn.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ooda.EditorTools
{
    /// <summary>
    /// OODA > Strike Check: opens the turn battle and flies the Town Strike headless from a few planned paths, every
    /// aircraft flown by the AI (you hold your place in the formation until it breaks), logging when and by what the
    /// bombers are seen, the take-offs, the bombers' lowest clearance and speed, and how it ends; and renders the town
    /// to PNGs in Shots~.
    /// </summary>
    public static class StrikeCheck
    {
        [MenuItem("OODA/Strike Check")]
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/OODA/Scenes/TurnBattle.unity");
            var terrain = Object.FindFirstObjectByType<Terrain>();
            var town = GameObject.Find("Town").transform;
            Vector3 factory = town.Find("FactoryTarget").position, rs = town.Find("RunwayStart").position, re = town.Find("RunwayEnd").position;
            var radars = town.Cast<Transform>().Where(t => t.name.StartsWith("RadarSite")).Select(t => t.position).ToArray();
            var tb = Object.FindFirstObjectByType<TurnBattle>();
            var flak = tb.flakSites.Where(f => f != null).Select(f => Ft(f.position)).ToArray();
            double G(double x, double y) => (terrain.SampleHeight(new Vector3((float)(x * Atmosphere.MPerFt), 0, (float)(y * Atmosphere.MPerFt))) + terrain.transform.position.y) * Atmosphere.FtPerM;
            float GroundM(float x, float z) => Mathf.Max(0, terrain.SampleHeight(new Vector3(x, 0, z)) + terrain.transform.position.y);

            Coverage(radars, G, GroundM);
            foreach (double deg in new[] { 2.0, 3.0, 4.0, 5.0, 6.0 })
            {
                var cb = new Battle(new FlightModel(AircraftNumbers.P51D(), PointMass.Make(new Vec3d(0, 0, 3000), 300, 0)), CombatNumbers.P51D(),
                                    new FlightModel(AircraftNumbers.Bf109G(), PointMass.Make(new Vec3d(0, 90000, 3000), 300, 0)), CombatNumbers.Bf109G());
                var start = new Vec3d(0, -60000, 1500);
                int k = cb.Add(new FlightModel(AircraftNumbers.B17G(), PointMass.Make(start, Atmosphere.IasToTasKt(160, 1500), System.Math.PI / 2)) { throttle = 0.8 }, CombatNumbers.B17G(), 0,
                               new Route { kias = 160, maxClimbDeg = deg, points = new[] { new Vec3d(0, 200000, 1500 + 200000 * System.Math.Tan(deg * System.Math.PI / 180)) }, next = 0 });
                string log = "";
                for (int t = 0; t < 30; t++) { cb.Resolve(new TurnPlan?[cb.Count], 3, false); if (t % 5 == 4) log += $" {cb.planes[k].Kias:F0}kt/{cb.planes[k].AltFt:F0}ft"; }
                Debug.Log($"OODA strike: B-17 climbing at {deg} degrees: {log}");
            }

            // the same ring the battle works out for the patrol
            Vector2 c = new Vector2(factory.x, factory.z);
            float bestR = 5500, bestTop = float.MaxValue;
            for (float r = 4500; r <= 6500; r += 250)
            {
                float top = 0;
                for (int a = 0; a < 360; a += 2) top = Mathf.Max(top, GroundM(c.x + Mathf.Cos(a * Mathf.Deg2Rad) * r, c.y + Mathf.Sin(a * Mathf.Deg2Rad) * r));
                if (top < bestTop) { bestTop = top; bestR = r; }
            }
            float alt = Mathf.Max(1500f, bestTop + 400f);
            var loop = Enumerable.Range(0, 16).Select(k => { float a = -k * 2 * Mathf.PI / 16; return Ft(new Vector3(c.x + Mathf.Cos(a) * bestR, alt, c.y + Mathf.Sin(a) * bestR)); }).ToArray();
            Debug.Log($"OODA strike: patrol ring {bestR:F0} m round the factory at {alt:F0} m (highest ground on it {bestTop:F0} m).");

            float Low(float x, float z) => GroundM(x, z) + 250f;
            var plans = new (string name, Vector3[] pts)[]
            {
                ("south, low up the main valley", new[] { new Vector3(0, Low(0, -9400), -9400), new Vector3(0, Low(0, -5000), -5000) }),
                ("east, high and straight", new[] { new Vector3(9400, 2400, 1500) }),
                ("west, low, across the lake and the farmland", new[] { new Vector3(-9400, Low(-9400, -3000), -3000), new Vector3(-2500, Low(-2500, -2500), -2500), new Vector3(1000, Low(1000, -3500), -3500) }),
            };
            foreach (var (name, pts) in plans)
            {
                var route = new List<Vector3>(pts);
                Vector3 last = route[route.Count - 1];
                float fy = last.y > 2000 ? 2400 : GroundM(factory.x, factory.z) + 250;
                route.Add(new Vector3(factory.x, fy, factory.z));
                Vector3 on = factory + (factory - last).normalized * 4000f; on.y = fy;
                route.Add(on);
                Fly(name, Scenarios.TownStrike(route.Select(Ft).ToArray(), Ft(factory), radars.Select(Ft).ToArray(), loop, Ft(rs), Ft(re), flak), G);
            }

            Shots(factory, rs, re, radars);
        }

        static Vec3d Ft(Vector3 m) => new Vec3d(m.x * Atmosphere.FtPerM, m.z * Atmosphere.FtPerM, m.y * Atmosphere.FtPerM);

        static void Coverage(Vector3[] radars, System.Func<double, double, double> g, System.Func<float, float, float> groundM)
        {
            var r = radars.Select(Ft).ToArray();
            double range = new Strike().radarRangeFt;
            foreach (var (lv, h) in new[] { ("low", -250f), ("medium", 1200f), ("high", 2400f) })
            {
                int seen = 0, open = 0;
                for (float x = -9000; x <= 9000; x += 250)
                    for (float z = -9000; z <= 9000; z += 250)
                    {
                        float gm = groundM(x, z), y = h < 0 ? gm - h : h;
                        if (y < gm + 50) continue;
                        open++;
                        var p = Ft(new Vector3(x, y, z));
                        if (r.Any(q => (p - q).Length < range && Strike.Clear(g, q, p))) seen++;
                    }
                Debug.Log($"OODA strike: radar sees {100f * seen / Mathf.Max(1, open):F0} % of the island at {lv} (of the {open} spots you can fly at that height).");
            }
        }

        static void Fly(string name, Battle b, System.Func<double, double, double> g)
        {
            b.groundFt = g;
            int n = b.Count;
            var ais = new EnemyAi[n];
            for (int i = 0; i < n; i++)
                if (b.routes[i] == null)
                    ais[i] = new EnemyAi { me = i, rng = new Lcg((uint)(77 + 13 * i)), ignoreObstacles = true, role = b.side[i] == 0 ? (i % 2 == 0 ? EnemyAi.Role.Escort : EnemyAi.Role.Fighter) : (i % 2 == 1 ? EnemyAi.Role.Hunter : EnemyAi.Role.Fighter) };
            var s = b.strike;
            bool wasAlert = false; int wasLaunched = 0, wasStruck = 0;
            double minClear = double.MaxValue, minKias = double.MaxValue;
            bool Flying() => Enumerable.Range(0, n).Any(i => b.routes[i] != null && !b.Out(i) && !s.struck[i] && !b.routes[i].Done);
            for (int turn = 0; turn < 90 && Flying(); turn++)   // on even if you go down: this checks the bombers
            {
                var plans = new TurnPlan?[n];
                for (int i = 0; i < n; i++)
                {
                    if (ais[i] == null || b.Out(i) || b.InFormation(i) || s.OnPatrol(i)) continue;   // the escorts hold formation, as with no orders
                    plans[i] = ais[i].Plan(b, 3);
                }
                b.Resolve(plans, 3, false, (bb, k) =>
                {
                    for (int i = 0; i < bb.Count; i++)
                        if (bb.routes[i] != null && !bb.Out(i))
                        {
                            var m = bb.planes[i];
                            minClear = System.Math.Min(minClear, m.AltFt - g(m.state.pos.x, m.state.pos.y));
                            minKias = System.Math.Min(minKias, m.Kias);
                        }
                });
                if (s.alerted && !wasAlert) Debug.Log($"OODA strike [{name}]: turn {turn + 1}, seen by the {s.alertBy} {s.alertDistFt * Atmosphere.MPerFt / 1000:F1} km from the factory.");
                if (s.launched != wasLaunched) Debug.Log($"OODA strike [{name}]: turn {turn + 1}, {s.launched} of {s.scramble.Length} off the runway.");
                if (s.Struck != wasStruck) Debug.Log($"OODA strike [{name}]: turn {turn + 1}, factory hit by {s.Struck}.");
                wasAlert = s.alerted; wasLaunched = s.launched; wasStruck = s.Struck;
            }
            int bombers = Enumerable.Range(0, n).Count(i => b.routes[i] != null), lost = Enumerable.Range(0, n).Count(i => b.routes[i] != null && b.Out(i));
            int foesDown = Enumerable.Range(0, n).Count(i => b.side[i] == 1 && (b.down[i] || b.crashed[i]));
            int oursDown = Enumerable.Range(0, n).Count(i => b.side[i] == 0 && b.routes[i] == null && (b.down[i] || b.crashed[i]));
            Debug.Log($"OODA strike [{name}]: over after turn {b.turn} (you {(b.Out(0) ? "down" : "flying")}); factory hit by {s.Struck} of {bombers}, {lost} bombers lost, " +
                      $"{oursDown} P-51s and {foesDown} Bf 109s down; bombers' least clearance {minClear * Atmosphere.MPerFt:F0} m, slowest {minKias:F0} KIAS.");
        }

        static void Shots(Vector3 factory, Vector3 rs, Vector3 re, Vector3[] radars)
        {
            string dir = Path.GetFullPath("Shots~");
            Directory.CreateDirectory(dir);
            var camGo = new GameObject("ShotCam");
            var cam = camGo.AddComponent<Camera>();
            cam.farClipPlane = 60000f; cam.nearClipPlane = 0.5f; cam.fieldOfView = 50f;
            var rt = new RenderTexture(1600, 1000, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;
            void Shot(string name, Vector3 from, Vector3 at)
            {
                cam.transform.position = from; cam.transform.LookAt(at); cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            Shot("strike_town", factory + new Vector3(-200, 650, -1700), factory + new Vector3(-300, 0, 0));
            Shot("strike_factory", factory + new Vector3(250, 160, -330), factory);
            Shot("strike_runway", rs + new Vector3(-220, 90, -260), Vector3.Lerp(rs, re, 0.5f));
            Shot("strike_from_south", factory + new Vector3(0, 900, -9000), factory);
            for (int k = 0; k < radars.Length; k++) Shot($"strike_radar{k}", radars[k] + new Vector3(60, 25, -80), radars[k] + Vector3.down * 10);
            cam.targetTexture = null;
            Object.DestroyImmediate(camGo);
            Debug.Log($"OODA strike: shots saved to {dir}.");
        }
    }
}
