using System;
using System.Collections.Generic;
using Ooda.Flight;
using Ooda.Flight.Core;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// 9, Portals (the rules are Portal and Specials in Turn/Core/Specials.cs; the rings on screen are PortalRings). The
    /// entry opens round you on your path at the moment you pick on the MFD's trigger bar (drag its green marker; 9 steps it
    /// 0, 1, 2, 3 s, then off), so you go through at that moment. The exit is yours to put: turned on, it starts on the
    /// six of the enemy you're after (N), and the next click in the view puts it there instead, at your height (the wheel
    /// raises or lowers it while you place it); drag from the click to point it. Shift+9 or the EXIT switch places it again.
    /// The ghost path shows you going through and where you come out. Anyone can follow you in for two turns, rounds and
    /// missiles too, both ways.
    /// </summary>
    public partial class TurnBattle
    {
        public Color portalEntryColor = new Color(0.36f, 1f, 0.48f, 1f), portalExitColor = new Color(1f, 0.31f, 0.85f, 1f);

        PortalRings rings;
        /// <summary>The next click in the view places the portal's exit (and a drag from it points it).</summary>
        bool placingExit, dragExit, dragPortalBar;
        Vector3 exitPressAt;
        /// <summary>The placing plane's height over yours, m (the wheel moves it while placing).</summary>
        float exitRiseM;
        /// <summary>Portals have been seen this session: paths are broken at jumps (LinePool.breakM).</summary>
        bool portalsSeen;

        static float PortalRadiusM => (float)(Specials.PortalRadiusFt * Atmosphere.MPerFt);

        PortalRings Rings
        {
            get
            {
                if (rings == null && view != null) { rings = PortalRings.Make(transform, view.Cam); rings.entryColor = portalEntryColor; rings.exitColor = portalExitColor; }
                return rings;
            }
        }

        /// <summary>With the lines' Begin: the frame's portal list starts over, and paths break at jumps once portals are about.</summary>
        void PortalsBegin()
        {
            Rings?.Begin();
            if (!portalsSeen && battle != null)
                foreach (var b in battle.bullets) if (b is Portal) { portalsSeen = true; break; }
            if (!portalsSeen && (last != null && last.portals.Count > 0 || preview != null && preview.portals.Count > 0)) portalsSeen = true;
            if (lines != null) lines.breakM = portalsSeen ? 450f : 0f;
        }

        // ---------- planning ----------

        void TogglePortal()
        {
            if (!plan.portal && battle.portals[0] <= 0) { Notice("No portals left."); return; }
            plan.portal = !plan.portal;
            if (plan.portal) PortalOn();
            else placingExit = false;
            dirty = true;
        }

        /// <summary>The 9 key: off, then opened at 0, 1, 2 and 3 s into the turn, then off again. Shift+9 places the exit again.</summary>
        void StepPortal(bool shift)
        {
            if (shift) { if (plan.portal) { placingExit = true; PortalNotice(); } return; }
            if (!plan.portal) { if (battle.portals[0] <= 0) { Notice("No portals left."); return; } plan.portal = true; plan.portalAt = 0; PortalOn(); }
            else if (plan.portalAt >= turnSec - 0.01) { plan.portal = false; plan.portalAt = 0; placingExit = false; }
            else { plan.portalAt = Math.Min(turnSec, Math.Floor(plan.portalAt + 1.0001)); PortalNotice(); }
            dirty = true;
        }

        /// <summary>Just turned on: the exit starts on the six of the enemy you're after, and the next click places it.</summary>
        void PortalOn()
        {
            plan.portalEntry = plan.portalEntryFace = Vec3d.Zero;   // the entry on your path
            if (plan.portalExitFace.Length < 0.5) DefaultExit();
            placingExit = true;
            exitRiseM = 0;
            PortalNotice();
        }

        void DefaultExit()
        {
            var me = battle.planes[0];
            int j = focus >= 0 && focus < battle.Count && battle.side[focus] != battle.side[0] && !battle.Out(focus) && !HiddenFromMe(focus) ? focus : -1;
            if (j >= 0)
            {
                var them = battle.planes[j];
                plan.portalExit = them.state.pos - them.Forward * EnemyAi.PortalSixFt;
                plan.portalExitFace = them.Forward;
            }
            else
            {
                plan.portalExit = me.state.pos + me.Right * 6000;
                plan.portalExitFace = me.Forward;
            }
        }

        void PortalNotice() => Notice($"Portal: you go through {plan.portalAt:0.0} s in (drag the green marker on the trigger bar, or 9 steps it). " +
                                      (placingExit ? "Click in the view to put the exit at your height, drag to point it; the wheel raises or lowers it. " : "Shift+9 places the exit again. ") +
                                      "It stays open two turns, both ways: anyone can follow you in, rounds and missiles too.");

        Rect PortalMarker => new Rect(burstRect.x + (float)(Math.Min(plan.portalAt, turnSec) / turnSec) * burstRect.width - 8, burstRect.y - 9, 16, burstRect.height + 12);

        bool PressPortal(Vector2 gui) => plan.portal && burstRect.width > 0 && PortalMarker.Contains(gui);

        void SetPortalFromBar(float guiX)
        {
            float t = Mathf.Clamp01((guiX - burstRect.x) / burstRect.width) * turnSec;
            plan.portalAt = Mathf.Round(t / 0.05f) * 0.05f;
            dirty = true;
        }

        /// <summary>The portal on the trigger bar: a green ring on a stalk at its moment, and its time on the label line.</summary>
        void PortalOnBar(Rect bar, GUIStyle lbl)
        {
            if (!plan.portal) return;
            float x = bar.x + (float)(Math.Min(plan.portalAt, turnSec) / turnSec) * bar.width;
            var c = portalEntryColor;
            Fill(new Rect(x - 1, bar.y - 2, 2, bar.height + 4), c);
            Fill(new Rect(x - 5, bar.y - 10, 10, 2), c); Fill(new Rect(x - 5, bar.y - 2, 10, 2), c);
            Fill(new Rect(x - 5, bar.y - 10, 2, 10), c); Fill(new Rect(x + 3, bar.y - 10, 2, 10), c);
            var st = new GUIStyle(lbl) { alignment = TextAnchor.MiddleRight };
            st.normal.textColor = c;
            GUI.Label(new Rect(bar.x, bar.y - 14, bar.width, 12), $"PORTAL {plan.portalAt:0.00} S", st);
        }

        /// <summary>Where a pointer at screen position sp meets the placing plane (your height, plus the wheel's rise), m, in reach.</summary>
        Vector3 ExitPoint(Vector2 sp, float height)
        {
            var cam = view.Cam;
            var ray = cam.ScreenPointToRay(sp);
            Vector3 me = PlayerPos;
            float reach = (float)((Specials.PortalReachFt - 200) * Atmosphere.MPerFt);
            Vector3 hit;
            var plane = new Plane(Vector3.up, new Vector3(0, height, 0));
            if (plane.Raycast(ray, out float d) && d < reach * 2) hit = ray.GetPoint(d);
            else
            {
                // looking up at the sky, or too flat: out along the way the pointer looks, at full reach
                Vector3 flat = ray.direction; flat.y = 0;
                if (flat.sqrMagnitude < 1e-6f) flat = cam.transform.forward; flat.y = 0;
                hit = me + flat.normalized * reach;
            }
            hit.y = height;
            Vector3 off = hit - me; off.y = 0;
            if (off.magnitude > reach) hit = new Vector3(me.x, height, me.z) + off.normalized * reach;
            return hit;
        }

        void ExitPress(Vector2 sp)
        {
            exitPressAt = ExitPoint(sp, PlayerPos.y + exitRiseM);
            plan.portalExit = UnityFrame.Pos(exitPressAt);
            Vector3 f = UnityFrame.Dir(battle.planes[0].Forward); f.y = 0;
            if (plan.portalExitFace.Length < 0.5) plan.portalExitFace = UnityFrame.Dir(f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward);
            dirty = true;
        }

        void ExitDrag(Vector2 sp)
        {
            Vector3 to = ExitPoint(sp, exitPressAt.y) - exitPressAt; to.y = 0;
            if (to.magnitude < 40f) return;
            plan.portalExitFace = UnityFrame.Dir(to.normalized);
            dirty = true;
        }

        /// <summary>The wheel while placing the exit: up or down 100 m.</summary>
        void NudgeExitHeight(float notches)
        {
            exitRiseM += notches * 100f;
            var e = plan.portalExit; e.z += notches * 100f * Atmosphere.FtPerM; plan.portalExit = e;
            exitPressAt.y += notches * 100f;
            Notice($"Portal exit {(exitRiseM >= 0 ? "+" : "")}{exitRiseM * Atmosphere.FtPerM:0} ft from your height.");
            dirty = true;
        }

        // ---------- drawing ----------

        /// <summary>While planning: the pairs open now, the one you're about to open (from the preview), and the exit's marker.</summary>
        void DrawLivePortals()
        {
            var r = Rings;
            if (r == null) return;
            foreach (var b in battle.bullets)
                if (b is Portal po)
                    r.Pair(UnityFrame.Pos(po.pos), UnityFrame.Dir(po.faceA), UnityFrame.Pos(po.posB), UnityFrame.Dir(po.faceB), (float)Portal.Openness(po.age), PortalRadiusM);
            if (!plan.portal) return;
            bool shown = false;
            if (preview != null)
                foreach (var tr in preview.portals)
                    if (tr.owner == 0 && tr.startFrame >= 1 && tr.points.Count > 0)
                    {
                        r.Pair(UnityFrame.Pos(tr.points[0]), UnityFrame.Dir(tr.portalFaceA), UnityFrame.Pos(tr.portalB), UnityFrame.Dir(tr.portalFaceB), 0.9f, PortalRadiusM);
                        ExitMarker(UnityFrame.Pos(tr.portalB), UnityFrame.Dir(tr.portalFaceB));
                        shown = true;
                    }
            if (!shown)
            {
                Specials.PortalEnds(battle.planes[0], plan, battle.groundFt, out _, out _, out var exitAt, out var exitFace);
                ExitMarker(UnityFrame.Pos(exitAt), UnityFrame.Dir(exitFace));
            }
            if (placingExit)
            {
                // the reach: a ring at the placing height
                float reach = (float)(Specials.PortalReachFt * Atmosphere.MPerFt);
                Vector3 c = PlayerPos + Vector3.up * exitRiseM;
                var pts = new List<Vector3>(64);
                for (int k = 0; k < 64; k++) { float a = k * Mathf.PI / 32; pts.Add(c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * reach); }
                lines.Strip(pts, 0.6f, new Color(portalExitColor.r, portalExitColor.g, portalExitColor.b, 0.35f), true);
            }
        }

        /// <summary>The exit's marker: an arrow out of it the way you'll come out, and a faint line to it from you.</summary>
        void ExitMarker(Vector3 at, Vector3 face)
        {
            var c = portalExitColor;
            float s = PortalRadiusM * 1.6f;
            Vector3 side = Vector3.Cross(Vector3.up, face).normalized;
            if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
            Vector3 tip = at + face * s;
            lines.Line(at, tip, 1.4f, c);
            lines.Line(tip, tip - face * s * 0.3f + side * s * 0.2f, 1.4f, c);
            lines.Line(tip, tip - face * s * 0.3f - side * s * 0.2f, 1.4f, c);
            var dash = new Color(c.r, c.g, c.b, placingExit ? 0.5f : 0.25f);
            Vector3 me = PlayerPos;
            for (int k = 0; k < 20; k += 2) lines.Line(Vector3.Lerp(me, at, k / 20f), Vector3.Lerp(me, at, (k + 1) / 20f), 0.5f, dash);
        }

        /// <summary>The pairs at frame now of a turn being played back.</summary>
        void DrawPortals(TurnResult r, int now)
        {
            if (r == null || r.portals == null || r.portals.Count == 0) return;
            portalsSeen = true;
            var rg = Rings;
            if (rg == null) return;
            foreach (var tr in r.portals)
            {
                var head = tr.At(now);
                if (head == null) continue;
                double age = tr.age0 + (now - tr.startFrame) * r.dt;
                rg.Pair(UnityFrame.Pos(head.Value), UnityFrame.Dir(tr.portalFaceA), UnityFrame.Pos(tr.portalB), UnityFrame.Dir(tr.portalFaceB), (float)Portal.Openness(age), PortalRadiusM);
            }
        }

        int warpSeen = -1;

        /// <summary>
        /// In playback: when the plane the camera follows comes out of a portal, the camera swings round with it (by the
        /// change in its heading), so it is still behind it on the other side.
        /// </summary>
        void FollowWarp(TurnResult r, int upTo)
        {
            if (view == null || r == null || r.frames == null || r.frames.Length == 0) return;
            if (upTo < warpSeen) warpSeen = -1;
            var f = r.frames[0];
            for (int k = Math.Max(1, warpSeen + 1); k <= upTo && k < f.Length; k++)
            {
                if (!f[k].warped) continue;
                Vector3 a = UnityFrame.Dir(f[k - 1].vel), b = UnityFrame.Dir(f[k].vel);
                float turn = Mathf.Atan2(b.x, b.z) * Mathf.Rad2Deg - Mathf.Atan2(a.x, a.z) * Mathf.Rad2Deg;
                if (view.focus == TurnCamera.Focus.Player && !view.followHeading) view.yaw += turn;   // following, the view turns with the heading by itself
            }
            warpSeen = Math.Max(warpSeen, upTo);
        }
    }
}
