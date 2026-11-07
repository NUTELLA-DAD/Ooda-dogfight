using System;
using System.Collections;
using Ooda.Flight;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// -blinkshots on the built game's command line: pictures of the blink next to the game (blink_1 the planned hop on the
    /// trigger bar and the broken ghost path, with a heat-seeker on your tail; blink_2 from the side just after the hop, the
    /// burst it left and the flash it came out of; blink_3 from where it left, looking up the path; blink_4 the end of the
    /// turn, the missile flying on lost), with the hop and the missile's fate in the log, then it quits. Start it in a
    /// dogfight (it hands your plane two blinks if it has none).
    /// </summary>
    public partial class TurnBattle
    {
        bool blinkShotsStarted;

        void BlinkShotsCheck()
        {
            if (blinkShotsStarted || Array.IndexOf(Environment.GetCommandLineArgs(), "-blinkshots") < 0) return;
            blinkShotsStarted = true;
            StartCoroutine(BlinkShots());
        }

        IEnumerator BlinkShots()
        {
            string dir = System.IO.Path.GetDirectoryName(Application.dataPath);
            IEnumerator Shot(string name, float wait = 1.2f) { yield return new WaitForSeconds(wait); yield return Grab(System.IO.Path.Combine(dir, $"blink_{name}.bmp")); }
            IEnumerator Planning() { while (phase != Phase.Planning || reading != null || thinking != null) yield return null; }
            yield return Planning();
            if (battle.blinks[0] <= 0) battle.blinks[0] = Specials.Blinks;
            if (battle.combat[0].blinks <= 0) { battle.combat[0] = battle.combat[0].Copy(); battle.combat[0].blinks = Specials.Blinks; }   // its switch shows

            // a heat-seeker on your tail, 2,500 ft back and closing fast, from the nearest enemy
            int foe = -1;
            for (int j = 0; j < battle.Count; j++) if (battle.side[j] != battle.side[0] && !battle.Out(j)) { foe = j; break; }
            var me = battle.planes[0];
            Missile ms = null;
            if (foe >= 0)
            {
                ms = Missile.Make(foe, battle.side[foe], 0, me.state.pos - me.Forward * 2500, me.state.vel + me.Forward * 500, battle.time);
                battle.bullets.Add(ms);
            }

            // 1: planned for 1.5 s into the turn: the cyan marker on the trigger bar, the ghost path broken over the hop
            plan.aim = me.Forward; plan.overpull = 0; plan.marks = null;
            plan.burstStart = plan.burstEnd = 0;
            plan.mine = plan.well = false;
            plan.holdFlares = true;   // so the blink, not the flares, is what beats it
            plan.blink = true; plan.blinkAt = 1.5;
            dirty = true;
            yield return Shot("1_planned", 2f);

            // 2 and 3: fly it; just after the hop, from the side, then from where it left
            float speed = playbackSpeed;
            playbackSpeed = 0.3f;
            Commit();
            float until = Time.unscaledTime + 30f;
            while (!(phase == Phase.Playing && playT >= 1.62f) && Time.unscaledTime < until) yield return null;
            playbackSpeed = 0f;   // hold the frame while the pictures are taken
            int b = last != null ? BlinkFrame(last.frames[0], last.FrameCount - 1) : -1;
            if (b > 0 && view != null)
            {
                var a = UnityFrame.Pos(last.frames[0][b - 1].pos);
                var z = UnityFrame.Pos(last.frames[0][b].pos);
                Debug.Log($"OODA: blink at frame {b} ({b * last.dt:F2} s): hopped {(last.frames[0][b].pos - last.frames[0][b - 1].pos).Length:F0} ft");
                var along = (z - a).normalized;
                var side = Vector3.Cross(Vector3.up, along).normalized;
                var mid = (a + z) * 0.5f;
                Look(mid + side * 330f + Vector3.up * 60f, mid);
                yield return Shot("2_side", 0.5f);
                Look(a - along * 40f + side * 12f + Vector3.up * 8f, z);
                yield return Shot("3_from_where_it_left", 0.5f);
                view.enabled = true;
            }
            else Debug.Log($"OODA: blink shots: no blink in the turn ({(last == null ? "no turn" : "no frame marked")})");
            playbackSpeed = speed;
            yield return Planning();

            // 4: the turn over: the missile flying on, lost
            if (ms != null) Debug.Log($"OODA: the heat-seeker {(ms.Tracking ? "is still tracking" : $"lost its target ({ms.lost})")}; it is {(ms.pos - battle.planes[0].state.pos).Length:F0} ft off; the plane is {(battle.SystemsOf(0).Hurt ? "hurt" : "unhurt")}");
            yield return Shot("4_after", 1.5f);
            Debug.Log("OODA: blink shots done");
            Application.Quit();
        }
    }
}
