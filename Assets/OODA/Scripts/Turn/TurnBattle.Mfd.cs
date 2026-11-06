using System;
using System.Collections.Generic;
using System.Linq;
using Ooda.Flight.Core;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// The gun MFD, bottom right while planning: a multifunction display in its bezel, with push buttons round the edge
    /// (FIRE at the predicted hits and the missile's ARM along the bottom, the funnel's mode and the lead ring down the
    /// left) and a green phosphor screen. On the screen: the gun funnel as seen from your seat at the burst bar's moment
    /// (your rounds in the air as two lines a wingspan apart, a rung each 1,000 ft, the gun cross and the horizon, and
    /// the target's wings in red where it is then), and under it the trigger bar: drag on it to set the burst, the red band
    /// where rounds fired would connect, a tick per predicted hit.
    /// </summary>
    public partial class TurnBattle
    {
        static readonly Color MfdBezel = new Color(0.17f, 0.18f, 0.18f, 0.97f), MfdBezelEdge = new Color(0.36f, 0.37f, 0.37f, 1f),
                              MfdGlass = new Color(0.015f, 0.05f, 0.025f, 0.96f), MfdGreen = new Color(0.35f, 1f, 0.42f, 1f),
                              MfdDim = new Color(0.35f, 1f, 0.42f, 0.7f), MfdRed = new Color(1f, 0.25f, 0.2f, 1f),
                              MfdOsb = new Color(0.62f, 0.64f, 0.64f, 1f), MfdOsbDark = new Color(0.08f, 0.08f, 0.08f, 1f);
        const double MfdFunnelFt = 4500;   // the MFD's funnel runs out this far: a gunsight looks a little past the guns' best range
        GUIStyle mfdText, mfdSmall;

        /// <summary>The whole MFD in r: the bezel, its buttons, the glass with the funnel and the trigger bar.</summary>
        void GunMfd(Rect r)
        {
            if (mfdText == null)
            {
                mfdText = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, padding = new RectOffset(0, 0, 0, 0) };
                mfdSmall = new GUIStyle(mfdText) { fontSize = 10, fontStyle = FontStyle.Normal };
            }
            guiRects.Add(r);
            // the bezel and its screws
            Fill(r, MfdBezelEdge);
            Fill(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), MfdBezel);
            foreach (var (sx, sy) in new[] { (r.x + 7, r.y + 7), (r.xMax - 11, r.y + 7), (r.x + 7, r.yMax - 11), (r.xMax - 11, r.yMax - 11) })
            { Fill(new Rect(sx, sy, 4, 4), MfdOsbDark); Fill(new Rect(sx + 1, sy + 1, 2, 2), MfdBezelEdge); }
            // the bezel round the glass, where the push buttons sit: thinner on a smaller MFD, so the glass keeps its room
            float rim = Mathf.Clamp(r.height * 0.09f, 20f, 30f);
            Rect glass = new Rect(r.x + rim, r.y + rim, r.width - 2 * rim, r.height - 2 * rim);
            Fill(new Rect(glass.x - 2, glass.y - 2, glass.width + 4, glass.height + 4), MfdOsbDark);
            Fill(glass, MfdGlass);

            // the push buttons: four along the bottom, three down the left; the label for each is on the glass beside it
            int left = battle.missiles[0];
            bool rdr = RadarPage;
            int leftOsbs = HasRadar ? 4 : 3;
            Rect Osb(int k, bool bottom)
            {
                if (bottom) { float bw = glass.width / 4; return new Rect(glass.x + bw * (k + 0.5f) - 12, r.yMax - rim + (rim - 14) / 2, 24, 14); }
                // down the left, below the header line and above the trigger bar
                float bh = (glass.height - 18 - 46) / leftOsbs; return new Rect(r.x + (rim - 14) / 2, glass.y + 18 + bh * (k + 0.5f) - 9, 14, 18);
            }
            bool Button(Rect b, bool enabled)
            {
                Fill(b, MfdOsbDark);
                Fill(new Rect(b.x + 1, b.y + 1, b.width - 2, b.height - 2), enabled ? MfdOsb : new Color(MfdOsb.r * 0.6f, MfdOsb.g * 0.6f, MfdOsb.b * 0.6f, 1f));
                Fill(new Rect(b.x + 1, b.yMax - 4, b.width - 2, 3), new Color(0, 0, 0, 0.35f));
                if (controlsLocked) return false;
                bool was = GUI.enabled; GUI.enabled = was && enabled;
                bool hit = GUI.Button(b, GUIContent.none, GUIStyle.none);
                GUI.enabled = was;
                return hit;
            }
            // the label on the glass by a button; boxed when that thing is on
            void Legend(Rect at, string text, bool boxed, TextAnchor anchor, Color c)
            {
                var st = new GUIStyle(mfdText) { alignment = anchor };
                st.normal.textColor = c;
                if (boxed)
                {
                    float tw = st.CalcSize(new GUIContent(text)).x + 6;
                    float bx = anchor == TextAnchor.MiddleCenter ? at.center.x - tw / 2 : at.x - 3;
                    Rect box = new Rect(bx, at.center.y - 8, tw, 16);
                    Fill(new Rect(box.x, box.y, box.width, 1), c); Fill(new Rect(box.x, box.yMax - 1, box.width, 1), c);
                    Fill(new Rect(box.x, box.y, 1, box.height), c); Fill(new Rect(box.xMax - 1, box.y, 1, box.height), c);
                }
                GUI.Label(at, text, st);
            }

            Action clicked = null;
            // bottom: FIRE at the hits, the missile's ARM (G), the timeline, the IR view
            var b0 = Osb(0, true); if (Button(b0, true)) clicked = FireAtPredicted;
            int sparrows = battle.radarMissiles[0];
            var b1 = Osb(1, true);
            if (rdr) { if (Button(b1, sparrows > 0)) clicked = () => ArmRadarMissile(!plan.radarMissile); }
            else if (Button(b1, left > 0)) clicked = () => ArmMissile(!plan.missile);
            var b2 = Osb(2, true); if (Button(b2, true)) clicked = () => showMoves = !showMoves;
            var b3 = Osb(3, true); if (Button(b3, true)) clicked = ToggleIr;
            float ly = glass.yMax - 16;
            Legend(new Rect(b0.center.x - 30, ly, 60, 14), "FIRE", false, TextAnchor.MiddleCenter, MfdGreen);
            if (rdr) Legend(new Rect(b1.center.x - 30, ly, 60, 14), sparrows > 0 ? $"RDR {sparrows}" : "RDR 0", plan.radarMissile, TextAnchor.MiddleCenter, plan.radarMissile ? MfdRed : MfdGreen);
            else Legend(new Rect(b1.center.x - 30, ly, 60, 14), left > 0 ? $"ARM {left}" : "MSL 0", plan.missile, TextAnchor.MiddleCenter, plan.missile ? MfdRed : MfdGreen);
            Legend(new Rect(b2.center.x - 30, ly, 60, 14), "TIME", showMoves || plan.HasMarks, TextAnchor.MiddleCenter, MfdGreen);
            Legend(new Rect(b3.center.x - 30, ly, 60, 14), "IR", irView, TextAnchor.MiddleCenter, MfdGreen);
            // left: the funnel's mode (K), the lead ring (L), the next target (N)
            var l0 = Osb(0, false); if (Button(l0, true)) clicked = NextSightMode;
            var l1 = Osb(1, false); if (Button(l1, true)) clicked = () => showLeadRing = !showLeadRing;
            var l2 = Osb(2, false); if (Button(l2, true)) clicked = CycleTarget;
            string fm = sightMode == SightMode.Off ? "FNL OFF" : sightMode == SightMode.Hover ? "FNL HOV" : "FNL ON";
            Legend(new Rect(glass.x + 6, l0.center.y - 7, 70, 14), fm, sightMode != SightMode.Off, TextAnchor.MiddleLeft, MfdGreen);
            Legend(new Rect(glass.x + 6, l1.center.y - 7, 70, 14), "RING", showLeadRing, TextAnchor.MiddleLeft, MfdGreen);
            Legend(new Rect(glass.x + 6, l2.center.y - 7, 70, 14), "TGT", false, TextAnchor.MiddleLeft, MfdGreen);
            if (HasRadar)
            {
                var l3 = Osb(3, false); if (Button(l3, true)) clicked = ToggleRadarPage;
                Legend(new Rect(glass.x + 6, l3.center.y - 7, 70, 14), "RDR", rdr, TextAnchor.MiddleLeft, MfdGreen);
            }

            // the trigger bar along the bottom of the glass, over the button legends
            Rect bar = new Rect(glass.x + 58, glass.yMax - 46, glass.width - 66, 14);
            TriggerBar(bar);
            // the funnel in all the room left above it, right of the left legends (its range line along its bottom)
            Rect scope = new Rect(glass.x + 58, glass.y + 16, glass.width - 64, bar.y - glass.y - 32);
            if (rdr) { RadarHeader(new Rect(glass.x + 6, glass.y + 2, glass.width - 12, 14)); RadarScope(scope); }
            else { MfdHeader(new Rect(glass.x + 6, glass.y + 2, glass.width - 12, 14), left); MfdFunnel(scope); }
            clicked?.Invoke();
        }

        /// <summary>The line across the top of the glass: GUN, the hits on the bright ghost, the missile's state.</summary>
        void MfdHeader(Rect r, int missilesLeft)
        {
            int predicted = 0, theirs = 0;
            if (preview != null) foreach (var h in preview.hits) { if (h.shooter == 0) predicted++; if (h.target == 0) theirs++; }
            var st = new GUIStyle(mfdText);
            st.normal.textColor = MfdGreen;
            GUI.Label(r, "GUN", st);
            string sure = confidencePct >= 0 ? $" {confidencePct}%" : confidenceHits >= 0 ? $" {confidenceHits}/{confidenceOf}" : "";
            st.alignment = TextAnchor.MiddleCenter;
            st.normal.textColor = predicted > 0 ? MfdRed : MfdGreen;
            GUI.Label(r, predicted > 0 ? $"HITS {predicted}{sure}" : "NO HITS", st);
            string msl;
            if (missilesLeft <= 0) msl = "";
            else if (plan.missile)
            {
                var mt = preview?.traces.FirstOrDefault(tr => tr.missile && tr.owner == 0);
                msl = mt == null ? "MSL RAIL" : mt.hit ? "MSL HIT" : "MSL MISS";
            }
            else msl = battle.LockTarget(0, focus) >= 0 ? "LOCK" : "NO LOCK";
            st.alignment = TextAnchor.MiddleRight;
            st.normal.textColor = theirs > 0 ? MfdRed : MfdGreen;
            GUI.Label(r, theirs > 0 ? $"{msl}  HIT ON YOU {theirs}" : msl, st);
        }

        /// <summary>The trigger bar on the glass: burstRect, so dragging on it sets the burst as the old bar did.</summary>
        void TriggerBar(Rect bar)
        {
            burstRect = bar;
            float w = bar.width;
            var lbl = new GUIStyle(mfdSmall);
            lbl.normal.textColor = MfdGreen;
            lbl.wordWrap = false; lbl.clipping = TextClipping.Clip;
            GUI.Label(new Rect(bar.x, bar.y - 14, w, 12), plan.burstEnd > plan.burstStart ? $"TRIG {plan.burstStart:F2}-{plan.burstEnd:F2} S" : plan.mine || plan.well ? "TRIG SAFE" : w >= 230 ? "TRIG SAFE  (DRAG TO SET A BURST)" : "TRIG SAFE  (DRAG A BURST)", lbl);   // the mine's time takes the right of the line
            // the hit window: when rounds fired would connect on the bright ghost
            foreach (var hw in hitWindows)
                Fill(new Rect(bar.x + (float)(hw.a / turnSec) * w, bar.y + 1, Mathf.Max(2f, (float)((hw.b - hw.a) / turnSec) * w), bar.height - 2), new Color(MfdRed.r, MfdRed.g, MfdRed.b, 0.35f));
            if (plan.burstEnd != plan.burstStart)
            {
                double a = Math.Min(plan.burstStart, plan.burstEnd), b = Math.Max(plan.burstStart, plan.burstEnd);
                Fill(new Rect(bar.x + (float)(a / turnSec) * w, bar.y + 4, (float)((b - a) / turnSec) * w, bar.height - 8), MfdGreen);
            }
            // the frame of the bar, and a tick each second
            Fill(new Rect(bar.x, bar.y, w, 1), MfdGreen); Fill(new Rect(bar.x, bar.yMax - 1, w, 1), MfdGreen);
            Fill(new Rect(bar.x, bar.y, 1, bar.height), MfdGreen); Fill(new Rect(bar.xMax - 1, bar.y, 1, bar.height), MfdGreen);
            for (int s = 1; s < (int)turnSec; s++) Fill(new Rect(bar.x + s / (float)turnSec * w, bar.yMax, 1, 4), MfdGreen);
            if (preview != null)
                foreach (var h in preview.hits)
                {
                    if (h.shooter != 0 && h.target != 0) continue;
                    float x = bar.x + (float)((h.fireTime - preview.startTime) / turnSec) * w;
                    Fill(new Rect(x - 1, bar.y - 3, 2, bar.height + 6), h.shooter == 0 ? Color.white : MfdRed);
                }
            MineOnBar(bar, lbl);
            WellOnBar(bar, lbl);
            lbl.normal.textColor = MfdDim;
            for (int s = 0; s <= (int)turnSec; s++)
                GUI.Label(new Rect(bar.x + s / (float)turnSec * w - 10, bar.yMax + 3, 20, 11), $"{s}", new GUIStyle(lbl) { alignment = TextAnchor.MiddleCenter });
        }

        /// <summary>
        /// The funnel on the glass, as seen down the guns from your ghost's seat at the funnel's moment: the gun cross,
        /// the horizon, your rounds in the air as two lines a wingspan apart (red when they're on the target then), a rung
        /// at 1,000 and 2,000 ft, and the target's wings in red where it is then.
        /// </summary>
        void MfdFunnel(Rect v)
        {
            Rect clip = new Rect(v.x, v.y, v.width, v.height - 14);   // the lines stop above the range line along the bottom
            var c = new Vector2(v.center.x, v.y + v.height * 0.42f);   // the gun cross a little high, like a gunsight's
            float k = Mathf.Min(v.width, v.height) * 0.5f / Mathf.Tan(5f * Mathf.Deg2Rad);   // px per unit of angle: ±5° fills it, like a gunsight
            // the gun cross
            Fill(new Rect(c.x - 9, c.y, 6, 1), MfdGreen); Fill(new Rect(c.x + 4, c.y, 6, 1), MfdGreen);
            Fill(new Rect(c.x, c.y - 9, 1, 6), MfdGreen); Fill(new Rect(c.x, c.y + 4, 1, 6), MfdGreen);
            var dim = new GUIStyle(mfdSmall) { alignment = TextAnchor.MiddleCenter };
            dim.normal.textColor = MfdDim;
            var gp = gunPreview;
            if (gp == null || battle.Out(0)) { GUI.Label(new Rect(v.x, v.yMax - 14, v.width, 12), "NO GUN SOLUTION", dim); return; }
            int f = FunnelFrame();
            var me = gp.frames[0][f];
            if (me.down) return;
            GunAxes(me, out var gun, out var up, out var right);
            Vector2? P(Vec3d at)
            {
                Vec3d d = at - me.pos;
                double fwd = Vec3d.Dot(d, gun);
                if (fwd < 1) return null;
                return new Vector2(c.x + (float)(Vec3d.Dot(d, right) / fwd) * k, c.y - (float)(Vec3d.Dot(d, up) / fwd) * k);
            }

            // the horizon: level directions, dashed
            var flat = new Vec3d(gun.x, gun.y, 0);
            if (flat.Length > 1e-3)
            {
                flat = flat.Normalized;
                var side = Vec3d.Cross(flat, new Vec3d(0, 0, 1)).Normalized;
                Vector2? h0 = P(me.pos + (flat - side * 0.6) * 10000), h1 = P(me.pos + (flat + side * 0.6) * 10000);
                if (h0 != null && h1 != null)
                {
                    Vector2 a = h0.Value, b = h1.Value;
                    for (float t = 0; t < 1; t += 0.08f) MfdLine(Vector2.Lerp(a, b, t), Vector2.Lerp(a, b, t + 0.05f), 1, MfdDim, clip);
                }
            }

            // the stream: every round of yours in the air then, nearest first
            var stream = new List<(double range, Vec3d at)>();
            foreach (var tr in gp.traces)
            {
                if (tr.owner != 0 || tr.missile) continue;
                var p = tr.At(f);
                if (p == null) continue;
                double rr = Vec3d.Dot(p.Value - me.pos, gun);
                if (rr < 600 || rr > MfdFunnelFt) continue;   // nearer, the stream is still sorting itself out off the guns
                stream.Add((rr, p.Value));
            }
            stream.Sort((x, y) => x.range.CompareTo(y.range));
            // smooth it: the rounds averaged in 200 ft steps of range, so the edges read as two clean lines
            var smooth = new List<(double range, Vec3d at)>();
            for (int a = 0; a < stream.Count;)
            {
                double bin = Math.Floor(stream[a].range / 200);
                Vec3d sum = Vec3d.Zero; double rs = 0; int n = 0;
                for (; a < stream.Count && Math.Floor(stream[a].range / 200) == bin; a++) { sum = sum + stream[a].at; rs += stream[a].range; n++; }
                smooth.Add((rs / n, sum * (1.0 / n)));
            }
            stream = smooth;
            int j = FunnelTarget();
            double half = SpanFt(j) / 2;
            bool hitNow = FunnelHit(j, out _);
            Color col = hitNow ? MfdRed : MfdGreen;
            for (int i = 1; i < stream.Count; i++)
            {
                Vector2? a0 = P(stream[i - 1].at - right * half), a1 = P(stream[i].at - right * half);
                Vector2? b0 = P(stream[i - 1].at + right * half), b1 = P(stream[i].at + right * half);
                if (a0 != null && a1 != null) MfdLine(a0.Value, a1.Value, 2, col, clip);
                if (b0 != null && b1 != null) MfdLine(b0.Value, b1.Value, 2, col, clip);
            }
            foreach (double tick in new[] { 1000.0, 2000.0 })
            {
                int i = stream.FindIndex(s => s.range >= tick);
                if (i <= 0) continue;
                Vector2? a = P(stream[i].at - right * half), b = P(stream[i].at + right * half);
                if (a == null || b == null) continue;
                MfdLine(a.Value, b.Value, 1, new Color(col.r, col.g, col.b, 0.8f), clip);
                if (clip.Contains(b.Value)) GUI.Label(new Rect(b.Value.x + 3, b.Value.y - 6, 20, 12), (tick / 1000).ToString("0"), dim);
            }

            // the target where it is then: its wings across, its fin up
            if (j >= 0 && j < gp.frames.Length && !battle.Out(j))
            {
                var them = gp.frames[j][Mathf.Min(f, gp.frames[j].Length - 1)];
                if (!them.down)
                {
                    Vec3d tr = Vec3d.Cross(them.vel.Normalized, them.up).Normalized;
                    double tspan = SpanFt(j) / 2;
                    Vector2? w0 = P(them.pos - tr * tspan), w1 = P(them.pos + tr * tspan), mid = P(them.pos), fin = P(them.pos + them.up * tspan * 0.4);
                    Color tc = hitNow ? Color.white : MfdRed;
                    if (w0 != null && w1 != null && mid != null)
                    {
                        // never smaller than a little plane mark, so a far one still shows
                        Vector2 half2 = (w1.Value - w0.Value) / 2;
                        if (half2.magnitude < 6) half2 = half2.magnitude < 1e-3f ? new Vector2(6, 0) : half2.normalized * 6;
                        MfdLine(mid.Value - half2, mid.Value + half2, 2, tc, clip);
                        Vector2 finV = fin != null ? fin.Value - mid.Value : Vector2.zero;
                        if (finV.magnitude < 4) finV = new Vector2(-half2.y, half2.x).normalized * 4 * (finV.y > 0 ? 1 : -1);
                        MfdLine(mid.Value, mid.Value + finV, 2, tc, clip);
                    }
                    double range = (them.pos - me.pos).Length;
                    bool inside = mid != null && v.Contains(mid.Value);
                    var rs = new GUIStyle(mfdSmall) { alignment = TextAnchor.MiddleRight };
                    rs.normal.textColor = hitNow ? MfdRed : inside ? MfdGreen : MfdDim;
                    GUI.Label(new Rect(v.x, v.yMax - 12, v.width, 12), hitNow ? $"ON TGT {range:F0} FT" : inside ? $"TGT {range:F0} FT" : $"OFF SCOPE {range:F0} FT", rs);
                }
            }
            GUI.Label(new Rect(v.x, v.yMax - 12, v.width, 12), $"AT {ArriveT:F1} S", new GUIStyle(dim) { alignment = TextAnchor.MiddleLeft });
        }

        /// <summary>A line on the glass, cut to the rect v.</summary>
        static void MfdLine(Vector2 a, Vector2 b, float width, Color c, Rect v)
        {
            // Liang-Barsky against v
            float t0 = 0, t1 = 1, dx = b.x - a.x, dy = b.y - a.y;
            bool Clip(float p, float q)
            {
                if (Mathf.Abs(p) < 1e-6f) return q >= 0;
                float t = q / p;
                if (p < 0) { if (t > t1) return false; if (t > t0) t0 = t; }
                else { if (t < t0) return false; if (t < t1) t1 = t; }
                return true;
            }
            if (!Clip(-dx, a.x - v.x) || !Clip(dx, v.xMax - a.x) || !Clip(-dy, a.y - v.y) || !Clip(dy, v.yMax - a.y)) return;
            Vector2 p0 = a + new Vector2(dx, dy) * t0, p1 = a + new Vector2(dx, dy) * t1;
            float len = (p1 - p0).magnitude;
            if (len < 0.5f) return;
            var old = GUI.matrix;
            RotateGui(Mathf.Atan2(p1.y - p0.y, p1.x - p0.x) * Mathf.Rad2Deg, p0);
            Fill(new Rect(p0.x, p0.y - width / 2, len, width), c);
            GUI.matrix = old;
        }
    }
}
