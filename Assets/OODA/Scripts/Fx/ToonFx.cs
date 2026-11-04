using System.Collections.Generic;
using UnityEngine;

namespace Ooda.Fx
{
    /// <summary>
    /// Smoke and explosions in the toon way, ported from Airship Crew's effects (its View/Effects.cs): graphic shapes,
    /// never photo particles. Flat, unlit, ROUND puffs facing the camera, each with a dark rim line, overlapping back to
    /// front so a mass reads as a cluster of circles, cel-shaded (a shade tone under a lit tone), a few with a curly swirl.
    /// - a blast: a white flash and star, a dome of yellow puffs cooling to orange, red and charcoal, two or three trails
    ///   of puffs shooting up in a V, a ring of brick-red puffs at its base, a shock ring, sparks and dark chunks flung out,
    ///   then a grey-black plume that rises and thins away;
    /// - a hit: a star, sparks, a chip or two and a wisp;
    /// - smoke trails (a hurt aircraft's grey, a falling one's black) and smoke columns rising from a wreck or a target;
    /// - tracers: a white-hot core in a soft glow of its side's colour, trailing a faint wisp of the same puffs.
    /// Everything animates in stepped keys (15 a second) and is placed by stable hashes, so a replay looks the same.
    /// One mesh, rebuilt each frame: the whole sky of effects is one draw call.
    /// </summary>
    [DefaultExecutionOrder(1000)]   // after the cameras have moved: the puffs face where the camera is this frame
    public sealed class ToonFx : MonoBehaviour
    {
        // ---- caps and timing
        const float KeysPerSec = 15f;
        const int MaxPuffs = 1400, MaxShards = 96, MaxFlashes = 48, MaxColumns = 12, MaxBlastsDrawn = 6;
        const float Gravity = 9.8f;

        /// <summary>The effects' clock, s. Shots in batch mode (no play loop) set ClockOverride to stage effects at known ages.</summary>
        public static float ClockOverride = -1f;
        public static float Now => ClockOverride >= 0f ? ClockOverride : Time.time;

        static ToonFx instance;

        /// <summary>The effects of this scene (made the first time it is asked for).</summary>
        public static ToonFx Get()
        {
            if (instance != null) return instance;
            var go = new GameObject("ToonFx");
            instance = go.AddComponent<ToonFx>();
            instance.Init();
            return instance;
        }

        /// <summary>Everything gone at once (a fight restarted, a replay from the top).</summary>
        public static void ClearAll() { if (instance != null) instance.Clear(); }

        // ---- the mesh
        Mesh mesh;
        readonly List<Vector3> verts = new List<Vector3>(16384);
        readonly List<Color> cols = new List<Color>(16384);
        readonly List<int> tris = new List<int>(49152);

