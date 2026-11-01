using System.IO;
using System.Linq;
using Ooda.CameraSystem;
using Ooda.Flight;
using Ooda.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ooda.EditorTools
{
    /// <summary>
    /// OODA > Build Title: the opening screen, from the alpine test world: the free-flight P-51 and its camera rig go, and a
    /// TitleScreen flies the squadron's close formation up the escort route under a cinematic camera, with the menu over
    /// it. Saved as Title.unity, first in the build, so the game opens on it and starts the turn battle from it.
    /// </summary>
    public static class TitleBuilder
    {
        const string ScenePath = "Assets/OODA/Scenes/Title.unity";
        const string BattlePath = "Assets/OODA/Scenes/TurnBattle.unity";

        [MenuItem("OODA/Build Title")]
        public static void Build()
        {
            if (!File.Exists(BattlePath)) TurnBattleBuilder.Build();
            var scene = EditorSceneManager.OpenScene(TestWorldBuilder.ScenePath, OpenSceneMode.Single);

            var plane = Object.FindFirstObjectByType<AircraftController>();
            if (plane != null) Object.DestroyImmediate(plane.gameObject);
            var cam = Camera.main;
            var rig = cam.GetComponent<CameraRig>();
            if (rig != null) Object.DestroyImmediate(rig);

            var ts = new GameObject("TitleScreen").AddComponent<TitleScreen>();
            ts.fighterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/OODA/Prefabs/P51Turn.prefab");
            ts.bomberPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/OODA/Prefabs/B17.prefab");
            ts.route = (Vector3[])AlpineWorld.EscortRoute.Clone();
            ts.cam = cam;
            ts.terrain = Object.FindFirstObjectByType<Terrain>();
            ts.battleScene = Path.GetFileNameWithoutExtension(BattlePath);
            cam.transform.position = ts.route[0] + new Vector3(0, 150, -500);
            cam.transform.LookAt(ts.route[1]);

            // saved under its own name: the test world stays as it was
            EditorSceneManager.SaveScene(scene, ScenePath);
            var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log("OODA: title screen built; the build opens on it: " + string.Join(", ", EditorBuildSettings.scenes.Select(s => Path.GetFileNameWithoutExtension(s.path))) + ".");
        }
    }
}
