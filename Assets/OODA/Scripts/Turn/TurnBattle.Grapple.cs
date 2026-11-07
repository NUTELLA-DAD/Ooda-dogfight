using System;
using System.Collections.Generic;
using Ooda.Flight;
using Ooda.Flight.Core;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// 0, Grapple (the rules are Tether and Specials in Turn/Core/Specials.cs): from the moment you pick on the MFD's trigger
    /// bar (drag its gold hook marker; 0 steps it 0, 1, 2, 3 s, then off) a hook flies at the first enemy in reach ahead (the
    /// one you are after first) and holds him on a line. Fly round him and you swing tighter than your wing could turn you,
    /// and drag him with you; pull the other way and the line goes bar-taut and snaps in a violent yank that jolts you both.
    /// With a line out the same switch cuts it at its moment: let go at the end of a swing. While planning the ghost path
    /// already flies the line, the rope shows where it holds at the end of the turn, and the trigger bar's label says
    /// whether it hooks, holds or snaps.
    /// </summary>
    public partial class TurnBattle
    {
        public Color grappleColor = new Color(1f, 0.78f, 0.3f, 1f);
        static readonly Color RopeTaut = new Color(1f, 0.25f, 0.12f, 1f);

        // ---------- planning ----------

        bool LineOut => battle != null && battle.LineOf(0) != null;

        void ToggleGrapple()
        {
            if (!plan.grapple && !LineOut && battle.grapples[0] <= 0) { Notice("No grapple hooks left."); return; }
            plan.grapple = !plan.grapple;
            if (plan.grapple) GrappleNotice();
            dirty = true;
        }

        /// <summary>The 0 key: off, then at 0, 1, 2 and 3 s into the turn, then off again.</summary>
        void StepGrapple()
        {
            if (!plan.grapple)
            {
                if (!LineOut && battle.grapples[0] <= 0) { Notice("No grapple hooks left."); return; }
                plan.grapple = true; plan.grappleAt = 0;
            }
            else if (plan.grappleAt >= turnSec - 0.01) { plan.grapple = false; plan.grappleAt = 0; }
            else plan.grappleAt = Math.Min(turnSec, Math.Floor(plan.grappleAt + 1.0001));
            if (plan.grapple) GrappleNotice();
            dirty = true;
        }

        void GrappleNotice() => Notice(LineOut
            ? $"Cut the line {plan.grappleAt:0.0} s in (drag the gold marker on the trigger bar, or 0 steps it): fly on free from the end of the swing."
            : $"Grapple: from {plan.grappleAt:0.0} s in, hook the first enemy within {Specials.GrappleReachFt:N0} ft ahead (drag the gold marker, or 0 steps it). " +
              "Swing round him to turn tighter and drag him; pull the other way and the line snaps in a violent yank.");

        Rect GrappleMarker => new Rect(burstRect.x + (float)(Math.Min(plan.grappleAt, turnSec) / turnSec) * burstRect.width - 8, burstRect.y - 9, 16, burstRect.height + 12);

        bool PressGrapple(Vector2 gui) => plan.grapple && burstRect.width > 0 && GrappleMarker.Contains(gui);

        void SetGrappleFromBar(float guiX)
        {
            float t = Mathf.Clamp01((guiX - burstRect.x) / burstRect.width) * turnSec;
            plan.grappleAt = Mathf.Round(t / 0.05f) * 0.05f;
            dirty = true;
        }

        /// <summary>The preview's line of yours (hooked this turn or already out), or null.</summary>
        TetherTrace PreviewLine()
        {
            if (preview == null || preview.tethers == null) return null;
            TetherTrace pick = null;
            foreach (var tr in preview.tethers) if (tr.owner == 0 && (pick == null || tr.startFrame > pick.startFrame)) pick = tr;
            return pick;
        }

        /// <summary>What the planned grapple does this turn, by the preview: hooks, holds, is cut or snaps.</summary>
        string GrappleOutcome()
        {
            var tr = PreviewLine();
            double dt = preview != null ? preview.dt : Battle.StepSec;
            if (tr == null) return plan.grapple && !LineOut ? "NO ONE IN REACH" : "";
            double peak = 0;
            foreach (var g in tr.tensionG) peak = Math.Max(peak, g);
            if (tr.snapFrame >= 0) return $"SNAPS {tr.snapFrame * dt:0.0} S  YANK";
            if (tr.cut) return $"CUT {tr.EndFrame * dt:0.0} S";
            string who = tr.hookedNow ? $"HOOK {tr.startFrame * dt:0.0} S" : "LINE";
            return $"{who}  {peak:0} G";
        }

        /// <summary>The grapple on the trigger bar: a gold hook on a stalk at its moment, and its time and outcome on the label line.</summary>
        void GrappleOnBar(Rect bar, GUIStyle lbl)
        {
            if (!plan.grapple) return;
            float x = bar.x + (float)(Math.Min(plan.grappleAt, turnSec) / turnSec) * bar.width;
            var c = grappleColor;
            Fill(new Rect(x - 1, bar.y - 2, 2, bar.height + 4), c);
            // a little hook: a stalk with a curl at its foot
            Fill(new Rect(x - 1, bar.y - 10, 2, 8), c);
            Fill(new Rect(x - 4, bar.y - 4, 6, 2), c);
            Fill(new Rect(x - 4, bar.y - 7, 2, 4), c);
            var st = new GUIStyle(lbl) { alignment = plan.mine ? TextAnchor.MiddleCenter : TextAnchor.MiddleRight };   // the mine has the right, the well the middle
            st.normal.textColor = c;
            string what = LineOut ? $"CUT {plan.grappleAt:0.00} S" : $"HOOK {plan.grappleAt:0.00} S";
            string outcome = GrappleOutcome();
            GUI.Label(new Rect(bar.x, bar.y - 14, bar.width, 12), outcome.Length > 0 && !LineOut ? $"{what} · {outcome}" : what, st);
        }

        // ---------- drawing ----------

        readonly List<Vector3> ropePts = new List<Vector3>(24);
        readonly Dictionary<TetherTrace, float> snapShown = new Dictionary<TetherTrace, float>();

        Vector3 PlanePos(int i) => i >= 0 && i < views.Length && views[i] != null ? views[i].transform.position : UnityFrame.Pos(battle.planes[i].state.pos);

        /// <summary>While planning: the lines out now, and where the planned one (or yours) is at the end of the ghost path.</summary>
        void DrawLiveTethers()
        {
            foreach (var b in battle.bullets)
                if (b is Tether tl && tl.owner >= 0 && tl.owner < battle.Count && tl.target >= 0 && tl.target < battle.Count)
                    Rope(PlanePos(tl.owner), PlanePos(tl.target), (float)tl.tensionG, 1f, tl.owner);
            var tr = PreviewLine();
            if (tr == null || preview.frames == null || tr.target < 0 || tr.target >= preview.frames.Length) return;
            int end = tr.EndFrame;
            var f0 = preview.frames[0]; var f1 = preview.frames[tr.target];
            if (f0 == null || f1 == null || end >= f0.Length || end >= f1.Length) return;
            // faint ropes along the ghost path, every half second the line holds, and a brighter one at its last moment
            int step = Mathf.Max(1, Mathf.RoundToInt(0.5f / (float)preview.dt));
            for (int k = tr.startFrame; k < end; k += step)
                Rope(UnityFrame.Pos(f0[k].pos), UnityFrame.Pos(f1[k].pos), tr.TensionAt(k), 0.25f, 0);
            Rope(UnityFrame.Pos(f0[end].pos), UnityFrame.Pos(f1[end].pos), tr.TensionAt(end), 0.6f, 0);
            if (tr.snapFrame >= 0) SnapBurst(UnityFrame.Pos(f0[end].pos), UnityFrame.Pos(f1[end].pos), 0.25f, 0.7f);
        }

        /// <summary>The lines at frame now of a turn being played back; a snap whips both ends back and jolts the camera.</summary>
        void DrawTethers(IList<TurnAircraftView> vs, TurnResult r, int now)
        {
            if (r == null || r.tethers == null) return;
            Vector3 At(int i) => i >= 0 && i < vs.Count && vs[i] != null ? vs[i].transform.position
                               : i >= 0 && i < r.frames.Length && r.frames[i] != null ? UnityFrame.Pos(r.frames[i][Mathf.Min(now, r.frames[i].Length - 1)].pos) : Vector3.zero;
            foreach (var tr in r.tethers)
            {
                if (tr.owner < 0 || tr.target < 0 || tr.owner >= r.frames.Length || tr.target >= r.frames.Length) continue;
                if (now >= tr.startFrame && now <= tr.EndFrame)
                {
                    float hookIn = tr.hookedNow ? Mathf.Clamp01((now - tr.startFrame) * (float)r.dt / 0.15f) : 1f;   // the hook flies out
                    Vector3 a = At(tr.owner), b = At(tr.target);
                    Rope(a, Vector3.Lerp(a, b, hookIn), tr.TensionAt(now), 1f, tr.owner);
                }
                if (tr.snapFrame < 0 || now < tr.snapFrame) continue;
                float since = (now - tr.snapFrame) * (float)r.dt;
                if (since > 0.7f) continue;
                SnapBurst(At(tr.owner), At(tr.target), since / 0.7f, 1f);
                if (!snapShown.TryGetValue(tr, out float shownAt) || Time.unscaledTime - shownAt > 1.5f)
                {
                    snapShown[tr] = Time.unscaledTime;
                    Flash((At(tr.owner) + At(tr.target)) * 0.5f);
                    bool mine = tr.owner == 0 || tr.target == 0;
                    if (view != null) view.Jolt(mine ? 1f : 0.4f);
                    if (mine) Notice($"The line snapped: a violent yank ({Math.Max(tr.jolt0, tr.jolt1) * 0.5925:0} kt jolt).");
                }
            }
            // the player's own tug shakes the camera a little while the line hauls on him
            if (vs.Count > 0 && r.frames.Length > 0 && r.frames[0] != null)
            {
                double tug = r.frames[0][Mathf.Min(now, r.frames[0].Length - 1)].tugG;
                if (tug > 3 && view != null) view.Jolt(Mathf.Clamp01((float)(tug - 3) / 12f) * 0.15f);
            }
        }

        /// <summary>
        /// A grapple's rope from a to b: slack it sags and is pale gold; taut it runs straight and reddens and thrums with the pull.
        /// </summary>
        void Rope(Vector3 a, Vector3 b, float tensionG, float alpha, int seed)
        {
            float len = Vector3.Distance(a, b);
            if (len < 0.5f) return;
            float taut = Mathf.Clamp01(tensionG / 8f);
            var c = Color.Lerp(grappleColor, RopeTaut, taut); c.a = Mathf.Lerp(0.75f, 1f, taut) * alpha;
            Vector3 dir = (b - a) / len;
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(dir, Vector3.right);
            side.Normalize();
            float sag = tensionG <= 0.01f ? len * 0.06f : 0f;
            float thrum = taut * Mathf.Min(1.5f, len * 0.004f) * Mathf.Sin(Time.time * 60f + seed);
            ropePts.Clear();
            const int N = 16;
            for (int k = 0; k <= N; k++)
            {
                float f = k / (float)N, bow = 4f * f * (1f - f);
                ropePts.Add(Vector3.Lerp(a, b, f) + Vector3.down * (sag * bow) + side * (thrum * bow));
            }
            lines.Strip(ropePts, Mathf.Lerp(0.35f, 0.6f, taut), c);
            // the hook at the far end
            float h = Mathf.Max(0.6f, W(b) * 3f);
            lines.Line(b - dir * h, b + side * h * 0.6f, 0.8f, c);
            lines.Line(b - dir * h, b - side * h * 0.6f, 0.8f, c);
        }

        /// <summary>The snap: the two ends whip back toward their planes, fading out over the burst (k from 0 to 1).</summary>
        void SnapBurst(Vector3 a, Vector3 b, float k, float alpha)
        {
            float len = Vector3.Distance(a, b);
            if (len < 0.5f) return;
            Vector3 dir = (b - a) / len;
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
            side.Normalize();
            float reach = len * 0.5f * (1f - k);
            var c = RopeTaut; c.a = (1f - k) * alpha;
            for (int end = 0; end < 2; end++)
            {
                Vector3 from = end == 0 ? a : b, along = end == 0 ? dir : -dir;
                ropePts.Clear();
                for (int s = 0; s <= 10; s++)
                {
                    float f = s / 10f;
                    ropePts.Add(from + along * (reach * f) + side * (Mathf.Sin(f * 9f + k * 20f) * reach * 0.12f * f));
                }
                lines.Strip(ropePts, 0.6f, c, false, new Color(c.r, c.g, c.b, 0f));
            }
            // a red star where it parted
            Vector3 mid = (a + b) * 0.5f;
            float r = Mathf.Max(1.5f, W(mid) * 10f) * (0.5f + k);
            for (int s = 0; s < 4; s++)
            {
                float ang = s * Mathf.PI / 4 + k;
                Vector3 d = side * Mathf.Cos(ang) + Vector3.up * Mathf.Sin(ang);
                lines.Line(mid - d * r, mid + d * r, 0.9f, new Color(1f, 0.85f, 0.4f, (1f - k) * alpha));
            }
        }
    }
}
