using System;
using System.Collections.Generic;
using Ooda.Flight;
using Ooda.Flight.Core;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// /, Ricochet shield (the rules are Specials.Bounce and Battle.Ricochet in Turn/Core): raised for the turn you plan, a
    /// chrome bubble round the plane that every round bounces off, back the way it came, splayed a few degrees. While
    /// planning, your ghost wears the bubble and the enemies' projected rounds kink off it; in playback each bounce sparks
    /// and the round flies on in the shield's silver, the shield's now (it can hit the shooter).
    /// </summary>
    public partial class TurnBattle
    {
        public Color mirrorColor = new Color(0.78f, 0.96f, 1f, 1f);
        /// <summary>A round after it bounced off a shield, in playback and the preview.</summary>
        public Color ricochetTracer = new Color(0.85f, 1f, 1f, 1f);

        // ---------- planning ----------

        void ToggleMirror()
        {
            if (!plan.mirror && battle.mirrors[0] <= 0) { Notice("No ricochet shield charges left."); return; }
            plan.mirror = !plan.mirror;
            if (plan.mirror) Notice($"Ricochet shield: up all this turn. Rounds bounce off you, back the way they came, and can hit whoever fired them. Missiles still get through. ({battle.mirrors[0]} left)");
            dirty = true;
        }

        // ---------- drawing ----------

        /// <summary>While planning: the bubble on your ghost if you raise it, and on anyone whose shield is up now.</summary>
        void MirrorPlanning()
        {
            for (int k = 0; k < views.Length && k < battle.Count; k++)
                if (views[k] != null && battle.Mirrored(k) && !HiddenFromMe(k)) Bubble(views[k].transform, BubbleRadius(views[k]), k, 1f);
            if (plan.mirror && holoMine != null && holoMine.activeSelf) Bubble(holoMine.transform, views.Length > 0 && views[0] != null ? BubbleRadius(views[0]) : 7f, 0, 0.7f);
        }

        /// <summary>The bubble on every plane whose shield is up at frame now of a turn being played back, and the sparks of the bounces since the last frame shown.</summary>
        void DrawMirrorFrames(IList<TurnAircraftView> vs, TurnResult r, int now)
        {
            for (int k = 0; k < vs.Count && k < r.frames.Length; k++)
            {
                if (vs[k] == null || r.frames[k] == null) continue;
                var f = r.frames[k][Mathf.Min(now, r.frames[k].Length - 1)];
                if (!f.mirror || f.down) continue;
                if (f.cloaked && !replaying && k < battle.Count && battle.side[k] != battle.side[0]) continue;   // a stealthy enemy's shield is unseen too
                Bubble(vs[k].transform, BubbleRadius(vs[k]), k, 1f);
            }
        }

        /// <summary>The sparks of the bounces between battle times a and b (since the last frame shown, up to now).</summary>
        void RicochetSparks(TurnResult r, double a, double b)
        {
            if (r == null || r.ricochets == null) return;
            var fx = Ooda.Fx.ToonFx.Get();
            foreach (var h in r.ricochets)
                if (h.time > a && h.time <= b) fx.Ricochet(UnityFrame.Pos(h.pos), UnityFrame.Dir(h.local));
        }

        /// <summary>How big the bubble is round a plane: a little past its wingtips.</summary>
        static float BubbleRadius(TurnAircraftView v)
        {
            var b = v.LocalBounds();
            float s = v.transform.lossyScale.x;
            return Mathf.Max(4f, Mathf.Max(b.extents.x, b.extents.z) * 1.12f * s);
        }

        readonly List<Vector3> bubblePts = new List<Vector3>(40);

        /// <summary>
        /// A chrome bubble round a plane: a bright rim facing the camera, three great circles turning slowly inside it, and
        /// a glint that runs round the rim.
        /// </summary>
        void Bubble(Transform t, float radius, int k, float alpha)
        {
            Vector3 c = t.position;
            Vector3 toCam = view != null ? (view.Cam.transform.position - c).normalized : Vector3.back;
            Vector3 right = Vector3.Cross(Vector3.up, toCam);
            if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
            right.Normalize();
            Vector3 up = Vector3.Cross(toCam, right);
            float shimmer = 0.85f + 0.15f * Mathf.Sin(Time.time * 7f + k * 1.9f);
            // the rim, as the bubble's outline from where you look
            Ring(c, right, up, radius, 0.9f * shimmer, new Color(mirrorColor.r, mirrorColor.g, mirrorColor.b, 0.85f * alpha));
            // three great circles, tilted and turning, so it reads as a sphere and not a hoop
            float spin = Time.time * 40f + k * 37f;
            for (int i = 0; i < 3; i++)
            {
                var q = Quaternion.AngleAxis(spin + i * 60f, Vector3.up) * Quaternion.AngleAxis(35f + i * 25f, Vector3.right);
                Ring(c, q * Vector3.right, q * Vector3.forward, radius * 0.995f, 0.35f, new Color(mirrorColor.r, mirrorColor.g, mirrorColor.b, 0.3f * alpha));
            }
            // the glint
            float a = Time.time * 2.3f + k;
            Vector3 g = c + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * radius;
            Vector3 along = (-right * Mathf.Sin(a) + up * Mathf.Cos(a)) * radius * 0.35f;
            lines.Line(g - along, g + along, 1.6f, new Color(1f, 1f, 1f, 0.9f * alpha));
        }

        void Ring(Vector3 c, Vector3 u, Vector3 v, float radius, float width, Color col)
        {
            bubblePts.Clear();
            const int N = 32;
            for (int i = 0; i < N; i++) { float a = i * Mathf.PI * 2f / N; bubblePts.Add(c + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radius); }
            lines.Strip(bubblePts, width, col, true);
        }

        /// <summary>A preview round that bounced off a shield: its path to the bubble in col, and on from there in the ricochet silver.</summary>
        bool DrawBouncedTrace(Trace tr, Color col, float width, float tailAlpha)
        {
            if (tr.bounceAt < 0 || tr.bounceAt >= tr.points.Count) return false;
            var a = new List<Vector3>();
            for (int i = 0; i < tr.bounceAt; i += 2) a.Add(UnityFrame.Pos(tr.points[i]));
            a.Add(UnityFrame.Pos(tr.points[tr.bounceAt]));
            if (a.Count >= 2) lines.Strip(a, width, col);
            var b = new List<Vector3>();
            for (int i = tr.bounceAt; i < tr.points.Count; i += 2) b.Add(UnityFrame.Pos(tr.points[i]));
            b.Add(UnityFrame.Pos(tr.points[tr.points.Count - 1]));
            var rc = tr.hit ? hitTracer : ricochetTracer;
            if (b.Count >= 2) lines.Strip(b, width, new Color(rc.r, rc.g, rc.b, Mathf.Max(col.a, 0.7f)), false, new Color(rc.r, rc.g, rc.b, Mathf.Max(tailAlpha, 0.15f)));
            return true;
        }

        /// <summary>A round's colour at frame now in playback: the shield's silver once it has bounced.</summary>
        Color AfterBounce(Trace tr, int now, Color col) => tr.bounceAt >= 0 && now - tr.startFrame >= tr.bounceAt ? ricochetTracer : col;
    }
}
