using System;
using System.Collections.Generic;
using System.Linq;
using Ooda.Flight;
using Ooda.Flight.Core;
using Ooda.Pilot;
using Ooda.Turn.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ooda.Turn
{
    /// <summary>
    /// The turn-based dogfight. Each turn both pilots plan at once: the player drags the arrow tip (where the
    /// nose should point when the turn ends) inside the reach shell, sets the throttle and a gun burst on the
    /// 3 s timeline, and sees the ghost path, the tracers and the enemy's "keeps doing what it's doing"
    /// projection. Fly turn: the AI plans without seeing the player's plan, the turn engine flies both,
    /// and the turn plays back. A kill wins; being shot down or hitting the ground loses.
    ///
    /// Mouse or touch: drag the yellow tip; drag anywhere else (or right-drag) to look around; wheel zooms;
    /// drag on the timeline to set the burst. Keyboard: WASD or arrows move the arrow, Shift/Ctrl throttle,
    /// Space WEP, F fire at predicted hits, B burst presets, Enter fly, R replay, P projection, V camera,
    /// X straight ahead, Backspace restart, H help. Gamepad: left stick arrow, triggers throttle, A fly,
    /// X burst presets, right bumper fire at predicted hits, Y projection, B replay, right stick look,
    /// left bumper straight ahead, right stick press camera, Start restart.
    /// </summary>
    public class TurnBattle : MonoBehaviour
    {
        public TurnAircraftView player, enemy;
        public TurnCamera view;
        public Terrain terrain;
        public PilotHeadLook playerPilot, enemyPilot;

        [Header("Start (head-on)")]
        [Tooltip("Middle of the fight, metres (Unity x east, z north; height is ignored).")]
        public Vector3 startCentre = new Vector3(0, 0, -6000);
        public float startAltFt = 5000, startKias = 300, startGapM = 3000;

        [Header("Turns")]
        public float turnSec = Scenarios.TurnSec;
        public float playbackSpeed = 1f;
        public bool showProjection = true;
        public bool showHelp = true;

        [Header("Drawing")]
        public float arrowLength = 150f;   // metres
        public Color shellColor = new Color(1f, 1f, 1f, 0.45f);
        public Color arrowColor = new Color(1f, 0.85f, 0.1f, 1f);
        public Color ghostColor = new Color(0.2f, 0.9f, 1f, 0.9f);
        public Color projectionColor = new Color(1f, 0.3f, 0.25f, 0.8f);
        public Color myTracer = new Color(1f, 0.75f, 0.2f, 0.9f);
        public Color theirTracer = new Color(1f, 0.35f, 0.5f, 0.9f);

        enum Phase { Planning, Playing, Over }

        Phase phase, phaseAfterPlayback;
        Battle battle;
        EnemyAi ai;
        Envelope env;
        TurnPlan plan;
        bool dirty;
        TurnResult preview, last;
        double[] healthBefore = new double[2];
        float playT, prevPlayT;
        string outcome, notice;
        float noticeUntil;

        LinePool lines;
        Transform tip;
        readonly List<(Transform t, float born)> flashes = new List<(Transform, float)>();
        Material flashMat, tipMat;

        bool dragArrow, dragLook, dragBurst;
        Vector2 lastPointer;
        float guiScale = 1;
        Rect burstRect;
        readonly List<Rect> guiRects = new List<Rect>();

        static readonly (float a, float b)[] BurstPresets = { (0, 0), (0, 1), (1, 2), (2, 3), (0, 3) };
        int burstPreset;

        // ---------- set up ----------

        void Start()
        {
            lines = new LinePool(transform, "Lines");
            tipMat = new Material(Shader.Find("Sprites/Default")) { color = arrowColor };
            flashMat = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 0.9f, 0.4f, 1f) };
            var t = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(t.GetComponent<Collider>());
            t.name = "ArrowTip";
            t.GetComponent<Renderer>().sharedMaterial = tipMat;
            t.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tip = t.transform;
            tip.SetParent(transform, false);
            if (terrain == null) terrain = Terrain.activeTerrain;
            if (view != null) { view.player = player.transform; view.enemy = enemy.transform; }
            if (playerPilot != null) playerPilot.target = enemy.transform;
            if (enemyPilot != null) enemyPilot.target = player.transform;
            Restart();
        }

        double GroundFt(double xFt, double yFt)
        {
            if (terrain == null) return 0;
            var w = new Vector3((float)(xFt * Atmosphere.MPerFt), 0, (float)(yFt * Atmosphere.MPerFt));
            float h = terrain.SampleHeight(w) + terrain.transform.position.y;
            return Math.Max(0, h) * Atmosphere.FtPerM;
        }

        public void Restart()
        {
            battle = Scenarios.HeadOn(UnityFrame.Pos(startCentre), startAltFt, startKias, startGapM);
            battle.groundFt = GroundFt;
            ai = new EnemyAi();
            player.Bind(battle.planes[0]);
            enemy.Bind(battle.planes[1]);
            plan = new TurnPlan { throttle = 1 };
            last = null;
            outcome = null;
            foreach (var f in flashes) Destroy(f.t.gameObject);
            flashes.Clear();
            BeginPlanning();
            if (view != null) view.BehindPlayer();
        }

        void BeginPlanning()
        {
            phase = Phase.Planning;
            var m = battle.planes[0];
            env = Envelope.Compute(m, turnSec);
            plan.aim = m.Forward;
            plan.burstStart = plan.burstEnd = 0;
            plan.wep = false;
            burstPreset = 0;
            dirty = true;
            player.ShowLive(battle.planes[0]);
            enemy.ShowLive(battle.planes[1]);
            UpdateSmoke(battle.health[0], battle.health[1], battle.down[0], battle.down[1]);
        }

        void Commit()
        {
            if (phase != Phase.Planning) return;
            var theirs = ai.Plan(battle, turnSec);
            healthBefore[0] = battle.health[0];
            healthBefore[1] = battle.health[1];
            last = battle.Resolve(plan, theirs, turnSec, true);
            phaseAfterPlayback = battle.Over ? Phase.Over : Phase.Planning;
            if (battle.Over) outcome = Outcome();
            newTurn = true;
            StartPlayback();
        }

        void StartPlayback()
        {
            if (last == null) return;
            playT = prevPlayT = 0;
            phase = Phase.Playing;
        }

        void Replay()
        {
            if (last == null || phase == Phase.Playing) return;
            phaseAfterPlayback = phase;
            newTurn = false;
            StartPlayback();
        }

        string Outcome()
        {
            bool me = battle.down[0], them = battle.down[1];
            if (me && them) return "Both aircraft went down. A draw.";
            if (them) return battle.crashed[1] ? "The Spitfire flew into the ground. You win!" : "Spitfire shot down. You win!";
            return battle.crashed[0] ? "You flew into the ground." : "You were shot down.";
        }

        void Notice(string s) { notice = s; noticeUntil = Time.time + 3f; }

        // ---------- every frame ----------

        void Update()
        {
            HandleInput();
            lines.Begin();
            if (phase == Phase.Playing) Playback();
            else if (phase == Phase.Planning) DrawPlanning();
            else DrawLastPaths(last != null ? last.FrameCount - 1 : 0);
            lines.End();
            tip.gameObject.SetActive(phase == Phase.Planning);
            FadeFlashes();
            if (playerPilot != null && view != null) playerPilot.hideHead = view.focus == TurnCamera.Focus.Cockpit;
        }

        float W(Vector3 at) => view == null ? 0.5f : Mathf.Max(0.05f, Vector3.Distance(view.transform.position, at) * 0.0025f);

        Vector3 PlayerPos => player.transform.position;

        void HandleInput()
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            var mouse = Mouse.current;
            var pointer = Pointer.current;
            float dt = Time.unscaledDeltaTime;

            bool Pressed(Key k) => kb != null && kb[k].wasPressedThisFrame;
            if (Pressed(Key.H)) showHelp = !showHelp;
            if (Pressed(Key.Backspace) || (pad != null && pad.startButton.wasPressedThisFrame)) Restart();
            if (Pressed(Key.R) || (pad != null && pad.buttonEast.wasPressedThisFrame)) Replay();
            if (Pressed(Key.P) || (pad != null && pad.buttonNorth.wasPressedThisFrame)) showProjection = !showProjection;
            if ((Pressed(Key.V) || (pad != null && pad.rightStickButton.wasPressedThisFrame)) && view != null) view.NextFocus();

            // Looking around: right-drag, a drag off the arrow, the right stick; the wheel zooms
            if (view != null)
            {
                if (mouse != null)
                {
                    if (mouse.rightButton.isPressed) view.Orbit(mouse.delta.ReadValue() * 0.25f);
                    float sc = mouse.scroll.ReadValue().y;
                    if (Mathf.Abs(sc) > 0.01f) view.Zoom(Mathf.Sign(sc));
                }
                if (pad != null)
                {
                    view.Orbit(pad.rightStick.ReadValue() * new Vector2(120f, -90f) * dt);
                    if (pad.dpad.up.isPressed) view.Zoom(4f * dt);
                    if (pad.dpad.down.isPressed) view.Zoom(-4f * dt);
                }
                if (kb != null)
                {
                    if (kb.equalsKey.isPressed || kb.numpadPlusKey.isPressed) view.Zoom(4f * dt);
                    if (kb.minusKey.isPressed || kb.numpadMinusKey.isPressed) view.Zoom(-4f * dt);
                }
            }

            // Pointer (mouse or touch): the arrow tip, the burst timeline, or looking around
            if (pointer != null)
            {
                Vector2 pos = pointer.position.ReadValue();
                Vector2 gui = new Vector2(pos.x, Screen.height - pos.y) / guiScale;
                if (pointer.press.wasPressedThisFrame)
                {
                    if (phase == Phase.Planning && burstRect.Contains(gui)) { dragBurst = true; SetBurstFromBar(gui.x, true); }
                    else if (guiRects.Any(r => r.Contains(gui))) { }
                    else if (phase == Phase.Planning && NearTip(pos)) dragArrow = true;
                    else dragLook = true;
                }
                else if (pointer.press.isPressed)
                {
                    if (dragArrow) DragArrow(pos);
                    else if (dragBurst) SetBurstFromBar(gui.x, false);
                    else if (dragLook && view != null && (mouse == null || !mouse.rightButton.isPressed)) view.Orbit((pos - lastPointer) * 0.25f);
                }
                if (pointer.press.wasReleasedThisFrame) { dragArrow = dragLook = false; if (dragBurst) { dragBurst = false; TidyBurst(); } }
                lastPointer = pos;
            }

            if (phase != Phase.Planning) return;

            if (Pressed(Key.Enter) || Pressed(Key.NumpadEnter) || (pad != null && pad.buttonSouth.wasPressedThisFrame)) { Commit(); return; }

            // The arrow by keys or stick: nudged in the start frame (up = pull, right = right wing)
            Vector2 move = Vector2.zero;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1;
            }
            if (pad != null) move += pad.leftStick.ReadValue();
            if (move.sqrMagnitude > 0.01f)
            {
                double rate = 45 * Math.PI / 180 * dt;
                Vec3d a = plan.aim + env.up * (move.y * rate) + env.right * (move.x * rate);
                plan.aim = env.Clamp(a);
                dirty = true;
            }
            if (Pressed(Key.X) || (pad != null && pad.leftShoulder.wasPressedThisFrame)) { plan.aim = env.fwd; dirty = true; }

            // Throttle and WEP
            float thr = 0;
            if (kb != null) { if (kb.leftShiftKey.isPressed) thr += 1; if (kb.leftCtrlKey.isPressed) thr -= 1; }
            if (pad != null) thr += pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
            if (Mathf.Abs(thr) > 0.05f) { plan.throttle = Math.Max(0, Math.Min(1, plan.throttle + thr * 0.6 * dt)); dirty = true; }
            if (Pressed(Key.Space)) { plan.wep = !plan.wep; dirty = true; }

            // Guns
            if (Pressed(Key.B) || (pad != null && pad.buttonWest.wasPressedThisFrame))
            {
                burstPreset = (burstPreset + 1) % BurstPresets.Length;
                plan.burstStart = BurstPresets[burstPreset].a;
                plan.burstEnd = BurstPresets[burstPreset].b;
                dirty = true;
            }
            if (Pressed(Key.F) || (pad != null && pad.rightShoulder.wasPressedThisFrame)) FireAtPredicted();

            if (dirty) RunPreview();
        }

        // ---------- planning ----------

        Vector3 TipPos => PlayerPos + UnityFrame.Dir(plan.aim) * arrowLength;

        bool NearTip(Vector2 screen)
        {
            if (view == null) return false;
            Vector3 s = view.Cam.WorldToScreenPoint(TipPos);
            float reach = Touchscreen.current != null ? 80f : 45f;
            return s.z > 0 && Vector2.Distance(screen, s) < reach * Mathf.Max(1, guiScale);
        }

        void DragArrow(Vector2 screen)
        {
            Ray r = view.Cam.ScreenPointToRay(screen);
            Vector3 c = PlayerPos;
            Vector3 oc = r.origin - c;
            float b = Vector3.Dot(oc, r.direction), q = oc.sqrMagnitude - arrowLength * arrowLength;
            float disc = b * b - q;
            Vector3 point;
            if (disc >= 0)
            {
                float s = Mathf.Sqrt(disc);
                Vector3 p1 = r.GetPoint(-b - s), p2 = r.GetPoint(-b + s);
                point = (p1 - TipPos).sqrMagnitude < (p2 - TipPos).sqrMagnitude ? p1 : p2;
            }
            else point = r.GetPoint(-b);
            Vector3 dir = (point - c).normalized;
            plan.aim = env.Clamp(UnityFrame.Dir(dir));
            dirty = true;
        }

        void SetBurstFromBar(float guiX, bool start)
        {
            float t = Mathf.Clamp01((guiX - burstRect.x) / burstRect.width) * turnSec;
            t = Mathf.Round(t / 0.05f) * 0.05f;
            if (start) { plan.burstStart = t; plan.burstEnd = t; }
            else plan.burstEnd = t;
            dirty = true;
        }

        void TidyBurst()
        {
            double a = Math.Min(plan.burstStart, plan.burstEnd), b = Math.Max(plan.burstStart, plan.burstEnd);
            if (b - a < 0.1) { a = 0; b = 0; }    // a tap clears the burst
            plan.burstStart = a; plan.burstEnd = b;
            dirty = true;
        }

        void FireAtPredicted()
        {
            if (preview == null) return;
            var full = plan; full.burstStart = 0; full.burstEnd = turnSec;
            var sim = battle.Clone();
            sim.dispersion = false;
            sim.health[1] = double.PositiveInfinity;
            sim.bullets.Clear();
            var r = sim.Resolve(full, TurnPlan.Holding(), turnSec, false);
            var times = r.hits.Where(h => h.shooter == 0).Select(h => h.fireTime - battle.time).ToList();
            if (times.Count == 0) { Notice("No hits predicted on this path if the Spitfire holds its turn."); plan.burstStart = plan.burstEnd = 0; }
            else { plan.burstStart = Math.Max(0, times.Min() - 0.15); plan.burstEnd = Math.Min(turnSec, times.Max() + 0.15); }
            dirty = true;
            RunPreview();
        }

        void RunPreview()
        {
            var sim = battle.Clone();
            sim.dispersion = false;
            preview = sim.Resolve(plan, TurnPlan.Holding(), turnSec, true);
            dirty = false;
        }

        void DrawPlanning()
        {
            var me = battle.planes[0];
            player.ShowLive(me);
            enemy.ShowLive(battle.planes[1]);
            Vector3 c = PlayerPos;
            float w = W(c);

            // The reach shell: its rim and four spokes, and where the nose is now
            var rim = new List<Vector3>();
            for (int k = 0; k < 48; k++)
            {
                double phi = 2 * Math.PI * k / 48;
                rim.Add(c + UnityFrame.Dir(env.Direction(phi, env.MaxAngle(phi))) * arrowLength);
            }
            lines.Strip(rim, w * 0.6f, shellColor, true);
            for (int k = 0; k < 4; k++) lines.Line(c, rim[k * 12], w * 0.3f, shellColor * new Color(1, 1, 1, 0.5f));
            lines.Line(c, c + UnityFrame.Dir(env.fwd) * arrowLength, w * 0.3f, new Color(1, 1, 1, 0.3f));

            // The arrow
            Vector3 t = TipPos;
            lines.Line(c, t, w * 1.2f, arrowColor);
            tip.position = t;
            tip.localScale = Vector3.one * W(t) * 8f;

            if (preview == null) return;
            DrawPath(preview.frames[0], preview.FrameCount - 1, ghostColor, true);
            if (showProjection) DrawPath(preview.frames[1], preview.FrameCount - 1, projectionColor, true);
            foreach (var tr in preview.traces)
            {
                if (tr.owner == 1 && !showProjection) continue;
                var pts = new List<Vector3>();
                for (int i = 0; i < tr.points.Count; i += 2) pts.Add(UnityFrame.Pos(tr.points[i]));
                pts.Add(UnityFrame.Pos(tr.points[tr.points.Count - 1]));
                var col = tr.owner == 0 ? myTracer : theirTracer;
                lines.Strip(pts, W(pts[0]) * 0.35f, col, false, new Color(col.r, col.g, col.b, 0.1f));
            }
        }

        /// <summary>A path through frames up to an index, with a small wings-and-fin mark at its end.</summary>
        void DrawPath(AircraftFrame[] frames, int upTo, Color color, bool endMark)
        {
            var pts = new List<Vector3>();
            for (int i = 0; i <= upTo; i += 3) pts.Add(UnityFrame.Pos(frames[i].pos));
            pts.Add(UnityFrame.Pos(frames[upTo].pos));
            lines.Strip(pts, W(pts[pts.Count - 1]) * 0.8f, new Color(color.r, color.g, color.b, color.a * 0.35f), false, color);
            if (!endMark) return;
            var f = frames[upTo];
            Vector3 p = UnityFrame.Pos(f.pos), fwd = UnityFrame.Dir(f.vel.Normalized), up = UnityFrame.Dir(f.up);
            Vector3 right = Vector3.Cross(up, fwd);
            float s = Mathf.Max(6f, W(p) * 25f);
            float lw = W(p) * 1.2f;
            lines.Line(p - right * s, p + right * s, lw, color);
            lines.Line(p - fwd * s * 0.8f, p + fwd * s * 0.5f, lw, color);
            lines.Line(p - fwd * s * 0.8f, p - fwd * s * 0.8f + up * s * 0.45f, lw, color);
        }

        // ---------- playback ----------

        void Playback()
        {
            float speed = playbackSpeed * (Keyboard.current != null && Keyboard.current.tabKey.isPressed ? 3f : 1f);
            prevPlayT = playT;
            playT = Mathf.Min(playT + Time.deltaTime * speed, turnSec);
            int n = last.FrameCount - 1;
            float f = playT / (float)last.dt;
            int i = Mathf.Min(n - 1, Mathf.FloorToInt(f));
            double u = Math.Min(1, f - i);
            int now = Mathf.Min(n, Mathf.RoundToInt(f));
            bool c0 = last.frames[0][now].down && battle.crashed[0] && now == n;
            player.ShowFrame(last.frames[0][i], last.frames[0][i + 1], u, Time.deltaTime, c0);
            enemy.ShowFrame(last.frames[1][i], last.frames[1][i + 1], u, Time.deltaTime, false);

            DrawLastPaths(now);
            foreach (var tr in last.traces)
            {
                var p1 = tr.At(now);
                if (p1 == null) continue;
                var p0 = tr.At(now - 3) ?? tr.points[0];
                Vector3 a = UnityFrame.Pos(p0), b = UnityFrame.Pos(p1.Value);
                lines.Line(a, b, W(b) * 0.6f, tr.owner == 0 ? myTracer : theirTracer);
            }

            // Hits: a flash where they land, and the damage so far shown as smoke
            double t0 = last.startTime;
            double[] hp = { healthBefore[0], healthBefore[1] };
            foreach (var h in last.hits)
            {
                double ht = h.time - t0;
                if (ht <= playT) hp[h.target] -= battle.combat[h.shooter].damage;
                if (ht > prevPlayT && ht <= playT) Flash(UnityFrame.Pos(h.pos));
            }
            UpdateSmoke(hp[0], hp[1], last.frames[0][now].down, last.frames[1][now].down);

            if (playT >= turnSec - 1e-4f)
            {
                if (phaseAfterPlayback == Phase.Over) phase = Phase.Over;
                else if (newTurn) BeginPlanning();
                else phase = Phase.Planning;   // back to the plan in hand after a replay
                newTurn = false;
            }
        }

        bool newTurn;

        void DrawLastPaths(int upTo)
        {
            if (last == null) return;
            DrawPath(last.frames[0], upTo, ghostColor * new Color(1, 1, 1, 0.6f), false);
            DrawPath(last.frames[1], upTo, projectionColor * new Color(1, 1, 1, 0.6f), false);
        }

        readonly double[] shownHp = new double[2];

        void UpdateSmoke(double hp0, double hp1, bool down0, bool down1)
        {
            shownHp[0] = hp0; shownHp[1] = hp1;
            player.SetSmoke(down0 ? 2 : hp0 < battle.combat[0].health * 0.5 ? 1 : 0);
            enemy.SetSmoke(down1 ? 2 : hp1 < battle.combat[1].health * 0.5 ? 1 : 0);
        }

        void Flash(Vector3 at)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = flashMat;
            go.transform.position = at;
            flashes.Add((go.transform, Time.time));
        }

        void FadeFlashes()
        {
            for (int i = flashes.Count - 1; i >= 0; i--)
            {
                float age = Time.time - flashes[i].born;
                if (age > 0.35f) { Destroy(flashes[i].t.gameObject); flashes.RemoveAt(i); continue; }
                flashes[i].t.localScale = Vector3.one * Mathf.Lerp(4f, 0.5f, age / 0.35f);
            }
        }

        // ---------- screen ----------

        GUIStyle label, big, small, title;

        void OnGUI()
        {
            if (battle == null) return;
            guiScale = Mathf.Clamp(Screen.height / 900f, 1f, 2.5f);
            GUI.matrix = Matrix4x4.Scale(Vector3.one * guiScale);
            float sw = Screen.width / guiScale, sh = Screen.height / guiScale;
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { fontSize = 16 };
                label.normal.textColor = new Color(0.85f, 1f, 0.85f);
                big = new GUIStyle(label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                big.normal.textColor = new Color(1f, 0.9f, 0.4f);
                small = new GUIStyle(label) { fontSize = 13 };
                title = new GUIStyle(label) { fontSize = 20, fontStyle = FontStyle.Bold };
            }
            guiRects.Clear();
            // What is on screen now (during playback, not the end of the turn)
            var me = player.Model;
            var them = enemy.Model;

            // Left: the player's aircraft
            Rect panel = new Rect(10, 10, 300, 170);
            guiRects.Add(panel);
            GUI.Box(panel, GUIContent.none);
            GUI.Label(new Rect(20, 14, 290, 26), $"P-51D   Turn {battle.turn + (phase == Phase.Planning ? 1 : 0)}", title);
            GUI.Label(new Rect(20, 42, 290, 22), $"{me.Kias:F0} KIAS   {me.AltFt:F0} ft   {me.g:F1} G", label);
            Bar(new Rect(20, 68, 270, 14), shownHp[0] / battle.combat[0].health, new Color(0.3f, 0.9f, 0.3f), "Airframe");
            Bar(new Rect(20, 88, 270, 14), battle.ammo[0] / battle.combat[0].ammoSec, new Color(0.9f, 0.75f, 0.2f), $"Ammo {battle.ammo[0]:F1} s");
            Vec3d los = them.state.pos - me.state.pos;
            double range = los.Length;
            double ata = Envelope.Angle(me.Forward, los) * 180 / Math.PI;
            double aa = Envelope.Angle(them.Forward, los) * 180 / Math.PI;
            GUI.Label(new Rect(20, 108, 290, 22), $"Spitfire {range:F0} ft, {ata:F0}° off your nose", label);
            GUI.Label(new Rect(20, 128, 290, 22), $"You are {aa:F0}° off its tail ({(aa < 45 ? "behind it" : aa > 135 ? "in front of it" : "beside it")})", label);
            Bar(new Rect(20, 152, 270, 14), shownHp[1] / battle.combat[1].health, new Color(0.9f, 0.3f, 0.3f), "Spitfire");

            // Right: buttons
            float bx = sw - 190, by = 10;
            Rect buttons = new Rect(bx - 10, 0, 200, 300);
            guiRects.Add(buttons);
            bool planning = phase == Phase.Planning;
            GUI.enabled = planning;
            if (GUI.Button(new Rect(bx, by, 180, 44), "Fly turn  (Enter)")) Commit();
            GUI.enabled = true;
            by += 52;
            GUI.enabled = last != null && phase != Phase.Playing;
            if (GUI.Button(new Rect(bx, by, 180, 30), "Replay last turn  (R)")) Replay();
            GUI.enabled = true;
            by += 36;
            if (GUI.Button(new Rect(bx, by, 180, 30), showProjection ? "Projection: on  (P)" : "Projection: off  (P)")) { showProjection = !showProjection; }
            by += 36;
            if (GUI.Button(new Rect(bx, by, 180, 30), $"View: {(view != null ? view.focus.ToString() : "-")}  (V)") && view != null) view.NextFocus();
            by += 36;
            if (GUI.Button(new Rect(bx, by, 180, 30), "Restart  (Backspace)")) Restart();
            by += 36;
            if (GUI.Button(new Rect(bx, by, 180, 30), showHelp ? "Hide help  (H)" : "Help  (H)")) showHelp = !showHelp;

            if (planning) PlanningPanel(sw, sh);
            if (phase == Phase.Playing) GUI.Label(new Rect(0, sh - 60, sw, 30), $"Turn playing  {playT:F1} / {turnSec:F0} s   (hold Tab to speed up)", new GUIStyle(label) { alignment = TextAnchor.MiddleCenter });
            if (phase == Phase.Over && outcome != null)
            {
                GUI.Label(new Rect(0, sh * 0.3f, sw, 50), outcome, big);
                GUI.Label(new Rect(0, sh * 0.3f + 50, sw, 30), "Restart for another go, or Replay to watch it again.", new GUIStyle(label) { alignment = TextAnchor.MiddleCenter });
            }
            if (notice != null && Time.time < noticeUntil)
                GUI.Label(new Rect(0, sh * 0.22f, sw, 30), notice, new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = 18 });

            if (showHelp)
            {
                Rect help = new Rect(10, 190, 420, 250);
                GUI.Box(help, GUIContent.none);
                guiRects.Add(help);
                GUI.Label(new Rect(20, 196, 405, 240),
                    "Plan where your nose points when the 3 s turn ends.\n" +
                    "Drag the yellow tip inside the white ring (how far you can turn).\n" +
                    "Blue: your path. Red: the Spitfire if it keeps doing what it's doing.\n" +
                    "Orange: your rounds. They arc and take time: lead the target.\n" +
                    "Drag on the timeline to fire; F fires where hits are predicted.\n\n" +
                    "Keys: WASD arrow, Shift/Ctrl throttle, Space WEP, B bursts,\n" +
                    "Enter fly, R replay, P projection, V view, X straight, H help.\n" +
                    "Right-drag or drag away from the tip to look; wheel zooms.\n" +
                    "Pad: left stick arrow, triggers throttle, A fly, X bursts,\n" +
                    "RB fire at predicted hits, Y projection, B replay, right stick look.", small);
            }
        }

        void PlanningPanel(float sw, float sh)
        {
            float pw = Mathf.Min(640, sw - 40), px = (sw - pw) / 2, py = sh - 150;
            Rect panel = new Rect(px - 10, py - 10, pw + 20, 150);
            GUI.Box(panel, GUIContent.none);
            guiRects.Add(panel);

            // Throttle and WEP
            GUI.Label(new Rect(px, py, 110, 22), $"Throttle {plan.throttle * 100:F0}%", label);
            float thr = GUI.HorizontalSlider(new Rect(px + 120, py + 6, pw - 260, 20), (float)plan.throttle, 0f, 1f);
            if (Math.Abs(thr - plan.throttle) > 1e-4) { plan.throttle = thr; dirty = true; }
            bool wep = GUI.Toggle(new Rect(px + pw - 130, py, 130, 22), plan.wep, " WEP (Space)");
            if (wep != plan.wep) { plan.wep = wep; dirty = true; }

            // The burst timeline
            GUI.Label(new Rect(px, py + 28, pw, 22), plan.burstEnd > plan.burstStart
                ? $"Guns: fire {plan.burstStart:F2} to {plan.burstEnd:F2} s   (drag on the bar, tap to clear)"
                : "Guns: hold fire   (drag on the bar to set a burst)", label);
            burstRect = new Rect(px, py + 54, pw, 28);
            GUI.Box(burstRect, GUIContent.none);
            if (plan.burstEnd != plan.burstStart)
            {
                double a = Math.Min(plan.burstStart, plan.burstEnd), b = Math.Max(plan.burstStart, plan.burstEnd);
                Fill(new Rect(burstRect.x + (float)(a / turnSec) * pw, burstRect.y + 3, (float)((b - a) / turnSec) * pw, burstRect.height - 6), new Color(1f, 0.6f, 0.1f, 0.85f));
            }
            int predicted = 0;
            if (preview != null)
                foreach (var h in preview.hits)
                {
                    if (h.shooter == 0) predicted++;
                    float x = burstRect.x + (float)((h.fireTime - preview.startTime) / turnSec) * pw;
                    Fill(new Rect(x - 1, burstRect.y, 2, burstRect.height), h.shooter == 0 ? Color.white : new Color(1f, 0.3f, 0.3f));
                }
            for (int s = 0; s <= (int)turnSec; s++)
                GUI.Label(new Rect(burstRect.x + s / turnSec * pw - 8, burstRect.yMax, 40, 20), $"{s}s", small);
            int theirs = preview == null ? 0 : preview.hits.Count(h => h.shooter == 1);
            GUI.Label(new Rect(px, py + 104, pw - 140, 22), $"Predicted if the Spitfire holds: you hit {predicted}, it hits you {theirs}", label);
            if (GUI.Button(new Rect(px + pw - 130, py + 102, 130, 26), "Fire at hits (F)")) FireAtPredicted();
        }

        void Bar(Rect r, double fraction, Color c, string text)
        {
            Fill(r, new Color(0, 0, 0, 0.5f));
            Fill(new Rect(r.x, r.y, r.width * Mathf.Clamp01((float)fraction), r.height), c);
            GUI.Label(new Rect(r.x + 4, r.y - 4, r.width, 22), text, small);
        }

        static Texture2D white;
        static void Fill(Rect r, Color c)
        {
            if (white == null) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); }
            var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, white); GUI.color = old;
        }
    }
}
