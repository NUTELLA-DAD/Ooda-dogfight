using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Ooda.Turn;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ooda.Look
{
    /// <summary>
    /// Runs the scorecard when asked: by the editor menu (OODA > Scorecard > Run, which leaves the folder in
    /// PlayerPrefs and presses Play on the title scene) or in a build with <c>-scorecard &lt;folder&gt;</c>.
    /// It takes the title screen, loads the battle, has it take the other golden shots, grades them, writes the
    /// report and opens the rating page, then stops.
    /// </summary>
    public class ScorecardRunner : MonoBehaviour
    {
        public string dir;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            string dir = null;
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-scorecard");
            if (at >= 0) dir = at + 1 < args.Length ? args[at + 1] : Path.Combine(Path.GetDirectoryName(Application.dataPath), "Scorecard~", DateTime.Now.ToString("yyyyMMdd-HHmm"));
            if (dir == null && PlayerPrefs.HasKey(Scorecard.RequestKey))
            {
                dir = PlayerPrefs.GetString(Scorecard.RequestKey);
                PlayerPrefs.DeleteKey(Scorecard.RequestKey);
                PlayerPrefs.Save();
            }
            if (string.IsNullOrEmpty(dir)) return;
            var go = new GameObject("Scorecard");
            DontDestroyOnLoad(go);
            go.AddComponent<ScorecardRunner>().dir = dir;
        }

        IEnumerator Start()
        {
            Debug.Log($"OODA scorecard: taking the golden shots into {dir}. Leave the mouse and keys alone until it stops.");
            var shots = new List<Scorecard.Shot>();
            yield return new WaitForSecondsRealtime(2f);

            // 8. the title screen, if the run started there
            if (FindFirstObjectByType<Ooda.UI.TitleScreen>() != null)
            {
                yield return new WaitForEndOfFrame();
                var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
                tex.Apply();
                shots.Add(new Scorecard.Shot { id = "title", title = "Title screen", ui = tex });
                Ooda.UI.TitleScreen.ChosenMission = (int)TurnBattle.Mission.Dogfight;
                Ooda.UI.TitleScreen.ChosenOurs = 1;
                Ooda.UI.TitleScreen.ChosenTheirs = 1;
                SceneManager.LoadScene("TurnBattle");
            }

            TurnBattle battle = null;
            float until = Time.realtimeSinceStartup + 30f;
            while (battle == null && Time.realtimeSinceStartup < until) { battle = FindFirstObjectByType<TurnBattle>(); yield return null; }
            if (battle == null) { Debug.LogError("OODA scorecard: no TurnBattle scene came up."); Stop(); yield break; }
            yield return new WaitForSecondsRealtime(2f);
            yield return battle.ScorecardShots(shots);

            var results = Scorecard.Grade(shots, battle.ScorecardMedianFps, battle.ScorecardLowFps, Scorecard.CodeChecks());
            if (Screen.width != 1920 || Screen.height != 1080)
                Debug.LogWarning($"OODA scorecard: the shots are {Screen.width}x{Screen.height}. Set the Game view to 1920x1080 so runs compare fairly.");
            Scorecard.Write(dir, shots, results);
            Application.OpenURL("file:///" + Path.Combine(dir, "rate.html").Replace('\\', '/').TrimStart('/'));
            foreach (var s in shots) { if (s.ui != null) Destroy(s.ui); if (s.noUi != null && s.noUi != s.ui) Destroy(s.noUi); if (s.noPlanes != null) Destroy(s.noPlanes); }
            Stop();
        }

        void Stop()
        {
            Destroy(gameObject);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
