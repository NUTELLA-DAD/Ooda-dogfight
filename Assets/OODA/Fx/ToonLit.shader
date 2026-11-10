// The toon look for characters and aircraft (ToonLit): two-tone light with a soft terminator, a cool shade, a warm rim
// on the sunlit edge, a hard sun glint, and an inked outline. Polished metal reflects a drawn horizon: bright sky above,
// dark ground below, a crisp line between, blended with the scene's real reflection.
// It answers to the same property names as URP's Lit (_BaseMap, _BaseColor, _MetallicGlossMap, _Metallic, _Smoothness,
// _EmissionColor), so everything in the game that tints, heats or frosts a Lit material works on it unchanged.
// The outline is the same mesh drawn again inside out and pushed along its normals by a width in pixels, so it wants
// smooth normals.
Shader "Ooda/ToonLit"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _MetallicGlossMap ("Metallic (R) Smoothness (A)", 2D) = "white" {}
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.5
        [HDR] _EmissionColor ("Emission", Color) = (0, 0, 0, 1)
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5

        [Header(Light)]
        _BandEdge ("Terminator (0 = at the horizon of the light)", Range(-1, 1)) = 0.05
        _BandSoft ("Terminator softness", Range(0.001, 0.5)) = 0.06
        _ShadeColor ("Shade tint", Color) = (0.56, 0.60, 0.78, 1)
        _Ambient ("Sky light", Range(0, 1.5)) = 0.55
        _RimColor ("Rim light", Color) = (1.0, 0.93, 0.80, 1)
        _RimEdge ("Rim width", Range(0, 1)) = 0.62
        _RimStrength ("Rim strength", Range(0, 2)) = 0.45
        _GlintSize ("Sun glint size", Range(0, 1)) = 0.5
        _GlintStrength ("Sun glint strength", Range(0, 4)) = 1.0

        [Header(Polished metal)]
        _ChromeToon ("Drawn horizon in the metal", Range(0, 1)) = 0.7
        _ChromeSky ("Metal: sky", Color) = (0.80, 0.90, 1.0, 1)
        _ChromeHorizon ("Metal: horizon", Color) = (1.0, 1.0, 1.0, 1)
        _ChromeGround ("Metal: ground", Color) = (0.22, 0.27, 0.34, 1)

        [Header(Ink)]
        _OutlineColor ("Outline", Color) = (0.07, 0.07, 0.10, 1)
        _OutlineWidth ("Outline width, pixels", Range(0, 8)) = 2.2
        _OutlineNear ("Full width within, metres", Range(1, 400)) = 40
        _OutlineMinDepth ("No outline nearer than, metres", Range(0, 3)) = 0.8

        [HideInInspector] _Surface ("__surface", Float) = 0
        [HideInInspector] _Blend ("__blend", Float) = 0
        [HideInInspector] _Cull ("__cull", Float) = 2
        [HideInInspector] _SrcBlend ("__src", Float) = 1
        [HideInInspector] _DstBlend ("__dst", Float) = 0
        [HideInInspector] _ZWrite ("__zw", Float) = 1
        [HideInInspector] _MainTex ("BaseMap", 2D) = "white" {}
        [HideInInspector] _Color ("Base Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _SmoothnessTextureChannel ("Smoothness texture channel", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        // One set of material constants for every pass (so the SRP batcher takes the shader, and no pass reads another's layout).
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor, _EmissionColor, _ShadeColor, _RimColor, _ChromeSky, _ChromeHorizon, _ChromeGround, _OutlineColor;
            half _Metallic, _Smoothness, _Cutoff, _BandEdge, _BandSoft, _Ambient, _RimEdge, _RimStrength, _GlintSize, _GlintStrength, _ChromeToon;
            half _OutlineWidth, _OutlineNear, _OutlineMinDepth;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ToonForward"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma shader_feature_local _METALLICSPECGLOSSMAP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_MetallicGlossMap); SAMPLER(sampler_MetallicGlossMap);

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; half3 normalWS : TEXCOORD2;
                half fog : TEXCOORD3; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes a)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(a); UNITY_TRANSFER_INSTANCE_ID(a, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(a.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(a.normalOS);
                o.uv = TRANSFORM_TEX(a.uv, _BaseMap);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 base = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
            #ifdef _METALLICSPECGLOSSMAP
                half4 ms = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, i.uv);
                half metal = ms.r, smooth = ms.a * _Smoothness;
            #else
                half metal = _Metallic, smooth = _Smoothness;
            #endif
                half3 N = normalize(i.normalWS);
                half3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                sun.color = clamp(sun.color, 0, 16);     // a first frame can arrive before the lighting is set up
                half3 L = sun.direction;
                half shadow = sun.shadowAttenuation * sun.distanceAttenuation;
                half ndl = dot(N, L), ndv = saturate(dot(N, V));

                // two tones: lit, and a cool shade
                half lit = smoothstep(_BandEdge - _BandSoft, _BandEdge + _BandSoft, ndl) * smoothstep(0.35, 0.65, shadow);
                half3 sky = clamp(SampleSH(N), 0, 8) * _Ambient;
                half3 light = lerp(_ShadeColor.rgb * (sun.color * 0.55 + sky), sun.color + sky * 0.35, lit);
                half3 col = base.rgb * (1 - metal) * light;

                // polished metal: the scene's reflection, and a drawn horizon over it
                half3 R = reflect(-V, N);
                half rough = 1 - smooth;
                // the same call Lit makes: the object's own reflection probe (blended, box-projected), not the global sky cube
                half3 env = clamp(GlossyEnvironmentReflection(R, i.positionWS, rough, 1.0h, GetNormalizedScreenSpaceUV(i.positionCS)), 0, 16);
                half up = R.y + (rough * rough) * 0.0;
                half w = max(0.012, rough * 0.6);
                half3 drawn = lerp(_ChromeGround.rgb, _ChromeHorizon.rgb, smoothstep(-w, w, up));
                drawn = lerp(drawn, _ChromeSky.rgb, smoothstep(0.02, 0.75, up));
                drawn *= lerp(0.75, 1.0, lit) * (sun.color * 0.6 + sky * 0.8);
                half3 f0 = lerp(half3(0.04, 0.04, 0.04), base.rgb, metal);
                half fres = pow(1 - ndv, 5);
                half3 spec = lerp(env, drawn, _ChromeToon * metal) * lerp(f0, half3(1, 1, 1), fres * smooth * 0.6);
                col += spec * lerp(smooth * 0.35, 1.0, metal);

                // the sun's glint, hard-edged
                half3 H = normalize(L + V);
                half ndh = saturate(dot(N, H));
                half edge = 1 - _GlintSize * 0.02 * (1.2 - smooth);
                half glint = smoothstep(edge, edge + 0.004, ndh) * lit * smooth * smooth;
                col += glint * _GlintStrength * sun.color * lerp(half3(0.25, 0.25, 0.25), f0 + 0.3, metal);

                // a rim of light on the sunlit edge
                half rim = smoothstep(_RimEdge, _RimEdge + 0.06, 1 - ndv) * saturate(ndl * 0.5 + 0.5) * lit;
                col += rim * _RimStrength * _RimColor.rgb * sun.color * lerp(base.rgb + 0.15, half3(1, 1, 1), metal * 0.5);

                if (AnyIsNaN(col) || AnyIsInf(col)) col = base.rgb * 0.6;
                col += _EmissionColor.rgb;
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ToonOutline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"


            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; half fog : TEXCOORD0; float depth : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes a)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 ws = TransformObjectToWorld(a.positionOS.xyz);
                // set the shell a little behind the surface, so it shows at the silhouette and not in every small dent
                float3 away = ws - GetCameraPositionWS();
                float dist = length(away);
                ws += away / max(dist, 1e-4) * min(0.12, dist * 0.004);
                o.positionCS = TransformWorldToHClip(ws);
                float3 nCS = TransformWorldToHClipDir(TransformObjectToWorldNormal(a.normalOS));
                float2 dir = nCS.xy; float len = length(dir);
                dir = len > 1e-5 ? dir / len : float2(0, 0);
                // a width in pixels, thinning with distance so a far aircraft is not all ink
                float px = _OutlineWidth * clamp(_OutlineNear / max(o.positionCS.w, 0.01), 0.3, 1.0);
                o.positionCS.xy += dir * px * 2.0 / _ScreenParams.xy * o.positionCS.w;
                o.fog = ComputeFogFactor(o.positionCS.z);
                o.depth = o.positionCS.w;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                clip(_OutlineWidth - 0.01);
                // no ink within arm's reach of the lens: from the pilot's seat the shell of his own head and shoulders
                // would be seen from inside, as a black wall
                clip(i.depth - _OutlineMinDepth);
                return half4(MixFog(_OutlineColor.rgb, i.fog), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes a)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(a);
                float3 ws = TransformObjectToWorld(a.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(a.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 toLight = normalize(_LightPosition - ws);
            #else
                float3 toLight = _LightDirection;
            #endif
                o.positionCS = TransformWorldToHClip(ApplyShadowBias(ws, n, toLight));
            #if UNITY_REVERSED_Z
                o.positionCS.z = min(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                o.positionCS.z = max(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings vert(Attributes a)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(a.positionOS.xyz);
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings vert(Attributes a)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
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
