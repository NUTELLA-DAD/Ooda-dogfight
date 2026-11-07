using UnityEngine;

namespace Ooda.Pilot
{
    /// <summary>
    /// The spectre pilot's ghost fire: blue-white flames wreathing his head and licking up from his hands and ankles, a
    /// soft halo round each (the game has no bloom, so the glow is drawn), and his glowing eyes and gloves flickering with
    /// them. The flames are billboards in one mesh rebuilt each frame on the bones (the SpectreFlame shader turns each to
    /// the camera and animates its shape), so they follow every clip, the briefing's standing poses and the head look.
    /// The head's flames go out with the head when the cockpit camera hides it. Sizes are in the pilot's own height, so
    /// they fit whatever scale a cockpit seats him at. PilotCast fills in the bones.
    /// </summary>
    public class SpectreFlames : MonoBehaviour
    {
        public Transform hips, head, handL, handR, lowerArmL, lowerArmR, upperLegL, upperLegR, footL, footR;
        public Renderer[] glowing;
        [Tooltip("1 = as built; scales every flame's height and width.")]
        public float size = 1f;
        [Tooltip("How strongly the eyes and gloves pulse with the fire.")]
        public float flicker = 0.25f;

        // one flame (or halo) on a bone: where on it (bone local), which way it rises (bone local; the world's up mixed
        // in), how tall and wide, its seed and speed
        struct Tongue { public Transform bone; public Vector3 at, outward; public float h, w, seed, speed, up; public bool halo; }
        Tongue[] tongues;
        Mesh mesh;
        Material mat;
        Vector3[] pos, nrm; Vector2[] uv0, uv1, uv2; Color[] col; int[] tri;
        float unit, unitLocal;
        MaterialPropertyBlock block;
        Color[] emission0;

        void OnEnable()
        {
            if (head == null || hips == null) { enabled = false; return; }
            // the pilot's size: hips to head is 0.31 of his height-1 rig, whatever the pose
            unit = Vector3.Distance(hips.position, head.position) / 0.31f;
            if (unit < 1e-4f) { enabled = false; return; }
            unitLocal = unit / Mathf.Max(1e-6f, Mathf.Abs(transform.lossyScale.x));   // kept in his own scale: the briefing resizes him after this
            Build();
            if (mat == null)
            {
                mat = Resources.Load<Material>("SpectreFlame");
                if (mat == null) { var sh = Shader.Find("Ooda/SpectreFlame"); if (sh != null) mat = new Material(sh); }
            }
            if (mesh == null) { mesh = new Mesh { name = "SpectreFlames" }; mesh.MarkDynamic(); }
            block ??= new MaterialPropertyBlock();
            if (glowing != null)
            {
                emission0 = new Color[glowing.Length];
                for (int i = 0; i < glowing.Length; i++)
                {
                    var m = glowing[i] != null ? glowing[i].sharedMaterial : null;
                    emission0[i] = m != null && m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black;
                }
            }
        }

        void OnDisable()
        {
            if (glowing != null)
                foreach (var r in glowing) if (r != null) r.SetPropertyBlock(null);
        }

        void OnDestroy() { if (mesh != null) Destroy(mesh); }

