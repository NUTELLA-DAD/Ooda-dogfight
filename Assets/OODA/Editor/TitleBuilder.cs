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
        /// <summary>The turn battle's prefab for each dogfight type (Prefabs/name.prefab): a new type's prefab is named after it.</summary>
        static string CraftPrefab(Ooda.Turn.Core.Craft c) => c == Ooda.Turn.Core.Craft.Mustang ? "P51Turn" : c == Ooda.Turn.Core.Craft.Phantom ? "PhantomTurn" : c.ToString();

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
            // the squadron briefing parks the dogfight's planes by type: the turn battle's prefabs, any type without one left empty
            var types = (Ooda.Turn.Core.Craft[])System.Enum.GetValues(typeof(Ooda.Turn.Core.Craft));
            ts.craftPrefabs = types.Select(c => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/OODA/Prefabs/" + CraftPrefab(c) + ".prefab")).ToArray();
            foreach (var c in types) if (ts.craftPrefabs[(int)c] == null) Debug.LogWarning($"OODA: no prefab for the {c} on the briefing (run Build Turn Battle).");
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
            BootSceneBuilder.Build();   // the spinning planes load the title, so they go first
            Debug.Log("OODA: title screen built; the build opens on it: " + string.Join(", ", EditorBuildSettings.scenes.Select(s => Path.GetFileNameWithoutExtension(s.path))) + ".");
        }
    }
}
