using System;
using System.Collections.Generic;
using Ooda.Flight;
using Ooda.Flight.Core;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// 8, Gravity well (the rules are Well and Specials in Turn/Core/Specials.cs): thrown ahead and into your turn at the
    /// moment you pick on the MFD's trigger bar (drag its violet marker; 8 steps it 0, 1, 2, 3 s, then off). While planning,
    /// the one you're about to throw shows where it settles, and the open ones show as violet whirlpools with their core
    /// ring: fly inside your turn's side of one to turn tighter, drag a missile on your tail through it, or set it between you
    /// and a shooter so his rounds bend away. It pulls everyone, you too.
    /// </summary>
    public partial class TurnBattle
    {
        public Color wellColor = new Color(0.62f, 0.38f, 1f, 1f);

        // ---------- planning ----------

        void ToggleWell()
        {
            if (!plan.well && battle.wells[0] <= 0) { Notice("No gravity wells left."); return; }
            plan.well = !plan.well;
            if (plan.well) WellNotice();
            dirty = true;
        }

        /// <summary>The 8 key: off, then thrown at 0, 1, 2 and 3 s into the turn, then off again.</summary>
        void StepWell()
        {
            if (!plan.well) { if (battle.wells[0] <= 0) { Notice("No gravity wells left."); return; } plan.well = true; plan.wellAt = 0; }
            else if (plan.wellAt >= turnSec - 0.01) { plan.well = false; plan.wellAt = 0; }
            else plan.wellAt = Math.Min(turnSec, Math.Floor(plan.wellAt + 1.0001));
            if (plan.well) WellNotice();
            dirty = true;
        }

        void WellNotice() => Notice($"Gravity well: thrown ahead and into your turn {plan.wellAt:0.0} s in (drag the violet marker on the trigger bar, or 8 steps it). " +
                                    "It pulls every plane, missile and round near it for two turns: you turn tighter round it, missiles and shots bend off.");

        Rect WellMarker => new Rect(burstRect.x + (float)(Math.Min(plan.wellAt, turnSec) / turnSec) * burstRect.width - 8, burstRect.y - 9, 16, burstRect.height + 12);

        bool PressWell(Vector2 gui) => plan.well && burstRect.width > 0 && WellMarker.Contains(gui);

        void SetWellFromBar(float guiX)
        {
            float t = Mathf.Clamp01((guiX - burstRect.x) / burstRect.width) * turnSec;
            plan.wellAt = Mathf.Round(t / 0.05f) * 0.05f;
            dirty = true;
        }

        /// <summary>The well on the trigger bar: a violet ring on a stalk at its moment, and its time in the middle of the label line.</summary>
        void WellOnBar(Rect bar, GUIStyle lbl)
        {
            if (!plan.well) return;
            float x = bar.x + (float)(Math.Min(plan.wellAt, turnSec) / turnSec) * bar.width;
            var c = wellColor;
            Fill(new Rect(x - 1, bar.y - 2, 2, bar.height + 4), c);
            Fill(new Rect(x - 4, bar.y - 9, 8, 2), c); Fill(new Rect(x - 4, bar.y - 3, 8, 2), c);
            Fill(new Rect(x - 4, bar.y - 9, 2, 8), c); Fill(new Rect(x + 2, bar.y - 9, 2, 8), c);
            var st = new GUIStyle(lbl) { alignment = TextAnchor.MiddleCenter };
            st.normal.textColor = c;
            GUI.Label(new Rect(bar.x, bar.y - 14, bar.width, 12), $"WELL {plan.wellAt:0.00} S", st);
        }

        // ---------- drawing ----------

        /// <summary>While planning: the wells open now (and about to open), and where the one you plan to throw settles.</summary>
        void DrawLiveWells()
        {
            foreach (var b in battle.bullets)
                if (b is Well wl) Whirlpool(UnityFrame.Pos(wl.pos), Mathf.Max(0.25f, (float)wl.Strength), true, wl.side == battle.side[0], wl.GetHashCode() % 7);
            if (preview == null || preview.wells == null) return;
            foreach (var tr in preview.wells)
                if (tr.owner == 0 && tr.startFrame >= 1 && tr.points.Count > 0)
                {
                    var pts = new List<Vector3>();
                    for (int i = 0; i < tr.points.Count; i += 4) pts.Add(UnityFrame.Pos(tr.points[i]));
                    pts.Add(UnityFrame.Pos(tr.points[tr.points.Count - 1]));
                    if (pts.Count >= 2) lines.Strip(pts, 0.4f, new Color(wellColor.r, wellColor.g, wellColor.b, 0.4f));
                    Whirlpool(UnityFrame.Pos(tr.points[tr.points.Count - 1]), 0.45f, true, true, 0);
                }
        }

        /// <summary>The wells at frame now of a turn being played back.</summary>
        void DrawWells(TurnResult r, int now)
        {
            if (r == null || r.wells == null) return;
            foreach (var tr in r.wells)
            {
                var head = tr.At(now);
                if (head == null) continue;
                double age = tr.age0 + (now - tr.startFrame) * r.dt;
                float s = age < Specials.WellArmSec ? 0.25f
                        : Mathf.Clamp01((float)Math.Min((age - Specials.WellArmSec) / 0.25, (Specials.WellLifeSec - age) / Specials.WellFadeSec));
                Whirlpool(UnityFrame.Pos(head.Value), Mathf.Max(0.25f, s), false, tr.owner >= 0 && tr.owner < battle.Count && battle.side[tr.owner] == battle.side[0], tr.startFrame % 7);
            }
        }

        readonly List<Vector3> wellPts = new List<Vector3>(48);

        /// <summary>
        /// A gravity well at p: a dark heart, three spiral arms winding in (turning), a ring sliding in to the middle over and
        /// over, and with ring, the core's edge (where it pulls hardest). strength 0..1 fades it.
        /// </summary>
        void Whirlpool(Vector3 p, float strength, bool ring, bool ours, int seed)
        {
            Vector3 right = view != null ? view.Cam.transform.right : Vector3.right, up = view != null ? view.Cam.transform.up : Vector3.up;
            float core = (float)(Specials.WellCoreFt * Atmosphere.MPerFt);
            float spin = Time.time * 1.8f + seed;
            var c = wellColor; c.a = 0.85f * strength;
            // the heart: dark, ringed in light
            float heart = Mathf.Max(core * 0.08f, W(p) * 6f);
            lines.Line(p - up * heart * 0.5f, p + up * heart * 0.5f, 6f, new Color(0.06f, 0.02f, 0.12f, 0.95f * strength));
            wellPts.Clear();
            for (int k = 0; k < 20; k++) { float a = k * Mathf.PI / 10; wellPts.Add(p + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * heart); }
            lines.Strip(wellPts, 1.2f, new Color(0.9f, 0.8f, 1f, 0.9f * strength), true);
            // three arms winding in
            for (int arm = 0; arm < 3; arm++)
            {
                wellPts.Clear();
                for (int k = 0; k <= 24; k++)
                {
                    float f = k / 24f, rr = Mathf.Lerp(core * 1.2f, heart, f), a = spin + arm * 2.094f + f * 4.2f;
                    wellPts.Add(p + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * rr);
                }
                lines.Strip(wellPts, 0.9f, c, false, new Color(c.r, c.g, c.b, 0.1f * strength));
            }
            // a ring sliding in to the middle
            float slide = 1f - Mathf.Repeat(Time.time * 0.6f + seed * 0.13f, 1f);
            wellPts.Clear();
            for (int k = 0; k < 32; k++) { float a = k * Mathf.PI / 16; wellPts.Add(p + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * Mathf.Lerp(heart, core * 1.5f, slide)); }
            lines.Strip(wellPts, 0.5f, new Color(c.r, c.g, c.b, c.a * (1f - slide) * 0.8f), true);
            if (!ring) return;
            wellPts.Clear();
            for (int k = 0; k < 40; k++) { float a = k * Mathf.PI / 20; wellPts.Add(p + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * core); }
            lines.Strip(wellPts, 0.35f, new Color(c.r, c.g, c.b, (ours ? 0.3f : 0.5f) * strength), true);
        }
    }
}
