// The spectre pilot's ghost fire (SpectreFlames builds the mesh): each quad is a billboard drawn round its centre here,
// so it faces whatever camera is drawing it. Two kinds, by uv1.y:
//  - a flame tongue (0): stands along its rise direction (the normal, its length the height) and turns about it to face
//    the camera; its shape is a teardrop eaten away by scrolling noise, white-hot at the core, cyan, deep blue at the rim;
//  - a halo (1): a round soft glow facing the camera, the bloom the game has no post effect for.
// Premultiplied: adds its light and covers most of what is behind, so it reads against a bright sky too.
Shader "Ooda/SpectreFlame"
{
    Properties
    {
        _Core ("Core", Color) = (0.85, 1, 1, 1)
        _Mid ("Flame", Color) = (0.25, 0.85, 1, 1)
        _Rim ("Rim", Color) = (0.02, 0.2, 0.9, 1)
        _Speed ("Rise speed", Float) = 1.6
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Core, _Mid, _Rim;
                float _Speed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;   // the quad's centre-bottom (a flame) or centre (a halo), world space
                float3 normalOS : NORMAL;       // a flame's rise: its direction and height
                float2 uv : TEXCOORD0;          // the corner: x -1..1 across, y 0..1 up
                float2 uv1 : TEXCOORD1;         // x the half width, y the kind
                float2 uv2 : TEXCOORD2;         // x a seed, y its own speed
                half4 color : COLOR;            // a tint; alpha its strength
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 info : TEXCOORD1;        // kind, seed, speed
                half4 color : COLOR;
            };

            Varyings vert(Attributes a)
            {
                Varyings o;
                float3 c = TransformObjectToWorld(a.positionOS.xyz);
                float3 toCam = normalize(GetCameraPositionWS() - c);
                float3 p;
                if (a.uv1.y > 0.5)
                {
                    float3 right = normalize(UNITY_MATRIX_V[0].xyz), up = normalize(UNITY_MATRIX_V[1].xyz);
                    p = c + (right * a.uv.x + up * (a.uv.y * 2 - 1)) * a.uv1.x;
                }
                else
                {
                    float3 rise = a.normalOS;
                    float3 axis = normalize(rise);
                    float3 side = cross(axis, toCam);
                    side = dot(side, side) > 1e-6 ? normalize(side) : normalize(UNITY_MATRIX_V[0].xyz);
                    p = c + side * a.uv.x * a.uv1.x + rise * a.uv.y;
                }
                o.positionCS = TransformWorldToHClip(p);
                o.uv = a.uv;
                o.info = float3(a.uv1.y, a.uv2.x, a.uv2.y);
                o.color = a.color;
                return o;
            }

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
            }
            float fbm(float2 p) { return noise(p) * 0.55 + noise(p * 2.03 + 7.1) * 0.3 + noise(p * 4.1 + 3.7) * 0.15; }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y * _Speed * i.info.z;
                float seed = i.info.y;
                if (i.info.x > 0.5)
                {
                    float d = length(float2(i.uv.x, i.uv.y * 2 - 1));
                    float flick = 0.85 + 0.15 * sin(t * 7 + seed * 11) * sin(t * 3.1 + seed * 5);
                    float g = saturate(1 - d) * exp(-d * d * 3.5) * flick;
                    half3 col = lerp(_Rim.rgb, _Mid.rgb, saturate(g * 1.6)) * i.color.rgb;
                    float a = g * i.color.a;
                    return half4(col * a, a * 0.25);
                }
                float y = i.uv.y, x = i.uv.x;
                // the tongue sways and licks: the noise scrolls up it, bending it more toward the tip
                float n = fbm(float2(x * 1.3 + seed * 3.1, y * 2.2 - t * 2.4 + seed * 7.3));
                float sway = (fbm(float2(seed * 5.7, y * 1.2 - t * 1.3)) - 0.5) * 1.3 * y;
                float xs = x + sway + (n - 0.5) * 0.55 * y;
                // a teardrop: round at the base, tapering to the tip
                float halfW = sqrt(saturate(1 - y)) * smoothstep(0.0, 0.18, y + 0.06);
                float r = abs(xs) / max(halfW, 1e-3);
                float body = saturate(1 - r);
                float eat = n * (0.35 + 0.75 * y);                     // the noise eats the top away into licks
                float m = smoothstep(0.05, 0.45, body - eat + 0.25);
                float core = smoothstep(0.55, 1.0, body - eat * 0.7 + 0.2) * (1 - y * 0.8);
                half3 col = lerp(_Rim.rgb, _Mid.rgb, saturate(m * 1.4));
                col = lerp(col, _Core.rgb, core);
                col *= i.color.rgb;
                float a = m * i.color.a * (1 - smoothstep(0.75, 1.0, y));
                return half4(col * a * (1 + 0.6 * core), a * 0.8);
            }
            ENDHLSL
        }
    }
}
