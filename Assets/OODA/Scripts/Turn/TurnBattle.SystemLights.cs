using UnityEngine;

namespace Ooda.Turn
{
    public partial class TurnBattle
    {
        /// <summary>The cockpit's system lamps (TurnBattle.Cockpit) read the player's systems (TurnBattle.cs).</summary>
        partial void SystemLight(int k, ref Color lamp, ref string text) => SystemLightState(k, ref lamp, ref text);
    }
}
