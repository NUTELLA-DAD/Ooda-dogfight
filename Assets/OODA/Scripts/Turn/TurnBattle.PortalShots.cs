using System;
using System.Collections;
using Ooda.Flight;
using Ooda.Flight.Core;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// -portalshots on the built game's command line (with -dogfight): pictures of the portals next to the game, then it
    /// quits. portal_1 planned (the green marker on the trigger bar, the ghost path through, the exit's arrow on the
    /// bandit's six), portal_2 the chase view on the entry with the bandit seen through it, portal_3 the plane about to go
    /// in, portal_4 just out of the exit, portal_5 and portal_6 the two rings from outside, with where the plane came out in
    /// the log. It hands your plane portals if its loadout has none.
    /// </summary>
    public partial class TurnBattle
    {
        bool portalShotsStarted;

        void PortalShotsCheck()
        {
            if (portalShotsStarted || Array.IndexOf(Environment.GetCommandLineArgs(), "-portalshots") < 0) return;
            portalShotsStarted = true;
            StartCoroutine(PortalShots());
        }

        IEnumerator PortalShots()
        {
            string dir = System.IO.Path.GetDirectoryName(Application.dataPath);
            IEnumerator Shot(string name, float wait = 1.2f) { yield return new WaitForSeconds(wait); yield return Grab(System.IO.Path.Combine(dir, $"portal_{name}.bmp")); Debug.Log($"OODA: portal shot {name}"); }
            IEnumerator Planning() { while (phase != Phase.Planning || reading != null || thinking != null) yield return null; }
            yield return Planning();
            if (battle.portals[0] <= 0) { battle.portals[0] = 2; battle.combat[0].portals = 2; }

            // 1: planned for 1 s in, the exit on the bandit's six (the default)
            plan.aim = battle.planes[0].Forward; plan.overpull = 0; plan.marks = null;
            plan.burstStart = plan.burstEnd = 0;
            plan.portalExitFace = Vec3d.Zero;
            plan.portal = true; plan.portalAt = 1.0; PortalOn();
            placingExit = false;
            dirty = true;
            if (view != null) { view.distance = 220f; view.pitch = 14f; view.BehindPlayer(); }
            yield return Shot("1_planned", 2.5f);

            // 2: closer behind, looking down the path at the entry: the bandit through it
            if (view != null) { view.distance = 60f; view.pitch = 4f; view.BehindPlayer(); }
            yield return Shot("2_window", 1.5f);

            // 3 and 4: fly it, slowed: just before it goes in, and just out of the exit
            float speed = playbackSpeed;
            playbackSpeed = 0.3f;
            Commit();
            float until = Time.unscaledTime + 40f;
            while (!(phase == Phase.Playing && playT >= 0.8f) && Time.unscaledTime < until) yield return null;
            playbackSpeed = 0f;
            yield return Shot("3_going_in", 0.5f);
            playbackSpeed = 0.3f;
            while (!(phase == Phase.Playing && playT >= 1.5f) && Time.unscaledTime < until) yield return null;
            playbackSpeed = 0f;
            yield return Shot("4_out", 0.5f);
            int warpAt = last != null ? Array.FindIndex(last.frames[0], f => f.warped) : -1;
            Debug.Log($"OODA: portal: went through at frame {warpAt} ({warpAt * Battle.StepSec:F2} s); now at {battle.planes[0].state.pos}");
            playbackSpeed = speed;
            yield return Planning();

            // 5 and 6: the two rings from outside
            Portal po = (Portal)battle.bullets.Find(b => b is Portal && b.owner == 0);
            if (po == null) { Debug.Log("OODA: portal shots: no portal open after the turn"); Application.Quit(); yield break; }
            Debug.Log($"OODA: portal open: entry {po.pos} facing {po.faceA}, exit {po.posB} facing {po.faceB}, age {po.age:F2} s");
            Vector3 b0 = UnityFrame.Pos(po.posB), fb = UnityFrame.Dir(po.faceB);
            Vector3 side = Vector3.Cross(Vector3.up, fb).normalized;
            Look(b0 + fb * 110f + side * 70f + Vector3.up * 25f, b0);
            yield return Shot("5_exit_ring", 1.5f);
            Vector3 a0 = UnityFrame.Pos(po.pos), fa = UnityFrame.Dir(po.faceA);
            Vector3 sideA = Vector3.Cross(Vector3.up, fa).normalized;
            Look(a0 - fa * 130f + sideA * 45f + Vector3.up * 18f, a0);
            yield return Shot("6_entry_ring", 1.5f);
            if (view != null) view.enabled = true;
            Debug.Log("OODA: portal shots done");
            Application.Quit();
        }
    }
}
