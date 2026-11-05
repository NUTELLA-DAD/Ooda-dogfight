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

        public void ShowLive(FlightModel m)
        {
            Model.state = m.state; Model.g = m.g; Model.throttle = m.throttle; Model.rollRateDps = 0;
            Wep = false;
            Place(m.state.pos, m.state.vel, m.state.up, m.AlphaDeg);
        }

        /// <summary>Show a moment between two recorded frames (u from 0 to 1).</summary>
        public void ShowFrame(AircraftFrame a, AircraftFrame b, double u, float dt, bool crashed)
        {
            Vec3d pos = a.pos + (b.pos - a.pos) * u;
            Vec3d vel = a.vel + (b.vel - a.vel) * u;
            Vec3d up = (a.up + (b.up - a.up) * u).Normalized;
            double alpha = a.alphaDeg + (b.alphaDeg - a.alphaDeg) * u;
            Model.state = new PointMass.State { pos = pos, vel = vel, up = up };
            Model.g = a.g + (b.g - a.g) * u;
            Model.throttle = b.throttle;
            Wep = b.wep;
            Vector3 upU = UnityFrame.Dir(up);
            if (dt > 0 && lastUp != Vector3.zero)
                Model.rollRateDps = Vector3.SignedAngle(lastUp, upU, UnityFrame.Dir(vel)) / dt * -1;
            lastUp = upU;
            Crashed = crashed;
            Place(pos, vel, up, alpha);
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
        }

        /// <summary>The model's own renderers (not the red-hot shell).</summary>
        Renderer[] Body()
        {
            var all = GetComponentsInChildren<Renderer>(true);
            var list = new System.Collections.Generic.List<Renderer>(all.Length);
            foreach (var r in all) if (r != null && (shell == null || !r.transform.IsChildOf(shell.transform))) list.Add(r);
            return list.ToArray();
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
