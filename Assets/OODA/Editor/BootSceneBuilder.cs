using System.IO;
using System.Linq;
using Ooda.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ooda.EditorTools
{
    /// <summary>OODA > Build Boot Screen: <c>Scenes/Boot.unity</c>, a camera and the <see cref="LoadingScreen"/> (the two
    /// fighters spinning round their circles) that loads the title, made first in the build. Build Title and Build
    /// Windows Game run it too, so the game always opens on it. The scene you have open stays open.</summary>
    public static class BootSceneBuilder
    {
        public const string ScenePath = "Assets/OODA/Scenes/Boot.unity";
        const string TitlePath = "Assets/OODA/Scenes/Title.unity";

        [MenuItem("OODA/Build Boot Screen")]
        public static void Build()
        {
            var open = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color32(5, 19, 64, 255);
            cam.cullingMask = 0;
            var ls = new GameObject("LoadingScreen").AddComponent<LoadingScreen>();
            ls.nextScene = Path.GetFileNameWithoutExtension(TitlePath);
            SceneManager.MoveGameObjectToScene(cam.gameObject, scene);
            SceneManager.MoveGameObjectToScene(ls.gameObject, scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            if (open.IsValid()) SceneManager.SetActiveScene(open);
            EditorSceneManager.CloseScene(scene, true);

            var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log("OODA: boot screen built; the build opens on it: " + string.Join(", ", EditorBuildSettings.scenes.Select(s => Path.GetFileNameWithoutExtension(s.path))) + ".");
        }
    }
}
