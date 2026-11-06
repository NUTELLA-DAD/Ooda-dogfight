using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ooda.Fx
{
    /// <summary>
    /// The island's forest: several hundred thousand trees, far too many for the terrain's own trees (they cost the CPU
    /// per tree every frame). Here they are cut into squares of Chunk metres, and each camera draws a square's trees of one
    /// kind with one instanced call: the full model when the square is near, the light far mesh to about 1.2 km, and past that
    /// the pines as plain cones without shadow. Squares outside the view or past MaxDistance are skipped whole, so the
    /// per-frame cost is a few thousand box tests, whatever the number of trees. Past CoarseDistance the cones are drawn by
    /// the kilometre square instead, so the forest out to 25 km is a few hundred calls.
    /// The trees (AlpineWorld writes them) are 7 floats each in a binary TextAsset: x, y, z, yaw (radians), width scale,
    /// height scale, kind; Local[kind] takes the kind's meshes to a tree of scale 1 standing at the origin.
    /// </summary>
    [ExecuteAlways]
    public class Forest : MonoBehaviour
    {
        public TextAsset trees;
        public Mesh[] near, far, low;           // by kind; low may be null (then far, out to LeafyDistance)
        public Material[] materials, lowMaterials;
        public Matrix4x4[] local;
        public float NearDistance = 180f, FarDistance = 1200f, LeafyDistance = 4000f, MaxDistance = 25000f;
        /// <summary>Past this the trees are drawn by the kilometre square instead (up to 1000 to a call), so the
        /// far forest costs a few hundred calls rather than thousands.</summary>
        public float CoarseDistance = 3000f;
        const float Chunk = 250f, Coarse = 1000f;
        const int Batch = 1000;

        struct Group { public int kind, parent; public Bounds bounds; public Matrix4x4[] m; }
        Group[] groups;
        struct Square { public Bounds bounds; public int kind; public List<Matrix4x4[]> m; }
        Square[] squares;
        float[] squareDistance;
        readonly Plane[] planes = new Plane[6];

        public int Count { get; private set; }

        void OnEnable()
        {
            Build();
            RenderPipelineManager.beginCameraRendering += Draw;
        }

        void OnDisable() => RenderPipelineManager.beginCameraRendering -= Draw;

        void Build()
        {
            groups = null; squares = null;
            if (trees == null || local == null) return;
            var bytes = trees.bytes;
            int n = bytes.Length / 28;
            var f = new float[n * 7];
            System.Buffer.BlockCopy(bytes, 0, f, 0, n * 28);
            Count = n;
            var lists = new Dictionary<long, List<int>>();
            for (int t = 0; t < n; t++)
            {
                long cx = Mathf.FloorToInt(f[t * 7] / Chunk) + 100000, cz = Mathf.FloorToInt(f[t * 7 + 2] / Chunk) + 100000;
                long key = (cx * 1000000 + cz) * 16 + (long)f[t * 7 + 6];
                if (!lists.TryGetValue(key, out var l)) lists[key] = l = new List<int>();
                l.Add(t);
            }
            var g = new List<Group>(lists.Count);
            foreach (var l in lists.Values)
            {
                int kind = (int)f[l[0] * 7 + 6];
                if (kind < 0 || kind >= local.Length || near[kind] == null) continue;
                var m = new Matrix4x4[l.Count];
                Vector3 lo = Vector3.one * float.MaxValue, hi = Vector3.one * float.MinValue;
                for (int k = 0; k < l.Count; k++)
                {
                    int o = l[k] * 7;
                    var p = new Vector3(f[o], f[o + 1], f[o + 2]);
                    m[k] = Matrix4x4.TRS(p, Quaternion.Euler(0, f[o + 3] * Mathf.Rad2Deg, 0), new Vector3(f[o + 4], f[o + 5], f[o + 4])) * local[kind];
                    lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p + Vector3.up * 40f * f[o + 5]);
                }
                var b = new Bounds(); b.SetMinMax(lo - new Vector3(15, 2, 15), hi + new Vector3(15, 0, 15));
                g.Add(new Group { kind = kind, bounds = b, m = m });
            }
            // the kilometre squares: every fine group belongs to one (by the corner of its square); a square's trees of a kind
            // that has a cone are kept again, in batches, for drawing far off
            var sq = new Dictionary<long, int>();
            var squareList = new List<Square>();
            var pending = new List<List<Matrix4x4>>();
            for (int k = 0; k < g.Count; k++)
            {
                var gr = g[k];
                long sx = Mathf.FloorToInt(gr.bounds.center.x / Coarse) + 100000, sz = Mathf.FloorToInt(gr.bounds.center.z / Coarse) + 100000;
                long key = (sx * 1000000 + sz) * 16 + gr.kind;
                if (!sq.TryGetValue(key, out int si))
                {
                    sq[key] = si = squareList.Count;
                    squareList.Add(new Square { kind = gr.kind, bounds = gr.bounds, m = new List<Matrix4x4[]>() });
                    pending.Add(new List<Matrix4x4>());
                }
                var s0 = squareList[si]; s0.bounds.Encapsulate(gr.bounds); squareList[si] = s0;
                pending[si].AddRange(gr.m);
                gr.parent = si; g[k] = gr;
            }
            for (int si = 0; si < squareList.Count; si++)
            {
                var all = pending[si];
                for (int a = 0; a < all.Count; a += Batch) squareList[si].m.Add(all.GetRange(a, Mathf.Min(Batch, all.Count - a)).ToArray());
            }
            groups = g.ToArray();
            squares = squareList.ToArray();
            squareDistance = new float[squares.Length];
        }

        void Draw(ScriptableRenderContext ctx, Camera cam)
        {
            if (groups == null || squares == null || cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            Vector3 eye = cam.transform.position;
            float max = Mathf.Min(MaxDistance, cam.farClipPlane);
            int layer = gameObject.layer;
            for (int i = 0; i < squares.Length; i++) squareDistance[i] = Mathf.Sqrt(squares[i].bounds.SqrDistance(eye));
            // near: the fine groups of the squares inside CoarseDistance
            foreach (var g in groups)
            {
                if (squareDistance[g.parent] >= CoarseDistance) continue;
                float d = Mathf.Sqrt(g.bounds.SqrDistance(eye));
                if (d > max || !GeometryUtility.TestPlanesAABB(planes, g.bounds)) continue;
                Mesh mesh; Material mat; bool shadow = true;
                if (d < NearDistance) { mesh = near[g.kind]; mat = materials[g.kind]; }
                else if (d < FarDistance || low == null || low[g.kind] == null)
                {
                    if (d >= FarDistance && d > LeafyDistance) continue;
                    mesh = far[g.kind] != null ? far[g.kind] : near[g.kind]; mat = materials[g.kind];
                    shadow = d < FarDistance;
                }
                else { mesh = low[g.kind]; mat = lowMaterials[g.kind]; shadow = false; }
                Submit(cam, layer, mat, mesh, g.bounds, shadow, g.m);
            }
            // far: whole squares, of cones
            for (int i = 0; i < squares.Length; i++)
            {
                var q = squares[i];
                float d = squareDistance[i];
                bool cone = low != null && low[q.kind] != null;
                if (d < CoarseDistance || d > (cone ? max : Mathf.Min(max, LeafyDistance))) continue;
                if (!GeometryUtility.TestPlanesAABB(planes, q.bounds)) continue;
                // the leafy kinds have no cone: their far mesh, out to LeafyDistance
                var mesh = cone ? low[q.kind] : far[q.kind] != null ? far[q.kind] : near[q.kind];
                foreach (var m in q.m) Submit(cam, layer, cone ? lowMaterials[q.kind] : materials[q.kind], mesh, q.bounds, false, m);
            }
        }

        static void Submit(Camera cam, int layer, Material mat, Mesh mesh, Bounds b, bool shadow, Matrix4x4[] m)
        {
            var rp = new RenderParams(mat)
            {
                camera = cam, layer = layer, worldBounds = b, receiveShadows = true,
                shadowCastingMode = shadow ? ShadowCastingMode.On : ShadowCastingMode.Off,
            };
            Graphics.RenderMeshInstanced(rp, mesh, 0, m);
        }
    }
}
