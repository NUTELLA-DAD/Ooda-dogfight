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
    /// Flying from the side controls, and the extras. The arrow and its ball are gone (F2 brings them back as a dev view):
    /// you drag the stick in the bottom-right corner to set where the nose goes, back to pull, sideways to roll into it,
    /// past the shaker into the overpull band; WASD and the gamepad move it as before, and the ghost path shows the result.
    /// The rocket (1, pad d-pad right) gives a few seconds of hard shove, then needs about ten turns before it is ready
    /// again; the speed brake (2, d-pad left) is out for the turn and sheds speed fast. N picks which enemy you are
    /// after: the lead ring, the ghosts and the panel follow it.
    /// </summary>
    public partial class TurnBattle
    {
        [Header("Side controls")]
        [Tooltip("Draw the old arrow and its ball (a dev view, F2). Off: steer by dragging the corner stick.")]
        public bool showArrow = false;
        [Tooltip("How far (as a share of the stick picture's height) a drag moves the stick from centre to full over.")]
        public float stickDragSpan = 0.5f;
        public Color rocketFlame = new Color(1f, 0.55f, 0.1f, 0.95f);
        public Color targetBracket = new Color(1f, 0.35f, 0.3f, 0.9f);

        Rect stickRect;            // where the corner stick is drawn (GUI units); zero when it is not
        bool dragStick;
        Vector2 stickDragFrom, stickAtPress;
        int pinnedFocus = -1;      // the enemy picked with N, kept while it flies

        /// <summary>The arrow is drawn (and its ball can be dragged): the dev view, or when there is no room for the corner stick.</summary>
        bool ArrowShown => showArrow || stickRect.width <= 0;

        // ---------- the corner stick ----------

        /// <summary>The plan's arrow as a stick position: up = back (a pull), right = right; the ring at RingTravel, the overpull band beyond.</summary>
        Vector2 StickFromPlan()
        {
            const float ring = Ooda.UI.StickThrottleGauge.RingTravel;
            env.Polar(plan.aim, out double phi, out double theta);
            float r = Mathf.Min(1f, (float)(theta / Math.Max(1e-6, env.MaxAngle(phi))));
            return new Vector2(Mathf.Sin((float)phi), Mathf.Cos((float)phi)) * (r * ring + (float)plan.overpull * (1 - ring));
        }

        /// <summary>Where a stick position points the nose: the inverse of StickFromPlan.</summary>
        Vec3d AimFromStick(Vector2 st)
        {
            const float ring = Ooda.UI.StickThrottleGauge.RingTravel;
            float mag = Mathf.Min(1f, st.magnitude);
            if (mag < 1e-4f) return env.fwd;
            double phi = Math.Atan2(st.x, st.y);
            if (phi < 0) phi += 2 * Math.PI;
            double max = env.MaxAngle(phi);
            double theta = mag <= ring ? max * mag / ring : max + (env.OuterAngle(phi) - max) * (mag - ring) / (1 - ring);
            return env.Direction(phi, theta);
        }

        /// <summary>A press on the corner stick starts a drag (planning with the arrow, not the timeline).</summary>
        bool PressStick(Vector2 gui)
        {
            if (phase != Phase.Planning || plan.HasMarks || stickRect.width <= 0 || !stickRect.Contains(gui)) return false;
            dragStick = true;
            stickDragFrom = gui;
            stickAtPress = StickFromPlan();
            return true;
        }

        /// <summary>Dragging the stick: down pulls back (nose up), sideways rolls into the turn.</summary>
        void DragStick(Vector2 gui)
        {
            if (phase != Phase.Planning || plan.HasMarks) { dragStick = false; return; }
            float span = Mathf.Max(20f, stickRect.height * stickDragSpan);
            Vector2 d = gui - stickDragFrom;
            Vector2 st = stickAtPress + new Vector2(d.x, d.y) / span;
            if (st.magnitude > 1f) st = st.normalized;
            SetAim(AimFromStick(st));
        }

        // ---------- the rocket, the speed brake and the target ----------

        /// <summary>Turns until the rocket can be lit again (0 ready).</summary>
        int RocketTurns(FlightModel m) => m.RocketReady ? 0 : Mathf.Max(1, Mathf.CeilToInt((float)(m.rocketCoolSec / Math.Max(0.1, turnSec))));

        void ToggleRocket()
        {
            var m = battle.planes[0];
            if (!plan.rocket && !m.RocketReady)
            {
                Notice(m.RocketBurning ? "The rocket is still burning." : $"The rocket is recharging: ready in {RocketTurns(m)} turn{(RocketTurns(m) == 1 ? "" : "s")}.");
                return;
            }
            plan.rocket = !plan.rocket;
            if (plan.rocket) plan.brake = false;
            dirty = true;
        }

        void ToggleBrake()
        {
            plan.brake = !plan.brake;
            if (plan.brake) plan.rocket = false;
            dirty = true;
        }

        /// <summary>The planning keys for the extras: 1 rocket, 2 speed brake, N next target, Tab your next plane (pad: d-pad right, left, and the View button for the next plane).</summary>
        void ExtrasInput(Keyboard kb, Gamepad pad)
        {
            bool Pressed(Key k) => kb != null && kb[k].wasPressedThisFrame;
            bool dpadFree = !showMoves;   // the d-pad scrubs the timeline while it is open
            if (Pressed(Key.Digit1) || Pressed(Key.Numpad1) || (pad != null && dpadFree && pad.dpad.right.wasPressedThisFrame)) ToggleRocket();
            if (Pressed(Key.Digit2) || Pressed(Key.Numpad2) || (pad != null && dpadFree && pad.dpad.left.wasPressedThisFrame)) ToggleBrake();
            if (Pressed(Key.Digit3) || Pressed(Key.Numpad3)) ToggleFlares();
            if (Pressed(Key.N)) CycleTarget();
            if (Pressed(Key.Tab) || (pad != null && pad.selectButton.wasPressedThisFrame))
                NextPlane(kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed) ? -1 : 1);
        }

        /// <summary>The enemies still flying, the fighters before the bombers, in order.</summary>
        List<int> LiveEnemies()
        {
            var list = new List<int>();
            for (int pass = 0; pass < 2; pass++)
                for (int i = 1; i < battle.Count; i++)
                    if (battle.side[i] != battle.side[0] && !battle.Out(i) && (battle.routes[i] != null) == (pass == 1)) list.Add(i);
            return list;
        }

        /// <summary>N: go after the next enemy. The lead ring and the ghosts are worked out again for it.</summary>
        void CycleTarget()
        {
            var list = LiveEnemies();
            if (list.Count < 2) { if (list.Count == 1) Notice("Only one enemy left."); return; }
            int at = list.IndexOf(focus);
            pinnedFocus = list[(at + 1) % list.Count];
            FocusNearest();
            Notice($"Target: {EnemyName(focus)}  ({list.IndexOf(focus) + 1} of {list.Count}, N for the next)");
            StartReading();
            dirty = true;
        }

        /// <summary>An aircraft's short name, numbered when its side has more than one of its kind.</summary>
        string EnemyName(int i)
        {
            string name = battle.planes[i].numbers.name.Replace(" G-6", "").Replace("D Mustang", "");
            int same = 0, k = 0;
            for (int j = 0; j < battle.Count; j++)
                if (battle.side[j] == battle.side[i] && battle.planes[j].numbers.name == battle.planes[i].numbers.name) { same++; if (j <= i) k = same; }
            return same > 1 ? $"{name} #{k}" : name;
        }

        // ---------- drawing ----------

        /// <summary>Brackets round the enemy you are after, so you can tell which one it is in a crowd.</summary>
        void DrawTargetBracket()
        {
            if (focus < 0 || focus >= views.Length || views[focus] == null || battle.Out(focus) || LiveEnemies().Count < 2) return;
            Vector3 p = views[focus].transform.position;
            Vector3 right = view != null ? view.Cam.transform.right : Vector3.right, up = view != null ? view.Cam.transform.up : Vector3.up;
            float r = Mathf.Max(12f, W(p) * 24f), l = r * 0.4f;
            foreach (var (sx, sy) in new[] { (1f, 1f), (-1f, 1f), (1f, -1f), (-1f, -1f) })
            {
                Vector3 c = p + right * (sx * r) + up * (sy * r);
                lines.Line(c, c - right * (sx * l), 0.8f, targetBracket);
                lines.Line(c, c - up * (sy * l), 0.8f, targetBracket);
            }
        }

        /// <summary>During playback: a flame behind each aircraft whose rocket is burning, and the brake panels out to its sides.</summary>
        void DrawExtras(int now) => DrawExtras(views, last, now);

        void DrawExtras(IList<TurnAircraftView> vs, TurnResult r, int now)
        {
            DrawFlares(r, now);
            for (int k = 0; k < vs.Count && k < r.frames.Length; k++)
            {
                if (vs[k] == null || r.frames[k] == null) continue;
                var f = r.frames[k][Mathf.Min(now, r.frames[k].Length - 1)];
                if (f.down || (!f.rocket && !f.brake)) continue;
                var t = vs[k].transform;
                Vector3 fwd = UnityFrame.Dir(f.vel.Normalized), up = UnityFrame.Dir(f.up), right = Vector3.Cross(up, fwd);
                Vector3 tail = t.position - fwd * 5f;
                if (f.rocket)
                {
                    float flick = 0.75f + 0.25f * Mathf.Sin(Time.time * 47f + k);
                    lines.Line(tail, tail - fwd * 14f * flick, 2.4f, rocketFlame);
                    lines.Line(tail, tail - fwd * 7f * flick, 1.4f, new Color(1f, 0.95f, 0.7f, 1f));
                }
                if (f.brake)
                {
                    var grey = new Color(0.85f, 0.85f, 0.9f, 0.9f);
                    lines.Line(t.position + right * 1.2f, t.position + right * 1.2f + up * 1.8f - fwd * 0.6f, 0.9f, grey);
                    lines.Line(t.position - right * 1.2f, t.position - right * 1.2f + up * 1.8f - fwd * 0.6f, 0.9f, grey);
                }
            }
        }

        /// <summary>The rocket and the brake in the planning panel's throttle row: x is where they start, w the room they have.</summary>
        void ExtrasRow(float x, float y, float w)
        {
            var m = battle.planes[0];
            float bw = (w - 8) / 2;
            int turns = RocketTurns(m);
            var rs = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = plan.rocket ? FontStyle.Bold : FontStyle.Normal };
            if (plan.rocket) rs.normal.textColor = rs.hover.textColor = rocketFlame;
            string rt = plan.rocket ? "ROCKET: lit (1)" : m.RocketBurning ? "Rocket: burning" : turns == 0 ? "Rocket: ready (1)" : $"Rocket: {turns} turn{(turns == 1 ? "" : "s")}";
            GUI.enabled = plan.rocket || m.RocketReady;
            if (GUI.Button(new Rect(x, y, bw, 24), rt, rs)) ToggleRocket();
            GUI.enabled = true;
            var bs = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = plan.brake ? FontStyle.Bold : FontStyle.Normal };
            if (plan.brake) bs.normal.textColor = bs.hover.textColor = new Color(0.6f, 0.85f, 1f);
            if (GUI.Button(new Rect(x + bw + 8, y, bw, 24), plan.brake ? "BRAKE: out (2)" : "Speed brake (2)", bs)) ToggleBrake();
        }
    }
}
