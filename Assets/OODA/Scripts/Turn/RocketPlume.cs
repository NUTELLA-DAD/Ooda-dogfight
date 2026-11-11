using Ooda.Flight;
using Ooda.Flight.Core;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// The rocket boost on a turn-battle aircraft: while its rocket burns (TurnAircraftView.RocketAge), the toon flame out of
    /// its tail (Fx.ToonFx.Rocket: the bands, shock diamonds, shock rings, glow and speed lines), a trail of fire puffs
    /// cooling to smoke behind it, a ring of puffs blown out round the tail as it lights, and a dirty cough of smoke as it
    /// burns out. It also puts a PilotThrust on each pilot in the cockpit, so whoever is flying gets shoved back into the
    /// seat. TurnAircraftView adds it the first time the rocket lights.
    /// </summary>
    public class RocketPlume : MonoBehaviour
    {
        [Tooltip("The nozzle in the aircraft's frame (m; x right, y up, z forward). Zero: just aft of the tail, from the model's size.")]
        public Vector3 nozzle;
        [Tooltip("1 for a fighter about 10 m long; zero: from the model's length.")]
        public float size;
        [Tooltip("Seconds for the flame to die once the rocket stops.")]
        public float dieSec = 0.3f;

        TurnAircraftView view;
        float burn, lastAge = -1f;
        bool wasLit;
        Vector3 lastPuff;
        float lastPuffT;
        bool hasLastPuff;
        int puffSeed, cluster;

        void Start()
        {
            view = GetComponent<TurnAircraftView>();
            if (view == null) { enabled = false; return; }
            var b = view.LocalBounds();
            if (size <= 0) size = Mathf.Clamp(b.size.z / 9.8f, 0.6f, 2.5f);
            if (nozzle == Vector3.zero) nozzle = new Vector3(b.center.x, b.center.y, b.min.z + 0.6f * size);
            cluster = GetInstanceID() * 2 + 2;   // (its own mass of puffs, apart from the damage smoke's)
            foreach (var a in GetComponentsInChildren<Animator>(true))
                if (a.GetComponent<Ooda.Pilot.PilotControls>() != null && a.GetComponent<Ooda.Pilot.PilotThrust>() == null) a.gameObject.AddComponent<Ooda.Pilot.PilotThrust>();
        }

        void LateUpdate()
        {
            if (view == null) return;
            float age = view.RocketAge;
            bool lit = age >= 0 && !view.Crashed && !view.Hidden;
            var fx = Fx.ToonFx.Get();
            Vector3 at = transform.TransformPoint(nozzle);
            Vector3 back = -transform.forward;
            if (view.Model != null && view.Model.state.vel.Length > 1) back = -UnityFrame.Dir(view.Model.state.vel.Normalized);   // (aft along the path, not the nose, in a high alpha pull)
            int seed = GetInstanceID();

            if (lit && (!wasLit || age < lastAge)) Ignite(fx, at, back, seed);
            if (!lit && wasLit && !view.Crashed && !view.Hidden) BurnOut(fx, at, back);
            wasLit = lit;
            if (lit) lastAge = age;

            burn = lit ? Mathf.MoveTowards(burn, 1f, Time.deltaTime / 0.06f) : Mathf.MoveTowards(burn, 0f, Time.deltaTime / Mathf.Max(0.05f, dieSec));
            if (burn <= 0f) { hasLastPuff = false; return; }
            fx.Rocket(at, back, transform.position, size, lit ? age : lastAge, burn, seed);
            if (lit) Trail(fx, at, back);
        }

        // the trail: a fat fire puff every couple of metres along the way it flew, and a few a second when it hangs still
        void Trail(Fx.ToonFx fx, Vector3 at, Vector3 back)
        {
            Vector3 from = at + back * (6f * size);   // (where the flame starts to break up)
            float r = 1.7f * size, spacing = 2.4f * size, now = Fx.ToonFx.Now;
            if (!hasLastPuff || (from - lastPuff).sqrMagnitude > 300f * 300f) { lastPuff = from; lastPuffT = now - 1f; hasLastPuff = true; }
            float gone = Vector3.Distance(lastPuff, from);
            int n = Mathf.Min(16, Mathf.FloorToInt(gone / spacing));
            for (int k = 1; k <= n; k++)
            {
                Vector3 p = Vector3.Lerp(lastPuff, from, k * spacing / gone);
                fx.RocketPuff(p, back * 14f + Random.insideUnitSphere * 3f, r, cluster, puffSeed++);
            }
            if (n > 0) { lastPuff = Vector3.Lerp(lastPuff, from, n * spacing / gone); lastPuffT = now; }
            else if (now - lastPuffT > 0.07f)
            {
                fx.RocketPuff(from, back * 22f + Random.insideUnitSphere * 4f, r, cluster, puffSeed++);
                lastPuffT = now;
            }
        }

        // lighting: a ring of puffs blown out round the tail
        void Ignite(Fx.ToonFx fx, Vector3 at, Vector3 back, int seed)
        {
            Vector3 e1 = Vector3.Cross(back, Mathf.Abs(back.y) < 0.9f ? Vector3.up : Vector3.right).normalized, e2 = Vector3.Cross(back, e1);
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI * 2f / 10f;
                Vector3 o = e1 * Mathf.Cos(a) + e2 * Mathf.Sin(a);
                fx.RocketPuff(at + o * (1.2f * size), o * 26f * size + back * 8f, 1.5f * size, cluster, puffSeed++);
            }
            hasLastPuff = false;
        }

        // burning out: a cough of dark smoke
        void BurnOut(Fx.ToonFx fx, Vector3 at, Vector3 back)
        {
            for (int i = 0; i < 5; i++)
                fx.TrailPuff(at + back * (i * 1.4f * size), back * 6f + Random.insideUnitSphere * 2f, 1.5f * size, true, cluster, puffSeed++);
        }
    }
}
