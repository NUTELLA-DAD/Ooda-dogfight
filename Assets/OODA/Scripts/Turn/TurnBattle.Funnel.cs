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
        [Header("The gun funnel")]
        [Tooltip("Show the gun funnel in the world while planning (K).")]
        public bool showSight = true;
        [Tooltip("Show the lead ring on the target's path while planning (L). The funnel replaces it.")]
        public bool showLeadRing = false;
        [Tooltip("The funnel runs out to this range, ft.")]
        public float funnelMaxFt = 3000f;
        [Tooltip("Rounds nearer than this (ft) are left out: the funnel starts this far off the nose.")]
        public float funnelMinFt = 150f;
        public Color sightColor = new Color(0.45f, 1f, 0.45f, 0.95f);

        float sightT;              // the moment the funnel shows, s into the turn
        bool sightAuto = true;     // until the player moves it, it sits where the rounds first connect

        /// <summary>The funnel's keys: K the funnel, L the lead ring, [ and ] move its moment while the timeline is shut.</summary>
        void FunnelInput(Keyboard kb, float dt)
        {
            if (kb == null) return;
            if (kb[Key.K].wasPressedThisFrame) showSight = !showSight;
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
            return j >= 0 && j < battle.Count && battle.side[j] != battle.side[0] ? j : -1;
        }

        /// <summary>The frame of the gun preview at the funnel's moment.</summary>
        int FunnelFrame() => gunPreview == null ? 0 : Mathf.Clamp(Mathf.RoundToInt(sightT / (float)gunPreview.dt), 0, gunPreview.FrameCount - 1);

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
            double now = sightT + gunPreview.startTime;
            foreach (var h in gunPreview.hits)
                if (h.shooter == 0 && h.target == j && Math.Abs(h.time - now) < 0.08) { fired = h.fireTime - gunPreview.startTime; return true; }
            return false;
        }

        /// <summary>The funnel in the world (with the other planning lines): the ribbon, your outline and the target's, at the moment.</summary>
        void DrawFunnel()
        {
            if (!showSight || phase != Phase.Planning || gunPreview == null || battle.Out(0)) return;
            var gp = gunPreview;
            int j = FunnelTarget();
            if (sightAuto)
            {
                // where your rounds first meet the target, else the middle of the turn
                double first = double.PositiveInfinity;
                foreach (var h in gp.hits)
                    if (h.shooter == 0 && (h.target == j || j < 0)) first = Math.Min(first, h.time - gp.startTime);
                sightT = (float)(double.IsInfinity(first) ? turnSec / 2 : first);
            }
            int f = FunnelFrame();
            var me = gp.frames[0][f];
            if (me.down) return;
            GunAxes(me, out var gun, out var up, out var right);

            // the stream: every round of yours in the air at this moment, nearest first
            var stream = new List<(double range, Vec3d at)>();
            foreach (var tr in gp.traces)
            {
                if (tr.owner != 0 || tr.missile) continue;
                var p = tr.At(f);
                if (p == null) continue;
                double r = Vec3d.Dot(p.Value - me.pos, gun);
                if (r < funnelMinFt || r > funnelMaxFt) continue;
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
                    left.Add(UnityFrame.Pos(s.at - right * half));
                    rightEdge.Add(UnityFrame.Pos(s.at + right * half));
                }
                lines.Strip(left, 0.8f, col, false, new Color(col.r, col.g, col.b, col.a * 0.35f));
                lines.Strip(rightEdge, 0.8f, col, false, new Color(col.r, col.g, col.b, col.a * 0.35f));
                // rungs across it every 500 ft, the first at the near end
                lines.Line(left[0], rightEdge[0], 0.6f, col);
                for (double tick = 500; tick < funnelMaxFt; tick += 500)
                {
                    int k = stream.FindIndex(s => s.range >= tick);
                    if (k <= 0) continue;
                    var rc = new Color(col.r, col.g, col.b, col.a * (tick % 1000 == 0 ? 0.8f : 0.4f));
                    lines.Line(left[k], rightEdge[k], 0.5f, rc);
                }
                // a faint line from your nose to where the stream starts
                lines.Line(UnityFrame.Pos(me.pos + gun * 20), UnityFrame.Pos(stream[0].at), 0.4f, new Color(col.r, col.g, col.b, 0.4f));
            }

            // you and the target at the moment, as outlines
            Outline(me, 36, 16, sightColor, 0.7f);
            if (j >= 0 && j < gp.frames.Length && !battle.Out(j) && !gp.frames[j][f].down)
                Outline(gp.frames[j][f], SpanFt(j), battle.combat[j].sizeFt > 0 ? 37 : 16, hitNow ? hitTracer : new Color(1f, 0.45f, 0.35f, 0.95f), 0.9f);
        }

        /// <summary>A little line outline of an aircraft at a frame: fuselage, wings, tailplane and fin, at its true size.</summary>
        void Outline(AircraftFrame a, double span, double len, Color c, float width)
        {
            Vec3d tf = a.vel.Normalized, tu = a.up, tr = Vec3d.Cross(tf, tu);
            double half = span / 2;
            Vec3d nose = a.pos + tf * len, tail = a.pos - tf * len, wing = a.pos + tf * (len * 0.15);
            lines.Line(UnityFrame.Pos(nose), UnityFrame.Pos(tail), width, c);
            lines.Line(UnityFrame.Pos(wing - tr * half), UnityFrame.Pos(wing + tr * half), width, c);
            lines.Line(UnityFrame.Pos(tail - tr * (half * 0.35)), UnityFrame.Pos(tail + tr * (half * 0.35)), width, c);
            lines.Line(UnityFrame.Pos(tail), UnityFrame.Pos(tail + tu * (half * 0.3)), width, c);
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
            }

            // the toggles, left above the throttle
            float pw = Mathf.Min(640, sw - 40);
            float side = (sw - pw) / 2 - 20;
            float gh = Mathf.Min(240f, side / 0.9f, sh * 0.32f);
            float bottom = gh >= 90 ? sh - gh - 8 - 24 : sh - 8;
            float bw = Mathf.Clamp(side, 220f, 280f);
            Rect toggle = new Rect(10, bottom - 26, bw, 24);
            guiRects.Add(toggle);
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 13 };
            if (GUI.Button(new Rect(toggle.x, toggle.y, bw / 2 - 3, toggle.height), showLeadRing ? "Ring: on (L)" : "Ring: off (L)", btn)) showLeadRing = !showLeadRing;
            if (GUI.Button(new Rect(toggle.x + bw / 2 + 3, toggle.y, bw / 2 - 3, toggle.height), showSight ? "Funnel: on (K)" : "Funnel: off (K)", btn)) showSight = !showSight;
            if (!showSight) return;

            // the moment on the burst bar
            if (burstRect.width > 0)
            {
                float mx = burstRect.x + sightT / (float)turnSec * burstRect.width;
                Fill(new Rect(mx - 1, burstRect.y - 4, 2, burstRect.height + 8), sightColor);
            }

            // the label by the target's outline (or by yours when there is no target)
            int f = FunnelFrame();
            int j = FunnelTarget();
            var me = gunPreview.frames[0][f];
            var tag = new GUIStyle(small) { fontStyle = FontStyle.Bold };
            tag.normal.textColor = sightColor;
            bool hitNow = FunnelHit(j, out double fired);
            string text = $"FUNNEL {sightT:F2} s";
            Vec3d at = me.pos;
            if (j >= 0 && j < gunPreview.frames.Length && !battle.Out(j) && !gunPreview.frames[j][f].down)
            {
                var them = gunPreview.frames[j][f];
                double range = (them.pos - me.pos).Length;
                at = them.pos;
                if (hitNow) { tag.normal.textColor = hitTracer; text = $"ON TARGET {range:F0} ft  (fired {fired:F2} s)"; }
                else text = $"{sightT:F2} s  {range:F0} ft";
            }
            var g = GuiPoint(UnityFrame.Pos(at));
            if (g != null) Shadow(new Rect(g.Value.x + 16, g.Value.y + 8, 320, 20), text, tag);
        }
    }
}
