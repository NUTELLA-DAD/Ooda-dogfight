using System.Collections.Generic;
using System.Linq;
using Ooda.Flight;
using Ooda.Flight.Core;
using Ooda.Pilot;
using Ooda.Turn.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Ooda.Turn
{
    /// <summary>
    /// The menu and the replay of the whole fight.
    ///
    /// Esc (or the Menu button) pauses and opens the menu: carry on, start the mission again, go back to the title
    /// screen, or quit. When the fight is over the end screen offers the same, and Watch replay.
    ///
    /// Every turn flown is kept on a tape, by aircraft in the order they were at the start of the fight (the plane
    /// you fly changes places in the battle's lists when you Tab to it, so the tape keeps its own order, by FlightModel).
    /// The replay flies the tape turn after turn with the tracers, the hits, the smoke and the pilots' gestures, and
    /// any camera: chase, the target, both, or the cockpit (V), following any aircraft in the fight (Tab, Shift+Tab back).
    /// Space pauses, the left and right arrows step a turn, up and down change the speed, the bar along the bottom
    /// jumps anywhere, Backspace leaves the replay.
    /// </summary>
    public partial class TurnBattle
    {
        [Header("Menu")]
        [Tooltip("The title screen's scene, for Main menu.")]
        public string titleScene = "Title";

        // ---------- the tape ----------

        class TapeTurn
        {
            public TurnResult result;     // frames, traces and hits, by tape index
            public Systems[] sysBefore;    // by tape index
            public bool[] crashedAfter;    // by tape index
            public int[] missilesAfter;    // by tape index: missiles left after the turn (TurnBattle.Racks)
            public float Sec => (float)((result.FrameCount - 1) * result.dt);
        }

        readonly List<TapeTurn> tape = new List<TapeTurn>();
        readonly List<FlightModel> tapePlanes = new List<FlightModel>();
        readonly List<TurnAircraftView> tapeViews = new List<TurnAircraftView>();
        readonly List<int> tapeSide = new List<int>();
        readonly List<bool> tapeBomber = new List<bool>();

        /// <summary>At a restart, once the views are laid out: an empty tape, in this order.</summary>
        void TapeStarts()
        {
            tape.Clear(); tapePlanes.Clear(); tapeViews.Clear(); tapeSide.Clear(); tapeBomber.Clear(); tapeLoaded.Clear();
            for (int i = 0; i < battle.Count; i++) TapeIndex(i);
        }

        int TapeIndex(int i)
        {
            int s = tapePlanes.IndexOf(battle.planes[i]);
            if (s >= 0) return s;
            tapePlanes.Add(battle.planes[i]);
            tapeViews.Add(i < views.Length ? views[i] : null);
            tapeSide.Add(battle.side[i]);
            tapeBomber.Add(battle.routes[i] != null);
            tapeLoaded.Add(Loaded(i));
            return tapePlanes.Count - 1;
        }

        /// <summary>Right after a turn is resolved (systemsBefore holds the systems it started with): a copy of it on the tape.</summary>
        void RecordTurn()
        {
            if (last == null) return;
            int n = battle.Count;
            var map = new int[n];
            for (int i = 0; i < n; i++) map[i] = TapeIndex(i);
            int m = tapePlanes.Count;
            var r = new TurnResult { startTime = last.startTime, dt = last.dt, frames = new AircraftFrame[m][] };
            var t = new TapeTurn { result = r, sysBefore = new Systems[m], crashedAfter = new bool[m], missilesAfter = new int[m] };
            for (int i = 0; i < n && i < last.frames.Length; i++) r.frames[map[i]] = last.frames[i];
            for (int i = 0; i < n; i++)
            {
                t.sysBefore[map[i]] = i < systemsBefore.Length ? systemsBefore[i] : battle.SystemsOf(i);
                t.crashedAfter[map[i]] = battle.crashed[i];
                t.missilesAfter[map[i]] = battle.missiles[i];
            }
            foreach (var tr in last.traces)
                r.traces.Add(new Trace { owner = tr.owner >= 0 && tr.owner < n ? map[tr.owner] : tr.owner, startFrame = tr.startFrame, points = tr.points, hit = tr.hit, missile = tr.missile, radar = tr.radar, magnet = tr.magnet, age0 = tr.age0, bounceAt = tr.bounceAt, bouncer = tr.bouncer >= 0 && tr.bouncer < n ? map[tr.bouncer] : tr.bouncer });
            foreach (var tr in last.flares)
                r.flares.Add(new Trace { owner = tr.owner >= 0 && tr.owner < n ? map[tr.owner] : tr.owner, startFrame = tr.startFrame, points = tr.points, age0 = tr.age0 });
            foreach (var tr in last.chaff)
                r.chaff.Add(new Trace { owner = tr.owner >= 0 && tr.owner < n ? map[tr.owner] : tr.owner, startFrame = tr.startFrame, points = tr.points, age0 = tr.age0, chaff = true });
            foreach (var h in last.hits)
            {
                var c = h;
                if (c.shooter >= 0 && c.shooter < n) c.shooter = map[c.shooter];
                if (c.target >= 0 && c.target < n) c.target = map[c.target];
                r.hits.Add(c);
            }
            foreach (var h in last.ricochets)
            {
                var c = h;
                if (c.shooter >= 0 && c.shooter < n) c.shooter = map[c.shooter];
                if (c.target >= 0 && c.target < n) c.target = map[c.target];
                r.ricochets.Add(c);
            }
            tape.Add(t);
        }

        // ---------- the replay ----------

        bool replaying, replayPaused, replayAtEnd;
        int rTurn, rFollow;
        float rT, rPrevT, rEnemyCheck;
        static readonly float[] ReplaySpeeds = { 0.25f, 0.5f, 1f, 2f, 4f };
        int rSpeed = 2;
        PilotHeadLook rPilot;
        readonly List<Rect> replayRects = new List<Rect>();
        bool rDragLook;

        bool CanWatchReplay => phase == Phase.Over && tape.Count > 0 && !replaying;

        void StartReplay()
        {
            if (tape.Count == 0) return;
            replaying = true;
            replayPaused = replayAtEnd = false;
            rTurn = 0; rT = rPrevT = 0;
            rSpeed = 2;
            Ooda.Fx.ToonFx.ClearAll();
            for (int s = 0; s < tapeViews.Count; s++) if (tapeViews[s] != null) tapeViews[s].SetSmoke(0);
            Follow(FirstFollow());
            if (view != null) view.BehindPlayer();
            ShowReplayFrame(false);
        }

        /// <summary>Your lead if it has a view, else the first aircraft that has.</summary>
        int FirstFollow()
        {
            for (int s = 0; s < tapeViews.Count; s++) if (tapeViews[s] != null && tapeSide[s] == tapeSide[0] && !tapeBomber[s]) return s;
            for (int s = 0; s < tapeViews.Count; s++) if (tapeViews[s] != null) return s;
            return 0;
        }

        void StopReplay()
        {
            if (!replaying) return;
            replaying = false;
            if (rPilot != null) rPilot.hideHead = false;
            rPilot = null;
            if (view != null)
            {
                view.player = player.transform;
                view.cockpitEye = FindDeep(player.transform, "CockpitCamera");
                view.BehindPlayer();
            }
            // back to where the fight ended
            if (tape.Count > 0)
            {
                var r = tape[tape.Count - 1];
                for (int s = 0; s < tapeViews.Count; s++)
                {
                    var fr = s < r.result.frames.Length ? r.result.frames[s] : null;
                    if (tapeViews[s] == null || fr == null) continue;
                    int e = fr.Length - 1;
                    tapeViews[s].ShowFrame(fr[Mathf.Max(0, e - 1)], fr[e], 1, 0, fr[e].down && r.crashedAfter[s]);
                }
            }
            RacksLive();
            UpdateSmoke(battle.AllSystems(), battle.down);
            FocusNearest();
        }

        void Follow(int s)
        {
            if (s < 0 || s >= tapeViews.Count || tapeViews[s] == null) return;
            rFollow = s;
            if (rPilot != null) rPilot.hideHead = false;
            var t = tapeViews[s].transform;
            rPilot = t.GetComponentInChildren<PilotHeadLook>();
            if (view != null)
            {
                view.player = t;
                view.cockpitEye = FindDeep(t, "CockpitCamera");
                if (view.focus == TurnCamera.Focus.Cockpit && view.cockpitEye == null) view.focus = TurnCamera.Focus.Player;
            }
            rEnemyCheck = 0;
            PickReplayEnemy();
        }

        /// <summary>Tab: the next aircraft to follow, those still flying first.</summary>
        void FollowNext(int dir)
        {
            int m = tapeViews.Count;
            if (m == 0) return;
            int fallback = -1;
            for (int k = 1; k <= m; k++)
            {
                int s = ((rFollow + dir * k) % m + m) % m;
                if (tapeViews[s] == null) continue;
                if (!ReplayDown(s)) { Follow(s); return; }
                if (fallback < 0) fallback = s;
            }
            if (fallback >= 0) Follow(fallback);
        }

        int ReplayFrameNo(TapeTurn t) => Mathf.Min(t.result.FrameCount - 1, Mathf.RoundToInt(rT / (float)t.result.dt));

        bool ReplayDown(int s)
        {
            var t = tape[rTurn];
            var fr = s < t.result.frames.Length ? t.result.frames[s] : null;
            return fr == null || fr[Mathf.Min(fr.Length - 1, ReplayFrameNo(t))].down;
        }

        /// <summary>The camera's other aircraft (for the Enemy and Both views): the nearest one on the other side still flying.</summary>
        void PickReplayEnemy()
        {
            if (view == null || tapeViews[rFollow] == null) return;
            Vector3 me = tapeViews[rFollow].transform.position;
            int best = -1; float bestD = float.PositiveInfinity;
            for (int s = 0; s < tapeViews.Count; s++)
            {
                if (s == rFollow || tapeViews[s] == null || tapeSide[s] == tapeSide[rFollow] || ReplayDown(s)) continue;
                float d = (tapeViews[s].transform.position - me).sqrMagnitude + (tapeBomber[s] ? 1e12f : 0);
                if (d < bestD) { bestD = d; best = s; }
            }
            if (best >= 0) view.enemy = tapeViews[best].transform;
            else if (view.enemy == null) view.enemy = view.player;
        }

        float TapeSec => tape.Sum(t => t.Sec);
        float TapeAt { get { float a = 0; for (int k = 0; k < rTurn; k++) a += tape[k].Sec; return a + rT; } }

        /// <summary>Jump to a moment of the whole fight, s from its start.</summary>
        void SeekTape(float at)
        {
            at = Mathf.Clamp(at, 0, TapeSec);
            rTurn = 0;
            while (rTurn < tape.Count - 1 && at > tape[rTurn].Sec) { at -= tape[rTurn].Sec; rTurn++; }
            rT = rPrevT = Mathf.Min(at, tape[rTurn].Sec);
            replayAtEnd = false;
            ShowReplayFrame(false);
        }

        void StepTurn(int dir)
        {
            rTurn = Mathf.Clamp(rTurn + dir, 0, tape.Count - 1);
            rT = rPrevT = 0;
            replayAtEnd = false;
            ShowReplayFrame(false);
        }

        void ReplayUpdate()
        {
            ReplayInput();
            if (!replaying) return;   // left the replay
            if (!replayPaused && !replayAtEnd)
            {
                var t = tape[rTurn];
                if (rT >= t.Sec - 1e-4f)
                {
                    // the turn's last moment has been shown: on to the next, or the end
                    if (rTurn < tape.Count - 1) { rTurn++; rT = rPrevT = 0; }
                    else replayAtEnd = true;
                }
                else { rPrevT = rT; rT = Mathf.Min(rT + Time.deltaTime * ReplaySpeeds[rSpeed], t.Sec); }
            }
            else rPrevT = rT;
            PortalsBegin();
            lines.Begin();
            ShowReplayFrame(true);
            lines.End();
            rEnemyCheck -= Time.deltaTime;
            if (rEnemyCheck <= 0) { rEnemyCheck = 0.5f; PickReplayEnemy(); }
            if (rPilot != null && view != null) rPilot.hideHead = view.focus == TurnCamera.Focus.Cockpit;
            foreach (var h in holos) if (h != null) h.SetActive(false);
            if (holoTheirs != null) holoTheirs.SetActive(false);
            HideSpread();
            tip.gameObject.SetActive(false);
            UpdateGauge();
        }

        /// <summary>Put everything where the tape has it at rT into turn rTurn; with effects, the flashes and gestures for hits since rPrevT.</summary>
        void ShowReplayFrame(bool effects)
        {
            var t = tape[rTurn];
            var r = t.result;
            int n = r.FrameCount - 1;
            float f = rT / (float)r.dt;
            int i = Mathf.Clamp(Mathf.FloorToInt(f), 0, Mathf.Max(0, n - 1));
            double u = Mathf.Clamp01(f - i);
            int now = Mathf.Min(n, Mathf.RoundToInt(f));
            var down = new bool[tapeViews.Count];
            for (int s = 0; s < tapeViews.Count; s++)
            {
                var fr = s < r.frames.Length ? r.frames[s] : null;
                if (tapeViews[s] == null) continue;
                if (fr == null || fr.Length < 2) continue;
                bool crashedNow = fr[now].down && t.crashedAfter[s] && now == n;
                tapeViews[s].ShowFrame(fr[i], fr[i + 1], u, effects ? Time.deltaTime : 0, crashedNow);
                down[s] = fr[now].down;
            }
            RacksReplay(t, now);
            if (!effects) return;

            for (int s = 0; s < r.frames.Length; s++)
            {
                if (r.frames[s] == null) continue;
                var col = s == rFollow ? ghostColor : tapeBomber[s] ? bomberColor : tapeSide[s] != tapeSide[rFollow] ? projectionColor : wingmanColor;
                DrawPath(r.frames[s], Mathf.Min(now, r.frames[s].Length - 1), col * new Color(1, 1, 1, 0.6f), false);
            }
            DrawExtras(tapeViews, r, now);
            for (int ti = 0; ti < r.traces.Count; ti++)
            {
                var tr = r.traces[ti];
                if (tr.missile) { DrawMissile(tr, ti, now, (float)r.dt); continue; }
                GlowTracer(tr, ti, now, MagnetTint(tr, AfterBounce(tr, now, TapeTracerColor(tr.owner))));
            }

            double t0 = r.startTime;
            var sys = (Systems[])t.sysBefore.Clone();
            foreach (var h in r.hits)
            {
                double ht = h.time - t0;
                bool on = h.target >= 0 && h.target < sys.Length;
                if (on && ht <= rT) Battle.Apply(ref sys[h.target], h);
                if (ht > rPrevT && ht <= rT)
                {
                    Flash(UnityFrame.Pos(h.pos), h.downed);
                    if (on) TapeReact(h, sys[h.target].Lost);
                }
            }
            for (int s = 0; s < tapeViews.Count; s++)
                if (tapeViews[s] != null) tapeViews[s].SetSmoke(down[s] ? 2 : s < sys.Length && sys[s].AnyDamaged ? 1 : 0);
            if (rT > rPrevT) { StrikePlayback(t0 + rPrevT, t0 + rT); RicochetSparks(r, t0 + rPrevT, t0 + rT); }
        }

        Color TapeTracerColor(int owner) => owner == rFollow ? myTracer
            : owner < 0 ? (-1 - owner < battle.flak.Length && battle.flak[-1 - owner].carrier >= 0 ? wingTracer : flakTracer)
            : owner < tapeSide.Count && tapeSide[owner] == tapeSide[rFollow] ? wingTracer : theirTracer;

        void TapeReact(HitEvent h, bool killed)
        {
            PilotControls Pilot(int k) => k >= 0 && k < tapeViews.Count && tapeViews[k] != null ? tapeViews[k].GetComponentInChildren<PilotControls>() : null;
            Pilot(h.target)?.Play(PilotControls.Gesture.Flinch);
            Pilot(h.shooter)?.Play(killed ? PilotControls.Gesture.Cheer : PilotControls.Gesture.ShakeFist);
        }

        void ReplayInput()
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            var mouse = Mouse.current;
            var pointer = Pointer.current;
            float dt = Time.unscaledDeltaTime;
            bool Pressed(Key k) => kb != null && kb[k].wasPressedThisFrame;
            bool shift = kb != null && kb.shiftKey.isPressed;

            if (Pressed(Key.Backspace) || (pad != null && pad.buttonEast.wasPressedThisFrame)) { StopReplay(); return; }
            if (Pressed(Key.Space) || Pressed(Key.Enter) || (pad != null && pad.buttonSouth.wasPressedThisFrame)) TogglePause();
            if (Pressed(Key.LeftArrow) || (pad != null && pad.dpad.left.wasPressedThisFrame)) StepTurn(-1);
            if (Pressed(Key.RightArrow) || (pad != null && pad.dpad.right.wasPressedThisFrame)) StepTurn(1);
            if (Pressed(Key.UpArrow) || Pressed(Key.Equals) || Pressed(Key.NumpadPlus)) rSpeed = Mathf.Min(ReplaySpeeds.Length - 1, rSpeed + 1);
            if (Pressed(Key.DownArrow) || Pressed(Key.Minus) || Pressed(Key.NumpadMinus)) rSpeed = Mathf.Max(0, rSpeed - 1);
            if (Pressed(Key.Home)) SeekTape(0);
            if (Pressed(Key.Tab) || Pressed(Key.N)) FollowNext(shift ? -1 : 1);
            if (pad != null && pad.rightShoulder.wasPressedThisFrame) FollowNext(1);
            if (pad != null && pad.leftShoulder.wasPressedThisFrame) FollowNext(-1);
            if ((Pressed(Key.V) && !AltHeld(kb) || (pad != null && pad.rightStickButton.wasPressedThisFrame)) && view != null) view.NextFocus();
            if (Pressed(Key.C) && view != null) ToggleCockpit();
            CameraKeys(kb, pad);
            if (pad != null && pad.dpad.up.wasPressedThisFrame) rSpeed = Mathf.Min(ReplaySpeeds.Length - 1, rSpeed + 1);
            if (pad != null && pad.dpad.down.wasPressedThisFrame) rSpeed = Mathf.Max(0, rSpeed - 1);

            if (view == null) return;
            if (mouse != null)
            {
                if (mouse.rightButton.isPressed) OrbitOrPan(kb, mouse.delta.ReadValue());
                if (mouse.middleButton.isPressed) view.Pan(mouse.delta.ReadValue());
                float sc = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(sc) > 0.01f) view.Zoom(Mathf.Sign(sc));
            }
            if (pad != null) view.Orbit(pad.rightStick.ReadValue() * new Vector2(120f, -90f) * dt);
            if (pointer != null)
            {
                Vector2 pos = pointer.position.ReadValue();
                Vector2 gui = new Vector2(pos.x, Screen.height - pos.y) / guiScale;
                if (pointer.press.wasPressedThisFrame) { rDragLook = !replayRects.Any(rc => rc.Contains(gui)); if (rDragLook && DoubleTap(pos)) RecenterView(); }
                else if (pointer.press.isPressed && rDragLook && (mouse == null || !mouse.rightButton.isPressed)) OrbitOrPan(kb, pos - lastPointer);
                if (pointer.press.wasReleasedThisFrame) rDragLook = false;
                lastPointer = pos;
            }
        }

        void TogglePause()
        {
            if (replayAtEnd) { SeekTape(0); replayPaused = false; return; }
            replayPaused = !replayPaused;
        }

        void ToggleCockpit()
        {
            if (view.focus == TurnCamera.Focus.Cockpit) view.focus = TurnCamera.Focus.Player;
            else if (view.cockpitEye != null) view.focus = TurnCamera.Focus.Cockpit;
        }

        static readonly string[] ViewNames = { "Chase", "Target", "Both", "Cockpit" };

        string TapeName(int s)
        {
            int i = s < tapePlanes.Count ? System.Array.IndexOf(battle.planes, tapePlanes[s]) : -1;
            string name = i >= 0 ? (battle.flightOf[i] >= 0 ? CallSign(i) : battle.planes[i].numbers.name) : $"Aircraft {s + 1}";
            string kind = tapePlanes[s].numbers.name;
            return name == kind ? name : $"{name} ({kind})";
        }

        void ReplayGUI(float sw, float sh)
        {
            replayRects.Clear();
            var t = tape[rTurn];
            var center = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
            var head = new GUIStyle(title) { alignment = TextAnchor.MiddleCenter };

            // top: what is playing and who the camera is on
            Rect top = new Rect(sw / 2 - 300, 10, 600, 84);
            replayRects.Add(top);
            GUI.Box(top, GUIContent.none);
            string state = replayAtEnd ? "   END" : replayPaused ? "   PAUSED" : "";
            GUI.Label(new Rect(top.x, top.y + 4, top.width, 28), $"REPLAY   Turn {rTurn + 1} of {tape.Count}   {rT:F1} / {t.Sec:F1} s   x{ReplaySpeeds[rSpeed]:0.##}{state}", head);
            var m = tapeViews[rFollow] != null ? tapeViews[rFollow].Model : null;
            string flying = m == null ? "" : ReplayDown(rFollow) ? "   down" : $"   {m.Kias:F0} KIAS   {m.AltFt:F0} ft   {m.g:F1} G";
            GUI.Label(new Rect(top.x, top.y + 32, top.width, 24), $"{(view != null ? ViewNames[(int)view.focus] : "")} view on {TapeName(rFollow)}{flying}", center);
            GUI.Label(new Rect(top.x, top.y + 56, top.width, 22), "Tab next plane   V camera   C cockpit   Space pause   ← → turn   ↑ ↓ speed   Backspace leave", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });

            // bottom: the scrub bar over the whole fight, and the buttons
            float bw = Mathf.Min(sw - 40, 980), bx = (sw - bw) / 2, by = sh - 110;
            Rect bar = new Rect(bx - 10, by - 10, bw + 20, 104);
            replayRects.Add(bar);
            GUI.Box(bar, GUIContent.none);
            float total = TapeSec, at = TapeAt;
            float to = GUI.HorizontalSlider(new Rect(bx, by + 4, bw, 18), at, 0, Mathf.Max(0.01f, total));
            if (Mathf.Abs(to - at) > 1e-3f) SeekTape(to);
            // a tick at each turn's start
            float acc = 0;
            for (int k = 0; k < tape.Count; k++)
            {
                Fill(new Rect(bx + bw * (acc / Mathf.Max(0.01f, total)) - 0.5f, by + 22, 1, 6), new Color(0.55f, 1f, 0.55f, 0.6f));
                acc += tape[k].Sec;
            }
            float y = by + 36, gap = 8;
            string[] names =
            {
                "◀ Turn", replayAtEnd ? "Again" : replayPaused ? "Play" : "Pause", "Turn ▶", $"Speed x{ReplaySpeeds[rSpeed]:0.##}",
                $"View: {(view != null ? ViewNames[(int)view.focus] : "-")}", "Next plane", "Leave replay",
            };
            float w = (bw - gap * (names.Length - 1)) / names.Length;
            for (int k = 0; k < names.Length; k++)
            {
                if (!GUI.Button(new Rect(bx + k * (w + gap), y, w, 40), names[k])) continue;
                switch (k)
                {
                    case 0: StepTurn(-1); break;
                    case 1: TogglePause(); break;
                    case 2: StepTurn(1); break;
                    case 3: rSpeed = (rSpeed + 1) % ReplaySpeeds.Length; break;
                    case 4: if (view != null) view.NextFocus(); break;
                    case 5: FollowNext(1); break;
                    case 6: StopReplay(); break;
                }
            }
            if (notice != null && Time.time < noticeUntil)
                GUI.Label(new Rect(0, sh * 0.22f, sw, 30), notice, new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = 18 });
        }

        // ---------- the end screen's buttons ----------

        void EndButtons(float sw, float y)
        {
            float bw = 200, gap = 14;
            bool tapeOk = tape.Count > 0;
            int count = tapeOk ? 3 : 2;
            float x = (sw - (count * bw + (count - 1) * gap)) / 2;
            Rect r = new Rect(x - 10, y - 8, count * bw + (count - 1) * gap + 20, 60);
            guiRects.Add(r);
            if (tapeOk && GUI.Button(new Rect(x, y, bw, 44), "Watch replay  (W)")) StartReplay();
            if (tapeOk) x += bw + gap;
            if (GUI.Button(new Rect(x, y, bw, 44), tutorial ? "Fly lesson again  (R)" : Dogfighting ? "New fight  (R)" : "Fly again  (Backspace)")) Restart();
            x += bw + gap;
            if (GUI.Button(new Rect(x, y, bw, 44), "Main menu")) GoToMainMenu();
        }

        // ---------- the pause menu ----------

        bool menuOpen;
        float timeScaleBefore = 1;

        void OpenMenu(bool open)
        {
            if (menuOpen == open) return;
            menuOpen = open;
            if (open) { timeScaleBefore = Time.timeScale; Time.timeScale = 0; }
            else Time.timeScale = timeScaleBefore > 0 ? timeScaleBefore : 1;
        }

        /// <summary>Esc opens and closes the menu. True while it is open: the battle waits.</summary>
        bool MenuUpdate()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame && !bot) OpenMenu(!menuOpen);
            return menuOpen;
        }

        void GoToMainMenu()
        {
            OpenMenu(false);
            Time.timeScale = 1;
            if (rPilot != null) rPilot.hideHead = false;
            NetLeave();
            SceneManager.LoadScene(titleScene);
        }

        void OnDestroy()
        {
            if (menuOpen) Time.timeScale = 1;
        }

        void MenuGUI(float sw, float sh)
        {
            GUI.color = new Color(0, 0, 0, 0.55f);
            GUI.DrawTexture(new Rect(0, 0, sw, sh), Texture2D.whiteTexture);
            GUI.color = Color.white;
            float w = 320, h = replaying ? 372 : 316;
            Rect panel = new Rect((sw - w) / 2, (sh - h) / 2, w, h);
            GUI.Box(panel, GUIContent.none);
            var heading = Ooda.UI.BriefingStyle.Heading(GUI.skin.label, 40);
            heading.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(panel.x, panel.y + 12, w, 50), "PAUSED", heading);
            float x = panel.x + 30, y = panel.y + 80, bw = w - 60;
            if (GUI.Button(new Rect(x, y, bw, 44), "Resume  (Esc)")) OpenMenu(false);
            y += 56;
            if (replaying)
            {
                if (GUI.Button(new Rect(x, y, bw, 44), "Leave replay")) { OpenMenu(false); StopReplay(); }
                y += 56;
            }
            if (GUI.Button(new Rect(x, y, bw, 44), "Restart mission")) { OpenMenu(false); Restart(); }
            y += 56;
            if (GUI.Button(new Rect(x, y, bw, 44), "Main menu")) GoToMainMenu();
            y += 56;
            if (GUI.Button(new Rect(x, y, bw, 44), "Quit game"))
            {
                Time.timeScale = 1;
                Application.Quit();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
            }
        }

        /// <summary>Out of the replay without putting the aircraft back (a restart lays everything out again).</summary>
        void StopReplayQuietly()
        {
            replaying = false;
            if (rPilot != null) rPilot.hideHead = false;
            rPilot = null;
        }
    }
}