        void Build()
        {
            var list = new System.Collections.Generic.List<Tongue>();
            var rnd = new System.Random(7);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            // his own axes, from his bones (whatever way the rig's root was turned): up the spine, left across the hips
            Vector3 up = (head.position - hips.position).normalized;
            Vector3 left = upperLegL != null && upperLegR != null ? upperLegL.position - upperLegR.position : -transform.right;
            left = Vector3.ProjectOnPlane(left, up).normalized;
            Vector3 right = -left, fwd = Vector3.Cross(up, left).normalized;
            var headScale = head.localScale; head.localScale = Vector3.one;   // built as if the head is shown
            // the agent faces the root's forward; his round head is about 0.34 across, its middle 0.17 over the head bone
            Vector3 headMid = head.position + up * 0.17f * unit;
            void OnHead(Vector3 dirRoot, float h, float w, float upMix)
            {
                Vector3 d = dirRoot.normalized;
                Vector3 p = headMid + d * 0.16f * unit;
                list.Add(new Tongue { bone = head, at = head.InverseTransformPoint(p), outward = head.InverseTransformDirection(d), h = h, w = w,
                                      seed = R(0, 10), speed = R(0.8f, 1.25f), up = upMix });
            }
            // a crown of tall licks over the top and back, shorter ones round the sides, none over the face
            for (int k = 0; k < 9; k++)
            {
                float az = Mathf.Lerp(-150f, 150f, k / 8f) + R(-8, 8);      // 0 is straight back
                float el = R(35f, 70f);
                Vector3 d = Quaternion.AngleAxis(az, up) * (Quaternion.AngleAxis(-el, right) * -fwd);
                OnHead(d, R(0.17f, 0.26f), R(0.06f, 0.085f), 0.7f);
            }
            for (int k = 0; k < 8; k++)
            {
                float az = (k < 4 ? Mathf.Lerp(55f, 165f, k / 3f) : -Mathf.Lerp(55f, 165f, (k - 4) / 3f)) + R(-6, 6);
                Vector3 d = Quaternion.AngleAxis(az, up) * (Quaternion.AngleAxis(-R(-5f, 25f), right) * -fwd);
                OnHead(d, R(0.10f, 0.17f), R(0.045f, 0.065f), 0.45f);
            }
            OnHead(up + fwd * 0.15f, 0.34f, 0.10f, 0.9f);                   // the big curl off the top
            OnHead(up - fwd * 0.35f, 0.28f, 0.09f, 0.9f);
            list.Add(new Tongue { bone = head, at = head.InverseTransformPoint(headMid), w = 0.42f, seed = R(0, 10), speed = 1, halo = true });

            void OnHand(Transform hand, Transform lower)
            {
                if (hand == null) return;
                Vector3 along = lower != null ? (hand.position - lower.position).normalized : up;
                Vector3 mid = hand.position + along * 0.05f * unit;
                for (int k = 0; k < 5; k++)
                {
                    Vector3 d = Quaternion.AngleAxis(k * 72f + R(-15, 15), along) * Vector3.Cross(along, fwd).normalized;
                    list.Add(new Tongue { bone = hand, at = hand.InverseTransformPoint(mid + d * 0.035f * unit), outward = hand.InverseTransformDirection(d),
                                          h = R(0.09f, 0.15f), w = R(0.03f, 0.045f), seed = R(0, 10), speed = R(0.9f, 1.4f), up = 0.75f });
                }
                list.Add(new Tongue { bone = hand, at = hand.InverseTransformPoint(mid), w = 0.15f, seed = R(0, 10), speed = 1, halo = true });
            }
            OnHand(handL, lowerArmL); OnHand(handR, lowerArmR);

            void OnAnkle(Transform foot)
            {
                if (foot == null) return;
                Vector3 mid = foot.position + up * 0.01f * unit;
                for (int k = 0; k < 5; k++)
                {
                    Vector3 d = Quaternion.AngleAxis(k * 72f + R(-15, 15), up) * fwd;
                    list.Add(new Tongue { bone = foot, at = foot.InverseTransformPoint(mid + d * 0.05f * unit), outward = foot.InverseTransformDirection(d),
                                          h = R(0.06f, 0.10f), w = R(0.025f, 0.035f), seed = R(0, 10), speed = R(0.9f, 1.4f), up = 0.85f });
                }
                list.Add(new Tongue { bone = foot, at = foot.InverseTransformPoint(mid), w = 0.10f, seed = R(0, 10), speed = 1, halo = true });
            }
            OnAnkle(footL); OnAnkle(footR);
            head.localScale = headScale;

            tongues = list.ToArray();
            int n = tongues.Length * 4;
            pos = new Vector3[n]; nrm = new Vector3[n]; uv0 = new Vector2[n]; uv1 = new Vector2[n]; uv2 = new Vector2[n]; col = new Color[n];
            tri = new int[tongues.Length * 6];
            for (int i = 0; i < tongues.Length; i++)
            {
                int v = i * 4;
                uv0[v] = new Vector2(-1, 0); uv0[v + 1] = new Vector2(1, 0); uv0[v + 2] = new Vector2(1, 1); uv0[v + 3] = new Vector2(-1, 1);
                int t = i * 6;
                tri[t] = v; tri[t + 1] = v + 2; tri[t + 2] = v + 1; tri[t + 3] = v; tri[t + 4] = v + 3; tri[t + 5] = v + 2;
            }
        }

        void LateUpdate()
        {
            if (mat == null || tongues == null) return;
            float s = unitLocal * Mathf.Abs(transform.lossyScale.x) * size;   // the pilot's height, as he is scaled now
            bool headOn = head.localScale.x > 0.1f;                           // the cockpit camera shrinks the head away (PilotHeadLook)
            Vector3 worldUp = Vector3.up;
            Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
            int shown = 0;
            for (int i = 0; i < tongues.Length; i++)
            {
                var g = tongues[i];
                int v = i * 4;
                bool on = g.bone != null && g.bone.gameObject.activeInHierarchy && (g.bone != head || headOn);
                Vector3 c = on ? g.bone.TransformPoint(g.at) : Vector3.zero;
                Vector3 rise = Vector3.zero;
                if (on && !g.halo)
                {
                    Vector3 o = g.bone.TransformDirection(g.outward).normalized;
                    rise = (o * (1 - g.up) + worldUp * g.up).normalized * g.h * s;
                }
                float w = on ? g.w * s : 0f;
                for (int k = 0; k < 4; k++)
                {
                    pos[v + k] = c; nrm[v + k] = rise;
                    uv1[v + k] = new Vector2(w, g.halo ? 1 : 0);
                    uv2[v + k] = new Vector2(g.seed, g.speed);
                    col[v + k] = g.halo ? new Color(0.6f, 0.9f, 1f, 0.55f) : new Color(1f, 1f, 1f, 1f);
                }
                if (on) { shown++; lo = Vector3.Min(lo, c); hi = Vector3.Max(hi, c); }
            }
            if (shown == 0) return;
            mesh.vertices = pos; mesh.normals = nrm; mesh.uv = uv0; mesh.uv2 = uv1; mesh.uv3 = uv2; mesh.colors = col;
            if (mesh.GetIndexCount(0) != (uint)tri.Length) mesh.triangles = tri;
            var b = new Bounds((lo + hi) * 0.5f, hi - lo); b.Expand(0.8f * s);
            mesh.bounds = b;
            Graphics.DrawMesh(mesh, Matrix4x4.identity, mat, gameObject.layer, null, 0, null, UnityEngine.Rendering.ShadowCastingMode.Off, false);

            // the eyes and gloves pulse with the fire
            if (glowing != null && emission0 != null)
            {
                float t = Time.time;
                float f = 1f + flicker * (0.6f * Mathf.Sin(t * 5.3f) * Mathf.Sin(t * 2.1f + 1.3f) + 0.4f * Mathf.Sin(t * 11.7f));
                for (int i = 0; i < glowing.Length; i++)
                {
                    if (glowing[i] == null) continue;
                    glowing[i].GetPropertyBlock(block);
                    block.SetColor("_EmissionColor", emission0[i] * f);
                    glowing[i].SetPropertyBlock(block);
                }
            }
        }
    }
}
