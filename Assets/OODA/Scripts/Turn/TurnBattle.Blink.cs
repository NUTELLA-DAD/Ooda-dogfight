using System;
using System.Collections.Generic;
using Ooda.Flight;
using Ooda.Flight.Core;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// Y, Blink (the rules are Battle.Blink and Specials in Turn/Core): at the moment you pick on the MFD's trigger bar (drag
    /// its cyan marker; Y steps it 0, 1, 2, 3 s, then off) you vanish and come out Specials.BlinkFt farther along your
    /// planned path. While planning, the ghost path breaks over the hop, with a dashed line across it and a spark at each
    /// end; in playback the plane is gone in a flash and back in another, with a fading streak between.
    /// </summary>
    public partial class TurnBattle
    {
        public Color blinkColor = new Color(0.55f, 0.95f, 1f, 1f);

        // ---------- planning ----------

        void ToggleBlink()
        {
            if (!plan.blink && battle.blinks[0] <= 0) { Notice("No blinks left."); return; }
            plan.blink = !plan.blink;
            if (plan.blink) BlinkNotice();
            dirty = true;
        }

        /// <summary>The Y key: off, then at 0, 1, 2 and 3 s into the turn, then off again.</summary>
        void StepBlink()
        {
            if (!plan.blink) { if (battle.blinks[0] <= 0) { Notice("No blinks left."); return; } plan.blink = true; plan.blinkAt = 0; }
            else if (plan.blinkAt >= turnSec - 0.01) { plan.blink = false; plan.blinkAt = 0; }
            else plan.blinkAt = Math.Min(turnSec, Math.Floor(plan.blinkAt + 1.0001));
            if (plan.blink) BlinkNotice();
            dirty = true;
        }

        void BlinkNotice() => Notice($"Blink: {plan.blinkAt:0.0} s in you jump {Specials.BlinkFt:0} ft ahead along your path (drag the cyan marker on the trigger bar, or Y steps it). " +
                                     "Rounds in the air miss and a missile on you loses you; time it for when they'd hit.");

        Rect BlinkMarker => new Rect(burstRect.x + (float)(Math.Min(plan.blinkAt, turnSec) / turnSec) * burstRect.width - 8, burstRect.y - 9, 16, burstRect.height + 12);

        bool PressBlink(Vector2 gui) => plan.blink && burstRect.width > 0 && BlinkMarker.Contains(gui);

        void SetBlinkFromBar(float guiX)
        {
            float t = Mathf.Clamp01((guiX - burstRect.x) / burstRect.width) * turnSec;
            plan.blinkAt = Mathf.Round(t / 0.05f) * 0.05f;
            dirty = true;
        }

        /// <summary>The blink on the trigger bar: a cyan stalk with a diamond at its moment, and its time on the left of the label line.</summary>
        void BlinkOnBar(Rect bar, GUIStyle lbl)
        {
            if (!plan.blink) return;
            float x = bar.x + (float)(Math.Min(plan.blinkAt, turnSec) / turnSec) * bar.width;
            var c = blinkColor;
            Fill(new Rect(x - 1, bar.y - 2, 2, bar.height + 4), c);
            for (int k = 0; k < 4; k++) Fill(new Rect(x - k - 1, bar.y - 9 + k, 2 * k + 2, 1), c);
            for (int k = 0; k < 4; k++) Fill(new Rect(x - k - 1, bar.y - 2 - k, 2 * k + 2, 1), c);
            var st = new GUIStyle(lbl) { alignment = TextAnchor.MiddleLeft };
            st.normal.textColor = c;
            GUI.Label(new Rect(bar.x, bar.y - 14, bar.width, 12), $"BLINK {plan.blinkAt:0.00} S", st);
        }

        // ---------- drawing ----------

        /// <summary>The first frame up to upTo that a blink landed on (the hop is from the frame before), or -1.</summary>
        static int BlinkFrame(AircraftFrame[] frames, int upTo)
        {
            if (frames == null) return -1;
            for (int i = 1; i <= upTo && i < frames.Length; i++) if (frames[i].blink) return i;
            return -1;
        }

        /// <summary>A planned hop on a path: a dashed line over it and a spark where it leaves and where it comes out.</summary>
        void BlinkHops(AircraftFrame[] frames, int upTo, bool mine)
        {
            int b = BlinkFrame(frames, upTo);
            if (b <= 0) return;
            Vector3 a = UnityFrame.Pos(frames[b - 1].pos), z = UnityFrame.Pos(frames[b].pos);
            var c = blinkColor; c.a = mine ? 0.9f : 0.55f;
            Dashes(a, z, 14, 0.7f, c);
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 6f);
            Spark(a, 0.7f * pulse, c, 0f);
            Spark(z, 1f * pulse, c, 0.4f);
        }

        /// <summary>The blinks in a turn being played back, at frame now: a burst where each left, one where it came out, and a fading streak between.</summary>
        void DrawBlinks(IList<TurnAircraftView> vs, TurnResult r, int now)
        {
            if (r == null || r.frames == null) return;
            const int fadeFrames = 35;
            for (int k = 0; k < r.frames.Length; k++)
            {
                var fr = r.frames[k];
                if (fr == null) continue;
                int b = BlinkFrame(fr, Mathf.Min(now, fr.Length - 1));
                if (b <= 0 || now - b > fadeFrames) continue;
                if (k < vs.Count && vs[k] != null && !vs[k].gameObject.activeInHierarchy) continue;
                bool enemy = !replaying && k < battle.Count && battle.side[k] != battle.side[0];
                if (enemy && fr[b].cloaked) continue;   // a stealthy enemy's blink is as unseen as it is
                float u = (now - b) / (float)fadeFrames;
                Vector3 a = UnityFrame.Pos(fr[b - 1].pos), z = UnityFrame.Pos(fr[b].pos);
                var c = blinkColor; c.a = 1f - u;
                Dashes(a, z, 18, 0.9f, new Color(c.r, c.g, c.b, c.a * 0.6f));
                Spark(a, 0.8f + 2.2f * u, c, u * 2f);                       // the burst it leaves, opening out
                Spark(z, 1.6f - 1.2f * Mathf.Min(1f, u * 2f), c, -u * 2f);   // the flash it comes out of, closing in
            }
        }

        readonly List<Vector3> blinkPts = new List<Vector3>(24);

        /// <summary>A spark at p: a ring and eight rays, size times a few pixels at that distance, turned by spin.</summary>
        void Spark(Vector3 p, float size, Color c, float spin)
        {
            Vector3 right = view != null ? view.Cam.transform.right : Vector3.right, up = view != null ? view.Cam.transform.up : Vector3.up;
            float r = Mathf.Max(3f, W(p) * 14f) * size;
            blinkPts.Clear();
            for (int k = 0; k < 18; k++) { float t = k * Mathf.PI / 9; blinkPts.Add(p + (right * Mathf.Cos(t) + up * Mathf.Sin(t)) * r); }
            lines.Strip(blinkPts, 0.9f, c, true);
            for (int k = 0; k < 8; k++)
            {
                float t = spin + k * Mathf.PI / 4, len = k % 2 == 0 ? 1.9f : 1.35f;
                Vector3 d = right * Mathf.Cos(t) + up * Mathf.Sin(t);
                lines.Line(p + d * r * 1.15f, p + d * r * len, 0.8f, c);
            }
            lines.Line(p - up * r * 0.25f, p + up * r * 0.25f, 3f, new Color(1f, 1f, 1f, c.a));   // a white-hot heart
        }

        void Dashes(Vector3 a, Vector3 b, int count, float width, Color c)
        {
            for (int k = 0; k < count; k++)
            {
                float f0 = k / (float)count, f1 = (k + 0.55f) / count;
                lines.Line(Vector3.Lerp(a, b, f0), Vector3.Lerp(a, b, f1), width, c);
            }
        }
    }
}
