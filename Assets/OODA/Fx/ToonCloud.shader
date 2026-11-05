// The toon clouds (ToonSky.cs): the same look as the smoke in ToonFx, at the scale of a cloud bank. Each puff is a quad
// that the vertex shader turns to face the camera; the fragment cuts it into a round, lobed puff with a flat bottom
// (cumulus sit on a level base), cel-shaded in two tones (a lit tone shifted toward the sun over a cool shade tone) with a
// deeper belly band along the base. The ink line is a second, slightly larger copy of each puff pushed back from the
// camera, so the puffs of one bank cover each other's lines and only the outline of the mass is inked.
//
// Kinds of cloud (uv1.w), each with its own palette (globals set by ToonSky.cs) and its own life:
//   0 cumulus: the fair-weather heaps of the middle heights;
//   1 storm: dark towers with an anvil, lightning flashing in them now and then;
//   2 cirrus: high, thin, pink-shaded streaks, racing in the high wind;
//   3 low cloud: flat grey-lavender banks hugging the valleys;
//   4 rain: a shaft of slanting toon streaks under a storm (and the lightning bolt when it flashes).
// Every bank rides the wind across a stretch of sky: it swells out of nothing at one end, drifts, and dissolves at the
// other, its puffs coming and going a little out of step; every puff billows slowly as it goes.
// Opaque (cut out, depth written), so it sorts against the world and the smoke on its own.
Shader "Ooda/ToonCloud"
{
    Properties
    {
        _Push ("Line pushed back (radii)", Float) = 0.9
        _FogAmount ("How much the fog takes it", Range(0, 1)) = 0.7
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "DisableBatching" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _Push;
            float _FogAmount;
        CBUFFER_END

        float4 _ToonSunDir;
        float4 _ToonWind;       // xz: the wind's direction (unit); w: a time scale (1)
        float4 _KindLit[4], _KindShade[4], _KindBelly[4], _KindLine[4];
        float4 _KindDrift[4];   // per kind: speed (m/s), span (m), billow rate, billow size
        float4 _RainCol, _FlashCol;
        float _ToonTime;        // real seconds (ToonSky's clock: it runs while the battle's clock is stopped for planning)

        // the puff's centre in POSITION (all four corners the same);
        // uv0: corner (-1/+1, -1/+1), radius, seed (0-1); uv1: the base it is cut flat at, the bank's top, lobes, kind;
        // uv2: stretch across, the bank's phase (0-1), how far out from the bank's heart (0-1); uv3: the bank's heart
        struct Attributes { float4 positionOS : POSITION; float4 uv0 : TEXCOORD0; float4 uv1 : TEXCOORD1; float4 uv2 : TEXCOORD2; float3 uv3 : TEXCOORD3; };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float4 c : TEXCOORD0;   // corner in radii (xy), line width in radii (z), shape's turn (w)
            float4 w : TEXCOORD1;   // world position on the puff's face (xyz), fog (w)
            float4 d : TEXCOORD2;   // base y, top y, lobes, fade
            float4 e : TEXCOORD3;   // lit tone's offset (xy), radius (z), lightning (w)
            float4 k : TEXCOORD4;   // kind, stretch, metres a pixel, phase
        };

        float Hash(float x) { return frac(sin(x * 12.9898 + 4.1414) * 43758.5453); }

        Varyings Build(Attributes a, bool isLine)
        {
            Varyings o;
            float kind = a.uv1.w, stretch = a.uv2.x, phase = a.uv2.y, seed = a.uv0.w;
            bool rain = kind > 3.5;
            int ki = rain ? 1 : (int)min(kind, 3.0);   // (rain rides with its storm)
            float t = _ToonTime * _ToonWind.w;
            // the bank's drift across its span, and its life: forming, drifting, dissolving
            float4 drift = _KindDrift[ki];
            float u = frac(t * drift.x / drift.y + phase);
            float life = smoothstep(0.0, 0.14, u) * (1.0 - smoothstep(0.86, 1.0, u));
            float3 cw = TransformObjectToWorld(a.positionOS.xyz);
            float R = a.uv0.z;
            if (!rain)
            {
                // a forming bank is the whole cloud small, grown about its heart (its puffs still overlapping), its outer
                // puffs swelling in last; dissolving, the same backwards
                float g = lerp(0.2, 1.0, smoothstep(0.0, 1.0, life));
                float3 heart = TransformObjectToWorld(a.uv3);
                cw = heart + (cw - heart) * g;
                float lifeP = saturate((life * 1.6 - a.uv2.z * 0.6) * 1.5);
                R *= g * lerp(0.5, 1.0, lifeP) * step(0.02, life) * (1.0 + drift.w * sin(t * drift.z + seed * 40.0));
            }
            cw += float3(_ToonWind.x, 0, _ToonWind.z) * ((u - 0.5) * drift.y);
            float3 cv = TransformWorldToView(cw);
            float dist = max(-cv.z, 0.01);
            float mPerPx = dist / (abs(UNITY_MATRIX_P[1][1]) * 0.5 * _ScreenParams.y);
            // the line is at least ~1.7 pixels wide, and never thinner than 6% of the puff
            float lw = clamp(1.7 * mPerPx / max(R, 1e-3), 0.06, 0.35);
            float grow = isLine ? 1.0 + lw : 1.0;
            float2 corner = a.uv0.xy * grow;
            if (rain)
            {
                // a shaft stands upright, turned to the camera round the vertical
                float3 right = float3(UNITY_MATRIX_V[0].x, 0, UNITY_MATRIX_V[0].z);
                right /= max(length(right), 1e-4);
                o.w.xyz = cw + right * (corner.x * R * stretch) + float3(0, corner.y * R, 0);
                o.positionCS = isLine ? float4(0, 0, -1, 1) : TransformWorldToHClip(o.w.xyz);
            }
            else
            {
                float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz;
                o.w.xyz = cw + (right * corner.x * stretch + up * corner.y) * R;
                float3 pv = cv + float3(corner.x * stretch * R, corner.y * R, 0);
                if (isLine) pv *= (dist + _Push * R) / dist;   // (back along the view ray: same size on screen, behind the fills)
                o.positionCS = TransformWViewToHClip(pv);
            }
            o.w.w = ComputeFogFactor(o.positionCS.z);
            o.c = float4(corner, lw, seed * 6.2832);
            // the flat base only reads from the side: looking up at a bank from under it, the cut would slice every puff at
            // a different place on screen, so it (and the belly band) sinks away and the puffs stay round
            float under = smoothstep(0.25, 0.5, normalize(cw - GetCameraPositionWS()).y);
            float baseY = a.uv1.x - under * 4.0 * R;
            // fly into a puff and it thins away (dithered) rather than filling the screen; a rain shaft fades with its storm
            float fade = rain ? life : saturate((length(cv) - R * 0.6) / (R * 0.9));
            o.d = float4(baseY, a.uv1.y, a.uv1.z, fade);
            // the lit tone sits toward the sun, always a little up (light from above even with the sun behind you)
            float3 s = mul((float3x3)UNITY_MATRIX_V, _ToonSunDir.xyz);
            float2 off = s.xy + float2(0, 0.45);
            // lightning: a storm flickers twice, every eight seconds or so, each storm to its own beat
            float fl = frac(t * 0.12 + phase * 5.3);
            float flash = (kind > 0.5 && kind < 1.5) || rain ? (fl < 0.008 || abs(fl - 0.016) < 0.003 ? 1.0 : 0.0) : 0.0;
            o.e = float4(off * (0.21 / max(length(off), 1e-3)), R, flash * step(0.3, life));
            o.k = float4(kind, stretch, mPerPx, phase);
            return o;
        }

        // the puff's edge at this point, in radii: round bumps with cusps between them (ToonFx's CloudShape)
        float Lobe(float2 uv, float turn, float lobes)
        {
            float a = atan2(uv.y, uv.x) - turn;
            return 0.84 + 0.16 * pow(abs(sin(a * lobes * 0.5)), 0.55);
        }

        void Dither(float4 positionCS, float fade)
        {
            if (fade >= 1) return;
            static const float bayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
            uint2 p = (uint2)positionCS.xy % 4;
            if (fade * 16 <= bayer[p.y * 4 + p.x] + 0.5) discard;
        }

        half3 Fogged(half3 c, float fog) { return lerp(c, MixFog(c, fog), _FogAmount); }

        // a rain shaft: slanting dashes falling, ragged at its sides, thinning out toward the ground; and when the storm
        // flashes, a zigzag bolt down the middle
        half4 Rain(Varyings i)
        {
            float2 uv = i.c.xy;
            float R = i.e.z, W = R * i.k.y, wx = uv.x * W, wy = uv.y * R;
            float px = i.k.z;
            if (i.e.w > 0.5)
            {
                float y8 = (1 - uv.y) * 5.0, seg = floor(y8);
                float x0 = Hash(seg + i.k.w * 91.0) - 0.5, x1 = Hash(seg + 1 + i.k.w * 91.0) - 0.5;
                float xb = lerp(x0, x1, frac(y8)) * 0.7 * W;
                if (abs(wx - xb) < max(4.0, 2.2 * px) && uv.y > -0.4) return half4(Fogged(_FlashCol.rgb, i.w.w), 1);
            }
            float spacing = max(12.0, 5.0 * px), slant = wx + wy * 0.28;
            float col = floor(slant / spacing), f = frac(slant / spacing);
            float dash = frac((wy + _ToonTime * 60.0) / (spacing * 6.0) + Hash(col));
            if (f > 0.3 || dash > 0.72) discard;
            if (abs(uv.x) > 0.9 - 0.35 * Hash(col * 3.1)) discard;
            Dither(i.positionCS, i.d.w * saturate((uv.y + 1.0) / 0.9));
            return half4(Fogged(_RainCol.rgb, i.w.w), 1);
        }
        ENDHLSL

        // the ink line round the mass
        Pass
        {
            Name "CloudLine"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            Varyings vert(Attributes a) { return Build(a, true); }
            half4 frag(Varyings i) : SV_Target
            {
                if (i.k.x > 3.5) discard;
                float grow = 1 + i.c.z;
                if (length(i.c.xy) > Lobe(i.c.xy, i.c.w, i.d.z) * grow) discard;
                if (i.w.y < i.d.x - i.c.z * i.e.z) discard;   // (a line along the flat bottom too)
                Dither(i.positionCS, i.d.w);
                int k = (int)round(i.k.x);
                return half4(Fogged(_KindLine[k].rgb, i.w.w), 1);
            }
            ENDHLSL
        }

        // the puff: shade tone, the lit tone over it toward the sun, a belly band along the base
        Pass
        {
            Name "CloudFill"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            Varyings vert(Attributes a) { return Build(a, false); }
            half4 frag(Varyings i) : SV_Target
            {
                if (i.k.x > 3.5) return Rain(i);
                float2 uv = i.c.xy;
                if (length(uv) > Lobe(uv, i.c.w, i.d.z)) discard;
                if (i.w.y < i.d.x) discard;
                Dither(i.positionCS, i.d.w);
                int k = (int)round(i.k.x);
                half3 lit = _KindLit[k].rgb, shade = _KindShade[k].rgb, belly = _KindBelly[k].rgb;
                if (i.e.w > 0.5) { lit = _FlashCol.rgb; shade = lerp(shade, _FlashCol.rgb, 0.55); belly = lerp(belly, _FlashCol.rgb, 0.35); }
                float2 q = (uv - i.e.xy) / 0.8;
                half3 col = length(q) < Lobe(q, i.c.w, i.d.z) ? lit : shade;
                if (k != 2 && i.w.y < i.d.x + (i.d.y - i.d.x) * 0.14) col = belly;
                return half4(Fogged(col, i.w.w), 1);
            }
            ENDHLSL
        }
    }
}
