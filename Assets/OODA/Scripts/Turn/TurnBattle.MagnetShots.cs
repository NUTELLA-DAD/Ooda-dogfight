using System;
using System.Collections;
using System.Linq;
using Ooda.Flight;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// -magnetshots on the built game's command line (with -dogfightbot 1 -dogfightstart Offensive): pictures of magnet rounds
    /// next to the game, then it quits. Your plane is handed magnet rounds; the bot flies until the planned burst comes near
    /// the target, then: magnet_1 the planning view with the violet preview tracers, magnet_2 the same plan with plain rounds
    /// (the magnet switched off) to compare, magnet_3 the burst flying in playback, from off to one side. The log says how many
    /// packets hit in each preview.
    /// </summary>
    public partial class TurnBattle
    {
        bool magnetShotsStarted;

        void MagnetShotsCheck()
        {
            if (magnetShotsStarted || Array.IndexOf(Environment.GetCommandLineArgs(), "-magnetshots") < 0) return;
            magnetShotsStarted = true;
            StartCoroutine(MagnetShots());
        }

        void SetMagnet(bool on)
        {
            var c = battle.combat[0].Copy();
            c.magnet = on;
            battle.combat[0] = c;
            dirty = true;
        }

        int PreviewHits() => preview == null ? 0 : preview.hits.Count(h => h.shooter == 0);
        int PreviewNear() => preview == null ? 0 : preview.traces.Count(t => t.owner == 0 && !t.missile && (t.hit || t.Near));

        IEnumerator MagnetShots()
        {
            string dir = System.IO.Path.GetDirectoryName(Application.dataPath);
            IEnumerator Shot(string name, float wait = 1.2f) { yield return new WaitForSeconds(wait); yield return Grab(System.IO.Path.Combine(dir, $"magnet_{name}.bmp")); }
            IEnumerator Planning() { while (phase != Phase.Planning || reading != null || thinking != null) yield return null; }
            yield return Planning();
            bot = false;   // the shots fly it from here
            if (botPilot == null) botPilot = new Core.EnemyAi { me = 0, role = Core.EnemyAi.Role.Fighter, rng = new Core.Lcg(4242), ignoreObstacles = true };
            SetMagnet(true);

            // fly on until the burst the bot plans comes near
            for (int t = 0; t < 10; t++)
            {
                yield return Planning();
                yield return new WaitForSeconds(0.3f);
                BotPlan();
                yield return new WaitForSeconds(0.5f);
                if (plan.burstEnd > plan.burstStart && PreviewNear() > 0) break;
                Commit();
            }

            // 1: the preview with magnet rounds; 2: the same plan with plain ones
            yield return Shot("1_magnet", 1.5f);
            int withHits = PreviewHits(), withNear = PreviewNear();
            SetMagnet(false);
            yield return new WaitForSeconds(0.5f);
            yield return Shot("2_plain", 1.0f);
            Debug.Log($"OODA: magnet shots: turn {battle.turn + 1}, burst {plan.burstStart:F2}-{plan.burstEnd:F2} s: magnet rounds {withHits} hits ({withNear} near or hit), plain {PreviewHits()} hits ({PreviewNear()} near or hit)");
            SetMagnet(true);
            yield return new WaitForSeconds(0.5f);

            // 3: fly it, and look at the burst from off to one side as it reaches the target
            float speed = playbackSpeed;
            playbackSpeed = 0.35f;
            Commit();
            float until = Time.unscaledTime + 30f;
            double hitAt = -1;
            while (phase != Phase.Playing && Time.unscaledTime < until) yield return null;
            if (last != null)
            {
                var first = last.hits.Where(h => h.shooter == 0).Select(h => h.time - last.startTime).DefaultIfEmpty(-1).Min();
                hitAt = first >= 0 ? first : (plan.burstStart + plan.burstEnd) / 2 + 0.3;
            }
            while (!(phase == Phase.Playing && playT >= hitAt) && Time.unscaledTime < until) yield return null;
            playbackSpeed = 0f;   // hold the frame while the pictures are taken
            if (last != null && view != null)
            {
                int now = Mathf.Min(last.FrameCount - 1, Mathf.RoundToInt(playT / (float)last.dt));
                int tgt = -1; double best = double.PositiveInfinity;
                for (int j = 1; j < last.frames.Length; j++)
                {
                    if (battle.side[j] == battle.side[0]) continue;
                    double d = (last.frames[j][now].pos - last.frames[0][now].pos).Length;
                    if (d < best) { best = d; tgt = j; }
                }
                var me = UnityFrame.Pos(last.frames[0][now].pos);
                var them = tgt >= 0 ? UnityFrame.Pos(last.frames[tgt][now].pos) : me + view.transform.forward * 200f;
                var mid = (me + them) * 0.5f;
                var along = (them - me).normalized;
                var side = Vector3.Cross(Vector3.up, along).normalized;
                float span = Mathf.Max(60f, (them - me).magnitude);
                Look(mid + side * span * 0.6f + Vector3.up * span * 0.15f, mid);
                yield return Shot("3_playback", 0.5f);
                Look(them - along * 25f + side * 12f + Vector3.up * 6f, them);
                yield return Shot("4_target", 0.5f);
                view.enabled = true;
                Debug.Log($"OODA: magnet shots: {last.hits.Count(h => h.shooter == 0)} hits in the turn flown");
            }
            playbackSpeed = speed;
            Debug.Log("OODA: magnet shots done");
            Application.Quit();
        }
    }
}
