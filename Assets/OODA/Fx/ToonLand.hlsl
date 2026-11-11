// The toon light for the land (ToonTerrain, ToonLand): the planes' two tones (ToonLit) with a third, softer band in the
// lit side, so a hill reads as lit, turning and shaded like a painted one. The shade takes the same cool tint as the
// aircraft, so the planes sit in the world instead of on top of it. The sunlit side is warmed a little (the concept's
// peach rock and honey fields), and the cast shadows land in the shade tone, banded, not as soft grey.
// Include after the material's CBUFFER, which must declare _ShadeColor, _LitTint, _Ambient, _BandEdge, _BandSoft, _MidBand.
#ifndef OODA_TOON_LAND_INCLUDED
#define OODA_TOON_LAND_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// value noise, 0..1, smooth; cheap enough per pixel for the large shapes it breaks up
float LandHash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float LandNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3 - 2 * f);
    float a = LandHash(i), b = LandHash(i + float2(1, 0)), c = LandHash(i + float2(0, 1)), d = LandHash(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// two octaves: the broad patches and the finer clumps inside them
float LandNoise2(float2 p) { return LandNoise(p) * 0.65 + LandNoise(p * 4.1 + 17.3) * 0.35; }

half3 ToonLandLight(half3 base, float3 positionWS, half3 N)
{
    Light sun = GetMainLight(TransformWorldToShadowCoord(positionWS));
    sun.color = clamp(sun.color, 0, 16);     // a first frame can arrive before the lighting is set up
    half shadow = sun.shadowAttenuation * sun.distanceAttenuation;
    half ndl = dot(N, sun.direction);

    // lit or shade, with a narrow soft edge; a cast shadow is shade too
    half lit = smoothstep(_BandEdge - _BandSoft, _BandEdge + _BandSoft, ndl) * smoothstep(0.35, 0.65, shadow);
    // inside the lit side, a step down where the slope turns away from the sun
    half full = smoothstep(_BandEdge + 0.38 - _BandSoft, _BandEdge + 0.38 + _BandSoft, ndl);
    half3 sky = clamp(SampleSH(N), 0, 8) * _Ambient;
    half3 litCol = (sun.color * _LitTint.rgb + sky * 0.35) * lerp(1 - _MidBand, 1, full);
    half3 shadeCol = _ShadeColor.rgb * (sun.color * 0.55 + sky);
    return base * lerp(shadeCol, litCol, lit);
}

#endif
