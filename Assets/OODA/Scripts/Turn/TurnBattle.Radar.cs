using System;
using System.Collections.Generic;
using Ooda.Flight;
using Ooda.Flight.Core;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// The radar and the radar missiles on screen (the rules are Turn/Core Radar). A jet's MFD gets a radar page (RDR on its
    /// left, U; it shows by itself while the target is beyond gun range): a B-scope, the cone across and the range up, a blip
    /// for each enemy in the cone, a box round the one tracked and the lock filling over a whole turn. J (or the RDR push
    /// button on that page) arms a radar missile for the turn. When a radar is tracking you a SPIKE warning shows, LOCK once
    /// it is locked; with a radar missile guiding on you, the planning view shows the notch: a green bar across your ghost,
    /// square to the missile's line of sight with the band's edges either side, your path green where it is in the notch,
    /// and a line saying how long the planned turn holds it and when the missile loses you.
    /// </summary>
    public partial class TurnBattle
    {
        public Color notchColor = new Color(0.3f, 1f, 0.45f, 0.95f);
        public Color radarWarnColor = new Color(1f, 0.25f, 0.2f, 1f), spikeColor = new Color(1f, 0.8f, 0.25f, 1f);
        public Color chaffColor = new Color(0.82f, 0.86f, 0.92f, 0.9f);
        bool mfdRadar, mfdPagePicked;

        // the planned turn against the radar missiles on you (RunPreview)
        bool[] previewNotch;          // each step of your path: in the notch against a radar missile guiding on you
        bool notchThreat;             // a radar missile is guiding on you as the turn starts
        double notchBest;             // the longest the notch is held this turn, s
        double notchBreakAt = -1;     // when the first one loses you (s into the turn), -1 none
        MissileLoss notchBreakBy;
        Vec3d? notchMissileEnd;       // the nearest one still guiding where the turn ends, for the notch bar
        bool previewLocked;           // your radar is locked when the planned turn ends
        readonly HashSet<(int, double)> notchWatched = new HashSet<(int, double)>();
        readonly List<Vector3> chaffBits = new List<Vector3>();

        bool HasRadar => battle != null && battle.Count > 0 && battle.combat[0].radarRangeFt > 0 && !battle.Out(0);
        bool RadarPage => HasRadar && (mfdPagePicked ? mfdRadar : AutoRadarPage());

        /// <summary>Until you pick a page, the MFD shows the radar while the target is beyond gun range, and the funnel inside it.</summary>
        bool AutoRadarPage()
        {
            if (focus < 0 || focus >= battle.Count || battle.Out(focus)) return true;
            return (battle.planes[focus].state.pos - battle.planes[0].state.pos).Length > 6000;
        }

        /// <summary>U, or the RDR push button: swap the MFD between the radar and the gun funnel.</summary>
        void ToggleRadarPage()
        {
            if (!HasRadar) { Notice("No radar: only the jets carry one."); return; }
            bool now = RadarPage;
            mfdPagePicked = true;
            mfdRadar = !now;
        }

        /// <summary>J: arm (or safe) this turn's radar missile. It goes the first moment the radar is locked, a whole turn after it starts tracking.</summary>
        void ArmRadarMissile(bool on)
        {
            if (!HasRadar) { Notice("No radar: only the jets carry radar missiles."); return; }
            if (on && battle.radarMissiles[0] <= 0) { Notice("No radar missiles left."); on = false; }
            plan.radarMissile = on;
            plan.radarTarget = focus;
            if (on) Notice(battle.Locked(0)
                ? "Radar missile armed: it goes at once. Keep them in your radar cone (60 degrees off the nose) until it hits."
                : "Radar missile armed: it goes once your radar has held the target in its cone for a whole turn.");
            dirty = true;
        }

        /// <summary>
        /// For RunPreview: watches the radar missiles guiding on you through the planned turn (in the preview's own battle),
        /// marking each step of your path that is in the notch, how long the notch is held and when one loses you. Null when
        /// no radar missile is on you.
        /// </summary>
        Action<Battle, int> NotchWatch(Battle sim, double turnSec)
        {
            int steps = (int)Math.Round(turnSec / Battle.StepSec);
            previewNotch = new bool[steps + 1];
            notchBest = 0; notchBreakAt = -1; notchBreakBy = MissileLoss.None; notchMissileEnd = null;
            notchWatched.Clear();
            foreach (var b in sim.bullets) if (b is Missile ms && ms.radar && ms.target == 0) notchWatched.Add((ms.owner, ms.fireTime));
            notchThreat = notchWatched.Count > 0;
            if (!notchThreat) return null;
            return (b, k) =>
            {
                var me = b.planes[0];
                double nearest = double.PositiveInfinity;
                Vec3d? at = null;
                foreach (var x in b.bullets)
                {
                    if (!(x is Missile ms) || !ms.radar || !notchWatched.Contains((ms.owner, ms.fireTime))) continue;
                    if (ms.target == 0)
                    {
                        Vec3d los = me.state.pos - ms.pos;
                        if (k < previewNotch.Length && Radar.Beaming(me.state.vel, los)) previewNotch[k] = true;
                        notchBest = Math.Max(notchBest, ms.notchSec);
                        if (los.Length < nearest) { nearest = los.Length; at = ms.pos; }
                    }
                    else if (ms.lost != MissileLoss.None && notchBreakAt < 0)
                    {
                        notchBreakAt = k * Battle.StepSec; notchBreakBy = ms.lost;
                        notchBest = Math.Max(notchBest, ms.notchSec);
                    }
                }
                if (at != null) notchMissileEnd = at;
            };
        }

        /// <summary>After the preview: whether your radar is locked when the planned turn ends.</summary>
        void RadarPreviewDone(Battle sim) => previewLocked = HasRadar && sim.Locked(0);

        // ---------- in the planning view ----------

        /// <summary>The notch against the radar missiles on you: your path green where it is in it, and the notch bar at your ghost.</summary>
        void DrawNotch()
        {
            if (!notchThreat || preview == null || previewNotch == null) return;
            var fr = preview.frames[0];
            var run = new List<Vector3>();
            void Flush() { if (run.Count >= 2) lines.Strip(run, 2f, notchColor); run.Clear(); }
            for (int k = 0; k < fr.Length && k < previewNotch.Length; k++)
            {
                if (previewNotch[k]) { if (k % 2 == 0 || run.Count == 0) run.Add(UnityFrame.Pos(fr[k].pos)); }
                else Flush();
            }
            Flush();
            if (notchMissileEnd == null) return;
            // the notch bar: through your ghost where the turn ends, square across the missile's line of sight (seen from
            // above), with the band's edges either side; bright when the turn ends in it
            var end = fr[fr.Length - 1];
            Vec3d los = end.pos - notchMissileEnd.Value;
            var flat = new Vec3d(los.x, los.y, 0);
            if (flat.Length < 1) return;
            flat = flat.Normalized;
            Vector3 p = UnityFrame.Pos(end.pos);
            float s = Mathf.Max(20f, W(p) * 70f);
            bool inAtEnd = previewNotch[Mathf.Min(previewNotch.Length, fr.Length) - 1];
            Color c = inAtEnd ? notchColor : new Color(notchColor.r, notchColor.g, notchColor.b, 0.45f);
            foreach (double off in new[] { 0.0, Radar.NotchBandDeg, -Radar.NotchBandDeg })
            {
                double a = (90 + off) * Math.PI / 180;
                var d = new Vec3d(flat.x * Math.Cos(a) - flat.y * Math.Sin(a), flat.x * Math.Sin(a) + flat.y * Math.Cos(a), 0);
                Vector3 dv = UnityFrame.Dir(d);
                float len = off == 0 ? s : s * 0.7f;
                lines.Line(p - dv * len, p + dv * len, off == 0 ? 1.6f : 0.6f, c);
            }
            // the missile's line of sight to you, faint red
            lines.Line(UnityFrame.Pos(notchMissileEnd.Value), p, 0.5f, new Color(1f, 0.3f, 0.25f, 0.5f));
        }

        /// <summary>The chaff at frame now: a glittering grey cloud that blooms behind the jet and fades as it falls.</summary>
        void DrawChaff(TurnResult r, int now)
        {
            if (r == null || r.chaff == null) return;
            Vector3 rgt = view != null ? view.Cam.transform.right : Vector3.right, up = view != null ? view.Cam.transform.up : Vector3.up;
            foreach (var tr in r.chaff)
            {
                var head = tr.At(now);
                if (head == null) continue;
                double age = tr.age0 + (now - tr.startFrame) * r.dt;
                float life = Mathf.Clamp01(1f - (float)(age / Radar.ChaffBurnSec));
                if (life <= 0) continue;
                Vector3 h = UnityFrame.Pos(head.Value);
                float spread = 1.5f + 6f * Mathf.Sqrt(Mathf.Clamp01((float)age / 1.2f));
                var c = chaffColor; c.a *= life;
                for (int k = 0; k < 9; k++)
                {
                    float a = k * 2.399f + tr.startFrame * 0.37f, rr = spread * Mathf.Sqrt((k + 0.5f) / 9f);
                    Vector3 at = h + rgt * (Mathf.Cos(a) * rr) + up * (Mathf.Sin(a) * rr * 0.7f);
                    float glint = 0.5f + 0.5f * Mathf.Sin(Time.time * 37f + k * 1.3f + tr.startFrame);
                    lines.Line(at - rgt * 0.3f, at + rgt * 0.3f, 1.2f, new Color(c.r, c.g, c.b, c.a * (0.4f + 0.6f * glint)));
                }
            }
        }

        // ---------- on screen ----------

        /// <summary>The radar warnings: a radar missile on you (and what the planned turn does to it), else a lock or a spike.</summary>
        void RadarGui(float sw, float sh)
        {
            if (battle == null || briefing || replaying || phase != Phase.Planning || battle.Out(0)) return;
            float y = irView ? 62 : 36;
            var w = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter, wordWrap = false };
            bool blink = Mathf.Repeat(Time.unscaledTime, 0.8f) < 0.5f;
            double nearest = double.PositiveInfinity;
            foreach (var b in battle.bullets)
                if (b is Missile ms && ms.radar && ms.target == 0) nearest = Math.Min(nearest, (ms.pos - battle.planes[0].state.pos).Length);
            if (!double.IsInfinity(nearest))
            {
                string turn = notchBreakAt >= 0
                    ? notchBreakBy == MissileLoss.Notch ? $"this turn NOTCHES it at {notchBreakAt:F1} s"
                    : notchBreakBy == MissileLoss.Chaff ? $"chaff breaks it at {notchBreakAt:F1} s"
                    : notchBreakBy == MissileLoss.Radar ? $"their radar loses you at {notchBreakAt:F1} s"
                    : $"it loses you at {notchBreakAt:F1} s"
                    : notchBest > 0.05 ? $"in the notch {notchBest:F1} of {Radar.NotchHoldSec:F1} s: hold it longer"
                    : "NOTCH IT: put it off your wingtip, along the green bar";
                string cm = battle.chaff[0] <= 0 ? "no chaff" : plan.holdFlares ? "chaff HELD" : $"chaff on auto ({battle.chaff[0]})";
                w.normal.textColor = notchBreakAt >= 0 ? notchColor : blink ? radarWarnColor : spikeColor;
                GUI.Label(new Rect(sw / 2 - 420, y, 840, 28), $"RADAR MISSILE  {nearest:N0} ft:  {turn};  {cm}", w);
                return;
            }
            bool locked = false; double tracked = -1;
            for (int j = 0; j < battle.Count; j++)
            {
                if (battle.side[j] == battle.side[0] || battle.Out(j) || battle.radarTrack[j] != 0) continue;
                if (battle.Locked(j)) locked = true;
                tracked = Math.Max(tracked, battle.trackSec[j]);
            }
            if (locked)
            {
                w.normal.textColor = blink ? radarWarnColor : spikeColor;
                GUI.Label(new Rect(sw / 2 - 420, y, 840, 28), "RADAR LOCK: they can fire a radar missile. Get off their nose, or be ready to notch", w);
            }
            else if (tracked >= 0)
            {
                w.fontSize = 17;
                w.normal.textColor = spikeColor;
                GUI.Label(new Rect(sw / 2 - 420, y, 840, 26), $"RADAR SPIKE: a radar is tracking you, locked in {Math.Max(0, Radar.LockSec - tracked):F1} s if you stay in its cone", w);
            }
        }

        /// <summary>The radar page's top line: RDR, the lock, the radar missile's state.</summary>
        void RadarHeader(Rect r)
        {
            var st = new GUIStyle(mfdText);
            st.normal.textColor = MfdGreen;
            GUI.Label(r, "RDR", st);
            int t = battle.radarTrack[0];
            string lockText; Color lc = MfdGreen;
            if (t >= 0 && battle.Locked(0)) { lockText = $"LOCK {EnemyName(t).ToUpperInvariant()}"; lc = MfdRed; }
            else if (t >= 0) lockText = previewLocked ? "TRK: LOCKS THIS TURN" : $"TRK {battle.trackSec[0]:F1}/{Radar.LockSec:F0} S";
            else lockText = previewLocked ? "LOCKS THIS TURN" : "NO TRK";
            st.alignment = TextAnchor.MiddleCenter;
            st.normal.textColor = lc;
            GUI.Label(r, lockText, st);
            string msl = "";
            if (battle.radarMissiles[0] > 0 && plan.radarMissile)
            {
                Trace mt = null;
                if (preview != null) foreach (var tr in preview.traces) if (tr.radar && tr.owner == 0 && tr.startFrame > 0) { mt = tr; break; }
                msl = mt == null ? "RDR WAIT" : mt.hit ? "RDR HIT" : "RDR AWAY";
            }
            st.alignment = TextAnchor.MiddleRight;
            st.normal.textColor = plan.radarMissile ? MfdRed : MfdGreen;
            GUI.Label(r, msl, st);
        }

        /// <summary>
        /// The radar page: a B-scope, the cone across (60 degrees each side of the nose) and the range up to the radar's reach,
        /// a blip for each enemy in the cone with a tick for where it is heading, a box round the one tracked (red once
        /// locked), your radar missiles in flight, and the lock filling along the bottom.
        /// </summary>
        void RadarScope(Rect v)
        {
            var me = battle.planes[0];
            double reach = battle.combat[0].radarRangeFt, cone = Radar.ConeDeg;
            Rect plot = new Rect(v.x, v.y, v.width, v.height - 16);
            float X(double az) => plot.x + (float)((az + cone) / (2 * cone)) * plot.width;
            float Y(double ft) => plot.yMax - (float)(ft / reach) * plot.height;
            var dim = new GUIStyle(mfdSmall) { alignment = TextAnchor.MiddleLeft };
            dim.normal.textColor = MfdDim;
            Color grid = new Color(MfdGreen.r, MfdGreen.g, MfdGreen.b, 0.25f);
            foreach (double az in new[] { -cone, -cone / 2, 0, cone / 2, cone }) Fill(new Rect(X(az), plot.y, 1, plot.height), grid);
            for (int k = 1; k <= 3; k++)
            {
                double ft = reach * k / 3;
                Fill(new Rect(plot.x, Y(ft), plot.width, 1), grid);
                GUI.Label(new Rect(plot.x + 2, Y(ft), 30, 11), (ft / 1000).ToString("0"), dim);
            }
            // your nose marker at the bottom
            Fill(new Rect(X(0) - 4, plot.yMax - 2, 9, 2), MfdGreen);
            Vec3d fwd = me.Forward, right = Vec3d.Cross(me.Forward, me.Up).Normalized;
            bool Place(Vec3d at, out Vector2 p)
            {
                Vec3d d = at - me.state.pos;
                double range = d.Length, az = Math.Atan2(Vec3d.Dot(d, right), Vec3d.Dot(d, fwd)) * 180 / Math.PI;
                p = new Vector2(X(az), Y(range));
                return Math.Abs(az) <= cone && range <= reach;
            }
            // the enemies in the cone
            for (int j = 0; j < battle.Count; j++)
            {
                if (battle.side[j] == battle.side[0] || battle.Out(j) || !battle.InRadarCone(0, j)) continue;   // (stealth is out of its cone; cloud is not)
                if (!Place(battle.planes[j].state.pos, out var p)) continue;
                bool tracked = battle.radarTrack[0] == j, locked = tracked && battle.Locked(0);
                Color c = locked ? MfdRed : MfdGreen;
                Fill(new Rect(p.x - 3, p.y - 3, 7, 7), c);
                // where it is heading, as a short tick (a target flying across the scope's lines is beaming you)
                Vec3d hv = battle.planes[j].state.vel;
                var tick = new Vector2((float)Vec3d.Dot(hv, right), -(float)Vec3d.Dot(hv, fwd));
                if (tick.magnitude > 1e-3f) MfdLine(p, p + tick.normalized * 12, 1, c, plot);
                if (tracked)
                {
                    Rect box = new Rect(p.x - 8, p.y - 8, 17, 17);
                    Fill(new Rect(box.x, box.y, box.width, 1), c); Fill(new Rect(box.x, box.yMax - 1, box.width, 1), c);
                    Fill(new Rect(box.x, box.y, 1, box.height), c); Fill(new Rect(box.xMax - 1, box.y, 1, box.height), c);
                }
                if (j == focus && !tracked) Fill(new Rect(p.x - 6, p.y + 6, 13, 1), MfdDim);
            }
            // your radar missiles in flight
            foreach (var b in battle.bullets)
                if (b is Missile ms && ms.radar && ms.owner == 0 && Place(ms.pos, out var mp))
                    Fill(new Rect(mp.x - 1, mp.y - 1, 3, 3), ms.target >= 0 ? Color.white : MfdDim);
            // the lock filling along the bottom: a whole turn with the same one in the cone
            Rect bar = new Rect(v.x + 46, v.yMax - 11, v.width - 120, 7);
            Fill(new Rect(bar.x, bar.y, bar.width, 1), MfdDim); Fill(new Rect(bar.x, bar.yMax - 1, bar.width, 1), MfdDim);
            Fill(new Rect(bar.x, bar.y, 1, bar.height), MfdDim); Fill(new Rect(bar.xMax - 1, bar.y, 1, bar.height), MfdDim);
            float fill = battle.radarTrack[0] >= 0 ? Mathf.Clamp01((float)(battle.trackSec[0] / Radar.LockSec)) : 0;
            if (fill > 0) Fill(new Rect(bar.x + 1, bar.y + 1, (bar.width - 2) * fill, bar.height - 2), fill >= 1 ? MfdRed : MfdGreen);
            GUI.Label(new Rect(v.x, v.yMax - 13, 44, 12), "LOCK", dim);
            var rs = new GUIStyle(dim) { alignment = TextAnchor.MiddleRight };
            GUI.Label(new Rect(v.x, v.yMax - 13, v.width, 12), $"{reach / 1000:0}K FT", rs);
        }
    }
}
