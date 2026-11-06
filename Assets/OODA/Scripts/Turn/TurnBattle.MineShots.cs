using System;
using System.Collections;
using Ooda.Flight;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// -mineshots on the built game's command line: pictures of the balloon mine next to the game (mine_1 the planned release
    /// on the trigger bar and the path, mine_2 just after it goes, mine_3 hanging in the air a turn later, mine_4 close up),
    /// then it quits. Start it in a dogfight (it hands your plane a mine if it has none).
    /// </summary>
    public partial class TurnBattle
    {
        bool mineShotsStarted;

        void MineShotsCheck()
        {
            if (mineShotsStarted || Array.IndexOf(Environment.GetCommandLineArgs(), "-mineshots") < 0) return;
            mineShotsStarted = true;
            StartCoroutine(MineShots());
        }

        IEnumerator MineShots()
        {
            string dir = System.IO.Path.GetDirectoryName(Application.dataPath);
            IEnumerator Shot(string name, float wait = 1.2f) { yield return new WaitForSeconds(wait); yield return Grab(System.IO.Path.Combine(dir, $"mine_{name}.bmp")); }
            IEnumerator Planning() { while (phase != Phase.Planning || reading != null || thinking != null) yield return null; }
            yield return Planning();
            if (battle.balloonMines[0] <= 0) battle.balloonMines[0] = 2;

            // 1: planned for 1.5 s into the turn: the marker on the trigger bar, the ring on your path where it goes
            plan.aim = battle.planes[0].Forward; plan.overpull = 0; plan.marks = null;
            plan.burstStart = plan.burstEnd = 0;
            plan.mine = true; plan.mineAt = 1.5;
            dirty = true;
            yield return Shot("1_planned", 2f);

            // 2: fly it, and look back as it goes
            float speed = playbackSpeed;
            playbackSpeed = 0.35f;
            Commit();
            float until = Time.unscaledTime + 30f;
            while (!(phase == Phase.Playing && playT >= 1.9f) && Time.unscaledTime < until) yield return null;
            if (view != null) { view.focus = TurnCamera.Focus.Player; view.yaw = 160; view.pitch = 8; view.distance = 45; }
            yield return Shot("2_released", 0.6f);
            playbackSpeed = speed;
            yield return Planning();

            // 3: a turn later it hangs there, drifting, its fuse ring round it
            if (view != null) { view.yaw = 175; view.pitch = 10; view.distance = 70; }
            yield return Shot("3_hanging", 2f);

            // 4: close up: the camera by the mine itself
            var mine = (Ooda.Turn.Core.Mine)battle.bullets.Find(b => b is Ooda.Turn.Core.Mine);
            if (mine != null && view != null)
            {
                view.enabled = false;
                var p = UnityFrame.Pos(mine.pos);
                view.transform.position = p + new Vector3(9, 2.5f, -9);
                view.transform.LookAt(p + Vector3.up * 1.4f);
                yield return Shot("4_close", 1.5f);
                view.enabled = true;
            }
            Debug.Log("OODA: mine shots done");
            Application.Quit();
        }
    }
}
