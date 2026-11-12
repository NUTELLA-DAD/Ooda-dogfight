// The mountain and grass hill models in the toon look (ToonLandModel). Their Hi3D textures have light and shadow painted
// in, which doubled up with the game's own light and smeared into grey streaks where a copy is stretched taller, so the
// colour here comes from the shape instead: snow on the faces that look up, more of them the higher they are (one snow
// line for every copy); grass on the gentle faces below the tree line; rock everywhere else, in warped horizontal strata
// like a painted cliff. Then the land's toon light (ToonLand.hlsl), so the models and the ground under them match.
// With _VertexColour on (the east lands' ground and monuments, Fx/EastlandScape) the colour is the mesh's own, painted
// per vertex, in two tones by patch; where its alpha is 1 (dressed stone) horizontal courses are cut every _Course metres.
Shader "Ooda/ToonLandModel"
{
    Properties
    {
        [Header(Colours)]
        _GrassA ("Grass", Color) = (0.42, 0.59, 0.25, 1)
        _GrassB ("Grass, second tone", Color) = (0.48, 0.65, 0.29, 1)
        _RockA ("Rock", Color) = (0.56, 0.52, 0.57, 1)
        _RockB ("Rock, second tone", Color) = (0.64, 0.58, 0.56, 1)
        _Snow ("Snow", Color) = (0.84, 0.87, 0.93, 1)
        _TexSnow ("Snow where the model's own texture is white", Range(0, 1)) = 1

        [Header(Where)]
        _SnowLow ("Snow starts, m", Float) = 1500
        _SnowHigh ("Snow on every upward face from, m", Float) = 2700
        _GrassSlope ("Grass on faces flatter than (normal y)", Range(0, 1)) = 0.78
        _GrassHigh ("Grass up to, m", Float) = 1800
        _Strata ("Rock strata thickness, m", Float) = 38
        _Patch ("Patch size, m", Float) = 90

        [Header(Painted per vertex)]
        [ToggleUI] _VertexColour ("Colour from the vertices", Float) = 0
        _Course ("Stone course height, m", Float) = 4

        [Header(Light)]
        _BandEdge ("Terminator (0 = at the horizon of the light)", Range(-1, 1)) = 0.05
        _BandSoft ("Terminator softness", Range(0.001, 0.5)) = 0.04
        _MidBand ("Turning-away step", Range(0, 0.5)) = 0.12
        _ShadeColor ("Shade tint", Color) = (0.56, 0.60, 0.78, 1)
        _LitTint ("Sunlit tint", Color) = (1.0, 0.95, 0.86, 1)
        _Ambient ("Sky light", Range(0, 1.5)) = 0.55

        // (so code that sets a Lit material's texture finds somewhere to put it)
        _BaseMap ("The model's texture (only its white is read, as snow)", 2D) = "black" {}
        [HideInInspector] _MainTex ("Base Map", 2D) = "white" {}
        [HideInInspector] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor, _GrassA, _GrassB, _RockA, _RockB, _Snow, _ShadeColor, _LitTint;
            float _SnowLow, _SnowHigh, _GrassSlope, _GrassHigh, _Strata, _Patch, _VertexColour, _Course;
            half _BandEdge, _BandSoft, _MidBand, _Ambient, _TexSnow;
        CBUFFER_END
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
            #include "ToonLand.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; half fog : TEXCOORD2;
                float2 uv : TEXCOORD3; half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes a)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(a.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(a.normalOS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                o.uv = TRANSFORM_TEX(a.uv, _BaseMap);
                o.color = a.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 N = normalize(i.normalWS);
                float3 p = i.positionWS;
                float n = LandNoise2(p.xz / _Patch);
                if (_VertexColour > 0.5)
                {
                    // the east lands: the painted colour (its patches are painted in, fading out at the Alps' edge); dressed stone in courses
                    half3 vc = i.color.rgb;
                    float course = frac(p.y / _Course);
                    vc *= lerp(1, 0.86, step(0.5, i.color.a) * step(course, 0.1));
                    half3 vlit = ToonLandLight(vc, p, N);
                    if (AnyIsNaN(vlit) || AnyIsInf(vlit)) vlit = vc * 0.6;
                    return half4(MixFog(vlit, i.fog), 1);
                }
                float up = N.y + (n - 0.5) * 0.18;

                // rock: two tones in strata, warped by the noise so the lines wander like a painted cliff
                float strata = frac(p.y / _Strata + n * 1.7 + LandNoise(p.xz / (_Patch * 4)) * 2.3);
                half3 col = lerp(_RockA.rgb, _RockB.rgb, step(0.55, strata));

                // grass on the gentle faces low down, in two tones by patch
                half grass = smoothstep(_GrassSlope - 0.04, _GrassSlope + 0.04, up) * (1 - smoothstep(_GrassHigh - 80, _GrassHigh + 80, p.y + (n - 0.5) * 160));
                col = lerp(col, lerp(_GrassA.rgb, _GrassB.rgb, step(0.5, n)), grass);

                // snow on the faces that look up: only the flattest at the snow line, nearly all of them high up
                float high = saturate((p.y + (n - 0.5) * 220 - _SnowLow) / max(_SnowHigh - _SnowLow, 1));
                float need = lerp(1.08, 0.36, high);
                half snow = smoothstep(need - 0.03, need + 0.03, up);
                // and where the Hi3D model has its own snow painted (its white: only the colour's floor is read, not
                // its painted light), so each peak keeps the artist's snowfields, drawn in the flat toon snow
                half3 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb;
                snow = max(snow, _TexSnow * smoothstep(0.58, 0.70, min(tex.r, min(tex.g, tex.b))));
                col = lerp(col, _Snow.rgb, snow);

                half3 lit = ToonLandLight(col, p, N);
                if (AnyIsNaN(lit) || AnyIsInf(lit)) lit = col * 0.6;
                return half4(MixFog(lit, i.fog), 1);
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
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 vert(Attributes a) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(a);
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
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 vert(Attributes a) : SV_POSITION { UNITY_SETUP_INSTANCE_ID(a); return TransformObjectToHClip(a.positionOS.xyz); }
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
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; };
            Varyings vert(Attributes a)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(a);
                o.positionCS = TransformObjectToHClip(a.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(a.normalOS);
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return half4(normalize(i.normalWS), 0); }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Lit"
}
