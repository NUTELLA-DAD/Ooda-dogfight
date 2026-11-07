using System;
using System.Collections;
using System.Linq;
using Ooda.Flight;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// -ricochetshots on the built game's command line (with -dogfight): pictures of the ricochet shield next to the game
    /// (ricochet_1 planning with the shield raised and a bandit set 700 ft on your six, ricochet_2 the playback as his rounds
    /// bounce, ricochet_3 the same moment from the side, ricochet_4 a moment later), with what bounced and what came back in
    /// the log, then it quits. It hands your plane the shield if its loadout didn't.
    /// </summary>
    public partial class TurnBattle
    {
        bool ricochetShotsStarted;

        void RicochetShotsCheck()
        {
            if (ricochetShotsStarted || Array.IndexOf(Environment.GetCommandLineArgs(), "-ricochetshots") < 0) return;
            ricochetShotsStarted = true;
            StartCoroutine(RicochetShots());
        }

        IEnumerator RicochetShots()
        {
            string dir = System.IO.Path.GetDirectoryName(Application.dataPath);
            IEnumerator Shot(string name, float wait = 1.2f) { yield return new WaitForSeconds(wait); yield return Grab(System.IO.Path.Combine(dir, $"ricochet_{name}.bmp")); }
            IEnumerator Planning() { while (phase != Phase.Planning || reading != null || thinking != null) yield return null; }
            yield return Planning();

            // the shield on your plane; the nearest bandit 700 ft dead astern, flying your way and speed, with no shield of his own
            battle.combat[0] = battle.combat[0].Copy(); battle.combat[0].mirrors = Specials.Mirrors;
            battle.mirrors[0] = Specials.Mirrors;
            int foe = Enumerable.Range(0, battle.Count).FirstOrDefault(i => battle.side[i] != battle.side[0] && !battle.Out(i));
            var me = battle.planes[0];
            var st = me.state;
            st.pos = st.pos - me.Forward * 700;
            battle.planes[foe].state = st;
            battle.planes[foe].throttle = me.throttle;
            battle.mirrors[foe] = 0;
            for (int i = 0; i < battle.Count; i++)   // everyone else well out of the way
                if (i != 0 && i != foe && !battle.Out(i)) { var o = battle.planes[i].state; o.pos = o.pos + new Ooda.Flight.Core.Vec3d(0, 0, 4000 + 900 * i); battle.planes[i].state = o; }
            BeginPlanning();
            yield return Planning();

            // 1: planned: the shield up on your ghost, straight and level
            plan.aim = battle.planes[0].Forward; plan.overpull = 0; plan.marks = null;
            plan.burstStart = plan.burstEnd = 0;
            plan.mirror = true;
            dirty = true;
            yield return Shot("1_planning", 2f);

            // 2: fly it slowly; once his rounds start bouncing, hold the frame
            float speed = playbackSpeed;
            playbackSpeed = 0.3f;
            Commit();
            float until = Time.unscaledTime + 40f;
            bool Bounced() => last != null && last.ricochets.Any(h => h.time - last.startTime <= playT - 0.25f);
            while (!(phase == Phase.Playing && Bounced()) && Time.unscaledTime < until) yield return null;
            if (phase == Phase.Playing && last != null)
            {
                Debug.Log($"OODA: ricochet shots: {last.ricochets.Count} rounds bounced off the shield; {last.hits.Count(h => h.shooter == 0 && h.target == foe)} came back into the bandit; {last.hits.Count(h => h.target == 0)} hit you");
                playbackSpeed = 0f;
                yield return Shot("2_playback", 0.6f);
                int now = Mathf.Min(last.FrameCount - 1, Mathf.RoundToInt(playT / (float)last.dt));
                var a = UnityFrame.Pos(last.frames[0][now].pos);
                var b = UnityFrame.Pos(last.frames[foe][now].pos);
                var along = (a - b).normalized;
                var side = Vector3.Cross(Vector3.up, along).normalized;
                Look(a - along * 70f + side * 55f + Vector3.up * 12f, Vector3.Lerp(a, b, 0.35f));
                yield return Shot("3_side", 0.6f);
                Look(a - along * 12f + side * 16f + Vector3.up * 4f, a);
                yield return Shot("5_close", 0.6f);
                if (view != null) view.enabled = true;
                playbackSpeed = 0.3f;
                float t1 = playT + 0.5f;
                while (phase == Phase.Playing && playT < t1) yield return null;
                playbackSpeed = 0f;
                yield return Shot("4_later", 0.6f);
            }
            else Debug.Log($"OODA: ricochet shots: nothing bounced ({(last == null ? "no turn" : $"{last.ricochets.Count} ricochets, phase {phase}")})");
            playbackSpeed = speed;
            Debug.Log("OODA: ricochet shots done");
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }
}
