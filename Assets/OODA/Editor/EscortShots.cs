using System.IO;
using System.Linq;
using System.Reflection;
using Ooda.Flight;
using Ooda.Turn;
using Ooda.Turn.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ooda.EditorTools
{
    /// <summary>
    /// OODA > Escort Shots: plays the escort in the turn battle scene by hand (batch mode has no play loop): renders the
    /// start, commits a few turns with the arrow straight ahead, fast-forwarding each playback, renders the fight, and
    /// checks that M switches to the dogfight and back and that the B-17's propellers turn. PNGs go to Shots~.
    /// </summary>
    public static class EscortShots
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [MenuItem("OODA/Escort Shots")]
        public static void Capture()
        {
            EditorSceneManager.OpenScene("Assets/OODA/Scenes/TurnBattle.unity");
            var all = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var name in new[] { "Awake", "OnEnable", "Start" }) foreach (var b in all) Call(b, name);
            Frames(all, 5);
            var tb = Object.FindFirstObjectByType<TurnBattle>();
            string dir = Path.GetFullPath("Shots~");
            Directory.CreateDirectory(dir);
            var cam = Camera.main;
            var rt = new RenderTexture(1600, 1000, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;

            Report(tb, "start");
            Save(cam, rt, dir, "escort_start");

            // the B-17's propellers: do they turn when PropellerSpin runs?
            var spins = tb.bomber.GetComponents<PropellerSpin>();
            var before = spins.Select(s => s.propeller != null ? s.propeller.localRotation : Quaternion.identity).ToArray();
            foreach (var s in spins) { Call(s, "Start"); Call(s, "Update"); Call(s, "Update"); }
            float turned = spins.Select((s, i) => s.propeller != null ? Quaternion.Angle(before[i], s.propeller.localRotation) : -1).Min();
            Debug.Log($"OODA escort: {spins.Length} PropellerSpins on the B-17, propellers {string.Join(", ", spins.Select(s => s.propeller != null ? s.propeller.name : "MISSING"))}; least turn after two updates {turned:F1} degrees (dt {Time.deltaTime:F3} s).");

            // M: to the dogfight and back
            var mission = typeof(TurnBattle).GetField("mission", Any);
            Call(tb, "SwitchMission");
            Frames(all, 2);
            Debug.Log($"OODA escort: after M the mission is {mission.GetValue(tb)}, the B-17 {(tb.bomber.gameObject.activeInHierarchy ? "shown" : "hidden")}.");
            Save(cam, rt, dir, "escort_after_m");
            Call(tb, "SwitchMission");
            Frames(all, 2);
            Debug.Log($"OODA escort: after M again the mission is {mission.GetValue(tb)}, the B-17 {(tb.bomber.gameObject.activeInHierarchy ? "shown" : "hidden")}.");

            // a few turns, flying the arrow as it starts (straight ahead) and letting the AI fly the Spitfire
            for (int turn = 1; turn <= 4; turn++)
            {
                Call(tb, "Commit");
                var last = typeof(TurnBattle).GetField("last", Any).GetValue(tb) as TurnResult;
                float turnSec = tb.turnSec;
                for (int step = 0; step < 400 && Phase(tb) == "Playing"; step++)
                {
                    typeof(TurnBattle).GetField("playT", Any).SetValue(tb, Mathf.Min(turnSec, (step + 1) * turnSec / 20f) - 0.0001f);
                    Call(tb, "Playback");
                    Frames(all, 1);
                    if (turn == 3 && step == 10) Save(cam, rt, dir, "escort_mid_playback");
                }
                Frames(all, 3);
                Report(tb, $"after turn {turn} (frames {last?.FrameCount}, hits {last?.hits.Count})");
                if (turn == 2) Save(cam, rt, dir, "escort_turn2");
                if (Phase(tb) == "Over") break;
            }
            Save(cam, rt, dir, "escort_mid");
            cam.targetTexture = null;
            Debug.Log($"OODA escort: shots saved to {dir}");
        }

        static string Phase(TurnBattle tb) => typeof(TurnBattle).GetField("phase", Any).GetValue(tb).ToString();

        static void Report(TurnBattle tb, string when)
        {
            var battle = (Battle)typeof(TurnBattle).GetField("battle", Any).GetValue(tb);
            string P(int i)
            {
                var m = battle.planes[i];
                var p = UnityFrame.Pos(m.state.pos);
                return $"({p.x:F0}, {p.y:F0}, {p.z:F0}) {m.Kias:F0} kias hp {battle.health[i]:F0}{(battle.Out(i) ? " OUT" : "")}";
            }
            Debug.Log($"OODA escort {when}: phase {Phase(tb)}, {battle.Count} aircraft; P-51 {P(0)}; Spitfire {P(1)}{(battle.Count > 2 ? "; B-17 " + P(2) : "")}.");
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
            catch (TargetInvocationException e) { Debug.LogWarning($"OODA escort: {b.GetType().Name}.{method} threw {e.InnerException?.GetType().Name}: {e.InnerException?.Message}"); }
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
