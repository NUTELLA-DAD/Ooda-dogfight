using System;
using System.Collections.Generic;
using Ooda.Flight.Core;

namespace Ooda.Turn.Core
{
    /// <summary>
    /// The air hazards (balloon tethers, cable-car ropes, radio masts and their guys, the balloons) as plain shapes in feet,
    /// so the AI can steer round them when it plans off the main thread, where the physics colliders can't be asked.
    /// A path segment hits a wire if it passes within the wire's radius, and a balloon if it passes within its radius.
    /// </summary>
    public class WireSet
    {
        readonly List<Vec3d> a = new List<Vec3d>(), b = new List<Vec3d>();
        readonly List<double> r = new List<double>();
        readonly List<Vec3d> lo = new List<Vec3d>(), hi = new List<Vec3d>();   // each shape's bounding box, for a quick reject

        public int Count => a.Count;

        /// <summary>A wire (or a balloon, as a zero-length wire) from p to q with this radius, all in feet.</summary>
        public void Add(Vec3d p, Vec3d q, double radiusFt)
        {
            a.Add(p); b.Add(q); r.Add(radiusFt);
            lo.Add(new Vec3d(Math.Min(p.x, q.x) - radiusFt, Math.Min(p.y, q.y) - radiusFt, Math.Min(p.z, q.z) - radiusFt));
            hi.Add(new Vec3d(Math.Max(p.x, q.x) + radiusFt, Math.Max(p.y, q.y) + radiusFt, Math.Max(p.z, q.z) + radiusFt));
        }

        /// <summary>True if the segment p0 to p1 (feet) passes through any shape.</summary>
        public bool Hit(Vec3d p0, Vec3d p1)
        {
            double minX = Math.Min(p0.x, p1.x), maxX = Math.Max(p0.x, p1.x);
            double minY = Math.Min(p0.y, p1.y), maxY = Math.Max(p0.y, p1.y);
            double minZ = Math.Min(p0.z, p1.z), maxZ = Math.Max(p0.z, p1.z);
            for (int k = 0; k < a.Count; k++)
            {
                if (maxX < lo[k].x || minX > hi[k].x || maxY < lo[k].y || minY > hi[k].y || maxZ < lo[k].z || minZ > hi[k].z) continue;
                if (SegmentDistanceSq(p0, p1, a[k], b[k]) <= r[k] * r[k]) return true;
            }
            return false;
        }

        /// <summary>The squared closest distance between segments p0-p1 and q0-q1.</summary>
        static double SegmentDistanceSq(Vec3d p0, Vec3d p1, Vec3d q0, Vec3d q1)
        {
            Vec3d d1 = p1 - p0, d2 = q1 - q0, w = p0 - q0;
            double aa = Vec3d.Dot(d1, d1), ee = Vec3d.Dot(d2, d2), ff = Vec3d.Dot(d2, w);
            double s, t;
            if (aa <= 1e-9 && ee <= 1e-9) return Vec3d.Dot(w, w);
            if (aa <= 1e-9) { s = 0; t = Clamp01(ff / ee); }
            else
            {
                double cc = Vec3d.Dot(d1, w);
                if (ee <= 1e-9) { t = 0; s = Clamp01(-cc / aa); }
                else
                {
                    double bb = Vec3d.Dot(d1, d2), denom = aa * ee - bb * bb;
                    s = denom > 1e-9 ? Clamp01((bb * ff - cc * ee) / denom) : 0;
                    t = (bb * s + ff) / ee;
                    if (t < 0) { t = 0; s = Clamp01(-cc / aa); }
                    else if (t > 1) { t = 1; s = Clamp01((bb - cc) / aa); }
                }
            }
            Vec3d gap = (p0 + d1 * s) - (q0 + d2 * t);
            return Vec3d.Dot(gap, gap);
        }

        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
    }
}
