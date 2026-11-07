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
    /// Three looks (<see cref="look"/>, or -spectrestyle aura|flames|fog on the command line): licking flames, a glowing
    /// fog of soft wisps that swell, rise and fade off him, or the aura (the usual one): the flames with a thin glowing
    /// mist close round his head and hands. Whichever, his face is left clear and his eyes blaze.
    /// </summary>
    public class SpectreFlames : MonoBehaviour
    {
        public Transform hips, head, handL, handR, lowerArmL, lowerArmR, upperLegL, upperLegR, footL, footR, toesL;
        public Renderer[] glowing;
        [Tooltip("1 = as built; scales every flame's height and width.")]
        public float size = 1f;
        [Tooltip("How strongly the eyes and gloves pulse with the fire.")]
        public float flicker = 0.25f;
        public enum Look { Flames, Fog, Aura }
        public Look look = Look.Aura;

        /// <summary>-spectrestyle fog (or flames) on the command line picks the look for every spectre.</summary>
        static Look? forced;
        static bool argsRead;
        static Look? Forced()
        {
            if (argsRead) return forced;
            argsRead = true;
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < a.Length; i++)
                if (a[i] == "-spectrestyle") { var v = a[i + 1].ToLowerInvariant(); forced = v.StartsWith("fog") ? Look.Fog : v.StartsWith("flame") ? Look.Flames : Look.Aura; }
            return forced;
        }

        // one flame (or halo) on a bone: where on it (bone local), which way it rises (bone local; the world's up mixed
        // in), how tall and wide, its seed and speed
        struct Tongue { public Transform bone; public Vector3 at, outward; public float h, w, seed, speed, up; public int kind; public bool eye; }   // kind: 0 flame, 1 halo, 2 wisp of fog
        const int Flame = 0, Halo = 1, Wisp = 2;
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
            var f = Forced(); if (f.HasValue) look = f.Value;
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
            if (toesL != null && footL != null && Vector3.Dot(toesL.position - footL.position, fwd) < 0) { fwd = -fwd; right = -right; }   // his toes point the way he faces
            var headScale = head.localScale; head.localScale = Vector3.one;   // built as if the head is shown
            // his round head is about 0.34 across, its middle 0.17 over the head bone. Directions round it are an azimuth
            // from straight behind (0) to straight ahead (180) and an elevation; the face, ahead within 70 degrees and
            // below the crown, is kept clear
            Vector3 headMid = head.position + up * 0.17f * unit;
            Vector3 Dir(float az, float el)
            {
                float a = az * Mathf.Deg2Rad, e = el * Mathf.Deg2Rad;
                return Mathf.Cos(e) * (Mathf.Cos(a) * -fwd + Mathf.Sin(a) * right) + Mathf.Sin(e) * up;
            }
            void OnHead(Vector3 d, float h, float w, float upMix, int kind, float r = 0.19f)   // r: just off the skin, so no flame cuts into it
            {
                d = d.normalized;
                Vector3 p = headMid + d * r * unit;
                list.Add(new Tongue { bone = head, at = head.InverseTransformPoint(p), outward = head.InverseTransformDirection(d), h = h, w = w,
                                      seed = R(0, 10), speed = R(0.8f, 1.25f), up = upMix, kind = kind });
            }
            bool fog = look == Look.Fog, aura = look == Look.Aura;
            if (!fog)
            {
                // a big crown: tall curling licks all over the top and back, rising off the crown, and a ring of shorter
                // ones round the back and sides, framing the face
                for (int k = 0; k < 13; k++)
                {
                    float az = Mathf.Lerp(-150f, 150f, k / 12f) + R(-8, 8);
                    float el = Mathf.Abs(az) > 110f ? R(62f, 80f) : R(35f, 75f);   // over the brow only from high up
                    OnHead(Dir(az, el), R(0.26f, 0.40f), R(0.09f, 0.12f), 0.75f, Flame);
                }
                for (int k = 0; k < 10; k++)
                {
                    float az = (k < 5 ? 1 : -1) * Mathf.Lerp(40f, 108f, (k % 5) / 4f) + R(-5, 5);
                    OnHead(Dir(az, R(-10f, 30f)), R(0.15f, 0.24f), R(0.06f, 0.085f), 0.55f, Flame);
                }
                OnHead(up + fwd * 0.1f, 0.48f, 0.13f, 0.95f, Flame);          // the big curl off the top
                OnHead(up - fwd * 0.4f, 0.40f, 0.12f, 0.9f, Flame);
                OnHead(up + right * 0.35f, 0.36f, 0.11f, 0.9f, Flame);
                OnHead(up - right * 0.35f, 0.36f, 0.11f, 0.9f, Flame);
                if (aura)
                    for (int k = 0; k < 16; k++)   // a glowing mist round the head, out past the flames' roots
                    {
                        float az = R(-140f, 140f), el = Mathf.Abs(az) > 105f ? R(55f, 85f) : R(-25f, 80f);
                        OnHead(Dir(az, el), R(0.08f, 0.16f), R(0.12f, 0.17f), 0.5f, Wisp, 0.24f);
                    }
            }
            else
            {
                // a glowing fog: soft wisps clinging over the top, back and sides of his head, and more swelling off the
                // crown and drifting up
                for (int k = 0; k < 18; k++)
                {
                    float az = R(-150f, 150f), el = Mathf.Abs(az) > 108f ? R(58f, 85f) : R(-15f, 85f);
                    OnHead(Dir(az, el), R(0.18f, 0.30f), R(0.10f, 0.15f), 0.7f, Wisp, 0.13f);
                }
                for (int k = 0; k < 8; k++)
                    OnHead(Dir(R(-180f, 180f), R(70f, 90f)), R(0.35f, 0.55f), R(0.13f, 0.19f), 0.9f, Wisp, 0.17f);
            }
            list.Add(new Tongue { bone = head, at = head.InverseTransformPoint(headMid + up * 0.05f * unit), w = fog ? 0.55f : 0.50f, seed = R(0, 10), speed = 1, kind = Halo });
            // the eyes blaze: a hot glow centred on each (the eyes sit 0.12 over the head bone, 0.1 either side, 0.09 ahead;
            // the glow just in front of them)
            for (int side = -1; side <= 1; side += 2)
                list.Add(new Tongue { bone = head, at = head.InverseTransformPoint(head.position + (up * 0.123f + right * (0.098f * side) + fwd * 0.12f) * unit),
                                      w = 0.085f, seed = R(0, 10), speed = 1.5f, kind = Halo, eye = true });

            void OnHand(Transform hand, Transform lower)
            {
                if (hand == null) return;
                Vector3 along = lower != null ? (hand.position - lower.position).normalized : up;
                Vector3 mid = hand.position + along * 0.05f * unit;
                Vector3 across = Vector3.Cross(along, fwd).normalized;
                for (int k = 0; k < (fog ? 6 : 7); k++)
                {
                    Vector3 d = Quaternion.AngleAxis(k * (fog ? 60f : 51f) + R(-15, 15), along) * across;
                    list.Add(new Tongue { bone = hand, at = hand.InverseTransformPoint(mid + d * (fog ? 0.025f : 0.035f) * unit), outward = hand.InverseTransformDirection(d),
                                          h = fog ? R(0.12f, 0.2f) : R(0.11f, 0.19f), w = fog ? R(0.06f, 0.09f) : R(0.04f, 0.055f),
                                          seed = R(0, 10), speed = R(0.9f, 1.4f), up = 0.75f, kind = fog ? Wisp : Flame });
                }
                if (aura)
                    for (int k = 0; k < 3; k++)   // a little mist round the glove, staying close
                    {
                        Vector3 d = Quaternion.AngleAxis(k * 120f + R(-20, 20), along) * across;
                        list.Add(new Tongue { bone = hand, at = hand.InverseTransformPoint(mid + d * 0.04f * unit), outward = hand.InverseTransformDirection(d),
                                              h = R(0.05f, 0.08f), w = R(0.08f, 0.11f), seed = R(0, 10), speed = R(0.9f, 1.3f), up = 0.5f, kind = Wisp });
                    }
                list.Add(new Tongue { bone = hand, at = hand.InverseTransformPoint(mid), w = 0.2f, seed = R(0, 10), speed = 1, kind = Halo });
            }
            OnHand(handL, lowerArmL); OnHand(handR, lowerArmR);

            void OnAnkle(Transform foot)
            {
                if (foot == null) return;
                Vector3 mid = foot.position + up * 0.01f * unit;
                for (int k = 0; k < 6; k++)
                {
                    Vector3 d = Quaternion.AngleAxis(k * 60f + R(-15, 15), up) * fwd;
                    list.Add(new Tongue { bone = foot, at = foot.InverseTransformPoint(mid + d * 0.05f * unit), outward = foot.InverseTransformDirection(d),
                                          h = fog ? R(0.1f, 0.16f) : R(0.08f, 0.13f), w = fog ? R(0.05f, 0.07f) : R(0.03f, 0.042f),
                                          seed = R(0, 10), speed = R(0.9f, 1.4f), up = 0.85f, kind = fog ? Wisp : Flame });
                }
                list.Add(new Tongue { bone = foot, at = foot.InverseTransformPoint(mid), w = 0.14f, seed = R(0, 10), speed = 1, kind = Halo });
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
                if (on && g.kind != Halo)
                {
                    Vector3 o = g.bone.TransformDirection(g.outward).normalized;
                    rise = (o * (1 - g.up) + worldUp * g.up).normalized * g.h * s;
                }
                float w = on ? g.w * s : 0f;
                for (int k = 0; k < 4; k++)
                {
                    pos[v + k] = c; nrm[v + k] = rise;
                    uv1[v + k] = new Vector2(w, g.kind);
                    uv2[v + k] = new Vector2(g.seed, g.speed);
                    col[v + k] = g.eye ? new Color(0.7f, 1f, 1f, 1.6f) : g.kind == Halo ? new Color(0.6f, 0.9f, 1f, 0.9f) : g.kind == Wisp && look == Look.Aura ? new Color(1f, 1f, 1f, 1.5f) : new Color(1f, 1f, 1f, 1f);
                }
                if (on) { shown++; lo = Vector3.Min(lo, c); hi = Vector3.Max(hi, c); }
            }
            if (shown == 0) return;
            mesh.vertices = pos; mesh.normals = nrm; mesh.uv = uv0; mesh.uv2 = uv1; mesh.uv3 = uv2; mesh.colors = col;
            if (mesh.GetIndexCount(0) != (uint)tri.Length) mesh.triangles = tri;
            var b = new Bounds((lo + hi) * 0.5f, hi - lo); b.Expand(1.2f * s);
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
