using System;
using Ooda.Flight.Core;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// The skyscraper city (Turn/Core/City.cs, the Skyline City arena): its towers are drawn once at the start
    /// (Fx/CityScape), and they are part of the ground everything here asks for, so the turn engine crashes you into one
    /// as into a mountain, the ghost path turns red and says BUILDING, rounds and radar stop at them, and the AI pilots
    /// plan round them. Over the city the AI pilots fly braver about the roofs and look ahead in short steps, so a straight
    /// line ahead never skips over a tower.
    /// </summary>
    public partial class TurnBattle
    {
        /// <summary>How close over the roofs (ft) an AI pilot in the city lets itself fly before it worries (600 elsewhere).</summary>
        public double citySafeClearFt = 400;
        /// <summary>The step (ft) along its look ahead in the city: less than the narrowest tower.</summary>
        public double cityLookStepFt = 90;

        void CityStart() => Ooda.Fx.CityScape.Ensure();

        /// <summary>The ground the battle flies over (ft), from any thread: the island's (its height grid, or the terrain
        /// itself if there is none), or the city's roofs and streets where it stands.</summary>
        double WorldGroundFt(double xFt, double yFt)
        {
            double c = City.Skyline.GroundFt(xFt, yFt);
            if (!double.IsNegativeInfinity(c)) return c;   // the city stands out at sea, clear of the island
            return grid != null ? grid.At(xFt, yFt) : GroundFt(xFt, yFt);
        }

        /// <summary>Whether aircraft i of a battle ended up in a tower (or a bridge) rather than in the street, the river or the hills.</summary>
        static bool CrashedIntoTower(Battle b, int i)
        {
            var p = b.planes[i].state.pos;
            return City.Skyline.Over(p.x * Atmosphere.MPerFt, p.y * Atmosphere.MPerFt) && City.Skyline.OverBuildingFt(p.x, p.y);
        }

        static readonly EnemyAi usualPilot = new EnemyAi();

        /// <summary>A fight that begins over the city: every AI pilot (and the bot's) gets the city's settings; anywhere else
        /// the usual ones (the bot's pilot lives on from fight to fight).</summary>
        void CityPilots()
        {
            if (battle == null || battle.Count == 0) return;
            var p = battle.planes[0].state.pos;
            bool inCity = City.Skyline.Over(p.x * Atmosphere.MPerFt, p.y * Atmosphere.MPerFt);
            foreach (var ai in ais) Tune(ai);
            Tune(botPilot);
            void Tune(EnemyAi ai)
            {
                if (ai == null) return;
                ai.safeClearFt = inCity ? citySafeClearFt : usualPilot.safeClearFt;
                ai.lookStepFt = inCity ? cityLookStepFt : usualPilot.lookStepFt;
            }
        }
    }
}
