using System;
using System.Collections.Generic;
using Ooda.Flight;
using Ooda.Flight.Core;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// Y, Time slow bomb (the rules are TimeBubble and Specials in Turn/Core/Specials.cs): thrown at the nearest threat (a
    /// missile on you first, else the nearest enemy) at the moment you pick on the MFD's trigger bar (drag its teal marker; Y
    /// steps it 0, 1, 2, 3 s, then off). It bursts (clear of you) into a bubble where every plane, round and missile, both sides, runs
    /// at half speed for two turns. While planning, the bubbles up now show as clock-faced spheres, and the
    /// one you're about to throw shows its throw line and where it bursts.
    /// </summary>
    public partial class TurnBattle
    {
        public Color slowColor = new Color(0.35f, 0.95f, 0.88f, 1f);
        bool dragSlow;

        // ---------- planning ----------

        void ToggleSlow()
        {
            if (!plan.slow && battle.slowBombs[0] <= 0) { Notice("No time slow bombs left."); return; }
            plan.slow = !plan.slow;
            if (plan.slow) SlowNotice();
            dirty = true;
        }

        /// <summary>The Y key: off, then thrown at 0, 1, 2 and 3 s into the turn, then off again.</summary>
        void StepSlow()
        {
            if (!plan.slow) { if (battle.slowBombs[0] <= 0) { Notice("No time slow bombs left."); return; } plan.slow = true; plan.slowAt = 0; }
            else if (plan.slowAt >= turnSec - 0.01) { plan.slow = false; plan.slowAt = 0; }
            else plan.slowAt = Math.Min(turnSec, Math.Floor(plan.slowAt + 1.0001));
            if (plan.slow) SlowNotice();
            dirty = true;
        }

        void SlowNotice() => Notice($"Time slow bomb: thrown {plan.slowAt:0.0} s in at the nearest threat (a missile on you first) within {Specials.SlowReachFt:N0} ft " +
                                    "(drag the teal marker on the trigger bar, or Y steps it). It bursts clear of you, and everything in it, you too, crawls at half speed for two turns.");

        Rect SlowMarker => new Rect(burstRect.x + (float)(Math.Min(plan.slowAt, turnSec) / turnSec) * burstRect.width - 8, burstRect.y - 9, 16, burstRect.height + 12);

        bool PressSlow(Vector2 gui) => plan.slow && burstRect.width > 0 && SlowMarker.Contains(gui);

        void SetSlowFromBar(float guiX)
        {
            float t = Mathf.Clamp01((guiX - burstRect.x) / burstRect.width) * turnSec;
            plan.slowAt = Mathf.Round(t / 0.05f) * 0.05f;
            dirty = true;
        }

        /// <summary>The slow bomb on the trigger bar: a teal hourglass on a stalk at its moment, its time just under the bar's top.</summary>
        void SlowOnBar(Rect bar, GUIStyle lbl)
        {
            if (!plan.slow) return;
            float x = bar.x + (float)(Math.Min(plan.slowAt, turnSec) / turnSec) * bar.width;
            var c = slowColor;
            Fill(new Rect(x - 1, bar.y - 2, 2, bar.height + 4), c);
            // an hourglass: two bars and a pinched middle
            Fill(new Rect(x - 4, bar.y - 10, 8, 2), c); Fill(new Rect(x - 4, bar.y - 2, 8, 2), c);
            Fill(new Rect(x - 3, bar.y - 8, 6, 2), c); Fill(new Rect(x - 1, bar.y - 6, 2, 2), c); Fill(new Rect(x - 3, bar.y - 4, 6, 2), c);
            var st = new GUIStyle(lbl) { alignment = TextAnchor.MiddleLeft };
            st.normal.textColor = c;
            float lx = Mathf.Min(x + 6, bar.xMax - 70);
            GUI.Label(new Rect(lx, bar.y + 1, 70, Mathf.Max(10, bar.height - 2)), $"SLOW {plan.slowAt:0.00}", st);
        }

        // ---------- drawing ----------

        /// <summary>While planning: the bubbles up now (and bombs in flight), and where the one you plan to throw bursts.</summary>
        void DrawLiveBubbles()
        {
            foreach (var b in battle.bullets)
                if (b is TimeBubble tb)
                {
                    if (tb.Burst) ClockBubble(UnityFrame.Pos(tb.pos), Mathf.Max(0.3f, (float)tb.Strength), tb.side == battle.side[0], tb.GetHashCode() % 7);
                    else BombDot(UnityFrame.Pos(tb.pos));
                }
            if (preview == null || preview.bubbles == null) return;
            foreach (var tr in preview.bubbles)
                if (tr.owner == 0 && tr.startFrame >= 1 && tr.points.Count > 0)
                {
                    var from = UnityFrame.Pos(tr.points[0]);
                    var at = UnityFrame.Pos(tr.points[tr.points.Count - 1]);
                    lines.Line(from, at, 0.6f, new Color(slowColor.r, slowColor.g, slowColor.b, 0.55f));
                    ClockBubble(at, 0.5f, true, 0);
                }
        }

        /// <summary>The bubbles at frame now of a turn being played back.</summary>
        void DrawBubbles(TurnResult r, int now)
        {
            if (r == null || r.bubbles == null) return;
            foreach (var tr in r.bubbles)
            {
                var head = tr.At(now);
                if (head == null) continue;
                double age = tr.age0 + (now - tr.startFrame) * r.dt;
                var p = UnityFrame.Pos(head.Value);
                if (age < Specials.SlowFlySec) { BombDot(p); continue; }
                float s = Mathf.Clamp01((float)Math.Min((age - Specials.SlowFlySec) / 0.15, (Specials.SlowFlySec + Specials.SlowLifeSec - age) / Specials.SlowFadeSec));
                // the burst: a bright ring flung out over the first quarter second
                double since = age - Specials.SlowFlySec;
                if (since < 0.3) Ring(p, (float)(Specials.SlowRadiusFt * Atmosphere.MPerFt) * (float)(since / 0.3) * 1.15f, 2.2f, new Color(1f, 1f, 1f, 0.9f * (1f - (float)(since / 0.3))));
                ClockBubble(p, Mathf.Max(0.2f, s), tr.owner >= 0 && tr.owner < battle.Count && battle.side[tr.owner] == battle.side[0], tr.startFrame % 7);
            }
        }

        readonly List<Vector3> slowPts = new List<Vector3>(64);

        void BombDot(Vector3 p)
        {
            Vector3 up = view != null ? view.Cam.transform.up : Vector3.up;
            float h = W(p) * 5f;
            lines.Line(p - up * h, p + up * h, 7f, new Color(0.08f, 0.16f, 0.18f, 0.95f));
            Ring(p, h * 1.4f, 1.2f, slowColor);
        }

        /// <summary>A camera-facing circle of radius rad at p.</summary>
        void Ring(Vector3 p, float rad, float width, Color c)
        {
            Vector3 right = view != null ? view.Cam.transform.right : Vector3.right, up = view != null ? view.Cam.transform.up : Vector3.up;
            slowPts.Clear();
            for (int k = 0; k < 48; k++) { float a = k * Mathf.PI / 24; slowPts.Add(p + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * rad); }
            lines.Strip(slowPts, width, c, true);
        }

        /// <summary>
        /// A time slow bubble at p: its edge as a glassy sphere (a camera-facing rim and two tilted rings), a clock face of
        /// twelve ticks inside it and two hands creeping round, slowly. strength 0..1 fades it; ours is a little fainter.
        /// </summary>
        void ClockBubble(Vector3 p, float strength, bool ours, int seed)
        {
            Vector3 right = view != null ? view.Cam.transform.right : Vector3.right, up = view != null ? view.Cam.transform.up : Vector3.up;
            Vector3 fwd = Vector3.Cross(right, up);
            float rad = (float)(Specials.SlowRadiusFt * Atmosphere.MPerFt);
            var c = slowColor; c.a = (ours ? 0.6f : 0.85f) * strength;
            // the rim, and an inner glow ring
            Ring(p, rad, 1.6f, c);
            Ring(p, rad * 0.94f, 0.6f, new Color(c.r, c.g, c.b, c.a * 0.35f));
            // two rings round the sphere, turning lazily, so it reads round
            float spin = Time.time * 0.12f + seed;
            for (int ring = 0; ring < 2; ring++)
            {
                Quaternion q = Quaternion.AngleAxis(spin * Mathf.Rad2Deg + ring * 90f, up) * Quaternion.AngleAxis(70f, right);
                slowPts.Clear();
                for (int k = 0; k < 48; k++) { float a = k * Mathf.PI / 24; slowPts.Add(p + q * (new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * rad)); }
                lines.Strip(slowPts, 0.5f, new Color(c.r, c.g, c.b, c.a * 0.4f), true);
            }
            // the clock face: twelve ticks
            for (int k = 0; k < 12; k++)
            {
                float a = k * Mathf.PI / 6;
                Vector3 d = right * Mathf.Cos(a) + up * Mathf.Sin(a);
                float inner = k % 3 == 0 ? 0.74f : 0.82f;
                lines.Line(p + d * rad * inner, p + d * rad * 0.9f, k % 3 == 0 ? 2.2f : 1.2f, c);
            }
            // the hands: crawling (time is slow in here)
            float minute = -Time.time * 0.35f + seed, hour = minute / 12f;
            Vector3 dm = right * Mathf.Sin(minute) + up * Mathf.Cos(minute), dh = right * Mathf.Sin(hour) + up * Mathf.Cos(hour);
            lines.Line(p, p + dm * rad * 0.66f, 1.6f, c);
            lines.Line(p, p + dh * rad * 0.42f, 2.6f, c);
            lines.Line(p - fwd * 0.01f - up * W(p) * 3f, p + up * W(p) * 3f, 6f, new Color(c.r, c.g, c.b, c.a));
        }
    }
}
