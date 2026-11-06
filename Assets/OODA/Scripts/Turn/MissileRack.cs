using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>
    /// The heat-seekers an aircraft still carries, hanging on short pylons under its wings: Kenny's Hi3D missile
    /// (Resources/Missile, rebuilt with even fins by Tools~/ArtPipeline/missile.py). Each turn view keeps one (made the
    /// first time it is shown) and TurnBattle tells it how many it was loaded with and how many are left: they go in
    /// firing order, the first pair first (L1, R1, then L2, R2, then any C1 under the fuselage).
    ///
    /// Where the hardpoints are, best first, so a re-exported airframe still gets them:
    /// 1. empties in the model named MissileMount_L1, MissileMount_R1, MissileMount_L2, ... (on the underside where the
    ///    pylon attaches, +z toward the nose);
    /// 2. the table OODA > Fit Missile Racks writes (Resources/MissileMounts, fitted in the editor from each airframe's
    ///    wing; GameBuild refits it before every build), looked up by the airframe mesh's name and vertex count;
    /// 3. the same fit made here, when the mesh can be read (always in the editor);
    /// 4. a guess from the airframe's bounds.
    /// </summary>
    public class MissileRack : MonoBehaviour
    {
        /// <summary>The missile's length, m (a stubby cartoon heat-seeker; its body is about a tenth as wide).</summary>
        public const float Length = 2.2f;
        /// <summary>The missile's body radius and its tail fins' reach from the axis, as shares of its length (from the OBJ's header).</summary>
        public const float BodyR = 0.0489f, FinR = 0.2244f;
        /// <summary>How far under the wing the missile's axis hangs, m: its upper fins (at 45 degrees) just clear the skin.</summary>
        public static float Drop => FinR * Length * 0.7071f + 0.06f;
        /// <summary>The name of the child that holds the pylons and missiles (the ghosts and the red-hot shell leave it out).</summary>
        public const string RootName = "MissileRack";

        /// <summary>Where the hardpoints came from, for the log: "empties", "table", "fit", "bounds".</summary>
        public string Source { get; private set; }

        readonly List<GameObject> stores = new List<GameObject>();
        Transform rackRoot;
        bool built;
        int shownLoaded = -1, shownLeft = -1;
        bool shownHidden;

        /// <summary>Show the missiles still on the rails: <paramref name="loaded"/> at the start of the fight, <paramref name="left"/> now.</summary>
        public void Show(int loaded, int left, bool hidden)
        {
            if (loaded == shownLoaded && left == shownLeft && hidden == shownHidden && built) return;
            if (!built) Build();
            shownLoaded = loaded; shownLeft = left; shownHidden = hidden;
            loaded = Mathf.Min(loaded, stores.Count);
            int gone = Mathf.Max(0, loaded - Mathf.Max(0, left));
            for (int k = 0; k < stores.Count; k++)
                if (stores[k] != null) stores[k].SetActive(!hidden && k < loaded && k >= gone);
        }

        // ---------- building the rack ----------

        static Mesh missileMesh;
        static Material missileMat, pylonMat;
        static bool missingLogged;

        void Build()
        {
            built = true;
            var mounts = Mounts(transform, out var src);
            Source = src;
            var old = transform.Find(RootName);
            if (old != null) Gone(old.gameObject);
            rackRoot = new GameObject(RootName).transform;
            rackRoot.SetParent(transform, false);
            if (!LoadArt()) return;
            for (int k = 0; k < mounts.Count; k++) stores.Add(MakeStore(mounts[k], k));
            Debug.Log($"OODA: missile rack on {name}: {mounts.Count} hardpoints ({src})");
        }

        static bool LoadArt()
        {
            if (missileMesh != null) return true;
            var model = Resources.Load<GameObject>("Missile");
            var mf = model != null ? model.GetComponentInChildren<MeshFilter>(true) : null;
            if (mf == null || mf.sharedMesh == null)
            {
                if (!missingLogged) Debug.LogWarning("OODA: no Resources/Missile model: no missiles under the wings");
                missingLogged = true;
                return false;
            }
            missileMesh = mf.sharedMesh;
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) lit = Shader.Find("Standard");
            var tex = Resources.Load<Texture2D>("Missile_BaseColor");
            missileMat = new Material(lit) { name = "Missile" };
            if (tex != null) { missileMat.SetTexture("_BaseMap", tex); missileMat.mainTexture = tex; }
            missileMat.SetFloat("_Metallic", 0.35f);
            missileMat.SetFloat("_Smoothness", 0.55f);
            pylonMat = new Material(lit) { name = "Pylon" };
            var grey = new Color(0.36f, 0.37f, 0.36f);
            pylonMat.SetColor("_BaseColor", grey); pylonMat.color = grey;
            pylonMat.SetFloat("_Metallic", 0.4f);
            pylonMat.SetFloat("_Smoothness", 0.4f);
            return true;
        }

        GameObject MakeStore(Pose mount, int k)
        {
            var store = new GameObject($"Store {k + 1}");
            store.transform.SetParent(rackRoot, false);
            store.transform.localPosition = mount.position;
            store.transform.localRotation = mount.rotation;
            float drop = Drop, r = BodyR * Length;
            // the pylon: a slim blade from the skin down onto the missile's back, a little shorter than the missile
            var pylon = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pylon.name = "Pylon";
            Gone(pylon.GetComponent<Collider>());
            pylon.transform.SetParent(store.transform, false);
            float h = drop - r * 0.6f + 0.08f;   // into the wing a little, into the missile a little
            pylon.transform.localPosition = new Vector3(0, 0.08f - h / 2, 0.06f * Length);
            pylon.transform.localScale = new Vector3(0.07f, h, 0.5f * Length);
            pylon.GetComponent<Renderer>().sharedMaterial = pylonMat;
            var m = new GameObject("Missile");
            m.transform.SetParent(store.transform, false);
            m.transform.localPosition = new Vector3(0, -drop, 0);
            m.transform.localScale = Vector3.one * Length;
            m.AddComponent<MeshFilter>().sharedMesh = missileMesh;
            m.AddComponent<MeshRenderer>().sharedMaterial = missileMat;
            store.SetActive(false);
            return store;
        }

        static void Gone(UnityEngine.Object o) { if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }   // (the editor's shots build racks outside Play)

        // ---------- finding the hardpoints ----------

        /// <summary>The hardpoints in the aircraft's own frame (x across, y up, z to the nose), in firing order.</summary>
        public static List<Pose> Mounts(Transform root, out string source)
        {
            var list = new List<Pose>();
            // 1. empties in the model
            var named = new List<(int order, Transform t)>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("MissileMount_", StringComparison.Ordinal) || t.name.Length < 15) continue;
                char side = t.name[13];
                if (!int.TryParse(t.name.Substring(14), out int pair)) continue;
                int order = side == 'C' ? 1000 + pair : pair * 2 + (side == 'R' ? 1 : 0);
                named.Add((order, t));
            }
            if (named.Count > 0)
            {
                named.Sort((a, b) => a.order.CompareTo(b.order));
                foreach (var (_, t) in named)
                    list.Add(new Pose(root.InverseTransformPoint(t.position), Quaternion.Inverse(root.rotation) * t.rotation));
                source = "empties";
                return list;
            }
            var anchor = Anchor(root);
            // 2. the fitted table
            if (anchor != null && Table().TryGetValue(Key(anchor.sharedMesh), out var pts))
            {
                foreach (var p in pts) list.Add(new Pose(root.InverseTransformPoint(anchor.transform.TransformPoint(p)), Quaternion.identity));
                source = "table";
                return list;
            }
            // 3. fit it now, if the meshes can be read
            var fit = FitPoints(root, out _);
            if (fit != null)
            {
                foreach (var p in fit) list.Add(new Pose(p, Quaternion.identity));
                source = "fit";
                return list;
            }
            // 4. a guess from the bounds
            var view = root.GetComponent<TurnAircraftView>();
            var b = view != null ? view.LocalBounds() : new Bounds(Vector3.zero, new Vector3(11.3f, 3.5f, 9.8f));
            float half = Mathf.Max(Mathf.Abs(b.min.x), Mathf.Abs(b.max.x));
            float y = b.center.y - 0.18f * b.size.y, z = b.center.z + 0.04f * b.size.z;
            foreach (float f in new[] { 0.42f, 0.58f })
            {
                list.Add(new Pose(new Vector3(-f * half, y, z), Quaternion.identity));
                list.Add(new Pose(new Vector3(f * half, y, z), Quaternion.identity));
            }
            source = "bounds";
            return list;
        }

        /// <summary>The airframe: the biggest plain mesh of the model (not the propeller, the canopy, the cockpit or the pilot).</summary>
        public static MeshFilter Anchor(Transform root)
        {
            MeshFilter best = null; float size = 0;
            foreach (var mf in Airframe(root))
            {
                float s = mf.sharedMesh.bounds.size.magnitude * mf.transform.lossyScale.magnitude;
                if (s > size) { size = s; best = mf; }
            }
            return best;
        }

        // parts that are not the airframe (case-sensitive: "Pit" only as the cockpit tub's prefix, never inside "Spitfire")
        static readonly string[] Skip = { "Prop", "Canopy", "Glass", "Cockpit", "Pilot", "Spinner", "Blade", " hot", "RedHot", "Ghost", "Chute", "Parachute", "Flame", "Burner", "Pedal", "Stick", "Throttle", "Wolf", "Tiger", "Bear", "Fox", "Koi", "Lion", "Wizard", "Skeleton" };

        static IEnumerable<MeshFilter> Airframe(Transform root)
        {
            var rack = root.Find(RootName);
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || (rack != null && mf.transform.IsChildOf(rack))) continue;
                bool skip = false;
                for (var t = mf.transform; t != null && t != root && !skip; t = t.parent)
                {
                    if (t.name.StartsWith("Pit", StringComparison.Ordinal)) skip = true;
                    foreach (var s in Skip) if (t.name.IndexOf(s, StringComparison.Ordinal) >= 0) { skip = true; break; }
                }
                if (!skip) yield return mf;
            }
        }

        /// <summary>
        /// Fit the hardpoints from the airframe's vertices, in the root's frame: L1, R1 at the most inboard station whose cut
        /// through the wing is thin for its chord (clear of the fuselage, the engine booms and the tailplane), L2, R2 further out.
        /// Null when no mesh can be read (a game build reads only what the table holds) or nothing like a wing is found.
        /// </summary>
        public static List<Vector3> FitPoints(Transform root, out MeshFilter anchor)
        {
            anchor = Anchor(root);
            var pts = new List<Vector3>();
            foreach (var mf in Airframe(root))
            {
                var mesh = mf.sharedMesh;
                if (!mesh.isReadable && !Application.isEditor) return null;
                Vector3[] v;
                try { v = mesh.vertices; } catch { return null; }
                if (v == null || v.Length == 0) continue;
                var m = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                int step = Mathf.Max(1, v.Length / 150000);
                for (int i = 0; i < v.Length; i += step) pts.Add(m.MultiplyPoint3x4(v[i]));
            }
            if (pts.Count < 100) return null;
            float half = 0;
            foreach (var p in pts) half = Mathf.Max(half, Mathf.Abs(p.x));
            if (half < 2f) return null;
            // the station: the most inboard one whose wing cut is about as thin (for its chord) as the thinnest
            float bestRatio = float.MaxValue;
            var cuts = new List<(float f, float ratio)>();
            for (float f = 0.32f; f <= 0.66f; f += 0.02f)
            {
                if (!Cut(pts, f * half, half, out _, out float ratio, out _)) continue;
                cuts.Add((f, ratio));
                bestRatio = Mathf.Min(bestRatio, ratio);
            }
            if (cuts.Count == 0) return null;
            float f1 = cuts[0].f;
            foreach (var c in cuts) if (c.ratio <= bestRatio * 1.35f) { f1 = c.f; break; }
            float f2 = Mathf.Min(f1 + 0.15f, 0.74f);
            var res = new List<Vector3>();
            foreach (float f in new[] { f1, f2 })
                foreach (int side in new[] { -1, 1 })
                {
                    if (!Cut(pts, side * f * half, half, out var at, out _, out _)) at = new Vector3(side * f * half, res.Count > 0 ? res[0].y : 0, res.Count > 0 ? res[0].z : 0);
                    res.Add(at);
                }
            return res;
        }

        /// <summary>
        /// The wing where it crosses x: the longest run of points along z in a thin slice there (the wing, not the tailplane
        /// behind it); its thickness over its chord, and the hardpoint under its middle. False if there is no wing-like run.
        /// </summary>
        static bool Cut(List<Vector3> pts, float x, float half, out Vector3 at, out float ratio, out float chord)
        {
            at = default; ratio = float.MaxValue; chord = 0;
            float band = Mathf.Max(0.08f, 0.012f * half);
            var zs = new List<Vector3>();
            foreach (var p in pts) if (Mathf.Abs(p.x - x) < band) zs.Add(p);
            if (zs.Count < 12) return false;
            zs.Sort((a, b) => a.z.CompareTo(b.z));
            // split into runs where the slice has a gap along z
            int bestA = 0, bestB = 0; float bestLen = -1;
            int a0 = 0;
            for (int i = 1; i <= zs.Count; i++)
            {
                if (i < zs.Count && zs[i].z - zs[i - 1].z < 0.25f) continue;
                float len = zs[i - 1].z - zs[a0].z;
                if (len > bestLen) { bestLen = len; bestA = a0; bestB = i; }
                a0 = i;
            }
            chord = bestLen;
            if (chord < 0.4f || chord > 0.5f * half) return false;   // a boom or a nacelle runs much longer than a wing chord
            float ymin = float.MaxValue, ymax = float.MinValue;
            for (int i = bestA; i < bestB; i++) { ymin = Mathf.Min(ymin, zs[i].y); ymax = Mathf.Max(ymax, zs[i].y); }
            ratio = (ymax - ymin) / chord;
            float zc = 0.5f * (zs[bestA].z + zs[bestB - 1].z) + 0.04f * chord;
            float under = float.MaxValue;
            for (int i = bestA; i < bestB; i++) if (Mathf.Abs(zs[i].z - zc) < 0.2f * chord) under = Mathf.Min(under, zs[i].y);
            if (under == float.MaxValue) under = ymin;
            at = new Vector3(x, under, zc);
            return true;
        }

        // ---------- the fitted table (Resources/MissileMounts) ----------

        static Dictionary<string, List<Vector3>> table;

        /// <summary>Forget the loaded table (the editor has just rewritten it).</summary>
        public static void ReloadTable() => table = null;

        /// <summary>The table's key for an airframe mesh: its name and vertex count (a re-exported mesh no longer matches).</summary>
        public static string Key(Mesh m) => m == null ? "" : $"{m.name}#{m.vertexCount}";

        static Dictionary<string, List<Vector3>> Table()
        {
            if (table != null) return table;
            table = new Dictionary<string, List<Vector3>>();
            var ta = Resources.Load<TextAsset>("MissileMounts");
            if (ta == null) return table;
            foreach (var raw in ta.text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var parts = line.Split('|');
                if (parts.Length < 2) continue;
                var pts = new List<Vector3>();
                foreach (var p in parts[1].Split(';'))
                {
                    var c = p.Split(',');
                    if (c.Length != 3) continue;
                    pts.Add(new Vector3(F(c[0]), F(c[1]), F(c[2])));
                }
                if (pts.Count > 0) table[parts[0].Trim()] = pts;
            }
            return table;
        }

        static float F(string s) => float.Parse(s.Trim(), CultureInfo.InvariantCulture);

        /// <summary>One table line: the airframe's key and its hardpoints in that mesh's own frame.</summary>
        public static string TableLine(MeshFilter anchor, Transform root, List<Vector3> rootPts)
        {
            var sb = new System.Text.StringBuilder(Key(anchor.sharedMesh)).Append('|');
            for (int k = 0; k < rootPts.Count; k++)
            {
                var p = anchor.transform.InverseTransformPoint(root.TransformPoint(rootPts[k]));
                if (k > 0) sb.Append(';');
                sb.Append(p.x.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
                  .Append(p.y.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
                  .Append(p.z.ToString("F4", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }
}
