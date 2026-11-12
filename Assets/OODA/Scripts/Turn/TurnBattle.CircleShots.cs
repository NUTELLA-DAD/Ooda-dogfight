using System;
using System.Collections;
using System.Collections.Generic;
using Ooda.Flight;
using Ooda.Flight.Core;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// -circleshots on the built game's command line: a Spitfire IX against a Bf 109 G-6 over the farmland, set up as a
    /// two-circle turning fight, and pictures of it from above with each fighter's turn circle drawn on the sky. Then it
    /// quits. The pictures go beside the exe as circles_*.bmp.
    ///
    /// Each circle is the type's best sustained turn at that height (AircraftNumbers.SustainedTurnDps: full power, WEP,
    /// no speed or height lost), so the Spitfire's is the smaller, quicker one. Both fly two turns round their circles
    /// first (real turns of the battle, so the bright trails behind them are where they flew), then the shots are taken
    /// in planning: your blue ghost path, the 109's red ghosts (its likely next moves), and on each circle a tick and a
    /// number for every 3 s turn ahead: the moves each has left to get round. The clean shots hide the cockpit and the
    /// panels and keep only the circles' labels; the last one is the game's own screen.
    /// </summary>
    public partial class TurnBattle
    {
        bool circleShots, circleShotsStarted, circleClean;

        struct TurnCircle
        {
            public Vec3d centre;    // ft
            public double radius;   // ft
            public double secs;     // a full circle, s
            public int sense;       // -1 clockwise seen from above, +1 counter-clockwise
            public string name;
            public Color color;
            public int plane;
        }

        readonly List<TurnCircle> circles = new List<TurnCircle>();
        readonly List<Vector3>[] circleTrails = { new List<Vector3>(), new List<Vector3>() };
        string circleCaption;

        /// <summary>Before the battle is made: a Spitfire against a 109 over Town &amp; Farmland, whatever the setup screen last held.</summary>
        void CircleShotsSetUp()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-circleshots") < 0) return;
            circleShots = true;
            mission = Mission.Dogfight;
            dogSetup.ours = new[] { Craft.Spitfire };
            dogSetup.theirs = new[] { Craft.Bf109 };
            dogSetup.oursPilots = new[] { PilotKind.Usual };
            dogSetup.theirsPilots = new[] { PilotKind.Usual };
            dogSetup.FitPilots();
            dogSetup.FitLoadouts();
            botArena = 3;   // Town & Farmland: the fields make a board under them
        }

        void CircleShotsCheck()
        {
            if (!circleShots || circleShotsStarted || battle == null) return;
            circleShotsStarted = true;
            StartCoroutine(CircleShots());
        }

        /// <summary>The sustained circle for a plane at a height: its time round, radius (ft) and speed (KTAS).</summary>
        static (double secs, double radius, double ktas) SustainedCircle(AircraftNumbers n, double altFt)
        {
            double dps = n.SustainedTurnDps(altFt, true, out double kias);
            double ktas = Atmosphere.IasToTasKt(kias, altFt);
            double v = ktas * Atmosphere.KtToFtps, w = dps * Math.PI / 180;
            return (360 / dps, v / w, ktas);
        }

        /// <summary>A plane on its circle at angle theta (radians, counter-clockwise from east), banked into the turn.</summary>
        static void PutOnCircle(FlightModel m, TurnCircle c, double theta, double ktas)
        {
            var radial = new Vec3d(Math.Cos(theta), Math.Sin(theta), 0);
            var pos = c.centre + radial * c.radius;
            // clockwise: the velocity is the radial turned a quarter clockwise; counter-clockwise the other way
            var dir = c.sense < 0 ? new Vec3d(radial.y, -radial.x, 0) : new Vec3d(-radial.y, radial.x, 0);
            double v = ktas * Atmosphere.KtToFtps;
            double g = Math.Sqrt(1 + Math.Pow(v * v / c.radius / Atmosphere.GFtps2, 2));
            double bank = Math.Acos(1 / g);
            m.state = new PointMass.State
            {
                pos = pos,
                vel = dir * v,
                up = Vec3d.Up * Math.Cos(bank) + (-radial) * Math.Sin(bank),   // lift tilted toward the middle
            };
            m.g = m.gAsked = g;
            m.gRate = 0;
            m.rollRateDps = 0;
            m.throttle = 1;
        }

        /// <summary>Where a plane on its circle wants its nose a turn from now: round the circle by a turn's worth.</summary>
        static Vec3d AimRound(FlightModel m, TurnCircle c, double turnSec)
        {
            double ang = 2 * Math.PI * turnSec / c.secs * c.sense;
            var f = m.state.vel; f.z = 0; f = f.Normalized;
            double ca = Math.Cos(ang), sa = Math.Sin(ang);
            return new Vec3d(f.x * ca - f.y * sa, f.x * sa + f.y * ca, 0).Normalized;
        }

        TurnPlan CirclePlan(int i)
        {
            return new TurnPlan { aim = AimRound(battle.planes[i], circles[i], turnSec), throttle = 1, wep = true, missileTarget = -1, radarTarget = -1 };
        }

        IEnumerator CircleShots()
        {
            string dir = System.IO.Path.GetDirectoryName(Application.dataPath);
            IEnumerator Planning() { while (phase != Phase.Planning || reading != null || thinking != null) yield return null; }
            yield return Planning();
            yield return new WaitForSeconds(1f);

            var spit = battle.planes[0]; var bf = battle.planes[1];
            double alt = (spit.state.pos.z + bf.state.pos.z) / 2;
            Vec3d mid = (spit.state.pos + bf.state.pos) * 0.5; mid.z = alt;
            var cs = SustainedCircle(spit.numbers, alt);
            var cb = SustainedCircle(bf.numbers, alt);
            // two circles side by side, overlapping: after a merge both turned toward each other (both clockwise),
            // so they meet again where the circles cross. The 109's circle is the bigger, slower one.
            double gap = 0.86 * (cs.radius + cb.radius);
            circles.Clear();
            circles.Add(new TurnCircle { centre = mid + new Vec3d(-gap / 2, 0, 0), radius = cs.radius, secs = cs.secs, sense = -1, name = "SPITFIRE IX", color = new Color(0.25f, 0.85f, 1f), plane = 0 });
            circles.Add(new TurnCircle { centre = mid + new Vec3d(gap / 2, 0, 0), radius = cb.radius, secs = cb.secs, sense = -1, name = "BF 109 G-6", color = new Color(1f, 0.32f, 0.25f), plane = 1 });
            Debug.Log($"OODA: circles at {alt:F0} ft: Spitfire {cs.secs:F1} s, r {cs.radius:F0} ft at {cs.ktas:F0} KTAS; Bf 109 {cb.secs:F1} s, r {cb.radius:F0} ft at {cb.ktas:F0} KTAS; centres {gap:F0} ft apart");

            // two turns before the picture: start each that far back round its circle (clockwise: back is counter-clockwise)
            const int preTurns = 2;
            double pre = preTurns * turnSec;
            double thS = 150 * Math.PI / 180 + 2 * Math.PI * pre / cs.secs;   // ends upper left, heading for the top crossing
            double thB = -30 * Math.PI / 180 + 2 * Math.PI * pre / cb.secs;   // ends lower right, heading for the bottom one
            PutOnCircle(spit, circles[0], thS, cs.ktas);
            PutOnCircle(bf, circles[1], thB, cb.ktas);
            last = null;
            BeginPlanning();
            yield return Planning();

            float speed = playbackSpeed;
            playbackSpeed = 4f;
            for (int t = 0; t < preTurns; t++)
            {
                var plans = new TurnPlan?[battle.Count];
                for (int i = 0; i < 2; i++) plans[i] = CirclePlan(i);
                plan = plans[0].Value;
                FlyTurn(plans, false);
                for (int i = 0; i < 2; i++)
                    foreach (var f in last.frames[i]) circleTrails[i].Add(UnityFrame.Pos(f.pos));
                yield return Planning();
                for (int i = 0; i < 2; i++)
                {
                    var c = circles[i];
                    var p = battle.planes[i].state.pos; p.z = c.centre.z;
                    Debug.Log($"OODA: circles: turn {t + 1}: {c.name} {(p - c.centre).Length - c.radius:+0;-0} ft off its circle, {battle.planes[i].Kias:F0} KIAS, {battle.planes[i].g:F1} G, {battle.planes[i].AltFt:F0} ft");
                }
            }
            playbackSpeed = speed;

            // the planned next move round the circle: the blue ghost path follows it
            plan.aim = AimRound(spit, circles[0], turnSec);
            plan.throttle = 1; plan.wep = true;
            dirty = true;
            circleCaption = $"TURN {battle.turn + 1}  ·  SPITFIRE IX v BF 109 G-6  ·  EACH TICK IS ONE 3 S MOVE";
            yield return new WaitForSeconds(2f);

            Vector3 c0 = UnityFrame.Pos(circles[0].centre), c1 = UnityFrame.Pos(circles[1].centre);
            Vector3 cm = (c0 + c1) / 2;
            float span = (c1 - c0).magnitude + (float)((cs.radius + cb.radius) * Atmosphere.MPerFt);   // across both circles, m
            var cam = view != null ? view.Cam : null;
            float fov0 = cam != null ? cam.fieldOfView : 60f;
            IEnumerator Shot(string name, Vector3 eye, Vector3 at, float fov, bool clean)
            {
                circleClean = clean;
                Look(eye, at);
                if (cam != null) cam.fieldOfView = fov;
                yield return new WaitForSeconds(1.5f);
                yield return Grab(System.IO.Path.Combine(dir, $"circles_{name}.bmp"));
                Debug.Log($"OODA: circles shot {name}");
            }

            // 1: high and from the south, the whole board in view
            yield return Shot("1_board", cm + new Vector3(0, span * 0.85f, -span * 0.75f), cm + Vector3.down * 20f, 50f, true);
            // 2: straight down
            yield return Shot("2_top", cm + new Vector3(0, span * 1.25f, 0.01f), cm, 50f, true);
            // 3: over the Spitfire's shoulder, high, looking across both circles at the 109
            Vector3 sp = UnityFrame.Pos(spit.state.pos), bp = UnityFrame.Pos(bf.state.pos);
            Vector3 back = (sp - bp); back.y = 0; back.Normalize();
            yield return Shot("3_shoulder", sp + back * span * 0.35f + Vector3.up * span * 0.4f, Vector3.Lerp(sp, bp, 0.55f) + Vector3.down * 30f, 55f, true);
            // 4: lower and closer from the side, the planes big
            Vector3 side = Vector3.Cross(Vector3.up, (bp - sp).normalized);
            yield return Shot("4_low", cm - side * span * 0.55f + Vector3.up * span * 0.22f, cm + Vector3.down * 15f, 45f, true);
            // 5: the board again with the game's own screen (the cockpit, the panels)
            yield return Shot("5_board_game_screen", cm + new Vector3(0, span * 0.85f, -span * 0.75f), cm + Vector3.down * 20f, 50f, false);

            if (cam != null) cam.fieldOfView = fov0;
            if (view != null) view.enabled = true;
            Debug.Log("OODA: circles shots done");
            Application.Quit();
        }

        /// <summary>The circles, their move ticks and the trails, into the frame's lines (between lines.Begin and lines.End).</summary>
        void CircleShotsDraw()
        {
            if (!circleShots || circles.Count == 0 || battle == null) return;
            const int segs = 160;
            for (int k = 0; k < circles.Count; k++)
            {
                var c = circles[k];
                var m = battle.planes[c.plane];
                Vector3 centre = UnityFrame.Pos(c.centre);
                var rim = new List<Vector3>(segs);
                for (int s = 0; s < segs; s++)
                {
                    double a = 2 * Math.PI * s / segs;
                    rim.Add(UnityFrame.Pos(c.centre + new Vec3d(Math.Cos(a), Math.Sin(a), 0) * c.radius));
                }
                // a faint disc under the ring: where the two overlap it reads darker, the ground both can reach
                var pts = new List<Vector3>(segs * 2); var cols = new List<Color>(segs * 2);
                var inner = new Color(c.color.r, c.color.g, c.color.b, 0.05f);
                var outer = new Color(c.color.r, c.color.g, c.color.b, 0.16f);
                for (int s = 0; s < segs; s++) { pts.Add(centre); cols.Add(inner); }
                for (int s = 0; s < segs; s++) { pts.Add(rim[s]); cols.Add(outer); }
                lines.Grid(pts, cols, 2, segs, true);
                lines.Strip(rim, 0.9f, new Color(c.color.r, c.color.g, c.color.b, 0.85f), true);

                // a tick and a move number for each 3 s turn ahead, from where the plane is now round to where it started
                var rel = m.state.pos - c.centre;
                double a0 = Math.Atan2(rel.y, rel.x);
                double per = 2 * Math.PI * turnSec / c.secs;
                int moves = (int)Math.Floor(c.secs / turnSec);
                for (int j = 1; j <= moves; j++)
                {
                    double a = a0 + c.sense * per * j;
                    var radial = new Vec3d(Math.Cos(a), Math.Sin(a), 0);
                    Vector3 pIn = UnityFrame.Pos(c.centre + radial * (c.radius * 0.93));
                    Vector3 pOut = UnityFrame.Pos(c.centre + radial * (c.radius * 1.07));
                    lines.Line(pIn, pOut, 1.1f, new Color(1f, 1f, 1f, 0.9f));
                }
                // the plane's spoke: a line from the middle to it
                lines.Line(centre, UnityFrame.Pos(c.centre + new Vec3d(Math.Cos(a0), Math.Sin(a0), 0) * c.radius), 0.5f, new Color(c.color.r, c.color.g, c.color.b, 0.5f));

                // the trail it flew, brightening toward the plane
                var tr = circleTrails[k];
                if (tr.Count > 2)
                {
                    var thin = new List<Vector3>(tr.Count / 3 + 2);
                    for (int s = 0; s < tr.Count; s += 3) thin.Add(tr[s]);
                    thin.Add(UnityFrame.Pos(m.state.pos));
                    lines.Strip(thin, 1.6f, new Color(1f, 1f, 1f, 0.1f), false, new Color(1f, 1f, 1f, 0.95f));
                }
            }
        }

        GUIStyle circleBig, circleSmall, circleNum, circleCap;

        /// <summary>The labels (in every circle shot). True: the clean shots, where the game's own screen is left out.</summary>
        bool CircleShotsGui()
        {
            if (!circleShots || circles.Count == 0 || view == null || view.Cam == null || view.enabled) return false;   // only while a shot's camera holds
            var cam = view.Cam;
            float h = Screen.height;
            if (circleBig == null)
            {
                circleBig = new GUIStyle(GUI.skin.label) { font = Ooda.UI.BriefingStyle.Stencil, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                circleSmall = new GUIStyle(circleBig) { font = null, fontStyle = FontStyle.Bold };
                circleNum = new GUIStyle(circleSmall);
                circleCap = new GUIStyle(circleBig) { alignment = TextAnchor.LowerLeft };
            }
            circleBig.fontSize = Mathf.RoundToInt(h / 26f);
            circleSmall.fontSize = Mathf.RoundToInt(h / 50f);
            circleNum.fontSize = Mathf.RoundToInt(h / 46f);
            circleCap.fontSize = Mathf.RoundToInt(h / 42f);
            var keep = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;

            void Text(Vector2 at, string s, GUIStyle st, Color col, float w = 900, float hh = 120)
            {
                var r = new Rect(at.x - w / 2, at.y - hh / 2, w, hh);
                st.normal.textColor = new Color(0, 0, 0, 0.75f);
                GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), s, st);
                st.normal.textColor = col;
                GUI.Label(r, s, st);
            }
            bool OnScreen(Vector3 world, out Vector2 gui)
            {
                var sp = cam.WorldToScreenPoint(world);
                gui = new Vector2(sp.x, h - sp.y);
                return sp.z > 0;
            }

            foreach (var c in circles)
            {
                var m = battle.planes[c.plane];
                // the name past the far side of the circle from the other one
                double outward = c.centre.x < (circles[0].centre.x + circles[1].centre.x) / 2 ? -1 : 1;
                var labelAt = UnityFrame.Pos(c.centre + new Vec3d(outward * c.radius * 1.0, -c.radius * 1.12, 0));
                if (OnScreen(labelAt, out var g))
                {
                    Text(g, c.name, circleBig, c.color);
                    Text(g + new Vector2(0, circleBig.fontSize * 1.05f), $"{c.secs:F1} s circle  ·  {c.radius:F0} ft radius  ·  {c.secs / turnSec:F1} moves round", circleSmall, Color.white);
                }
                var rel = m.state.pos - c.centre;
                double a0 = Math.Atan2(rel.y, rel.x), per = 2 * Math.PI * turnSec / c.secs;
                int moves = (int)Math.Floor(c.secs / turnSec);
                for (int j = 1; j <= moves; j++)
                {
                    double a = a0 + c.sense * per * j;
                    var at = UnityFrame.Pos(c.centre + new Vec3d(Math.Cos(a), Math.Sin(a), 0) * (c.radius * 1.17));
                    if (OnScreen(at, out var gp)) Text(gp, j.ToString(), circleNum, new Color(1f, 0.95f, 0.75f), 80, 60);
                }
            }
            if (circleCaption != null)
                Text(new Vector2(Screen.width / 2f, h - h * 0.06f), circleCaption, circleCap, new Color(1f, 0.92f, 0.55f), Screen.width, 120);
            GUI.matrix = keep;
            return circleClean;
        }
    }
}
