using System.Collections.Generic;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// The snow clouds' puffs (see TurnBattle.Snow): each cloud a cluster of soft, see-through white balls, brighter on top and
    /// a cold blue underneath, slowly billowing. Draw each cloud every frame it shows; a cluster not drawn in a frame hides.
    /// Pooled, so a cloud costs nothing to make once the pool has grown to the most shown at once.
    /// </summary>
    public class SnowPuffs : MonoBehaviour
    {
        const int Balls = 14;

        class Cluster { public GameObject root; public Transform[] balls; public MeshRenderer[] rends; public int frame = -1; }

        readonly List<Cluster> pool = new List<Cluster>();
        int frame = -1, used;
        static Material template;
        MaterialPropertyBlock block;

        public static SnowPuffs On(GameObject host)
        {
            var p = host.GetComponent<SnowPuffs>();
            return p != null ? p : host.AddComponent<SnowPuffs>();
        }

        /// <summary>A cloud at centre, radiusM across (metres), thick 0..1 (it thins as it goes), its puffs laid out by seed.</summary>
        public void Draw(Vector3 centre, float radiusM, float thick, int seed)
        {
            if (frame != Time.frameCount) { frame = Time.frameCount; used = 0; }
            if (used >= pool.Count) pool.Add(Make());
            var c = pool[used++];
            c.frame = frame;
            if (!c.root.activeSelf) c.root.SetActive(true);
            c.root.transform.position = centre;
            if (block == null) block = new MaterialPropertyBlock();
            float t = Time.time;
            for (int k = 0; k < Balls; k++)
            {
                uint h = Hash((uint)seed, (uint)k);
                float a = (h % 628) / 100f, u = ((h >> 9) % 1000) / 1000f, v = ((h >> 19) % 1000) / 1000f;
                float rr = radiusM * 0.55f * Mathf.Sqrt(u);
                // a flattened heap: wider than tall, the big ones in the middle
                var off = new Vector3(Mathf.Cos(a) * rr, radiusM * 0.32f * (v - 0.4f), Mathf.Sin(a) * rr);
                float size = radiusM * Mathf.Lerp(0.75f, 0.42f, u) * (1f + 0.05f * Mathf.Sin(t * 0.7f + k * 1.3f));   // billowing
                c.balls[k].localPosition = off;
                c.balls[k].localScale = Vector3.one * size;
                float top = Mathf.Clamp01(0.5f + off.y / (radiusM * 0.5f));
                var col = Color.Lerp(new Color(0.70f, 0.82f, 0.98f), new Color(1f, 1f, 1f), top);
                col.a = Mathf.Lerp(0.22f, 0.36f, top) * Mathf.Clamp01(thick);
                block.SetColor("_BaseColor", col);
                block.SetColor("_Color", col);
                c.rends[k].SetPropertyBlock(block);
            }
        }

        void LateUpdate()
        {
            foreach (var c in pool)
                if (c.frame != Time.frameCount && c.root.activeSelf) c.root.SetActive(false);
        }

        Cluster Make()
        {
            if (template == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) template = new Material(Shader.Find("Sprites/Default"));
                else
                {
                    template = new Material(sh);
                    template.SetFloat("_Surface", 1);   // transparent, alpha blended
                    template.SetFloat("_Blend", 0);
                    template.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    template.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    template.SetFloat("_ZWrite", 0);
                    template.SetOverrideTag("RenderType", "Transparent");
                    template.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    template.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                }
                template.name = "Snow cloud";
                template.SetColor("_BaseColor", Color.white);
                template.color = Color.white;
            }
            var c = new Cluster { root = new GameObject("Snow cloud"), balls = new Transform[Balls], rends = new MeshRenderer[Balls] };
            c.root.transform.SetParent(transform, false);
            for (int k = 0; k < Balls; k++)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                var col = b.GetComponent<Collider>();
                if (col != null) Destroy(col);
                b.name = "Puff";
                b.transform.SetParent(c.root.transform, false);
                var r = b.GetComponent<MeshRenderer>();
                r.sharedMaterial = template;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                c.balls[k] = b.transform;
                c.rends[k] = r;
            }
            return c;
        }

        static uint Hash(uint a, uint b)
        {
            uint h = a * 2246822519u + b * 3266489917u + 374761393u;
            h = (h ^ (h >> 15)) * 668265263u;
            return h ^ (h >> 13);
        }
    }
}
