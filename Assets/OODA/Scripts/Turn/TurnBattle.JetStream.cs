using System;
using System.Collections.Generic;
using Ooda.Flight;
using Ooda.Flight.Core;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// ' (apostrophe), Jet stream (the rules are JetStream and Lane in Turn/Core/JetStream.cs): a wind lane laid at the moment
    /// you pick on the MFD's trigger bar (drag its sky-blue marker; ' steps it 0, 1, 2, 3 s, then off). Laid ahead it blows
    /// along your arrow, so you point it by planning the turn; ; (semicolon) or the JET switch turns it round, back down your
    /// path at whoever is on your tail. While planning, the lanes blowing now show as see-through tubes of streaming wind
    /// with chevrons the way they blow, and the one you are about to lay shows where it will lie: your ghost path already
    /// rides it. It carries everyone, you too.
    /// </summary>
    public partial class TurnBattle
    {
        public Color laneColor = new Color(0.62f, 0.9f, 1f, 1f);

        // ---------- planning ----------

        /// <summary>The JET switch: off, laid ahead, laid back, off.</summary>
        void ToggleLane()
        {
            if (!plan.lane) { if (battle.lanes[0] <= 0) { Notice("No jet streams left."); return; } plan.lane = true; plan.laneBack = false; }
            else if (!plan.laneBack) plan.laneBack = true;
            else { plan.lane = false; plan.laneBack = false; }
            if (plan.lane) LaneNotice();
            dirty = true;
        }

        /// <summary>The ' key: off, then laid at 0, 1, 2 and 3 s into the turn, then off again.</summary>
        void StepLane()
        {
            if (!plan.lane) { if (battle.lanes[0] <= 0) { Notice("No jet streams left."); return; } plan.lane = true; plan.laneAt = 0; }
            else if (plan.laneAt >= turnSec - 0.01) { plan.lane = false; plan.laneAt = 0; }
            else plan.laneAt = Math.Min(turnSec, Math.Floor(plan.laneAt + 1.0001));
            if (plan.lane) LaneNotice();
            dirty = true;
        }

        /// <summary>The ; key: the lane turned round, ahead along the arrow or back at a tailer (laid at 0 s if it was off).</summary>
        void FlipLane()
        {
            if (!plan.lane) { if (battle.lanes[0] <= 0) { Notice("No jet streams left."); return; } plan.lane = true; plan.laneAt = 0; plan.laneBack = true; }
            else plan.laneBack = !plan.laneBack;
            LaneNotice();
            dirty = true;
        }

        void LaneNotice() => Notice(plan.laneBack
            ? $"Jet stream, laid back {plan.laneAt:0.0} s in: a 180 kt headwind down your path for three turns. Anyone on your tail wades upstream and falls back; ; turns it round."
            : $"Jet stream, laid ahead {plan.laneAt:0.0} s in: a 180 kt wind along your arrow for three turns. Ride it to run or close in; ; turns it back at a tailer.");

        Rect LaneMarker => new Rect(burstRect.x + (float)(Math.Min(plan.laneAt, turnSec) / turnSec) * burstRect.width - 8, burstRect.y - 9, 16, burstRect.height + 12);

        bool PressLane(Vector2 gui) => plan.lane && burstRect.width > 0 && LaneMarker.Contains(gui);

        void SetLaneFromBar(float guiX)
        {
            float t = Mathf.Clamp01((guiX - burstRect.x) / burstRect.width) * turnSec;
            plan.laneAt = Mathf.Round(t / 0.05f) * 0.05f;
            dirty = true;
        }

        /// <summary>The lane on the trigger bar: a sky-blue stalk with a chevron the way it blows (right ahead, left back), and its time over it.</summary>
        void LaneOnBar(Rect bar, GUIStyle lbl)
        {
            if (!plan.lane) return;
            float x = bar.x + (float)(Math.Min(plan.laneAt, turnSec) / turnSec) * bar.width;
            var c = laneColor;
            Fill(new Rect(x - 1, bar.y - 2, 2, bar.height + 4), c);
            float s = plan.laneBack ? -1 : 1;
            for (int k = 0; k < 4; k++)   // a chevron, > or <
            {
                Fill(new Rect(x - 1 + s * (k - 2), bar.y - 9 + k, 2, 2), c);
                Fill(new Rect(x - 1 + s * (k - 2), bar.y - 3 - k, 2, 2), c);
            }
            var st = new GUIStyle(lbl) { alignment = TextAnchor.MiddleCenter };
            st.normal.textColor = c;
            GUI.Label(new Rect(x - 50, bar.y - 26, 100, 12), $"JET {(plan.laneBack ? "BACK" : "AHEAD")} {plan.laneAt:0.00} S", st);
        }

        // ---------- drawing ----------

        /// <summary>While planning: the lanes blowing now, and where the one you plan to lay will lie.</summary>
        void DrawLiveLanes()
        {
            foreach (var b in battle.bullets)
                if (b is Lane ln) WindTube(ln.pos, ln.dir, Mathf.Max(0.3f, (float)ln.Strength), ln.side == battle.side[0], ln.GetHashCode() % 7);
            if (preview == null || preview.lanes == null) return;
            foreach (var tr in preview.lanes)
                if (tr.owner == 0 && tr.startFrame >= 1) WindTube(tr.start, tr.dir, 0.55f, true, 0);
        }

        /// <summary>The lanes at frame now of a turn being played back.</summary>
        void DrawLanes(TurnResult r, int now)
        {
            if (r == null || r.lanes == null) return;
            foreach (var tr in r.lanes)
            {
                float s = (float)tr.StrengthAt(now, r.dt);
                if (s > 0) WindTube(tr.start, tr.dir, Mathf.Max(0.3f, s), tr.owner >= 0 && tr.owner < battle.Count && battle.side[tr.owner] == battle.side[0], tr.startFrame % 7);
            }
        }

        readonly List<Vector3> lanePts = new List<Vector3>(48);

        /// <summary>
        /// A jet stream from start (feet) blowing along dir: a faint ring every 2,000 ft with a chevron on it the way it blows,
        /// four faint rails along its rim, and streaks of wind racing down it. strength 0..1 fades it; theirs is a little
        /// brighter, so you see the enemy's.
        /// </summary>
        void WindTube(Vec3d start, Vec3d dir, float strength, bool ours, int seed)
        {
            Vector3 a = UnityFrame.Pos(start), d = UnityFrame.Dir(dir).normalized;
            float len = (float)(JetStream.LengthFt * Atmosphere.MPerFt), rad = (float)(JetStream.RadiusFt * Atmosphere.MPerFt);
            Vector3 u = Vector3.Cross(d, Vector3.up);
            if (u.sqrMagnitude < 1e-4f) u = Vector3.Cross(d, Vector3.right);
            u.Normalize();
            Vector3 v = Vector3.Cross(u, d).normalized;
            var c = laneColor;
            float alpha = strength * (ours ? 0.8f : 1f);

            // the rings, each with a chevron the way it blows
            int rings = 5;
            for (int k = 0; k <= rings; k++)
            {
                Vector3 m = a + d * (len * k / rings);
                lanePts.Clear();
                for (int j = 0; j < 28; j++) { float t = j * Mathf.PI * 2 / 28; lanePts.Add(m + (u * Mathf.Cos(t) + v * Mathf.Sin(t)) * rad); }
                lines.Strip(lanePts, 0.3f, new Color(c.r, c.g, c.b, 0.22f * alpha), true);
                if (k == rings) break;
                Vector3 tip = m + d * rad * 0.6f;
                float wing = rad * 0.45f;
                lines.Line(tip - d * wing + v * wing, tip, 0.9f, new Color(c.r, c.g, c.b, 0.65f * alpha));
                lines.Line(tip - d * wing - v * wing, tip, 0.9f, new Color(c.r, c.g, c.b, 0.65f * alpha));
            }
            // the rails along its rim
            for (int j = 0; j < 4; j++)
            {
                float t = j * Mathf.PI / 2 + Mathf.PI / 4;
                Vector3 off = (u * Mathf.Cos(t) + v * Mathf.Sin(t)) * rad;
                lines.Line(a + off, a + off + d * len, 0.25f, new Color(c.r, c.g, c.b, 0.16f * alpha));
            }
            // streaks of wind racing down it (shown at a third of its speed, so the eye can follow them)
            float run = (float)(JetStream.WindFps * Atmosphere.MPerFt) * 0.35f, streak = len * 0.05f;
            for (int s = 0; s < 14; s++)
            {
                float h1 = Hash01(seed * 31 + s * 7 + 1), h2 = Hash01(seed * 31 + s * 7 + 2), h3 = Hash01(seed * 31 + s * 7 + 3);
                float along = Mathf.Repeat(h1 * len + Time.time * run * (0.8f + 0.4f * h3), len);
                float t = h2 * Mathf.PI * 2, rr = rad * Mathf.Sqrt(h3) * 0.85f;
                Vector3 p = a + d * along + (u * Mathf.Cos(t) + v * Mathf.Sin(t)) * rr;
                float fade = Mathf.Clamp01(Mathf.Min(along, len - along) / (len * 0.08f));
                lines.Line(p - d * streak, p, 0.7f, new Color(1f, 1f, 1f, 0.55f * alpha * fade));
            }
        }

        static float Hash01(int n)
        {
            unchecked
            {
                uint x = (uint)n * 2654435761u;
                x ^= x >> 15; x *= 2246822519u; x ^= x >> 13;
                return (x & 0xFFFFFF) / 16777216f;
            }
        }
    }
}
