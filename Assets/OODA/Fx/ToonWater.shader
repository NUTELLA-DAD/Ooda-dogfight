// The water in the toon look (ToonWater): the lakes, the river and the sea. It reads the depth of the ground under it
// (the camera's depth texture), so the shore is drawn where the water really meets the ground: clear shallows that show
// the bed, a turquoise middle and a deep blue in flat bands, a white foam line along the shore that wobbles and laps, and
// a second line that comes and goes a little way out. The river runs: streaks of light and white flecks travel along its
// flow (the mesh's second UV: direction times speed), turning to white water where it drops fast. The still water
// glints in the sun from moving ripples. Shadows fall on it in the shade tint, like the land.
Shader "Ooda/ToonWater"
{
    Properties
    {
        _Shallow ("Shallow", Color) = (0.50, 0.90, 0.84, 1)
        _Mid ("Middle", Color) = (0.20, 0.75, 0.78, 1)
        _Deep ("Deep", Color) = (0.12, 0.54, 0.66, 1)
        _DeepDepth ("Deep from, m", Float) = 14
        _ShallowAlpha ("Clearness of the shallows", Range(0, 1)) = 0.35
        _Foam ("Foam", Color) = (0.97, 1.0, 1.0, 1)
        _FoamWidth ("Shore foam width, m of depth", Float) = 1.8
        _Ripple ("Ripple size, m", Float) = 14
        _FlowSpeed ("Flow speed, m/s at speed 1", Float) = 2.5
        [Toggle] _FlowOn ("Flows (off for the sea, whose mesh's UVs are not a flow)", Float) = 1
        _Streak ("Streak strength", Range(0, 1)) = 0.6
        _GlintStrength ("Sun glint", Range(0, 3)) = 1.2
        _ShadeColor ("Shade tint", Color) = (0.56, 0.60, 0.78, 1)

        // (so code that sets a Lit material's colour or texture finds somewhere to put it)
        [HideInInspector] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _BaseMap ("Base Map", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Water"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "LandNoise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _Shallow, _Mid, _Deep, _Foam, _ShadeColor, _BaseColor;
                float _DeepDepth, _ShallowAlpha, _FoamWidth, _Ripple, _FlowSpeed, _Streak, _GlintStrength, _FlowOn;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 flow : TEXCOORD1; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float2 flow : TEXCOORD1; float eye : TEXCOORD2;
                half fog : TEXCOORD3;
            };

            Varyings vert(Attributes a)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(a.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.eye = -TransformWorldToView(o.positionWS).z;
                o.flow = a.flow * _FlowOn;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // a ripple's height field, and the slope of it, for the glints
            float Ripples(float2 p, float t) { return LandNoise(p + float2(t * 0.11, t * 0.07)) + LandNoise(p * 1.9 - float2(t * 0.09, -t * 0.13)) * 0.5; }

            half4 frag(Varyings i) : SV_Target
            {
                float3 p = i.positionWS;
                float t = _Time.y;
                float3 V = normalize(GetWorldSpaceViewDir(p));

                // how deep the water is here: the ground behind it along the view, made vertical
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float ground = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float depth = max(ground - i.eye, 0) * max(abs(V.y), 0.08);

                // the flow: direction and speed (0 still, 1 a calm river, 3 rapids)
                float speed = length(i.flow);
                float2 dir = speed > 1e-3 ? i.flow / speed : float2(0.6, 0.8);
                float2 q = float2(dot(p.xz, dir), dot(p.xz, float2(-dir.y, dir.x)));   // along and across the flow
                float run = q.x - t * _FlowSpeed * max(speed, 0.15);

                // depth bands, their edges wobbling with the water's surface
                float wob = (LandNoise(p.xz / 9 + float2(run * 0.02, 0)) - 0.5) * 0.12;
                float d = depth / _DeepDepth + wob;
                half3 col = _Shallow.rgb;
                col = lerp(col, _Mid.rgb, smoothstep(0.16, 0.2, d));
                col = lerp(col, _Deep.rgb, smoothstep(0.55, 0.6, d));
                half alpha = lerp(_ShallowAlpha, 1, smoothstep(0.0, 0.3, d));

                // the river's streaks: long thin lights drawn along the flow, and white flecks where it runs fast
                half streak = 0;
                if (speed > 1e-3)
                {
                    float s1 = LandNoise(float2(run / 22, q.y / 2.2));
                    float s2 = LandNoise(float2((run * 1.3 + 50) / 9, q.y / 1.4 + 7));
                    streak = smoothstep(0.70, 0.73, s1) * _Streak;
                    half white = saturate((speed - 1.2) / 1.5);
                    streak = max(streak, smoothstep(0.62 - 0.22 * white, 0.66 - 0.22 * white, s2) * white);
                }

                // the shore: a foam line where the water gets thin, wobbling and lapping in and out, and a second line
                // that swells and fades a little way out
                float lap = 0.5 + 0.5 * sin(t * 0.9 + LandNoise(p.xz / 25) * 6.2832);
                float edge = _FoamWidth * (0.6 + 0.5 * LandNoise(p.xz / 6 + float2(run * 0.05, t * 0.15)) + 0.25 * lap);
                half foam = 1 - smoothstep(edge * 0.85, edge, depth);
                float ring = _FoamWidth * (2.2 + 1.2 * lap);
                foam = max(foam, (1 - smoothstep(0.12, 0.2, abs(depth - ring) / _FoamWidth)) * (1 - lap) * 0.8
                                  * step(0.45, LandNoise(p.xz / 4 + t * 0.1)));

                // light: the shade tint in shadow, and hard sun glints off the ripples
                Light sun = GetMainLight(TransformWorldToShadowCoord(p));
                half lit = smoothstep(0.35, 0.65, sun.shadowAttenuation * sun.distanceAttenuation);
                half3 light = lerp(_ShadeColor.rgb * 0.8, half3(1, 1, 1), lit) * saturate(dot(sun.color, half3(0.3, 0.59, 0.11)) / 1.3 + 0.15);
                float2 rp = p.xz / _Ripple + float2(run / _Ripple * 0.5, 0) * step(1e-3, speed);
                float h0 = Ripples(rp, t), hx = Ripples(rp + float2(0.05, 0), t), hz = Ripples(rp + float2(0, 0.05), t);
                half3 N = normalize(half3((h0 - hx) * 2.5, 1, (h0 - hz) * 2.5));
                half3 H = normalize(sun.direction + V);
                half glint = smoothstep(0.985, 0.99, dot(N, H)) * lit * _GlintStrength;

                col = col * light;
                col = lerp(col, _Foam.rgb * light, saturate(streak));
                col = lerp(col, _Foam.rgb * lerp(0.85, 1.0, lit), foam);
                col += glint * sun.color * 0.6;
                alpha = saturate(max(alpha, max(foam, streak)) + glint);

                if (AnyIsNaN(col) || AnyIsInf(col)) col = _Mid.rgb;
                return half4(MixFog(col, i.fog), alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
