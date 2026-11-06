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
    /// per-frame cost is a few thousand box tests, whatever the number of trees.
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
        const float Chunk = 250f;

        struct Group { public int kind; public Bounds bounds; public Matrix4x4[] m; }
        Group[] groups;
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
            groups = null;
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
            groups = g.ToArray();
        }

        void Draw(ScriptableRenderContext ctx, Camera cam)
        {
            if (groups == null || cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            Vector3 eye = cam.transform.position;
            float max = Mathf.Min(MaxDistance, cam.farClipPlane);
            int layer = gameObject.layer;
            foreach (var g in groups)
            {
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
                var rp = new RenderParams(mat)
                {
                    camera = cam, layer = layer, worldBounds = g.bounds, receiveShadows = true,
                    shadowCastingMode = shadow ? ShadowCastingMode.On : ShadowCastingMode.Off,
                };
                Graphics.RenderMeshInstanced(rp, mesh, 0, g.m);
            }
        }
    }
}
