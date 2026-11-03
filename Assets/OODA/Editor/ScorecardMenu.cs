using System;
using System.IO;
using System.Linq;
using Ooda.Look;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ooda.EditorTools
{
    /// <summary>
    /// OODA > Scorecard > Run: presses Play on the title scene with a scorecard request, so the game takes the
    /// eight golden shots, grades them and opens the rating page (see <see cref="ScorecardRunner"/>). Each run
    /// gets its own folder under Scorecard~ next to Assets; history.csv there keeps a row per run.
    /// Set the Game view to 1920x1080 first so runs compare fairly.
    /// </summary>
    public static class ScorecardMenu
    {
        const string TitleScene = "Assets/OODA/Scenes/Title.unity";
        static string Root => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Scorecard~");

        [MenuItem("OODA/Scorecard/Run (golden shots and score)")]
        public static void Run()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("OODA scorecard: stop Play first."); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string dir = Path.Combine(Root, DateTime.Now.ToString("yyyyMMdd-HHmm"));
            PlayerPrefs.SetString(Scorecard.RequestKey, dir);
            PlayerPrefs.Save();
            if (File.Exists(TitleScene)) EditorSceneManager.OpenScene(TitleScene);
            else EditorSceneManager.OpenScene("Assets/OODA/Scenes/TurnBattle.unity");
            EditorApplication.isPlaying = true;
        }

        [MenuItem("OODA/Scorecard/Open latest rating page")]
        public static void OpenLatest()
        {
            if (!Directory.Exists(Root)) { Debug.LogWarning("OODA scorecard: no runs yet."); return; }
            var last = Directory.GetDirectories(Root).Where(d => File.Exists(Path.Combine(d, "rate.html"))).OrderBy(d => d, StringComparer.Ordinal).LastOrDefault();
            if (last == null) { Debug.LogWarning("OODA scorecard: no runs yet."); return; }
            Application.OpenURL("file:///" + Path.Combine(last, "rate.html").Replace('\\', '/').TrimStart('/'));
        }
    }
}
