// The haze round a cloud bank: a shell that is faint where you look through its middle and fades to nothing at its
// edges and as you fly into it, so the bank sits in a soft glow instead of hard against the sky.
Shader "OODA/Haze"
{
    Properties
    {
        _Color ("Colour and strength", Color) = (1, 1, 1, 0.3)
        _Power ("Edge softness", Float) = 1.5
        _NearFade ("Fade in over (m)", Float) = 300
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            Name "Haze"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Power;
                float _NearFade;
            CBUFFER_END

            struct Attributes { float4 pos : POSITION; float3 normal : NORMAL; };
            struct Varyings { float4 pos : SV_POSITION; float3 normal : TEXCOORD0; float3 world : TEXCOORD1; float fog : TEXCOORD2; };

            Varyings vert(Attributes a)
            {
                Varyings o;
                o.world = TransformObjectToWorld(a.pos.xyz);
                o.pos = TransformWorldToHClip(o.world);
                o.normal = TransformObjectToWorldNormal(a.normal);
                o.fog = ComputeFogFactor(o.pos.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 toCam = GetCameraPositionWS() - i.world;
                float facing = saturate(abs(dot(normalize(i.normal), normalize(toCam))));
                half4 c = _Color;
                c.a *= pow(facing, _Power) * saturate(length(toCam) / _NearFade);
                c.rgb = MixFog(c.rgb, i.fog);
                return c;
            }
            ENDHLSL
        }
    }
}
