using System.IO;
using System.Reflection;
using Ooda.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ooda.EditorTools
{
    /// <summary>
    /// OODA > Title Shots: opens the title scene, starts the TitleScreen by hand (batch mode has no play loop) and renders
    /// each of its camera shots, mid-shot, with the formation a few kilometres up the valley, to PNGs in Shots~. The menu is
    /// IMGUI, which a camera render doesn't include.
    /// </summary>
    public static class TitleShots
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [MenuItem("OODA/Title Shots")]
        public static void Capture()
        {
            EditorSceneManager.OpenScene("Assets/OODA/Scenes/Title.unity");
            var ts = Object.FindFirstObjectByType<TitleScreen>();
            typeof(TitleScreen).GetMethod("Start", Any).Invoke(ts, null);
            foreach (var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (b != ts) b.GetType().GetMethod("Start", Any, null, System.Type.EmptyTypes, null)?.Invoke(b, null);
            string dir = Path.GetFullPath("Shots~");
            Directory.CreateDirectory(dir);
            var rt = new RenderTexture(1600, 1000, 24) { antiAliasing = 4 };
            ts.cam.targetTexture = rt;
            string[] names = { "orbit", "flyby", "low_behind", "wingman", "high_wide" };
            float[] at = { 2500, 3500, 4500, 5500, 6500 };
            for (int n = 0; n < TitleScreen.ShotCount; n++)
            {
                typeof(TitleScreen).GetField("distance", Any).SetValue(ts, at[n]);
                ts.StartShot(n);
                typeof(TitleScreen).GetField("clock", Any).SetValue(ts, ts.shotSec * 0.5f);
                typeof(TitleScreen).GetMethod("Update", Any).Invoke(ts, null);
                ts.cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(dir, $"title_{n}_{names[n]}.png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Debug.Log($"OODA title: shot {n} ({names[n]}) from ({ts.cam.transform.position.x:F0}, {ts.cam.transform.position.y:F0}, {ts.cam.transform.position.z:F0}).");
            }
            ts.cam.targetTexture = null;
        }
    }
}
