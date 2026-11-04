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
            if (rends == null) rends = GetComponentsInChildren<Renderer>(true);
            if (!on)
            {
                wasEnabled = new bool[rends.Length];
                for (int k = 0; k < rends.Length; k++) if (rends[k] != null) { wasEnabled[k] = rends[k].enabled; rends[k].enabled = false; }
            }
            else if (wasEnabled != null)
                for (int k = 0; k < rends.Length; k++) if (rends[k] != null) rends[k].enabled = wasEnabled[k];
            hidden = !on;
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