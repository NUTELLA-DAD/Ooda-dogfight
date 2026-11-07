using System;
using System.Collections;
using Ooda.Flight;
using Ooda.Flight.Core;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// -grappleshots on the built game's command line: pictures of the grapple next to the game, then it quits. Start it in
    /// a 1 v 1 dogfight (it hands your plane hooks if it has none and sets the enemy where the shots need him):
    /// grapple_1 the planned hook (the gold marker and the label on the trigger bar, the ropes along the ghost path),
    /// grapple_2 mid-swing in the playback, from off to one side, grapple_3 the line out while planning the next turn,
    /// grapple_4 the planned hook on one passing the other way (the label says it snaps), grapple_5 the snap in the playback.
    /// What each line did goes to the log.
    /// </summary>
    public partial class TurnBattle
    {
        bool grappleShotsStarted;

        void GrappleShotsCheck()
        {
            if (grappleShotsStarted || Array.IndexOf(Environment.GetCommandLineArgs(), "-grappleshots") < 0) return;
            grappleShotsStarted = true;
            StartCoroutine(GrappleShots());
        }

        /// <summary>Put enemy j at (ahead, left) ft from the player (at its height), heading off the player's heading by turnDeg (left positive), at kias.</summary>
        void PlaceFoe(int j, double ahead, double left, double turnDeg, double kias)
        {
            var me = battle.planes[0];
            Vec3d f = me.Forward; f.z = 0; f = f.Normalized;
            Vec3d l = new Vec3d(-f.y, f.x, 0);
            Vec3d pos = me.state.pos + f * ahead + l * left;
            double heading = Math.Atan2(f.y, f.x) + turnDeg * Math.PI / 180;
            battle.planes[j].state = PointMass.Make(pos, Atmosphere.IasToTasKt(kias, pos.z), heading);
            battle.planes[j].throttle = 1;
        }

        IEnumerator GrappleShots()
        {
            string dir = System.IO.Path.GetDirectoryName(Application.dataPath);
            IEnumerator Shot(string name, float wait = 1.2f) { yield return new WaitForSeconds(wait); yield return Grab(System.IO.Path.Combine(dir, $"grapple_{name}.bmp")); }
            IEnumerator Planning() { while (phase != Phase.Planning || reading != null || thinking != null) yield return null; }
            IEnumerator PlayTo(float t)
            {
                float until = Time.unscaledTime + 40f;
                while (!(phase == Phase.Playing && playT >= t) && Time.unscaledTime < until) yield return null;
            }
            void Log(TurnResult r)
            {
                if (r == null) return;
                foreach (var tr in r.tethers)
                {
                    float peak = 0; foreach (var g in tr.tensionG) peak = Mathf.Max(peak, g);
                    Debug.Log($"OODA: grapple line {tr.owner}->{tr.target}: frames {tr.startFrame}-{tr.EndFrame}, peak {peak:F1} G, snap {tr.snapFrame} (jolts {tr.jolt0:F0} / {tr.jolt1:F0} ft/s), cut {tr.cut}");
                }
            }
            yield return Planning();
            if (battle.Count < 2) { Debug.Log("OODA: grapple shots need a dogfight"); Application.Quit(); yield break; }
            battle.grapples[0] = Math.Max(battle.grapples[0], 2);
            var load = battle.combat[0]; if (load.grapples <= 0) { battle.combat = (CombatNumbers[])battle.combat.Clone(); battle.combat[0] = load.Copy(); battle.combat[0].grapples = 2; }
            float speed = playbackSpeed;

            // 1: a slower enemy 800 ft ahead, breaking: hook him at once and pull hard after him
            PlaceFoe(1, 800, 150, 25, 250);
            focus = 1;
            var me = battle.planes[0];
            Vec3d fw = me.Forward, left = new Vec3d(-fw.y, fw.x, 0).Normalized;
            plan.aim = (fw + left * 1.2).Normalized; plan.overpull = 0; plan.marks = null; plan.hold = false;
            plan.burstStart = plan.burstEnd = 0; plan.throttle = 1;
            plan.grapple = true; plan.grappleAt = 0;
            plan.missileTarget = 1;
            dirty = true;
            yield return Shot("1_planned", 2.5f);

            // 2: fly it slowly; mid-swing, from off to one side, looking at the pair on the line
            playbackSpeed = 0.35f;
            Commit();
            yield return PlayTo(2.2f);
            playbackSpeed = 0f;
            if (last != null && view != null)
            {
                int now = Mathf.Min(last.FrameCount - 1, Mathf.RoundToInt(playT / (float)last.dt));
                Vector3 a = UnityFrame.Pos(last.frames[0][now].pos), b = UnityFrame.Pos(last.frames[1][now].pos);
                Vector3 mid = (a + b) * 0.5f, across = Vector3.Cross(Vector3.up, (b - a).normalized).normalized;
                float d = Mathf.Max(120f, Vector3.Distance(a, b) * 1.3f);
                Look(mid + across * d + Vector3.up * d * 0.35f, mid);
                yield return Shot("2_swing", 0.5f);
                view.enabled = true;
            }
            Log(last);
            playbackSpeed = speed;
            yield return Planning();

            // 3: the line still out while planning the next turn
            dirty = true;
            yield return Shot("3_line_out", 2f);

            // 4: cut it and set up one passing the other way, 1,200 ft ahead and a little to the right
            if (battle.LineOf(0) != null) { plan.grapple = true; plan.grappleAt = 0; Commit(); yield return Planning(); Log(last); }
            PlaceFoe(1, 1300, -250, 180, 300);
            plan.aim = battle.planes[0].Forward; plan.marks = null; plan.hold = false; plan.overpull = 0;
            plan.grapple = battle.grapples[0] > 0; plan.grappleAt = 0;
            dirty = true;
            yield return Shot("4_opposite_planned", 2.5f);

            // 5: the snap in the playback, from off to one side
            Commit();
            float wait = Time.unscaledTime + 40f;
            while (phase != Phase.Playing && Time.unscaledTime < wait) yield return null;
            var snap = last != null ? last.tethers.Find(t => t.owner == 0 && t.snapFrame >= 0) : null;
            float snapT = snap != null ? (snap.snapFrame + 3) * (float)last.dt : 2f;
            playbackSpeed = 0.35f;
            yield return PlayTo(snapT);
            playbackSpeed = 0f;
            if (last != null && view != null)
            {
                int now = Mathf.Min(last.FrameCount - 1, Mathf.RoundToInt(playT / (float)last.dt));
                Vector3 a = UnityFrame.Pos(last.frames[0][now].pos), b = UnityFrame.Pos(last.frames[1][now].pos);
                Vector3 mid = (a + b) * 0.5f, across = Vector3.Cross(Vector3.up, (b - a).normalized).normalized;
                float d = Mathf.Max(120f, Vector3.Distance(a, b) * 1.1f);
                Look(mid + across * d + Vector3.up * d * 0.3f, mid);
                yield return Shot("5_snap", 0.15f);
                view.enabled = true;
            }
            Log(last);
            playbackSpeed = speed;
            Debug.Log("OODA: grapple shots done");
            Application.Quit();
        }
    }
}
