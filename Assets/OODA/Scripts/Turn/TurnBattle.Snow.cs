using System;
using System.Collections.Generic;
using Ooda.Flight;
using Ooda.Flight.Core;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// ; (semicolon), Snow cloud (the rules are SnowCloud and Specials in Turn/Core/Specials.cs): let go round you at the
    /// moment you pick on the MFD's trigger bar (drag its ice-blue marker; ; steps it 0, 1, 2, 3 s, then off). It billows out
    /// round you and hangs for four turns. Everyone who flies through it comes out rimed over (the Ice pick's frost and, when
    /// it is thick, its icicles): slower, heavier on the stick and with less climb until it melts, but cold to a
    /// heat-seeker. You fly out through the back of yours lightly rimed; a tailer flies through the whole of it. While
    /// planning, the clouds hang as white puffs with their edge ring, and the one you're about to let go shows where it
    /// will hang; a pale blue line over the switches says how much rime you carry and how long it lasts.
    /// </summary>
    public partial class TurnBattle
    {
        public Color snowColor = new Color(0.72f, 0.9f, 1f, 1f);

        SnowPuffs snowPuffs;
        SnowPuffs Puffs => snowPuffs != null ? snowPuffs : (snowPuffs = SnowPuffs.On(gameObject));

        // ---------- planning ----------

        void ToggleSnow()
        {
            if (!plan.snow && battle.snowClouds[0] <= 0) { Notice("No snow clouds left."); return; }
            plan.snow = !plan.snow;
            if (plan.snow) SnowNotice();
            dirty = true;
        }

        /// <summary>The ; key: off, then let go at 0, 1, 2 and 3 s into the turn, then off again.</summary>
        void StepSnow()
        {
            if (!plan.snow) { if (battle.snowClouds[0] <= 0) { Notice("No snow clouds left."); return; } plan.snow = true; plan.snowAt = 0; }
            else if (plan.snowAt >= turnSec - 0.01) { plan.snow = false; plan.snowAt = 0; }
            else plan.snowAt = Math.Min(turnSec, Math.Floor(plan.snowAt + 1.0001));
            if (plan.snow) SnowNotice();
            dirty = true;
        }

        void SnowNotice() => Notice($"Snow cloud: let go round you {plan.snowAt:0.0} s in (drag the blue marker on the trigger bar, or ; steps it). " +
                                    "Anyone flying through comes out rimed: slower and heavier until it melts, but heat-seekers can't lock them. You too.");

        Rect SnowMarker => new Rect(burstRect.x + (float)(Math.Min(plan.snowAt, turnSec) / turnSec) * burstRect.width - 8, burstRect.y - 9, 16, burstRect.height + 12);

        bool PressSnow(Vector2 gui) => plan.snow && burstRect.width > 0 && SnowMarker.Contains(gui);

        void SetSnowFromBar(float guiX)
        {
            float t = Mathf.Clamp01((guiX - burstRect.x) / burstRect.width) * turnSec;
            plan.snowAt = Mathf.Round(t / 0.05f) * 0.05f;
            dirty = true;
        }

        /// <summary>The snow cloud on the trigger bar: a blue flake on a stalk at its moment, and its time on the label line.</summary>
        void SnowOnBar(Rect bar, GUIStyle lbl)
        {
            if (!plan.snow) return;
            float x = bar.x + (float)(Math.Min(plan.snowAt, turnSec) / turnSec) * bar.width;
            var c = snowColor;
            Fill(new Rect(x - 1, bar.y - 2, 2, bar.height + 4), c);
            Fill(new Rect(x - 5, bar.y - 6, 10, 2), c);   // a flake: a cross with a dot in the middle
            Fill(new Rect(x - 1, bar.y - 10, 2, 10), c);
            Fill(new Rect(x - 2, bar.y - 7, 4, 4), Color.white);
            var st = new GUIStyle(lbl) { alignment = TextAnchor.MiddleRight };
            st.normal.textColor = c;
            GUI.Label(new Rect(bar.x, bar.y - 14, bar.width, 12), $"SNOW {plan.snowAt:0.00} S", st);
        }

        /// <summary>
        /// Over the switches, while you carry rime: how much, what it does and how long it lasts; with a cloud planned, how
        /// much the preview says you come out with.
        /// </summary>
        void RimeWarning(float x, float y, float w)
        {
            if (battle == null || battle.Count == 0) return;
            double rime = battle.planes[0].systems.rime;
            double planned = plan.snow && preview != null && preview.frames != null && preview.frames.Length > 0 && preview.frames[0] != null
                ? preview.frames[0][preview.frames[0].Length - 1].rime : 0;
            if (rime <= 0.005 && planned <= 0.005) return;
            var st = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            st.normal.textColor = snowColor;
            string text;
            if (rime > 0.005)
            {
                double coldFor = Math.Max(0, (rime - Systems.RimeColdAt) * Systems.RimeMeltSec);
                text = rime >= Systems.RimeColdAt
                    ? $"RIME ICE {rime * 100:0}%: heat-seekers can't lock you for {coldFor:0} s more. Less lift, speed and climb till it melts."
                    : $"RIME ICE {rime * 100:0}%: melting. Heat-seekers can lock you again.";
            }
            else text = $"SNOW: you come out of it rimed {planned * 100:0}%, cold to heat-seekers for about {Math.Max(0, (planned - Systems.RimeColdAt) * Systems.RimeMeltSec):0} s.";
            Shadow(new Rect(x, y, w, 22), text, st);
        }

        // ---------- drawing ----------

        /// <summary>While planning: the clouds hanging now, and where the one you plan to let go will hang at the turn's end.</summary>
        void DrawLiveSnow()
        {
            foreach (var b in battle.bullets)
                if (b is SnowCloud sc) SnowCloudAt(UnityFrame.Pos(sc.pos), (float)sc.RadiusFt, (float)sc.Thickness, true, sc.side == battle.side[0], sc.owner * 31 + (int)sc.fireTime);
            if (preview == null || preview.snow == null) return;
            foreach (var tr in preview.snow)
                if (tr.owner == 0 && tr.startFrame >= 1 && tr.points.Count > 0)
                {
                    var end = UnityFrame.Pos(tr.points[tr.points.Count - 1]);
                    double age = tr.age0 + (tr.points.Count - 1) * preview.dt;
                    SnowRing(end, (float)SnowCloud.RadiusAt(age), 0.55f, true);
                    var drop = UnityFrame.Pos(tr.points[0]);
                    lines.Line(drop, end, 0.4f, new Color(snowColor.r, snowColor.g, snowColor.b, 0.35f));
                }
        }

        /// <summary>The snow clouds at frame now of a turn being played back (and in the replay).</summary>
        void DrawSnow(TurnResult r, int now)
        {
            if (r == null || r.snow == null) return;
            foreach (var tr in r.snow)
            {
                var head = tr.At(now);
                if (head == null) continue;
                double age = tr.age0 + (now - tr.startFrame) * r.dt;
                bool ours = tr.owner >= 0 && tr.owner < battle.Count && battle.side[tr.owner] == battle.side[0];
                SnowCloudAt(UnityFrame.Pos(head.Value), (float)SnowCloud.RadiusAt(age), (float)SnowCloud.ThicknessAt(age, Specials.SnowLifeSec), false, ours, tr.owner * 31 + tr.startFrame);
            }
        }

        /// <summary>A snow cloud at p, radiusFt across, thick 0..1: white puffs, snow falling through it, and with ring its edge.</summary>
        void SnowCloudAt(Vector3 p, float radiusFt, float thick, bool ring, bool ours, int seed)
        {
            float r = radiusFt * (float)Atmosphere.MPerFt;
            Puffs.Draw(p, r, thick, seed);
            // the snow: flakes drifting down through it, each a little cross
            var flake = new Color(1f, 1f, 1f, 0.85f * thick);
            for (int k = 0; k < 36; k++)
            {
                uint h = SnowHash((uint)seed, (uint)k);
                float a = (h % 628) / 100f, u = ((h >> 10) % 1000) / 1000f, ph = ((h >> 20) % 1000) / 1000f;
                float rr = r * 0.9f * Mathf.Sqrt(u);
                float fall = Mathf.Repeat(Time.time * 0.12f + ph, 1f);   // top to bottom, over and over
                var at = p + new Vector3(Mathf.Cos(a) * rr, r * 0.5f * (1f - 2f * fall), Mathf.Sin(a) * rr)
                           + new Vector3(Mathf.Sin(Time.time * 0.9f + k) * r * 0.03f, 0, 0);
                float s = Mathf.Max(0.6f, W(at) * 2.5f);
                lines.Line(at - Vector3.right * s, at + Vector3.right * s, 0.35f, flake);
                lines.Line(at - Vector3.up * s, at + Vector3.up * s, 0.35f, flake);
            }
            if (ring) SnowRing(p, radiusFt, (ours ? 0.3f : 0.5f) * Mathf.Max(0.3f, thick), false);
        }

        readonly List<Vector3> snowPts = new List<Vector3>(48);

        /// <summary>The cloud's edge, facing the camera: where the icing stops.</summary>
        void SnowRing(Vector3 p, float radiusFt, float alpha, bool dashed)
        {
            Vector3 right = view != null ? view.Cam.transform.right : Vector3.right, up = view != null ? view.Cam.transform.up : Vector3.up;
            float r = radiusFt * (float)Atmosphere.MPerFt;
            var c = new Color(snowColor.r, snowColor.g, snowColor.b, alpha);
            if (!dashed)
            {
                snowPts.Clear();
                for (int k = 0; k < 40; k++) { float a = k * Mathf.PI / 20; snowPts.Add(p + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * r); }
                lines.Strip(snowPts, 0.4f, c, true);
                return;
            }
            for (int k = 0; k < 40; k += 2)
            {
                float a0 = k * Mathf.PI / 20, a1 = (k + 1) * Mathf.PI / 20;
                lines.Line(p + (right * Mathf.Cos(a0) + up * Mathf.Sin(a0)) * r, p + (right * Mathf.Cos(a1) + up * Mathf.Sin(a1)) * r, 0.5f, c);
            }
        }

        static uint SnowHash(uint a, uint b)
        {
            uint h = a * 374761393u + b * 668265263u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }
    }
}
