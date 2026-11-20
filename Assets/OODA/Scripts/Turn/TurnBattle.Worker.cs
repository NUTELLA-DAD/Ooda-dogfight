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

        /// <summary>The obstacle colliders' world boxes now, in feet (min, max), padded a little. Main thread only.</summary>
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
            var boxes = new List<double>(obstacleColliders.Length * 6);
            const double pad = 3.0;   // feet
            foreach (var c in obstacleColliders)
            {
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy || c.isTrigger) continue;
                var b = c.bounds;
                Vec3d lo = UnityFrame.Pos(b.min), hi = UnityFrame.Pos(b.max);   // (min and max survive the axis swap: it only reorders)
                boxes.Add(Math.Min(lo.x, hi.x) - pad); boxes.Add(Math.Min(lo.y, hi.y) - pad); boxes.Add(Math.Min(lo.z, hi.z) - pad);
                boxes.Add(Math.Max(lo.x, hi.x) + pad); boxes.Add(Math.Max(lo.y, hi.y) + pad); boxes.Add(Math.Max(lo.z, hi.z) + pad);
            }
            return boxes.ToArray();
        }

        /// <summary>The worker's obstacle test over a box snapshot: false when the line misses every box, else give up.</summary>
        static Func<Vec3d, Vec3d, bool> WorkerObstacleTest(double[] boxes)
        {
            if (boxes.Length == 0) return null;
            return (a, b) =>
            {
                for (int k = 0; k + 5 < boxes.Length; k += 6)
                    if (SegmentTouchesBox(a, b, boxes, k)) throw new NeedsMainThread();
                return false;
            };
        }

        /// <summary>The slab test: does the segment a-b pass through the box (min at k, max at k + 3)?</summary>
        static bool SegmentTouchesBox(Vec3d a, Vec3d b, double[] box, int k)
        {
            double t0 = 0, t1 = 1;
            return Slab(a.x, b.x - a.x, box[k], box[k + 3], ref t0, ref t1)
                && Slab(a.y, b.y - a.y, box[k + 1], box[k + 4], ref t0, ref t1)
                && Slab(a.z, b.z - a.z, box[k + 2], box[k + 5], ref t0, ref t1);
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
