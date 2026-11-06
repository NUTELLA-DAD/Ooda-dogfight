using System;
using System.Collections;
using Ooda.Flight;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// -mineshots on the built game's command line: pictures of the balloon mine next to the game (mine_1 the planned release
    /// on the trigger bar and the path, mine_2 just after it goes, mine_3 hanging in the air a turn later, mine_4 close up, mine_5
    /// a turn after that, with how far it drifted in the log),
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

        /// <summary>Take the camera off its plane and put it at eye, looking at at (for a picture).</summary>
        void Look(Vector3 eye, Vector3 at)
        {
            if (view == null) return;
            view.enabled = false;
            view.transform.position = eye;
            view.transform.LookAt(at);
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

            // 2: fly it; just after it goes, the camera off to one side of it, looking past it at the plane flying on
            float speed = playbackSpeed;
            playbackSpeed = 0.35f;
            Commit();
            float until = Time.unscaledTime + 30f;
            while (!(phase == Phase.Playing && playT >= 2.0f) && Time.unscaledTime < until) yield return null;
            playbackSpeed = 0f;   // hold the frame while the picture is taken
            var tr = last != null && last.mines.Count > 0 ? last.mines.Find(t => t.owner == 0) : null;
            int now = last != null ? Mathf.Min(last.FrameCount - 1, Mathf.RoundToInt(playT / (float)last.dt)) : 0;
            var head = tr?.At(now);
            if (head != null && view != null)
            {
                var p = UnityFrame.Pos(head.Value);
                var planeAt = UnityFrame.Pos(last.frames[0][now].pos);
                var away = (planeAt - p).normalized;
                var side = Vector3.Cross(Vector3.up, away).normalized;
                Look(p - away * 28f + side * 14f + Vector3.up * 5f, (p + planeAt) * 0.5f);
                yield return Shot("2_released", 0.4f);
                view.enabled = true;
            }
            else Debug.Log($"OODA: mine shots: no mine at frame {now} ({(last == null ? "no turn" : $"{last.mines.Count} traces")})");
            playbackSpeed = speed;
            yield return Planning();

            // 3: a turn later it hangs there, drifting, its fuse ring round it: from 60 m off
            var mine = (Ooda.Turn.Core.Mine)battle.bullets.Find(b => b is Ooda.Turn.Core.Mine && b.owner == 0);
            if (mine == null) { Debug.Log("OODA: mine shots: no mine hanging after the turn"); Application.Quit(); yield break; }
            var p0 = mine.pos; double t0 = battle.time;
            Debug.Log($"OODA: mine hangs at ({p0.x:F0}, {p0.y:F0}, {p0.z:F0}) ft, {mine.vel.Length:F1} ft/s, age {mine.age:F2} s");
            var mp = UnityFrame.Pos(mine.pos);
            Look(mp + new Vector3(45, 12, -40), mp + Vector3.up * 1.4f);
            yield return Shot("3_hanging", 1.5f);

            // 4: close up: the camera by the mine itself
            Look(mp + new Vector3(9, 2.5f, -9), mp + Vector3.up * 1.4f);
            yield return Shot("4_close", 1.5f);
            if (view != null) view.enabled = true;

            // 5: one more turn: how far and how fast it drifted
            Commit();
            yield return Planning();
            if (mine.alive)
            {
                var d = mine.pos - p0; double dt = battle.time - t0;
                Debug.Log($"OODA: mine drifted {d.Length:F0} ft in {dt:F1} s ({d.Length / dt:F1} ft/s): {d.x:F0} east, {d.y:F0} north, {d.z:F0} up; now {mine.vel.Length:F1} ft/s");
                var np = UnityFrame.Pos(mine.pos);
                Look(np + new Vector3(45, 12, -40), np + Vector3.up * 1.4f);
                yield return Shot("5_drifted", 1.5f);
                if (view != null) view.enabled = true;
            }
            else Debug.Log("OODA: mine went off in the second turn");
            Debug.Log("OODA: mine shots done");
            Application.Quit();
        }
    }
}
