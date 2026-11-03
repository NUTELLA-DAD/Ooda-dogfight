using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ooda.Look;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// The golden shots for the scorecard (OODA > Scorecard > Run): the same seven situations every run, each
    /// taken with the HUD, without it, and (where the aircraft are judged) without the aircraft, with the frame
    /// held still in between. <see cref="ScorecardRunner"/> drives it and takes the title screen itself.
    /// </summary>
    public partial class TurnBattle
    {
        public float ScorecardMedianFps { get; private set; } = float.NaN;
        public float ScorecardLowFps { get; private set; } = float.NaN;

        public IEnumerator ScorecardShots(List<Scorecard.Shot> shots)
        {
            var keepMission = mission; int keepOurs = dogOurs, keepTheirs = dogTheirs; var keepStart = botStart;
            botStart = Scenarios.Start.Offensive;   // the same start every run: behind the Bf 109

            // 1. planning, 1 v 1, turn 1
            yield return ScStart(Mission.Dogfight, 1, 1);
            yield return ScTake(shots, "planning_1v1", "Planning view, Dogfight 1 v 1, turn 1", planes: true, footprint: true);

            // 6. cockpit, same moment
            if (view != null && view.cockpitEye != null)
            {
                view.focus = TurnCamera.Focus.Cockpit;
                yield return ScWait(1.5f);
                yield return ScTake(shots, "cockpit", "Cockpit view, looking forward", planes: false, footprint: false);
                view.focus = TurnCamera.Focus.Player;
                yield return ScWait(1f);
            }

            // 4. chase during playback with the guns going
            FireAtPredicted();
            if (plan.burstEnd <= plan.burstStart) { plan.burstStart = 0; plan.burstEnd = turnSec; dirty = true; }
            float burstMid = (float)(plan.burstStart + plan.burstEnd) / 2f;
            yield return ScFly();
            float until = Time.realtimeSinceStartup + 10f;
            while (phase == Phase.Playing && playT < burstMid && Time.realtimeSinceStartup < until) yield return null;
            yield return ScTake(shots, "chase_guns", "Chase view in playback, guns firing", planes: true, footprint: false);

            // 2. planning, 4 v 4, then a turn flown for the frame rate
            yield return ScStart(Mission.Dogfight, 4, 4);
            yield return ScTake(shots, "planning_4v4", "Planning view, Dogfight 4 v 4, every overlay", planes: false, footprint: true);
            var dts = new List<float>();
            yield return ScFly();
            until = Time.realtimeSinceStartup + 10f;
            while (phase == Phase.Playing && Time.realtimeSinceStartup < until) { dts.Add(Time.unscaledDeltaTime); yield return null; }
            if (dts.Count > 20)
            {
                dts.Sort();
                ScorecardMedianFps = 1f / Mathf.Max(1e-4f, dts[dts.Count / 2]);
                ScorecardLowFps = 1f / Mathf.Max(1e-4f, dts[Mathf.Min(dts.Count - 1, (int)(dts.Count * 0.99f))]);
            }

            // 3. town strike planning, past the map
            if (CanStrike)
            {
                mission = Mission.TownStrike; Restart();
                yield return ScWait(1f);
                if (briefing) FlyPlan();
                yield return ScPlanning();
                yield return ScWait(1.5f);
                yield return ScTake(shots, "planning_strike", "Planning view, Town Strike, radar and flak", planes: false, footprint: true);
            }

            // 5. low in the Valley Run, in playback
            if (CanValleyRun)
            {
                yield return ScStart(Mission.ValleyRun, 1, 1);
                yield return ScFly();
                until = Time.realtimeSinceStartup + 10f;
                while (phase == Phase.Playing && playT < turnSec * 0.5f && Time.realtimeSinceStartup < until) yield return null;
                yield return ScTake(shots, "chase_valley", "Chase view low in the Valley Run", planes: true, footprint: false);
            }

            // 7. the world: up the pass from above the basin, no HUD, no overlays
            yield return ScStart(Mission.Dogfight, 1, 1);
            yield return ScWorld(shots);

            botStart = keepStart; mission = keepMission; dogOurs = keepOurs; dogTheirs = keepTheirs;
        }

        IEnumerator ScStart(Mission m, int ours, int theirs)
        {
            mission = m; dogOurs = ours; dogTheirs = theirs;
            Restart();
            if (view != null) { view.focus = TurnCamera.Focus.Player; view.BehindPlayer(); }
            yield return ScPlanning();
            yield return ScWait(1.5f);   // the camera eases in and the preview settles
        }

        IEnumerator ScPlanning()
        {
            float until = Time.realtimeSinceStartup + 20f;
            while ((phase != Phase.Planning || reading != null || briefing) && Time.realtimeSinceStartup < until) yield return null;
        }

        static IEnumerator ScWait(float s) { yield return new WaitForSecondsRealtime(s); }

        IEnumerator ScFly()
        {
            Commit();
            yield return null;
            if (phase == Phase.Planning) Commit();   // a plane of yours with no plan: Fly asks first, the second press flies
            float until = Time.realtimeSinceStartup + 20f;
            while (phase != Phase.Playing && Time.realtimeSinceStartup < until) yield return null;
        }

        /// <summary>Holds the frame and grabs it with the HUD, without it, and without the aircraft.</summary>
        IEnumerator ScTake(List<Scorecard.Shot> shots, string id, string title, bool planes, bool footprint)
        {
            var s = new Scorecard.Shot { id = id, title = title, usePlanes = planes, useFootprint = footprint, useSaturation = true, useClipped = true };
            float ts = Time.timeScale;
            Time.timeScale = 0f;
            Scorecard.Frozen = true;
            yield return null;
            yield return ScGrab(t => s.ui = t);
            Scorecard.HideUi = true;
            yield return null;
            yield return ScGrab(t => s.noUi = t);
            if (planes)
            {
                var hidden = new List<Renderer>();
                foreach (var v in views)
                    if (v != null)
                        foreach (var r in v.GetComponentsInChildren<Renderer>())
                            if (r.enabled) { r.enabled = false; hidden.Add(r); }
                yield return null;
                yield return ScGrab(t => s.noPlanes = t);
                foreach (var r in hidden) if (r != null) r.enabled = true;
            }
            Scorecard.HideUi = false;
            Scorecard.Frozen = false;
            Time.timeScale = ts;
            shots.Add(s);
        }

        IEnumerator ScWorld(List<Scorecard.Shot> shots)
        {
            var s = new Scorecard.Shot { id = "world", title = "The pass from above the basin, no HUD", useSaturation = true, useClipped = true };
            var cam = view != null ? view.Cam : Camera.main;
            if (cam == null) yield break;
            Scorecard.Frozen = true;
            Scorecard.HideUi = true;
            float ts = Time.timeScale;
            Time.timeScale = 0f;
            bool camOn = view != null && view.enabled;
            if (view != null) view.enabled = false;
            var keepPos = cam.transform.position; var keepRot = cam.transform.rotation; float keepFov = cam.fieldOfView;
            // the overlays: everything the battle keeps under itself (the lines, the arrow tip) and the ghosts
            var off = new List<GameObject>();
            foreach (Transform c in transform) if (c.gameObject.activeSelf) { c.gameObject.SetActive(false); off.Add(c.gameObject); }
            foreach (var g in holos.Concat(new[] { holoMine, holoTheirs }))
                if (g != null && g.activeSelf) { g.SetActive(false); off.Add(g); }
            Vector3 from = new Vector3(0f, 1700f, -12000f), to = new Vector3(0f, 1100f, 6000f);
            if (HasRoute) { from = escortRoute[0] + new Vector3(0f, 450f, -5500f); to = escortRoute[escortRoute.Length - 1]; }
            cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(to - from));
            cam.fieldOfView = 60f;
            yield return null;
            yield return null;
            yield return ScGrab(t => s.noUi = t);
            s.ui = s.noUi;
            s.depth = Scorecard.SampleDepth(cam, s.noUi);
            cam.transform.SetPositionAndRotation(keepPos, keepRot);
            cam.fieldOfView = keepFov;
            foreach (var g in off) if (g != null) g.SetActive(true);
            if (view != null) view.enabled = camOn;
            Time.timeScale = ts;
            Scorecard.HideUi = false;
            Scorecard.Frozen = false;
            shots.Add(s);
        }

        static IEnumerator ScGrab(Action<Texture2D> done)
        {
            yield return new WaitForEndOfFrame();
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            tex.Apply();
            done(tex);
        }
    }
}
