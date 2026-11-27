using System;
using System.Collections.Generic;
using Ooda.Flight;
using Ooda.Flight.Core;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// Flying the planning preview and the confidence check on a worker thread. The battle tests flight paths and rounds
    /// against the obstacle colliders with Physics.Linecast, which only the main thread may call. So the worker gets a
    /// snapshot of those colliders' boxes (taken on the main thread): a line that misses every box can't hit a collider,
    /// so the answer is the same; a line that touches a box stops the worker run, and that preview is flown on the main
    /// thread instead, exactly as before. Over open country the worker never has to give up.
    /// </summary>
    public partial class TurnBattle
    {
        /// <summary>Thrown on the worker when a line comes near an obstacle: fly this one on the main thread.</summary>
        sealed class NeedsMainThread : Exception { }

        Collider[] obstacleColliders;
        float obstacleCollidersAt = -999f;
        int previewSyncLeft;   // after a worker run gave up near an obstacle: the next few previews go straight to the main thread

        /// <summary>The obstacle colliders' boxes now, in feet, padded a little: Stride numbers each (the middle, then three
        /// unit axes, then the half size along each). A box collider gets its own turned box, so a long wire strung
        /// across a valley is a thin box along it, not the huge world-aligned box round it (that one sent nearly every
        /// preview near a wire back to the main thread); anything else gets its world-aligned bounds. Main thread only.</summary>
        double[] ObstacleBoxes()
        {
            if (obstacleMask == -2) obstacleMask = LayerMask.GetMask("Obstacles");
            if (obstacleMask == 0) return Array.Empty<double>();
            // the list of colliders now and then (things can be built or switched on); their boxes every time (things move)
            if (obstacleColliders == null || Time.unscaledTime - obstacleCollidersAt > 5f)
            {
                var all = FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                var mine = new List<Collider>();
                foreach (var c in all) if (c != null && ((1 << c.gameObject.layer) & obstacleMask) != 0) mine.Add(c);
                obstacleColliders = mine.ToArray();
                obstacleCollidersAt = Time.unscaledTime;
            }
            var boxes = new List<double>(obstacleColliders.Length * Stride);
            const double pad = 3.0;   // feet
            foreach (var c in obstacleColliders)
            {
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy || c.isTrigger) continue;
                if (c is BoxCollider bc && Square(bc.transform))
                {
                    var t = bc.transform;
                    Vec3d mid = UnityFrame.Pos(t.TransformPoint(bc.center));
                    boxes.Add(mid.x); boxes.Add(mid.y); boxes.Add(mid.z);
                    var half = new double[3];
                    for (int a = 0; a < 3; a++)
                    {
                        var local = Vector3.zero; local[a] = bc.size[a] * 0.5f;
                        Vec3d v = UnityFrame.Dir(t.TransformVector(local)) * Atmosphere.FtPerM;
                        double len = v.Length;
                        Vec3d u = len > 1e-9 ? v / len : (a == 0 ? new Vec3d(1, 0, 0) : a == 1 ? new Vec3d(0, 0, 1) : new Vec3d(0, 1, 0));
                        boxes.Add(u.x); boxes.Add(u.y); boxes.Add(u.z);
                        half[a] = len + pad;
                    }
                    boxes.Add(half[0]); boxes.Add(half[1]); boxes.Add(half[2]);
                    continue;
                }
                var b = c.bounds;
                Vec3d lo = UnityFrame.Pos(b.min), hi = UnityFrame.Pos(b.max);   // (min and max survive the axis swap: it only reorders)
                boxes.Add((lo.x + hi.x) / 2); boxes.Add((lo.y + hi.y) / 2); boxes.Add((lo.z + hi.z) / 2);
                boxes.Add(1); boxes.Add(0); boxes.Add(0);
                boxes.Add(0); boxes.Add(1); boxes.Add(0);
                boxes.Add(0); boxes.Add(0); boxes.Add(1);
                boxes.Add(Math.Abs(hi.x - lo.x) / 2 + pad); boxes.Add(Math.Abs(hi.y - lo.y) / 2 + pad); boxes.Add(Math.Abs(hi.z - lo.z) / 2 + pad);
            }
            return boxes.ToArray();
        }

        /// <summary>Whether a transform keeps right angles (no skew from a turned parent's uneven scale), so its box stays a box.</summary>
        static bool Square(Transform t)
        {
            Vector3 x = t.TransformVector(Vector3.right).normalized, y = t.TransformVector(Vector3.up).normalized, z = t.TransformVector(Vector3.forward).normalized;
            return Mathf.Abs(Vector3.Dot(x, y)) < 1e-3f && Mathf.Abs(Vector3.Dot(y, z)) < 1e-3f && Mathf.Abs(Vector3.Dot(x, z)) < 1e-3f;
        }

        /// <summary>The numbers for each box in ObstacleBoxes: middle (3), axes (9), half sizes (3).</summary>
        const int Stride = 15;

        /// <summary>The worker's obstacle test over a box snapshot: false when the line misses every box, else give up.</summary>
        static Func<Vec3d, Vec3d, bool> WorkerObstacleTest(double[] boxes)
        {
            if (boxes.Length == 0) return null;
            return (a, b) =>
            {
                for (int k = 0; k + Stride - 1 < boxes.Length; k += Stride)
                    if (SegmentTouchesBox(a, b, boxes, k)) throw new NeedsMainThread();
                return false;
            };
        }

        /// <summary>The slab test in the box's own frame: does the segment a-b pass through the box at k?</summary>
        static bool SegmentTouchesBox(Vec3d a, Vec3d b, double[] box, int k)
        {
            double t0 = 0, t1 = 1;
            double rx = a.x - box[k], ry = a.y - box[k + 1], rz = a.z - box[k + 2];
            double dx = b.x - a.x, dy = b.y - a.y, dz = b.z - a.z;
            for (int ax = 0; ax < 3; ax++)
            {
                int u = k + 3 + ax * 3;
                double p = rx * box[u] + ry * box[u + 1] + rz * box[u + 2];
                double d = dx * box[u] + dy * box[u + 1] + dz * box[u + 2];
                double h = box[k + 12 + ax];
                if (!Slab(p, d, -h, h, ref t0, ref t1)) return false;
            }
            return true;
        }

        static bool Slab(double p, double d, double lo, double hi, ref double t0, ref double t1)
        {
            if (Math.Abs(d) < 1e-12) return p >= lo && p <= hi;
            double u = (lo - p) / d, v = (hi - p) / d;
            if (u > v) { var s = u; u = v; v = s; }
            if (u > t0) t0 = u;
            if (v < t1) t1 = v;
            return t0 <= t1;
        }

        /// <summary>Whether a worker run failed only because it came near an obstacle (and should be flown here instead).</summary>
        static bool GaveUpNearObstacle(AggregateException e)
        {
            if (e == null) return false;
            foreach (var x in e.Flatten().InnerExceptions) if (!(x is NeedsMainThread)) return false;
            return true;
        }
    }
}
