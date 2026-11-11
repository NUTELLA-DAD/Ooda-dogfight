// The ink on the land (Ink): a full-screen pass, drawn after the opaque things and before the transparent ones (URP's
// Full Screen Pass renderer feature, made by OODA > Build Test World), that draws a dark line wherever the depth jumps:
// a ridge against the sky or the valley behind it, a cliff edge, one peak in front of another. It works in 1/depth,
// which runs straight across any flat surface on screen, so a slope seen edge-on far away gets no line; and the line
// goes on the near side of the jump only. A small jump (a tree on the ground, a house in a field) is under the
// threshold, so the forest is not drawn as speckle; the houses get their own outline from the toon shader instead.
// Things that ink themselves (the toon aircraft and pilots, the clouds) mark the stencil, and get none here. The line
// thins and fades into the fog with distance, and fades out near the camera (the cockpit, the gauges' own cameras).
Shader "Ooda/Ink"
{
    Properties
    {
        _InkColor ("Ink", Color) = (0.15, 0.13, 0.26, 1)
        _InkWidth ("Width, pixels at 1080p", Range(0.5, 4)) = 1.4
        _InkJump ("Jump that draws a line (share of the distance)", Range(0.005, 0.3)) = 0.035
        _InkNear ("Starts at, m", Float) = 120
        _InkNearFull ("Full from, m", Float) = 350
        _InkFar ("Gone by, m", Float) = 16000
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "Ink"
            // skip what inks itself (bit 64: ToonLit, ToonCloud)
            Stencil { Ref 64 ReadMask 64 Comp NotEqual }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            half4 _InkColor;
            float _InkWidth, _InkJump, _InkNear, _InkNearFull, _InkFar;

            float InvDepth(float2 uv) { return 1.0 / LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams); }

            // how far the middle stands out in front of the line through its two neighbours, as a share of its 1/depth
            float Jump(float2 uv, float2 d, float c)
            {
                float a = InvDepth(uv - d), b = InvDepth(uv + d);
                return (c - 0.5 * (a + b)) / c;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float eye = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                if (eye > _InkFar) return 0;
                float c = 1.0 / eye;
                float2 px = _InkWidth * (_ScaledScreenParams.y / 1080.0) / _ScaledScreenParams.xy;
                float j = max(max(Jump(uv, float2(px.x, 0), c), Jump(uv, float2(0, px.y), c)),
                              max(Jump(uv, px * 0.7071, c), Jump(uv, float2(px.x, -px.y) * 0.7071, c)));
                half ink = saturate((j - _InkJump) / _InkJump);
                ink *= smoothstep(_InkNear, _InkNearFull, eye) * (1 - smoothstep(_InkFar * 0.4, _InkFar, eye));
                if (ink <= 0.002) return 0;
                half fog = ComputeFogIntensity(ComputeFogFactorZ0ToFar(eye));
                half3 col = lerp(unity_FogColor.rgb, _InkColor.rgb, fog);
                return half4(col, ink * _InkColor.a);
            }
            ENDHLSL
        }
    }
}
