using System;
using System.Collections.Generic;
using Ooda.Flight;
using Ooda.Flight.Core;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// 0, Gravity flip (the rules are Flip and Specials in Turn/Core/Specials.cs): set off at the start of the turn, it opens a
    /// bubble of upside-down gravity round where you are, fixed in the sky for two turns. Everyone in it but you falls up:
    /// their planes, their wrecks, their rounds and missiles. While planning, the bubble you are about to open shows round
    /// you, the open ones show as green spheres full of chevrons rising, and the ghost paths and tracers show the arcs bent
    /// up (the preview flies the same rules). Inside someone else's, a FALLING UP warning shows.
    /// </summary>
    public partial class TurnBattle
    {
        public Color flipColor = new Color(0.3f, 1f, 0.68f, 1f);

        // ---------- planning ----------

        void ToggleFlip()
        {
            if (!plan.flip && battle.flips[0] <= 0) { Notice("No gravity flips left."); return; }
            plan.flip = !plan.flip;
            if (plan.flip) Notice($"Gravity flip: for two turns everyone within {Specials.FlipCoreFt:N0} ft of where you are now falls up, " +
                                  "their rounds and missiles too. You keep your weight. Watch their ghosts bend up.");
            dirty = true;
        }

        /// <summary>The plane you are flying is in someone else's gravity flip now: it falls up.</summary>
        bool FlippedNow => battle != null && battle.Flipped(0);

        /// <summary>Over the weapon switches: the warning that you are falling up.</summary>
        void FlipWarning(float x, float y, float w)
        {
            if (!FlippedNow) return;
            var st = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            st.normal.textColor = Mathf.Repeat(Time.unscaledTime, 0.8f) < 0.5f ? flipColor : Color.white;
            Shadow(new Rect(x, y - (JammedNow ? 46 : 24), w, 22), "GRAVITY FLIPPED: you fall up here. Push to hold your height, or fly out.", st);
        }

        // ---------- drawing ----------

        /// <summary>While planning: the flips open now, and the one you are about to set off.</summary>
        void DrawLiveFlips()
        {
            foreach (var b in battle.bullets)
                if (b is Flip f) Bubble(UnityFrame.Pos(f.pos), Mathf.Max(0.3f, (float)f.Strength), f.side == battle.side[0], f.GetHashCode() % 7);
            if (preview == null || preview.flips == null) return;
            foreach (var tr in preview.flips)
                if (tr.owner == 0 && tr.startFrame == 0 && tr.points.Count > 0 && plan.flip)
                    Bubble(UnityFrame.Pos(tr.points[0]), 0.5f, true, 0);
        }

        /// <summary>The flips at frame now of a turn being played back.</summary>
        void DrawFlips(TurnResult r, int now)
        {
            if (r == null || r.flips == null) return;
            foreach (var tr in r.flips)
            {
                var head = tr.At(now);
                if (head == null) continue;
                double age = tr.age0 + (now - tr.startFrame) * r.dt;
                float s = Mathf.Clamp01((float)Math.Min(age / Specials.FlipOpenSec, (Specials.FlipSec - age) / Specials.FlipFadeSec));
                Bubble(UnityFrame.Pos(head.Value), Mathf.Max(0.2f, s), tr.owner >= 0 && tr.owner < battle.Count && battle.side[tr.owner] == battle.side[0], tr.startFrame % 7);
            }
        }

        readonly List<Vector3> flipPts = new List<Vector3>(48);

        /// <summary>
        /// A gravity flip's bubble at p: its outline facing you, its equator and two rings of latitude (so it reads as a
        /// ball in the sky), the core's edge faint inside, and chevrons pointing up that rise through it over and over.
        /// strength 0..1 fades it; one of the other side's shows bolder.
        /// </summary>
        void Bubble(Vector3 p, float strength, bool ours, int seed)
        {
            Vector3 right = view != null ? view.Cam.transform.right : Vector3.right, camUp = view != null ? view.Cam.transform.up : Vector3.up;
            float reach = (float)(Specials.FlipReachFt * Atmosphere.MPerFt), core = (float)(Specials.FlipCoreFt * Atmosphere.MPerFt);
            var c = flipColor; c.a = (ours ? 0.55f : 0.8f) * strength;
            // the outline, square to the view
            flipPts.Clear();
            for (int k = 0; k < 48; k++) { float a = k * Mathf.PI / 24; flipPts.Add(p + (right * Mathf.Cos(a) + camUp * Mathf.Sin(a)) * reach); }
            lines.Strip(flipPts, 0.6f, c, true);
            // the equator and two rings of latitude, level in the world
            foreach (float lat in new[] { 0f, 0.55f, -0.55f })
            {
                float rr = reach * Mathf.Sqrt(1 - lat * lat), h = reach * lat;
                flipPts.Clear();
                for (int k = 0; k < 40; k++) { float a = k * Mathf.PI / 20; flipPts.Add(p + new Vector3(Mathf.Cos(a) * rr, h, Mathf.Sin(a) * rr)); }
                lines.Strip(flipPts, lat == 0 ? 0.45f : 0.3f, new Color(c.r, c.g, c.b, c.a * (lat == 0 ? 0.7f : 0.4f)), true);
            }
            // the core's edge, where it starts to fade
            flipPts.Clear();
            for (int k = 0; k < 40; k++) { float a = k * Mathf.PI / 20; flipPts.Add(p + (right * Mathf.Cos(a) + camUp * Mathf.Sin(a)) * core); }
            lines.Strip(flipPts, 0.25f, new Color(c.r, c.g, c.b, c.a * 0.3f), true);
            // chevrons rising: a ring of them round the middle and one up the middle, each climbing from the bottom to the top
            for (int k = 0; k < 7; k++)
            {
                float a = k * Mathf.PI * 2 / 6 + seed, rr = k == 6 ? 0 : core * 0.6f;
                Vector3 at = p + new Vector3(Mathf.Cos(a) * rr, 0, Mathf.Sin(a) * rr);
                float u = Mathf.Repeat(Time.time * 0.45f + k * 0.37f + seed * 0.11f, 1f);
                float h = Mathf.Lerp(-core * 0.8f, core * 0.8f, u), size = core * 0.13f;
                float fade = Mathf.Sin(u * Mathf.PI);
                var cc = new Color(c.r, c.g, c.b, Mathf.Min(1f, c.a * 1.3f) * fade);
                Vector3 tip = at + Vector3.up * h;
                for (int row = 0; row < 2; row++)
                {
                    Vector3 t = tip - Vector3.up * (row * size * 0.6f);
                    lines.Line(t - right * size - Vector3.up * size * 0.7f, t, 0.9f, cc);
                    lines.Line(t, t + right * size - Vector3.up * size * 0.7f, 0.9f, cc);
                }
            }
        }
    }
}
