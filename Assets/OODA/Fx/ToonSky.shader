// The toon sky (ToonSky.cs): a saturated gradient from a deep blue overhead through a bright azure to a pale cyan
// horizon, a warm glow on the horizon under the sun, and the sun itself as a flat disc in two flat rings, like the
// cel-shaded smoke and clouds. The colours and the sun's direction are set by ToonSky.cs.
Shader "Ooda/ToonSky"
{
    Properties
    {
        _Zenith ("Overhead", Color) = (0.106, 0.31, 0.85, 1)
        _High ("Upper sky", Color) = (0.18, 0.56, 0.95, 1)
        _Horizon ("Horizon", Color) = (0.62, 0.91, 1, 1)
        _Glow ("Horizon glow under the sun", Color) = (1, 0.84, 0.65, 1)
        _Ground ("Below the horizon", Color) = (0.45, 0.72, 0.86, 1)
        _Sun ("Sun", Color) = (1, 0.98, 0.9, 1)
        _SunRing ("Sun's rings", Color) = (1, 0.94, 0.69, 1)
        _SunSize ("Sun's radius (radians)", Float) = 0.035
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Zenith, _High, _Horizon, _Glow, _Ground, _Sun, _SunRing;
                float _SunSize;
            CBUFFER_END
            float4 _ToonSunDir;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes a)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(a.positionOS.xyz);
                o.dir = a.positionOS.xyz;
                return o;
            }

            // 1 inside edge, 0 outside, a pixel's blend between
            float Inside(float x, float edge) { float aa = max(fwidth(x), 1e-5); return saturate((edge - x) / aa + 0.5); }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 sun = normalize(_ToonSunDir.xyz + float3(0, 1e-4, 0));
                float h = d.y;
                float upness = saturate(h);
                // (two overlapping, smooth ramps: no seam where one hands over to the other)
                half3 c = lerp(_Horizon.rgb, _High.rgb, 1 - exp(-upness * 7.0));
                c = lerp(c, _Zenith.rgb, smoothstep(0.08, 1.0, upness));
                // (below the horizon it stays the horizon's colour, which is the fog's: the fogged sea out at the far clip
                // meets it with no seam)
                // the warm glow along the horizon on the sun's side
                float2 dx = d.xz / max(length(d.xz), 1e-4), sx = sun.xz / max(length(sun.xz), 1e-4);
                float toward = saturate(dot(dx, sx) * 0.5 + 0.5);
                float glow = toward * toward * toward * (1 - smoothstep(0.0, 0.2, abs(h)));
                c = lerp(c, _Glow.rgb, glow * 0.75);
                // the sun: a soft bloom, two flat rings and a hard disc
                float cosA = dot(d, sun), ang = acos(clamp(cosA, -1, 1));
                c += _SunRing.rgb * pow(saturate(cosA), 80) * 0.22;
                c = lerp(c, _SunRing.rgb, 0.22 * Inside(ang, _SunSize * 2.8));
                c = lerp(c, _SunRing.rgb, 0.42 * Inside(ang, _SunSize * 1.75));
                c = lerp(c, _Sun.rgb, Inside(ang, _SunSize));
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
