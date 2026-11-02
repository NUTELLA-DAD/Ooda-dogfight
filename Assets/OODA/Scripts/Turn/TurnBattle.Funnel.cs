using System;
using System.Collections.Generic;
using Ooda.Flight.Core;
using Ooda.Turn.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ooda.Turn
{
    /// <summary>
    /// The gun funnel: a gunsight glass while planning that shows, from your ghost's seat at one moment of the turn, where
    /// your rounds are in the air (the gun line's rounds, the trigger held all turn) and the target's bright ghost. The
    /// stream is drawn as two lines a wingspan apart at each range, so it bends with your turn and narrows with range:
    /// when the target's wings fill the funnel it is in range and on the line. The moment follows the mouse over the burst
    /// bar (or [ and ] while the timeline is shut), and starts each turn where your rounds first connect. The old lead
    /// ring on the target's path is a toggle now (L), off at first.
    /// </summary>
    public partial class TurnBattle
    {
        [Header("The gun funnel")]
        [Tooltip("Show the gunsight glass with the gun funnel while planning (K).")]
        public bool showSight = true;
        [Tooltip("Show the lead ring on the target's path while planning (L). The funnel replaces it.")]
        public bool showLeadRing = false;
        [Tooltip("The glass's half field of view, mils (a mil is about a foot at 1,000 ft).")]
        public float sightFieldMil = 200f;
        [Tooltip("The funnel runs out to this range, ft.")]
        public float funnelMaxFt = 3000f;
        public Color sightColor = new Color(0.45f, 1f, 0.45f, 0.95f);
        public Color sightGlass = new Color(0.05f, 0.09f, 0.04f, 0.82f);

        float sightT;              // the moment the glass shows, s into the turn
        bool sightAuto = true;     // until the player moves it, the glass sits where the rounds first connect

        /// <summary>The funnel's keys: K the glass, L the lead ring, [ and ] move the glass's moment while the timeline is shut.</summary>
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

        /// <summary>The glass, left above the throttle, and the lead ring's toggle under it.</summary>
        void SightGUI(float sw, float sh)
        {
            if (gunPreview == null || battle.Out(0)) return;
            float pw = Mathf.Min(640, sw - 40);
            float side = (sw - pw) / 2 - 20;
            float gh = Mathf.Min(240f, side / 0.9f, sh * 0.32f);
            float bottom = gh >= 90 ? sh - gh - 8 - 24 : sh - 8;
            float size = Mathf.Min(270f, side, bottom - (showHelp ? helpTop + 320 : helpTop) - 56);
            if (size < 130) return;   // no room beside the panel
            float y0 = bottom - size - 30;
            Rect head = new Rect(10, y0 - 22, size, 20);
            Rect glass = new Rect(10, y0, size, size);
            Rect toggle = new Rect(10, y0 + size + 4, size, 24);

            // the moment: the mouse over the burst bar moves it
            var ev = Event.current;
            if (burstRect.width > 0 && burstRect.Contains(ev.mousePosition) && (ev.type == EventType.Repaint || ev.type == EventType.MouseDrag || ev.type == EventType.MouseDown))
            {
                sightT = Mathf.Clamp01((ev.mousePosition.x - burstRect.x) / burstRect.width) * (float)turnSec;
                sightAuto = false;
            }
            int j = read != null && read.target >= 0 ? read.target : focus;
            if (j < 0 || j >= battle.Count) j = -1;
            if (sightAuto)
            {
                // where your rounds first meet the target, else the middle of the turn
                double first = double.PositiveInfinity;
                foreach (var h in gunPreview.hits)
                    if (h.shooter == 0 && (h.target == j || j < 0)) first = Math.Min(first, h.time - gunPreview.startTime);
                sightT = (float)(double.IsInfinity(first) ? turnSec / 2 : first);
            }

            // the lead ring's toggle is always there, even with the glass hidden
            guiRects.Add(toggle);
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 13 };
            if (GUI.Button(new Rect(toggle.x, toggle.y, size / 2 - 3, toggle.height), showLeadRing ? "Lead ring: on (L)" : "Lead ring: off (L)", btn)) showLeadRing = !showLeadRing;
            if (GUI.Button(new Rect(toggle.x + size / 2 + 3, toggle.y, size / 2 - 3, toggle.height), showSight ? "Gunsight: on (K)" : "Gunsight: off (K)", btn)) showSight = !showSight;
            // the moment on the burst bar
            if (showSight && burstRect.width > 0)
            {
                float mx = burstRect.x + sightT / (float)turnSec * burstRect.width;
                Fill(new Rect(mx - 1, burstRect.y - 4, 2, burstRect.height + 8), sightColor);
            }
            if (!showSight) return;

            guiRects.Add(glass); guiRects.Add(head);
            Fill(glass, sightGlass);
            var hs = new GUIStyle(small) { fontStyle = FontStyle.Bold };
            hs.normal.textColor = sightColor;
            GUI.Label(head, $"GUNSIGHT  {sightT:F2} s   ([ ] or hover the bar)", hs);

            GUI.BeginGroup(glass);
            DrawGlass(size, j, hs);
            GUI.EndGroup();
        }

        /// <summary>The glass's contents, in its own coordinates (0 to size).</summary>
        void DrawGlass(float size, int j, GUIStyle hs)
        {
            var gp = gunPreview;
            float cx = size / 2, cy = size / 2, rad = size / 2 - 6;
            float pxPerMil = rad / Mathf.Max(20f, sightFieldMil);
            int f = Mathf.Clamp(Mathf.RoundToInt(sightT / (float)gp.dt), 0, gp.FrameCount - 1);
            var me = gp.frames[0][f];
            if (me.down) return;
            Vec3d fwd = me.vel.Normalized;
            double a = me.alphaDeg * Math.PI / 180;
            Vec3d gun = (fwd * Math.Cos(a) + me.up * Math.Sin(a)).Normalized;   // the sight looks down the gun line
            Vec3d up = (me.up - gun * Vec3d.Dot(me.up, gun)).Normalized;
            Vec3d right = Vec3d.Cross(gun, up);
            Vec3d eye = me.pos;

            // where a point in the air sits on the glass; null behind you
            Vector2? Sight(Vec3d p, out double range)
            {
                Vec3d d = p - eye;
                range = Vec3d.Dot(d, gun);
                if (range < 1) return null;
                double x = Math.Atan2(Vec3d.Dot(d, right), range) * 1000, y = Math.Atan2(Vec3d.Dot(d, up), range) * 1000;
                return new Vector2(cx + (float)x * pxPerMil, cy - (float)y * pxPerMil);
            }
            bool OnGlass(Vector2 p) => (p - new Vector2(cx, cy)).sqrMagnitude <= rad * rad;

            // the rim and the boresight cross
            var rim = new Color(sightColor.r, sightColor.g, sightColor.b, 0.35f);
            for (int k = 0; k < 48; k++)
            {
                float t0 = 2 * Mathf.PI * k / 48, t1 = 2 * Mathf.PI * (k + 1) / 48;
                GuiLine(new Vector2(cx + rad * Mathf.Cos(t0), cy + rad * Mathf.Sin(t0)), new Vector2(cx + rad * Mathf.Cos(t1), cy + rad * Mathf.Sin(t1)), 1f, rim);
            }
            GuiLine(new Vector2(cx - 7, cy), new Vector2(cx - 2, cy), 1.5f, sightColor);
            GuiLine(new Vector2(cx + 2, cy), new Vector2(cx + 7, cy), 1.5f, sightColor);
            GuiLine(new Vector2(cx, cy - 7), new Vector2(cx, cy - 2), 1.5f, sightColor);
            GuiLine(new Vector2(cx, cy + 2), new Vector2(cx, cy + 7), 1.5f, sightColor);

            // the stream: every round of yours in the air at this moment, nearest first
            double span = SpanFt(j);
            var stream = new List<(double range, Vector2 at)>();
            foreach (var tr in gp.traces)
            {
                if (tr.owner != 0 || tr.missile) continue;
                var p = tr.At(f);
                if (p == null) continue;
                var s = Sight(p.Value, out double r);
                if (s == null || r < 120 || r > funnelMaxFt) continue;
                stream.Add((r, s.Value));
            }
            stream.Sort((x, y) => x.range.CompareTo(y.range));
            if (stream.Count >= 2)
            {
                var left = new List<Vector2>(); var rightEdge = new List<Vector2>();
                for (int k = 0; k < stream.Count; k++)
                {
                    Vector2 prev = stream[Math.Max(0, k - 1)].at, next = stream[Math.Min(stream.Count - 1, k + 1)].at;
                    Vector2 tan = next - prev;
                    if (tan.sqrMagnitude < 1e-6f) tan = Vector2.up;
                    tan.Normalize();
                    Vector2 n = new Vector2(-tan.y, tan.x);
                    float half = (float)(span / 2 / stream[k].range * 1000) * pxPerMil;
                    left.Add(stream[k].at + n * half);
                    rightEdge.Add(stream[k].at - n * half);
                }
                for (int k = 1; k < stream.Count; k++)
                {
                    float fade = 1f - 0.6f * (float)(stream[k].range / funnelMaxFt);
                    var c = new Color(sightColor.r, sightColor.g, sightColor.b, sightColor.a * fade);
                    if (OnGlass(left[k - 1]) && OnGlass(left[k])) GuiLine(left[k - 1], left[k], 2f, c);
                    if (OnGlass(rightEdge[k - 1]) && OnGlass(rightEdge[k])) GuiLine(rightEdge[k - 1], rightEdge[k], 2f, c);
                }
                // range ticks across the funnel every 1,000 ft
                for (double tick = 1000; tick < funnelMaxFt; tick += 1000)
                {
                    int k = stream.FindIndex(s => s.range >= tick);
                    if (k <= 0) continue;
                    if (OnGlass(left[k]) && OnGlass(rightEdge[k]))
                    {
                        Vector2 l = left[k], rr = rightEdge[k], d = (rr - l) * 0.18f;
                        var tc = new Color(sightColor.r, sightColor.g, sightColor.b, 0.5f);
                        GuiLine(l, l + d, 1f, tc);
                        GuiLine(rr, rr - d, 1f, tc);
                    }
                }
            }

            // the target's bright ghost, as a little wireframe at its true size and attitude
            string status = "No target";
            if (j >= 0 && j < gp.frames.Length)
            {
                var them = gp.frames[j][f];
                if (them.down || battle.Out(j)) status = "Target down";
                else
                {
                    Vec3d tf = them.vel.Normalized, tu = them.up, tr = Vec3d.Cross(tf, tu);
                    double half = span / 2, len = battle.combat[j].sizeFt > 0 ? 37 : 16;
                    Vec3d c = them.pos, nose = c + tf * len, tail = c - tf * len, wing = c + tf * (len * 0.15);
                    double now = sightT + gp.startTime;
                    bool hitNow = false; double fired = -1;
                    foreach (var h in gp.hits)
                        if (h.shooter == 0 && h.target == j && Math.Abs(h.time - now) < 0.08) { hitNow = true; fired = h.fireTime - gp.startTime; break; }
                    var col = hitNow ? hitTracer : new Color(1f, 0.45f, 0.35f, 0.95f);
                    var centre = Sight(c, out double range);
                    if (centre != null && OnGlass(centre.Value))
                    {
                        void Seg(Vec3d p, Vec3d q)
                        {
                            var sp = Sight(p, out _); var sq = Sight(q, out _);
                            if (sp != null && sq != null && OnGlass(sp.Value) && OnGlass(sq.Value)) GuiLine(sp.Value, sq.Value, 2f, col);
                        }
                        Seg(nose, tail);
                        Seg(wing - tr * half, wing + tr * half);
                        Seg(tail - tr * (half * 0.35), tail + tr * (half * 0.35));
                        Seg(tail, tail + tu * (half * 0.3));
                        status = hitNow ? $"ON TARGET  {range:F0} ft  (rounds fired at {fired:F2} s)" : $"Target {range:F0} ft";
                    }
                    else
                    {
                        // off the glass: a pointer on the rim toward it
                        Vec3d d = c - eye;
                        var dir = new Vector2((float)Vec3d.Dot(d, right), -(float)Vec3d.Dot(d, up));
                        if (Vec3d.Dot(d, gun) < 0 && dir.sqrMagnitude < 1e-6f) dir = Vector2.down;
                        dir.Normalize();
                        Vector2 at = new Vector2(cx, cy) + dir * (rad - 4), back = at - dir * 14, n = new Vector2(-dir.y, dir.x) * 6;
                        GuiLine(back + n, at, 2f, col);
                        GuiLine(back - n, at, 2f, col);
                        status = $"Target off the glass, {d.Length:F0} ft";
                    }
                }
            }
            var st = new GUIStyle(hs) { alignment = TextAnchor.LowerCenter, fontStyle = FontStyle.Normal };
            if (status.StartsWith("ON TARGET")) { st.normal.textColor = hitTracer; st.fontStyle = FontStyle.Bold; }
            GUI.Label(new Rect(0, size - 26, size, 22), status, st);
        }

        /// <summary>A straight line on the screen, w wide, in the current GUI coordinates.</summary>
        static void GuiLine(Vector2 a, Vector2 b, float w, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.01f) return;
            var old = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            Fill(new Rect(a.x, a.y - w / 2, len, w), c);
            GUI.matrix = old;
        }
    }
}