        void Init()
        {
            mesh = new Mesh { name = "ToonFx", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = gameObject.AddComponent<MeshRenderer>();
            var mat = Resources.Load<Material>("ToonFx");
            if (mat == null) mat = new Material(Shader.Find("Ooda/ToonFx") ?? Shader.Find("Sprites/Default"));
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        void OnDestroy() { if (instance == this) instance = null; }

        public void Clear()
        {
            for (int i = 0; i < MaxPuffs; i++) puffs[i].On = false;
            for (int i = 0; i < MaxShards; i++) shards[i].On = false;
            for (int i = 0; i < MaxFlashes; i++) flashes[i].On = false;
            for (int i = 0; i < MaxColumns; i++) columns[i].On = false;
            tracerQ.Clear(); wispQ.Clear();
        }

        // ---------------------------------------------------------------------------------------------------------
        // pools
        struct Puff { public Vector3 P, V, Drift; public float T0, Life, R, Grow, Rise, Drag; public int Cluster; public byte Style; public bool Swirl, On; }
        struct Shard { public Vector3 P, V; public float T0, Life, Spin, Len, Thick, Rot0; public Color C; public bool On; }
        enum FK : byte { Blast, Hit }
        struct Flash { public Vector3 P, D; public float T0, S; public int Seed; public FK K; public bool Ground, On; }
        struct Column { public Vector3 P; public float T0, Until, Last, S; public int Seed; public bool On; }

        readonly Puff[] puffs = new Puff[MaxPuffs];
        readonly Shard[] shards = new Shard[MaxShards];
        readonly Flash[] flashes = new Flash[MaxFlashes];
        readonly Column[] columns = new Column[MaxColumns];
        int nextPuff, nextShard, nextFlash, nextColumn;

        // ---- puff styles (fills paling with age, rim lines)
        const byte Smoke = 0, Trail = 1, Black = 2, ColumnS = 3;
        static readonly Color[][] Fills =
        {
            new[] { Hex("#2f2a38"), Hex("#3d3747"), Hex("#5a5466") },   // explosion smoke: charcoal purple-grey, paling
            new[] { Hex("#6a6570"), Hex("#85808c"), Hex("#a8a4ae") },   // a hurt aircraft's thin grey trail
            new[] { Hex("#26232c"), Hex("#38343f"), Hex("#57525e") },   // a falling aircraft's thick black trail
            new[] { Hex("#4a4054"), Hex("#695d77"), Hex("#93889f") },   // a smoke column: warm purple-grey, lightening as it climbs
        };
        static readonly Color[] Lines = { Hex("#16131c"), Hex("#3a363f"), Hex("#121016"), Hex("#2a2233") };
        static readonly Color[] Metal = { Hex("#3a3f48"), Hex("#5a606a"), Hex("#2a2d33"), Hex("#6e5a48") };
        static readonly Color[] Rubble = { Hex("#4a4038"), Hex("#6e6152"), Hex("#3a3330"), Hex("#7a4a2a") };
        static readonly Color FlashWhite = new Color(1f, 1f, 0.94f, 1f), FlashYellow = new Color(1f, 0.92f, 0.36f, 1f);
                static readonly Color SparkHot = new Color(1f, 0.97f, 0.7f, 1f), ShardLine = Hex("#141218");

        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
        static readonly float[] Tilt3 = { -40f, 6f, 38f }, Tilt2 = { -30f, 32f }, TrailLen = { 1f, 0.66f, 0.84f, 0.74f };

        // ---------------------------------------------------------------------------------------------------------
        // stable hashes (no unseeded random)
        static uint H(uint x) { x ^= x >> 16; x *= 0x7feb352dU; x ^= x >> 15; x *= 0x846ca68bU; x ^= x >> 16; return x; }
        static float R01(int a, int b) => (H((uint)a * 73856093U ^ H((uint)b * 19349663U + 0x9e3779b9U)) & 0xffffff) / 16777216f;
        static float Rng(int a, int b, float lo, float hi) => lo + (hi - lo) * R01(a, b);
        static int Seed(Vector3 p, float t) => (int)H((uint)Mathf.RoundToInt(p.x * 3f) * 92837111U ^ (uint)Mathf.RoundToInt(p.y * 3f) * 689287499U ^ (uint)Mathf.RoundToInt(p.z * 3f) * 283923481U ^ (uint)Mathf.RoundToInt(t * 30f));

        static int Key(float now, float t0) => Mathf.FloorToInt((now - t0) * KeysPerSec);

        // ---------------------------------------------------------------------------------------------------------
        // Adding effects (t0: when the thing happened, on the Now clock)

        void AddPuff(Vector3 p, Vector3 v, float r, float life, float t0, byte style, float grow = 0.6f, float rise = 0.8f, float drag = 2.5f, bool swirl = false, int cluster = 0)
        {
            ref var q = ref puffs[nextPuff];
            nextPuff = (nextPuff + 1) % MaxPuffs;
            q = new Puff { P = p, V = v, R = r, Life = Mathf.Max(0.15f, life), T0 = t0, Style = style, Grow = grow, Rise = rise, Drag = drag, Swirl = swirl, Cluster = cluster, On = true };
        }

        void AddShard(Vector3 p, Vector3 v, float t0, int seed, Color c, float len, float life)
        {
            ref var s = ref shards[nextShard];
            nextShard = (nextShard + 1) % MaxShards;
            s = new Shard { P = p, V = v, T0 = t0, Life = life, Spin = Rng(seed, 4, 360f, 900f) * (R01(seed, 5) < 0.5f ? -1f : 1f) * Mathf.Deg2Rad, Rot0 = Rng(seed, 7, 0f, 6.28f), Len = len, Thick = len * Rng(seed, 6, 0.3f, 0.5f), C = c, On = true };
        }

        void AddFlash(FK k, Vector3 p, Vector3 d, float s, float t0, bool ground = false)
        {
            ref var f = ref flashes[nextFlash];
            nextFlash = (nextFlash + 1) % MaxFlashes;
            f = new Flash { K = k, P = p, D = d, S = s, T0 = t0, Seed = Seed(p, t0), Ground = ground, On = true };
        }

        /// <summary>
        /// An explosion at p (size 1 is about 5 m tall; an aircraft going up is about 2.5, a bomb 6 to 9). ground: it
        /// bursts off the ground (a warm light pool round it, rubble), else in the air (metal bits). bits: the chunks'
        /// colours (null: by ground).
        /// </summary>
        public void Blast(Vector3 p, float s, bool ground = false, Vector3? normal = null, float? t0 = null, Color[] bits = null)
        {
            float t = t0 ?? Now;
            Vector3 n = normal ?? Vector3.up;
            // (two blasts at once in nearly the same spot read as one)
            for (int i = 0; i < MaxFlashes; i++)
            {
                ref var f = ref flashes[i];
                if (f.On && f.K == FK.Blast && Mathf.Abs(f.T0 - t) <= 0.2f && (f.P - p).sqrMagnitude < 9f * s * s) { if (s > f.S) f.S = s; return; }
            }
            AddFlash(FK.Blast, p, n, s, t, ground);
            int seed = Seed(p, t);
            var c1 = bits ?? (ground ? Rubble : Metal);
            Chunks(p + n * 0.6f * s, 5 + (int)(R01(seed, 77) * 4.99f), t, c1, 13f * Mathf.Sqrt(s), 1.15f * Mathf.Sqrt(s), n);
            Chunks(p + n * 0.3f * s, 4, t + 1f / 30f, c1, 7f * Mathf.Sqrt(s), 0.7f * Mathf.Sqrt(s), n);
        }

        /// <summary>A round striking home: a star, sparks, a chip or two and a wisp.</summary>
        public void Hit(Vector3 p, float s = 1f, float? t0 = null)
        {
            float t = t0 ?? Now;
            AddFlash(FK.Hit, p, Vector3.up, s, t);
            AddPuff(p, Vector3.up * 0.6f * s, 0.45f * s, 0.6f, t + 1f / 30f, Trail, 0.6f, 0.6f * s, 2f);
            Chunks(p, 2, t, Metal, 4.5f * Mathf.Sqrt(s), 0.5f * s);
        }

        /// <summary>Chunks flung out of p, inked, tumbling, falling.</summary>
        public void Chunks(Vector3 p, int count, float t0, Color[] cols, float speed, float size = 1f, Vector3? away = null)
        {
            int seed = Seed(p, t0 + 0.25f);
            for (int i = 0; i < count; i++)
            {
                float a = Rng(seed, i * 3, 0f, Mathf.PI * 2f);
                Vector3 d = new Vector3(Mathf.Cos(a), Rng(seed, i * 3 + 1, 0.4f, 1.5f), Mathf.Sin(a)).normalized;
                if (away.HasValue && Vector3.Dot(d, away.Value) < 0f) d = Vector3.Reflect(d, away.Value);   // (out of the surface, not into it)
                AddShard(p, d * (speed * Rng(seed, i * 3 + 2, 0.55f, 1.15f)), t0, seed + i * 31, cols[i % cols.Length], Rng(seed, i + 300, 0.3f, 0.6f) * size, 1.4f);
            }
        }

        /// <summary>
        /// One puff of an aircraft's smoke trail at p (heavy: the thick black of one going down, else the thin grey of a
        /// hurt one). Puffs of one cluster (one aircraft's trail) are drawn as one outlined mass.
        /// </summary>
        public void TrailPuff(Vector3 p, Vector3 v, float r, bool heavy, int cluster, int seed, float? t0 = null)
        {
            float t = t0 ?? Now;
            if (heavy) AddPuff(p, v, r * Rng(seed, 1, 0.8f, 1.2f), Rng(seed, 2, 2.6f, 3.4f), t, Black, 2.6f, 1.6f, 1.2f, (seed & 15) == 0, cluster);
            else AddPuff(p, v, r * Rng(seed, 1, 0.8f, 1.2f), Rng(seed, 2, 1.6f, 2.2f), t, Trail, 2.0f, 1.0f, 1.2f, (seed & 15) == 0, cluster);
        }

        /// <summary>
        /// A dark column of smoke rising from p for a while (a wreck, a bombed target), with fire at its foot while it
        /// burns. s: its size (1 is a small wreck's; a factory's is 10 or more). seconds &lt; 0: until cleared.
        /// </summary>
        public void SmokeColumn(Vector3 p, float s, float seconds, float? t0 = null)
        {
            float t = t0 ?? Now;
            for (int i = 0; i < MaxColumns; i++)   // (a second hit by an old one renews it)
            {
                ref var q = ref columns[i];
                if (q.On && (q.P - p).sqrMagnitude < 4f * s * s) { q.Until = seconds < 0 ? float.MaxValue : Mathf.Max(q.Until, t + seconds); q.S = Mathf.Max(q.S, s); return; }
            }
            ref var c = ref columns[nextColumn];
            nextColumn = (nextColumn + 1) % MaxColumns;
            c = new Column { P = p, S = s, T0 = t, Until = seconds < 0 ? float.MaxValue : t + seconds, Last = t, Seed = Seed(p, t), On = true };
        }

        // ---------------------------------------------------------------------------------------------------------
        // Tracers: drawn fresh each frame, like the lines they replace (call Tracer and TracerSmoke every frame the
        // round is in the air; what was asked for last frame is dropped). The round is a hot white core in a soft glow
        // of its colour, brightest at the head; behind it a faint trail of the smoke's own puffs, pale and see-through,
        // swelling and rising a little as they age and gone in about half a second. Placed from the round's recorded
        // path and its age, never from a running pool, so a replay or a scrub back looks the same.

        struct TracerReq { public Vector3 Tail, Head; public Color C; public int Seed; }
        struct WispReq { public Vector3 P; public float Age, Life, R; public int Seed, Cluster; public bool Thick; }
        readonly List<TracerReq> tracerQ = new List<TracerReq>(256);
        readonly List<WispReq> wispQ = new List<WispReq>(2048);
        int tracerFrame = -1;
        const int MaxWisps = 1800;
        /// <summary>How long a tracer's smoke lasts, s.</summary>
        public const float TracerSmokeLife = 0.5f;
        /// <summary>How long a missile's smoke lasts, s.</summary>
        public const float MissileSmokeLife = 2.4f;

        void TracerFrame()
        {
            if (tracerFrame == Time.frameCount) return;
            tracerFrame = Time.frameCount;
            tracerQ.Clear(); wispQ.Clear();
        }

        /// <summary>A round in flight this frame: its glowing streak from tail to head, in its side's colour.</summary>
        public void Tracer(Vector3 tail, Vector3 head, Color color, int seed)
        {
            TracerFrame();
            tracerQ.Add(new TracerReq { Tail = tail, Head = head, C = color, Seed = seed });
        }

        /// <summary>
        /// A tracer's smoke this frame: path[i] is where the round was age0 + i * dt seconds ago (newest first), at the
        /// round's recorded frame frame0 - i * frameStep (which keeps each puff's own shape as it ages). Puffs of one
        /// cluster (one aircraft's guns) are drawn as one soft mass.
        /// </summary>
        public void TracerSmoke(IList<Vector3> path, float age0, float dt, int seed, int cluster, int frame0, int frameStep = 1)
            => Wisps(path, age0, dt, seed, cluster, frame0, frameStep, TracerSmokeLife, 0.7f, false);

        /// <summary>
        /// A missile's smoke this frame, as TracerSmoke: a fuller trail of light puffs, white with a cool lavender shade
        /// and no dark ink, in a soft haze like the cloud banks; bigger and slower to go, billowing out as it ages.
        /// </summary>
        public void MissileSmoke(IList<Vector3> path, float age0, float dt, int seed, int cluster, int frame0, int frameStep = 1)
            => Wisps(path, age0, dt, seed, cluster, frame0, frameStep, MissileSmokeLife, 1.3f, true);

        void Wisps(IList<Vector3> path, float age0, float dt, int seed, int cluster, int frame0, int frameStep, float life, float r, bool thick)
        {
            TracerFrame();
            for (int i = 0; i < path.Count && wispQ.Count < MaxWisps; i++)
            {
                float age = age0 + i * dt;
                if (age >= life) break;
                wispQ.Add(new WispReq { P = path[i], Age = age, Life = life, R = r, Thick = thick, Seed = seed * 131 + frame0 - i * frameStep, Cluster = cluster });
            }
        }

        // pale see-through smoke: fills paling with age, a soft grey line
        static readonly Color[] WispFill = { new Color(0.93f, 0.91f, 0.95f, 0.5f), new Color(0.86f, 0.84f, 0.89f, 0.38f), new Color(0.79f, 0.77f, 0.83f, 0.24f) };
        static readonly Color WispLine = new Color(0.55f, 0.52f, 0.6f, 0.3f);
        static readonly Color[] MissileFill = { new Color(0.98f, 0.97f, 0.99f, 0.92f), new Color(0.92f, 0.91f, 0.94f, 0.78f), new Color(0.85f, 0.84f, 0.88f, 0.5f) };
        static readonly Color MissileLine = new Color(0.79f, 0.76f, 0.85f, 0.6f), MissileHaze = new Color(1f, 1f, 1f, 0.22f);
        readonly Dictionary<int, int> wispGroups = new Dictionary<int, int>(16);

        void TracerWisps()
        {
            if (tracerFrame != Time.frameCount && ClockOverride < 0f) return;   // (nobody asked this frame)
            wispGroups.Clear();
            for (int i = 0; i < wispQ.Count; i++)
            {
                var w = wispQ[i];
                if (w.Age < 0.04f) continue;   // (the glow covers its first few metres)
                // stepped like every other puff: one size a key
                float t = Mathf.Floor(w.Age * KeysPerSec) / KeysPerSec, u = t / w.Life;
                float s = u < 0.15f ? 0.6f + 0.4f * (u / 0.15f) : 1f + (w.Thick ? 2.6f : 1.6f) * (u - 0.15f) / 0.85f;
                if (u > 0.7f) s *= Mathf.Max(0f, 1f - (u - 0.7f) / 0.3f);
                if (s <= 0f) continue;
                if (!wispGroups.TryGetValue(w.Cluster, out int g))
                {
                    var line = Mul(w.Thick ? MissileLine : WispLine, tint);
                    Group(line, line, 1f);
                    wispGroups[w.Cluster] = curGroup;
                }
                else curGroup = g;
                Vector3 p = w.P + Vector3.up * ((w.Thick ? 0.6f : 0.9f) * t) + right * Rng(w.Seed, 1, -0.25f, 0.25f) * t * (w.Thick ? 2f : 1f);
                var fills = w.Thick ? MissileFill : WispFill;
                var fill = Mul(u < 0.3f ? fills[0] : u < 0.6f ? fills[1] : fills[2], tint);
                // (a missile's: a swirl now and then, like the other trails)
                float r = w.R * s * Rng(w.Seed, 2, 0.8f, 1.2f);
                Add(p, r, fill, w.Thick && (w.Seed & 15) == 0, w.Seed, 3 + (w.Seed % 3));
                // (a missile's sits in a soft white haze, like the cloud banks, drawn under the puffs)
                if (w.Thick && (w.Seed & 1) == 0 && Vector3.Dot(p - camPos, camFwd) > 0.5f)
                    SoftDisc(p, r * 2f, Mul(new Color(MissileHaze.r, MissileHaze.g, MissileHaze.b, MissileHaze.a * fill.a), tint), 14);
            }
        }

        void DrawTracers(float now)
        {
            if (tracerFrame != Time.frameCount && ClockOverride < 0f) return;
            int key = Mathf.FloorToInt(now * KeysPerSec);
            for (int i = 0; i < tracerQ.Count; i++)
            {
                var q = tracerQ[i];
                float dh = Vector3.Dot(q.Head - camPos, camFwd), dt = Vector3.Dot(q.Tail - camPos, camFwd);
                if (dh < 0.5f && dt < 0.5f) continue;
                Vector3 head = q.Head, tail = q.Tail;
                // (clip the end behind the camera, so the strip never turns inside out)
                if (dh < 0.5f) head = Vector3.Lerp(tail, head, (dt - 0.5f) / Mathf.Max(1e-3f, dt - dh));
                else if (dt < 0.5f) tail = Vector3.Lerp(head, tail, (dh - 0.5f) / Mathf.Max(1e-3f, dh - dt));
                dh = Mathf.Max(0.5f, dh); dt = Mathf.Max(0.5f, dt);
                float mh = dh / pxPerM1, mt = dt / pxPerM1;   // (metres a pixel, at each end)
                float flick = 0.85f + 0.3f * R01(q.Seed, key);   // (a little shimmer, stepped)
                Color c = q.C;
                Color glow = new Color(c.r, c.g, c.b, 0.32f * flick), inner = new Color(c.r, c.g, c.b, 0.6f);
                Color hot = Color.Lerp(new Color(c.r, c.g, c.b, 1f), new Color(1f, 0.98f, 0.9f, 1f), 0.6f);
                float haloH = Mathf.Max(1.1f, 10f * mh) * flick, haloT = Mathf.Max(0.5f, 4f * mt);
                float coreH = Mathf.Max(0.2f, 2.4f * mh), coreT = Mathf.Max(0.05f, 0.8f * mt);
                // the wide soft glow along the streak, then a tighter brighter one, then the hard white-hot core
                SoftStroke(tail, head, haloT, haloH, glow);
                SoftStroke(Vector3.Lerp(tail, head, 0.4f), head, haloT * 0.3f, haloH * 0.45f, inner);
                pts.Clear(); pts.Add(tail); pts.Add(Vector3.Lerp(tail, head, 0.7f)); pts.Add(head);
                Stroke(pts, coreT, coreH, hot);
                // the head: a round soft bloom and a white spot
                SoftDisc(head, haloH * 0.85f, new Color(c.r, c.g, c.b, 0.45f * flick), 16);
                Circle(head, right, up, coreH * 0.75f, 10, new Color(1f, 0.99f, 0.94f, 1f));
            }
        }

        // a soft band from a to b, flat to the camera: full colour down its middle, clear at its edges; its width goes
        // from w0 to w1
        void SoftStroke(Vector3 a, Vector3 b, float w0, float w1, Color col)
        {
            Vector3 side = Vector3.Cross(b - a, toCam);
            if (side.sqrMagnitude < 1e-8f) return;
            side.Normalize();
            Color clear = new Color(col.r, col.g, col.b, 0f);
            int m = verts.Count;
            verts.Add(a - side * (w0 * 0.5f)); cols.Add(clear);
            verts.Add(a); cols.Add(col);
            verts.Add(a + side * (w0 * 0.5f)); cols.Add(clear);
            verts.Add(b - side * (w1 * 0.5f)); cols.Add(clear);
            verts.Add(b); cols.Add(col);
            verts.Add(b + side * (w1 * 0.5f)); cols.Add(clear);
            for (int k = 0; k < 2; k++)
            {
                int p = m + k, q = m + 3 + k;
                tris.Add(p); tris.Add(q); tris.Add(p + 1); tris.Add(p + 1); tris.Add(q); tris.Add(q + 1);
            }
        }

        // a round glow facing the camera: full colour at its middle, clear at its rim
        void SoftDisc(Vector3 c, float r, Color col, int segs)
        {
            Color clear = new Color(col.r, col.g, col.b, 0f);
            int m = verts.Count;
            verts.Add(c); cols.Add(col);
            for (int i = 0; i < segs; i++)
            {
                float a = i * Mathf.PI * 2f / segs;
                verts.Add(c + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * r); cols.Add(clear);
            }
            for (int i = 0; i < segs; i++) { tris.Add(m); tris.Add(m + 1 + i); tris.Add(m + 1 + (i + 1) % segs); }
        }

        // ---------------------------------------------------------------------------------------------------------
        // drawing, each frame

        Camera cam;
        Vector3 right, up, toCam, camPos, camFwd;
        float pxPerM1, screenH;
        readonly float[] radii = new float[64];

        void LateUpdate() => Draw(Camera.main);

        /// <summary>Rebuild the mesh for this camera (shots call it by hand after placing their camera).</summary>
        public void Draw(Camera c)
        {
            cam = c;
            verts.Clear(); cols.Clear(); tris.Clear();
            flats.Clear(); groups.Clear(); curGroup = -1;
            if (cam != null)
            {
                float now = Now;
                right = cam.transform.right; up = cam.transform.up; toCam = -cam.transform.forward;
                camPos = cam.transform.position; camFwd = cam.transform.forward;
                screenH = Mathf.Max(1f, cam.pixelHeight);
                pxPerM1 = screenH / (2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
                SceneTint();
                DrawShards(now);
                Columns(now);
                PoolPuffs(now);
                TracerWisps();
                blastOrder.Clear();
                for (int i = 0; i < MaxFlashes; i++) if (flashes[i].On && flashes[i].K == FK.Blast && flashes[i].T0 <= now) blastOrder.Add(i);
                blastOrder.Sort((x, y) => flashes[y].T0.CompareTo(flashes[x].T0));   // (newest first)
                for (int b = 0; b < blastOrder.Count; b++)
                {
                    ref var f = ref flashes[blastOrder[b]];
                    if (b < MaxBlastsDrawn) BlastPuffs(ref f, now);
                    else if (Key(now, f.T0) > BlastKeys) f.On = false;
                }
                DrawGroups();
                DrawTracers(now);
                DrawFlashes(now);
            }
            if (mesh == null) return;
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }

        public int Triangles => tris.Count / 3;

        // ---- puffs in groups: each group's puffs are drawn as one outlined mass, groups back to front
        struct Flat { public Vector3 P; public float R, D, Px, GD; public Color Fill; public bool Swirl; public int Seed, Group, Index; public byte Lobes; }
        struct GroupStyle { public Color Line, SwirlCol; public float Bias, Sum; public int Count; }
        readonly List<Flat> flats = new List<Flat>(4096);
        readonly List<GroupStyle> groups = new List<GroupStyle>(512);
        int curGroup = -1;
        static readonly System.Comparison<Flat> Order = (a, b) =>
        {
            int c = b.GD.CompareTo(a.GD);   // (far groups first)
            if (c != 0) return c;
            c = a.Group.CompareTo(b.Group);
            return c != 0 ? c : a.Index.CompareTo(b.Index);
        };

        // a new group of puffs drawn as one mass (bias < 0: drawn over the groups round it)
        void Group(Color line, Color swirlCol, float bias = 0f)
        {
            groups.Add(new GroupStyle { Line = line, SwirlCol = swirlCol, Bias = bias });
            curGroup = groups.Count - 1;
        }

        void Add(Vector3 p, float r, Color fill, bool swirl = false, int seed = 0, int lobes = 4)
        {
            if (r < 0.03f || curGroup < 0) return;
            float d = Vector3.Dot(p - camPos, camFwd);
            if (d < 0.3f) return;
            float px = r * pxPerM1 / d;
            if (px < 0.8f) return;   // (smaller than a pixel: skip)
            flats.Add(new Flat { P = p, R = r, D = d, Px = px, Fill = fill, Swirl = swirl, Seed = seed, Lobes = (byte)lobes, Group = curGroup, Index = flats.Count });
            var g = groups[curGroup]; g.Sum += d; g.Count++; groups[curGroup] = g;
        }

        // smoke takes the scene's light a little (grey puffs at dusk are not paper grey); fire does not
        Color tint = Color.white;
        static Color Mul(Color a, Color b) => new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a);

        void SceneTint()
        {
            var sun = RenderSettings.sun;
            if (sun == null) { tint = Color.white; return; }
            Color c = sun.color;
            float mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (mx > 1e-3f) c /= mx;   // (its hue, not its strength)
            tint = Color.Lerp(Color.white, c, 0.45f);
        }

        void DrawGroups()
        {
            for (int i = 0; i < flats.Count; i++)
            {
                var f = flats[i];
                var g = groups[f.Group];
                f.GD = g.Sum / Mathf.Max(1, g.Count) + g.Bias;
                flats[i] = f;
            }
            flats.Sort(Order);
            int start = 0;
            while (start < flats.Count)
            {
                int end = start;
                while (end < flats.Count && flats[end].Group == flats[start].Group) end++;
                var g = groups[flats[start].Group];
                // the line: each puff's shape grown by a line's width (at least ~1.6 pixels)
                for (int i = start; i < end; i++)
                {
                    var f = flats[i];
                    float lw = Mathf.Max(f.R * 0.1f, 1.6f * f.D / pxPerM1);
                    Cloud(f.P, f.R + lw, f.Lobes, f.Seed, g.Line, f.Px);
                }
                // two-tone cel shading inside the mass: every puff in its shade tone, then its lit tone a little smaller and
                // shifted up toward the light, so each lobe keeps a crisp shade crescent on its lower side
                for (int i = start; i < end; i++) { var f = flats[i]; Cloud(f.P, f.R, f.Lobes, f.Seed, ShadeOf(f.Fill), f.Px); }
                for (int i = start; i < end; i++)
                {
                    var f = flats[i];
                    if (f.Px < 5f) { Cloud(f.P, f.R, f.Lobes, f.Seed, f.Fill, f.Px); continue; }
                    Cloud(f.P + (up * 0.2f - right * 0.09f) * f.R, f.R * 0.8f, f.Lobes, f.Seed, f.Fill, f.Px * 0.8f);
                }
                // (at most one swirl a mass: two side by side read as a pair of eyes)
                for (int i = start; i < end; i++) { var f = flats[i]; if (f.Swirl && f.Px > 10f) { Swirl(f.P + up * (0.06f * f.R), f.R * 0.8f, f.Seed, g.SwirlCol); break; } }
                start = end;
            }
        }

        // the cel shade tone of a fill: darker, cooler (a purple shade on charcoal smoke, a red-orange shade on yellow fire)
        static Color ShadeOf(Color c)
        {
            if (c.r > c.b + 0.25f) return new Color(c.r * 0.9f, c.g * 0.62f, c.b * 0.55f, c.a);   // (fire: deeper, redder)
            return new Color(c.r * 0.74f, c.g * 0.78f, Mathf.Min(1f, c.b * 0.9f + 0.06f), c.a);
        }

        // a little cloud facing the camera: round bumps (lobes) with cusps between them
        void Cloud(Vector3 c, float r, int lobes, int seed, Color col, float px)
        {
            if (px < 6f || lobes < 2) { Circle(c, right, up, r, px < 3f ? 8 : px < 20f ? 14 : 24, col); return; }
            int detail = px > 110f ? 2 : px > 40f ? 1 : 0, lb = Mathf.Clamp(lobes, 2, 5), ph = seed & 7;
            var unit = CloudShape(lb, ph, detail);
            float rot = R01(seed, 4) * 6.283f;
            for (int i = 0; i < unit.Length; i++) radii[i] = r * unit[i];
            Shape(c, right, up, radii, unit.Length, rot, col);
        }

        static readonly float[][] cloudCache = new float[6 * 8 * 3][];
        static float[] CloudShape(int lobes, int phase, int detail)
        {
            int idx = (lobes * 8 + phase) * 3 + detail;
            var s = cloudCache[idx];
            if (s != null) return s;
            int n = Mathf.Clamp(lobes * (detail == 2 ? 10 : detail == 1 ? 6 : 4), 12, 50);   // (enough sides that no facets show)
            s = new float[n];
            float ph = phase * Mathf.PI / 8f;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                s[i] = 0.84f + 0.16f * Mathf.Pow(Mathf.Abs(Mathf.Sin(a * lobes * 0.5f + ph)), 0.55f);
            }
            return cloudCache[idx] = s;
        }

        readonly List<Vector3> pts = new List<Vector3>(64), pts2 = new List<Vector3>(64);
        void Swirl(Vector3 c, float r, int seed, Color col)
        {
            pts.Clear();
            float a0 = R01(seed, 9) * 6.28f;
            for (int i = 0; i <= 12; i++)
            {
                float u = i / 12f, a = a0 + u * Mathf.PI * 2.4f, rr = r * (0.08f + 0.42f * u);
                pts.Add(c + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * rr + toCam * 0.01f);
            }
            Stroke(pts, r * 0.07f, r * 0.14f, col);
        }

        // ---- THE BLAST, drawn from where, when, how big and which way it threw. Its value runs HOT, then COOL, then DARK:
        // keys 0-1 white-hot, then pale yellow, yellow, orange, red, a maroon ember, then charcoal purple-grey smoke rising
        // as a plume (~3 s). The core is 4 LARGE lobes and a few satellites; trails shoot up in a V, big at the head, small
        // at the tail, cooling first; a ring of brick-red puffs spreads at its base.
        static readonly Color[] StageFill = { Hex("#ffffff"), Hex("#fff4b8"), Hex("#ffc93a"), Hex("#ff8c1e"), Hex("#d8401f"), Hex("#6e2a2a"), Hex("#3b3447"), Hex("#2d2836"), Hex("#4d4858") };
        static readonly Color[] StageLine = { Hex("#ffd86a"), Hex("#ffb02a"), Hex("#e0760e"), Hex("#c2440e"), Hex("#7a1c0e"), Hex("#3c1414"), Hex("#1a1622"), Hex("#15121b"), Hex("#2a2632") };
        // stage by key: 0-1 white-hot, 2 pale yellow, 3-4 yellow, 5-6 orange, 7-9 red, 10-12 ember, 13-30 charcoal, 31-40 dark, then paling
        static int StageOf(int k) => k <= 1 ? 0 : k <= 2 ? 1 : k <= 4 ? 2 : k <= 6 ? 3 : k <= 9 ? 4 : k <= 12 ? 5 : k <= 30 ? 6 : k <= 40 ? 7 : 8;
        static readonly Color Brick = Hex("#b6300e"), BrickLine = Hex("#6e1a08");
        const int BlastKeys = 50;   // (about 3.3 s: the plume lingers)
        const float HeroScreen = 0.12f;   // (a blast is at least this share of the screen tall, at any distance)
        readonly List<int> blastOrder = new List<int>(16);
        static readonly Vector3[] Lobes4 = { new Vector3(0f, 0.8f, 0f), new Vector3(-0.85f, 0.32f, 0.1f), new Vector3(0.82f, 0.38f, 0.05f), new Vector3(0.05f, 0.05f, 0.35f) };
        static readonly float[] LobeR = { 1.0f, 0.82f, 0.86f, 0.78f };

        // how much bigger to draw an effect so it is at least minFrac of the screen's height tall (sizeM: its height in
        // metres at scale 1), at most 3 times
        float MinScreen(Vector3 p, float sizeM, float minFrac)
        {
            float d = Mathf.Max(1f, Vector3.Dot(p - camPos, camFwd));
            float px = sizeM * pxPerM1 / d;
            float want = minFrac * screenH;
            return px >= want ? 1f : Mathf.Min(3f, want / Mathf.Max(1f, px));
        }

        float BlastScale(in Flash f) => f.S * MinScreen(f.P, 5f * f.S, HeroScreen);

        void BlastPuffs(ref Flash f, float now)
        {
            int key = Key(now, f.T0);
            if (key < 0) return;
            if (key > BlastKeys) { f.On = false; return; }
            Vector3 P0 = f.P;
            float s = BlastScale(f);
            int seed = f.Seed;
            Vector3 n = f.D.sqrMagnitude > 0.01f ? f.D.normalized : Vector3.up;
            SurfaceAxes(n, out Vector3 e1, out Vector3 e2);
            Vector3 c = P0 + n * (0.45f * s) + Vector3.up * (0.55f * s) + toCam * (0.6f * s);
            // on the ground: the warm light it throws round it, the first keys (a hard-edged scalloped pool, stepping down)
            if (f.Ground && key <= 3)
            {
                float la = key == 0 ? 0.5f : key == 1 ? 0.38f : key == 2 ? 0.26f : 0.14f;
                SurfaceAxes(Vector3.up, out Vector3 d1, out Vector3 d2);
                for (int k = 0; k < 18; k++) radii[k] = s * (2.4f - 0.25f * key) * ((k & 1) == 0 ? 1f : 0.86f);
                Shape(P0 + Vector3.up * 0.3f, d1, d2, radii, 18, 0f, new Color(1f, 0.6f, 0.2f, la));
            }
            // the burst at its base: clusters of brick-red puffs spreading out (above and beside it, never dripping below)
            Group(BrickLine, BrickLine, 2f * s);
            const int RingN = 9;
            float spread = s * (0.8f + 1.9f * Mathf.Min(1f, key / 6f));
            for (int i = 0; i < RingN; i++)
            {
                float sc = Pop(key, 12, 20, i % 3);
                if (sc <= 0f) continue;
                float a = (i + Rng(seed, i + 500, -0.3f, 0.3f)) * Mathf.PI * 2f / RingN;
                Vector3 d = f.Ground ? e1 * Mathf.Cos(a) + e2 * Mathf.Sin(a) : right * Mathf.Cos(a) + up * Mathf.Sin(a);
                if (d.y < -0.3f && f.Ground) continue;
                Vector3 p0 = P0 + n * (0.3f * s) + d * spread * Rng(seed, i + 520, 0.8f, 1.1f);
                float r = s * Rng(seed, i + 540, 0.4f, 0.54f) * sc;
                int st = key < 9 ? -1 : StageOf(key);
                Color fill = st < 0 ? Brick : StageFill[st];
                Add(p0, r, fill, false, seed + i, 4);
                Add(p0 + (d * 0.75f + n * 0.3f) * r, r * 0.7f, fill, false, seed + i + 50, 3);
            }
            // the trails: 2-3 chains shooting up in a V, big at the head and tapering to the tail, jittered and overlapping
            int trails = R01(seed, 600) < 0.55f ? 3 : 2;
            Vector3 root = c + Vector3.up * (0.7f * s);
            for (int j = 0; j < trails; j++)
            {
                float tilt = (trails == 3 ? Tilt3[j] : Tilt2[j]) + Rng(seed, j + 610, -14f, 14f);
                float tr = tilt * Mathf.Deg2Rad;
                Vector3 d = (Vector3.up * Mathf.Cos(tr) + right * Mathf.Sin(tr) + n * 0.25f).normalized;
                float len = s * Rng(seed, j + 630, 4.4f, 5.8f) * TrailLen[(j + (seed & 3)) % 4];   // (unequal: never a mirrored pair of ears)
                float bend = (tilt >= 0f ? 1f : -1f) * Rng(seed, j + 640, 0.2f, 0.45f);   // (each trail curves outward as it flies)
                int headStage = StageOf(key + 1);   // (the head cools first)
                Group(StageLine[Mathf.Min(headStage, 7)], StageLine[4], 3f * s);   // (always behind the core)
                const int M = 11;
                float reach = Mathf.Min(1f, (key + 1) / 5f);
                float rise = key > 10 ? (key - 10) / KeysPerSec * 0.6f * s : 0f;
                for (int k = 0; k < M; k++)
                {
                    float u = Mathf.Clamp01((k + 1 + Rng(seed, j * 30 + k + 700, -0.35f, 0.35f)) / M);
                    if (u > reach + 1e-3f || u < 0.22f) continue;   // (its root is inside the core)
                    float sc = Pop(key - (int)(u * 2f), 14, 24, (M - k) / 3);
                    if (sc <= 0f) continue;
                    Vector3 p = root + d * (len * u) + right * (bend * len * u * u) + Vector3.down * (0.08f * len * u * u) + Vector3.up * rise
                        + (right * Rng(seed, j * 20 + k + 640, -0.28f, 0.28f) + Vector3.up * Rng(seed, j * 20 + k + 660, -0.18f, 0.18f)) * s;
                    float r = s * (0.18f + 0.5f * u * u + 0.08f * u) * Rng(seed, j * 20 + k + 680, 0.8f, 1.2f) * sc;   // (big head, small tail)
                    int st = Mathf.Clamp(StageOf(key + 1 + Mathf.RoundToInt(u * 3f)), 1, 8);
                    Add(p, r, StageFill[st], k == M - 1, seed + j * 40 + k, 4);   // (each ends in a swirl)
                }
            }
            // the core: 4 big lobes and a few satellites, one mass; from key 10 it cools and rises as the plume
            int cs = StageOf(key);
            Group(StageLine[cs], StageLine[cs], -6f * s);   // (always over its trails and base burst)
            float smokeT = key > 9 ? (key - 9) / KeysPerSec : 0f;
            float pop = key == 0 ? 0.85f : key == 1 ? 1.2f : key == 2 ? 1.08f : 1f;   // (pops past full size, then settles)
            float grow = 1f + Mathf.Min(0.45f, smokeT * 0.22f);
            for (int i = 0; i < 4; i++)
            {
                Vector3 l = Lobes4[i];
                int fade = key - (i == 3 ? 22 : 34 + i * 2);   // (the low lobe goes first; the top lingers)
                float sc = pop * (fade > 0 ? Mathf.Max(0f, 1f - fade / 9f) : 1f);
                if (sc <= 0f) continue;
                Vector3 o = (right * l.x + Vector3.up * l.y + toCam * l.z) * (s * 1.25f);
                Vector3 plume = Vector3.up * (smokeT * (1.4f + 1.6f * l.y) * Mathf.Sqrt(s)) + right * (smokeT * 0.2f * (l.x >= 0f ? 1f : -1f));
                int st = StageOf(key + (l.y > 0.6f ? 1 : 0));
                Add(c + o + plume, s * LobeR[i] * Rng(seed, i + 730, 0.92f, 1.08f) * sc * grow, StageFill[st], false, seed + i * 7, 5);
            }
            for (int i = 0; i < 6; i++)
            {
                float sc = pop * Pop(key, 8, 14, i % 3);
                if (sc <= 0f) continue;
                float a = (i + Rng(seed, i + 750, -0.25f, 0.25f)) * Mathf.PI * 2f / 6f + R01(seed, 751) * 6.28f;
                Vector3 o = (right * Mathf.Cos(a) + Vector3.up * (0.35f + 0.65f * Mathf.Abs(Mathf.Sin(a)))) * (s * 1.65f);
                Vector3 plume = Vector3.up * (smokeT * 1.8f * Mathf.Sqrt(s));
                int st = StageOf(key + 1);
                Add(c + o + plume, s * Rng(seed, i + 760, 0.3f, 0.44f) * sc * grow, StageFill[st], false, seed + i * 11 + 3, 3);
            }
            // on the ground, a flattened contact puff where the blast meets it
            if (f.Ground)
            {
                float sc = Pop(key, 10, 18, 0);
                int st = StageOf(key + 2);
                for (int k = -1; k <= 1 && sc > 0f; k++)
                    Add(P0 + n * (0.3f * s) + right * (k * 0.62f * s) + toCam * (0.3f * s), s * (k == 0 ? 0.6f : 0.46f) * sc, StageFill[st], false, seed + 90 + k, 4);
            }
            // the shockwave: a bold broken ring racing out (flat on the ground, or facing the camera in the air)
            if (key <= 4)
            {
                float rr = s * (1.3f + 0.55f * key);
                if (f.Ground) TaperRing(P0 + n * 0.15f * s, e1, e2, rr, s * (0.3f - 0.05f * key), seed, 3, new Color(1f, 0.98f, 0.9f, 1f));
                if (key >= 1) TaperRing(c + toCam * (0.4f * s), right, up, rr * 1.2f, s * (0.24f - 0.04f * key), seed + 5, 4, new Color(1f, 0.95f, 0.8f, 1f));
            }
        }

        // a ring of tapered arcs (thick in the middle of each arc, thin to nothing at its ends), flat in (ex, ez)
        void TaperRing(Vector3 c, Vector3 ex, Vector3 ez, float r, float w, int seed, int arcs, Color col)
        {
            float a0 = R01(seed, 31) * 6.283f, span = 6.283f / arcs;
            for (int k = 0; k < arcs; k++)
            {
                pts.Clear(); pts2.Clear();
                float start = a0 + k * span, len = span * Rng(seed, k + 32, 0.55f, 0.8f);
                const int N = 12;
                for (int i = 0; i <= N; i++)
                {
                    float u = i / (float)N, a = start + len * u;
                    float ww = w * Mathf.Pow(Mathf.Sin(Mathf.PI * u), 0.7f);
                    Vector3 d = ex * Mathf.Cos(a) + ez * Mathf.Sin(a);
                    pts.Add(c + d * (r - ww * 0.5f)); pts2.Add(c + d * (r + ww * 0.5f));
                }
                Strip(pts, pts2, col);
            }
        }

        static void SurfaceAxes(Vector3 n, out Vector3 e1, out Vector3 e2)
        {
            e1 = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            e2 = Vector3.Cross(n, e1).normalized;
        }

        // pop out, hold, then shrink away in steps (a scale for each key; 0 = gone). stagger: keys late to start shrinking
        static float Pop(int key, int hold, int gone, int stagger)
        {
            if (key < 0) return 0f;
            if (key == 0) return 0.55f;
            if (key == 1) return 0.85f;
            int k = key - stagger;
            if (k < hold) return 1f;
            if (k >= gone) return 0f;
            return 1f - (k - hold + 1) / (float)(gone - hold + 1);
        }

        // ---- smoke columns: dark puffs keep rising from the spot, small and tight at the foot, swelling as they climb
        void Columns(float now)
        {
            const float Every = 2f / KeysPerSec;   // (a puff every other key)
            for (int i = 0; i < MaxColumns; i++)
            {
                ref var q = ref columns[i];
                if (!q.On) continue;
                if (now > q.Until + 12f) { q.On = false; continue; }
                float s = q.S, life = Mathf.Min(7f, 2.8f * Mathf.Sqrt(s));
                if (now < q.T0 + 0.5f) { q.Last = now; continue; }   // (the blast's own plume first)
                if (now <= q.Until) FootFire(q.P + Vector3.up * 0.3f * s, s, q.Seed, now);
                // every puff due since the last frame (a shot's first frame back-fills the column already risen)
                float from = Mathf.Max(q.Last, q.T0 + 0.5f, now - life);
                int k0 = Mathf.CeilToInt(from / Every), k1 = Mathf.FloorToInt(Mathf.Min(now, q.Until) / Every);
                q.Last = now;
                Vector3 drift = new Vector3(0.7f, 0f, 0.3f) * (0.6f * Mathf.Sqrt(s));   // (it leans away with the wind, more the higher it gets)
                for (int k = k0; k <= k1; k++)
                {
                    if (k * Every <= from - 1e-4f && k0 != k1) continue;
                    int seed = k * 7 + i * 131 + q.Seed;
                    if (R01(seed, 9) < 0.18f) continue;   // (gaps)
                    Vector3 side = right * Rng(seed, 3, -0.35f, 0.35f) * s;
                    AddPuff(q.P + Vector3.up * 0.4f * s + side * 0.3f, new Vector3(0f, 2.2f * s, 0f) + side * 0.5f, 0.27f * s * Rng(seed, 4, 0.6f, 1.5f), life, k * Every, ColumnS, 3.2f, 1.9f * s, 0.9f, false, 7000 + i);
                    puffs[(nextPuff + MaxPuffs - 1) % MaxPuffs].Drift = drift;
                }
            }
        }

        // ---- the pooled puffs (smoke trails, columns, wisps)
        readonly Dictionary<int, int> clusterGroups = new Dictionary<int, int>(64);
        void PoolPuffs(float now)
        {
            clusterGroups.Clear();
            for (int i = 0; i < MaxPuffs; i++)
            {
                ref var q = ref puffs[i];
                if (!q.On) continue;
                float age = now - q.T0;
                if (age < 0f) continue;
                if (age >= q.Life) { q.On = false; continue; }
                float t = Mathf.Floor(age * KeysPerSec) / KeysPerSec, u = t / q.Life;
                // pop out, swell, then shrink to nothing (in steps: one size a key)
                float s = u < 0.1f ? 0.55f + 0.45f * (u / 0.1f) : 1f + q.Grow * (u - 0.1f) / 0.9f;
                if (u > 0.62f) s *= Mathf.Max(0f, 1f - (u - 0.62f) / 0.38f);
                float glide = q.Drag > 0f ? (1f - Mathf.Exp(-q.Drag * t)) / q.Drag : t;
                Vector3 p = q.P + q.V * glide + Vector3.up * (q.Rise * t) + q.Drift * (t * t * 0.35f);
                var fills = Fills[q.Style];
                Color fill = Mul(u < 0.28f ? fills[0] : u < 0.56f ? fills[1] : fills[2], tint);
                // (puffs of one event - one aircraft's trail, one column - are one mass; others each their own)
                if (q.Cluster == 0 || !clusterGroups.TryGetValue(q.Cluster, out int cg)) { Group(Mul(Lines[q.Style], tint), Mul(Lines[q.Style], tint)); if (q.Cluster != 0) clusterGroups[q.Cluster] = curGroup; }
                else curGroup = cg;
                if (q.Style == ColumnS && u > 0.58f)
                {
                    // the top of a column thins into separate wisps: each old puff tears off as a little cloud of its own with
                    // a tapering tail of smaller puffs back down toward the column, lighter as it climbs
                    float br = (u - 0.58f) / 0.42f;
                    float sideW = R01(i, 5) < 0.5f ? 1f : -1f;
                    Vector3 apart = (right * sideW + Vector3.up * 0.5f) * (q.R * 2.4f * br);
                    float rr = q.R * (1f + q.Grow * 0.42f) * (1f - br * 0.85f) * 0.7f;
                    Color wisp = Mul(Color.Lerp(fills[2], Hex("#b9b1c4"), br), tint);
                    Group(Mul(Lines[q.Style], tint), Mul(Lines[q.Style], tint));
                    Add(p + apart, rr, wisp, false, i * 13 + (int)(q.T0 * 30f), 3);
                    Add(p + apart * 0.45f - Vector3.up * (rr * 0.6f), rr * 0.55f, wisp, false, i * 13 + 2, 2);
                    Add(p + apart * 0.1f - Vector3.up * (rr * 1.1f), rr * 0.3f, wisp, false, i * 13 + 3, 2);
                    continue;
                }
                Add(p, q.R * s, fill, q.Swirl, i * 13 + (int)(q.T0 * 30f), 3 + (i % 3));
            }
        }

        // ---- chunks: inked bits tumbling on arcs, flat to the camera
        void DrawShards(float now)
        {
            for (int i = 0; i < MaxShards; i++)
            {
                ref var s = ref shards[i];
                if (!s.On) continue;
                float age = now - s.T0;
                if (age < 0f) continue;
                if (age > s.Life) { s.On = false; continue; }
                float t = Mathf.Floor(age * KeysPerSec * 2f) / (KeysPerSec * 2f);
                Vector3 p = s.P + s.V * t + Vector3.down * (0.5f * Gravity * t * t) + toCam * 0.05f;
                float rot = s.Rot0 + s.Spin * t, sc = age > s.Life - 0.3f ? (s.Life - age) / 0.3f : 1f;
                Vector3 a = right * Mathf.Cos(rot) + up * Mathf.Sin(rot), b = Vector3.Cross(toCam, a);
                float d = Mathf.Max(0.3f, Vector3.Dot(p - camPos, camFwd)), lw = Mathf.Max(s.Thick * 0.25f, 1.4f * d / pxPerM1);
                Quad(p, a * (s.Len * 0.5f * sc + lw), b * (s.Thick * 0.5f * sc + lw), ShardLine);
                Quad(p + toCam * 0.01f, a * (s.Len * 0.5f * sc), b * (s.Thick * 0.5f * sc), s.C);
            }
        }

        void DrawFlashes(float now)
        {
            for (int i = 0; i < MaxFlashes; i++)
            {
                ref var f = ref flashes[i];
                if (!f.On) continue;
                int key = Key(now, f.T0);
                if (key < 0) continue;
                bool alive = true;
                switch (f.K)
                {
                    case FK.Blast:
                        {
                            // a white-hot flash on the first key (over everything), a yellow star on the second; sparks fly
                            float s = BlastScale(f);
                            Vector3 n = f.D.sqrMagnitude > 0.01f ? f.D.normalized : Vector3.up;
                            Vector3 p = f.P + n * (0.5f * s) + toCam * (1.4f * s);
                            // (a round flash, no star spikes: a hot white disc in a pale yellow ring, then a smaller yellow one)
                            if (key == 0) { Circle(p, right, up, s * 2.6f, 28, new Color(1f, 0.92f, 0.5f, 0.55f)); Circle(p + toCam * 0.01f, right, up, s * 1.8f, 24, FlashWhite); }
                            if (key == 1) Circle(p, right, up, s * 1.5f, 22, new Color(1f, 0.92f, 0.36f, 0.8f));
                            Sparks(p, s, key, f.Seed, 6, 14f, 2.0f, (n + up).normalized * 0.6f);
                            if (key > BlastKeys) alive = false;
                        }
                        break;
                    case FK.Hit:
                        alive = HitLight(f.P + toCam * (1.2f * f.S), f.S * MinScreen(f.P, 1.2f * f.S, 0.03f), key, f.Seed);
                        break;
                }
                if (!alive) f.On = false;
            }
        }

        bool HitLight(Vector3 p, float s, int key, int seed)
        {
            if (key > 4) return false;
            float rot = R01(seed, 1) * 6.28f;
            if (key <= 1) Star(p, (key == 0 ? 0.95f : 1.2f) * s, 0.32f * s, 6, seed, key == 0 ? FlashWhite : FlashYellow, rot);
            if (key == 2) Star(p, 0.6f * s, 0.2f * s, 6, seed, FlashYellow, rot + 0.5f);
            Sparks(p, s, key, seed, 4, 7f, 0.65f);
            return true;
        }

        // short hot streaks flying out (ballistic), drawn as tapered wedges: head white, tail orange
        void Sparks(Vector3 p, float s, int key, int seed, int n, float speed, float len, Vector3? bias = null)
        {
            if (key > 6) return;
            float t = key / KeysPerSec + 0.03f;
            for (int i = 0; i < n; i++)
            {
                float a = Rng(seed, i + 200, 0f, 6.283f), el = Rng(seed, i + 220, -0.4f, 0.9f);
                Vector3 d = new Vector3(Mathf.Cos(a) * (1f - Mathf.Abs(el)), el, Mathf.Sin(a) * (1f - Mathf.Abs(el))).normalized;
                if (bias.HasValue) d = (d + bias.Value * 1.4f).normalized;
                float v = speed * Rng(seed, i + 240, 0.6f, 1.1f) * Mathf.Sqrt(s);
                Vector3 vel = d * v + Vector3.down * (Gravity * t);
                Vector3 head = p + d * v * t + Vector3.down * (0.5f * Gravity * t * t);
                Vector3 tail = head - vel.normalized * (len * s * (1f - key / 8f));
                pts.Clear(); pts.Add(tail); pts.Add(Vector3.Lerp(tail, head, 0.6f)); pts.Add(head);
                Stroke(pts, 0.02f, 0.42f * s, key <= 2 ? SparkHot : new Color(1f, 0.62f, 0.2f, 1f));
                if (key <= 3) Star(head, 0.55f * s * (1f - key * 0.2f), 0.14f * s, 4, seed + i, SparkHot, key * 0.4f);
            }
        }

        void Star(Vector3 c, float rOut, float rIn, int points, int seed, Color col, float rot = 0f)
        {
            int n = points * 2;
            for (int i = 0; i < n; i++) radii[i] = (i & 1) == 0 ? rOut * Rng(seed, i, 0.7f, 1.1f) : rIn;
            Shape(c, right, up, radii, n, rot, col);
        }

        // fire at a column's foot, in the blast's puff language: a low clump of round puffs, a yellow heart in orange
        // and red, each swelling and shrinking on its own stepped beat (no cartoon flame leaves)
        static readonly Color[] FireFill = { Hex("#ffd24a"), Hex("#ff9a2a"), Hex("#e8561e"), Hex("#b8361a") };
        void FootFire(Vector3 p, float s, int seed, float now)
        {
            int beat = Mathf.FloorToInt(now * KeysPerSec / 2f);
            Group(Hex("#8a2a10"), Hex("#8a2a10"), -0.5f * s);   // (over the smoke rising from it)
            for (int k = 0; k < 6; k++)
            {
                int ph = (beat + k * 3 + seed) & 3;
                float a = (k + Rng(seed, k, -0.3f, 0.3f)) * Mathf.PI * 2f / 6f;
                Vector3 o = right * (Mathf.Cos(a) * 0.55f * s) + Vector3.up * ((0.15f + 0.35f * Mathf.Abs(Mathf.Sin(a))) * s) + toCam * (0.2f * s);
                float r = s * (0.32f + 0.08f * ph) * Rng(seed, k + 20, 0.85f, 1.15f);
                Add(p + o + Vector3.up * (0.06f * ph * s), r, FireFill[2 + (k & 1)], false, seed + k * 5 + ph, 4);
            }
            for (int k = 0; k < 3; k++)
            {
                int ph = (beat + k * 2 + seed + 1) & 3;
                Vector3 o = right * ((k - 1) * 0.3f * s) + Vector3.up * ((0.35f + 0.1f * ph) * s) + toCam * (0.35f * s);
                Add(p + o, s * (0.26f + 0.05f * ph), FireFill[k == 1 ? 0 : 1], false, seed + 60 + k + ph, 4);
            }
        }

        // ---------------------------------------------------------------------------------------------------------
        // mesh building: flat, unlit, hard-edged shapes

        // a filled shape round c: radii[i] out at angle rot + i/n of a turn (a disc, a star, a scalloped blob)
        void Shape(Vector3 c, Vector3 ax, Vector3 ay, float[] rs, int n, float rot, Color col)
        {
            int m = verts.Count;
            verts.Add(c); cols.Add(col);
            for (int i = 0; i < n; i++)
            {
                float a = rot + i * Mathf.PI * 2f / n;
                verts.Add(c + (ax * Mathf.Cos(a) + ay * Mathf.Sin(a)) * rs[i]); cols.Add(col);
            }
            for (int i = 0; i < n; i++) { tris.Add(m); tris.Add(m + 1 + i); tris.Add(m + 1 + (i + 1) % n); }
        }

        void Circle(Vector3 c, Vector3 ax, Vector3 ay, float r, int segs, Color col)
        {
            int m = verts.Count;
            verts.Add(c); cols.Add(col);
            for (int i = 0; i < segs; i++)
            {
                float a = i * Mathf.PI * 2f / segs;
                verts.Add(c + (ax * Mathf.Cos(a) + ay * Mathf.Sin(a)) * r); cols.Add(col);
            }
            for (int i = 0; i < segs; i++) { tris.Add(m); tris.Add(m + 1 + i); tris.Add(m + 1 + (i + 1) % segs); }
        }

        void Polygon(Vector3 c, IList<Vector3> outline, Color col)
        {
            int m = verts.Count, n = outline.Count;
            verts.Add(c); cols.Add(col);
            for (int i = 0; i < n; i++) { verts.Add(outline[i]); cols.Add(col); }
            for (int i = 0; i < n; i++) { tris.Add(m); tris.Add(m + 1 + i); tris.Add(m + 1 + (i + 1) % n); }
        }

        void Quad(Vector3 c, Vector3 a, Vector3 b, Color col)
        {
            int m = verts.Count;
            verts.Add(c - a - b); verts.Add(c + a - b); verts.Add(c + a + b); verts.Add(c - a + b);
            cols.Add(col); cols.Add(col); cols.Add(col); cols.Add(col);
            tris.Add(m); tris.Add(m + 1); tris.Add(m + 2); tris.Add(m); tris.Add(m + 2); tris.Add(m + 3);
        }

        void Strip(IList<Vector3> inner, IList<Vector3> outer, Color col)
        {
            int n = Mathf.Min(inner.Count, outer.Count);
            if (n < 2) return;
            int m = verts.Count;
            for (int i = 0; i < n; i++) { verts.Add(inner[i]); cols.Add(col); verts.Add(outer[i]); cols.Add(col); }
            for (int i = 0; i < n - 1; i++)
            {
                int a = m + i * 2, b = a + 2;
                tris.Add(a); tris.Add(b); tris.Add(a + 1); tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
            }
        }

        // a hard-edged stroke along points, flat to the camera, its width going from w0 to w1
        void Stroke(IList<Vector3> line, float w0, float w1, Color col)
        {
            int n = line.Count;
            if (n < 2) return;
            int m = verts.Count;
            for (int i = 0; i < n; i++)
            {
                Vector3 t = line[Mathf.Min(i + 1, n - 1)] - line[Mathf.Max(i - 1, 0)];
                Vector3 side = Vector3.Cross(t, toCam);
                side = side.sqrMagnitude > 1e-8f ? side.normalized : up;
                float half = Mathf.Lerp(w0, w1, i / (float)(n - 1)) * 0.5f;
                verts.Add(line[i] - side * half); cols.Add(col);
                verts.Add(line[i] + side * half); cols.Add(col);
            }
            for (int i = 0; i < n - 1; i++)
            {
                int a = m + i * 2, b = a + 2;
                tris.Add(a); tris.Add(b); tris.Add(a + 1); tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
            }
        }
    }
}
