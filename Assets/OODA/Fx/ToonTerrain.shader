// The ground in the toon look (ToonTerrain), in place of URP's Terrain Lit. All eight ground layers in one pass: the
// layers' weights come from the terrain's two splat maps (bound by the build as _Ctrl0 and _Ctrl1, so the terrain draws
// once, with no add pass), are nudged by a world-space noise and sharpened, so where meadow meets forest floor or rock
// the edge is a crisp, wobbly painted line instead of a 20 m blur; then the land's toon light (ToonLand.hlsl).
// It draws instanced (the terrain's drawInstanced): the patch's vertices are placed from the heightmap here.
Shader "Ooda/ToonTerrain"
{
    Properties
    {
        [NoScaleOffset] _Ctrl0 ("Splat 0-3", 2D) = "red" {}
        [NoScaleOffset] _Ctrl1 ("Splat 4-7", 2D) = "black" {}
        _Lay0 ("Layer 0", 2D) = "grey" {}
        _Lay1 ("Layer 1", 2D) = "grey" {}
        _Lay2 ("Layer 2", 2D) = "grey" {}
        _Lay3 ("Layer 3", 2D) = "grey" {}
        _Lay4 ("Layer 4", 2D) = "grey" {}
        _Lay5 ("Layer 5", 2D) = "grey" {}
        _Lay6 ("Layer 6", 2D) = "grey" {}
        _Lay7 ("Layer 7", 2D) = "grey" {}
        _Tile0123 ("Tile sizes, m (layers 0-3)", Vector) = (80, 80, 120, 120)
        _Tile4567 ("Tile sizes, m (layers 4-7)", Vector) = (480, 60, 480, 480)

        [Header(Edges between layers)]
        _Break ("Edge wobble", Range(0, 1)) = 0.35
        _BreakScale ("Wobble size, m", Range(5, 400)) = 60
        _Sharp ("Edge sharpness", Range(1, 16)) = 6
        _Macro ("Painted patch size, m", Range(50, 3000)) = 450

        [Header(Light)]
        _BandEdge ("Terminator (0 = at the horizon of the light)", Range(-1, 1)) = 0.05
        _BandSoft ("Terminator softness", Range(0.001, 0.5)) = 0.05
        _MidBand ("Turning-away step", Range(0, 0.5)) = 0.12
        _ShadeColor ("Shade tint", Color) = (0.56, 0.60, 0.78, 1)
        _LitTint ("Sunlit tint", Color) = (1.0, 0.97, 0.90, 1)
        _Ambient ("Sky light", Range(0, 1.5)) = 0.55
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry-100" "RenderPipeline" = "UniversalPipeline" "TerrainCompatible" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _Lay0_ST, _Lay1_ST, _Lay2_ST, _Lay3_ST, _Lay4_ST, _Lay5_ST, _Lay6_ST, _Lay7_ST;
            float4 _Tile0123, _Tile4567;
            half4 _ShadeColor, _LitTint;
            half _Break, _BreakScale, _Sharp, _Macro, _BandEdge, _BandSoft, _MidBand, _Ambient;
        CBUFFER_END

        // what the terrain sets for an instanced patch (as URP's own terrain shader reads them)
        #ifdef UNITY_INSTANCING_ENABLED
            TEXTURE2D(_TerrainHeightmapTexture);
            TEXTURE2D(_TerrainNormalmapTexture);
            float4 _TerrainHeightmapRecipSize;   // (1/width, 1/height, 1/(width-1), 1/(height-1))
            float4 _TerrainHeightmapScale;       // (scale.x, scale.y / max height, scale.z, 0)
        #endif
        UNITY_INSTANCING_BUFFER_START(Terrain)
            UNITY_DEFINE_INSTANCED_PROP(float4, _TerrainPatchInstanceData)   // (xBase, yBase, skipScale, ~)
        UNITY_INSTANCING_BUFFER_END(Terrain)

        struct TerrainAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };

        // an instanced patch's vertex: placed from the heightmap, its normal from the terrain's normal map
        void TerrainInstancing(inout float4 positionOS, inout float3 normal, inout float2 uv)
        {
        #ifdef UNITY_INSTANCING_ENABLED
            float2 patchVertex = positionOS.xy;
            float4 instanceData = UNITY_ACCESS_INSTANCED_PROP(Terrain, _TerrainPatchInstanceData);
            float2 sampleCoords = (patchVertex.xy + instanceData.xy) * instanceData.z;
            float height = _TerrainHeightmapTexture.Load(int3(sampleCoords, 0)).r;
            positionOS.xz = sampleCoords * _TerrainHeightmapScale.xz;
            positionOS.y = height * _TerrainHeightmapScale.y;
            normal = _TerrainNormalmapTexture.Load(int3(sampleCoords, 0)).rgb * 2 - 1;
            uv = sampleCoords * _TerrainHeightmapRecipSize.zw;
        #endif
        }
        ENDHLSL

        Pass
        {
            Name "ToonForward"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
            #include "ToonLand.hlsl"

            TEXTURE2D(_Ctrl0); TEXTURE2D(_Ctrl1); SAMPLER(sampler_Ctrl0);
            TEXTURE2D(_Lay0); TEXTURE2D(_Lay1); TEXTURE2D(_Lay2); TEXTURE2D(_Lay3);
            TEXTURE2D(_Lay4); TEXTURE2D(_Lay5); TEXTURE2D(_Lay6); TEXTURE2D(_Lay7);
            SAMPLER(sampler_Lay0);   // one repeating, anisotropic sampler for all eight

            struct Varyings
            {
                float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; half3 normalWS : TEXCOORD2;
                half fog : TEXCOORD3; UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(TerrainAttributes a)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                TerrainInstancing(a.positionOS, a.normalOS, a.uv);
                o.positionWS = TransformObjectToWorld(a.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(a.normalOS);
                o.uv = a.uv;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 c0 = SAMPLE_TEXTURE2D(_Ctrl0, sampler_Ctrl0, i.uv);
                half4 c1 = SAMPLE_TEXTURE2D(_Ctrl1, sampler_Ctrl0, i.uv);
                // each layer's weight nudged by its own phase of one noise, so the edges between them wobble
                float n = LandNoise2(i.positionWS.xz / _BreakScale);
                const float4 ph0 = float4(0.0, 0.618, 0.236, 0.854), ph1 = float4(0.472, 0.09, 0.708, 0.326);
                float4 w0 = saturate(c0 + _Break * 0.5 * sin(6.2832 * (n + ph0)));
                float4 w1 = saturate(c1 + _Break * 0.5 * sin(6.2832 * (n + ph1)));
                // keep only layers that are really there (a nudge must not grow a layer out of nothing)
                w0 *= step(0.004, c0); w1 *= step(0.004, c1);
                w0 = pow(w0, _Sharp); w1 = pow(w1, _Sharp);
                float sum = dot(w0, 1) + dot(w1, 1);
                if (sum < 1e-6) { w0 = c0; w1 = c1; sum = max(dot(c0, 1) + dot(c1, 1), 1e-4); }
                w0 /= sum; w1 /= sum;

                float2 p = i.positionWS.xz;
                half3 base = 0;
                base += w0.x * SAMPLE_TEXTURE2D(_Lay0, sampler_Lay0, p / _Tile0123.x).rgb;
                base += w0.y * SAMPLE_TEXTURE2D(_Lay1, sampler_Lay0, p / _Tile0123.y).rgb;
                base += w0.z * SAMPLE_TEXTURE2D(_Lay2, sampler_Lay0, p / _Tile0123.z).rgb;
                base += w0.w * SAMPLE_TEXTURE2D(_Lay3, sampler_Lay0, p / _Tile0123.w).rgb;
                base += w1.x * SAMPLE_TEXTURE2D(_Lay4, sampler_Lay0, p / _Tile4567.x).rgb;
                base += w1.y * SAMPLE_TEXTURE2D(_Lay5, sampler_Lay0, p / _Tile4567.y).rgb;
                base += w1.z * SAMPLE_TEXTURE2D(_Lay6, sampler_Lay0, p / _Tile4567.z).rgb;
                base += w1.w * SAMPLE_TEXTURE2D(_Lay7, sampler_Lay0, p / _Tile4567.w).rgb;

                // broad painted patches over everything, cool and darker to warm and lighter, so a flat valley floor
                // is not one sheet of green (the concept's meadows are mottled at this size)
                float macro = LandNoise2(p / _Macro);
                base *= lerp(half3(0.88, 0.91, 0.98), half3(1.07, 1.05, 0.95), smoothstep(0.25, 0.75, macro));

                half3 col = ToonLandLight(base, i.positionWS, normalize(i.normalWS));
                if (AnyIsNaN(col) || AnyIsInf(col)) col = base * 0.6;
                return half4(MixFog(col, i.fog), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            float4 vert(TerrainAttributes a) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(a);
                TerrainInstancing(a.positionOS, a.normalOS, a.uv);
                float3 ws = TransformObjectToWorld(a.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(a.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 toLight = normalize(_LightPosition - ws);
            #else
                float3 toLight = _LightDirection;
            #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(ws, n, toLight));
            #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
            #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return cs;
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
            float4 vert(TerrainAttributes a) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(a);
                TerrainInstancing(a.positionOS, a.normalOS, a.uv);
                return TransformObjectToHClip(a.positionOS.xyz);
            }
            half4 frag(float4 positionCS : SV_POSITION) : SV_Target { return positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; };
            Varyings vert(TerrainAttributes a)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(a);
                TerrainInstancing(a.positionOS, a.normalOS, a.uv);
                o.positionCS = TransformObjectToHClip(a.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(a.normalOS);
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return half4(normalize(i.normalWS), 0); }
            ENDHLSL
        }
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
