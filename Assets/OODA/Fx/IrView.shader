// The IR (thermal) view: the scene as a heat-seeker's camera sees it, white-hot on a dark, cold sky. Drawn as one
// triangle pair over the whole screen (the mesh's x, y are clip space), from the camera's opaque texture (the camera
// asks for it while the view is on): grey by "warmth" (the sky and the blue in things read cold, the earth a dull grey),
// dimmed so the heat glows drawn after it stand out, with a vignette and faint scan lines. Before the transparent queue,
// so the clouds, the lines and the effects still draw over it.
Shader "Ooda/IrView"
{
    Properties
    {
        _Tint ("Tint", Color) = (0.92, 1, 0.94, 1)
        _Low ("Coldest", Float) = 0.05
        _High ("Warmest background", Float) = 0.42
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half _Low, _High;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes a)
            {
                Varyings o;
                o.positionCS = float4(a.positionOS.xy, UNITY_NEAR_CLIP_VALUE, 1);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(i.positionCS);
                half3 c = SampleSceneColor(uv);
                half warm = saturate(c.r * 0.9h + c.g * 0.55h - c.b * 0.55h + 0.15h);
                half v = lerp(_Low, _High, warm);
                float2 d = uv - 0.5;
                v *= 1.0h - (half)dot(d, d) * 1.2h;
                v *= 0.94h + 0.06h * (half)sin(i.positionCS.y * 3.14159);
                return half4(v * _Tint.rgb, 1);
            }
            ENDHLSL
        }
    }
}
