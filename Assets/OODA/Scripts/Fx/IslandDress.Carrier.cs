using UnityEngine;
using UnityEngine.Rendering;

namespace Ooda.Fx
{
    /// <summary>
    /// The aircraft carrier: Kenny's Hi3D carrier (Resources/Carrier, cleaned and baked by Tools~/ArtPipeline/carrier.py),
    /// 240 m long, riding at anchor in the channel between Volcano Isle and the north-west coast, its bow up the Volcano
    /// Isle arena's line, so a fight there passes over its deck. Its near mesh and a lighter far one under an LODGroup, in
    /// the chalet's toon material (Resources/Carrier.mat: Ooda/ToonLit, inked). It is solid: its hull and its island are
    /// boxes on the Obstacles layer, so a plane that flies into it crashes and rounds stop on it, like a farmhouse.
    /// Missing from the build, the sea is simply empty there.
    /// </summary>
    public partial class IslandDress
    {
        /// <summary>Where it may lie (x, z, the bow's heading in degrees from north), first choice first: the first whose
        /// water is deep enough under the whole hull is used.</summary>
        static readonly Vector3[] CarrierSpots =
        {
            new Vector3(-9800, 5000, 20),     // the channel off the inlet, 4 km south-east of the volcano
            new Vector3(-10300, 3900, 20),
            new Vector3(-10900, 5600, 20),
            new Vector3(-11200, 2600, 10),    // off Harbour Town's coast, further south
        };
        /// <summary>The model's length and flight deck, in its own frame (metres, the waterline at 0, the bow toward +z).</summary>
        public const float CarrierLengthM = 240f, CarrierDeckM = 25.5f;
        /// <summary>The island on the starboard side, from the deck to the masthead (measured on the mesh by carrier.py's numbers).</summary>
        static readonly Bounds CarrierIsland = new Bounds(new Vector3(21.5f, 42.5f, 4f), new Vector3(27f, 34f, 58f));
        const float CarrierDeepM = -10f;      // the sea floor must be at least this deep under every point of the hull

        /// <summary>Where the carrier lies once built (its waterline, the middle of its length), or null.</summary>
        public static Vector3? CarrierAt { get; private set; }
        public static float CarrierHeadingDeg { get; private set; }

        void Carrier()
        {
            CarrierAt = null;
            var near = Resources.Load<GameObject>("Carrier");
            var far = Resources.Load<GameObject>("Carrier_Far");
            var mat = Resources.Load<Material>("Carrier");
            if (near == null || mat == null) { Debug.Log("OODA: no carrier model (Resources/Carrier): the sea off the north-west coast is empty."); return; }

            Vector3? spot = null;
            foreach (var s in CarrierSpots) if (DeepUnder(s.x, s.y, s.z)) { spot = s; break; }
            if (spot == null) { Debug.LogWarning("OODA: none of the carrier's spots has deep water under the whole hull; no carrier."); return; }
            var at = new Vector3(spot.Value.x, 0f, spot.Value.y);
            float heading = spot.Value.z;

            var go = new GameObject("Aircraft Carrier");
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(at, Quaternion.Euler(0, heading, 0));
            go.isStatic = true;
            var nearParts = Parts(near, go.transform, "Near", mat);
            var lods = new System.Collections.Generic.List<LOD> { new LOD(0.06f, nearParts) };
            if (far != null) lods.Add(new LOD(0.004f, Parts(far, go.transform, "Far", mat)));
            else lods[0] = new LOD(0.004f, nearParts);
            var lod = go.AddComponent<LODGroup>();
            lod.SetLODs(lods.ToArray());
            lod.RecalculateBounds();

            // solid: the hull up to the flight deck (from the near mesh's bounds), and the island over it
            int layer = LayerMask.NameToLayer("Obstacles");
            var b = PartsBounds(nearParts, go.transform);
            Box(go.transform, "Hull", new Vector3(b.center.x, (b.min.y + CarrierDeckM) / 2, b.center.z), new Vector3(b.size.x * 0.86f, CarrierDeckM - b.min.y, b.size.z * 0.97f), layer);
            Box(go.transform, "Island", CarrierIsland.center, CarrierIsland.size, layer);

            CarrierAt = at; CarrierHeadingDeg = heading;
            Debug.Log($"OODA: the aircraft carrier at ({at.x:F0}, {at.z:F0}), heading {heading:F0}, {b.size.z:F0} m long; {CarrierIsland.max.y:F0} m to the masthead.");
        }

        /// <summary>Every mesh of an imported OBJ (a big one may come in as several), under parent with mat.</summary>
        Renderer[] Parts(GameObject model, Transform parent, string name, Material mat)
        {
            var filters = model.GetComponentsInChildren<MeshFilter>(true);
            var list = new Renderer[filters.Length];
            for (int k = 0; k < filters.Length; k++)
            {
                var c = new GameObject(filters.Length == 1 ? name : $"{name} {k}");
                c.transform.SetParent(parent, false);
                c.transform.localPosition = model.transform.InverseTransformPoint(filters[k].transform.position);
                c.transform.localRotation = Quaternion.Inverse(model.transform.rotation) * filters[k].transform.rotation;
                c.transform.localScale = filters[k].transform.lossyScale;
                c.isStatic = true;
                c.AddComponent<MeshFilter>().sharedMesh = filters[k].sharedMesh;
                var r = c.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
                list[k] = r;
            }
            return list;
        }

        /// <summary>The parts' bounds in the carrier's own frame.</summary>
        static Bounds PartsBounds(Renderer[] parts, Transform root)
        {
            var b = new Bounds();
            bool any = false;
            foreach (var r in parts)
            {
                var mb = r.GetComponent<MeshFilter>().sharedMesh.bounds;
                var t = r.transform;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3((i & 1) == 0 ? mb.min.x : mb.max.x, (i & 2) == 0 ? mb.min.y : mb.max.y, (i & 4) == 0 ? mb.min.z : mb.max.z);
                    var p = root.InverseTransformPoint(t.TransformPoint(c));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        static void Box(Transform parent, string name, Vector3 centre, Vector3 size, int layer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.isStatic = true;
            if (layer >= 0) go.layer = layer;
            var box = go.AddComponent<BoxCollider>();
            box.center = centre;
            box.size = size;
        }

        /// <summary>Whether the sea is deep under the whole hull lying at (x, z) with its bow toward heading, and 60 m round it.</summary>
        static bool DeepUnder(float x, float z, float heading)
        {
            float h = heading * Mathf.Deg2Rad;
            Vector2 fwd = new Vector2(Mathf.Sin(h), Mathf.Cos(h)), side = new Vector2(fwd.y, -fwd.x);
            for (float along = -CarrierLengthM / 2 - 60; along <= CarrierLengthM / 2 + 60; along += 30)
                for (float across = -100; across <= 100; across += 50)
                {
                    var p = new Vector2(x, z) + fwd * along + side * across;
                    if (GroundY(p.x, p.y) > CarrierDeepM) return false;
                }
            return true;
        }
    }
}
