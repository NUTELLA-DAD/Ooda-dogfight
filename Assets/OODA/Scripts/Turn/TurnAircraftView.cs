using Ooda.Flight;
using Ooda.Flight.Core;
using Ooda.Turn.Core;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// Puts an aircraft model where the turn engine says it is: from the live battle while planning, from the
    /// recorded frames during playback. Keeps a display FlightModel so the propeller and the pilot's head
    /// (which follow IFlightView) see the G, the roll and the throttle. Smokes when hit hard, and when down.
    /// </summary>
    public class TurnAircraftView : MonoBehaviour, IFlightView
    {
        public FlightModel Model { get; private set; }
        public bool Crashed { get; private set; }
        /// <summary>War emergency power in: a jet's afterburner lit (for its JetBurner).</summary>
        public bool Wep { get; private set; }
        /// <summary>The pilot's G stress, 0 to 1 (1 blacked out: G-LOC), for the screen to grey.</summary>
        public float GreyOut { get; private set; }

        [Tooltip("Where the smoke comes from (the engine); empty uses the aircraft's centre.")]
        public Transform smokePoint;

        int smokeLevel, puffSeed;
        Vector3 lastPuff;
        float lastPuffT;
        bool hasLastPuff, wasCrashed;
        Vector3 lastUp;

        public void Bind(FlightModel model)
        {
            Model = model.Clone();
            Crashed = wasCrashed = false;
            SetSmoke(0);
            ShowLive(model);
        }

        /// <summary>Show the aircraft as it is now (now: the battle time, for a bailed-out pilot's parachute).</summary>
        public void ShowLive(FlightModel m, double now = double.NaN)
        {
            Model.state = m.state; Model.g = m.g; Model.throttle = m.throttle; Model.rollRateDps = 0;
            Wep = false;
            GreyOut = (float)m.GreyOut;
            var s = m.systems;
            ShowBailout(s.bailed && !double.IsNaN(now), s.bailPos, s.bailVel, now - s.bailAt);
            Place(m.state.pos, m.Forward, m.state.up, m.AlphaDeg);   // the nose (in a tail slide it isn't along the path)
        }

        /// <summary>Show a moment between two recorded frames (u from 0 to 1).</summary>
        public void ShowFrame(AircraftFrame a, AircraftFrame b, double u, float dt, bool crashed)
        {
            Vec3d pos = a.pos + (b.pos - a.pos) * u;
            Vec3d vel = a.vel + (b.vel - a.vel) * u;
            Vec3d nose = (a.Nose + (b.Nose - a.Nose) * u).Normalized;
            Vec3d up = (a.up + (b.up - a.up) * u).Normalized;
            double alpha = a.alphaDeg + (b.alphaDeg - a.alphaDeg) * u;
            Model.state = new PointMass.State { pos = pos, vel = vel, up = up };
            Model.g = a.g + (b.g - a.g) * u;
            Model.throttle = b.throttle;
            Wep = b.wep;
            GreyOut = (float)(a.greyOut + (b.greyOut - a.greyOut) * u);
            double t = a.time + (b.time - a.time) * u;
            ShowBailout(b.bailed && t >= b.bailAt, b.bailPos, b.bailVel, t - b.bailAt);
            Vector3 upU = UnityFrame.Dir(up);
            if (dt > 0 && lastUp != Vector3.zero)
                Model.rollRateDps = Vector3.SignedAngle(lastUp, upU, UnityFrame.Dir(vel)) / dt * -1;
            lastUp = upU;
            Crashed = crashed;
            Place(pos, nose, up, alpha);
        }

        Renderer[] rends;
        bool[] wasEnabled;
        bool hidden;

        /// <summary>Show or hide the whole model, its pilot with it (stealth). Each part comes back as it was when hidden.</summary>
        public void SetVisible(bool on)
        {
            if (on != hidden) return;   // already so
            if (rends == null) rends = Body();
            if (!on)
            {
                wasEnabled = new bool[rends.Length];
                for (int k = 0; k < rends.Length; k++) if (rends[k] != null) { wasEnabled[k] = rends[k].enabled; rends[k].enabled = false; }
            }
            else if (wasEnabled != null)
                for (int k = 0; k < rends.Length; k++) if (rends[k] != null) rends[k].enabled = wasEnabled[k];
            hidden = !on;
            if (rack != null) rack.Show(rackLoaded, rackLeft, hidden);
        }

        // ---------- the missiles under the wings ----------

        MissileRack rack;
        int rackLoaded, rackLeft;

        /// <summary>The heat-seekers on the rails: loaded at the start of the fight, left now (MissileRack; hidden with the rest of it under stealth).</summary>
        public void ShowMissiles(int loaded, int left)
        {
            rackLoaded = loaded; rackLeft = left;
            if (rack == null)
            {
                if (loaded <= 0) return;   // nothing to hang (the B-17, a missile-less lesson): no rack made
                rack = GetComponent<MissileRack>();
                if (rack == null) rack = gameObject.AddComponent<MissileRack>();
            }
            rack.Show(loaded, left, hidden);
        }

        /// <summary>The model's own renderers (not the red-hot shell).</summary>
        Renderer[] Body()
        {
            var all = GetComponentsInChildren<Renderer>(true);
            var list = new System.Collections.Generic.List<Renderer>(all.Length);
            var stores = transform.Find(MissileRack.RootName);
            foreach (var r in all)
                if (r != null && (shell == null || !r.transform.IsChildOf(shell.transform)) && (stores == null || !r.transform.IsChildOf(stores))) list.Add(r);
            return list.ToArray();
        }

        // ---------- bailing out: the pilot leaves the cockpit under a parachute ----------

        SkinnedMeshRenderer[] pilotRends;
        bool[] pilotWas;
        bool seatEmpty;
        GameObject chute;
        Transform canopy, chuteMan;
        LineRenderer[] chuteLines;
        static Material chuteCloth, chuteBody, chuteCord;

        /// <summary>
        /// The pilot has bailed out (t seconds ago, from p0 at v0, ft): the seat is empty and he hangs under a parachute
        /// (Systems.ChuteAt), the canopy blossoming over the first second and a half, until he lands. Off: back in the seat.
        /// </summary>
        void ShowBailout(bool on, Vec3d p0, Vec3d v0, double t)
        {
            if (on && !seatEmpty)
            {
                // the pilot is the skinned mesh (only the roster's chosen one is on): note which, then empty the seat
                pilotRends = GetComponentsInChildren<SkinnedMeshRenderer>(true);
                pilotWas = new bool[pilotRends.Length];
                for (int k = 0; k < pilotRends.Length; k++) if (pilotRends[k] != null) { pilotWas[k] = pilotRends[k].enabled; pilotRends[k].enabled = false; }
                seatEmpty = true;
            }
            else if (!on && seatEmpty)
            {
                for (int k = 0; k < pilotRends.Length; k++) if (pilotRends[k] != null) pilotRends[k].enabled = pilotWas[k] && !hidden;
                seatEmpty = false;
            }
            if (!on) { if (chute != null) chute.SetActive(false); return; }
            if (chute == null) BuildChute();
            chute.SetActive(true);
            Vector3 at = UnityFrame.Pos(Systems.ChuteAt(p0, v0, t));
            bool landed = false;
            if (Physics.Raycast(at + Vector3.up * 2000f, Vector3.down, out var hit, 2000f + 1.5f) && hit.point.y > at.y - 0.5f) { at = hit.point; landed = true; }
            chute.transform.position = at;
            float open = Mathf.Clamp01(((float)t - 0.4f) / 1.1f);
            float sway = landed ? 0 : Mathf.Sin((float)t * 1.3f) * 6f;
            chute.transform.rotation = Quaternion.Euler(sway, (float)(t * 4.0) % 360f, sway * 0.5f);
            canopy.localScale = landed ? new Vector3(7f, 0.4f, 7f) : new Vector3(0.6f + 6.4f * open, 0.6f + 2.9f * open, 0.6f + 6.4f * open);
            canopy.localPosition = landed ? new Vector3(3.5f, 0.2f, 0) : new Vector3(0, 1.2f + 6.5f * open, 0);
            for (int k = 0; k < chuteLines.Length; k++)
            {
                float a = k * Mathf.PI / 2 + Mathf.PI / 4;
                var rim = canopy.localPosition + new Vector3(Mathf.Cos(a) * canopy.localScale.x * 0.45f, 0, Mathf.Sin(a) * canopy.localScale.z * 0.45f);
                chuteLines[k].SetPosition(0, chute.transform.TransformPoint(new Vector3(0, 1.5f, 0)));
                chuteLines[k].SetPosition(1, chute.transform.TransformPoint(rim));
                chuteLines[k].enabled = !landed;
            }
            chuteMan.localRotation = landed ? Quaternion.Euler(90, 0, 0) : Quaternion.identity;
            chuteMan.localPosition = landed ? new Vector3(0, 0.3f, 0) : new Vector3(0, 0.9f, 0);
        }

        static Material ChuteMaterial(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh);
            m.SetColor("_BaseColor", c);
            m.color = c;
            return m;
        }

        void BuildChute()
        {
            if (chuteCloth == null)
            {
                chuteCloth = ChuteMaterial(new Color(0.96f, 0.93f, 0.84f));
                chuteBody = ChuteMaterial(new Color(0.42f, 0.36f, 0.24f));
                chuteCord = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.85f, 0.85f, 0.8f) };
            }
            chute = new GameObject("Parachute");   // not under the aircraft: it falls apart from it
            var c = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(c.GetComponent<Collider>());
            c.name = "Canopy";
            c.transform.SetParent(chute.transform, false);
            c.GetComponent<Renderer>().sharedMaterial = chuteCloth;
            canopy = c.transform;
            var man = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(man.GetComponent<Collider>());
            man.name = "Pilot";
            man.transform.SetParent(chute.transform, false);
            man.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
            man.GetComponent<Renderer>().sharedMaterial = chuteBody;
            chuteMan = man.transform;
            chuteLines = new LineRenderer[4];
            for (int k = 0; k < 4; k++)
            {
                var g = new GameObject("Cord");
                g.transform.SetParent(chute.transform, false);
                var lr = g.AddComponent<LineRenderer>();
                lr.positionCount = 2; lr.widthMultiplier = 0.05f; lr.useWorldSpace = true;
                lr.sharedMaterial = chuteCord;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                chuteLines[k] = lr;
            }
        }

        void OnDestroy()
        {
            if (chute != null) Destroy(chute);
        }

        // ---------- the heat blade: the whole plane glowing red ----------

        GameObject shell;
        Material shellMat;
        static Material shellTemplate;

        /// <summary>
        /// The heat blade: the whole airframe glows red hot, a glowing copy of each of its meshes laid over it (additive, so it
        /// lights up whatever colour is under it), pulsing with glow (0 to 1). Off hides the copies again.
        /// </summary>
        public void SetRedHot(bool on, float glow = 1f)
        {
            if (!on || hidden) { if (shell != null) shell.SetActive(false); return; }
            if (shell == null) BuildShell();
            shell.SetActive(true);
            var c = new Color(1f, 0.16f + 0.18f * glow, 0.04f, 0.55f + 0.4f * glow);
            shellMat.SetColor("_BaseColor", c);
            shellMat.color = c;
        }

        void BuildShell()
        {
            if (rends == null) rends = Body();
            shell = new GameObject("RedHot");
            shell.transform.SetParent(transform, false);
            if (shellTemplate == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) shellTemplate = new Material(Shader.Find("Sprites/Default"));
                else
                {
                    shellTemplate = new Material(sh);
                    shellTemplate.SetFloat("_Surface", 1);   // transparent, additive: it glows over the paint
                    shellTemplate.SetFloat("_Blend", 2);
                    shellTemplate.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    shellTemplate.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                    shellTemplate.SetFloat("_ZWrite", 0);
                    shellTemplate.SetOverrideTag("RenderType", "Transparent");
                    shellTemplate.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    shellTemplate.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                }
            }
            shellMat = new Material(shellTemplate);
            foreach (var r in rends)
            {
                if (!(r is MeshRenderer) || !r.enabled) continue;   // the airframe's meshes (the pilot is skinned: left as he is)
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var g = new GameObject(r.name + " hot");
                g.transform.SetParent(r.transform, false);
                g.transform.localScale = Vector3.one * 1.01f;
                g.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var mr = g.AddComponent<MeshRenderer>();
                var mats = new Material[Mathf.Max(1, mf.sharedMesh.subMeshCount)];
                for (int k = 0; k < mats.Length; k++) mats[k] = shellMat;
                mr.sharedMaterials = mats;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                g.transform.SetParent(shell.transform, true);   // kept under the shell (so it switches with it), still where its part is
                var follow = g.AddComponent<FollowPart>();
                follow.part = r.transform;
            }
            shell.SetActive(false);
        }

        // ---------- stealth: the dotted outline ----------

        Bounds? outline;

        /// <summary>The airframe's size in its own frame (x across the wings, y up, z along), m, from its meshes.</summary>
        public Bounds LocalBounds()
        {
            if (outline.HasValue) return outline.Value;
            if (rends == null) rends = Body();
            var b = new Bounds(Vector3.zero, Vector3.zero); bool any = false;
            foreach (var r in rends)
            {
                if (!(r is MeshRenderer)) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                for (int k = 0; k < 8; k++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                    var p = transform.InverseTransformPoint(r.transform.TransformPoint(c));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            if (!any || b.size.x < 1f) b = new Bounds(Vector3.zero, new Vector3(11.3f, 3.5f, 9.8f));   // about a P-51
            outline = b;
            return b;
        }

        void Place(Vec3d pos, Vec3d vel, Vec3d up, double alphaDeg) => Pose(transform, pos, vel, up, alphaDeg);

        /// <summary>Put any transform where an aircraft is: on its path, its up, nose raised by the angle of attack.</summary>
        public static void Pose(Transform t, Vec3d pos, Vec3d vel, Vec3d up, double alphaDeg)
        {
            var path = Quaternion.LookRotation(UnityFrame.Dir(vel.Normalized), UnityFrame.Dir(up));
            t.SetPositionAndRotation(UnityFrame.Pos(pos), path * Quaternion.Euler(-(float)alphaDeg, 0, 0));
        }

        /// <summary>0 none, 1 a thin grey trail (damaged), 2 thick black (going down).</summary>
        public void SetSmoke(int level)
        {
            if (level != 0 && smokeLevel == 0) hasLastPuff = false;   // (a new trail starts here, not back where an old one stopped)
            smokeLevel = level;
        }

        // The trail is toon puffs (Fx.ToonFx) laid down along the way the aircraft flew: one every so many metres, so
        // it is unbroken at any speed, and a few a second when it hangs still (planning), so the smoke still rises.
        void LateUpdate()
        {
            Vector3 at = (smokePoint != null ? smokePoint : transform).position;
            if (Crashed && !wasCrashed) Wrecked(at);
            wasCrashed = Crashed;
            if (smokeLevel == 0) return;
            var fx = Fx.ToonFx.Get();
            bool heavy = smokeLevel == 2;
            float r = heavy ? 3.2f : 1.6f, spacing = r * 0.9f, now = Fx.ToonFx.Now;
            int cluster = GetInstanceID() * 2 + 1;
            if (!hasLastPuff || (at - lastPuff).sqrMagnitude > 200f * 200f) { lastPuff = at; lastPuffT = now - 1f; hasLastPuff = true; }
            float gone = Vector3.Distance(lastPuff, at);
            int n = Mathf.Min(24, Mathf.FloorToInt(gone / spacing));
            for (int k = 1; k <= n; k++)
            {
                Vector3 p = Vector3.Lerp(lastPuff, at, k * spacing / gone);
                fx.TrailPuff(p, new Vector3(0f, 0.6f, 0f), r, heavy, cluster, puffSeed++);
            }
            if (n > 0) { lastPuff = Vector3.Lerp(lastPuff, at, n * spacing / gone); lastPuffT = now; }
            else if (now - lastPuffT > 0.12f)
            {
                fx.TrailPuff(at, new Vector3(0f, 1.4f, 0f), r, heavy, cluster, puffSeed++);
                lastPuffT = now;
            }
        }

        // It has hit the ground: a blast, and a wreck that burns and smokes for a while.
        void Wrecked(Vector3 at)
        {
            var fx = Fx.ToonFx.Get();
            fx.Blast(at, 3f, ground: true);
            fx.SmokeColumn(at, 4f, 25f);
        }
    }
}
