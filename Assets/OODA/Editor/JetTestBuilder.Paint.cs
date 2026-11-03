using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Ooda.EditorTools
{
    /// <summary>
    /// The Phantom's paint and canopy. The Hi3D texture is a grey-and-cream show scheme; this repaints it in the
    /// Vietnam-era USAF South East Asia camouflage (T.O. 1-1-4: FS 30219 tan, 34102 medium green and 34079 dark green
    /// on top, FS 36622 light grey underneath). The pattern is worked out from the 3D shape, not the UV layout, so its
    /// blobs run on across the texture's seams: each texel is traced back to the point of the plane it paints. The old
    /// texture's shading (panel lines, dirt, highlights) is kept by scaling the camo with its brightness; the dark
    /// parts (intakes, nozzles, radome) and the canopy glass keep their own colours. The canopy (the blue texels) is
    /// split into its own submesh so it can be clear glass with the crew seen through it.
    /// </summary>
    public static partial class JetTestBuilder
    {
        const string CamoTex = Dir + "/Phantom_Camo_BaseColor.jpg";
        const string CamoMetalTex = Dir + "/Phantom_Camo_MetallicSmoothness.png";
        const string SplitMeshPath = "Assets/OODA/Generated/Phantom_Split.asset";

        // The SEA colours, about as the chips (the sun here is bright, so no lighter).
        static readonly Color Tan = new Color(0.56f, 0.45f, 0.31f), Green = new Color(0.31f, 0.37f, 0.21f),
                              DarkGreen = new Color(0.17f, 0.22f, 0.14f), Belly = new Color(0.80f, 0.80f, 0.76f);
        const float TanShare = 0.36f, GreenShare = 0.34f;   // and the rest dark green, about as on the real jets

        [MenuItem("OODA/Repaint Phantom")]
        public static void Repaint()
        {
            Imports();
            Paint(true);
            AssetDatabase.SaveAssets();
        }

        static bool IsGlass(Color c) => c.b > c.r + 0.2f && c.b > c.g + 0.1f;

        static Texture2D ReadRaw(string path)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            t.LoadImage(File.ReadAllBytes(path));
            return t;
        }

        /// <summary>Bake the camo maps when they are missing (or asked), split off the canopy, and set up both materials.</summary>
        static (Mesh mesh, Material body, Material glass) Paint(bool force = false)
        {
            var src = AssetDatabase.LoadAllAssetsAtPath(Obj).OfType<Mesh>().FirstOrDefault();
            if (src == null) { Debug.LogWarning("OODA: no mesh in " + Obj + "."); return (null, null, null); }
            var (turn, k, centre) = FitOf(src);
            var toRoot = Matrix4x4.TRS(-(turn * centre) * k, turn, Vector3.one * k);
            var baseRaw = ReadRaw(BaseTex);

            if (force || !File.Exists(CamoTex) || !File.Exists(CamoMetalTex))
            {
                Bake(src, toRoot, baseRaw, GlassTriangles(src, toRoot, baseRaw));
                AssetDatabase.ImportAsset(CamoTex);
                AssetDatabase.ImportAsset(CamoMetalTex);
            }
            foreach (var (path, srgb) in new[] { (CamoTex, true), (CamoMetalTex, false) })
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                ti.sRGBTexture = srgb;
                ti.mipmapEnabled = true;
                ti.anisoLevel = 8;
                ti.maxTextureSize = 4096;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.SaveAndReimport();
            }

            var mesh = Split(src, toRoot, baseRaw);
            Object.DestroyImmediate(baseRaw);
            var body = TestWorldBuilder.Textured("Phantom", CamoTex, CamoMetalTex);
            // clear, lightly tinted and shiny, so the crew and their cockpits show through
            var glass = TestWorldBuilder.Mat("Phantom_CanopyGlass", new Color(0.78f, 0.88f, 0.95f, 0.12f), 0.55f);
            TestWorldBuilder.MakeTransparent(glass);
            glass.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);   // seen from outside only, so it is clear from the cockpit
            return (mesh, body, glass);
        }

        /// <summary>The model as two submeshes: the body, and the canopy glass (the triangles painted blue, above the wing).</summary>
        static Mesh Split(Mesh src, Matrix4x4 toRoot, Texture2D baseRaw)
        {
            var v = src.vertices; var uv = src.uv; var tri = src.triangles;
            var isGlass = GlassTriangles(src, toRoot, baseRaw);
            var body = new List<int>(tri.Length); var glass = new List<int>();
            for (int t = 0; t < tri.Length; t += 3)
                (isGlass[t / 3] ? glass : body).AddRange(new[] { tri[t], tri[t + 1], tri[t + 2] });
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(SplitMeshPath);
            if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, SplitMeshPath); }
            mesh.Clear();
            mesh.name = "Phantom";
            mesh.indexFormat = src.indexFormat;
            mesh.vertices = v;
            mesh.normals = src.normals;
            mesh.tangents = src.tangents;
            mesh.uv = uv;
            mesh.subMeshCount = 2;
            mesh.SetTriangles(body, 0);
            mesh.SetTriangles(glass, 1);
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            Debug.Log($"OODA: the Phantom's canopy is {glass.Count / 3} of its {tri.Length / 3} triangles.");
            return mesh;
        }

        /// <summary>Which triangles are canopy glass: most of each painted blue (its corners, edges' middles and centre), above the wing.</summary>
        static bool[] GlassTriangles(Mesh src, Matrix4x4 toRoot, Texture2D baseRaw)
        {
            var v = src.vertices; var uv = src.uv; var tri = src.triangles;
            var glass = new bool[tri.Length / 3];
            for (int t = 0; t < tri.Length; t += 3)
            {
                Vector2 a = uv[tri[t]], b = uv[tri[t + 1]], c = uv[tri[t + 2]];
                float y = toRoot.MultiplyPoint3x4((v[tri[t]] + v[tri[t + 1]] + v[tri[t + 2]]) / 3f).y;
                int blue = new[] { a, b, c, (a + b) / 2, (b + c) / 2, (c + a) / 2, (a + b + c) / 3 }.Count(p => IsGlass(baseRaw.GetPixelBilinear(p.x, p.y)));
                glass[t / 3] = blue >= 3 && y > -1f;
            }
            return glass;
        }

        static readonly Color Frame = new Color(0.2f, 0.22f, 0.23f);

        static void Bake(Mesh mesh, Matrix4x4 toRoot, Texture2D baseRaw, bool[] glassTri)
        {
            var msRaw = ReadRaw(MetalTex);
            int W = baseRaw.width, H = baseRaw.height;
            var col = baseRaw.GetPixels32();
            var ms = msRaw.GetPixels32();
            int MW = msRaw.width, MH = msRaw.height;

            // trace every texel back to the point of the plane it paints
            var pos = new Vector3[W * H];
            var nrm = new Vector3[W * H];
            var has = new bool[W * H];
            var onGlass = new bool[W * H];
            var v = mesh.vertices; var n = mesh.normals; var uv = mesh.uv; var tri = mesh.triangles;
            var P = v.Select(x => toRoot.MultiplyPoint3x4(x)).ToArray();
            var N = n.Select(x => toRoot.MultiplyVector(x).normalized).ToArray();
            for (int t = 0; t < tri.Length; t += 3)
            {
                int i0 = tri[t], i1 = tri[t + 1], i2 = tri[t + 2];
                Vector2 a = new Vector2(uv[i0].x * W, uv[i0].y * H), b = new Vector2(uv[i1].x * W, uv[i1].y * H), c = new Vector2(uv[i2].x * W, uv[i2].y * H);
                float area = (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y);
                if (Mathf.Abs(area) < 1e-9f) continue;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)))), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)))), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = x + 0.5f, py = y + 0.5f;
                        float w1 = ((px - a.x) * (c.y - a.y) - (c.x - a.x) * (py - a.y)) / area;
                        float w2 = ((b.x - a.x) * (py - a.y) - (px - a.x) * (b.y - a.y)) / area;
                        float w0 = 1 - w1 - w2;
                        if (w0 < -1e-4f || w1 < -1e-4f || w2 < -1e-4f) continue;
                        int i = y * W + x;
                        // where the UV layout puts two bits of the plane on one texel, the upper one gets it: a grey belly patch up on a side shows more than camo underneath
                        Vector3 nn = (N[i0] * w0 + N[i1] * w1 + N[i2] * w2).normalized;
                        if (has[i] && nn.y < nrm[i].y) continue;
                        has[i] = true;
                        onGlass[i] = glassTri[t / 3];
                        pos[i] = P[i0] * w0 + P[i1] * w1 + P[i2] * w2;
                        nrm[i] = nn;
                    }
            }

            // which texels are paint: bright, not glass, not metal; and the pattern value at each
            var paint = new float[W * H];
            var f = new float[W * H];
            var fs = new List<float>();
            for (int i = 0; i < W * H; i++)
            {
                if (!has[i]) continue;
                Color c = col[i];
                float lum = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
                if (IsGlass(c))
                {
                    if (onGlass[i]) continue;   // the canopy, drawn as glass
                    paint[i] = 1;               // blue on the body: the canopy frame's edges, and panels under the nose
                }
                else
                {
                    // the metal is the dark parts (the nozzles), left as they are by brightness; the show scheme also
                    // marks light patches of the skin as metal, which shone white through the camo, so those are painted
                    paint[i] = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.36f, 0.55f, lum));
                }
                if (paint[i] <= 0) continue;
                f[i] = Pattern(pos[i]);
                if ((i & 7) == 0) fs.Add(f[i]);
            }
            fs.Sort();
            float tTan = fs.Count > 0 ? fs[(int)(fs.Count * TanShare)] : 0.45f;
            float tGreen = fs.Count > 0 ? fs[(int)(fs.Count * (TanShare + GreenShare))] : 0.55f;

            var outCol = new Color32[W * H];
            var outMs = new Color32[W * H];
            for (int i = 0; i < W * H; i++)
            {
                Color c = col[i];
                int mi = (i / W) * MH / H * MW + (i % W) * MW / W;
                Color32 m = ms[mi];
                if (paint[i] <= 0) { outCol[i] = col[i]; outMs[i] = m; continue; }
                float lum = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
                Color camo = IsGlass(c) ? (pos[i].y > -1f ? Frame : Belly) * Mathf.Clamp(lum / 0.4f, 0.6f, 1.05f)
                                         : Camo(pos[i], nrm[i], f[i], tTan, tGreen) * Mathf.Clamp(lum / 0.8f, 0.45f, 1.1f);
                camo.a = 1;
                outCol[i] = Color.Lerp(c, camo, paint[i]);
                // flat paint, not the show scheme's gloss
                outMs[i] = new Color32((byte)(m.r * (1 - paint[i])), m.g, m.b, (byte)Mathf.Lerp(m.a, m.a * 0.45f + 20, paint[i]));
            }
            Dilate(outCol, has, W, H, 8);
            Dilate(outMs, has, W, H, 8);

            var o = new Texture2D(W, H, TextureFormat.RGBA32, false);
            o.SetPixels32(outCol); o.Apply();
            File.WriteAllBytes(CamoTex, o.EncodeToJPG(92));
            var om = new Texture2D(W, H, TextureFormat.RGBA32, false, true);
            om.SetPixels32(outMs); om.Apply();
            File.WriteAllBytes(CamoMetalTex, om.EncodeToPNG());
            Object.DestroyImmediate(o); Object.DestroyImmediate(om); Object.DestroyImmediate(msRaw);
            Debug.Log($"OODA: painted the Phantom in SEA camo ({W}x{H}, tan below {tTan:F3}, green below {tGreen:F3}).");
        }

        /// <summary>The camo field at a point of the plane (metres, nose +z): big soft-edged blobs, warped so they meander.</summary>
        static float Pattern(Vector3 p)
        {
            var warp = new Vector3(Noise(p * 0.12f + new Vector3(11, 3, 7)), Noise(p * 0.12f + new Vector3(5, 17, 2)), Noise(p * 0.12f + new Vector3(23, 9, 13)));
            var q = p * 0.24f + warp * 1.8f;
            return Noise(q) * 0.75f + Noise(q * 2.6f + new Vector3(31, 0, 0)) * 0.25f;
        }

        static Color Camo(Vector3 p, Vector3 n, float f, float tTan, float tGreen)
        {
            // the light grey wraps up from the belly to a wavy line low on the sides
            float line = -0.28f + 0.12f * (Noise(p * 0.7f + new Vector3(3, 3, 3)) - 0.5f);
            float under = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(line + 0.04f, line - 0.04f, n.y));
            const float soft = 0.012f;   // sprayed, so a little soft
            Color top = Color.Lerp(Tan, Green, Mathf.InverseLerp(tTan - soft, tTan + soft, f));
            top = Color.Lerp(top, DarkGreen, Mathf.InverseLerp(tGreen - soft, tGreen + soft, f));
            return Color.Lerp(top, Belly, under);
        }

        /// <summary>Grow the painted texels out over the empty gutters between UV islands, so mipmaps and filtering don't bleed the old colours in.</summary>
        static void Dilate(Color32[] px, bool[] has, int W, int H, int passes)
        {
            var mask = (bool[])has.Clone();
            for (int pass = 0; pass < passes; pass++)
            {
                var add = new List<int>();
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int i = y * W + x;
                        if (mask[i]) continue;
                        int r = 0, g = 0, b = 0, a = 0, cnt = 0;
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int xx = x + dx, yy = y + dy;
                                if (xx < 0 || yy < 0 || xx >= W || yy >= H) continue;
                                int j = yy * W + xx;
                                if (!mask[j]) continue;
                                r += px[j].r; g += px[j].g; b += px[j].b; a += px[j].a; cnt++;
                            }
                        if (cnt == 0) continue;
                        px[i] = new Color32((byte)(r / cnt), (byte)(g / cnt), (byte)(b / cnt), (byte)(a / cnt));
                        add.Add(i);
                    }
                foreach (int i in add) mask[i] = true;
            }
        }

        // ---------- smooth value noise, 0..1 ----------

        static float Hash(int x, int y, int z)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xffffff) / (float)0xffffff;
            }
        }

        static float Noise(Vector3 p)
        {
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
            float fx = p.x - x, fy = p.y - y, fz = p.z - z;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy); fz = fz * fz * (3 - 2 * fz);
            float L(float a, float b, float t) => a + (b - a) * t;
            return L(L(L(Hash(x, y, z), Hash(x + 1, y, z), fx), L(Hash(x, y + 1, z), Hash(x + 1, y + 1, z), fx), fy),
                     L(L(Hash(x, y, z + 1), Hash(x + 1, y, z + 1), fx), L(Hash(x, y + 1, z + 1), Hash(x + 1, y + 1, z + 1), fx), fy), fz);
        }
    }
}
