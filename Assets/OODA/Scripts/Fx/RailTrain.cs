using UnityEngine;

namespace Ooda.Fx
{
    /// <summary>
    /// The steam train on the island's railway: scenery only. It runs the line from the station by the town to the station at
    /// the head of the main valley and back, waiting a while at each end, its cars following the rails up the grades and
    /// round the curves, in and out of the tunnels, puffing steam while it is near the camera. It has no collider and is
    /// not on the Obstacles layer: the battle never sees it, so a game over the network stays in step.
    /// </summary>
    public class RailTrain : MonoBehaviour
    {
        [Tooltip("The rail top along the line, metres, every few metres (AlpineWorld lays it down).")]
        public Vector3[] path;
        [Tooltip("The cars, front first: the engine, its tender, the coaches. Each one's origin is its middle at rail height, nose +z.")]
        public Transform[] cars;
        [Tooltip("Each car's length, m (the gaps between them are added).")]
        public float[] lengths;
        [Tooltip("Where on the engine the chimney is, in its own frame.")]
        public Vector3 chimney = new Vector3(0, 4.2f, 3.6f);
        public float speedMs = 15f, waitSec = 25f, gapM = 1.2f;
        [Tooltip("Where along the line it starts, 0 to 1 (so it is out on the line when a fight begins).")]
        [Range(0, 1)] public float startAt = 0.42f;

        float[] along;     // the distance to each path point
        float at;          // how far along the front of the train is, m
        int dir = 1;
        float wait;
        float nextPuff;
        int puffs;

        void Start() => Setup();

        /// <summary>Put the train at its starting place on the line (the world builder calls it too, so it stands there in the scene).</summary>
        public void Setup()
        {
            if (path == null || path.Length < 2) { enabled = false; return; }
            along = new float[path.Length];
            for (int k = 1; k < path.Length; k++) along[k] = along[k - 1] + Vector3.Distance(path[k - 1], path[k]);
            at = Mathf.Lerp(TrainLength, along[along.Length - 1], startAt);
            Place();
        }

        float TrainLength
        {
            get { float l = 0; if (lengths != null) foreach (var x in lengths) l += x + gapM; return l; }
        }

        void Update()
        {
            if (along == null) return;
            if (wait > 0) wait -= Time.deltaTime;
            else
            {
                at += dir * speedMs * Time.deltaTime;
                float end = along[along.Length - 1];
                // the front of the train stops at the buffers: going back, the "front" is the last car's tail
                if (dir > 0 && at >= end) { at = end; dir = -1; wait = waitSec; }
                else if (dir < 0 && at <= TrainLength) { at = TrainLength; dir = 1; wait = waitSec; }
                Puff();
            }
            Place();
        }

        /// <summary>Each car on the rails: its two ends on the line, its middle between them, turned along it.</summary>
        void Place()
        {
            float d = at;
            for (int i = 0; i < cars.Length; i++)
            {
                if (cars[i] == null) continue;
                float len = lengths != null && i < lengths.Length ? lengths[i] : 15f;
                Vector3 front = PointAt(d), back = PointAt(d - len);
                cars[i].SetPositionAndRotation((front + back) / 2, Quaternion.LookRotation(front - back));
                d -= len + gapM;
            }
        }

        Vector3 PointAt(float d)
        {
            d = Mathf.Clamp(d, 0, along[along.Length - 1]);
            int lo = 0, hi = along.Length - 1;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (along[mid] <= d) lo = mid; else hi = mid; }
            float seg = along[hi] - along[lo];
            return Vector3.Lerp(path[lo], path[hi], seg > 1e-4f ? (d - along[lo]) / seg : 0);
        }

        /// <summary>A puff of steam from the chimney now and then, while the camera is within a few km (the puffs are shared with the battle's smoke).</summary>
        void Puff()
        {
            if (Time.time < nextPuff || cars.Length == 0 || cars[0] == null) return;
            nextPuff = Time.time + 0.35f;
            var cam = Camera.main;
            if (cam == null || Vector3.Distance(cam.transform.position, cars[0].position) > 5000f) return;
            Vector3 p = cars[0].TransformPoint(chimney);
            Vector3 v = Vector3.up * 3f - cars[0].forward * dir * speedMs * 0.3f;
            ToonFx.Get().TrailPuff(p, v, 3.2f, false, 7700, 7700 + puffs++);
        }
    }
}
