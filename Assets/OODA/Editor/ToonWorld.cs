using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Ooda.EditorTools
{
    /// <summary>
    /// The world in the toon look (step 1 of the makeover): the ground on Fx/ToonTerrain.shader, the mountain and grass hill
    /// models on Fx/ToonLandModel.shader, both lit like the aircraft (Fx/ToonLand.hlsl), and the ink on ridges and cliff
    /// edges, a full-screen pass (Fx/Ink.shader) in the renderer. The palette is here, so the ground's layers and the
    /// models' colours stay one set: greens toward emerald, wheat toward honey, rock lavender in shade and warm in sun.
    /// The colours are what the ground is painted, before the light: the sun (1.3) brightens the lit side about 1.5 times.
    /// </summary>
    static class ToonWorld
    {
        public static readonly Color MeadowA = Hex("#5f9a45"), MeadowB = Hex("#6ea94f");
        public static readonly Color ForestA = Hex("#2c5639"), ForestB = Hex("#356142");
        public static readonly Color RockA = Hex("#7c7d8f"), RockB = Hex("#8d8b97");
        public static readonly Color SnowA = Hex("#d3dbea"), SnowB = Hex("#dfe5f1");
        public static readonly Color PastureA = Hex("#74a84a"), PastureB = Hex("#82b554");

        public static Shader Terrain => Shader.Find("Ooda/ToonTerrain");
        public static Shader LandModel => Shader.Find("Ooda/ToonLandModel");
        const string InkMatPath = "Assets/OODA/Settings/Ink.mat";

        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        /// <summary>The terrain's material on the toon ground: the splat maps and the eight layers bound to it directly, so
        /// it draws in one pass; past the base map distance it would switch to a baked base map, so that is pushed out.</summary>
        public static void BindTerrain(Terrain t, Material m)
        {
            var shader = Terrain;
            if (shader == null) { Debug.LogWarning("OODA: no Ooda/ToonTerrain shader; the ground keeps Terrain Lit."); return; }
            var data = t.terrainData;
            m.shader = shader;
            var ctrl = data.alphamapTextures;
            m.SetTexture("_Ctrl0", ctrl.Length > 0 ? ctrl[0] : null);
            m.SetTexture("_Ctrl1", ctrl.Length > 1 ? ctrl[1] : Texture2D.blackTexture);
            var layers = data.terrainLayers;
            var tiles = new float[8];
            for (int i = 0; i < 8; i++)
            {
                var l = i < layers.Length ? layers[i] : null;
                m.SetTexture($"_Lay{i}", l != null ? l.diffuseTexture : Texture2D.grayTexture);
                tiles[i] = l != null ? l.tileSize.x : 100f;
            }
            m.SetVector("_Tile0123", new Vector4(tiles[0], tiles[1], tiles[2], tiles[3]));
            m.SetVector("_Tile4567", new Vector4(tiles[4], tiles[5], tiles[6], tiles[7]));
            EditorUtility.SetDirty(m);
            t.materialTemplate = m;
            t.basemapDistance = 40000;
            // the layers want the same filtering as the crops: anisotropic, so the ground holds at a low angle
            foreach (var l in layers)
            {
                if (l == null || l.diffuseTexture == null) continue;
                var ti = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(l.diffuseTexture)) as TextureImporter;
                if (ti == null || (ti.anisoLevel >= 8 && ti.wrapMode == TextureWrapMode.Repeat)) continue;
                ti.anisoLevel = 8; ti.wrapMode = TextureWrapMode.Repeat; ti.SaveAndReimport();
            }
        }

        /// <summary>A mountain or grass hill model's material on the toon land shader: colour from the shape, not from its
        /// texture. The hills get no snow, and grass up steeper faces.</summary>
        public static void LandModelMaterial(Material m, bool mountain)
        {
            var shader = LandModel;
            if (shader == null) { Debug.LogWarning("OODA: no Ooda/ToonLandModel shader; the land models keep Lit."); return; }
            m.shader = shader;
            m.SetColor("_GrassA", MeadowA); m.SetColor("_GrassB", MeadowB);
            m.SetColor("_RockA", RockA); m.SetColor("_RockB", RockB);
            m.SetColor("_Snow", SnowA);
            m.SetFloat("_SnowLow", mountain ? 1500f : 99999f);
            m.SetFloat("_SnowHigh", mountain ? 2700f : 100000f);
            m.SetFloat("_TexSnow", mountain ? 1f : 0f);
            m.SetColor("_LitTint", new Color(1f, 0.98f, 0.94f));   // grey rock in the sun, not tan
            m.SetFloat("_GrassSlope", mountain ? 0.78f : 0.6f);
            m.SetFloat("_GrassHigh", mountain ? 1800f : 2600f);
            EditorUtility.SetDirty(m);
        }

        /// <summary>The buildings (chalets, church, viaduct stone) on the aircraft's toon shader, which inks their outline:
        /// the full-screen ink skips them (a house in a field is too small a depth jump), so they ink themselves.</summary>
        public static void Building(Material m)
        {
            var toon = TestWorldBuilder.Toon;
            if (toon == null || m == null) return;
            m.shader = toon;
            m.SetFloat("_OutlineWidth", 1.8f);
            m.SetFloat("_OutlineNear", 120f);    // full width out to 120 m, then thinning to a third
            m.SetFloat("_GlintStrength", 0.3f);
            m.SetFloat("_RimStrength", 0.25f);
            EditorUtility.SetDirty(m);
        }

        /// <summary>
        /// The ink on the land: URP's Full Screen Pass feature on the renderer, before the transparent things (so smoke and
        /// glass cover it), reading the depth and testing the stencil (what inks itself is marked there). The depth is
        /// copied after the opaque things for it. Turn the "Ink" feature off on OODA_Renderer to see the land without it.
        /// </summary>
        public static void EnsureInk(UniversalRenderPipelineAsset urp)
        {
            var shader = Shader.Find("Ooda/Ink");
            if (shader == null) { Debug.LogWarning("OODA: no Ooda/Ink shader; no ink on the land."); return; }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(InkMatPath);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, InkMatPath); }
            mat.shader = shader;

            var uso = new SerializedObject(urp);
            var list = uso.FindProperty("m_RendererDataList");
            var data = list != null && list.arraySize > 0 ? list.GetArrayElementAtIndex(0).objectReferenceValue as UniversalRendererData : null;
            if (data == null) { Debug.LogWarning("OODA: the URP asset has no Universal renderer; no ink on the land."); return; }

            var feat = data.rendererFeatures.OfType<FullScreenPassRendererFeature>().FirstOrDefault(f => f != null && f.name == "Ink");
            var so = new SerializedObject(data);
            if (feat == null)
            {
                feat = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
                feat.name = "Ink";
                AssetDatabase.AddObjectToAsset(feat, data);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feat, out string _, out long id);
                var features = so.FindProperty("m_RendererFeatures");
                var map = so.FindProperty("m_RendererFeatureMap");
                features.arraySize++;
                features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feat;
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = id;
            }
            var copy = so.FindProperty("m_CopyDepthMode");
            if (copy != null) copy.intValue = 0;   // after the opaque things, where the ink reads it
            so.ApplyModifiedPropertiesWithoutUndo();

            feat.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingTransparents;
            feat.fetchColorBuffer = false;          // it blends over the picture, it needn't read it
            feat.bindDepthStencilAttachment = true; // for the stencil test
            feat.requirements = ScriptableRenderPassInput.Depth;
            feat.passMaterial = mat;
            feat.passIndex = 0;
            feat.SetActive(true);
            EditorUtility.SetDirty(feat);
            EditorUtility.SetDirty(data);
            Debug.Log("OODA: ink on the land: the Ink full-screen pass is on the renderer (depth copied after opaques).");
        }
    }
}
