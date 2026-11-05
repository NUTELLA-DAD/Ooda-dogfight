// The toon clouds (ToonSky.cs): the same look as the smoke in ToonFx, at the scale of a cloud bank. Each puff is a quad
// that the vertex shader turns to face the camera; the fragment cuts it into a round, lobed puff with a flat bottom
// (cumulus sit on a level base), cel-shaded in two tones (a lit tone shifted toward the sun over a cool shade tone) with a
// deeper belly band along the base. The ink line is a second, slightly larger copy of each puff pushed back from the
// camera, so the puffs of one bank cover each other's lines and only the outline of the mass is inked.
// Opaque (cut out, depth written), so it sorts against the world and the smoke on its own. The colours are globals set by
// ToonSky.cs, so the whole sky's palette lives in one place.
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
        half4 _CloudLit, _CloudShade, _CloudBelly, _CloudLine;

        // the puff's centre in POSITION (all four corners the same); uv0: corner (-1/+1, -1/+1), radius, seed (0-1);
        // uv1: the base it is cut flat at, the bank's top, its lobes
        struct Attributes { float4 positionOS : POSITION; float4 uv0 : TEXCOORD0; float4 uv1 : TEXCOORD1; };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float4 c : TEXCOORD0;   // corner in radii (xy), line width in radii (z), shape's turn (w)
            float4 w : TEXCOORD1;   // world position on the puff's face (xyz), fog (w)
            float4 d : TEXCOORD2;   // base y, top y, lobes, near fade
            float4 e : TEXCOORD3;   // lit tone's offset (xy), radius (z)
        };

        Varyings Build(Attributes a, bool isLine)
        {
            Varyings o;
            float3 cw = TransformObjectToWorld(a.positionOS.xyz);
            float R = a.uv0.z;
            float3 cv = TransformWorldToView(cw);
            float dist = max(-cv.z, 0.01);
            // the line is at least ~1.7 pixels wide, and never thinner than 6% of the puff
            float pxR = R * abs(UNITY_MATRIX_P[1][1]) * 0.5 * _ScreenParams.y / dist;
            float lw = clamp(1.7 / max(pxR, 1.0), 0.06, 0.35);
            float grow = isLine ? 1.0 + lw : 1.0;
            float2 corner = a.uv0.xy * grow;
            float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz;
            o.w.xyz = cw + (right * corner.x + up * corner.y) * R;
            float3 pv = cv + float3(corner * R, 0);
            if (isLine) pv *= (dist + _Push * R) / dist;   // (back along the view ray: same size on screen, behind the fills)
            o.positionCS = TransformWViewToHClip(pv);
            o.w.w = ComputeFogFactor(o.positionCS.z);
            o.c = float4(corner, lw, a.uv0.w * 6.2832);
            // fly into a puff and it thins away (dithered) rather than filling the screen
            // the flat base only reads from the side: looking up at a bank from under it, the cut would slice every puff at
            // a different place on screen, so it (and the belly band) sinks away and the puffs stay round
            float under = smoothstep(0.25, 0.5, normalize(cw - GetCameraPositionWS()).y);
            float baseY = a.uv1.x - under * 4.0 * R;
            o.d = float4(baseY, a.uv1.y, a.uv1.z, saturate((length(cv) - R * 0.7) / (R * 0.9)));
            // the lit tone sits toward the sun, always a little up (light from above even with the sun behind you)
            float3 s = mul((float3x3)UNITY_MATRIX_V, _ToonSunDir.xyz);
            float2 off = s.xy + float2(0, 0.45);
            o.e = float4(off * (0.21 / max(length(off), 1e-3)), R, 0);
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
                float grow = 1 + i.c.z;
                if (length(i.c.xy) > Lobe(i.c.xy, i.c.w, i.d.z) * grow) discard;
                if (i.w.y < i.d.x - i.c.z * i.e.z) discard;   // (a line along the flat bottom too)
                Dither(i.positionCS, i.d.w);
                return half4(Fogged(_CloudLine.rgb, i.w.w), 1);
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
                float2 uv = i.c.xy;
                if (length(uv) > Lobe(uv, i.c.w, i.d.z)) discard;
                if (i.w.y < i.d.x) discard;
                Dither(i.positionCS, i.d.w);
                float2 q = (uv - i.e.xy) / 0.8;
                half3 col = length(q) < Lobe(q, i.c.w, i.d.z) ? _CloudLit.rgb : _CloudShade.rgb;
                if (i.w.y < i.d.x + (i.d.y - i.d.x) * 0.14) col = _CloudBelly.rgb;
                return half4(Fogged(col, i.w.w), 1);
            }
            ENDHLSL
        }
    }
}
