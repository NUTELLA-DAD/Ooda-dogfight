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

        [Tooltip("Where the smoke comes from (the engine); empty uses the aircraft's centre.")]
        public Transform smokePoint;

        ParticleSystem smoke;
        Vector3 lastUp;

        public void Bind(FlightModel model)
        {
            Model = model.Clone();
            Crashed = false;
            SetSmoke(0);
            ShowLive(model);
        }

        public void ShowLive(FlightModel m)
        {
            Model.state = m.state; Model.g = m.g; Model.throttle = m.throttle; Model.rollRateDps = 0;
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
            Vector3 upU = UnityFrame.Dir(up);
            if (dt > 0 && lastUp != Vector3.zero)
                Model.rollRateDps = Vector3.SignedAngle(lastUp, upU, UnityFrame.Dir(vel)) / dt * -1;
            lastUp = upU;
            Crashed = crashed;
            Place(pos, vel, up, alpha);
        }

        void Place(Vec3d pos, Vec3d vel, Vec3d up, double alphaDeg)
        {
            var path = Quaternion.LookRotation(UnityFrame.Dir(vel.Normalized), UnityFrame.Dir(up));
            transform.SetPositionAndRotation(UnityFrame.Pos(pos), path * Quaternion.Euler(-(float)alphaDeg, 0, 0));
        }

        /// <summary>0 none, 1 a thin trail (damaged), 2 thick black (going down).</summary>
        public void SetSmoke(int level)
        {
            if (level == 0) { if (smoke != null) smoke.Stop(); return; }
            if (smoke == null) smoke = MakeSmoke();
            var main = smoke.main;
            main.startSize = level == 2 ? 6f : 2.5f;
            main.startColor = level == 2 ? new Color(0.12f, 0.12f, 0.12f, 0.85f) : new Color(0.55f, 0.55f, 0.55f, 0.5f);
            var em = smoke.emission;
            em.rateOverTime = level == 2 ? 60 : 20;
            if (!smoke.isPlaying) smoke.Play();
        }

        ParticleSystem MakeSmoke()
        {
            var go = new GameObject("Smoke");
            go.transform.SetParent(smokePoint != null ? smokePoint : transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 5f;
            main.startSpeed = 2f;
            main.maxParticles = 600;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.5f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 3f));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                         new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
            col.color = grad;
            var r = go.GetComponent<ParticleSystemRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            mat.SetFloat("_Surface", 1);
            mat.SetColor("_BaseColor", Color.white);
            r.material = mat;
            return ps;
        }
    }
}
