using System;
using System.Collections.Generic;
using Ooda.Flight;
using Ooda.Flight.Core;
using Ooda.Turn.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ooda.Turn
{
    /// <summary>
    /// The gun funnel, drawn in the world while planning. At one moment of the turn it shows your rounds in the air (the gun
    /// line's rounds, the trigger held all turn) as a green ribbon a wingspan wide along the stream off your ghost's nose,
    /// a small green outline of your aircraft where it is then, and the target's bright ghost where it is then. Seen from
    /// behind, the ribbon narrows with range like a gunsight's funnel: when the target's wings fit across it, the rounds
    /// fired around then hit. The moment follows the mouse over the burst bar (a green mark shows it), or [ and ] while the
    /// timeline is shut, and starts each turn where your rounds first connect. K hides the funnel; the old lead ring on the
    /// target's path is a toggle now (L), off at first.
    /// </summary>
    public partial class TurnBattle
    {
        public enum SightMode { Off, Hover, Always }
        [Header("The gun funnel")]
        [Tooltip("The gun funnel while planning (K steps through): off; in the world, with the gunsight view while the mouse is over the burst bar; or the gunsight view all the time.")]
        public SightMode sightMode = SightMode.Hover;
        bool showSight => sightMode != SightMode.Off;
        [Tooltip("Show the lead ring on the target's path while planning (L). The funnel replaces it.")]
        public bool showLeadRing = false;
        [Tooltip("The funnel runs out to this range, ft.")]
        public float funnelMaxFt = 3000f;
        [Tooltip("Rounds nearer than this (ft) are left out: the funnel starts this far off the nose.")]
        public float funnelMinFt = 150f;
        public Color sightColor = new Color(0.45f, 1f, 0.45f, 0.95f);

        float sightT;              // the moment picked on the burst bar: when you fire, s into the turn (the funnel shows when those rounds arrive)
        float hoverUntil;          // the mouse was over the burst bar until about now (unscaled time)

        void NextSightMode() => sightMode = (SightMode)(((int)sightMode + 1) % 3);

        /// <summary>The gunsight view is up: the camera in your ghost's seat at the funnel's moment, looking down the guns.</summary>
        bool SightViewOn => sightMode != SightMode.Off && phase == Phase.Planning && gunPreview != null && battle != null && !battle.Out(0)
                            && (sightMode == SightMode.Always || Time.unscaledTime < hoverUntil);

        /// <summary>Every frame: put the camera in the ghost's seat while the gunsight view is up. From there the funnel looks like a HUD's.</summary>
        void SightCamera()
        {
            if (view == null) return;
            // the mouse over the burst bar, read here too (not only in OnGUI's events) so resting on it holds the view
            var mouse = Mouse.current;
            if (mouse != null && burstRect.width > 0 && phase == Phase.Planning)
            {
                Vector2 mp = mouse.position.ReadValue();
                var gui = new Vector2(mp.x / guiScale, (Screen.height - mp.y) / guiScale);
                if (burstRect.Contains(gui))
                {
                    hoverUntil = Time.unscaledTime + 0.6f;
                    sightT = Mathf.Clamp01((gui.x - burstRect.x) / burstRect.width) * (float)turnSec;
                    sightAuto = false;
                }
            }
            if (!SightViewOn) return;
            var me = gunPreview.frames[0][FunnelFrame()];
            if (me.down) return;
            GunAxes(me, out var gun, out var up, out _);
            // a chase camera straight behind your ghost on its gun line, a little above, rolled with its wings. The funnel
            // and the target's outline are drawn shifted by the camera's offset from the ghost's seat, so from here they
            // look exactly as they would from the seat: the HUD's funnel ahead of your ghost. The wheel sets how far back.
            Vector3 g = UnityFrame.Dir(gun), u = UnityFrame.Dir(up);
            float back = Mathf.Clamp(view.distance, 25f, 70f);
            view.sightPos = UnityFrame.Pos(me.pos) - g * back + u * (back * 0.3f);   // high enough that the ghost sits low, clear of the funnel
            view.sightRot = Quaternion.LookRotation(g, u);
            view.sightPose = true;
        }
        bool sightAuto = true;     // until the player moves it, it sits where the rounds first connect

        /// <summary>The funnel's keys: K the funnel, L the lead ring, [ and ] move its moment while the timeline is shut.</summary>
        void FunnelInput(Keyboard kb, float dt)
        {
            if (kb == null) return;
            if (kb[Key.K].wasPressedThisFrame) NextSightMode();
            if (kb[Key.L].wasPressedThisFrame) showLeadRing = !showLeadRing;
            if (showMoves) return;   // [ and ] are the timeline's slider then
            float scrub = 0;
            if (kb.leftBracketKey.isPressed) scrub -= 1;
            if (kb.rightBracketKey.isPressed) scrub += 1;
            if (scrub != 0) { sightT = Mathf.Clamp(sightT + scrub * dt * 1.0f, 0, (float)turnSec); sightAuto = false; }
        }

        /// <summary>A target's wingspan for the funnel, ft: a bomber's or a fighter's.</summary>
        double SpanFt(int j) => j >= 0 && j < battle.Count && battle.combat[j].sizeFt > 0 ? 104 : 36;

        /// <summary>The enemy the funnel is for: the one being read, else the one in focus; -1 none.</summary>
        int FunnelTarget()
        {
            int j = read != null && read.target >= 0 ? read.target : focus;
            return j >= 0 && j < battle.Count && battle.side[j] != battle.side[0] && !HiddenFromMe(j) && !JammedNow ? j : -1;
        }

        /// <summary>The frame of the gun preview at the funnel's moment.</summary>
        int FunnelFrame()
        {
            if (gunPreview == null) return 0;
            var gp = gunPreview;
            int f0 = Mathf.Clamp(Mathf.RoundToInt(sightT / (float)gp.dt), 0, gp.FrameCount - 1);
            int j = FunnelTarget();
            if (j < 0 || j >= gp.frames.Length) return f0;
            // the burst bar is in firing time (its red band is when rounds fired connect), so the funnel shows the moment
            // the round fired at sightT gets out to the target's range (or strikes it): then the two always agree
            Trace best = null; int bestD = int.MaxValue;
            foreach (var tr in gp.traces)
            {
                if (tr.owner != 0 || tr.missile || tr.startFrame == 0) continue;
                int d = Math.Abs(tr.startFrame - f0);
                if (d < bestD) { bestD = d; best = tr; }
            }
            if (best == null || bestD > 5) return f0;
            int last = best.startFrame;
            for (int f = best.startFrame; f < gp.FrameCount; f++)
            {
                var p = best.At(f);
                if (p == null) break;   // spent, or it hit something: it ends there
                last = f;
                Vec3d me = gp.frames[0][f].pos;
                if ((p.Value - me).Length >= (gp.frames[j][f].pos - me).Length) break;
            }
            return last;
        }

        /// <summary>The funnel's moment, s into the turn: when the rounds fired at the picked moment arrive.</summary>
        float ArriveT => FunnelFrame() * (float)(gunPreview?.dt ?? 0);

        /// <summary>Your gun line and wing line at a frame: the axis the rounds leave along, and across it.</summary>
        static void GunAxes(AircraftFrame me, out Vec3d gun, out Vec3d up, out Vec3d right)
        {
            Vec3d fwd = me.vel.Normalized;
            double a = me.alphaDeg * Math.PI / 180;
            gun = (fwd * Math.Cos(a) + me.up * Math.Sin(a)).Normalized;
            up = (me.up - gun * Vec3d.Dot(me.up, gun)).Normalized;
            right = Vec3d.Cross(gun, up);
        }

        /// <summary>Is a round of yours hitting the target at the funnel's moment, and when was it fired (s into the turn)?</summary>
        bool FunnelHit(int j, out double fired)
        {
            fired = -1;
            if (gunPreview == null || j < 0) return false;
            // by firing time, just as the burst bar's red band is drawn, so ON TARGET and the band always agree
            double at = sightT + gunPreview.startTime;
            foreach (var h in gunPreview.hits)
                if (h.shooter == 0 && h.target == j && Math.Abs(h.fireTime - at) <= 0.06) { fired = h.fireTime - gunPreview.startTime; return true; }
            return false;
        }

        /// <summary>The funnel in the world (with the other planning lines): the ribbon, your outline and the target's, at the moment.</summary>
        void DrawFunnel()
        {
            if (!showSight || phase != Phase.Planning || gunPreview == null || battle.Out(0)) return;
            var gp = gunPreview;
            int j = FunnelTarget();
            bool sv = SightViewOn;
            if (sightAuto)
            {
                // where your rounds first meet the target, else the middle of the turn
                double first = double.PositiveInfinity;
                foreach (var h in gp.hits)
                    if (h.shooter == 0 && (h.target == j || j < 0)) first = Math.Min(first, h.fireTime - gp.startTime);
                sightT = (float)(double.IsInfinity(first) ? turnSec / 2 : first);
            }
            if (!sv) return;   // only lined up: from the overview chase it is just a long green trail
            int f = FunnelFrame();
            var me = gp.frames[0][f];
            if (me.down) return;
            GunAxes(me, out var gun, out var up, out var right);
            // in the lined-up chase view, everything of the funnel moves with the camera off the seat (see SightCamera)
            Vector3 shift = sv && view != null ? view.sightPos - UnityFrame.Pos(me.pos) : Vector3.zero;

            // the stream: every round of yours in the air at this moment, nearest first
            var stream = new List<(double range, Vec3d at)>();
            foreach (var tr in gp.traces)
            {
                if (tr.owner != 0 || tr.missile) continue;
                var p = tr.At(f);
                if (p == null) continue;
                double r = Vec3d.Dot(p.Value - me.pos, gun);
                if (r < (sv ? Mathf.Max(funnelMinFt, 350f) : funnelMinFt) || r > funnelMaxFt) continue;   // lined up, it starts past your ghost
                stream.Add((r, p.Value));
            }
            stream.Sort((x, y) => x.range.CompareTo(y.range));

            // the ribbon: a wingspan wide across your wings, so a target whose wings fit across it is in the rounds' path
            double half = SpanFt(j) / 2;
            bool hitNow = FunnelHit(j, out _);
            var col = hitNow ? hitTracer : sightColor;
            if (stream.Count >= 2)
            {
                var left = new List<Vector3>(stream.Count); var rightEdge = new List<Vector3>(stream.Count);
                foreach (var s in stream)
                {
                    left.Add(UnityFrame.Pos(s.at - right * half) + shift);
                    rightEdge.Add(UnityFrame.Pos(s.at + right * half) + shift);
                }
                float wide = SightViewOn ? 2.4f : 1.4f;
                lines.Strip(left, wide, col, false, new Color(col.r, col.g, col.b, col.a * 0.6f));
                lines.Strip(rightEdge, wide, col, false, new Color(col.r, col.g, col.b, col.a * 0.6f));
                // rungs across it every 500 ft (from your seat only the 1,000 ft ones, so they don't read as one bar)
                if (!sv) lines.Line(left[0], rightEdge[0], 0.6f, col);
                for (double tick = 500; tick < funnelMaxFt; tick += 500)
                {
                    int k = stream.FindIndex(s => s.range >= tick);
                    if (k <= 0) continue;
                    if (sv && tick % 1000 != 0) continue;
                    var rc = new Color(col.r, col.g, col.b, col.a * (tick % 1000 == 0 ? 0.8f : 0.4f));
                    lines.Line(left[k], rightEdge[k], 0.5f, rc);
                }
                // a faint line from your nose to where the stream starts (not from your own seat)
                if (!sv) lines.Line(UnityFrame.Pos(me.pos + gun * 20), UnityFrame.Pos(stream[0].at), 0.4f, new Color(col.r, col.g, col.b, 0.4f));
            }

            // lined up, a horizon line level with the camera, so you know which way is up
            if (SightViewOn && view != null)
            {
                Vector3 eye = view.sightPos, ahead = view.sightRot * Vector3.forward;
                ahead.y = 0;
                if (ahead.sqrMagnitude < 1e-4f) ahead = Vector3.forward;
                ahead.Normalize();
                Vector3 across = Vector3.Cross(Vector3.up, ahead), mid = eye + ahead * 8000f;
                lines.Line(mid - across * 6000f, mid - across * 300f, 0.6f, new Color(sightColor.r, sightColor.g, sightColor.b, 0.6f));
                lines.Line(mid + across * 300f, mid + across * 6000f, 0.6f, new Color(sightColor.r, sightColor.g, sightColor.b, 0.6f));
            }

        }

        /// <summary>
        /// Lined up: the target's red ghost model where it is at the funnel's moment, moved by the same offset as the funnel
        /// so its wings sit against the funnel just as they would from the seat; bright red when the rounds hit it then.
        /// </summary>
        void ShowFunnelTarget()
        {
            HideSpread();
            if (!SightViewOn || view == null) return;
            int j = FunnelTarget();
            if (j < 1 || j >= holos.Length || holos[j] == null || battle.Out(j)) return;
            int f = FunnelFrame();
            var them = gunPreview.frames[j][f];
            if (them.down) return;
            var me = gunPreview.frames[0][f];
            Vector3 shift = view.sightPos - UnityFrame.Pos(me.pos);
            ShowHolo(holos[j], them, true, FunnelHit(j, out _) ? holoTargetHitColor : holoEnemyColor);
            holos[j].transform.position += shift;
            // its less likely moves, fainter by their chance, so you can line up on one of those instead
            if (!showProjection || read == null || read.target != j || read.spread == null) return;
            var pr = read.pred[j];
            for (int k = 1; k < read.spread.Length && k - 1 < spreadHolos.Length; k++)
            {
                var alt = SpreadAt(k, f);
                float rel = Mathf.Clamp01((float)(pr.chance[k] / Math.Max(1e-6, pr.chance[0])));
                var hc = RoundsAt(alt.pos, f, j) ? holoTargetHitColor : holoEnemyColor;
                hc.a *= 0.3f + 0.5f * rel;
                ShowHolo(spreadHolos[k - 1], alt, true, hc);
                spreadHolos[k - 1].transform.position += shift;
            }
        }

        /// <summary>The target flying its k-th likely move, at frame f of the turn.</summary>
        AircraftFrame SpreadAt(int k, int f) => read.spread[k][Mathf.Clamp(f, 0, read.spread[k].Length - 1)];

        /// <summary>Is one of your rounds (the trigger held all turn) within hitting distance of this point at frame f?</summary>
        bool RoundsAt(Vec3d p, int f, int j)
        {
            double reach = battle.combat[0].hitRadiusFt + battle.combat[j].sizeFt;
            foreach (var tr in gunPreview.traces)
            {
                if (tr.owner != 0 || tr.missile) continue;
                var q = tr.At(f);
                if (q != null && (q.Value - p).Length <= reach) return true;
            }
            return false;
        }

        /// <summary>On the screen: the moment from the burst bar, its mark, the label by the target, and the K and L toggles.</summary>
        void SightGUI(float sw, float sh)
        {
            if (gunPreview == null || battle.Out(0)) return;
            // the moment: the mouse over the burst bar moves it
            var ev = Event.current;
            if (burstRect.width > 0 && burstRect.Contains(ev.mousePosition) && (ev.type == EventType.Repaint || ev.type == EventType.MouseDrag || ev.type == EventType.MouseDown))
            {
                sightT = Mathf.Clamp01((ev.mousePosition.x - burstRect.x) / burstRect.width) * (float)turnSec;
                sightAuto = false;
                hoverUntil = Mathf.Max(hoverUntil, Time.unscaledTime + 0.6f);   // held a moment after leaving the bar
            }

            // the toggles go with the guns: right, over the weapons panel's rows (the specials' row, when there are specials)
            var load = battle.combat[0];
            bool specials = load.balloonMines + load.heatBlades + load.stealths + load.jammers > 0;
            float bw = 260f;
            Rect toggle = new Rect(ctl.weapons.xMax - bw, ctl.weapons.y - (specials ? 90 : 60), bw, 24);
            guiRects.Add(toggle);
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 13 };
            if (GUI.Button(new Rect(toggle.x, toggle.y, bw / 2 - 3, toggle.height), showLeadRing ? "Ring: on (L)" : "Ring: off (L)", btn)) showLeadRing = !showLeadRing;
            string sm = sightMode == SightMode.Off ? "Funnel: off (K)" : sightMode == SightMode.Hover ? "Funnel: hover (K)" : "Funnel: sight (K)";
            if (GUI.Button(new Rect(toggle.x + bw / 2 + 3, toggle.y, bw / 2 - 3, toggle.height), sm, btn)) NextSightMode();
            if (!showSight) return;

            // the moment on the burst bar
            if (burstRect.width > 0)
            {
                float mx = burstRect.x + sightT / (float)turnSec * burstRect.width;
                Fill(new Rect(mx - 1, burstRect.y - 4, 2, burstRect.height + 8), sightColor);
            }

            // in the gunsight view: the gun cross in the middle (the camera looks down the guns) and what you are seeing
            if (SightViewOn)
            {
                float cx = sw / 2, cy = sh / 2;
                Fill(new Rect(cx - 14, cy - 1, 10, 2), sightColor); Fill(new Rect(cx + 4, cy - 1, 10, 2), sightColor);
                Fill(new Rect(cx - 1, cy - 14, 2, 10), sightColor); Fill(new Rect(cx - 1, cy + 4, 2, 10), sightColor);
                var hud = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                hud.normal.textColor = sightColor;
                Shadow(new Rect(0, sh * 0.12f, sw, 24), $"FIRE {sightT:F2} s  >  THERE {ArriveT:F2} s   fit their wings across the funnel", hud);
            }

            // the label by the target's outline (or by yours when there is no target)
            int f = FunnelFrame();
            int j = FunnelTarget();
            var me = gunPreview.frames[0][f];
            var tag = new GUIStyle(small) { fontStyle = FontStyle.Bold };
            tag.normal.textColor = sightColor;
            bool hitNow = FunnelHit(j, out double fired);
            string text = $"fired {sightT:F2} s";
            Vec3d at = me.pos;
            if (j >= 0 && j < gunPreview.frames.Length && !battle.Out(j) && !gunPreview.frames[j][f].down)
            {
                var them = gunPreview.frames[j][f];
                double range = (them.pos - me.pos).Length;
                at = them.pos;
                if (hitNow) { tag.normal.textColor = hitTracer; text = $"ON TARGET {range:F0} ft  (fired {fired:F2} s)"; }
                else text = $"{range:F0} ft";
            }
            if (!SightViewOn) return;   // the funnel and its labels show only lined up
            Vector3 shift = view != null ? view.sightPos - UnityFrame.Pos(me.pos) : Vector3.zero;
            if (j >= 0 && read != null && read.target == j && read.pred != null && j < read.pred.Length && read.pred[j] != null) text += $"  {read.pred[j].chance[0] * 100:F0}%";
            var g = GuiPoint(UnityFrame.Pos(at) + shift);
            if (g != null) Shadow(new Rect(g.Value.x + 16, g.Value.y + 8, 360, 20), text, tag);
            // the less likely moves: their chance, and ON TARGET when your rounds are at them then
            if (j < 0 || !showProjection || read == null || read.target != j || read.spread == null) return;
            var pr = read.pred[j];
            for (int k = 1; k < read.spread.Length; k++)
            {
                var alt = SpreadAt(k, f);
                bool on = RoundsAt(alt.pos, f, j);
                var st = new GUIStyle(tag);
                st.normal.textColor = on ? hitTracer : new Color(1f, 0.55f, 0.45f, 0.9f);
                var ga = GuiPoint(UnityFrame.Pos(alt.pos) + shift);
                if (ga != null) Shadow(new Rect(ga.Value.x + 16, ga.Value.y + 8, 240, 20), on ? $"ON TARGET  {Prediction.Percent(pr.chance[k])}" : Prediction.Percent(pr.chance[k]), st);
            }
        }
    }
}
