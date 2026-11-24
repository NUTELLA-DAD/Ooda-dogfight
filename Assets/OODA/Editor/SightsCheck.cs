using Ooda.Turn.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ooda.EditorTools
{
    /// <summary>
    /// OODA > Check Sights (or -executeMethod Ooda.EditorTools.SightsCheck.Run): makes the island's sights in the turn
    /// battle scene as the game does, then line-casts on the Obstacles layer where an aircraft would crash (through the
    /// bridge's deck, an arch pier, the carrier, a balloon's cable) and where it should fly clear (under the deck between the
    /// towers, through the arch's opening), and logs each as hit or clear against what it should be.
    /// </summary>
    public static class SightsCheck
    {
        [MenuItem("OODA/Check Sights")]
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/OODA/Scenes/TurnBattle.unity");
            Ooda.Fx.EastlandScape.Ensure();
            Physics.SyncTransforms();
            int mask = LayerMask.GetMask("Obstacles");
            if (mask == 0) { Debug.LogWarning("OODA check: sights: no Obstacles layer."); return; }

            double bx = Sights.BridgeX, bz = Sights.BridgeZ, ax = Sights.ArchX, az = Sights.ArchZ;
            var carrier = Sights.Ships[0];
            var balloon = Sights.Balloons[0];
            float cableY = (float)((balloon.groundM + balloon.y) / 2);
            int wrong = 0;
            void Cast(string what, Vector3 a, Vector3 b, bool shouldHit)
            {
                bool hit = Physics.Linecast(a, b, out var info, mask);
                string got = hit ? $"hit ({info.collider.name} at {info.point.x:F0}, {info.point.y:F0}, {info.point.z:F0})" : "clear";
                if (hit != shouldHit) wrong++;
                Debug.Log($"OODA check: sights: {what}: {got}, {(hit == shouldHit ? "as it should" : "WRONG: should be " + (shouldHit ? "hit" : "clear"))}.");
            }
            Cast("across the bridge deck", new Vector3((float)bx - 60, (float)Sights.BridgeDeckM, (float)bz), new Vector3((float)bx + 60, (float)Sights.BridgeDeckM, (float)bz), true);
            Cast("under the deck between the towers", new Vector3((float)bx - 200, 30, (float)bz), new Vector3((float)bx + 200, 30, (float)bz), false);
            Cast("through the arch's opening", new Vector3((float)ax, 50, (float)az - 300), new Vector3((float)ax, 50, (float)az + 300), false);
            Cast("through an arch pier", new Vector3((float)ax - 130, 50, (float)az - 300), new Vector3((float)ax - 130, 50, (float)az + 300), true);
            Cast("through the carrier", new Vector3((float)carrier.x, 8, (float)carrier.z - 200), new Vector3((float)carrier.x, 8, (float)carrier.z + 200), true);
            Cast("through a balloon's cable", new Vector3((float)balloon.x - 50, cableY, (float)balloon.z), new Vector3((float)balloon.x + 50, cableY, (float)balloon.z), true);
            Debug.Log($"OODA check: sights: {(wrong == 0 ? "all as they should be" : wrong + " wrong")}.");
        }
    }
}
