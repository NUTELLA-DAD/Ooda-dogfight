using System.Collections.Generic;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// Ice (a loadout pick, see Core/Loadout): the plane cased in ice. A pale frost laid over every part of the airframe
    /// (a see-through copy of each of its meshes, following the part as it moves), and icicles hanging all over its
    /// underside: under the wings, the belly and the tail, one from every few of the lowest points of the airframe, each
    /// its own length. It shows and hides with the airframe (stealth, a crash). The icicles need the airframe's mesh
    /// readable (the editor always can; a build needs Read/Write on the model); without it there is only the frost.
    /// The same coat shows the rime from a snow cloud (see Core/Specials), as thick as it lies: the frost fades as it melts,
    /// and the icicles go first (below IciclesFrom).
    /// </summary>
    public class IceCoat : MonoBehaviour
    {
        public const string RootName = "IceCoat";

        GameObject root, icicles;
        Renderer anchor;
        Material frost;
        float amount = 1f;
        /// <summary>Rime thinner than this has no icicles, only frost.</summary>
        public const float IciclesFrom = 0.3f;
        readonly List<Renderer> mine = new List<Renderer>();
        bool on, shown = true;
        static Material frostTemplate, icicleTemplate;

        /// <summary>
        /// Ice on a plane (or off), thick 0 to 1 (1 the Ice pick's full coat; rime as it lies). Cheap to call every frame: it
        /// builds once and only switches after.
        /// </summary>
        public static void Set(GameObject plane, bool on, float thick = 1f)
        {
            if (plane == null) return;
            var c = plane.GetComponent<IceCoat>();
            if (c == null) { if (!on) return; c = plane.AddComponent<IceCoat>(); }
            c.enabled = true;
            c.Show(on);
            if (on) c.Thick(Mathf.Clamp01(thick));
        }

        void Thick(float t)
        {
            if (Mathf.Abs(t - amount) < 0.01f) return;
            amount = t;
            if (frost != null)
            {
                var c = frostTemplate.color;
                c.a *= Mathf.Lerp(0.3f, 1f, t);
                frost.SetColor("_BaseColor", c);
                frost.color = c;
            }
            if (icicles != null) icicles.SetActive(t >= IciclesFrom);
        }

        void Show(bool v)
        {
            if (v == on) return;
            on = v;
            if (v && root == null) Build();
            if (root != null) root.SetActive(v);
        }

        void LateUpdate()
        {
            if (!on || anchor == null) return;
            bool vis = anchor.enabled && anchor.gameObject.activeInHierarchy;
            if (vis == shown) return;
            shown = vis;
            foreach (var r in mine) if (r != null) r.enabled = vis;
        }

        void Build()
        {
            root = new GameObject(RootName);
            root.transform.SetParent(transform, false);
            var parts = new List<MeshFilter>(MissileRack.Airframe(transform));
            var a = MissileRack.Anchor(transform);
            anchor = a != null ? a.GetComponent<Renderer>() : null;
            Frost(parts);
            Icicles(parts);
            float t = amount;
            amount = -1;
            Thick(t);
        }

        /// <summary>The frost: a see-through pale blue copy of each airframe mesh, a hair bigger, following its part.</summary>
        void Frost(List<MeshFilter> parts)
        {
            if (frostTemplate == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) frostTemplate = new Material(Shader.Find("Sprites/Default"));
                else
                {
                    frostTemplate = new Material(sh);
                    frostTemplate.SetFloat("_Surface", 1);   // transparent, alpha blended: a frosting over the paint
                    frostTemplate.SetFloat("_Blend", 0);
                    frostTemplate.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    frostTemplate.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    frostTemplate.SetFloat("_ZWrite", 0);
                    frostTemplate.SetOverrideTag("RenderType", "Transparent");
                    frostTemplate.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    frostTemplate.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                }
                var c = new Color(0.82f, 0.94f, 1f, 0.42f);
                frostTemplate.SetColor("_BaseColor", c);
                frostTemplate.color = c;
            }
            frost = new Material(frostTemplate);   // this plane's own: a rime coat thins as it melts
            foreach (var mf in parts)
            {
                var r = mf.GetComponent<MeshRenderer>();
                if (r == null || !r.enabled || mf.sharedMesh == null) continue;
                var g = new GameObject(mf.name + " frost");
                g.transform.SetParent(mf.transform, false);
                g.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var mr = g.AddComponent<MeshRenderer>();
                var mats = new Material[Mathf.Max(1, mf.sharedMesh.subMeshCount)];
                for (int k = 0; k < mats.Length; k++) mats[k] = frost;
                mr.sharedMaterials = mats;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                g.transform.SetParent(root.transform, true);
                g.AddComponent<FollowPart>().part = mf.transform;   // keeps it on a moving part (an aileron, the rudder), 1% bigger
                mine.Add(mr);
            }
        }

        /// <summary>
        /// The icicles: the airframe's points in its own frame (x across, y up, z along), binned into columns across the
        /// plan view; the lowest point of a column is on the underside, and about every third column hangs an icicle there,
        /// longer near the trailing edge of each. All of them one mesh, one draw.
        /// </summary>
        void Icicles(List<MeshFilter> parts)
        {
            var pts = new List<Vector3>();
            foreach (var mf in parts)
            {
                var mesh = mf.sharedMesh;
                if (mesh == null || (!mesh.isReadable && !Application.isEditor)) continue;
                Vector3[] v;
                try { v = mesh.vertices; } catch { continue; }
                if (v == null) continue;
                var m = transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                int step = Mathf.Max(1, v.Length / 40000);
                for (int i = 0; i < v.Length; i += step) pts.Add(m.MultiplyPoint3x4(v[i]));
            }
            if (pts.Count < 200) return;
            float half = 0, ymin = float.MaxValue, ymax = float.MinValue;
            foreach (var p in pts) { half = Mathf.Max(half, Mathf.Abs(p.x)); ymin = Mathf.Min(ymin, p.y); ymax = Mathf.Max(ymax, p.y); }
            float cell = Mathf.Max(0.25f, half / 15f);
            var low = new Dictionary<long, Vector3>();
            foreach (var p in pts)
            {
                long key = ((long)Mathf.FloorToInt(p.x / cell) << 32) ^ (uint)Mathf.FloorToInt(p.z / cell);
                if (!low.TryGetValue(key, out var q) || p.y < q.y) low[key] = p;
            }
            // the bottom tenth of the height is the wheels and the belly fairings: no icicles from them
            float floor = ymin + 0.08f * (ymax - ymin);
            float scale = Mathf.Clamp(half / 5.5f, 0.6f, 2.5f);
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tris = new List<int>();
            foreach (var kv in low)
            {
                uint h = Hash((uint)(kv.Key >> 32), (uint)kv.Key);
                if (h % 100 >= 38) continue;
                var p = kv.Value;
                if (p.y < floor) continue;
                float len = (0.22f + 0.6f * ((h >> 8) % 1000) / 1000f) * scale;
                float rad = (0.045f + 0.035f * ((h >> 18) % 100) / 100f) * scale;
                Cone(p + new Vector3(0, 0.03f * scale, 0), rad, len, (h >> 4) % 360, verts, norms, tris);
            }
            if (verts.Count == 0) return;
            var mesh2 = new Mesh { name = "Icicles", indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh2.SetVertices(verts);
            mesh2.SetNormals(norms);
            mesh2.SetTriangles(tris, 0);
            mesh2.RecalculateBounds();
            if (icicleTemplate == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                icicleTemplate = new Material(sh);
                var c = new Color(0.80f, 0.93f, 1f);
                icicleTemplate.SetColor("_BaseColor", c);
                icicleTemplate.color = c;
                icicleTemplate.SetFloat("_Smoothness", 0.95f);
                icicleTemplate.SetFloat("_Glossiness", 0.95f);
                icicleTemplate.SetFloat("_Metallic", 0.1f);
            }
            var g = new GameObject("Icicles");
            icicles = g;
            g.transform.SetParent(root.transform, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh2;
            var mr = g.AddComponent<MeshRenderer>();
            mr.sharedMaterial = icicleTemplate;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mine.Add(mr);
        }

        /// <summary>A six-sided icicle hanging from top (its base) down len, faceted (each face its own normal), turned by twistDeg.</summary>
        static void Cone(Vector3 top, float rad, float len, float twistDeg, List<Vector3> v, List<Vector3> n, List<int> t)
        {
            const int Sides = 6;
            Vector3 tip = top + Vector3.down * len;
            float tw = twistDeg * Mathf.Deg2Rad;
            for (int s = 0; s < Sides; s++)
            {
                float a0 = tw + s * Mathf.PI * 2 / Sides, a1 = tw + (s + 1) * Mathf.PI * 2 / Sides;
                Vector3 p0 = top + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * rad, p1 = top + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * rad;
                // Unity's front face winds clockwise: the triangle (p0, tip, p1) faces Cross(tip - p0, p1 - p0); out, or swap
                Vector3 nrm = Vector3.Cross(tip - p0, p1 - p0).normalized;
                if (Vector3.Dot(nrm, (p0 + p1) * 0.5f - top) < 0) { nrm = -nrm; (p0, p1) = (p1, p0); }
                int b = v.Count;
                v.Add(p0); v.Add(tip); v.Add(p1);
                n.Add(nrm); n.Add(nrm); n.Add(nrm);
                t.Add(b); t.Add(b + 1); t.Add(b + 2);
            }
        }

        static uint Hash(uint a, uint b)
        {
            uint h = a * 374761393u + b * 668265263u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }

        void OnDestroy()
        {
            if (root != null) Destroy(root);
            if (frost != null) Destroy(frost);
        }
    }
}
