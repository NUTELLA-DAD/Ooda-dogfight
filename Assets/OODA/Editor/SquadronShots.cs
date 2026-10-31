using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Ooda.Flight;
using Ooda.Flight.Core;
using Ooda.Turn;
using Ooda.Turn.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Ooda.EditorTools
{
    /// <summary>
    /// OODA > Squadron Shots: plays the squadron battle in the turn battle scene by hand (batch mode has no play loop):
    /// renders turn 0 from behind the player, flies turns with the player's arrow straight ahead while the AI pilots
    /// think on their threads (timed), renders a flak gun the first time one fires and the fight once the 109s reach
    /// the formation, and a close-up of a Bf 109 and its wolf. PNGs go to Shots~.
    /// </summary>
    public static class SquadronShots
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [MenuItem("OODA/Squadron Shots")]
        public static void Capture()
        {
            EditorSceneManager.OpenScene("Assets/OODA/Scenes/TurnBattle.unity");
            var all = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var name in new[] { "Awake", "OnEnable", "Start" }) foreach (var b in all) Call(b, name);
            all = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);   // the squadrons made at the start
            Frames(all, 5);
            var tb = Object.FindFirstObjectByType<TurnBattle>();
            string dir = Path.GetFullPath("Shots~");
            Directory.CreateDirectory(dir);
            var cam = Camera.main;
            var rt = new RenderTexture(1600, 1000, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;
            var camScripts = cam.GetComponents<MonoBehaviour>();

            Report(tb, "turn 0");
            Save(cam, rt, dir, "squad_turn0");

            bool flakShot = false, metShot = false;
            var thinkMs = new System.Collections.Generic.List<long>();
            for (int turn = 1; turn <= 14 && Phase(tb) != "Over"; turn++)
            {
                var sw = Stopwatch.StartNew();
                Call(tb, "Commit");
                while (Phase(tb) == "Thinking" && sw.ElapsedMilliseconds < 60000)
                {
                    System.Threading.Thread.Sleep(5);
                    Call(tb, "Update");
                }
                thinkMs.Add(sw.ElapsedMilliseconds);
                var last = typeof(TurnBattle).GetField("last", Any).GetValue(tb) as TurnResult;
                float turnSec = (float)(last.FrameCount * last.dt);
                for (int step = 0; step < 40 && Phase(tb) == "Playing"; step++)
                {
                    float t = Mathf.Min(turnSec, (step + 1) * turnSec / 20f) - 0.0001f;
                    typeof(TurnBattle).GetField("playT", Any).SetValue(tb, t);
                    Call(tb, "Playback");
                    Frames(all, 1);
                    if (!flakShot)
                    {
                        int frame = Mathf.Clamp(Mathf.FloorToInt(t / (float)last.dt), 0, last.FrameCount - 1);
                        var trace = last.traces.FirstOrDefault(tr => tr.owner < 0 && tr.At(frame).HasValue && tr.At(frame - 6).HasValue);
                        if (trace != null && tb.flakSites != null && -1 - trace.owner < tb.flakSites.Length)
                        {
                            var gun = tb.flakSites[-1 - trace.owner];
                            var round = UnityFrame.Pos(trace.At(frame).Value);
                            Vector3 dirToRound = (round - gun.position).normalized;
                            foreach (var c in camScripts) c.enabled = false;
                            // beside and behind the gun, looking along its fire, and never under the ground
                            Vector3 flat = new Vector3(dirToRound.x, 0, dirToRound.z).normalized;
                            Vector3 at = gun.position - flat * 22f + Vector3.Cross(flat, Vector3.up) * 12f;
                            var terrain = Object.FindFirstObjectByType<Terrain>();
                            at.y = terrain.SampleHeight(at) + terrain.transform.position.y + 5f;
                            cam.transform.position = at;
                            cam.transform.LookAt(gun.position + dirToRound * 150f + Vector3.up * 2f);
                            Frames(all.Where(b => b != null && b.gameObject != cam.gameObject).ToArray(), 1);   // lines sized for this camera
                            Save(cam, rt, dir, "squad_flak");
                            Debug.Log($"OODA squadron: flak gun {-1 - trace.owner} at ({gun.position.x:F0}, {gun.position.y:F0}, {gun.position.z:F0}) firing in turn {turn}.");
                            foreach (var c in camScripts) c.enabled = true;
                            flakShot = true;
                        }
                    }
                }
                Frames(all, 3);
                float closest = Closest(tb);
                Report(tb, $"after turn {turn} (thinking {thinkMs[thinkMs.Count - 1]} ms, {last.FrameCount} frames, {last.hits.Count} hits, {last.traces.Count(tr => tr.owner < 0)} flak packets), nearest 109 to a bomber {closest:F0} m");
                if (!metShot && (turn >= 6 && closest < 1500f || turn == 10))
                {
                    Save(cam, rt, dir, "squad_meet");
                    metShot = true;
                }
            }
            if (!metShot) Save(cam, rt, dir, "squad_meet");
            Debug.Log($"OODA squadron: pilots thinking took {string.Join(", ", thinkMs)} ms per turn (mean {thinkMs.Average():F0} ms) on this machine, {System.Environment.ProcessorCount} logical cores.");

            // a Bf 109 and its wolf, close
            var bf = Object.FindObjectsByType<TurnAircraftView>(FindObjectsSortMode.None).Where(v => v.name.StartsWith("Bf109") && v.gameObject.activeInHierarchy).FirstOrDefault();
            if (bf != null)
            {
                foreach (var c in camScripts) c.enabled = false;
                var t = bf.transform;
                cam.nearClipPlane = 0.05f;
                cam.fieldOfView = 35f;
                cam.transform.position = t.position + t.right * 6.5f + t.up * 2.2f + t.forward * 3f;
                cam.transform.LookAt(t.position + t.up * 0.6f + t.forward * 0.4f);
                Frames(all.Where(b => b != null && b.gameObject != cam.gameObject).ToArray(), 1);
                Save(cam, rt, dir, "squad_bf109");
                cam.transform.position = t.position + t.right * 2.2f + t.up * 1.6f - t.forward * 0.3f;
                cam.transform.LookAt(t.position + t.up * 0.7f + t.forward * 0.4f);
                Save(cam, rt, dir, "squad_bf109_wolf");
            }
            cam.targetTexture = null;
            Debug.Log($"OODA squadron: shots saved to {dir}");
        }

        /// <summary>
        /// OODA > Marks Shot: the planning screen with two timed marks (level for 1 s, then roll 60 right and pull 4 G), set
        /// straight on the plan since batch mode has no keyboard, with the slider at 1.5 s; logs whether the arrow tip hides.
        /// </summary>
        [MenuItem("OODA/Marks Shot")]
        public static void MarksShot()
        {
            EditorSceneManager.OpenScene("Assets/OODA/Scenes/TurnBattle.unity");
            var all = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var name in new[] { "Awake", "OnEnable", "Start" }) foreach (var b in all) Call(b, name);
            all = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            Frames(all, 5);
            var tb = Object.FindFirstObjectByType<TurnBattle>();
            string dir = Path.GetFullPath("Shots~");
            var cam = Camera.main;
            var rt = new RenderTexture(1600, 1000, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;
            var tip = (Transform)typeof(TurnBattle).GetField("tip", Any).GetValue(tb);
            Debug.Log($"OODA marks: before, the arrow tip is {(tip.gameObject.activeSelf ? "shown" : "hidden")}.");
            Save(cam, rt, dir, "marks_none");

            var planField = typeof(TurnBattle).GetField("plan", Any);
            var plan = (TurnPlan)planField.GetValue(tb);
            plan.marks = new[] { new Mark(0, 0, 1), new Mark(1, 60, 4) };
            planField.SetValue(tb, plan);
            typeof(TurnBattle).GetField("markT", Any).SetValue(tb, 1.5f);
            typeof(TurnBattle).GetField("dirty", Any).SetValue(tb, true);
            Frames(all, 5);
            var after = (TurnPlan)planField.GetValue(tb);
            Debug.Log($"OODA marks: plan has {after.marks?.Length ?? 0} marks ({string.Join("; ", (after.marks ?? new Mark[0]).Select(k => $"{k.time:F1} s bank {k.bankDeg:F0} {k.g:F1} G"))}); the arrow tip is {(tip.gameObject.activeSelf ? "SHOWN" : "hidden")}; markT {typeof(TurnBattle).GetField("markT", Any).GetValue(tb)}.");
            Save(cam, rt, dir, "marks_two");
            cam.targetTexture = null;
        }

        /// <summary>
        /// OODA > Timeline Shot: a timeline of four points, each setting only some controls (level at 0 s; bank 60 right at 1 s;
        /// pull 4 G at 1.6 s; throttle 30 % and half right rudder at 2.2 s), the slider at 1.6 s; renders the ghost and logs bank,
        /// G and throttle from the preview at 1, 2 and 3 s.
        /// </summary>
        [MenuItem("OODA/Timeline Shot")]
        public static void TimelineShot()
        {
            EditorSceneManager.OpenScene("Assets/OODA/Scenes/TurnBattle.unity");
            var all = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var name in new[] { "Awake", "OnEnable", "Start" }) foreach (var b in all) Call(b, name);
            all = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            Frames(all, 5);
            var tb = Object.FindFirstObjectByType<TurnBattle>();
            var cam = Camera.main;
            var rt = new RenderTexture(1600, 1000, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;

            var planField = typeof(TurnBattle).GetField("plan", Any);
            var plan = (TurnPlan)planField.GetValue(tb);
            plan.marks = new[]
            {
                new Mark(0, 0, 1),
                new Mark(1.0) { bankDeg = 60 },
                new Mark(1.6) { g = 4 },
                new Mark(2.2) { throttle = 0.3, rudder = 0.5 },
            };
            planField.SetValue(tb, plan);
            typeof(TurnBattle).GetField("markT", Any).SetValue(tb, 1.6f);
            typeof(TurnBattle).GetField("dirty", Any).SetValue(tb, true);
            Frames(all, 5);
            var tip = (Transform)typeof(TurnBattle).GetField("tip", Any).GetValue(tb);
            var preview = typeof(TurnBattle).GetField("preview", Any).GetValue(tb) as TurnResult;
            if (preview == null) { Debug.LogError("OODA timeline: no preview."); return; }
            foreach (double s in new[] { 0.0, 1.0, 1.6, 2.0, 2.2, 3.0 })
            {
                int i = Mathf.Clamp((int)System.Math.Round(s / preview.dt), 0, preview.FrameCount - 1);
                var f = preview.frames[0][i];
                Vector3 fwd = UnityFrame.Dir(f.vel).normalized, up = UnityFrame.Dir(f.up).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
                float bank = Mathf.Atan2(Vector3.Dot(up, right), Vector3.Dot(up, Vector3.Cross(fwd, right).normalized)) * Mathf.Rad2Deg;
                float kias = (float)Atmosphere.TasToIasKt(f.vel.Length * Atmosphere.FtpsToKt, f.pos.z);
                Debug.Log($"OODA timeline: at {s:F1} s (frame {i}): bank {bank:F0} (+ right), {f.g:F2} G, throttle {f.throttle:F2}, {kias:F0} kias, alpha {f.alphaDeg:F1}.");
            }
            Debug.Log($"OODA timeline: {((TurnPlan)planField.GetValue(tb)).marks.Length} marks; arrow tip {(tip.gameObject.activeSelf ? "SHOWN" : "hidden")}.");
            Save(cam, rt, Path.GetFullPath("Shots~"), "timeline_ghost");
            cam.targetTexture = null;
        }

        static float Closest(TurnBattle tb)
        {
            var battle = (Battle)typeof(TurnBattle).GetField("battle", Any).GetValue(tb);
            float best = float.MaxValue;
            for (int i = 0; i < battle.Count; i++)
                for (int j = 0; j < battle.Count; j++)
                    if (battle.routes[i] == null && Side(battle, i) == 1 && battle.routes[j] != null && !battle.Out(i) && !battle.Out(j))
                        best = Mathf.Min(best, (float)((battle.planes[i].state.pos - battle.planes[j].state.pos).Length * 0.3048));
            return best;
        }

        static int Side(Battle b, int i)
        {
            var f = typeof(Battle).GetField("side", Any) ?? typeof(Battle).GetField("sides", Any);
            return f?.GetValue(b) is int[] s ? s[i] : (i % 2);
        }

        static string Phase(TurnBattle tb) => typeof(TurnBattle).GetField("phase", Any).GetValue(tb).ToString();

        static void Report(TurnBattle tb, string when)
        {
            var battle = (Battle)typeof(TurnBattle).GetField("battle", Any).GetValue(tb);
            int out0 = 0, out1 = 0, n0 = 0, n1 = 0;
            for (int i = 0; i < battle.Count; i++)
            {
                if (Side(battle, i) == 0) { n0++; if (battle.Out(i)) out0++; } else { n1++; if (battle.Out(i)) out1++; }
            }
            var p = UnityFrame.Pos(battle.planes[0].state.pos);
            Debug.Log($"OODA squadron {when}: phase {Phase(tb)}, {battle.Count} aircraft (side 0: {n0}, {out0} out; side 1: {n1}, {out1} out); player at ({p.x:F0}, {p.y:F0}, {p.z:F0}).");
        }

        static void Frames(MonoBehaviour[] all, int n)
        {
            for (int i = 0; i < n; i++)
            {
                foreach (var b in all) if (b != null && b.isActiveAndEnabled) Call(b, "Update");
                foreach (var b in all) if (b != null && b.isActiveAndEnabled) Call(b, "LateUpdate");
            }
        }

        static void Call(MonoBehaviour b, string method)
        {
            var m = b.GetType().GetMethod(method, Any, null, System.Type.EmptyTypes, null);
            if (m == null) return;
            try { m.Invoke(b, null); }
            catch (TargetInvocationException e) { Debug.LogWarning($"OODA squadron: {b.GetType().Name}.{method} threw {e.InnerException?.GetType().Name}: {e.InnerException?.Message}"); }
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
