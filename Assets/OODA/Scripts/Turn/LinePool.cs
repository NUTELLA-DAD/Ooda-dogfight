using System.Collections.Generic;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// Plain coloured lines (the reach shell, the arrow, the ghost path, the projection, tracers), drawn
    /// fresh each frame from a pool: call Begin, then Line or Strip (width is a multiple of sizeAt) as many times as needed, then End.
    /// </summary>
    public class LinePool
    {
        readonly Transform parent;
        readonly Material mat;
        readonly List<LineRenderer> lines = new List<LineRenderer>();
        int used;

        /// <summary>
        /// How wide a line of width 1 is at a point (for example a share of the distance to the camera), so a
        /// long line stays thin up close and visible far away: each end gets its own width.
        /// </summary>
        public System.Func<Vector3, float> sizeAt = p => 1f;

        public LinePool(Transform parent, string name)
        {
            this.parent = new GameObject(name).transform;
            this.parent.SetParent(parent, false);
            mat = new Material(Shader.Find("Sprites/Default"));
        }

        public void Begin() { used = 0; usedFills = 0; }

        public void End()
        {
            for (int i = used; i < lines.Count; i++) if (lines[i].enabled) lines[i].enabled = false;
            for (int i = usedFills; i < fills.Count; i++) if (fills[i].enabled) fills[i].enabled = false;
        }

        /// <summary>A strip with a fixed width in metres (not scaled by sizeAt), for bands that must fill a gap.</summary>
        public void StripMetres(IList<Vector3> pts, float widthM, Color color, bool loop = false)
        {
            if (pts.Count < 2) return;
            var l = Next(widthM, color);
            l.loop = loop;
            l.positionCount = pts.Count;
            for (int i = 0; i < pts.Count; i++) l.SetPosition(i, pts[i]);
            l.startWidth = l.endWidth = widthM;
        }

        // ---------- shaded areas: meshes with a colour at each corner, drawn both sides, see-through ----------

        readonly List<MeshRenderer> fills = new List<MeshRenderer>();
        readonly List<Mesh> fillMeshes = new List<Mesh>();
        int usedFills;
        readonly List<Vector3> fv = new List<Vector3>();
        readonly List<Color> fc = new List<Color>();
        readonly List<int> ft = new List<int>();

        Mesh NextFill()
        {
            if (usedFills == fills.Count)
            {
                var go = new GameObject("Fill");
                go.transform.SetParent(parent, false);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;   // Sprites/Default: vertex colours, alpha blended, no culling
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                var mesh = new Mesh { name = "Fill" };
                mesh.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                fills.Add(mr);
                fillMeshes.Add(mesh);
            }
            fills[usedFills].enabled = true;
            // world-space vertices: keep the object itself at the origin
            fills[usedFills].transform.position = Vector3.zero;
            fills[usedFills].transform.rotation = Quaternion.identity;
            fills[usedFills].transform.localScale = Vector3.one;
            return fillMeshes[usedFills++];
        }

        /// <summary>
        /// A grid of points (rows by cols, row-major, world space) with a colour each, filled as quads between
        /// neighbours. wrap joins the last column back to the first (a ring).
        /// </summary>
        public void Grid(IList<Vector3> pts, IList<Color> colors, int rows, int cols, bool wrap = false)
        {
            if (rows < 2 || cols < 2 || pts.Count < rows * cols || colors.Count < rows * cols) return;
            var mesh = NextFill();
            fv.Clear(); fc.Clear(); ft.Clear();
            for (int i = 0; i < rows * cols; i++) { fv.Add(pts[i]); fc.Add(colors[i]); }
            int last = wrap ? cols : cols - 1;
            for (int r = 0; r < rows - 1; r++)
                for (int k = 0; k < last; k++)
                {
                    int k1 = (k + 1) % cols;
                    int a = r * cols + k, b = r * cols + k1, c = (r + 1) * cols + k1, d = (r + 1) * cols + k;
                    ft.Add(a); ft.Add(b); ft.Add(c);
                    ft.Add(a); ft.Add(c); ft.Add(d);
                }
            mesh.Clear();
            mesh.SetVertices(fv);
            mesh.SetColors(fc);
            mesh.SetTriangles(ft, 0);
            mesh.RecalculateBounds();
        }

        /// <summary>
        /// A flat band along a path: at each point it reaches halfWidth (times sizeAt, but at least minHalf metres)
        /// either side along side[i], shaded from startColor at the first point to endColor at the last.
        /// </summary>
        public void Ribbon(IList<Vector3> pts, IList<Vector3> side, float halfWidth, float minHalf, Color startColor, Color endColor)
        {
            int n = Mathf.Min(pts.Count, side.Count);
            if (n < 2) return;
            var gp = new List<Vector3>(2 * n);
            var gc = new List<Color>(2 * n);
            for (int r = 0; r < 2; r++)
                for (int i = 0; i < n; i++)
                {
                    float hw = Mathf.Max(minHalf, halfWidth * sizeAt(pts[i]));
                    gp.Add(pts[i] + side[i].normalized * (r == 0 ? -hw : hw));
                    gc.Add(Color.Lerp(startColor, endColor, i / (float)(n - 1)));
                }
            Grid(gp, gc, 2, n);
        }

        LineRenderer Next(float width, Color color)
        {
            if (used == lines.Count)
            {
                var go = new GameObject("Line");
                go.transform.SetParent(parent, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = mat;
                lr.useWorldSpace = true;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.numCapVertices = 2;
                lines.Add(lr);
            }
            var l = lines[used++];
            l.enabled = true;
            l.startWidth = l.endWidth = width;
            l.startColor = l.endColor = color;
            l.loop = false;
            return l;
        }

        public void Line(Vector3 a, Vector3 b, float width, Color color)
        {
            var l = Next(width, color);
            l.positionCount = 2;
            l.SetPosition(0, a);
            l.SetPosition(1, b);
            l.startWidth = width * sizeAt(a);
            l.endWidth = width * sizeAt(b);
        }

        public void Strip(IList<Vector3> pts, float width, Color color, bool loop = false, Color? endColor = null)
        {
            if (pts.Count < 2) return;
            var l = Next(width, color);
            if (endColor.HasValue) l.endColor = endColor.Value;
            l.loop = loop;
            l.positionCount = pts.Count;
            for (int i = 0; i < pts.Count; i++) l.SetPosition(i, pts[i]);
            l.startWidth = width * sizeAt(pts[0]);
            l.endWidth = width * sizeAt(pts[pts.Count - 1]);
        }
    }
}
