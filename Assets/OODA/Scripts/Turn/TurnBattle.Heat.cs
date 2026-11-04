using System;
using System.Collections.Generic;
using Ooda.Flight;
using Ooda.Flight.Core;
using Ooda.Turn.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace Ooda.Turn
{
    /// <summary>
    /// The IR view and the flares.
    ///
    /// I (any time, or the IR button) turns the screen into a heat-seeker's thermal picture: the world a dim grey, the sky
    /// cold and dark (Fx/IrView.shader over the camera's opaque texture), and every heat source a white-hot glow, as bright
    /// as the missile's seeker sees it from where the camera is (Turn/Core Heat, the same numbers it locks with): each
    /// engine by its throttle, WEP and the rocket, much brighter from astern than from ahead; a jet's afterburner blazing
    /// with a plume; a missile's motor; and the flares, the hottest things in the sky. Cool down (throttle back, out of the
    /// burner) and you fade, and the seeker has to come closer to lock.
    ///
    /// The flares go by themselves: a pair at each heat-seeker that closes inside 3,000 ft. 3 (or the Flares button) holds
    /// them for the turn. A red warning says when a missile is tracking you. In playback they are bright sparks falling
    /// away behind the plane with a short smoke tail.
    /// </summary>
    public partial class TurnBattle
    {
        [Header("IR view")]
        [Tooltip("The thermal view is on (I).")]
        public bool irView;
        [Tooltip("How big a heat glow of heat 1 is, m, close up; it grows with the distance so a far one still shows.")]
        public float irGlowSize = 2.2f;
        public Color flareColor = new Color(1f, 0.93f, 0.75f, 1f);
        public Color missileWarnColor = new Color(1f, 0.25f, 0.2f, 1f);

        GameObject irScreen, irGlowObj;
        Mesh irGlowMesh;
        Material irGlowMat;
        Camera irCam;
        bool irCamAskedColor;
        Texture2D irDot;
        readonly List<Vector3> irVerts = new List<Vector3>(1024);
        readonly List<Color> irCols = new List<Color>(1024);
        readonly List<Vector2> irUvs = new List<Vector2>(1024);
        readonly List<int> irTris = new List<int>(1536);
        readonly List<Vector3> flareTail = new List<Vector3>(16);

        void ToggleIr()
        {
            irView = !irView;
            Notice(irView ? "IR view: the heat the seeker sees. Throttle back to fade. I to leave." : "IR view off.");
        }

        void ToggleFlares()
        {
            plan.holdFlares = !plan.holdFlares;
            if (battle.flares[0] <= 0) Notice("No flares left.");
            else Notice(plan.holdFlares ? "Flares held this turn: none go, even with a missile on you." : "Flares on auto: a pair goes at each missile that closes in.");
            dirty = true;
        }

        // ---------- every frame, after the cameras have moved ----------

        void LateUpdate()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.iKey.wasPressedThisFrame && !menuOpen && !briefing) ToggleIr();
            Camera cam = view != null ? view.Cam : Camera.main;
            bool on = irView && cam != null && battle != null && !briefing;
            IrCamera(on ? cam : null);
            if (irScreen != null) irScreen.SetActive(on);
            if (irGlowObj != null) irGlowObj.SetActive(on);
            if (!on) return;
            MakeIr(cam);
            BuildGlows(cam);
        }

        /// <summary>The camera keeps the opaque texture (the IR shader reads it) only while the view is on.</summary>
        void IrCamera(Camera cam)
        {
            if (irCam == cam) return;
            if (irCam != null && irCamAskedColor)
            {
                var old = irCam.GetComponent<UniversalAdditionalCameraData>();
                if (old != null) old.requiresColorTexture = false;
            }
            irCam = cam; irCamAskedColor = false;
            if (cam == null) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null && !data.requiresColorTexture) { data.requiresColorTexture = true; irCamAskedColor = true; }
        }

        void MakeIr(Camera cam)
        {
            if (irScreen == null)
            {
                irScreen = new GameObject("IrScreen");
                var mf = irScreen.AddComponent<MeshFilter>();
                var mesh = new Mesh { name = "IrScreen" };
                mesh.SetVertices(new List<Vector3> { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) });
                mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);   // the shader puts it over the whole screen: never culled
                mf.sharedMesh = mesh;
                var r = irScreen.AddComponent<MeshRenderer>();
                var mat = Resources.Load<Material>("IrView");
                if (mat == null)
                {
                    // without the IR shader: a dark see-through veil, so the glows still stand out
                    mat = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.02f, 0.05f, 0.03f, 0.7f), renderQueue = 2990 };
                }
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            // in front of the camera (the IR shader ignores where it is; the fallback veil needs to be there)
            if (irScreen.transform.parent != cam.transform)
            {
                irScreen.transform.SetParent(cam.transform, false);
                irScreen.transform.localRotation = Quaternion.identity;
            }
            float d = cam.nearClipPlane + 0.05f, h = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * d * 1.2f;
            irScreen.transform.localPosition = new Vector3(0, 0, d);
            irScreen.transform.localScale = new Vector3(h * cam.aspect, h, 1);

            if (irGlowObj == null)
            {
                irGlowObj = new GameObject("IrGlow");
                irGlowObj.transform.SetParent(transform, false);
                irGlowMesh = new Mesh { name = "IrGlow" };
                irGlowMesh.MarkDynamic();
                irGlowObj.AddComponent<MeshFilter>().sharedMesh = irGlowMesh;
                var r = irGlowObj.AddComponent<MeshRenderer>();
                irDot = SoftDot(64);
                irGlowMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = irDot, renderQueue = 3100 };
                r.sharedMaterial = irGlowMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            irGlowObj.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            irGlowObj.transform.localScale = Vector3.one;
        }

        /// <summary>A white dot, solid in the middle and soft to nothing at the edge.</summary>
        static Texture2D SoftDot(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "IrDot" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1, r2 = dx * dx + dy * dy;
                    float a = r2 >= 1 ? 0 : Mathf.Exp(-r2 * 3.2f) * (1 - r2);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255));
                }
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }

        // ---------- the glows ----------

        Vector3 irCamPos, irRight, irUp;

        /// <summary>One glow: a dot facing the camera, pulled a little toward it so the aircraft's own skin doesn't hide it.</summary>
        void Glow(Vector3 p, float r, float a)
        {
            if (r <= 0 || a <= 0.01f) return;
            Vector3 toCam = irCamPos - p;
            float dist = toCam.magnitude;
            if (dist < 0.5f) return;
            p += toCam * (Mathf.Min(r * 1.2f, dist * 0.5f) / dist);
            int b = irVerts.Count;
            Vector3 x = irRight * r, y = irUp * r;
            irVerts.Add(p - x - y); irVerts.Add(p + x - y); irVerts.Add(p + x + y); irVerts.Add(p - x + y);
            var c = new Color(1f, 1f, 0.97f, Mathf.Clamp01(a));
            for (int k = 0; k < 4; k++) irCols.Add(c);
            irUvs.Add(new Vector2(0, 0)); irUvs.Add(new Vector2(1, 0)); irUvs.Add(new Vector2(1, 1)); irUvs.Add(new Vector2(0, 1));
            irTris.Add(b); irTris.Add(b + 1); irTris.Add(b + 2); irTris.Add(b); irTris.Add(b + 2); irTris.Add(b + 3);
        }

        /// <summary>A heat source of this heat (seeker units) at p: a soft halo and a hot core, both growing with the heat and the range.</summary>
        void HeatGlow(Vector3 p, double heat)
        {
            if (heat <= 0.01) return;
            float s = Mathf.Sqrt((float)heat);
            float dist = Vector3.Distance(irCamPos, p);
            float r = irGlowSize * s + dist * 0.004f * s;
            Glow(p, r * 2.2f, 0.25f + 0.2f * Mathf.Min(1.5f, s));   // the halo
            Glow(p, r * 0.7f, 0.65f + 0.35f * Mathf.Min(1f, s));     // the core
        }

        void BuildGlows(Camera cam)
        {
            irVerts.Clear(); irCols.Clear(); irUvs.Clear(); irTris.Clear();
            irCamPos = cam.transform.position; irRight = cam.transform.right; irUp = cam.transform.up;

            // the moment on screen: a frame of the replay, of the turn playing back, or the live battle
            TurnResult r = null; int now = 0;
            if (replaying && rTurn >= 0 && rTurn < tape.Count) { r = tape[rTurn].result; now = Mathf.Clamp(Mathf.RoundToInt(rT / (float)r.dt), 0, r.FrameCount - 1); }
            else if (phase == Phase.Playing && last != null) { r = last; now = Mathf.Clamp(Mathf.RoundToInt(playT / (float)last.dt), 0, last.FrameCount - 1); }
            else if (phase == Phase.Over && last != null) { r = last; now = last.FrameCount - 1; }

            // Aircraft
            if (replaying)
                for (int s = 0; s < tapeViews.Count; s++)
                {
                    int bi = Array.IndexOf(battle.planes, tapePlanes[s]);
                    bool down = r != null && s < r.frames.Length && r.frames[s] != null && r.frames[s][Mathf.Min(now, r.frames[s].Length - 1)].down;
                    bool rocket = r != null && s < r.frames.Length && r.frames[s] != null && r.frames[s][Mathf.Min(now, r.frames[s].Length - 1)].rocket;
                    bool ram = r != null && s < r.frames.Length && r.frames[s] != null && r.frames[s][Mathf.Min(now, r.frames[s].Length - 1)].ram;
                    AircraftGlow(tapeViews[s], bi >= 0 ? battle.combat[bi] : null, down, rocket, ram);
                }
            else
                for (int k = 0; k < views.Length && k < battle.Count; k++)
                {
                    bool live = r == null || k >= r.frames.Length || r.frames[k] == null;
                    var f = live ? default : r.frames[k][Mathf.Min(now, r.frames[k].Length - 1)];
                    AircraftGlow(views[k], battle.combat[k], live ? battle.down[k] : f.down, live ? battle.planes[k].RocketBurning : f.rocket, !live && f.ram);
                }

            // Missiles and flares
            if (r != null)
            {
                foreach (var tr in r.traces)
                {
                    if (!tr.missile) continue;
                    var p = tr.At(now);
                    if (p != null) HeatGlow(UnityFrame.Pos(p.Value), Heat.Missile(tr.age0 + (now - tr.startFrame) * r.dt, 2.5));
                }
                foreach (var tr in r.flares)
                {
                    var p = tr.At(now);
                    if (p != null) HeatGlow(UnityFrame.Pos(p.Value), Heat.Flare(tr.age0 + (now - tr.startFrame) * r.dt));
                }
            }
            else
                foreach (var b in battle.bullets)
                {
                    if (b is Missile ms) HeatGlow(UnityFrame.Pos(ms.pos), Heat.Missile(ms.age, ms.burnSec));
                    else if (b is Core.Flare) HeatGlow(UnityFrame.Pos(b.pos), Heat.Flare(b.age));
                }

            irGlowMesh.Clear();
            irGlowMesh.SetVertices(irVerts);
            irGlowMesh.SetColors(irCols);
            irGlowMesh.SetUVs(0, irUvs);
            irGlowMesh.SetTriangles(irTris, 0);
            irGlowMesh.bounds = new Bounds(irCamPos, Vector3.one * 2e5f);
        }

        /// <summary>
        /// Where an aircraft's engines are, in its own frame (m: x right, y up, z forward), by type: the exhausts of a
        /// single piston engine by the nose, the P-38's two out on the booms, the B-17's four along the wing, the F-4's
        /// two nozzles at the tail.
        /// </summary>
        static Vector3[] EngineSpots(string name)
        {
            if (name == null) return OneNose;
            if (name.Contains("P-38")) return TwinBooms;
            if (name.Contains("B-17")) return FourWing;
            if (name.Contains("F-4") || name.Contains("Phantom")) return JetTail;
            return OneNose;
        }
        static readonly Vector3[] OneNose = { new Vector3(0.45f, 0.2f, 2.4f), new Vector3(-0.45f, 0.2f, 2.4f) };
        static readonly Vector3[] TwinBooms = { new Vector3(2.4f, 0.2f, 1.6f), new Vector3(-2.4f, 0.2f, 1.6f) };
        static readonly Vector3[] FourWing = { new Vector3(3.8f, 0.4f, 2.4f), new Vector3(-3.8f, 0.4f, 2.4f), new Vector3(7.8f, 0.4f, 1.6f), new Vector3(-7.8f, 0.4f, 1.6f) };
        static readonly Vector3[] JetTail = { new Vector3(0.65f, 0f, -8.2f), new Vector3(-0.65f, 0f, -8.2f) };

        void AircraftGlow(TurnAircraftView v, CombatNumbers c, bool down, bool rocket, bool ram = false)
        {
            if (v == null || c == null || !v.isActiveAndEnabled || v.Crashed || v.Model == null) return;
            var t = v.transform;
            if ((irCamPos - t.position).sqrMagnitude < 12f * 12f) return;   // the one you sit in (the cockpit view): its glow would fill the screen
            Vector3 toCam = (irCamPos - t.position).normalized;
            Vec3d fwd = UnityFrame.Dir(t.forward), eye = UnityFrame.Dir(toCam);
            double aspect = Heat.Aspect(fwd, eye);
            // the airframe is warm all over: a faint glow the size of the plane, so it shows as a shape
            float span = v.Model.numbers != null && v.Model.numbers.name != null && v.Model.numbers.name.Contains("B-17") ? 16f : 6f;
            Glow(t.position, span + Vector3.Distance(irCamPos, t.position) * 0.003f, ram ? 0.7f : 0.16f);
            if (down) { HeatGlow(t.position, 1.5); return; }   // burning
            if (ram) HeatGlow(t.position, Specials.BladeHeat);   // the heat blade: red hot all over, from every side
            double heat = Heat.Engine(c, v.Model.throttle, v.Wep, rocket) * aspect;
            var spots = EngineSpots(v.Model.numbers?.name);
            double each = heat / spots.Length;
            foreach (var s in spots) HeatGlow(t.TransformPoint(s), each);
            // the hot exhaust trailing behind: a plume, long for a jet in afterburner or the rocket
            float plume = (v.Wep && spots == JetTail) || rocket ? 1f : 0.35f;
            Vector3 back = -t.forward, from = spots == JetTail ? t.TransformPoint(new Vector3(0, 0, -8.2f)) : t.position;
            for (int k = 1; k <= 4; k++)
                HeatGlow(from + back * (k * 3.5f * plume * (spots == JetTail ? 1.6f : 1f)), heat * plume * (1 - k * 0.2f) * 0.6);
        }

        // ---------- flares in playback ----------

        /// <summary>The flares at frame now: a bright flickering spark falling away with a short smoke tail, gone when it burns out.</summary>
        void DrawFlares(TurnResult r, int now)
        {
            if (r == null || r.flares == null) return;
            foreach (var tr in r.flares)
            {
                var head = tr.At(now);
                if (head == null) continue;
                double age = tr.age0 + (now - tr.startFrame) * r.dt;
                float burn = (float)(Heat.Flare(age) / Heat.FlareHeat);
                if (burn <= 0) continue;
                Vector3 h = UnityFrame.Pos(head.Value);
                flareTail.Clear();
                for (int f = now; f >= tr.startFrame && f > now - 30; f -= 3) { var p = tr.At(f); if (p != null) flareTail.Add(UnityFrame.Pos(p.Value)); }
                if (flareTail.Count >= 2) lines.Strip(flareTail, 1.1f, new Color(0.95f, 0.93f, 0.9f, 0.55f * burn), false, new Color(0.9f, 0.9f, 0.9f, 0f));
                float flick = 0.8f + 0.2f * Mathf.Sin(Time.time * 53f + tr.startFrame * 1.7f);
                var c = flareColor; c.a = Mathf.Clamp01(burn * 1.3f);
                Vector3 rgt = view != null ? view.Cam.transform.right : Vector3.right;
                lines.Line(h - rgt * 0.6f * flick, h + rgt * 0.6f * flick, 2.6f * flick, c);
                lines.Line(h - rgt * 0.25f, h + rgt * 0.25f, 1.4f, new Color(1f, 1f, 1f, c.a));
            }
        }

        // ---------- on screen ----------

        /// <summary>The IR view's mark, and the warning when a missile is tracking you, with what your flares will do.</summary>
        void IrGui(float sw, float sh)
        {
            if (battle == null || briefing) return;
            if (irView)
            {
                var s = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
                s.normal.textColor = new Color(0.92f, 1f, 0.94f, 0.95f);
                GUI.Label(new Rect(sw / 2 - 160, 8, 320, 26), "IR  WHITE HOT   (I to leave)", s);
            }
            if (replaying || phase != Phase.Planning) return;
            // a seeker on you now
            double nearest = double.PositiveInfinity;
            foreach (var b in battle.bullets)
                if (b is Missile ms && ms.target == 0) nearest = Math.Min(nearest, (ms.pos - battle.planes[0].state.pos).Length);
            if (double.IsInfinity(nearest)) return;
            var w = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
            w.normal.textColor = Mathf.Repeat(Time.unscaledTime, 0.8f) < 0.5f ? missileWarnColor : new Color(1f, 0.85f, 0.3f);
            string what = battle.flares[0] <= 0 ? "no flares left: break hard across it, or throttle back"
                        : plan.holdFlares ? "flares HELD (3 to let them go)"
                        : $"flares on auto, {battle.flares[0]} left (3 holds them)";
            GUI.Label(new Rect(sw / 2 - 330, irView ? 34 : 8, 660, 28), $"MISSILE ON YOU  {nearest:N0} ft:  {what}", w);
        }

        /// <summary>The IR and flare buttons, in a row over the planning panel's left end.</summary>
        void HeatButtons(float x, float y)
        {
            Rect row = new Rect(x, y, 262, 24);
            guiRects.Add(row);
            var ir = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = irView ? FontStyle.Bold : FontStyle.Normal };
            if (GUI.Button(new Rect(x, y, 110, 24), irView ? "IR: ON (I)" : "IR view (I)", ir)) ToggleIr();
            int left = battle.flares[0];
            var fs = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = plan.holdFlares ? FontStyle.Bold : FontStyle.Normal };
            if (plan.holdFlares) fs.normal.textColor = fs.hover.textColor = new Color(1f, 0.6f, 0.3f);
            GUI.enabled = left > 0;
            string ft = left <= 0 ? "Flares: none" : plan.holdFlares ? $"Flares {left}: HELD (3)" : $"Flares {left}: auto (3)";
            if (GUI.Button(new Rect(x + 118, y, 144, 24), ft, fs)) ToggleFlares();
            GUI.enabled = true;
        }
    }
}
