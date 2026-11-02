using UnityEngine;

namespace Ooda.Flight
{
    /// <summary>
    /// The jet test's exhaust: a faint heat cone from each nozzle on dry power, and with the afterburner lit a long
    /// flickering orange flame with a white-hot core and an orange light on the tail. Near Mach 1 low down, a white
    /// vapour cone forms round the fuselage. All made here from code, so there are no assets to keep in step.
    /// </summary>
    public class JetBurner : MonoBehaviour
    {
        public AircraftController aircraft;
        [Tooltip("The nozzles' exits in the aircraft's frame (metres; x right, y up, z forward).")]
        public Vector3[] nozzles = { new Vector3(-0.5f, -0.85f, -8.75f), new Vector3(0.5f, -0.85f, -8.75f) };
        public float nozzleRadius = 0.34f;
        [Tooltip("The afterburner flame's length, metres.")]
        public float burnerLength = 8f;
        [Tooltip("Seconds for the afterburner to light up fully or die away.")]
        public float lightSec = 0.35f;

        struct Plume { public Transform outer, core; }
        Plume[] plumes;
        Transform vapour;
        Material vapourMat;
        Light glow;
        float burn;   // 0 out, 1 fully lit

        static readonly Color OuterBase = new Color(1f, 0.55f, 0.15f, 0.9f), OuterTip = new Color(1f, 0.3f, 0.05f, 0f);
        static readonly Color CoreBase = new Color(1f, 0.97f, 0.85f, 1f), CoreTip = new Color(1f, 0.7f, 0.3f, 0f);

        void Start()
        {
            var mat = new Material(Shader.Find("Sprites/Default"));   // vertex colours, alpha blended, both sides
            var outerMesh = Cone(OuterBase, OuterTip, 1f, 0f);
            var coreMesh = Cone(CoreBase, CoreTip, 1f, 0f);
            plumes = new Plume[nozzles.Length];
            for (int i = 0; i < nozzles.Length; i++)
            {
                plumes[i].outer = Part("Burner " + i, outerMesh, mat, nozzles[i]);
                plumes[i].core = Part("Burner core " + i, coreMesh, mat, nozzles[i]);
            }

            var lightGo = new GameObject("Burner light");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = new Vector3(0, nozzles.Length > 0 ? nozzles[0].y : 0, (nozzles.Length > 0 ? nozzles[0].z : -9) - 3f);
            glow = lightGo.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.55f, 0.2f);
            glow.range = 30f;
            glow.intensity = 0;
            glow.shadows = LightShadows.None;

            // the vapour cone: a bell round the fuselage from just behind the canopy back past the wing
            vapourMat = new Material(mat);
            vapour = Part("Vapour cone", Cone(new Color(1, 1, 1, 0.55f), new Color(1, 1, 1, 0f), 0.25f, 1f), vapourMat, new Vector3(0, 0, 3f));
            vapour.localScale = new Vector3(6.5f, 4.2f, 8f);
        }

        Transform Part(string name, Mesh mesh, Material mat, Vector3 at)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = at;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.SetActive(false);
            return go.transform;
        }

        /// <summary>An open cone along -z from z = 0 (radius r0) to z = -1 (radius r1), coloured from base to tip.</summary>
        static Mesh Cone(Color baseCol, Color tipCol, float r0, float r1)
        {
            const int sides = 20, rings = 6;
            var v = new Vector3[(sides + 1) * (rings + 1)];
            var c = new Color[v.Length];
            var t = new int[sides * rings * 6];
            for (int j = 0; j <= rings; j++)
            {
                float u = j / (float)rings;
                float r = Mathf.Lerp(r0, r1, u);
                if (r1 < r0) r = r0 * (1 - u * u);   // a flame: full near the nozzle, rounding off to its tip
                for (int i = 0; i <= sides; i++)
                {
                    float a = i * Mathf.PI * 2 / sides;
                    v[j * (sides + 1) + i] = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, -u);
                    c[j * (sides + 1) + i] = Color.Lerp(baseCol, tipCol, u);
                }
            }
            int k = 0;
            for (int j = 0; j < rings; j++)
                for (int i = 0; i < sides; i++)
                {
                    int a = j * (sides + 1) + i, b = a + sides + 1;
                    t[k++] = a; t[k++] = b; t[k++] = a + 1;
                    t[k++] = a + 1; t[k++] = b; t[k++] = b + 1;
                }
            var m = new Mesh { vertices = v, colors = c, triangles = t };
            m.RecalculateBounds();
            return m;
        }

        void LateUpdate()
        {
            if (aircraft == null || aircraft.Model == null || plumes == null) return;
            var m = aircraft.Model;
            bool lit = !aircraft.Crashed && aircraft.Input.Wep && m.throttle > 0.95;
            burn = Mathf.MoveTowards(burn, lit ? 1 : 0, Time.deltaTime / lightSec);
            float dry = aircraft.Crashed ? 0 : (float)m.throttle;

            for (int i = 0; i < plumes.Length; i++)
            {
                float flick = 1 + 0.12f * (Mathf.PerlinNoise(Time.time * 14f, i * 3.1f) - 0.5f) * 2;
                float len = Mathf.Lerp(1.2f * dry, burnerLength * flick, burn);
                float rad = nozzleRadius * Mathf.Lerp(0.8f, 1.05f, burn);
                var p = plumes[i];
                p.outer.gameObject.SetActive(len > 0.05f && (burn > 0.01f || dry > 0.05f));
                p.outer.localScale = new Vector3(rad, rad, len);
                p.core.gameObject.SetActive(burn > 0.01f);
                p.core.localScale = new Vector3(rad * 0.6f, rad * 0.6f, len * 0.55f);
            }
            glow.intensity = burn * (6f + 1.5f * Mathf.PerlinNoise(Time.time * 9f, 0.5f));

            // the vapour cone: right at Mach 1, thickest low down in the moist air, gone above about 25,000 ft
            float mach = (float)m.Mach;
            float wet = Mathf.InverseLerp(25000f, 3000f, (float)m.AltFt);
            float v = Mathf.Clamp01(1 - Mathf.Abs(mach - 0.99f) / 0.035f) * Mathf.Lerp(0.25f, 1f, wet);
            vapour.gameObject.SetActive(v > 0.01f && !aircraft.Crashed);
            vapourMat.color = new Color(1, 1, 1, v);
        }
    }
}
