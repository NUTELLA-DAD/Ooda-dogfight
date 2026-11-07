using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ooda.EditorTools
{
    /// <summary>
    /// The koi's fishbowl helmet: a glass sphere round its head, full of water nearly to the top (the dorsal fin breaks
    /// the surface), three bubbles rising on its left, and a riveted brass collar at the neck. It is skinned to the Head
    /// bone, so it turns with the head look, shrinks away with the head in the cockpit camera, and goes with the
    /// skinned pilot when he bails out. Two renderers: the glass, water and bubbles (no shadow, so the face stays lit)
    /// and the brass collar. The numbers are in the koi's own height-1 frame (its FBX: head bone base 0.675 over the
    /// ground, hips 0.395); Tools~/ArtPipeline/koi_fishbowl_preview.py builds the same shapes in Blender for renders.
    /// </summary>
    internal static class KoiFishbowl
    {
        const string Gen = "Assets/OODA/Generated";
        const float HipsToHead = 0.675f - 0.395f;   // the FBX's hips-to-head, to turn these numbers into the model's units

        // the bowl: a sphere from its centre (above and a little behind the head bone), cut flat at the collar
        const float Up = 0.175f, Back = 0.048f;     // the centre, from the head bone's base
        const float R = 0.35f;                      // clears the cheek fins, the dorsal fin and the back of the head
        const float Cut = -0.22f;                   // the glass's lower edge, under the centre
        const float Water = 0.25f;                  // the water line, over the centre (the dorsal fin's tip pokes through)

        // the collar's cross-section (radius, height from the centre), outside then over the top
        static readonly Vector2[] Collar =
        {
            new Vector2(0.235f, -0.265f), new Vector2(0.262f, -0.265f), new Vector2(0.276f, -0.258f), new Vector2(0.284f, -0.235f),
            new Vector2(0.286f, -0.210f), new Vector2(0.278f, -0.200f), new Vector2(0.250f, -0.200f),
        };
        const float RivetR = 0.284f, RivetUp = -0.233f, RivetSize = 0.012f;
        // bubbles on the koi's left, in front: (left, up, forward) from the centre, and size
        static readonly Vector4[] Bubbles =
        {
            new Vector4(0.200f, 0.205f, 0.190f, 0.024f), new Vector4(0.222f, 0.145f, 0.170f, 0.016f), new Vector4(0.215f, 0.105f, 0.185f, 0.011f),
        };
        const int Seg = 48;

        /// <summary>Put the helmet on koi (the KoiPilot FBX, just instantiated, in its rest pose).</summary>
        internal static void Fit(GameObject koi)
        {
            var anim = koi.GetComponent<Animator>();
            Transform head = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
            Transform hips = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Hips) : null;
            if (head == null || hips == null) { Debug.LogWarning("OODA: the koi has no Head or Hips bone; no fishbowl."); return; }
            foreach (var old in new[] { "KoiFishbowl", "KoiFishbowlCollar" })
            {
                var t = koi.transform.Find(old);
                if (t != null) Object.DestroyImmediate(t.gameObject);
            }

            // the koi's frame, in its root's space: up, forward (from the heel to the toes), left, and its units
            var root = koi.transform;
            Vector3 L(Transform t) => root.InverseTransformPoint(t.position);
            Vector3 headAt = L(head);
            float unit = Mathf.Abs(headAt.y - L(hips).y) / HipsToHead;
            Vector3 up = Vector3.up, fwd = Vector3.forward;
            var foot = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
            var toes = anim.GetBoneTransform(HumanBodyBones.LeftToes);
            if (foot != null && toes != null)
            {
                Vector3 f = Vector3.ProjectOnPlane(L(toes) - L(foot), up);
                if (f.sqrMagnitude > 1e-8f) fwd = f.normalized;
            }
            Vector3 left = Vector3.Cross(fwd, up).normalized;   // Unity is left-handed: forward x up points left
            // the bubbles go on the koi's left arm's side (its left, unless the model is mirrored)
            var arm = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            float side = arm != null && Vector3.Dot(L(arm) - headAt, left) < 0 ? -1f : 1f;
            Vector3 centre = headAt + (up * Up - fwd * Back) * unit;
            Vector3 P(float l, float u, float f) => centre + (left * l + up * u + fwd * f) * unit;
            Vector3 Ring(float r, float u, float a) => P(r * Mathf.Cos(a), u, r * Mathf.Sin(a));

            // ---- the glass, the water and the bubbles ----
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var water = new List<int>(); var glass = new List<int>(); var surface = new List<int>(); var bubbles = new List<int>();
            void Band(float r, float u0, float u1, int rings, List<int> tri, bool sphere)
            {
                int start = v.Count;
                for (int i = 0; i <= rings; i++)
                {
                    float u = Mathf.Lerp(u0, u1, i / (float)rings);
                    float rr = sphere ? Mathf.Sqrt(Mathf.Max(0, r * r - u * u)) : r;
                    for (int k = 0; k < Seg; k++)
                    {
                        float a = 2 * Mathf.PI * k / Seg;
                        var p = Ring(rr, u, a);
                        v.Add(p);
                        var nn = sphere ? (p - centre).normalized : (p - P(0, u, 0)).normalized;
                        n.Add(nn);
                    }
                }
                Quads(start, rings, tri);
            }
            Band(R, Cut, Water, 40, water, true);
            Band(R, Water, R, 14, glass, true);
            // the water line: a bright ring on the glass and the surface across it
            Band(R + 0.002f, Water - 0.005f, Water + 0.005f, 1, surface, false);
            {
                int c0 = v.Count;
                v.Add(P(0, Water, 0)); n.Add(up);
                float rs = Mathf.Sqrt(R * R - Water * Water) - 0.002f;
                for (int k = 0; k < Seg; k++) { v.Add(Ring(rs, Water, 2 * Mathf.PI * k / Seg)); n.Add(up); }
                for (int k = 0; k < Seg; k++) { surface.Add(c0); surface.Add(c0 + 1 + (k + 1) % Seg); surface.Add(c0 + 1 + k); }
            }
            foreach (var b in Bubbles) Ball(v, n, bubbles, P(side * b.x, b.y, b.z), b.w * unit);
            var bowl = Skinned(koi, head, "KoiFishbowl", v, n, new[] { water, glass, surface, bubbles },
                new[] { Mat("Fishbowl_Water"), Mat("Fishbowl_Glass"), Mat("Fishbowl_Surface"), Mat("Fishbowl_Bubble") });
            bowl.shadowCastingMode = ShadowCastingMode.Off;
            bowl.receiveShadows = false;

            // ---- the brass collar and its rivets ----
            v = new List<Vector3>(); n = new List<Vector3>();
            var brass = new List<int>();
            {
                int start = v.Count;
                for (int i = 0; i < Collar.Length; i++)
                {
                    // the profile's normal: square to it, pointing out of the ring
                    Vector2 d = Collar[Mathf.Min(i + 1, Collar.Length - 1)] - Collar[Mathf.Max(i - 1, 0)];
                    Vector2 out2 = new Vector2(d.y, -d.x).normalized;
                    for (int k = 0; k < Seg; k++)
                    {
                        float a = 2 * Mathf.PI * k / Seg;
                        v.Add(Ring(Collar[i].x, Collar[i].y, a));
                        Vector3 radial = left * Mathf.Cos(a) + fwd * Mathf.Sin(a);
                        n.Add((radial * out2.x + up * out2.y).normalized);
                    }
                }
                Quads(start, Collar.Length - 1, brass);
            }
            for (int k = 0; k < 8; k++)
            {
                float a = 2 * Mathf.PI * (k + 0.5f) / 8;
                Ball(v, n, brass, Ring(RivetR, RivetUp, a), RivetSize * unit);
            }
            var collar = Skinned(koi, head, "KoiFishbowlCollar", v, n, new[] { brass }, new[] { Mat("Fishbowl_Brass") });
            collar.shadowCastingMode = ShadowCastingMode.On;
        }

        /// <summary>Quads between rings of Seg vertices (rings + 1 of them from start, each running from the left toward
        /// the front, the rings going up), clockwise seen from outside, so they face out.</summary>
        static void Quads(int start, int rings, List<int> tri)
        {
            for (int i = 0; i < rings; i++)
                for (int k = 0; k < Seg; k++)
                {
                    int a = start + i * Seg + k, b = start + i * Seg + (k + 1) % Seg;
                    int c = a + Seg, d = b + Seg;
                    tri.Add(a); tri.Add(b); tri.Add(c);
                    tri.Add(b); tri.Add(d); tri.Add(c);
                }
        }

        /// <summary>A small ball (a bubble, a rivet).</summary>
        static void Ball(List<Vector3> v, List<Vector3> n, List<int> tri, Vector3 at, float r)
        {
            const int U = 12, V = 8;
            int start = v.Count;
            for (int i = 0; i <= V; i++)
            {
                float th = Mathf.PI * i / V;
                for (int k = 0; k < U; k++)
                {
                    float ph = 2 * Mathf.PI * k / U;
                    var d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    v.Add(at + d * r); n.Add(d);
                }
            }
            for (int i = 0; i < V; i++)
                for (int k = 0; k < U; k++)
                {
                    int a = start + i * U + k, b = start + i * U + (k + 1) % U;
                    int c = a + U, d = b + U;
                    tri.Add(a); tri.Add(b); tri.Add(c);
                    tri.Add(b); tri.Add(d); tri.Add(c);
                }
        }

        /// <summary>A renderer skinned wholly to the head, its mesh saved as an asset so the prefabs keep it.</summary>
        static SkinnedMeshRenderer Skinned(GameObject koi, Transform head, string name, List<Vector3> v, List<Vector3> n, List<int>[] subs, Material[] mats)
        {
            var go = new GameObject(name);
            go.transform.SetParent(koi.transform, false);
            string path = $"{Gen}/{name}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, path); }
            mesh.Clear();
            mesh.name = name;
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.subMeshCount = subs.Length;
            for (int i = 0; i < subs.Length; i++) mesh.SetTriangles(subs[i], i);
            var w = new BoneWeight[v.Count];
            for (int i = 0; i < w.Length; i++) w[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1 };
            mesh.boneWeights = w;
            mesh.bindposes = new[] { head.worldToLocalMatrix * go.transform.localToWorldMatrix };
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);

            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = new[] { head };
            smr.rootBone = head;
            var b = mesh.bounds;   // in the koi's space: into the head's
            var c = head.InverseTransformPoint(go.transform.TransformPoint(b.center));
            var s = head.InverseTransformVector(go.transform.TransformVector(b.size));
            float m = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
            smr.localBounds = new Bounds(c, Vector3.one * m);
            smr.sharedMaterials = mats;
            return smr;
        }

        static Material Mat(string name)
        {
            switch (name)
            {
                case "Fishbowl_Brass": return TestWorldBuilder.Mat(name, new Color(0.72f, 0.52f, 0.22f), 0.6f, 1f);
                case "Fishbowl_Water": return Glass(name, new Color(0.40f, 0.78f, 0.84f, 0.38f), CullMode.Back, 3003);
                case "Fishbowl_Glass": return Glass(name, new Color(0.90f, 0.97f, 1f, 0.08f), CullMode.Back, 3003);
                case "Fishbowl_Surface": return Glass(name, new Color(0.82f, 0.96f, 1f, 0.55f), CullMode.Off, 3002);
                default: return Glass(name, new Color(0.95f, 1f, 1f, 0.5f), CullMode.Back, 3001);
            }
        }

        /// <summary>Clear and shiny, drawn after the head inside it: the bubbles first, then the water's surface, then the bowl.</summary>
        static Material Glass(string name, Color c, CullMode cull, int queue)
        {
            var m = TestWorldBuilder.Mat(name, c, 0.95f);
            TestWorldBuilder.MakeTransparent(m);
            m.SetFloat("_Cull", (float)cull);
            m.renderQueue = queue;
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
