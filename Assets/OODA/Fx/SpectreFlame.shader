// The spectre pilot's ghost fire (SpectreFlames builds the mesh): each quad is a billboard drawn round its centre here,
// so it faces whatever camera is drawing it. Three kinds, by uv1.y:
//  - a flame tongue (0): stands along its rise direction (the normal, its length the height) and turns about it to face
//    the camera; its shape is a teardrop eaten away by scrolling noise, white-hot at the core, cyan, deep blue at the rim;
//  - a halo (1): a round soft glow facing the camera, the bloom the game has no post effect for;
//  - a wisp of fog (2): a soft cloud of noise facing the camera that swells, drifts up its rise and fades, over and
//    over, each on its own clock, so a crowd of them reads as a glowing, churning fog.
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
                float4 tangentOS : TANGENT;     // x: how far nearer the camera to draw it, so where it crosses his head it
                                                // shows over the skin instead of being cut off hard by it
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
                float phase : TEXCOORD2;        // a wisp's age, 0..1
                half4 color : COLOR;
            };

            Varyings vert(Attributes a)
            {
                Varyings o;
                float3 c = TransformObjectToWorld(a.positionOS.xyz);
                float3 toCam = normalize(GetCameraPositionWS() - c);
                float3 p;
                o.phase = 0;
                if (a.uv1.y > 1.5)
                {
                    // a wisp: born at the bone, swelling as it drifts up its rise, gone at the top
                    float ph = frac(_Time.y * _Speed * 0.3 * a.uv2.y + a.uv2.x * 0.618);
                    o.phase = ph;
                    float3 right = normalize(UNITY_MATRIX_V[0].xyz), up = normalize(UNITY_MATRIX_V[1].xyz);
                    float r = a.uv1.x * (0.55 + 0.9 * ph);
                    p = c + a.normalOS * ph + (right * a.uv.x + up * (a.uv.y * 2 - 1)) * r;
                }
                else if (a.uv1.y > 0.5)
                {
                    float3 right = normalize(UNITY_MATRIX_V[0].xyz), up = normalize(UNITY_MATRIX_V[1].xyz);
                    p = c + (right * a.uv.x + up * (a.uv.y * 2 - 1)) * a.uv1.x;
                }
                else
                {
                    float3 rise = a.normalOS;
                    float3 axis = normalize(rise + float3(0, 1e-5, 0));   // a hidden one has no rise
                    float3 side = cross(axis, toCam);
                    side = dot(side, side) > 1e-6 ? normalize(side) : normalize(UNITY_MATRIX_V[0].xyz);
                    p = c + side * a.uv.x * a.uv1.x + rise * a.uv.y;
                }
                p += normalize(GetCameraPositionWS() - p) * a.tangentOS.x;   // along the view ray: the same place on screen
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
                if (i.info.x > 1.5)
                {
                    float2 q = float2(i.uv.x, i.uv.y * 2 - 1);
                    float d = length(q);
                    // churning: the noise is warped by more noise, and both roll up through the wisp
                    float2 w = float2(fbm(q * 1.7 + seed * 3.3 + float2(0, -t * 0.5)), fbm(q * 1.7 + seed * 1.9 + float2(4.2, -t * 0.45)));
                    float dens = fbm(q * 2.1 + w * 1.6 + seed * 5.1 + float2(0, -t * 0.7));
                    float fall = saturate(1 - d);
                    fall = fall * (2 - fall);                            // soft all the way out to the rim
                    float life = sin(i.phase * 3.14159);                 // fades in and out
                    float m = smoothstep(0.2, 0.7, dens * fall * 1.6) * life;
                    half3 col = lerp(_Rim.rgb, _Mid.rgb, saturate(dens * fall * 2.2));
                    col = lerp(col, _Core.rgb, smoothstep(0.45, 0.8, dens * fall * 1.7) * (1 - i.phase));
                    float a = m * i.color.a * 0.75;
                    return half4(col * a * 1.3, a * 0.55);
                }
                if (i.info.x > 0.5)
                {
                    float d = length(float2(i.uv.x, i.uv.y * 2 - 1));
                    float flick = 0.85 + 0.15 * sin(t * 7 + seed * 11) * sin(t * 3.1 + seed * 5);
                    float g = saturate(1 - d) * (exp(-d * d * 3.0) * 0.8 + 0.2) * flick;
                    half3 col = lerp(_Rim.rgb, _Mid.rgb, saturate(g * 1.8)) * i.color.rgb;
                    float a = g * i.color.a;
                    return half4(col * a * 1.2, a * 0.4);
                }
                float y = i.uv.y, x = i.uv.x;
                // the tongue sways and licks: the noise scrolls up it, bending it more toward the tip
                float n = fbm(float2(x * 1.3 + seed * 3.1, y * 2.2 - t * 2.4 + seed * 7.3));
                float sway = (fbm(float2(seed * 5.7, y * 1.2 - t * 1.3)) - 0.5) * 1.3 * y;
                float curlDir = frac(seed * 0.37) < 0.5 ? -1 : 1;     // each tongue's tip hooks over to one side, like the drawing
                sway += curlDir * y * y * y * 0.6 * (0.8 + 0.2 * sin(t * 1.7 + seed));
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
