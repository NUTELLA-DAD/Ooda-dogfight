using UnityEngine;

namespace Ooda.Pilot
{
    /// <summary>
    /// The pilot's eyelids. His eyes are painted on, so the lids are their own small meshes folded away along the upper
    /// lid line, and two blend shapes unfold them: <c>BlinkHalf</c> (lids half down) and <c>Blink</c> (shut). Going
    /// through the half shape keeps the lid on the curve of the eye on its way down.
    /// He blinks every few seconds (now and then twice), and <see cref="PilotControls"/> has him shut his eyes on a hit,
    /// narrow them when he shakes a fist and squeeze them shut in a cheer (<see cref="Express"/>).
    /// Added by itself to any pilot whose mesh has the shapes (Tools~/ArtPipeline/pilot_face.py makes them).
    /// </summary>
    public class PilotFace : MonoBehaviour
    {
        [Tooltip("Seconds between blinks, picked at random in this range.")]
        public Vector2 blinkEvery = new Vector2(2.2f, 5.5f);
        [Range(0, 1)] public float doubleBlinkChance = 0.18f;
        public float closeTime = 0.07f, shutTime = 0.05f, openTime = 0.13f;

        SkinnedMeshRenderer smr;
        int half = -1, full = -1;
        float nextBlink, blinkT = -1f, expr, exprTarget, exprUntil, exprVel;
        bool again;

        public static bool HasLids(GameObject pilot)
        {
            foreach (var r in pilot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r.sharedMesh != null && r.sharedMesh.GetBlendShapeIndex("Blink") >= 0) return true;
            return false;
        }

        void Awake()
        {
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r.sharedMesh == null) continue;
                int f = r.sharedMesh.GetBlendShapeIndex("Blink");
                if (f < 0) continue;
                smr = r; full = f; half = r.sharedMesh.GetBlendShapeIndex("BlinkHalf");
                break;
            }
            nextBlink = Time.time + Random.Range(blinkEvery.x, blinkEvery.y) * Random.value;
        }

        /// <summary>Blink now.</summary>
        public void Blink() { if (blinkT < 0) blinkT = 0f; }

        /// <summary>Hold the lids this far shut (0 open, 1 shut) for this long, then let them open again.</summary>
        public void Express(float closed, float seconds)
        {
            exprTarget = Mathf.Clamp01(closed); exprUntil = Time.time + seconds;
        }

        void Update()
        {
            if (smr == null) return;
            float dt = Time.deltaTime;
            if (blinkT < 0 && Time.time > nextBlink) { blinkT = 0f; again = Random.value < doubleBlinkChance; }
            float blink = 0f;
            if (blinkT >= 0)
            {
                blinkT += dt;
                if (blinkT < closeTime) blink = blinkT / closeTime;
                else if (blinkT < closeTime + shutTime) blink = 1f;
                else if (blinkT < closeTime + shutTime + openTime) blink = 1f - (blinkT - closeTime - shutTime) / openTime;
                else
                {
                    blinkT = -1f;
                    nextBlink = Time.time + (again ? 0.12f : Random.Range(blinkEvery.x, blinkEvery.y));
                    again = false;
                }
            }
            expr = Mathf.SmoothDamp(expr, Time.time < exprUntil ? exprTarget : 0f, ref exprVel, 0.06f);
            Set(Mathf.Max(blink, expr));
        }

        void Set(float w)
        {
            w = Mathf.Clamp01(w);
            // open -> half -> shut: the half shape fades in over the first half and out over the second as the full shape comes in
            if (half >= 0)
            {
                smr.SetBlendShapeWeight(half, 100f * (w <= 0.5f ? 2f * w : 2f * (1f - w)));
                smr.SetBlendShapeWeight(full, 100f * Mathf.Max(0f, 2f * w - 1f));
            }
            else smr.SetBlendShapeWeight(full, 100f * w);
        }
    }
}
