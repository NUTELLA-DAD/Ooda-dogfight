using System.Collections.Generic;
using UnityEngine;

namespace Ooda.Flight
{
    /// <summary>
    /// Gives each aircraft of one type in the air its own paint job. The first one keeps the standard paint; each one
    /// after it takes the next of <see cref="others"/> (and round again when there are more aircraft than paint jobs).
    /// The paint jobs are whole materials (Tools~/ArtPipeline/p51_paint.py makes the P-51's), swapped in wherever the
    /// model uses the standard one.
    /// </summary>
    public class PlaneLivery : MonoBehaviour
    {
        [Tooltip("The paint the model comes with.")]
        public Material standard;
        [Tooltip("The other paint jobs, for the second aircraft, the third, and so on.")]
        public Material[] others;

        static readonly List<PlaneLivery> live = new List<PlaneLivery>();
        int slot = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Clear() => live.Clear();

        /// <summary>Which paint job this aircraft took: 0 the standard one, 1 the first of the others, and so on.</summary>
        public int Slot => slot;

        // In Start, not Awake: a planning ghost is a copy of the aircraft stripped of its scripts before Start runs,
        // so it never takes a paint job from a real one.
        void Start()
        {
            live.RemoveAll(l => l == null);
            slot = 0;
            while (live.Exists(l => l.standard == standard && l.slot == slot)) slot++;
            live.Add(this);
            if (standard == null || others == null || others.Length == 0) return;
            int pick = slot % (others.Length + 1);
            if (pick == 0 || others[pick - 1] == null) return;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var ms = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < ms.Length; i++)
                    if (ms[i] == standard) { ms[i] = others[pick - 1]; changed = true; }
                if (changed) r.sharedMaterials = ms;
            }
        }

        void OnDestroy() => live.Remove(this);
    }
}
