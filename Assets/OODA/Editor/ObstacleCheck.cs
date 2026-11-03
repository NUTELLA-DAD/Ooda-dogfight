using System.Linq;
using System.Reflection;
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
    /// OODA > Check Obstacles: in the turn battle scene, flies the P-51 straight at the main valley's viaduct, once just
    /// under its deck (it should crash) and once well over it (it should not), with the battle's own obstacle check, and
    /// logs what happened. Batch mode has no play loop, so this stands in for flying a turn into a bridge by hand.
    /// </summary>
    public static class ObstacleCheck
    {
        [MenuItem("OODA/Check Obstacles")]
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/OODA/Scenes/TurnBattle.unity");
            Physics.SyncTransforms();
            int layer = LayerMask.NameToLayer(AlpineWorld.ObstacleLayer);
            var solid = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Where(c => c.gameObject.layer == layer).ToList();
            Debug.Log($"OODA check: {AlpineWorld.ObstacleLayer} is layer {layer}; {solid.Count} colliders on it: {string.Join(", ", solid.Select(c => c.name.Split('(')[0].Trim()).Distinct())}.");

            var tb = Object.FindFirstObjectByType<TurnBattle>();
            foreach (var name in new[] { "Awake", "Start" })
                foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                    mb.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(mb, null);
            var battle = (Battle)typeof(TurnBattle).GetField("battle", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(tb);

            // the long stone viaduct across the main valley above the village (it crosses at about z = 300)
            var mark = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                .Where(t => t.name.StartsWith("Bridge ") && t.name.EndsWith(" centre"))
                .OrderBy(t => Vector2.Distance(new Vector2(t.position.x, t.position.z), new Vector2(0, 300))).First();
            Vector3 deck = mark.position;   // the rail top, over the middle of the bridge
            Debug.Log($"OODA check: target {mark.parent.name}, rail top at ({deck.x:F0}, {deck.y:F0}, {deck.z:F0}).");

            Fly(battle, deck, -1f, "1 m under the rail top, into the deck");
            Fly(battle, deck, -10f, "10 m under the rail top, at the arch crowns");
            Fly(battle, deck, 100f, "100 m over the rail top");
            // and a gun round: straight through the deck
            Vec3d a = UnityFrame.Pos(deck + new Vector3(0, -2, -300)), b = UnityFrame.Pos(deck + new Vector3(0, -2, 300));
            Debug.Log($"OODA check: a line through the deck {(battle.obstacleHit(a, b) ? "hits" : "MISSES")}; one 100 m over it {(battle.obstacleHit(a + new Vec3d(0, 0, 330), b + new Vec3d(0, 0, 330)) ? "HITS" : "misses")}.");
        }

        static void Fly(Battle original, Vector3 deck, float over, string what)
        {
            // a fresh pair, so nothing of the scene's own fight (its G, roll or throttle) carries over: the P-51 600 m south of
            // the bridge heading north at 300 knots, level; the Bf 109 out of the way, high to the east
            Vec3d start = UnityFrame.Pos(new Vector3(deck.x, deck.y + over, deck.z - 600));
            Vec3d away = UnityFrame.Pos(new Vector3(deck.x + 6000, deck.y + 2000, deck.z));
            var battle = new Battle(new FlightModel(AircraftNumbers.P51D(), PointMass.Make(start, 300, System.Math.PI / 2)), CombatNumbers.P51D(),
                                    new FlightModel(AircraftNumbers.Bf109G(), PointMass.Make(away, 300, 0)), CombatNumbers.Bf109G());
            battle.obstacleHit = original.obstacleHit;
            battle.groundFt = original.groundFt;
            var r = battle.Resolve(TurnPlan.Holding(), TurnPlan.Holding(), 8, true);
            int crashFrame = System.Array.FindIndex(r.frames[0], f => f.down);
            var end = UnityFrame.Pos(battle.planes[0].state.pos);
            Vector3 at = crashFrame >= 0 ? UnityFrame.Pos(r.frames[0][crashFrame].pos) : end;
            Debug.Log($"OODA check: flying {what}: {(battle.crashed[0] ? $"CRASHED after {crashFrame * r.dt:F2} s at ({at.x:F0}, {at.y:F0}, {at.z:F0}), {at.z - deck.z:F0} m from the bridge's line, {at.y - deck.y:F0} m from the rail top" : "flew on")}, ended at ({end.x:F0}, {end.y:F0}, {end.z:F0}).");
        }
    }
}
