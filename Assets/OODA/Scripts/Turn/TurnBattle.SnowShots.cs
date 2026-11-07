using System;
using System.Collections;
using Ooda.Flight;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// -snowshots on the built game's command line: pictures of the snow cloud next to the game (snow_1 the planned release
    /// on the trigger bar with what the preview says you come out with, snow_2 the cloud billowing out as you fly out of it,
    /// snow_3 it hanging there a turn later with its edge ring, snow_4 your plane close up with its rime, snow_5 a plane
    /// with a heavy coat (the bandit, iced for the picture) with its icicles, snow_6 the IR view, the rimed planes cold),
    /// with the rime in the log, then it quits. Start it in a dogfight (it hands your plane two snow clouds if it has none).
    /// </summary>
    public partial class TurnBattle
    {
        bool snowShotsStarted;

        void SnowShotsCheck()
        {
            if (snowShotsStarted || Array.IndexOf(Environment.GetCommandLineArgs(), "-snowshots") < 0) return;
            snowShotsStarted = true;
            StartCoroutine(SnowShots());
        }

        IEnumerator SnowShots()
        {
            string dir = System.IO.Path.GetDirectoryName(Application.dataPath);
            IEnumerator Shot(string name, float wait = 1.2f) { yield return new WaitForSeconds(wait); yield return Grab(System.IO.Path.Combine(dir, $"snow_{name}.bmp")); }
            IEnumerator Planning() { while (phase != Phase.Planning || reading != null || thinking != null) yield return null; }
            yield return Planning();
            if (battle.snowClouds[0] <= 0) battle.snowClouds[0] = Specials.SnowClouds;

            // 1: planned for 0.5 s into the turn: the marker on the trigger bar, and the line over the switches
            plan.aim = battle.planes[0].Forward; plan.overpull = 0; plan.marks = null;
            plan.burstStart = plan.burstEnd = 0;
            plan.snow = true; plan.snowAt = 0.5;
            dirty = true;
            yield return Shot("1_planned", 2f);

            // 2: fly it; as it billows out, from off to one side and above, the plane flying out of its front
            float speed = playbackSpeed;
            playbackSpeed = 0.35f;
            Commit();
            float until = Time.unscaledTime + 30f;
            while (!(phase == Phase.Playing && playT >= 1.6f) && Time.unscaledTime < until) yield return null;
            playbackSpeed = 0f;   // hold the frame while the picture is taken
            var tr = last != null && last.snow.Count > 0 ? last.snow.Find(t => t.owner == 0) : null;
            int now = last != null ? Mathf.Min(last.FrameCount - 1, Mathf.RoundToInt(playT / (float)last.dt)) : 0;
            var head = tr?.At(now);
            if (head != null && view != null)
            {
                var p = UnityFrame.Pos(head.Value);
                var planeAt = UnityFrame.Pos(last.frames[0][now].pos);
                var along = (planeAt - p).normalized;
                var side = Vector3.Cross(Vector3.up, along).normalized;
                Look(p + side * 420f + Vector3.up * 120f - along * 60f, (p + planeAt) * 0.5f);
                yield return Shot("2_billowing", 0.4f);
                view.enabled = true;
            }
            else Debug.Log($"OODA: snow shots: no cloud at frame {now} ({(last == null ? "no turn" : $"{last.snow.Count} traces")})");
            playbackSpeed = speed;
            yield return Planning();
            Debug.Log($"OODA: snow: after the turn the player carries {battle.planes[0].systems.rime:F2} rime (IR-proof {battle.Iced(0)}), the bandit {battle.planes[1].systems.rime:F2}");

            // 3: a turn later it hangs there, its edge ring round it: from 700 m off
            var cloud = (SnowCloud)battle.bullets.Find(b => b is SnowCloud && b.owner == 0);
            if (cloud == null) { Debug.Log("OODA: snow shots: no cloud hanging after the turn"); Application.Quit(); yield break; }
            Debug.Log($"OODA: snow cloud hangs at ({cloud.pos.x:F0}, {cloud.pos.y:F0}, {cloud.pos.z:F0}) ft, {cloud.RadiusFt:F0} ft across, {cloud.vel.Length:F1} ft/s, age {cloud.age:F2} s");
            var cp = UnityFrame.Pos(cloud.pos);
            Look(cp + new Vector3(560, 160, -520), cp);
            yield return Shot("3_hanging", 1.5f);

            // 4: your plane close up, with what rime it flew out with
            if (views[0] != null)
            {
                var t = views[0].transform;
                Look(t.position + t.right * 9f + t.up * 3f - t.forward * 8f, t.position);
                yield return Shot("4_rimed_light", 1.2f);
            }

            // 5: a heavy coat (the bandit, as if it had flown through the whole cloud): frost and icicles
            if (views.Length > 1 && views[1] != null)
            {
                battle.planes[1].systems.rime = Math.Max(battle.planes[1].systems.rime, 0.9);
                RacksLive();
                var t = views[1].transform;
                Look(t.position + t.right * 9f - t.up * 1.5f - t.forward * 8f, t.position);
                yield return Shot("5_rimed_heavy", 1.5f);

                // 6: the IR view from behind the bandit: no heat to see
                irView = false; ToggleIr();
                Look(t.position - t.forward * 40f + t.up * 6f, t.position);
                yield return Shot("6_ir_cold", 1.5f);
                ToggleIr();
            }
            if (view != null) view.enabled = true;
            Debug.Log("OODA: snow shots done");
            Application.Quit();
        }
    }
}
