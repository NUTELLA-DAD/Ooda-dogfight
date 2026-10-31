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

        public void Begin() => used = 0;

        public void End()
        {
            for (int i = used; i < lines.Count; i++) if (lines[i].enabled) lines[i].enabled = false;
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
