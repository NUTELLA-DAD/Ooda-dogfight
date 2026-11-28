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

            // The far cloud line: a row of flat cumulus heads standing on the horizon, in banks with clear gaps between,
            // the way a painted sky closes its distance. All from the direction, nothing to tile: two rows of round heads
            // (the second half a step along, so they overlap), their heights from a hash that wraps once round the compass.
            float SkyHash(float i, float n) { i = fmod(i + n, n); return frac(sin(i * 127.1 + n * 3.7) * 43758.5453); }
            float SkyWave(float az, float n)   // smooth, 0..1, wrapping
            {
                float x = az * n, i = floor(x), f = frac(x);
                f = f * f * (3 - 2 * f);
                return lerp(SkyHash(i, n), SkyHash(i + 1, n), f);
            }
            float SkyHeads(float az, float n, float shift)
            {
                float x = az * n + shift, i = floor(x), f = frac(x) * 2 - 1;
                return sqrt(saturate(1 - f * f)) * lerp(0.35, 1.0, SkyHash(i, n + shift * 2));
            }
            // x: how much of the pixel is far cloud, y: 1 on its sunlit heads, 0 in its flat shaded base
            float2 FarClouds(float3 d)
            {
                float az = atan2(d.z, d.x) / 6.2831853 + 0.5;
                float bank = smoothstep(0.42, 0.62, SkyWave(az, 7) * 0.7 + SkyWave(az, 19) * 0.3);
                float heads = max(SkyHeads(az, 96, 0), SkyHeads(az, 64, 0.5) * 0.8);
                heads = max(heads, SkyHeads(az, 150, 0.25) * 0.55);
                float top = (0.010 + 0.034 * heads) * bank;
                float aa = max(fwidth(d.y), 1e-5);
                float inside = saturate((top - d.y) / aa + 0.5) * step(0.004, top);
                float lit = saturate((d.y - top * 0.42) / aa + 0.5);
                return float2(inside, lit);
            }

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
                // (it fades in over the first few degrees above the horizon: at the horizon itself the sky is exactly the fog's
                // colour, so the fogged ground and sea out at the far clip meet it with no step and no hard edge)
                float glow = toward * toward * toward * smoothstep(0.0, 0.12, h) * (1 - smoothstep(0.12, 0.38, h));
                c = lerp(c, _Glow.rgb, glow * 0.75);
                // the far cloud line, pale in the distance's air
                float2 fc = FarClouds(d);
                half3 farCol = lerp(half3(0.74, 0.80, 1.0), half3(1.0, 0.99, 0.97), fc.y);
                farCol = lerp(farCol, _Glow.rgb, glow * 0.5 + toward * toward * 0.12);   // (warm on the sun's side)
                farCol = lerp(farCol, _Horizon.rgb, lerp(0.38, 1.0, 1 - smoothstep(-0.035, 0.0, h)));
                c = lerp(c, farCol, fc.x);
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
