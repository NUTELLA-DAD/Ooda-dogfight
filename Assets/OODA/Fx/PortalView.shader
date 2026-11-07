// A portal's window (see Turn/PortalRings.cs): the picture from the camera at the other end, read at this pixel's place on
// the screen, so the disc shows what lies past the other ring as if you looked through it. Round its edge a swirl of the
// rim's colour winds in; _Open grows the window from the middle as the pair opens; without a live picture (_Live 0) it is
// all swirl. Before the other see-through things, and writing depth, so lines and clouds behind it don't show over it.
Shader "Ooda/PortalView"
{
    Properties
    {
        _MainTex ("View", 2D) = "black" {}
        _Tint ("Rim", Color) = (0.36, 1, 0.48, 1)
        _Open ("Open", Float) = 1
        _Live ("Live", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Tint;
                half _Open, _Live;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes a)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(a.positionOS.xyz);
                o.uv = a.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 d = i.uv * 2 - 1;
                float r = length(d);
                float open = saturate(_Open);
                clip(open - r);
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                half3 view = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, suv).rgb;
                float a = atan2(d.y, d.x), t = _Time.y;
                float rn = r / max(open, 1e-3);
                // two sets of arms winding in, turning
                float swirl = 0.5 + 0.5 * sin(a * 5 + rn * 13 - t * 3.2);
                float swirl2 = 0.5 + 0.5 * sin(-a * 3 + rn * 7 - t * 1.7);
                float edge = smoothstep(0.62, 1.0, rn);
                half3 rim = _Tint.rgb;
                half3 col;
                if (_Live > 0.5)
                {
                    float band = saturate(edge * (0.45 + 0.55 * swirl));
                    col = lerp(view, rim, band);
                    col += rim * 0.25 * smoothstep(0.93, 1.0, rn);   // a bright lip
                }
                else
                {
                    // no picture: a dark heart and the arms in the rim's colour
                    half3 deep = rim * 0.18;
                    col = lerp(deep, rim, saturate(0.25 + 0.5 * swirl * swirl2 + 0.5 * edge));
                }
                half alpha = lerp(0.98, 1.0, edge);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
