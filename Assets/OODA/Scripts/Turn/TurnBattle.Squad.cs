using System;
using System.Collections;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// The squad controls' self-test (-squadshots, from the title straight into the Squadron): pictures of the lead's
    /// planning, Tab onto Blue 2, Blue 2 steered off, the Fly prompt, the next turn, a bomber and a formation change,
    /// saved as squad_*.bmp beside the game's data folder. Then it quits.
    /// </summary>
    public partial class TurnBattle
    {
        bool squadShotsStarted;

        void SquadShotsCheck()
        {
            if (squadShotsStarted || Array.IndexOf(Environment.GetCommandLineArgs(), "-squadshots") < 0) return;
            squadShotsStarted = true;
            StartCoroutine(SquadShots());
        }

        IEnumerator SquadShots()
        {
            string dir = System.IO.Path.GetDirectoryName(Application.dataPath);
            IEnumerator Shot(string name) { yield return new WaitForSeconds(1.2f); yield return Grab(System.IO.Path.Combine(dir, $"squad_{name}.bmp")); }
            IEnumerator Planning() { while (phase != Phase.Planning || reading != null) yield return null; }
            yield return Planning();
            yield return Shot("1_lead");
            NextPlane();
            yield return Shot("2_blue2");
            var m = battle.planes[0];
            plan.aim = env.Clamp((m.Forward + m.Right * 0.5).Normalized, true);
            plan.overpull = env.Overpull(plan.aim);
            dirty = true;
            yield return Shot("3_steered");
            NextPlane(-1);
            Commit();
            yield return Shot("4_prompt");
            Commit();
            yield return Planning();
            yield return Shot("5_turn2");
            Commit();   // Blue 2 is yours and has no plan this turn: Fly asks
            yield return Shot("5b_prompt");
            for (int k = 0; k < flight.Count && battle.routes[0] == null; k++) NextPlane();
            SetFormation(Formation.Box);
            yield return Shot("6_bomber_box");
            Application.Quit();
        }
    }
}
