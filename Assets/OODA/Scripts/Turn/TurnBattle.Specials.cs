using System;
using System.Collections.Generic;
using Ooda.Flight;
using Ooda.Flight.Core;
using Ooda.Turn.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ooda.Turn
{
    /// <summary>
    /// The arcade specials (the rules are Turn/Core/Specials.cs), planned like the rocket for the start of the turn:
    ///
    /// 4, Balloon mine: Kenny's red balloon with a spiked mine on a line (Resources/Mine), let go behind you at the moment
    /// you pick on the MFD's trigger bar (drag its red marker; 4 steps it 0, 1, 2, 3 s, then off). It hangs where it was
    /// dropped and drifts slowly on, blinks once armed, and goes off on the first enemy that flies within its fuse (the faint
    /// red ring round it while planning). Ideal for a tailer.
    ///
    /// 5, Heat blade: the whole plane glows red hot all turn and its guns stay cold; fly through an enemy and it is cut in half.
    /// You are the brightest thing in the IR view while it glows: every heat-seeker locks you from farther.
    ///
    /// 7, Jammer: the enemies near you see scrambled ghosts for two turns (their AI pilots plan against where you aren't).
    /// Jammed yourself, the enemies' projections, ghosts and predicted moves jump to the wrong place and flicker, and the
    /// funnel and the spread of their likely moves are gone: you have only the planes themselves to go by.
    ///
    /// 6, Stealth: two turns unseen. A stealthy enemy vanishes from your screen with its ghosts and projection (only its
    /// heat shows, in the IR view, and a heat-seeker still locks it); yours shows only as a dotted cyan outline. Firing shows you again.
    /// </summary>
    public partial class TurnBattle
    {
        [Header("Specials")]
        public Color mineColor = new Color(0.92f, 0.16f, 0.12f, 1f);
        public Color bladeColor = new Color(1f, 0.32f, 0.06f, 0.95f);
        public Color stealthColor = new Color(0.55f, 0.95f, 1f, 0.7f);
        public Color jamColor = new Color(0.85f, 0.5f, 1f, 1f);

        /// <summary>An enemy you can't see now: stealthy, on the other side.</summary>
        bool HiddenFromMe(int i) => battle != null && i >= 0 && i < battle.Count && battle.side[i] != battle.side[0] && battle.Cloaked(i);

        // ---------- planning ----------

        void ToggleMine()
        {
            if (!plan.mine && battle.balloonMines[0] <= 0) { Notice("No balloon mines left."); return; }
            plan.mine = !plan.mine;
            if (plan.mine) MineNotice();
            dirty = true;
        }

        /// <summary>The 4 key: off, then let go at 0, 1, 2 and 3 s into the turn, then off again.</summary>
        void StepMine()
        {
            if (!plan.mine) { if (battle.balloonMines[0] <= 0) { Notice("No balloon mines left."); return; } plan.mine = true; plan.mineAt = 0; }
            else if (plan.mineAt >= turnSec - 0.01) { plan.mine = false; plan.mineAt = 0; }
            else plan.mineAt = Math.Min(turnSec, Math.Floor(plan.mineAt + 1.0001));
            if (plan.mine) MineNotice();
            dirty = true;
        }

        void MineNotice() => Notice($"Balloon mine: let go {plan.mineAt:0.0} s into the turn (drag the red marker on the trigger bar, or 4 steps it). It hangs and drifts slowly, and catches whoever follows.");

        /// <summary>Where the mine's marker sits on the trigger bar, and its grab area (a little wider than it).</summary>
        Rect MineMarker => new Rect(burstRect.x + (float)(Math.Min(plan.mineAt, turnSec) / turnSec) * burstRect.width - 8, burstRect.y - 9, 16, burstRect.height + 12);

        bool PressMine(Vector2 gui) => plan.mine && burstRect.width > 0 && MineMarker.Contains(gui);

        void SetMineFromBar(float guiX)
        {
            float t = Mathf.Clamp01((guiX - burstRect.x) / burstRect.width) * turnSec;
            plan.mineAt = Mathf.Round(t / 0.05f) * 0.05f;
            dirty = true;
        }

        /// <summary>The mine on the trigger bar: a red ball on a stalk at its moment, and its time in the label line.</summary>
        void MineOnBar(Rect bar, GUIStyle lbl)
        {
            if (!plan.mine) return;
            float x = bar.x + (float)(Math.Min(plan.mineAt, turnSec) / turnSec) * bar.width;
            var red = new Color(1f, 0.25f, 0.2f, 1f);
            Fill(new Rect(x - 1, bar.y - 2, 2, bar.height + 4), red);
            Fill(new Rect(x - 4, bar.y - 9, 8, 8), red);
            Fill(new Rect(x - 2, bar.y - 8, 2, 2), new Color(1f, 0.75f, 0.7f, 1f));
            var st = new GUIStyle(lbl) { alignment = TextAnchor.MiddleRight };
            st.normal.textColor = red;
            GUI.Label(new Rect(bar.x, bar.y - 14, bar.width, 12), $"MINE {plan.mineAt:0.00} S", st);
        }

        void ToggleBlade()
        {
            if (!plan.ram && battle.heatBlades[0] <= 0) { Notice("The heat blade is spent."); return; }
            plan.ram = !plan.ram;
            if (plan.ram) Notice(plan.burstEnd > plan.burstStart
                ? "Heat blade lit: your guns stay cold this turn. Fly through an enemy to cut it in half."
                : "Heat blade lit: fly through an enemy to cut it in half. Every heat-seeker can see you.");
            dirty = true;
        }

        void ToggleJam()
        {
            if (!plan.jam && battle.jammers[0] <= 0) { Notice("The jammer is spent."); return; }
            plan.jam = !plan.jam;
            if (plan.jam) Notice($"Jammer: every enemy within {Specials.JamRangeFt / 6076.1:0.#} nm sees scrambled ghosts for two turns.");
            dirty = true;
        }

        // ---------- jammed: the enemies' ghosts where they aren't ----------

        /// <summary>The plane you are flying is jammed: the other side's projections are scrambled.</summary>
        bool JammedNow => battle != null && battle.Jammed(0);

        /// <summary>A projection you can't trust: an enemy's, while you are jammed.</summary>
        bool JammedFrom(int i) => JammedNow && i >= 0 && i < battle.Count && battle.side[i] != battle.side[0];

        readonly Dictionary<AircraftFrame[], AircraftFrame[]> jamSeen = new Dictionary<AircraftFrame[], AircraftFrame[]>();
        int jamSeenTurn = -1;

        /// <summary>An aircraft's projected path as you see it: as it is, or (an enemy's, while jammed) moved and turned by the jammer's scramble.</summary>
        AircraftFrame[] Seen(int i, AircraftFrame[] fr)
        {
            if (fr == null || fr.Length == 0 || !JammedFrom(i)) return fr;
            if (jamSeenTurn != battle.turn || jamSeen.Count > 64) { jamSeen.Clear(); jamSeenTurn = battle.turn; }
            if (jamSeen.TryGetValue(fr, out var got)) return got;
            Jam.Offset(battle.turn, 0, i, out var shift, out double rot);
            double c = Math.Cos(rot), s = Math.Sin(rot);
            Vec3d Turn(Vec3d v) => new Vec3d(v.x * c - v.y * s, v.x * s + v.y * c, v.z);
            Vec3d at = fr[0].pos;
            var o = new AircraftFrame[fr.Length];
            for (int k = 0; k < fr.Length; k++)
            {
                var f = fr[k];
                f.pos = at + Turn(f.pos - at) + shift; f.vel = Turn(f.vel); f.up = Turn(f.up);
                o[k] = f;
            }
            jamSeen[fr] = o;
            return o;
        }

        /// <summary>A jammed enemy ghost drops out now and then, like a bad signal.</summary>
        bool JamFlicker(int i) => !JammedFrom(i) || Mathf.Repeat(Time.time * 3.1f + i * 0.41f, 1f) > 0.18f;

        void ToggleCloak()
        {
            if (!plan.cloak && battle.stealths[0] <= 0) { Notice("No stealth left."); return; }
            plan.cloak = !plan.cloak;
            if (plan.cloak) Notice(plan.burstEnd > plan.burstStart || plan.missile
                ? "Stealth: but firing this turn will show you again at once."
                : "Stealth: two turns unseen. Heat-seekers still lock you; firing shows you.");
            dirty = true;
        }

        /// <summary>4 balloon mine (stepping its moment), 5 heat blade, 6 stealth, 7 jammer, 8 gravity well (stepping its moment; TurnBattle.Well.cs), ' jet stream (stepping its moment) and \ turning it round (TurnBattle.JetStream.cs).</summary>
        void SpecialsInput(Keyboard kb)
        {
            bool Pressed(Key k) => kb != null && kb[k].wasPressedThisFrame;
            if (Pressed(Key.Digit4) || Pressed(Key.Numpad4)) StepMine();
            if (Pressed(Key.Digit5) || Pressed(Key.Numpad5)) ToggleBlade();
            if (Pressed(Key.Digit6) || Pressed(Key.Numpad6)) ToggleCloak();
            if (Pressed(Key.Digit7) || Pressed(Key.Numpad7)) ToggleJam();
            if (Pressed(Key.Digit8) || Pressed(Key.Numpad8)) StepWell();
            if (Pressed(Key.Quote)) StepLane();
            if (Pressed(Key.Backslash)) FlipLane();
        }

        // ---------- drawing ----------

        /// <summary>The balloon mines hanging in the air now (planning): each with its fuse ring, and where a planned one would go.</summary>
        void DrawLiveMines()
        {
            foreach (var b in battle.bullets)
                if (b is Mine mn) Balloon(UnityFrame.Pos(mn.pos), mn.Armed, true, mn.side == battle.side[0]);
            if (preview != null)
                foreach (var tr in preview.mines)
                    if (tr.owner == 0 && tr.startFrame >= 1 && tr.points.Count > 0)
                    {
                        // the one you're planning to let go: a mark on your path where it leaves, and faint, where it ends the turn
                        var drop = UnityFrame.Pos(tr.points[0]);
                        Vector3 right = view != null ? view.Cam.transform.right : Vector3.right, up = view != null ? view.Cam.transform.up : Vector3.up;
                        float rr = Mathf.Max(2f, W(drop) * 6f);
                        var mark = new List<Vector3>(12);
                        for (int k = 0; k < 12; k++) { float a = k * Mathf.PI / 6; mark.Add(drop + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * rr); }
                        lines.Strip(mark, 0.5f, new Color(mineColor.r, mineColor.g, mineColor.b, 0.8f), true);
                        var pts = new List<Vector3>();
                        for (int i = 0; i < tr.points.Count; i += 4) pts.Add(UnityFrame.Pos(tr.points[i]));
                        pts.Add(UnityFrame.Pos(tr.points[tr.points.Count - 1]));
                        if (pts.Count >= 2) lines.Strip(pts, 0.4f, new Color(mineColor.r, mineColor.g, mineColor.b, 0.35f));
                        Balloon(UnityFrame.Pos(tr.points[tr.points.Count - 1]), false, true, true, 0.5f);
                    }
        }

        /// <summary>The balloon mines at frame now of a turn being played back; one that went off is gone (its flash is the hit's).</summary>
        void DrawMines(TurnResult r, int now)
        {
            if (r == null || r.mines == null) return;
            foreach (var tr in r.mines)
            {
                var head = tr.At(now);
                if (head == null) continue;
                double age = tr.age0 + (now - tr.startFrame) * r.dt;
                Balloon(UnityFrame.Pos(head.Value), age >= Specials.ArmSec, false, tr.owner >= 0 && tr.owner < battle.Count && battle.side[tr.owner] == battle.side[0]);
            }
        }

        // ---------- the mine model (Resources/Mine, Kenny's Hi3D balloon mine, cleaned by Tools~/ArtPipeline/mine.py) ----------

        Mesh mineMesh;
        Material mineMat;
        bool mineLoaded;
        readonly List<GameObject> mineModels = new List<GameObject>();
        int minesShown, minesFrame = -1;

        /// <summary>The model's mesh and material, loaded once; false when it is not in the build (the drawn balloon stands in).</summary>
        bool MineModelReady()
        {
            if (mineLoaded) return mineMesh != null;
            mineLoaded = true;
            var model = Resources.Load<GameObject>("Mine");
            var mf = model != null ? model.GetComponentInChildren<MeshFilter>(true) : null;
            if (mf == null || mf.sharedMesh == null) { Debug.Log("OODA: no mine model (Resources/Mine): the balloon mines are drawn"); return false; }
            mineMesh = mf.sharedMesh;
            var tex = Resources.Load<Texture2D>("Mine_BaseColor");
            // the aircraft's own lit material as the template, so its shader is surely in the build
            var like = player != null ? player.GetComponentInChildren<MeshRenderer>() : null;
            mineMat = like != null && like.sharedMaterial != null ? new Material(like.sharedMaterial) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mineMat.name = "Mine";
            foreach (var k in new[] { "_BaseMap", "_MainTex" }) if (mineMat.HasProperty(k)) mineMat.SetTexture(k, tex);
            foreach (var k in new[] { "_BaseColor", "_Color" }) if (mineMat.HasProperty(k)) mineMat.SetColor(k, Color.white);
            foreach (var k in new[] { "_MetallicGlossMap", "_BumpMap", "_OcclusionMap", "_EmissionMap" }) if (mineMat.HasProperty(k)) mineMat.SetTexture(k, null);
            if (mineMat.HasProperty("_Metallic")) mineMat.SetFloat("_Metallic", 0.1f);
            if (mineMat.HasProperty("_Smoothness")) mineMat.SetFloat("_Smoothness", 0.6f);
            Debug.Log($"OODA: mine model {mineMesh.vertexCount} vertices, {(tex != null ? $"colour map {tex.width}x{tex.height}" : "NO colour map")}, shader {mineMat.shader.name}");
            return true;
        }

        /// <summary>Put the next mine model of this frame at p: upright, turning slowly, size times its 4.2 m.</summary>
        void PlaceMineModel(Vector3 p, float size, float spin)
        {
            if (minesFrame != Time.frameCount) { minesFrame = Time.frameCount; minesShown = 0; }
            if (minesShown == mineModels.Count)
            {
                var go = new GameObject("BalloonMine");
                go.AddComponent<MeshFilter>().sharedMesh = mineMesh;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mineMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mineModels.Add(go);
            }
            var m = mineModels[minesShown++];
            m.SetActive(true);
            m.transform.SetPositionAndRotation(p, Quaternion.Euler(0, spin, 0));
            m.transform.localScale = Vector3.one * size;
        }

        /// <summary>After the frame's drawing: hide the mine models no mine used this frame.</summary>
        void HideSpareMines()
        {
            int used = minesFrame == Time.frameCount ? minesShown : 0;
            for (int i = used; i < mineModels.Count; i++) if (mineModels[i] != null && mineModels[i].activeSelf) mineModels[i].SetActive(false);
        }

        /// <summary>
        /// A balloon mine at p (the mine's middle): the model, or a spiked red balloon drawn on a short dangling line when the
        /// model isn't in the build or this is the faint one you're planning; its light blinks once armed; with ring, the
        /// fuse's reach round it.
        /// </summary>
        void Balloon(Vector3 p, bool armed, bool ring, bool ours, float alpha = 1f)
        {
            Vector3 right = view != null ? view.Cam.transform.right : Vector3.right, up = view != null ? view.Cam.transform.up : Vector3.up;
            if (alpha >= 1f && MineModelReady())
            {
                // life size close by, growing with range so it still reads as a mine a mile off
                float size = Mathf.Max(1f, W(p) * 1.6f);
                PlaceMineModel(p, size, Mathf.Repeat(Time.time * 12f + p.x * 3.1f, 360f));
                if (armed && Mathf.Repeat(Time.time * 1.6f, 1f) < 0.3f)
                    lines.Line(p + Vector3.up * 3.4f * size, p + Vector3.up * 3.75f * size, 2.2f, ours ? new Color(0.5f, 1f, 0.5f, 1f) : new Color(1f, 0.9f, 0.3f, 1f));
                if (ring) FuseRing(p, right, up, ours, alpha);
                return;
            }
            float r = Mathf.Max(1.6f, W(p) * 7f);
            var red = mineColor; red.a *= alpha;
            var dark = new Color(0.25f, 0.05f, 0.05f, 0.9f * alpha);
            lines.Line(p, p + Vector3.down * r * 2.5f, 0.25f, new Color(0.85f, 0.85f, 0.8f, 0.8f * alpha));   // the line it hangs from (a mine on a string)
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4;
                Vector3 d = right * Mathf.Cos(a) + up * Mathf.Sin(a);
                lines.Line(p + d * r * 0.8f, p + d * r * 1.35f, 0.5f, dark);   // the spikes
            }
            lines.Line(p - up * r * 0.55f, p + up * r * 0.55f, 5.5f, red);   // the ball
            lines.Line(p + up * r * 0.2f - right * r * 0.25f, p + up * r * 0.35f - right * r * 0.15f, 1.6f, new Color(1f, 0.7f, 0.65f, 0.8f * alpha));   // its shine
            if (armed && Mathf.Repeat(Time.time * 1.6f, 1f) < 0.3f)
                lines.Line(p + up * r * 0.9f, p + up * r * 1.15f, 2.2f, ours ? new Color(0.5f, 1f, 0.5f, alpha) : new Color(1f, 0.9f, 0.3f, alpha));
            if (ring) FuseRing(p, right, up, ours, alpha);
        }

        void FuseRing(Vector3 p, Vector3 right, Vector3 up, bool ours, float alpha)
        {
            float fuse = (float)(Specials.FuseFt * Atmosphere.MPerFt);
            var pts = new List<Vector3>(24);
            for (int k = 0; k < 24; k++) { float a = k * Mathf.PI / 12; pts.Add(p + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * fuse); }
            lines.Strip(pts, 0.35f, new Color(mineColor.r, mineColor.g, mineColor.b, (ours ? 0.25f : 0.5f) * alpha), true);
        }

        /// <summary>
        /// The heat blade's glow and the stealth shimmer on the aircraft at frame now. In the turn's playback a stealthy enemy
        /// is hidden; in the replay you see everyone, the stealthy ones shimmering.
        /// </summary>
        void DrawSpecialFrames(IList<TurnAircraftView> vs, TurnResult r, int now)
        {
            for (int k = 0; k < vs.Count && k < r.frames.Length; k++)
            {
                if (vs[k] == null || r.frames[k] == null) continue;
                var f = r.frames[k][Mathf.Min(now, r.frames[k].Length - 1)];
                bool enemy = !replaying && k < battle.Count && battle.side[k] != battle.side[0];
                Stealth(vs[k], f.cloaked && !f.down, enemy, k);
                RedHot(vs[k], f.ram && !f.down, k);
            }
        }

        /// <summary>While planning: hide the stealthy enemies, outline your own, and glow your ghost if the blade is lit.</summary>
        void SpecialsPlanning()
        {
            for (int k = 0; k < views.Length && k < battle.Count; k++)
                if (views[k] != null) { Stealth(views[k], battle.Cloaked(k), battle.side[k] != battle.side[0], k); views[k].SetRedHot(false); }
            if (plan.ram && holoMine != null && holoMine.activeSelf) BladeLook(holoMine.transform, 0);
            DrawLiveMines();
            DrawLiveWells();
            DrawLiveLanes();
        }

        /// <summary>Everyone shown again (the fight is over, or no one is stealthy).</summary>
        void RevealAll()
        {
            foreach (var v in views) if (v != null) { v.SetVisible(true); v.SetRedHot(false); }
        }

        void Stealth(TurnAircraftView v, bool cloaked, bool enemy, int k)
        {
            if (!cloaked) { v.SetVisible(true); return; }
            v.SetVisible(false);
            if (!enemy) DottedOutline(v, k);   // yours: only a dotted outline, so you know where you are
        }

        // a fighter seen from above, nose first down its right side (x across the wings, z along, as shares of the half span
        // and half length); the left side is its mirror
        static readonly Vector2[] PlanForm =
        {
            new Vector2(0f, 1f), new Vector2(0.1f, 0.85f), new Vector2(0.12f, 0.3f), new Vector2(1f, 0.12f), new Vector2(1f, -0.05f),
            new Vector2(0.12f, -0.15f), new Vector2(0.1f, -0.72f), new Vector2(0.38f, -0.8f), new Vector2(0.38f, -0.95f), new Vector2(0f, -0.93f),
        };
        readonly List<Vector3> outlinePts = new List<Vector3>(32);

        /// <summary>A stealthy plane of yours: its outline from above and its fin, in marching cyan dashes, the model itself hidden.</summary>
        void DottedOutline(TurnAircraftView v, int k)
        {
            var t = v.transform;
            var b = v.LocalBounds();
            float sx = b.extents.x, sz = b.extents.z, y = b.center.y;
            outlinePts.Clear();
            for (int i = 0; i < PlanForm.Length; i++) outlinePts.Add(t.TransformPoint(new Vector3(b.center.x + PlanForm[i].x * sx, y, b.center.z + PlanForm[i].y * sz)));
            for (int i = PlanForm.Length - 2; i >= 1; i--) outlinePts.Add(t.TransformPoint(new Vector3(b.center.x - PlanForm[i].x * sx, y, b.center.z + PlanForm[i].y * sz)));
            Dashes(outlinePts, true, k);
            // the fin, standing up at the tail
            outlinePts.Clear();
            float top = b.max.y;
            outlinePts.Add(t.TransformPoint(new Vector3(b.center.x, y, b.center.z - 0.55f * sz)));
            outlinePts.Add(t.TransformPoint(new Vector3(b.center.x, top, b.center.z - 0.85f * sz)));
            outlinePts.Add(t.TransformPoint(new Vector3(b.center.x, top, b.center.z - 1f * sz)));
            outlinePts.Add(t.TransformPoint(new Vector3(b.center.x, y, b.center.z - 0.97f * sz)));
            Dashes(outlinePts, false, k);
        }

        /// <summary>A path drawn as dashes, 0.9 m on and 0.7 m off, creeping along it.</summary>
        void Dashes(List<Vector3> pts, bool loop, int k)
        {
            const float on = 0.9f, period = 1.6f;
            float phase = Mathf.Repeat(Time.time * 1.5f + k * 0.3f, period);
            int n = loop ? pts.Count : pts.Count - 1;
            float start = 0;   // the distance along the path where this segment begins
            for (int i = 0; i < n; i++)
            {
                Vector3 a = pts[i], c = pts[(i + 1) % pts.Count];
                float len = Vector3.Distance(a, c);
                if (len < 1e-4f) continue;
                // dashes begin at m * period - phase along the whole path
                for (float d = Mathf.Floor((start + phase) / period) * period - phase; d < start + len; d += period)
                {
                    float s0 = Mathf.Max(d, start) - start, s1 = Mathf.Min(d + on, start + len) - start;
                    if (s1 > s0) lines.Line(Vector3.Lerp(a, c, s0 / len), Vector3.Lerp(a, c, s1 / len), 0.55f, stealthColor);
                }
                start += len;
            }
        }

        /// <summary>The heat blade on a plane: the whole airframe glowing red, pulsing, with the heat haze trailing.</summary>
        void RedHot(TurnAircraftView v, bool on, int k)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 9f + k * 1.3f);
            v.SetRedHot(on, pulse);
            if (!on) return;
            var t = v.transform;
            var haze = bladeColor; haze.a = 0.35f;
            lines.Line(t.position - t.forward * 4.5f, t.position - t.forward * 16f, 1.6f, haze);
        }

        /// <summary>Red hot on your ghost while planning: the wings and the fuselage glowing orange to white, flickering.</summary>
        void BladeLook(Transform t, int k)
        {
            float flick = 0.8f + 0.2f * Mathf.Sin(Time.time * 31f + k * 1.3f);
            Vector3 p = t.position, rgt = t.right, fwd = t.forward;
            lines.Line(p - rgt * 6f, p + rgt * 6f, 2.4f * flick, bladeColor);
            lines.Line(p - rgt * 5f, p + rgt * 5f, 0.9f, new Color(1f, 0.9f, 0.6f, 0.95f));
            lines.Line(p + fwd * 4.5f, p - fwd * 4.5f, 2.0f * flick, bladeColor);
            var haze = bladeColor; haze.a = 0.35f;
            lines.Line(p - fwd * 4.5f, p - fwd * 16f, 1.6f, haze);
        }
    }
}
