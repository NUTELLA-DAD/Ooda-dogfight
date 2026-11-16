// The planning ghosts (TurnBattle.MakeHolo): a see-through glowing copy of an aircraft where the plan puts it.
// Clean, cartoon hologram: a faint fill, a soft glow that brightens toward the silhouette, a crisp bright rim line
// (anti-aliased with fwidth, so it never stair-steps), and a soft band of light that sweeps nose to tail.
// Two passes: the first lays the ghost's own depth and draws nothing, so the second lights only the surface nearest the
// camera. Without it every hidden part (the far wing, the engine, the cockpit insides) stacked its glow on top and the
// ghost came out blotchy, with hard bright seams wherever parts overlapped.
// Drawn after the other see-through things (Transparent+50), so its depth hides nothing that comes after it but itself.
// Premultiplied: it adds its light and covers a little of what is behind, so it reads on a bright sky as well as on the ground.
Shader "Ooda/Hologram"
{
    Properties
    {
        _BaseColor ("Colour (alpha: strength)", Color) = (0.25, 0.85, 1, 0.35)
        _Fill ("Fill", Range(0, 1)) = 0.3
        _GlowPower ("Glow falloff", Range(0.5, 8)) = 2.2
        _RimEdge ("Rim line at", Range(0.3, 0.98)) = 0.78
        _SweepSpeed ("Sweep speed", Float) = 0.45
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        // URP draws SRPDefaultUnlit before UniversalForward for each renderer.
        Pass
        {
            Name "GhostDepth"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            float4 vert(Attributes a) : SV_POSITION { return TransformObjectToHClip(a.positionOS.xyz); }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "GhostGlow"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Fill, _GlowPower, _RimEdge;
                float _SweepSpeed;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float along : TEXCOORD2;    // how far along the aircraft, nose to tail, in its own metres
            };

            Varyings vert(Attributes a)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(a.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(a.normalOS);
                // the aircraft's forward is its root's +z; parts are children, so measure along the world forward of the
                // object instead of its local z, and scale to the world so every part sweeps together
                float3 fwd = normalize(UNITY_MATRIX_M._m02_m12_m22);
                o.along = dot(o.positionWS, fwd);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetCameraPositionWS() - i.positionWS);
                half facing = saturate(abs(dot(n, v)));
                half rim = 1 - facing;

                half glow = pow(rim, _GlowPower);
                half w = max(fwidth(rim), 0.02);
                half line_ = smoothstep(_RimEdge - w, _RimEdge + w, rim);

                // a soft band of light running nose to tail every couple of seconds
                float s = frac(i.along * 0.06 - _Time.y * _SweepSpeed);
                half sweep = exp(-pow((s - 0.5) * 9, 2)) * 0.35;

                half a = _BaseColor.a;
                half strength = a * (_Fill + 1.35 * glow + sweep) + line_ * saturate(a * 2.2);
                strength = saturate(strength);
                half3 col = lerp(_BaseColor.rgb, 1, saturate(line_ * 0.55 + glow * 0.2));
                return half4(col * strength, saturate(strength * 0.55));
            }
            ENDHLSL
        }
    }
}
